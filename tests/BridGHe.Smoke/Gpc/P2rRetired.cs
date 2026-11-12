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

    /// <summary>P2F (12/11/2026, item 5): the key <c>c5|recipe</c> of the native capture of 99 retired by name (label, key) → reason. It is
    /// not recorded any more (a retired key recorded again would be compared).</summary>
    internal static readonly IReadOnlyDictionary<(string Label, string Key), string> NativeKeysP2f = new Dictionary<(string, string), string>
    {
        [("99", "c5|recipe")] = "P2F: the recipe of the GPC bridge model has the same content as at the base 2c7c243 (4 phases, 5 actions, phase by phase and action by action, "
            + "the identities written by what they name: P2B/results/p2fn-recipe, the three texts identical); its fingerprint EF712C2E… → 1DBE610F… moved with the identities alone "
            + "(model GUID, analysis fingerprint, case GUIDs, phase IDs) (" + Decision + ")",
    };

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
        if (ScanKeys.TryGetValue(key, out var why)) return "P2F: " + why + " (" + Decision + ")";
        return null;
    }

    /// <summary>P2F (12/11/2026): the 31 entries <c>check-material</c> of the source <c>scan</c> retired by name, listed one by one in
    /// <c>Fixtures/capture-rules/P2F-retired-scan-20261112.tsv</c> (key, reason, decision): the check materials the FEM walk of the scan
    /// reached only through FEM values (FromFemm of the material of a FEM property, element or model), with no GH_Material of the same text
    /// in the file. Read once; the list must hold 31 keys.</summary>
    internal static readonly string ScanFile = Path.GetFullPath(Path.Combine("tests", "BridGHe.Smoke", "Fixtures", "capture-rules", "P2F-retired-scan-20261112.tsv"));
    private static Dictionary<string, string> scan;
    private static readonly object scanGate = new object();

    private static IReadOnlyDictionary<string, string> ScanKeys
    {
        get
        {
            lock (scanGate) return scan ??= ReadScanKeys();
        }
    }

    private static Dictionary<string, string> ReadScanKeys()
    {
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadLines(ScanFile).Skip(1))
            {
                var f = line.Split('\t');
                if (f.Length != 3 || !f[0].Contains("|check-material|", StringComparison.Ordinal) || f[2] != Decision) throw new InvalidDataException("P2F RETIRED: a scan key not of the declared kind: " + line);
                d.Add(f[0], f[1]);
            }
            if (d.Count != 31) throw new InvalidDataException("P2F RETIRED: the list must hold 31 check materials of the scan (U3)");
            return d;
        }
    }

    /// <summary>P2F (12/11/2026, rule U3): the keys of the P2R replay capture retired by name, (file, key) → reason. The captures are not
    /// rewritten; a retired key is not compared (see <see cref="ReplayRaw"/> for the keys whose raw field alone is retired).</summary>
    internal static readonly IReadOnlyDictionary<(string File, string Key), string> ReplayKeys = new Dictionary<(string, string), string>
    {
        [("gpc-only-models", "in memory: Bridge Model Audit (a homogenised beam beside a plate, far away)")] =
            "P2F: the in-memory model of the case had no nodes and was refused («Nessun nodo FEM entro tolleranza»); it is given its nodes, so the captured refusal is not the case any more (" + Decision + ")",
    };

    /// <summary>P2F (12/11/2026, rule U3): the 8 keys of the P2R replay capture whose raw field (the outputs with their identities) was
    /// captured with the GUID of a GPC-only composite beam that changed at every solve (P7, fixed by P2F in CompositeBeamComponent): that
    /// identity cannot be given again. Their line count, messages and outputs modulo GUIDs are compared as before.</summary>
    internal static readonly IReadOnlyList<(string File, string Key)> ReplayRawKeys = new[]
    {
        ("gpc-only-models", @"19-flusso-acciaio-misto\19-flusso-acciaio-misto_REV003.ghx: Composite beam [CB]#2"),
        ("gpc-only-models", @"19-flusso-acciaio-misto\19-flusso-acciaio-misto_REV003.ghx: Composite beam [CB]#3"),
        ("gpc-only-models", @"19-flusso-acciaio-misto\archivio\alias-REV003\19-flusso-acciaio-misto.ghx: Composite beam [CB]#2"),
        ("gpc-only-models", @"19-flusso-acciaio-misto\archivio\alias-REV003\19-flusso-acciaio-misto.ghx: Composite beam [CB]#3"),
        ("gpc-only-models", "47-pioli-irrigidimenti-verifiche\\47-integrazione-modello.ghx: Composite beam [Propriet\u00E0 fisica mista]"),
        ("gpc-only-models", "48-report-word\\48-report-word.ghx: Composite beam [Propriet\u00E0 fisica mista]"),
        ("gpc-only-models", "in memory: Composite beam (a beam of the example 02 and a composite property)"),
        ("gpc-only-models", "in memory: Composite beam (start and end sections and a group)"),
    };

    internal static string ReplayRaw(string file, string key) => ReplayRawKeys.Contains((file, key))
        ? "P2F: raw outputs captured with a composite beam GUID new at every solve (P7, fixed by P2F), not compared; lines, messages and outputs compared (" + Decision + ")" : null;
}
#endif
