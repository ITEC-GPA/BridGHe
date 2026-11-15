using System;
using System.IO;

/// <summary>The owned replay output directory as text in the frozen P2R capture, independent of the process TEMP.</summary>
internal static class ReplayTemporaryPaths
{
    // The three frozen gpc-only-models digests contain this historical spelling. The capture stores hashes, not raw
    // strings: translate only our output directory back to that spelling BEFORE hashing; never rewrite the captured hashes.
    // This is capture metadata, not a filesystem destination. No file is read or written at this path.
    internal const string CapturedDirectory = @"C:\Users\g.pacini\AppData\Local\Temp\bridghe-gpc-only-models\";

    internal static string Normalize(string line, string temporaryDirectory)
    {
        string prefix = "GH_String " + Path.Combine(temporaryDirectory, "bridghe-gpc-only-models") + Path.DirectorySeparatorChar;
        return line.StartsWith(prefix, StringComparison.Ordinal)
            ? "GH_String " + CapturedDirectory + line.Substring(prefix.Length)
            : line;
    }

    internal static void Regression()
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception("REPLAY TEMP: " + label); checks++; }
        string root = Path.Combine(Path.GetFullPath("artifacts"), "temp text only");
        string normal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp");
        // Neither the normal profile path nor its short-name spelling is accessed: these are strings only.
        string shortNormal = Environment.GetEnvironmentVariable("BRIDGHE_REPLAY_NORMAL_TEMP_TEXT") ?? normal;
        foreach (string temp in new[] { normal, shortNormal, root, root + Path.DirectorySeparatorChar })
            foreach (string suffix in new[] { "combinations.xlsx", @"46-combinazioni-excel\Combinations.xlsx", "report.docx" })
            {
                string actual = "GH_String " + Path.Combine(temp, "bridghe-gpc-only-models", suffix);
                string expected = "GH_String " + CapturedDirectory + suffix;
                Check(Normalize(actual, temp) == expected, "only the TEMP prefix changes: " + suffix);
                Check(Normalize(actual + ".changed", temp) != expected, "changed filename is still compared");
            }
        foreach (string text in new[] {
            "GH_String 0.7", "GH_String 0.8", "Warning: generation failed", "File {0} 1",
            "GH_String " + Path.Combine(root, "bridghe-gpc-only-models-other", "report.docx"),
            "GH_String note " + Path.Combine(root, "bridghe-gpc-only-models", "report.docx") })
            Check(Normalize(text, root) == text, "unowned text is unchanged");
        string oldValue = "GH_String " + Path.Combine(root, "bridghe-gpc-only-models", "combinations.xlsx") + "\nGH_String 0.7";
        string changedValue = oldValue.Replace("0.7", "0.8", StringComparison.Ordinal);
        Check(P2rCapture.Sha(Normalize(oldValue, root)) != P2rCapture.Sha(Normalize(changedValue, root)), "content change survives prefix normalization");
        Console.WriteLine($"REPLAY TEMP PASS: {checks} checks; normal/short/custom TEMP as text, owned prefix only, names and content retained.");
    }
}
