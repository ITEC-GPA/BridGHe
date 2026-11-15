using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
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
/// <see cref="LegacyModelImport.PlateDraft"/>, <see cref="LegacyModelImport.Material"/> on <see cref="LegacyArchive.Read(byte[])"/>) and
/// compared with the R6.0 capture <c>payloads</c> (the datum of the FEM path when it was written); the GH types read, write and read back by
/// value.</item>
/// </list>
/// R6.6 lane R3 (11/11): (i) reads its corpus by value from the bytes (<see cref="ByValue"/>, no FEM class), compared with the R6.0 capture
/// with the identities of the FEM path taken from the capture.
/// <para>P2CA (13/11/2026, U3): the transitional parts are retired by name with the FEM types (<see cref="P2rRetired.Checks"/>): in (i) the
/// examples 01–10 built in memory by the FEM catalogue; in (ii) the comparisons with the FEM path of today (the drafts of the FEM values,
/// their identities, owners and refusals, the FEM value kept by the GH types, the drafts of a model against the conversion of its FEM values).</para>
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
        "bridge " + (r.Bridge == null ? "null" : r.Bridge.Count + " " + (CompareStaleAsStale ? StaleAsStale(r.Bridge) : r.Bridge).Fingerprint()),
    });

    private static string Bridge(GPC.Checkers.CompositeBridge.BridgeBeamData d)
    {
        if (d == null) return "bridge none";
        var one = new GPC.Checkers.CompositeBridge.BridgeModelData(); one.Set(CompareStaleAsStale ? StaleAsStale(d) : d);
        return "bridge " + d.BeamGuid + " " + one.Fingerprint();
    }

    /// <summary>PAR (A08): true while the DRAFT CHECK and the GPC-only replay compute their texts: a stale section state is then compared as
    /// stale. False otherwise: the texts of the captures (R6.0) and of the round trips keep the marker as it is (never regenerated).</summary>
    [ThreadStatic] internal static bool CompareStaleAsStale;

    /// <summary>PAR (A08): a stale section state compared as stale: its marker holds the signature the state was computed for (the FEM signature
    /// of the former value for the reader of a FEM output, the GPC fingerprint for a builder; neither ever equals a property fingerprint). The
    /// data of a beam, and the bridge data of a report with every stale marker so; the others as they are.</summary>
    internal static GPC.Checkers.CompositeBridge.BridgeBeamData StaleAsStale(GPC.Checkers.CompositeBridge.BridgeBeamData d)
    {
        var s = d?.SectionState;
        if (s == null || !s.PropertyFingerprint.StartsWith(LegacyModelImport.StaleSectionStatePrefix, StringComparison.Ordinal)) return d;
        return new GPC.Checkers.CompositeBridge.BridgeBeamData(d.BeamGuid, d.Member, d.SuperElement, new GPC.Checkers.CompositeBridge.BridgeLocalSectionState(s.Behavior, s.ConcreteExcluded,
            s.ModularRatio, s.ClassificationCase, LegacyModelImport.StaleSectionStatePrefix + "(stale)"), d.CompositeSection);
    }

    internal static GPC.Checkers.CompositeBridge.BridgeModelData StaleAsStale(GPC.Checkers.CompositeBridge.BridgeModelData data)
    {
        if (!data.Beams.Any(b => !ReferenceEquals(StaleAsStale(b), b))) return data;
        var copy = data.Copy();
        foreach (var b in data.Beams.ToArray()) { var n = StaleAsStale(b); if (!ReferenceEquals(n, b)) copy.Set(n); }
        return copy;
    }

    internal static string Property(LegacyBeamPropertyData p)
    {
        if (p == null) return "property none";
        var s = p.Section;
        string section = s == null ? "none" : $"{s.Kind} {s.Name} " + string.Join(",", new[] { s.Angle, s.D, s.B, s.B1, s.B2, s.T, s.T1, s.T2, s.T3, s.Area, s.I11, s.I22, s.J, s.CentroidX, s.CentroidY, s.ShearL1, s.ShearL2, s.ShearA1, s.ShearA2 }.Select(B))
            + $" {s.CompositePreview} {s.NativeKey ?? "null"} [{(s.NativeDimensions == null ? "null" : string.Join(",", s.NativeDimensions.Select(B)))}] {s.BxsPath ?? "null"} {s.LibraryName ?? "null"} {s.LibraryItem ?? "null"} {s.NativeLengthUnit}"
            + (s == null ? "" : Lane2c(s));
        return $"property {p.Formulation} {B(p.Angle)} native {Hex(p.Native?.Save())} signature {p.Signature ?? "null"} section {section}";
    }

    /// <summary>L2C (lane 2c): the data of the recipe added in lane 2c, only where the section has them (the texts of the sections without them,
    /// the captures, are unchanged): the fillet radius, the section moduli, the shapes of a Brep section, the mirror and its gaps.</summary>
    private static string Lane2c(LegacySectionRecipe s)
    {
        var text = new System.Text.StringBuilder();
        if (s.R != 0) text.Append(" R ").Append(B(s.R));
        if (s.W11 != 0 || s.W22 != 0 || double.IsNaN(s.W11) || double.IsNaN(s.W22)) text.Append(" W ").Append(B(s.W11)).Append(',').Append(B(s.W22));
        if (s.Shapes != null)
            text.Append(" shapes ").Append(s.Shapes.Count).Append(' ').Append(string.Join(" | ", s.Shapes.Select(h => string.Join(",", h.Fill.Select(B))
                + string.Concat(h.Holes.Select(o => " hole " + string.Join(",", o.Select(B)))))));
        if (s.Mirror != LegacySectionRecipe.NoMirror || s.MirrorGapA != 0 || s.MirrorGapB != 0) text.Append(" mirror ").Append(s.Mirror).Append(' ').Append(B(s.MirrorGapA)).Append(',').Append(B(s.MirrorGapB));
        return text.ToString();
    }

    /// <summary>L2C (lane 2c): the BridGHe data beside a GPC datum that the canonical texts (the captures) do not carry, for the checks that
    /// compare two data of today (the draft check of the attached data, the GPC-only replay): the material kind recorded with a beam property.</summary>
    internal static string Extras(object datum) => datum switch
    {
        LegacyBeamProperty p => "\nmaterial kind " + (p.Data?.MaterialKind ?? "null"),
        _ => "",
    };

    internal static string Text(LegacyNodeDraft d) => "NODE\n" + GpcInputComparison.ValueDocument(d.Node) + "\n" + Records(d.Records) + "\n" + Report(d.Report);
    internal static string Text(LegacyBeamDraft d) => "BEAM\n" + GpcInputComparison.ValueDocument(d.Beam) + "\n" + Bridge(d.Bridge) + "\nangle " + B(d.AngleDeg) + "\n" + Property(d.Property)
        + "\n" + Records(d.Records) + "\n" + Report(d.Report);
    internal static string Text(LegacyPlateDraft d) => "PLATE\n" + GpcInputComparison.ValueDocument(d.Plate) + "\ncorners " + string.Join(",", d.Corners.Select(c => B(c.X) + " " + B(c.Y) + " " + B(c.Z)))
        // P2X: the local axis angle of the plate, only when it has one (the texts of the plates without, the captures, are unchanged).
        + (d.AngleDeg != 0 ? "\nangle " + B(d.AngleDeg) : "")
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
            case GpcModels.Conversion c: { var (d, r) = GpcDraftArchive.Write(c); return (GpcDraftCapture.ConversionText(c), GpcDraftCapture.ConversionText(GpcDraftArchive.ReadConversion(d, r)), d.Length + r.Length); }
            case GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition c: { var d = GpcDraftArchive.Write(c); return (Text(c), Text(GpcDraftArchive.ReadComposite(d)), d.Length); }
            default: throw new ArgumentException("Not a draft: " + draft?.GetType().Name);
        }
    }

    internal sealed class Tally
    {
        internal readonly List<string> Failures = new List<string>();
        internal readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        internal long Bytes;
        /// <summary>R6.0: the capture session the checked data are recorded in (null: none), by the key given (or the label).</summary>
        internal GpcDraftCapture.Session Capture;
        internal void Count(string what, int n = 1) { Counts.TryGetValue(what, out int c); Counts[what] = c + n; }
        /// <summary>A datum the reader refuses: counted, and captured with its message.</summary>
        internal void Refused(string label, string kind, Exception x, string key = null)
        {
            Count(kind + " refused by the reader"); Capture?.Record(key ?? label, new GpcDraftCapture.Refused(kind, x.Message));
        }
        internal void Check(string label, object draft, string key = null)
        {
            string kind = draft.GetType().Name;
            try
            {
                var (before, after, bytes) = RoundTrip(draft); Bytes += bytes;
                if (before != after) { Count(kind + " different"); if (Failures.Count < 20) Failures.Add(label + ": " + kind + " round trip differs\n" + FirstDifference(before, after)); }
                else Count(kind);
            }
            catch (Exception e) { Count(kind + " error"); if (Failures.Count < 20) Failures.Add(label + ": " + kind + " " + e.GetType().Name + ": " + e.Message); }
            try { Capture?.Record(key ?? label, draft); }
            catch (Exception e) { Count(kind + " capture error"); if (Failures.Count < 20) Failures.Add(label + ": " + kind + " capture " + e.GetType().Name + ": " + e.Message); }
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

    /// <summary>
    /// R6.6 lane R3 (11/11): the corpus of the regression read by value from its bytes (<see cref="R3LegacyCorpus"/>: the captures, the captured
    /// examples, the frozen node, beam and plate data), with no FEM class: every element of the archive of the model as the GH types read it
    /// (<see cref="LegacyModelImport.NodeDraft"/>, <see cref="LegacyModelImport.BeamDraft"/>, <see cref="LegacyModelImport.PlateDraft"/>), every
    /// material, case, combination, stage, beam and plate property and section reachable from the model (by value, each instance once, at the
    /// place the FEM path found it first, the keys of the capture: <c>._elements[i]._beamProperty</c> and its <c>._section</c> and
    /// <c>._material</c>, <c>._elements[i]._property</c> and its <c>._material</c>, <c>._elements[i]._loads[j]._loadCase</c>,
    /// <c>._loadCombinations[i]</c>, <c>._stagedConstruction[i]</c> and its <c>._loadCombinations[j]</c>), and the model (the import of the
    /// reader); each through the final writer and reader, and recorded for the comparison with the capture. The identities of the FEM path
    /// (digests of FEM archives) come from the capture in compare mode (<see cref="R3CaptureIdentities"/>), else from the key.
    /// <para>The R6.1 primitives on every draft: <see cref="GpcDrafts.Copy(LegacyNodeDraft)"/> equal to the draft, and
    /// <see cref="GpcDrafts.Reidentify(LegacyNodeDraft, Guid)"/> of the copy equal to the draft read by value with that identity.</para>
    /// </summary>
    internal static void ByValue(Tally tally, R3LegacyCorpus.Item item, R3CaptureIdentities identities)
    {
        string id = item.Id; var root = item.Root; var signatures = item.Signatures;
        Guid Identity(string key, string what) => identities?.Of(key, Guid.Empty) is Guid g && g != Guid.Empty ? g : GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r3-corpus:" + key + ":" + what);
        static bool Refusal(Exception x) => x is ArgumentException || x is NotSupportedException || x is InvalidOperationException;
        var elements = root.Field("Elements").Items;
        int beams = 0;
        for (int q = 0; q < elements.Count; q++)
        {
            var e = elements[q]; string kind = e.ShortType, label = id + ":" + kind + " " + q;
            if (kind != "NodeModel" && kind != "BeamModel" && kind != "PlateModel") continue;
            var g = Identity(label, "draft");
            int beam = kind == "BeamModel" ? beams++ : -1;
            Func<Guid, object> read = kind switch
            {
                "NodeModel" => x => LegacyModelImport.NodeDraft(e, GpcBeamDrafts.DraftUnits, x),
                "BeamModel" => x => LegacyModelImport.BeamDraft(e, GpcBeamDrafts.DraftUnits, x, signatures?.Invoke(beam)),
                _ => x => LegacyModelImport.PlateDraft(e, GpcBeamDrafts.DraftUnits, x),
            };
            object draft;
            try { draft = read(g); }
            catch (Exception x) when (Refusal(x)) { tally.Refused(label, kind, x); continue; }
            tally.Check(label, draft);
            // R6.1 primitives, by value.
            var other = GpcIdentity.Derive(GpcIdentity.NameSpace, "reidentify:" + label);
            string original, copy, expected, got;
            switch (draft)
            {
                case LegacyNodeDraft d: { var c = GpcDrafts.Copy(d); original = Text(d); copy = Text(c); expected = Text((LegacyNodeDraft)read(other)); got = Text(GpcDrafts.Reidentify(c, other)); break; }
                case LegacyBeamDraft d: { var c = GpcDrafts.Copy(d); original = Text(d); copy = Text(c); expected = Text((LegacyBeamDraft)read(other)); got = Text(GpcDrafts.Reidentify(c, other)); break; }
                default: { var d = (LegacyPlateDraft)draft; var c = GpcDrafts.Copy(d); original = Text(d); copy = Text(c); expected = Text((LegacyPlateDraft)read(other)); got = Text(GpcDrafts.Reidentify(c, other)); break; }
            }
            if (copy != original) { tally.Count("draft copy different"); if (tally.Failures.Count < 20) tally.Failures.Add(label + ": GpcDrafts.Copy differs\n" + FirstDifference(original, copy)); }
            else if (got != expected) { tally.Count("draft re-identified different"); if (tally.Failures.Count < 20) tally.Failures.Add(label + ": GpcDrafts.Reidentify differs from the draft by value with that identity\n" + FirstDifference(expected, got)); }
            else tally.Count("draft copied and re-identified equal");
        }
        // The data of the GH types reachable from the model, each instance once at the first place the FEM path found it.
        var seen = new HashSet<LegacyValue>(ReferenceEqualityComparer.Instance);
        void Datum(string path, LegacyValue v, Func<LegacyValue, object> read)
        {
            if (v == null || v.IsNull || !seen.Add(v)) return;
            object draft;
            try { draft = read(v); }
            catch (Exception x) when (Refusal(x)) { tally.Refused(id + ":" + path, v.ShortType, x); return; }
            tally.Check(id + ":" + path, draft);
        }
        // Materials first (the FEM path walked them apart, before the other data).
        for (int i = 0; i < elements.Count; i++)
        {
            var e = elements[i];
            if (e.Is("BeamModel") && e.Has("BeamProperty")) Datum($"._elements[{i}]._beamProperty._material", e["BeamProperty"]["Material"], m => LegacyModelImport.Material(m));
            if (e.Is("PlateModel") && e.Has("PlateProperty")) Datum($"._elements[{i}]._property._material", e["PlateProperty"].Any("Material", "_material"), m => LegacyModelImport.Material(m));
        }
        for (int i = 0; i < elements.Count; i++)
        {
            var e = elements[i];
            if (e.Is("BeamModel") && e.Has("BeamProperty"))
            {
                var p = e["BeamProperty"];
                Datum($"._elements[{i}]._beamProperty", p, v => LegacyModelImport.BeamProperty(v, GpcBeamDrafts.DraftUnits));
                Datum($"._elements[{i}]._beamProperty._section", p["SectionModel"], LegacyModelImport.SectionDraft);
            }
            if (e.Is("PlateModel") && e.Has("PlateProperty")) Datum($"._elements[{i}]._property", e["PlateProperty"], v => LegacyModelImport.PlatePropertyDraft(v, GpcBeamDrafts.DraftUnits));
            if (e.Has("Loads")) { var loads = e.Field("Loads").Items; for (int j = 0; j < loads.Count; j++) Datum($"._elements[{i}]._loads[{j}]._loadCase", loads[j]["LoadCase"], LegacyModelImport.CaseDraft); }
        }
        if (root.Has("LoadCombinations")) { var c = root.Field("LoadCombinations").Items; for (int i = 0; i < c.Count; i++) Datum($"._loadCombinations[{i}]", c[i], LegacyModelImport.CombinationDraft); }
        if (root.Has("StagedConstruction"))
        {
            var stages = root.Field("StagedConstruction").Items;
            for (int i = 0; i < stages.Count; i++)
            {
                Datum($"._stagedConstruction[{i}]", stages[i], LegacyModelImport.StageDraft);
                var c = stages[i].Has("_loadCombinations") ? stages[i].Field("_loadCombinations").Items : Array.Empty<LegacyValue>();
                for (int j = 0; j < c.Count; j++) Datum($"._stagedConstruction[{i}]._loadCombinations[{j}]", c[j], LegacyModelImport.CombinationDraft);
            }
        }
        // The model: the import of the reader at the tolerance of the former corpus (1E-3), with the owner of the FEM path from the capture.
        try
        {
            var owner = identities?.Of(id + ":model", Guid.Empty) is Guid o && o != Guid.Empty ? o : GpcLegacyPayloads.ModelOwner(item.Bytes, 1e-3);
            var report = new LegacyImportReport();
            var model = LegacyModelImport.Import(root, owner, 1e-3, report, signatures);
            tally.Check(id + ":model", Conversion(model, report, 1e-3));
        }
        catch (Exception x) when (Refusal(x)) { tally.Count("model refused by the reader"); tally.Capture?.Record(id + ":model", new GpcDraftCapture.Refused("model", x.Message)); }
    }

    /// <summary>A conversion of the reader (the GPC input of a GH_Model) from its model and report: the constructor of the product is internal,
    /// public only through the FEM conversion (<c>GpcModels.Convert</c>) and the reader of the payloads, which gives no former signatures.</summary>
    private static GpcModels.Conversion Conversion(GPC.Model.Models.Model model, LegacyImportReport report, double tolerance) =>
        (GpcModels.Conversion)typeof(GpcModels.Conversion).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(GPC.Model.Models.Model), typeof(LegacyImportReport), typeof(double), typeof(GpcModelDrafts) }, null)
        .Invoke(new object[] { model, report, tolerance, null });

    internal static void Regression()
    {
        var capture = GpcDraftCapture.Open("corpus");
        var identities = R3CaptureIdentities.Open("corpus");
        var tally = new Tally { Capture = capture }; int models = 0, byValue = 0;
        // R3: the corpus read by value from its bytes (no FEM class).
        foreach (var item in R3LegacyCorpus.Captures().Concat(R3LegacyCorpus.Examples()).Concat(R3LegacyCorpus.Fixtures(duplicates: false))) { ByValue(tally, item, identities); models++; byValue++; }
        // P2CA (13/11/2026, U3): the examples 01–10 built in memory by the FEM catalogue (memory-*, no frozen payload holds them) are retired by
        // name with the catalogue (P2rRetired.Checks; their entries of the capture are retired sources, P2rRetired.CaptureSources).
        Console.WriteLine($"GPC DRAFT ARCHIVE: {byValue} models read by value from their bytes{(identities == null ? "" : $" ({identities.Count} identities of the FEM path from the capture)")}; RETIRED BY NAME (P2CA): the 10 models of the FEM catalogue");
        Console.WriteLine("GPC DRAFT ARCHIVE: " + tally.Summary + $"; {tally.Bytes / 1048576.0:0.0} MB written");
        if (tally.Failures.Count > 0) throw new Exception("GPC DRAFT ARCHIVE FAIL:\n" + string.Join("\n", tally.Failures));
        if (models < 100) throw new Exception("GPC DRAFT ARCHIVE FAIL: only " + models + " models");
        capture?.Dispose();
        if (GpcDraftCapture.Failed) throw new Exception("GPC DRAFT ARCHIVE FAIL: the data differ from the capture R6.0 (CAPTURE COMPARE above)");
        Console.WriteLine($"GPC DRAFT ARCHIVE PASS: {models} models, every draft written and read back bit for bit ({tally.Summary})");
    }

    /// <summary>(ii) <c>--legacy-payload-scan [files]</c>: every BGHA1 payload of every example definition (all of <c>example</c>, archives
    /// included, outputs folders excluded), from the raw archive.
    /// <para>R6.6 lane R3: the oracle that stays after P2 is the R6.0 capture <c>payloads</c>: in compare mode every payload is recorded by value
    /// (<see cref="GpcLegacyPayloads"/>, no FEM class) and compared with the datum the FEM path gave when the capture was written; the GH types
    /// read, write and read back by value (<see cref="GhCheck"/>). Transitional (P2 removes them with the FeMM models): the comparisons with the
    /// FEM path (<c>Same</c>, <c>Compare</c>: the value read by the FEM classes, the identity of the FEM value, the owner of the FEM path), the FEM
    /// value kept by the GH types and the drafts of a model against the conversion of its FEM values (<see cref="ModelDrafts"/>).</para></summary>
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
        // R6.0/R6.1: the capture of the payloads (source "payloads"), by location: written with the datum of the FEM path (the GH types of
        // 0.3.0 and of the branch before R6.1), compared with the datum by value as the GH types read it from R6.1 (GpcLegacyPayloads:
        // identity from the stored bytes, models without results, no former signature).
        var capture = GpcDraftCapture.Open("payloads");
        // P2CA (13/11/2026, U3): the identities of the FEM path from the capture (the owner of every captured conversion), as R3 reads them for
        // the corpus: the conversion of a model by value with the owner the capture was written with.
        var identities = R3CaptureIdentities.Open("payloads");
        // The datum by value of a payload (GpcLegacyPayloads, the GH types of 0.4.0), counted and recorded for the comparison with the capture.
        // The capture is frozen: the FEM path that wrote it is retired with the FEM types (P2rRetired.Checks).
        void Rec(string type, string where, Func<object> byValue)
        {
            if (capture != null && capture.Writing) throw new InvalidOperationException("LEGACY PAYLOAD SCAN: the capture payloads is frozen; the FEM path that wrote it is retired (P2CA)");
            object d;
            try { d = byValue() ?? new GpcDraftCapture.Nothing("model", "0.3.0 reads no model"); }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException || x is System.Runtime.Serialization.SerializationException)
            { d = new GpcDraftCapture.Refused("payload", x.Message); }
            Count(type + (d is GpcDraftCapture.Refused ? " by value refused" : d is GpcDraftCapture.Nothing ? " by value: no model" : " by value"));
            capture?.Record(where, d);
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
                        // P2CA: unreadable by value is a failure (the fallback to the 0.3.0 FEM reader is retired; none at the base).
                        Count("unreadable payload"); Fail(where + ": by value: " + e.Message);
                        continue;
                    }
                    string type = root.ShortType;
                    string family = label.Split(Path.DirectorySeparatorChar)[0];
                    families.TryGetValue(family, out var perFamily); families[family] = perFamily ??= new Dictionary<string, int>(StringComparer.Ordinal);
                    perFamily.TryGetValue(type, out int pf); perFamily[type] = pf + 1;
                    try
                    {
                        // P2CA (13/11/2026, U3): the datum by value of every payload against the R6.0 capture (the datum of the FEM path when it was
                        // written); the comparisons with the FEM path of today (the drafts of the FEM values, their identities and owners, the FEM
                        // value with results) are retired by name with the FEM types (P2rRetired.Checks).
                        switch (type)
                        {
                            case "NodeModel": Rec(type, where, () => GpcLegacyPayloads.Node(bytes)); break;
                            case "BeamModel": Rec(type, where, () => GpcLegacyPayloads.Beam(bytes)); break;
                            case "PlateModel": Rec(type, where, () => GpcLegacyPayloads.Plate(bytes)); break;
                            case "MaterialModel": Rec(type, where, () => GpcLegacyPayloads.Material(bytes)); break;
                            case "LoadCaseModel": Rec(type, where, () => GpcLegacyPayloads.Case(bytes)); break;
                            case "LoadCombinationModel": Rec(type, where, () => GpcLegacyPayloads.Combination(bytes)); break;
                            case "StagedConstructionModel": Rec(type, where, () => GpcLegacyPayloads.Stage(bytes)); break;
                            case "BeamPropertyModel": Rec(type, where, () => GpcLegacyPayloads.BeamProperty(bytes)); break;
                            case "PlatePropertyModel": Rec(type, where, () => GpcLegacyPayloads.PlateProperty(bytes)); break;
                            case "SectionModel": Rec(type, where, () => GpcLegacyPayloads.Section(bytes)); break;
                            case "CompositeBeamPropertyModel": Rec(type, where, () => GpcLegacyPayloads.Composite(bytes)); break;
                            case "ModelModel":
                            {
                                Guid owner = identities?.Of(where, Guid.Empty) ?? Guid.Empty;
                                Rec(type, where, () => GpcLegacyPayloads.Model(bytes, 1e-3, out _, owner == Guid.Empty ? null : owner));
                                break;
                            }
                            default: Count("other " + type); break;
                        }
                        if (item.Name == "Data" && GhTypes.ContainsKey(type)) GhCheck(type, where, (GH_IO.Serialization.GH_IReader)chunk, bytes, Count, Fail);
                    }
                    catch (Exception e) { Count(type + " error"); Fail(where + ": " + type + " " + e.GetType().Name + ": " + e.Message + " @ " + (e.StackTrace ?? "").Split(new[] { (char)10 }, 2)[0].Trim()); }
                }
                foreach (var c in chunk.Chunks) Walk(c, path + "/" + c.Name + (c.Index > 0 ? "[" + c.Index + "]" : ""));
            }
            Walk(archive.GetRootNode, "");
            Console.WriteLine($"LEGACY PAYLOAD SCAN {label}: {n} payloads");
        }
        Console.WriteLine("Payloads: " + string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + " " + c.Value)));
        foreach (var f in failures) Console.WriteLine("  " + f);
        foreach (var fam in families) Console.WriteLine("  family " + fam.Key + ": " + string.Join(", ", fam.Value.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + " " + c.Value)));
        Console.WriteLine($"LEGACY PAYLOAD SCAN TOTAL {files.Count} files ({unreadable} unreadable), {counts.Where(c => !c.Key.StartsWith("other ")).Sum(c => c.Key.Contains(" identity ") || c.Key.Contains(" with ") ? 0 : c.Value)} payloads compared or refused, {failureCount} differences or errors");
        capture?.Dispose();
        return failureCount == 0 && unreadable == 0 && !GpcDraftCapture.Failed ? 0 : 1;
    }

    /// <summary>R6.1: the GH type of every former payload type (GH_Plate has no persistence).</summary>
    private static readonly Dictionary<string, string> GhTypes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["NodeModel"] = "GH_Node", ["BeamModel"] = "GH_Beam", ["MaterialModel"] = "GH_Material", ["LoadCaseModel"] = "GH_LoadCase",
        ["LoadCombinationModel"] = "GH_LoadCombination", ["StagedConstructionModel"] = "GH_StagedConstruction", ["BeamPropertyModel"] = "GH_BeamProperty",
        ["PlatePropertyModel"] = "GH_PlateProperty", ["SectionModel"] = "GH_BeamSection", ["CompositeBeamPropertyModel"] = "GH_CompositeBeamProperty", ["ModelModel"] = "GH_Model",
    };
    private static Assembly grasshopper;

    /// <summary>The GPC datum of a GH type as text (the model: its GPC input with the report), or the refusal.</summary>
    private static string GhText(object goo)
    {
        try
        {
            if (goo.GetType().Name == "GH_Model")
                return goo.GetType().GetProperty("GpcInput").GetValue(goo) is GpcModels.Conversion c ? GpcDraftCapture.ConversionText(c) : "NO MODEL";
            return goo.GetType().GetProperty("Gpc").GetValue(goo) is object d ? GpcDraftCapture.Text(d) : "NO DATUM";
        }
        catch (TargetInvocationException t) when (t.InnerException is ArgumentException || t.InnerException is NotSupportedException || t.InnerException is InvalidOperationException
            || t.InnerException is System.Runtime.Serialization.SerializationException) { return "REFUSED " + t.InnerException.Message; }
    }

    /// <summary>
    /// R6.1: a former payload through its GH type: <c>Read</c> of the chunk Grasshopper gives it (by value, <see cref="GpcLegacyPayloads"/>),
    /// its datum equal to the datum by value computed here (models: at the tolerance of the plug-in settings); then <c>Write</c> (BGHGPC1 +
    /// records + the transitional FEM chunk) and <c>Read</c> into a new instance: the same datum, and the FEM value kept until R6.5.
    /// </summary>
    private static void GhCheck(string type, string where, GH_IO.Serialization.GH_IReader chunk, byte[] bytes, Action<string> count, Action<string> fail)
    {
        grasshopper ??= Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var t = grasshopper.GetTypes().Single(x => x.Name == GhTypes[type]);
        var goo = Activator.CreateInstance(t);
        bool read = (bool)t.GetMethod("Read").Invoke(goo, new object[] { chunk });
        string name = GhTypes[type];
        if (!read)
        {
            // 0.3.0 reads no model from some payloads (the GH_Model has no model): the same now.
            if (type == "ModelModel") { count(name + " read: no model (as 0.3.0)"); return; }
            count(name + " not read"); fail(where + ": " + name + " Read returned false"); return;
        }
        string byType = GhText(goo), byValue;
        try
        {
            if (type == "ModelModel")
            {
                double tolerance = (double)grasshopper.GetType("BridGHe.Grasshopper.Settings").GetProperty("Instance").GetValue(null).GetType().GetProperty("Tolerance")
                    .GetValue(grasshopper.GetType("BridGHe.Grasshopper.Settings").GetProperty("Instance").GetValue(null));
                var c = GpcLegacyPayloads.Model(bytes, tolerance, out _); byValue = c == null ? "NO MODEL" : GpcDraftCapture.ConversionText(c);
            }
            else byValue = GpcDraftCapture.Text(type switch
            {
                "NodeModel" => GpcLegacyPayloads.Node(bytes), "BeamModel" => GpcLegacyPayloads.Beam(bytes), "MaterialModel" => GpcLegacyPayloads.Material(bytes),
                "LoadCaseModel" => GpcLegacyPayloads.Case(bytes), "LoadCombinationModel" => GpcLegacyPayloads.Combination(bytes), "StagedConstructionModel" => GpcLegacyPayloads.Stage(bytes),
                "BeamPropertyModel" => GpcLegacyPayloads.BeamProperty(bytes), "PlatePropertyModel" => GpcLegacyPayloads.PlateProperty(bytes), "SectionModel" => GpcLegacyPayloads.Section(bytes),
                "CompositeBeamPropertyModel" => (object)GpcLegacyPayloads.Composite(bytes), _ => throw new ArgumentException(type),
            });
        }
        catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException || x is System.Runtime.Serialization.SerializationException) { byValue = "REFUSED " + x.Message; }
        if (byType != byValue) { count(name + " read differs from by value"); fail(where + ": " + name + " Read differs from the datum by value\n" + FirstDifference(byValue, byType)); return; }
        count(name + " read = by value");
        string drafts = type == "ModelModel" ? ModelDrafts(t, goo, where, count, fail) : null;
        if (byType.StartsWith("REFUSED", StringComparison.Ordinal) || byType.StartsWith("NO ", StringComparison.Ordinal)) return;
        // The final writer of the GH type and its reader.
        var written = new GH_IO.Serialization.GH_LooseChunk("Item");
        if (!(bool)t.GetMethod("Write", new[] { typeof(GH_IO.Serialization.GH_IWriter) }).Invoke(goo, new object[] { written })) { count(name + " not written"); fail(where + ": " + name + " Write returned false"); return; }
        if (!written.ItemExists("BridGHeRecords") && !(written.ItemExists("Data") && BridGHe.Common.Gpc.GpcGooArchive.IsGpc(written.GetByteArray("Data"))) && type != "CompositeBeamPropertyModel")
        { count(name + " written without the datum"); fail(where + ": " + name + " written without BGHGPC1 or records"); return; }
        var back = Activator.CreateInstance(t);
        if (!(bool)t.GetMethod("Read").Invoke(back, new object[] { written })) { count(name + " not read back"); fail(where + ": " + name + " written, not read back"); return; }
        string again = GhText(back);
        if (again != byType) { count(name + " round trip differs"); fail(where + ": " + name + " BGHGPC1 round trip differs\n" + FirstDifference(byType, again)); return; }
        count(name + " BGHGPC1 round trip equal");
        // R6.4 (2): the drafts of the elements saved with the model (chunk BridGHeDrafts) read back equal.
        if (drafts != null)
        {
            string again2 = t.GetProperty("GpcDrafts").GetValue(back) is GpcModelDrafts d ? DraftsText(d) : "NO DRAFTS";
            if (again2 != drafts) { count(name + " drafts round trip differs"); fail(where + ": " + name + " drafts round trip differs\n" + FirstDifference(drafts, again2)); return; }
            count(name + " drafts round trip equal");
        }
    }

    internal static string DraftsText(GpcModelDrafts d) => string.Join("\n--\n", d.Nodes.Select(x => GpcDraftCapture.Text(x)).Concat(d.Beams.Select(x => GpcDraftCapture.Text(x))).Concat(d.Plates.Select(x => GpcDraftCapture.Text(x))));

    /// <summary>R6.4 (2), extension (α): the drafts a model read from a former payload carries (by value, <see cref="GpcModelDrafts"/>); returns
    /// their text for the round trip (null: none). P2CA (13/11/2026, U3): their comparison with the conversion of every element of the FEM value
    /// of the payload (the DRAFT CHECK of Decompose Model) is retired by name with the FEM types (P2rRetired.Checks); the round trip stays.</summary>
    private static string ModelDrafts(Type t, object goo, string where, Action<string> count, Action<string> fail)
    {
        GpcModelDrafts drafts;
        try { drafts = t.GetProperty("GpcDrafts").GetValue(goo) as GpcModelDrafts; }
        catch (TargetInvocationException x) { count("GH_Model drafts refused"); fail(where + ": GH_Model drafts refused: " + x.InnerException?.Message); return null; }
        if (drafts == null) { count("GH_Model drafts: none"); return null; }
        return DraftsText(drafts);
    }
}
