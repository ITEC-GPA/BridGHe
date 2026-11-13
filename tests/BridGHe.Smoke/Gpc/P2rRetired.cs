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
        ? "P2F: raw outputs captured with a composite beam GUID new at every solve (P7, fixed by P2F), not compared; lines, messages and outputs compared (" + Decision + ")"
        : ReplayRawKeysP2ca.Contains((file, key)) ? "P2CA: in-memory case built with its GPC twin, its identities differ from the FEM-backed input of the capture; "
            + "lines, messages and outputs modulo GUIDs compared (" + CutDecision + ")" : null;

    /// <summary>P2CA (13/11/2026, coordinator decision 1, rule U3): the keys of the P2R replay capture whose input is now built with its GPC twin (no FEM
    /// value): their identities differ from those of the conversion of the former FEM-backed input, so their raw field alone is retired; the line count,
    /// the messages and the outputs modulo GUIDs are compared as before.</summary>
    internal static readonly IReadOnlyList<(string File, string Key)> ReplayRawKeysP2ca = new[]
    {
        ("gpc-only-models", "in memory: Build Model (A05 Knm)"),
        ("gpc-only-models", "in memory: Build Model (A05 SI)"),
        ("gpc-only-models", "in memory: Build Model (A05 IPS)"),
    };

    /// <summary>The date of the retirements of P2CUT (rule U3: a check that loses its FEM oracle or tests a FEM type keeps an independent oracle, or is
    /// retired by name).</summary>
    internal const string CutDecision = "U3, P2CUT 13/11/2026";

    /// <summary>
    /// P2CUT (13/11/2026, rule U3): the checks retired by name with the FEM types they tested, key <c>scope|name</c> → reason (with the GPC
    /// coverage). Scopes: <c>core</c> (the checks of <c>--core-only</c> counted in <c>PASS TOTAL</c>), <c>core-native</c> (the checks added by
    /// <c>--native</c>), <c>regression</c> (the inline regressions of <c>RegressionTests</c>, one per key, no longer in <c>--regression all</c>).
    /// Every other check of these runs is unchanged or ported to GPC with the same oracle.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> Checks = CutChecks();

    private static Dictionary<string, string> CutChecks()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string key, string reason) => d.Add(key, reason + " (" + CutDecision + ")");
        P2rRetiredP2cb.AddTo(Add); // P2CB (13/11): the retirements of lane P2CB (Gpc/P2rRetired.P2cb.cs)
        const string FemType = "a check of the FEM model types (BridGHe.Common.Models), which the GPC path does not use";
        P2caChecks(Add); // P2CA (13/11/2026): the replay, archive and data side of the tests
        Add("core|section archive roundtrip", FemType + "; the GH archive round trip of a section on its GPC datum: P2fChecks.GooArchives (core)");
        Add("core|standalone moving case included in export", "ModelHelper.GetLoadCases on a FEM model; the cases of the moving paths of a GPC model: --examples (04) and --moving-load-tests --native");
        Add("core|model and moving paths archive roundtrip", FemType + "; the GPC model with its moving paths in BGHGPC1: gpc-draft-archive, p2d");
        Add("core|replacing moving paths removes obsolete generated cases", "MovingModelBuilder.ReplacePaths on a FEM model; on GPC GpcModelTransforms.ReplacePaths: gpc-model-transforms (explicit expected values of R1)");
        Add("core|replacing moving paths leaves original model intact", "MovingModelBuilder.ReplacePaths on a FEM model; on GPC GpcModelTransforms.ReplacePaths: gpc-model-transforms (explicit expected values of R1)");
        Add("core|FEM graph and indexes roundtrip", FemType);
        Add("core|attributes and results roundtrip", FemType);
        Add("core|shared load case identity preserved", FemType);
        Add("core|FEM copy constructor with cloned attributes", FemType);
        Add("core|shrinkage contributions accumulate in the correct phase", "the slab strain loads of a FEM element in the phase index; on GPC the index has no strains (the strains come from the recipe of Build BridGHe Model): r2b-merge-forces and the native 50");
        foreach (var shape in BridGHe.Common.Straus.StrausSectionCatalog.All)
        {
            Add("core-native|native solid preview " + shape.Key, "the solid preview of a FEM beam (BeamModel.Shapes); the preview of the native sections on GPC: C3 (--c3, core-native)");
            Add("core-native|native preview archive " + shape.Key, "the FEM archive of a FEM section (ModelArchive); the GH archive round trip of a section on its GPC datum: P2fChecks.GooArchives (core)");
        }
        Add("core-native|legacy native section re-resolved in mm from model-unit dimensions", "the 0.3.0 reader on a payload written from a FEM model; the 0.3.0 reader with native sections: gpc-legacy-import, gpc-build-model-legacy");
        Add("core-native|library moving template expanded by the Converter (former model)", "the 0.3.0 reader on a payload written from a FEM model; the GPC half (Attach Moving Paths and Export) stays in CheckMovingLibrary");
        Add("regression|archive-before-cleanup", FemType + " (a FEM model archive); the 0.3.0 payloads read by value: gpc-legacy-import, --legacy-payload-scan");
        Add("regression|group-null-identity", FemType + " (GroupModel, BeamStringGroupAttributeModel); the groups of the drafts: gpc-element-drafts, gpc-only-elements");
        Add("regression|composite-archive", FemType + " (CompositeBeamModel); the composite beam on GPC: gpc-only-models, p2k-components (P7)");
        Add("regression|composite-preview", FemType + " (CompositeBeamModel.Shapes); the preview of the GPC drafts: gpc-c3, p2d (U5)");
        Add("regression|nodal-actions", FemType + " (NodalLoadAccumulator on FEM loads); the nodal loads on GPC: gpc-builders-a, --examples (01)");
        Add("regression|load-case-identity", FemType + " (ModelHelper.GetLoadCases); the cases of Build Model on GPC: gpc-build-model, gpc-build-model-legacy");
        Add("regression|merge-node-data", FemType + " (ModelHelper.MergeModel); Merge Model on the drafts: gpc-draft-merge");
        Add("regression|beam-property-validity", FemType + " (BeamPropertyModel.IsValid); the Beam Property on GPC: gpc-builders-b, gpc-only-properties");
        Add("regression|native-beam-validity", FemType + " (a FEM beam on a native section); the native sections on GPC: gpc-only-properties (native part in core-native), C3 native preview");
        Add("regression|deep-copies", FemType + " (copy constructors of the FEM types); the GPC copies: gpc-null-values, shared-properties");
        Add("regression|staged-load-cases", FemType + " (ModelHelper.GetLoadCases of a staged FEM model); the staged cases on GPC: gpc-build-model, --examples (06-10)");
        Add("regression|staged-selection", FemType + " (StrausResultSelection on a FEM model); the selection on GPC: gpc-result-selection");
        Add("regression|merge-plate-connectivity", FemType + " (ModelHelper.MergeModel of FEM plates); Merge Model on the drafts: gpc-draft-merge");
        Add("regression|node-index", FemType + " (StrausNodeIndex of FEM nodes); the export of the GPC model by the Converter: --examples, gpc-only-analysis");
        Add("regression|archived-attribute-updates", FemType + " (preview of a FEM beam after its archive); the preview of the GPC drafts: gpc-c3");
        Add("regression|plate-point-updates", FemType + " (PlateModel.Points); the plate drafts: gpc-element-drafts, gpc-only-elements");
        Add("regression|incomplete-beam-preview", FemType + " (BeamModel.Shapes); the preview of the GPC drafts: gpc-c3");
        Add("regression|preview-bounds", FemType + " (bounds of FEM beams in the GH types); the GH types on their GPC datum: gpc-null-values");
        Add("regression|model-preview", FemType + " (the preview of a GH model with a FEM value); the GH model on its GPC input: gh-model-gpc, gpc-null-values");
        Add("regression|r4b-inputs", "transitional (R4b): the GPC inputs of the natives equal to the conversion of their former FEM models; the natives compare their GPC results with the captures");
        Add("regression|r4c-inputs", "transitional (R4c): the GPC inputs of the examples 11-18 equal to the conversion of their former FEM models; the natives compare with the captures (phase-native D3, grillage/spine/suite)");
        Add("regression-part|model-guids: result models duplicate with complete GUID maps", "the GUID map of the FEM value of GH_Model (GetGuid); the GPC part (P2fChecks.ModelGuids) stays");
        Add("regression-part|model-guids: GUID restoration is idempotent", "the GUID map of the FEM value of GH_Model (FromModel, SynchronizeGuids); the GPC part (P2fChecks.ModelGuids) stays");
        return d;
    }

    /// <summary>P2CA (13/11/2026, rule U3): the checks of the replay, archive and data side of the tests retired by name with the FEM types
    /// they tested. Scopes as above, and <c>import-dump</c> (the conversions of <c>--import-dump</c>, one per key: the file it no longer writes)
    /// and <c>regression-part</c> (a part of a regression that stays).</summary>
    private static void P2caChecks(Action<string, string> add)
    {
        add("regression-part|gpc-legacy-import: the frozen payloads equal to the FEM writer now, byte for byte, and the former local axes (58 checks)",
            "the FEM writer of the fixtures (LegacyPayloadFixtures.Fem.cs, ModelArchive.Write of the FEM models) and its parity with the frozen payloads; the frozen payloads stay "
            + "the oracle of the 0.3.0 reader (gpc-legacy-import: SHA-256 of every payload, the outcomes of the index, the frozen local axes of units-axes.tsv), "
            + "--legacy-payload-scan and --import-dump (the fixtures beams-*, nodes-*, plates-*)");
        foreach (var memory in MemorySources)
            add("import-dump|" + memory, "the example built in memory by the FEM catalogue (NativeExamples.Create) and written by the FEM writer (ModelArchive.Write); no frozen "
                + "payload holds it (it is also retired from the R6.0 capture, CaptureSources); the same example as BridGHe 0.3.0 saved it stays in the corpus of --import-dump "
                + "(examples-*, R3LegacyCorpus.Examples)");
        add("regression-part|gpc-draft-archive: the examples 01-10 built in memory by the FEM catalogue (10 models)", "the drafts of the FEM values of the "
            + "FEM catalogue (NativeExamples.Create, GpcBeamDrafts.FromFemm), their re-identification against the conversion of the FEM value, the FEM materials and "
            + "the by-value reading of their FEM archives; no frozen payload holds them and their entries of the R6.0 capture are retired sources (CaptureSources). "
            + "GPC coverage: the 103 models of the corpus read by value from their bytes (captures, captured examples 01-10, fixtures), every draft through the "
            + "final writer and reader and GpcDrafts.Copy/Reidentify on each, compared with the capture corpus");
        add("legacy-payload-scan|the comparisons with the FEM path of today", "the payloads read by the FEM classes (ModelArchive.Read) and converted by the "
            + "FEM path (GpcBeamDrafts.FromFemm, GpcMaterials.FromFemm, GpcModels: drafts, materials, cases, combinations, stages, properties, sections, composite "
            + "definitions, models with their owner and signatures), the identity of the FEM value, the fallback to the 0.3.0 reader for unreadable payloads, "
            + "the FEM value kept by the GH types and the drafts of a model against the conversion of its FEM values; the oracle that stays is the R6.0 "
            + "capture payloads (every payload by value compared with the datum of the FEM path when it was written, the owner of the models from the "
            + "capture) and the GH types (read = by value, BGHGPC1 round trip, drafts round trip)");
        add("regression-part|gpc-null-values: TRANSITIONAL checks of the FEM catalogue against the FEM-backed types (1290 checks)", "the FEM catalogue of the examples "
            + "01-10 (NativeExamples.Create), every GPC-only GH type against its FEM-backed one (items, bounding boxes, solids, GUIDs and copy constructors of the FEM "
            + "types); GPC coverage: the checks without FEM of the same regression on the GPC examples 01-10 (R0), the drafts of the builders and the producers "
            + "(validity, datum, bounding box and casts, text, copies, persistence without FemmR6, damaged chunks, state of the model)");
        add("regression-part|gpc-null-values: R65-C2-01 on coordinate systems with a FEM value (3 checks)", "a GH coordinate system made from a FEM CoordinateSystemModel "
            + "(stretched and global); GPC coverage: the global system by its frame alone (gpc-null-values) and the frame rule of U6 on the points (2, 0, 0) and (0, 3, 0) "
            + "(p2d, GpcLoadFrames.IsGlobal)");
        add("regression-part|gpc-state-override: TRANSITIONAL areas of the beams of the FEM catalogue (25 areas)", "the FEM area of the beam properties of the "
            + "examples 01-10 (NativeExamples.Create) against the former formula on the GPC section of their conversion (GpcModels.Convert); GPC coverage: the "
            + "analytical areas of the standard shapes and the mass per length = density x area bit for bit in the same regression, the beam properties of the "
            + "0.3.0 models read by value (gpc-build-model-legacy)");
        add("regression-part|p2k-components: P10 the definition from the former slab value equals the one from the slab datum (1 check)", "the former slab value "
            + "(SectionConcreteSlabModel) given to Composite Beam Property; GPC coverage: the slab datum of Concrete Slab Section with its numbers, the definition of "
            + "the same inputs twice (p2k-components), composite-property-phases on the slab datum");
        add("regression-part|p2d: frame rule, the conversion of a FEM model with a distributed load on a system (global, stretched, turned; 3 checks)", "the "
            + "conversion of an in-memory FEM model (GpcModels.Convert) with the load on a CoordinateSystemModel; GPC coverage: the frame rule of U6 on the points "
            + "(GpcLoadFrames.IsGlobal: global, stretched (2, 0, 0) (0, 3, 0), off-axis, another origin, turned, aligned) and the global frame, p2d");
        add("regression-part|p2d: preview, the drawing of the FEM beam and the reader of a FEM beam (5 checks)", "BeamModel.Shapes and ReinforcementLines of a FEM beam "
            + "on a homogenised property, its draft by GpcBeamDrafts.FromFemm, and the builder compared with them; GPC coverage: the beam built on GPC with explicit counts "
            + "(2 solids, 6 or 3 bar lines) in both variants, the plug-in preview of the draft, the outline through the records (p2d), the bar rows of display-settings");
        add("regression-part|p2d: construction, TRANSITIONAL the conversion of the FEM value without drafts (1 check)", "GpcModels.Convert of the FEM model (the "
            + "FEM-input copy of gpc-only-models); GPC coverage: the 0.3.0 model read by value with its drafts gives no native stage (p2d)");
        add("regression-part|gpc-only-models: L3e A05 and A06 on the conversion of the FEM value (4 checks)", "GpcModels.Convert of an in-memory FEM model with "
            + "the plate mass and the heat path; GPC coverage: the same values in N, mm, t on Build Model of the GPC twin and Attach Moving Paths in every unit system "
            + "(gpc-only-models, Lane3eChecks), and the A05 Build Model cases of the replay");
        add("regression-part|p2d: development, a FEM-backed GH type writes its datum alone (1 check)", "a GH_Beam made from a FEM BeamModel (a FEM-backed "
            + "GH type of a component not yet on GPC); after the cut no GH type has a FEM value; GPC coverage: no FemmR6 written again by a beam and by a model of a "
            + "development file and by a 0.3.0 model (p2d, on the frozen payloads of Fixtures/p2d-payloads)");
    }
}
