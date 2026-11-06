#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using GPC.Model.Materials;

/// <summary>
/// BGHGPC1 (coordinator decision of 6/11, option (b)), the two paths built and proven before the removal of FeMM, not switched on:
/// <list type="bullet">
/// <item>(i) regression <c>gpc-draft-archive</c>: the final writer and reader of the GH types (<see cref="GpcDraftArchive"/>): draft → write
/// → read → draft bit for bit (<see cref="Text(LegacyNodeDraft)"/>: the canonical document of the GPC element, the records, the bridge data,
/// the property data with the section recipe, the corners, the report) for every node, beam and plate value and every material of the
/// import-dump corpus; the drafts of the Build Model inputs of the examples in <c>--draft-scan</c>;</item>
/// <item>(ii) <c>--legacy-payload-scan</c>: every BGHA1 payload internalised in the example definitions (raw archive, nothing solved or
/// written), read by value (<see cref="LegacyModelImport.NodeDraft"/>, <see cref="LegacyModelImport.BeamDraft"/>,
/// <see cref="LegacyModelImport.PlateDraft"/>, <see cref="LegacyModelImport.Material"/> on <see cref="LegacyArchive.Read(byte[])"/>) against
/// the drafts of the FEM values (<see cref="GpcBeamDrafts.FromFemm(BeamModel, Guid?)"/>, <see cref="GpcMaterials.FromFemm"/>), with the same
/// identity; the identity the payload bytes would give (SHA-256 of the stored bytes) counted against the one of the FEM value.</item>
/// </list>
/// </summary>
internal static class GpcDraftArchiveChecks
{
    private const string Results = @"C:\Users\g.pacini\Documents\Repository\gpc-f6-claude\P2B\results";
    private static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");
    private static string Fp(params object[] values) => GPC.Model.Core.ModelValues.Fingerprint(values);
    private static string Hex(byte[] b) => b == null ? "null" : Convert.ToHexString(SHA256.HashData(b));

    internal static string Records(LegacyElementRecords r) => string.Join("\n", new[]
    {
        "groups " + string.Join("|", r.Groups), "labels " + string.Join("|", r.Labels), "super elements " + string.Join("|", r.SuperElements),
        $"counts {r.Members} {r.SectionStates} {r.Tapers} {r.EmptyLabels}", "support carries " + string.Join("|", r.SupportCarries), "first label " + (r.FirstLabel ?? "<null>"),
        "cases " + string.Join("|", r.LoadCases.Select(c => c == null ? "null" : $"{c.Name}~{B(c.Temperature)}~{c.StructuralMass}~{c.NonStructuralMass}~{c.Inertia}~{c.GravityDirection}~{B(c.GravityValue)}~{c.Stages}~{c.HasStageList}")),
        "property carries " + string.Join("|", r.PropertyCarries.Select(p => p.Key + "=" + p.Value)), "loads " + string.Join("|", r.LoadRecords.Select(l => l.Kind + ":" + l.Case)),
    });

    internal static string Report(LegacyImportReport r) => string.Join("\n", new[]
    {
        $"units {r.Units.Units} owner {r.Owner} registered {r.RegisteredCases} build {(r.BuildModel == null ? "null" : "set")}",
        "warnings " + string.Join(" | ", r.Warnings), "carried " + string.Join(" | ", r.NotExported.Select(x => x.Key + "=" + x.Value)),
        "element warnings " + string.Join(" | ", r.ElementWarnings.Select(w => $"{w.Family}:{w.Number}:{w.Format}:{w.Restraint}")),
        "angles " + string.Join(",", r.BeamAngles.Select(a => a.Key + "=" + B(a.Value))),
        $"model data {r.NativeSections.Count} {r.CaseOrigins.Count} {r.BeamProperties.Count} {r.ShadowedPropertyBeams.Count} {r.ElementLoadCases.Count}",
        "bridge " + (r.Bridge == null ? "null" : r.Bridge.Count + " " + r.Bridge.Fingerprint()),
    });

    private static string Bridge(GPC.Checkers.CompositeBridge.BridgeBeamData d)
    {
        if (d == null) return "bridge none";
        var one = new GPC.Checkers.CompositeBridge.BridgeModelData(); one.Set(d);
        return "bridge " + d.BeamGuid + " " + one.Fingerprint();
    }

