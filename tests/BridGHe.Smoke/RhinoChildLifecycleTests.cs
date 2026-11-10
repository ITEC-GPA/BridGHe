using System;

internal static class RhinoChildLifecycleTests
{
    internal static void Run()
    {
        const string mode = "--rhino-child-probe";
        foreach (var sample in new[] { (0, ""), (0, "RHINO CHILD COMPLETE: --gpc-guard 0\n"),
            (0, "RHINO CHILD COMPLETE: --rhino-child-probe 1\n") })
        {
            bool rejected = false;
            try { SmokeChildProcess.ValidateCompletion(mode, sample.Item1, sample.Item2, ""); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "zero exit alone, wrong mode and inconsistent marker must not count as success");
        }
        var failed = SmokeChildProcess.Run(mode, "fail");
        Require(failed.ExitCode == 1 && failed.Error.Contains("Intentional Rhino child failure"),
            "child exceptions retain exit 1 and their diagnostic; parent survives");
        bool incompleteRejected = false;
        try { SmokeChildProcess.Run(mode, "incomplete"); }
        catch (InvalidOperationException ex) { incompleteRejected = ex.Message.Contains("incomplete") && ex.Message.Contains("exit 0"); }
        Require(incompleteRejected, "a real child exiting zero without completion is rejected; parent survives");
        var passed = SmokeChildProcess.Run(mode, "pass");
        Require(passed.ExitCode == 0, "parent can start and collect a successful Rhino child after a failure");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS CHILD LIFECYCLE: " + message);
    }
}
