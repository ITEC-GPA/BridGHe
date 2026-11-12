#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// P2R (12/11/2026, user decision U3, «Sì, tutti», about 10:00): the captured entries retired by name. A retirement is a declared list (key or
/// source, reason, decision date): a retired entry is neither required nor compared, and the captures themselves are never rewritten. It is not
/// a tolerance: every other entry is compared as before.
/// <list type="bullet">
/// <item><see cref="NativeKeys"/>: the frozen native capture of 50 and 99 (<see cref="NativeParityCapture"/>): the 228 keys
/// <c>definitions|…|fem-constant</c> (180 of 50, 48 of 99), listed one by one in
/// <c>Fixtures/native-capture-rules/P2R-retired-20261112.tsv</c> (label, key, reason, decision).</item>
/// <item><see cref="CaptureSources"/>: the R6.0 capture (<see cref="GpcDraftCapture"/>): the 10 sources <c>memory-01</c> … <c>memory-10</c> (the FEM
/// in-memory models of the examples 01–10, no GPC counterpart in the corpus), and the entries of the 7 examples remade after P2 (23, 46, 24, 42,
/// 43, 47, 48: their payloads change when the coordinator regenerates them, U2).</item>
/// </list>
/// </summary>
internal static class P2rRetired
{
    internal const string Decision = "U3 12/11/2026";

    internal static readonly string NativeFile = Path.GetFullPath(Path.Combine("tests", "BridGHe.Smoke", "Fixtures", "native-capture-rules", "P2R-retired-20261112.tsv"));
    private static Dictionary<(string Label, string Key), string> native;

    /// <summary>The retired keys of the native capture: (label, key) → reason.</summary>
    internal static IReadOnlyDictionary<(string Label, string Key), string> NativeKeys
    {
        get
        {
            if (native != null) return native;
            var d = new Dictionary<(string, string), string>();
            foreach (var line in File.ReadLines(NativeFile).Skip(1))
            {
                var f = line.Split('\t');
                if (f.Length != 4 || !f[1].EndsWith("|fem-constant", StringComparison.Ordinal) || f[3] != Decision) throw new InvalidDataException("P2R RETIRED: a native key not of the declared kind: " + line);
                d.Add((f[0], f[1]), f[2]);
            }
            if (d.Keys.Count(k => k.Item1 == "50") != 180 || d.Keys.Count(k => k.Item1 == "99") != 48) throw new InvalidDataException("P2R RETIRED: the list must hold 180 keys of 50 and 48 of 99 (U3)");
            return native = d;
        }
    }

    /// <summary>The 10 memory sources of the corpus (key prefix <c>memory-&lt;id&gt;:</c>).</summary>
    internal static readonly string[] MemorySources =
    {
        "memory-01-cantilever", "memory-02-simply-supported", "memory-03-plate-patch", "memory-04-moving-load", "memory-05-nonlinear-increments",
        "memory-06-stage-loading", "memory-07-stage-unloading", "memory-08-stage-activation", "memory-09-stage-removal", "memory-10-stage-selection",
    };

    /// <summary>The 7 examples remade after P2 (U2): their definition files (relative to <c>example</c>).</summary>
    internal static readonly string[] RemadeExamples =
    {
        @"23-audit-modello-fase\23-audit-modello-fase.ghx", @"46-combinazioni-excel\46-combinazioni-excel.ghx",
        @"24-inviluppi-concomitanti\24-inviluppi-concomitanti.ghx", @"42-frecce-controfreccia\42-frecce-controfreccia.ghx", @"43-storie-fatica\43-storie-fatica.ghx",
        @"47-pioli-irrigidimenti-verifiche\47-integrazione-modello.ghx", @"48-report-word\48-report-word.ghx",
    };

    /// <summary>The reason an entry of the R6.0 capture is retired, or null when it is compared.</summary>
    internal static string CaptureSources(string key)
    {
        if (MemorySources.Any(s => key.StartsWith(s + ":", StringComparison.Ordinal)))
            return "memory source of the corpus: the FEM in-memory model of an example 01-10, no GPC counterpart (" + Decision + ")";
        if (RemadeExamples.Any(f => key.Contains(Path.GetFileName(f), StringComparison.OrdinalIgnoreCase)))
            return "example remade after P2 (23, 46, 24, 42, 43, 47, 48; U2): its captured payloads retired (" + Decision + ")";
        return null;
    }
}
#endif