    private static string Property(LegacyBeamPropertyData p)
    {
        if (p == null) return "property none";
        var s = p.Section;
        string section = s == null ? "none" : $"{s.Kind} {s.Name} " + string.Join(",", new[] { s.Angle, s.D, s.B, s.B1, s.B2, s.T, s.T1, s.T2, s.T3, s.Area, s.I11, s.I22, s.J, s.CentroidX, s.CentroidY, s.ShearL1, s.ShearL2, s.ShearA1, s.ShearA2 }.Select(B))
            + $" {s.CompositePreview} {s.NativeKey ?? "null"} [{(s.NativeDimensions == null ? "null" : string.Join(",", s.NativeDimensions.Select(B)))}] {s.BxsPath ?? "null"} {s.LibraryName ?? "null"} {s.LibraryItem ?? "null"} {s.NativeLengthUnit}";
        return $"property {p.Formulation} {B(p.Angle)} native {Hex(p.Native?.Save())} signature {p.Signature ?? "null"} section {section}";
    }

    internal static string Text(LegacyNodeDraft d) => "NODE\n" + GpcInputComparison.ValueDocument(d.Node) + "\n" + Records(d.Records) + "\n" + Report(d.Report);
    internal static string Text(LegacyBeamDraft d) => "BEAM\n" + GpcInputComparison.ValueDocument(d.Beam) + "\n" + Bridge(d.Bridge) + "\nangle " + B(d.AngleDeg) + "\n" + Property(d.Property)
        + "\n" + Records(d.Records) + "\n" + Report(d.Report);
    internal static string Text(LegacyPlateDraft d) => "PLATE\n" + GpcInputComparison.ValueDocument(d.Plate) + "\ncorners " + string.Join(",", d.Corners.Select(c => B(c.X) + " " + B(c.Y) + " " + B(c.Z)))
        + "\n" + Records(d.Records) + "\n" + Report(d.Report);
    internal static string Text(Material m) => "MATERIAL\n" + GpcMaterialKindChecks.Dump(m) + "\n" + GpcInputComparison.ValueDocument(m);
    private static string Case(LegacyCaseData c) => c == null ? "null" : $"{c.Name}~{B(c.Temperature)}~{c.StructuralMass}~{c.NonStructuralMass}~{c.Inertia}~{c.GravityDirection}~{B(c.GravityValue)}~{c.Stages}~{c.HasStageList}";
    internal static string Text(LegacyCaseData c) => "CASE " + Case(c);
    internal static string Text(LegacyCombinationDraft c) => "COMBINATION " + c.Name + " " + string.Join("|", c.Factors.Select(f => Case(f.Case) + "*" + B(f.Factor)));
    internal static string Text(LegacyStageDraft s) => "STAGE " + s.Name + " " + (s.Groups == null ? "null" : string.Join("|", s.Groups)) + $" {s.Morph} {s.Rotate} "
        + (s.Increments == null ? "null" : string.Join(" / ", s.Increments.Select(Text)));
    internal static string Text(LegacySectionRecipe s) => "SECTION " + Property(new LegacyBeamPropertyData(GPC.Model.Structure.Assignments.BeamFormulation.Unknown, 0, null, null) { Section = s });
    internal static string Text(LegacyBeamProperty p) => "BEAM PROPERTY\n" + GpcInputComparison.ValueDocument((GPC.Model.ElementProperties.ElasticBeamProperty)p.Property) + "\n" + Property(p.Data);
    internal static string Text(GPC.Model.ElementProperties.PlateProperty p) => "PLATE PROPERTY\n" + (p is GPC.Model.ElementProperties.MembranePlateProperty m ? GpcInputComparison.ValueDocument(m) : GpcInputComparison.ValueDocument((GPC.Model.ElementProperties.ElasticPlateProperty)p));
    internal static string Text(GPC.Checkers.CompositeBridge.BridgeModel m, BridgeUnits u) => "MODEL " + u.Units + "\n" + (GpcInputComparison.Document(m.Model) ?? "beyond the serializer limit")
        + "\nbridge " + m.Data.Count + " " + m.Data.Fingerprint();
    internal static string Text(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition d)
    {
        var one = new GPC.Checkers.CompositeBridge.BridgeModelData();
        one.Set(new GPC.Checkers.CompositeBridge.BridgeBeamData(GpcIdentity.Derive(GpcIdentity.NameSpace, "text"), compositeSection: new GPC.Checkers.CompositeBridge.BridgeBeamCompositeSection(d)));
        return "COMPOSITE " + d.Name + " " + one.Fingerprint();
    }

