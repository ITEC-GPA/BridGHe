using System;
using System.IO;

/// <summary>Optional per-process trace, surviving a native exit of the parent host.</summary>
internal static class SmokeTrace
{
    internal static void Write(string message)
    {
        string folder = Environment.GetEnvironmentVariable("BRIDGHE_SMOKE_TRACE_DIRECTORY");
        if (string.IsNullOrEmpty(folder)) return;
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, Environment.ProcessId + ".log"),
            DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
    }
}
