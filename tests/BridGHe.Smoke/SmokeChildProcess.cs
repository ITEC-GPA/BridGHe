using System;
using System.Diagnostics;
using Rhino.Runtime.InProcess;

/// <summary>Protocol and lifetime of isolated Rhino test hosts, not the plugin runtime.</summary>
internal static class SmokeChildProcess
{
    internal static bool IsChildMode(string mode) => mode is
        "--gpc-guard" or "--plugin-check" or "--plugin-check-load" or "--rhino-child-probe";

    private static string Completion(string mode, int code) => $"RHINO CHILD COMPLETE: {mode} {code}";

    internal static (int ExitCode, string Output, string Error) Run(string mode, string argument)
    {
        if (!IsChildMode(mode)) throw new ArgumentException("Unknown child mode: " + mode);
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
        start.ArgumentList.Add(typeof(SmokeChildProcess).Assembly.Location);
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(argument);
        using var process = Process.Start(start);
        SmokeTrace.Write($"CHILD START {mode} PID {process.Id}");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(300000))
        {
            process.Kill(true);
            throw new TimeoutException("Rhino child timed out: " + mode);
        }
        string text = output.GetAwaiter().GetResult(), errors = error.GetAwaiter().GetResult();
        SmokeTrace.Write($"CHILD EXIT {mode} {process.ExitCode}");
        ValidateCompletion(mode, process.ExitCode, text, errors);
        return (process.ExitCode, text, errors);
    }

    internal static void ValidateCompletion(string mode, int exitCode, string output, string error)
    {
        if (!Array.Exists(output.Split('\n'), line => line.TrimEnd('\r') == Completion(mode, exitCode)))
            throw new InvalidOperationException($"Rhino child incomplete: {mode}, exit {exitCode}\n{output}\n{error}");
    }

    internal static int RunHost(string mode, string argument)
    {
        RhinoCore host = null;
        int code;
        try
        {
            SmokeTrace.Write("CHILD RHINO INIT " + mode);
            host = new RhinoCore(new[] { "/nosplash" }, WindowStyle.Hidden);
            SmokeTrace.Write("CHILD RHINO READY " + mode);
            code = mode switch
            {
                "--gpc-guard" => GpcReleaseSetTests.Guard(argument),
                "--plugin-check" => PluginBuildCheckTests.Child(argument),
                "--plugin-check-load" => PluginBuildCheckTests.LoadChild(argument),
                "--rhino-child-probe" => Probe(argument),
                _ => throw new ArgumentException("Unknown child mode: " + mode)
            };
        }
        catch (Exception ex)
        {
            SmokeTrace.Write("CHILD ERROR " + ex);
            Console.Error.WriteLine(ex);
            code = 1;
        }
        Console.WriteLine(Completion(mode, code));
        Console.Out.Flush();
        Console.Error.Flush();
        SmokeTrace.Write("CHILD MANAGED EXIT " + code);
        // Rhino 8.36: disposing a nested RhinoCore after these tests terminates
        // both child and waiting parent with exit 0. The dedicated child ends
        // here instead: all checks have run, errors and completion are flushed,
        // and process exit releases its resources. Never apply this to the parent
        // or to a host intended to continue executing after a test.
        GC.KeepAlive(host);
        Environment.Exit(code);
        return code; // Environment.Exit does not return.
    }

    private static int Probe(string action)
    {
        if (action == "fail") throw new InvalidOperationException("Intentional Rhino child failure");
        if (action == "incomplete") Environment.Exit(0); // Deliberately no completion marker.
        if (action != "pass") throw new ArgumentException("Unknown lifecycle probe: " + action);
        return 0;
    }
}