    /// <summary>A model datum (<c>GH_Model</c>) for the round trip: the conversion and its definition units.</summary>
    internal sealed record ModelDatum(GPC.Checkers.CompositeBridge.BridgeModel Model, BridgeUnits Units);

    /// <summary>The round trip of one draft through the final writer and reader, as two texts that must be equal.</summary>
    internal static (string Before, string After, int Bytes) RoundTrip(object draft)
    {
        switch (draft)
        {
            case LegacyNodeDraft n: { var (d, r) = GpcDraftArchive.Write(n); return (Text(n), Text(GpcDraftArchive.ReadNode(d, r)), d.Length + r.Length); }
            case LegacyBeamDraft b: { var (d, r) = GpcDraftArchive.Write(b); return (Text(b), Text(GpcDraftArchive.ReadBeam(d, r)), d.Length + r.Length); }
            case LegacyPlateDraft p: { var (d, r) = GpcDraftArchive.Write(p); return (Text(p), Text(GpcDraftArchive.ReadPlate(d, r)), d.Length + r.Length); }
            case Material m: { var d = GpcDraftArchive.Write(m); return (Text(m), Text(GpcDraftArchive.ReadMaterial(d)), d.Length); }
            case LegacyCaseData c: { var r = GpcDraftArchive.Write(c); return (Text(c), Text(GpcDraftArchive.ReadCase(r)), r.Length); }
            case LegacyCombinationDraft c: { var r = GpcDraftArchive.Write(c); return (Text(c), Text(GpcDraftArchive.ReadCombination(r)), r.Length); }
            case LegacyStageDraft s: { var r = GpcDraftArchive.Write(s); return (Text(s), Text(GpcDraftArchive.ReadStage(r)), r.Length); }
            case LegacySectionRecipe s: { var r = GpcDraftArchive.Write(s); return (Text(s), Text(GpcDraftArchive.ReadSection(r)), r.Length); }
            case LegacyBeamProperty p: { var (d, r) = GpcDraftArchive.Write(p); return (Text(p), Text(GpcDraftArchive.ReadBeamProperty(d, r)), d.Length + r.Length); }
            case GPC.Model.ElementProperties.PlateProperty p: { var (d, r) = GpcDraftArchive.Write(p); return (Text(p), Text(GpcDraftArchive.ReadPlateProperty(d, r)), d.Length + r.Length); }
            case ModelDatum m: { var (d, r) = GpcDraftArchive.Write(m.Model, m.Units); var back = GpcDraftArchive.ReadModel(d, r); return (Text(m.Model, m.Units), Text(back.Model, back.Units), d.Length + r.Length); }
            case GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition c: { var d = GpcDraftArchive.Write(c); return (Text(c), Text(GpcDraftArchive.ReadComposite(d)), d.Length); }
            default: throw new ArgumentException("Not a draft: " + draft?.GetType().Name);
        }
    }

    internal sealed class Tally
    {
        internal readonly List<string> Failures = new List<string>();
        internal readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        internal long Bytes;
        internal void Count(string what, int n = 1) { Counts.TryGetValue(what, out int c); Counts[what] = c + n; }
        internal void Check(string label, object draft)
        {
            string kind = draft.GetType().Name;
            try
            {
                var (before, after, bytes) = RoundTrip(draft); Bytes += bytes;
                if (before != after) { Count(kind + " different"); if (Failures.Count < 20) Failures.Add(label + ": " + kind + " round trip differs\n" + FirstDifference(before, after)); }
                else Count(kind);
            }
            catch (Exception e) { Count(kind + " error"); if (Failures.Count < 20) Failures.Add(label + ": " + kind + " " + e.GetType().Name + ": " + e.Message); }
        }
        internal string Summary => string.Join(", ", Counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + " " + c.Value));
    }

    internal static string FirstDifference(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
        {
            string p = i < x.Length ? x[i] : "<end>", q = i < y.Length ? y[i] : "<end>";
            if (p != q) return $"  line {i + 1}\n  before {Cut(p)}\n  after  {Cut(q)}";
        }
        return "  (same lines)";
        static string Cut(string s) => s.Length > 400 ? s.Substring(0, 400) + "…" : s;
    }

    /// <summary>The drafts of the GH types of a FEM model (every node, beam, plate value, as <c>GH_Node.Gpc</c> etc., and every material).</summary>
    internal static void Model(Tally tally, string id, ModelModel fem)
    {
        for (int q = 0; q < fem.Elements.Count; q++)
        {
            var e = fem.Elements[q];
            object draft;
            try { draft = e switch { NodeModel n => GpcBeamDrafts.FromFemm(n), BeamModel b => GpcBeamDrafts.FromFemm(b), PlateModel p => GpcBeamDrafts.FromFemm(p), _ => null }; }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException) { tally.Count(e.GetType().Name + " refused by the reader"); continue; }
            if (draft != null) tally.Check(id + ":" + e.GetType().Name + " " + q, draft);
        }
        foreach (var (m, _, path) in GpcMaterialKindChecks.Materials(fem)) tally.Check(id + ":" + path, GpcMaterials.FromFemm(m));
        // The other GH types: every load case, combination, stage, beam and plate property, section and composite property of the model, as
        // their GH types would carry them (the by-value readers on the archive of the value), and the model itself (the GH_Model conversion).
        foreach (var (v, path) in Values(fem, typeof(LoadCaseModel), typeof(LoadCombinationModel), typeof(StagedConstructionModel), typeof(BeamPropertyModel),
            typeof(PlatePropertyModel), typeof(SectionModel), typeof(CompositeBeamPropertyModel)))
        {
            object draft;
            try { draft = DraftOf(v); }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException) { tally.Count(v.GetType().Name + " refused by the reader"); continue; }
            if (draft != null) tally.Check(id + ":" + path, draft);
        }
        try { var c = GpcModels.Convert(fem, 1e-3); tally.Check(id + ":model", new ModelDatum(c.BridgeModel, c.Report.Units)); }
        catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException) { tally.Count("model refused by the reader"); }
        // (ii) on the corpus: every element of the archive of the model, read by value inside the model archive, against the draft of the FEM
        // value (its own archive), with the same identity.
        var clean = fem.Elements.Any(e => e.Results.Count != 0) ? BridGHe.Common.Helpers.ModelArchive.Clone(fem) : fem;
        if (!ReferenceEquals(clean, fem)) foreach (var e in clean.Elements) e.Results.Clear();
        var legacy = LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(clean)).Field("Elements").Items;
        for (int k = 0; k < clean.Elements.Count; k++)
        {
            var e = clean.Elements[k]; string label = id + ":" + e.GetType().Name + " " + k + " (by value)";
            try
            {
                string expected, byValue;
                switch (e)
                {
                    case NodeModel n: { var d = GpcBeamDrafts.FromFemm(n); expected = Text(d); byValue = Text(LegacyModelImport.NodeDraft(legacy[k], GpcBeamDrafts.DraftUnits, d.Node.Guid)); break; }
                    case BeamModel b:
                    {
                        var d = GpcBeamDrafts.FromFemm(b); expected = Text(d);
                        byValue = Text(LegacyModelImport.BeamDraft(legacy[k], GpcBeamDrafts.DraftUnits, d.Beam.Guid, b.BeamProperty == null ? null : BridgeSectionAssignment.Signature(b.BeamProperty))); break;
                    }
                    case PlateModel p: { var d = GpcBeamDrafts.FromFemm(p); expected = Text(d); byValue = Text(LegacyModelImport.PlateDraft(legacy[k], GpcBeamDrafts.DraftUnits, d.Plate.Guid)); break; }
                    default: continue;
                }
                if (expected != byValue) { tally.Count("by value different"); if (tally.Failures.Count < 20) tally.Failures.Add(label + "\n" + FirstDifference(expected, byValue)); }
                else tally.Count("by value " + e.GetType().Name + " equal");
            }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException)
            {
                // The same error on both paths (a value the reader refuses) counts as equal.
                tally.Count("by value " + e.GetType().Name + " refused");
            }
        }
    }

    private static LegacyValue Legacy(object value) => LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(value));

    /// <summary>The GPC datum (draft or record) the GH type of a former value carries, by the conversions of today.</summary>
    internal static object DraftOf(object v) => v switch
    {
        LoadCaseModel c => LegacyModelImport.CaseDraft(Legacy(c)),
        LoadCombinationModel c => GpcModels.CombinationDraft(c),
        StagedConstructionModel s => GpcModels.StageDraft(s),
        CompositeBeamPropertyModel c => GpcCompositeSections.Definition(c, c.Name),
        BeamPropertyModel p => GpcModels.BeamProperty(p, GpcBeamDrafts.DraftUnits),
        PlatePropertyModel p => LegacyModelImport.PlatePropertyDraft(Legacy(p), GpcBeamDrafts.DraftUnits),
        SectionModel s => LegacyModelImport.SectionDraft(Legacy(s)),
        _ => null,
    };

    /// <summary>Every value of the given former types reachable from a FEM value (distinct instances).</summary>
    internal static List<(object Value, string Path)> Values(object root, params Type[] types)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance); var found = new List<(object, string)>();
        void Visit(object o, string path)
        {
            if (o == null || o is string || o.GetType().IsPrimitive || o.GetType().IsEnum || !seen.Add(o)) return;
            var t = o.GetType();
            if (types.Any(x => x.IsAssignableFrom(t))) found.Add((o, path));
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) { Visit(t.GetProperty("Key").GetValue(o), path); Visit(t.GetProperty("Value").GetValue(o), path); return; }
            if (o is System.Collections.IEnumerable e) { var items = new List<object>(); try { foreach (var item in e) items.Add(item); } catch (Exception) { return; } for (int i = 0; i < items.Count; i++) Visit(items[i], path.Length > 200 ? path : path + "[" + i + "]"); return; }
            if (t.Namespace == null || !t.Namespace.StartsWith("BridGHe.Common", StringComparison.Ordinal)) return;
            for (var c = t; c != null && c != typeof(object); c = c.BaseType)
                foreach (var f in c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!f.FieldType.IsPrimitive && f.FieldType != typeof(string)) Visit(f.GetValue(o), path.Length > 200 ? path : path + "." + f.Name);
        }
        Visit(root, "");
        return found;
    }

    internal static void Regression()
    {
        var tally = new Tally(); int models = 0;
        void Run(string id, ModelModel fem) { Model(tally, id, fem); models++; }
        foreach (var file in Directory.GetFiles(Path.Combine(Results, "capture-1"), "*.bgmodel").OrderBy(f => f, StringComparer.Ordinal))
            Run("capture-" + Path.GetFileNameWithoutExtension(file), BridGHe.Common.Helpers.ModelArchive.Read<ModelModel>(File.ReadAllBytes(file)));
        foreach (var file in Directory.GetFiles(Path.Combine(Results, "examples-1"), "*.bgmodel", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            Run("examples-" + Path.GetFileName(Path.GetDirectoryName(file)) + "-" + Path.GetFileNameWithoutExtension(file), BridGHe.Common.Helpers.ModelArchive.Read<ModelModel>(File.ReadAllBytes(file)));
        foreach (var example in NativeExamples.Create()) Run("memory-" + example.Id, example.Model);
        foreach (ModelModel.ModelUnits units in Enum.GetValues(typeof(ModelModel.ModelUnits)))
        {
            Run("nodes-" + units, ImportDump.NodeData(units)); Run("nodes-labelled-rigid-" + units, ImportDump.NodeData(units, true));
            Run("beams-" + units, ImportDump.BeamData(units, null)); Run("plates-" + units, ImportDump.PlateData(units));
        }
        Console.WriteLine("GPC DRAFT ARCHIVE: " + tally.Summary + $"; {tally.Bytes / 1048576.0:0.0} MB written");
        if (tally.Failures.Count > 0) throw new Exception("GPC DRAFT ARCHIVE FAIL:\n" + string.Join("\n", tally.Failures));
        if (models < 100) throw new Exception("GPC DRAFT ARCHIVE FAIL: only " + models + " models");
        Console.WriteLine($"GPC DRAFT ARCHIVE PASS: {models} models, every draft written and read back bit for bit ({tally.Summary})");
    }

    /// <summary>(ii) <c>--legacy-payload-scan [files]</c>: every BGHA1 payload of every example definition (all of <c>example</c>, archives
    /// included, outputs folders excluded), from the raw archive.</summary>
    internal static int PayloadScan(string[] args)
    {
        var files = args.SkipWhile(a => a != "--legacy-payload-scan").Skip(1).Where(a => !a.StartsWith("--")).Select(a => Path.GetFullPath(Path.Combine(ExamplePaths.Root, a))).ToList();
        if (files.Count == 0)
            files = Directory.GetFiles(ExamplePaths.Root, "*.gh*", SearchOption.AllDirectories).Where(f => (f.EndsWith(".gh") || f.EndsWith(".ghx"))
                && !f.Contains(Path.DirectorySeparatorChar + "calcoli" + Path.DirectorySeparatorChar)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal); var failures = new List<string>(); int unreadable = 0;
        void Count(string what) { counts.TryGetValue(what, out int c); counts[what] = c + 1; }
        int failureCount = 0;
        void Fail(string what) { failureCount++; if (failures.Count < 30) failures.Add(what); }
        var families = new SortedDictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        // By value against the FEM path: equal texts, or the same refusal on both (a value the reader refuses: the GH type keeps the message).
        void Same(string type, string where, Func<string> byValue, Func<string> fem)
        {
            string a, b, ea = null, eb = null;
            try { a = byValue(); } catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException) { a = null; ea = x.Message; }
            try { b = fem(); } catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException) { b = null; eb = x.Message; }
            if (a == null || b == null)
            {
                if (a == null && b == null && ea == eb) { Count(type + " refused on both paths (same message): " + ea); return; }
                Count(type + " different"); Fail(where + ": " + type + " by value: " + (ea ?? "ok") + " | FEM: " + (eb ?? "ok")); return;
            }
            if (a != b) { Count(type + " different"); Fail(where + ": " + type + " by value differs from the FEM path\n" + FirstDifference(b, a)); }
            else Count(type + " equal");
        }
        foreach (var file in files)
        {
            string label = Path.GetRelativePath(ExamplePaths.Root, file);
            var archive = new GH_IO.Serialization.GH_Archive();
            if (!archive.ReadFromFile(file)) { unreadable++; Console.WriteLine($"LEGACY PAYLOAD SCAN {label}: not readable"); continue; }
            int n = 0;
            void Walk(GH_IO.Serialization.GH_IChunk chunk, string path)
            {
                foreach (var item in chunk.Items)
                {
                    if (item.Type != GH_IO.Types.GH_Types.gh_bytearray) continue;
                    var bytes = item._bytearray;
                    if (!LegacyArchive.IsLegacy(bytes)) continue;
                    n++; string where = label + ":" + path + "/" + item.Name;
                    LegacyValue root;
                    try { root = LegacyArchive.Read(bytes); }
                    catch (Exception e)
                    {
                        // A failure, unless the reader of 0.3.0 fails on it with the same message.
                        string former; try { BridGHe.Common.Helpers.ModelArchive.Read<object>(bytes); former = "read by 0.3.0"; } catch (Exception f) { former = f.Message; }
                        Count(former == e.Message ? "unreadable payload (0.3.0 the same)" : "unreadable payload"); if (former != e.Message) Fail(where + ": by value: " + e.Message + " | 0.3.0: " + former);
                        continue;
                    }
                    string type = root.ShortType;
                    string family = label.Split(Path.DirectorySeparatorChar)[0];
                    families.TryGetValue(family, out var perFamily); families[family] = perFamily ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    perFamily.TryGetValue(type, out int pf); perFamily[type] = pf + 1;
                    try
                    {
                        switch (type)
                        {
                            case "NodeModel":
                            {
                                var fem = BridGHe.Common.Helpers.ModelArchive.Read<NodeModel>(bytes); var expected = GpcBeamDrafts.FromFemm(fem);
                                var byValue = LegacyModelImport.NodeDraft(root, GpcBeamDrafts.DraftUnits, expected.Node.Guid);
                                Compare(type, where, Text(expected), Text(byValue), expected.Node.Guid, "femm-node:", bytes, fem.Results.Count); break;
                            }
                            case "BeamModel":
                            {
                                var fem = BridGHe.Common.Helpers.ModelArchive.Read<BeamModel>(bytes); var expected = GpcBeamDrafts.FromFemm(fem);
                                string signature = fem.BeamProperty == null ? null : BridgeSectionAssignment.Signature(fem.BeamProperty);
                                var byValue = LegacyModelImport.BeamDraft(root, GpcBeamDrafts.DraftUnits, expected.Beam.Guid, signature);
                                if (fem.Attributes.OfType<BridgeSectionStateAttributeModel>().Any()) Count("BeamModel with a section state (FEM signature)");
                                Compare(type, where, Text(expected), Text(byValue), expected.Beam.Guid, "femm-beam:", bytes, fem.Results.Count); break;
                            }
                            case "PlateModel":
                            {
                                var fem = BridGHe.Common.Helpers.ModelArchive.Read<PlateModel>(bytes); var expected = GpcBeamDrafts.FromFemm(fem);
                                var byValue = LegacyModelImport.PlateDraft(root, GpcBeamDrafts.DraftUnits, expected.Plate.Guid);
                                Compare(type, where, Text(expected), Text(byValue), expected.Plate.Guid, "femm-plate:", bytes, fem.Results.Count); break;
                            }
                            case "MaterialModel":
                            {
                                var fem = BridGHe.Common.Helpers.ModelArchive.Read<MaterialModel>(bytes);
                                Compare(type, where, Text(GpcMaterials.FromFemm(fem)), Text(LegacyModelImport.Material(root)), Guid.Empty, null, bytes, 0); break;
                            }
                            case "LoadCaseModel": Same(type, where, () => Text(LegacyModelImport.CaseDraft(root)), () => Text((LegacyCaseData)DraftOf(BridGHe.Common.Helpers.ModelArchive.Read<LoadCaseModel>(bytes)))); break;
                            case "LoadCombinationModel": Same(type, where, () => Text(LegacyModelImport.CombinationDraft(root)), () => Text(GpcModels.CombinationDraft(BridGHe.Common.Helpers.ModelArchive.Read<LoadCombinationModel>(bytes)))); break;
                            case "StagedConstructionModel": Same(type, where, () => Text(LegacyModelImport.StageDraft(root)), () => Text(GpcModels.StageDraft(BridGHe.Common.Helpers.ModelArchive.Read<StagedConstructionModel>(bytes)))); break;
                            case "BeamPropertyModel": Same(type, where, () => Text(LegacyModelImport.BeamProperty(root, GpcBeamDrafts.DraftUnits)), () => Text(GpcModels.BeamProperty(BridgeModelArchiveRead<BeamPropertyModel>(bytes), GpcBeamDrafts.DraftUnits))); break;
                            case "PlatePropertyModel": Same(type, where, () => Text(LegacyModelImport.PlatePropertyDraft(root, GpcBeamDrafts.DraftUnits)), () => Text((GPC.Model.ElementProperties.PlateProperty)DraftOf(BridgeModelArchiveRead<PlatePropertyModel>(bytes)))); break;
                            case "SectionModel": Same(type, where, () => Text(LegacyModelImport.SectionDraft(root)), () => Text((LegacySectionRecipe)DraftOf(BridgeModelArchiveRead<SectionModel>(bytes)))); break;
                            case "CompositeBeamPropertyModel": Same(type, where, () => Text(GpcCompositeSections.Definition(root)),
                                () => { var fem = BridgeModelArchiveRead<CompositeBeamPropertyModel>(bytes); return Text(GpcCompositeSections.Definition(fem, fem.Name)); }); break;
                            case "ModelModel":
                            {
                                var fem = BridgeModelArchiveRead<ModelModel>(bytes);
                                if (fem?.Elements == null)
                                {
                                    // 0.3.0 reads no model (null value or no element list): the GH_Model has no model. By value: the same, or an empty model.
                                    int elements = -1; try { elements = LegacyModelImport.Import(root, GpcIdentity.Derive(GpcIdentity.NameSpace, "empty"), 1e-3, new LegacyImportReport()).AllElements.Count(); } catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException || x is System.Runtime.Serialization.SerializationException || x is NullReferenceException) { }
                                    if (elements > 0) { Count(type + " different"); Fail(where + ": 0.3.0 reads no model, by value " + elements + " elements"); }
                                    else Count(type + " without a model in 0.3.0 (by value: " + (elements < 0 ? "refused" : "empty") + ")");
                                    break;
                                }
                                Same(type, where, () =>
                                {
                                    // By value: the payload as stored (results, if any, are not converted), the owner and signatures of the FEM path.
                                    var expected = GpcModels.FromFemm(fem, 1e-3, out _); var report = new LegacyImportReport();
                                    var owner = GpcInputComparison.ElementOwner(expected);
                                    var m = LegacyModelImport.Import(root, owner, 1e-3, report, GpcModels.SectionStateSignatures(fem));
                                    return (GpcInputComparison.Document(m) ?? "beyond the serializer limit") + "\n" + ImportDump.Report(report);
                                }, () => { var report = new LegacyImportReport(); var m = GpcModels.FromFemm(fem, 1e-3, out report); return (GpcInputComparison.Document(m) ?? "beyond the serializer limit") + "\n" + ImportDump.Report(report); });
                                if (fem.Elements.Any(e => e.Results != null && e.Results.Count != 0)) Count("ModelModel with results (cache, not converted)");
                                break;
                            }
                            default: Count("other " + type); break;
                        }
                    }
                    catch (Exception e) { Count(type + " error"); Fail(where + ": " + type + " " + e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split(new[] { (char)10 }, 2)[0].Trim()); }
                }
                foreach (var c in chunk.Chunks) Walk(c, path + "/" + c.Name + (c.Index > 0 ? "[" + c.Index + "]" : ""));
            }
            void Compare(string type, string where, string expected, string byValue, Guid identity, string prefix, byte[] bytes, int results)
            {
                if (expected != byValue) { Count(type + " different"); Fail(where + ": " + type + " by value differs from the FEM draft\n" + FirstDifference(expected, byValue)); }
                else Count(type + " equal");
                if (results != 0) Count(type + " with results (dropped)");
                if (prefix != null)
                {
                    var fromBytes = GpcIdentity.Derive(GpcIdentity.NameSpace, prefix + Convert.ToHexString(SHA256.HashData(bytes)));
                    Count(type + (fromBytes == identity ? " identity of the stored bytes = identity of the FEM value" : " identity of the stored bytes differs"));
                }
            }
            Walk(archive.GetRootNode, "");
            Console.WriteLine($"LEGACY PAYLOAD SCAN {label}: {n} payloads");
        }
        Console.WriteLine("Payloads: " + string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + " " + c.Value)));
        foreach (var f in failures) Console.WriteLine("  " + f);
        foreach (var fam in families) Console.WriteLine("  family " + fam.Key + ": " + string.Join(", ", fam.Value.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + " " + c.Value)));
        Console.WriteLine($"LEGACY PAYLOAD SCAN TOTAL {files.Count} files ({unreadable} unreadable), {counts.Where(c => !c.Key.StartsWith("other ")).Sum(c => c.Key.Contains(" identity ") || c.Key.Contains(" with ") ? 0 : c.Value)} payloads compared or refused, {failureCount} differences or errors");
        return failureCount == 0 && unreadable == 0 ? 0 : 1;
    }

    private static T BridgeModelArchiveRead<T>(byte[] bytes) => BridGHe.Common.Helpers.ModelArchive.Read<T>(bytes);
}
#endif
