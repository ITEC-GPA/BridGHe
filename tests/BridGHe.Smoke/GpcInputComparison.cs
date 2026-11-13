using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using GPC.Model.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

/// <summary>
/// F6 chain D: the GPC input a model transformer builds from the GPC input of its source (<see cref="GpcModelTransforms"/>) against the
/// conversion of its FEM output by the legacy reader, with the identity the transformed copy keeps (the model GUID of the source): analysis
/// fingerprint, the content fingerprint of every part (<see cref="GpcBridgeComparison.PartsDiffer"/>), and the XML document of the model
/// with only the random GUIDs of the GPC geometry values masked. Everything must be equal bit for bit.
/// </summary>
internal static class GpcInputComparison
{
    private static readonly System.Text.RegularExpressions.Regex RandomGuid = new System.Text.RegularExpressions.Regex("[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex AnyGuid = new System.Text.RegularExpressions.Regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Bridge Construction Sequence: the renumbered snapshot on GPC against the conversion of the FEM output with the identity of the
    /// snapshot (owner of the element identities and model GUID). The snapshot keeps the GUIDs of the elements of its source and the
    /// conversion derives them from the new numbers, so the elements correspond by family and number (and must have the same name and
    /// position): the report with the GUIDs mapped (numbers, BridGHe angles, native sections, property data, shadowed beams, element cases
    /// and warnings, warnings) and the XML document of the model with the element GUIDs mapped, bit for bit: numbering and every index.
    /// With <paramref name="source"/>: the element GUIDs are those of the source.</summary>
    internal static string RequireRenumbered(string label, GpcModels.Conversion attached, ModelModel fem, double tolerance, GpcModels.Conversion source = null)
    {
        void Fail(string what) => throw new Exception("GPC INPUT FAIL (" + label + "): " + what);
        var model = attached.Model; var owner = ElementOwner(model);
        if (owner == model.Guid) Fail("the transformed model has the model GUID of its source");
        var expected = GPC.Model.Core.ModelValues.Copy(Conversion(fem, tolerance, owner, out var report), GPC.Model.Core.ModelCopyOptions.None, model.Guid);
        if (model.NodesElements.Count != expected.NodesElements.Count || model.BeamElements.Count != expected.BeamElements.Count || model.AreaElements.Count != expected.AreaElements.Count)
            Fail($"element counts {model.NodesElements.Count}/{model.BeamElements.Count}/{model.AreaElements.Count} / {expected.NodesElements.Count}/{expected.BeamElements.Count}/{expected.AreaElements.Count}");
        var map = new Dictionary<Guid, Guid>();
        void Correspond(GPC.Model.Elements.Element a, GPC.Model.Elements.Element b, string what)
        {
            if (b == null || a.Name != b.Name) Fail($"{what} {a.Id}: '{a.Name}' / '{b?.Name}'");
            map.Add(a.Guid, b.Guid);
        }
        foreach (var n in model.NodesElements.Values) { expected.NodesElements.TryGetValue(n.Id, out var x); Correspond(n, x, "node"); if (n.Position.X != x.Position.X || n.Position.Y != x.Position.Y || n.Position.Z != x.Position.Z) Fail($"node {n.Id} position"); }
        foreach (var beam in model.BeamElements.Values) { expected.BeamElements.TryGetValue(beam.Id, out var x); Correspond(beam, x, "beam"); }
        foreach (var p in model.AreaElements.Values) { expected.AreaElements.TryGetValue(p.Id, out var x); Correspond(p, x, "plate"); }
        if (source != null && model.AllElements.Any(e => !source.Model.AllElements.Any(s => s.Guid == e.Guid))) Fail("an element GUID is not one of the source");
        RequireReport(label, attached.Report, report, g => map.TryGetValue(g, out var m) ? m : g);
        // L12: the bridge data, beam by beam in numbering order (the keys are the GUIDs of each model).
        RequireBridge(label, model, attached.Bridge, expected, report.Bridge, false);
        // The fingerprints complete the lazy values the graph writes (e.g. the outline of a section whose shear areas the conversion took from
        // another property with the same dimensions), as Require does before its document.
        model.AnalysisFingerprint(); expected.AnalysisFingerprint();
        string a = Document(model, map), b = Document(expected);
        if (a == null || b == null) Fail("XML document beyond the serializer limit");
        if (a != b)
        {
            string folder = Path.Combine(Path.GetTempPath(), "bridghe-gpc-input-diff"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, FileName(label) + "-gpc.xml"), a); File.WriteAllText(Path.Combine(folder, FileName(label) + "-conversion.xml"), b);
            var x = a.Split('\n'); var y = b.Split('\n');
            int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            Fail($"XML document differs, both in {folder}: " + (line < 0 ? $"{x.Length} / {y.Length} lines" : $"line {line + 1}: GPC '{x[line].Trim()}' / conversion '{y[line].Trim()}'"));
        }
        return $"{model.NodesElements.Count} nodes, {model.BeamElements.Count} beams, {model.AreaElements.Count} plates, {model.Costrains.Count} rigid links, {model.LoadCases.Count} cases, {model.Groups.Count} groups, {model.PhysicalMembers.Count} members, renumbered XML document equal ({a.Length} characters); " + RequireDrafts(label, attached.Drafts, fem);
    }

    /// <summary>The conversion of a FEM model with an explicit identity (the results are never converted: archived without them).</summary>
    internal static Model Conversion(ModelModel fem, double tolerance, Guid owner) => Conversion(fem, tolerance, owner, out _);

    internal static Model Conversion(ModelModel fem, double tolerance, Guid owner, out LegacyImportReport report)
    {
        if (fem.Elements.Any(e => e.Results.Count != 0)) { fem = new ModelModel(fem); foreach (var e in fem.Elements) e.Results.Clear(); }
        report = new LegacyImportReport();
        return LegacyModelImport.Import(LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(fem)), owner, tolerance, report, GpcModels.SectionStateSignatures(fem));
    }

    /// <summary>The report of a transformed input against the report of the conversion of the FEM output: what the analysis and the next
    /// transformers read (units, registered cases, case origins, BridGHe angles, native sections, beam property data, shadowed beams) and the
    /// warnings (as a multiset).</summary>
    /// <param name="guid">The element GUID of <paramref name="expected"/> for an element GUID of <paramref name="actual"/> (a renumbered
    /// snapshot keeps the GUIDs of its source; null: the same GUIDs).</param>
    internal static void RequireReport(string label, LegacyImportReport actual, LegacyImportReport expected, Func<Guid, Guid> guid = null)
    {
        void Fail(string what) => throw new Exception("GPC INPUT FAIL (" + label + "): report " + what);
        string Map<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> map, Func<TValue, string> text) =>
            string.Join("; ", map.Select(p => (p.Key is Guid g && guid != null ? guid(g).ToString() : p.Key.ToString()) + "=" + text(p.Value)).OrderBy(s => s, StringComparer.Ordinal));
        string Cases(LegacyImportReport r) => string.Join(" | ", r.ElementLoadCases.Select(c => c.Family + ":" + c.Number + ":" + string.Join(",", c.Cases)));
        if (Cases(actual) != Cases(expected)) Fail($"element cases '{Cases(actual)}' / '{Cases(expected)}'");
        string Warnings(LegacyImportReport r) => string.Join(" | ", r.ElementWarnings.Select(w => w.Family + ":" + w.Number + ":" + w.Restraint + ":" + w.Text(w.Number)).OrderBy(s => s, StringComparer.Ordinal));
        if (Warnings(actual) != Warnings(expected)) Fail($"element warnings '{Warnings(actual)}' / '{Warnings(expected)}'");
        if (actual.Units.Units != expected.Units.Units) Fail($"units {actual.Units.Units} / {expected.Units.Units}");
        if (actual.RegisteredCases != expected.RegisteredCases) Fail($"registered cases {actual.RegisteredCases} / {expected.RegisteredCases}");
        if (actual.Owner != expected.Owner) Fail($"owner {actual.Owner} / {expected.Owner}");
        string a = Map(actual.CaseOrigins, o => o.ToString()), b = Map(expected.CaseOrigins, o => o.ToString());
        if (a != b) Fail($"case origins '{a}' / '{b}'");
        a = Map(actual.BeamAngles, d => BitConverter.DoubleToInt64Bits(d).ToString("X16")); b = Map(expected.BeamAngles, d => BitConverter.DoubleToInt64Bits(d).ToString("X16"));
        if (a != b) Fail("BridGHe angles differ");
        a = Map(actual.NativeSections, s => s == null ? "null" : s.GetType().Name); b = Map(expected.NativeSections, s => s == null ? "null" : s.GetType().Name);
        if (a != b) Fail($"native sections '{a}' / '{b}'");
        string Property(LegacyBeamPropertyData d) => d.Formulation + "/" + BitConverter.DoubleToInt64Bits(d.Angle).ToString("X16") + "/" + (d.Native == null ? "-" : "native") + "/" + d.Signature;
        a = Map(actual.BeamProperties, Property); b = Map(expected.BeamProperties, Property);
        if (a != b) Fail($"beam properties '{a}' / '{b}'");
        a = string.Join(",", actual.ShadowedPropertyBeams.OrderBy(i => i)); b = string.Join(",", expected.ShadowedPropertyBeams.OrderBy(i => i));
        var expectedWarnings = expected.Warnings.ToList();
        if (a != b)
        {
            // R6.3, declared by name (ShadowRule): the registry of Build Model compares the GPC signatures of the drafts; beams whose property
            // has the name of a registered one and the same GPC property (the former content differed in data the GPC property does not hold)
            // are not shadowed and give no warning; the conversion of the FEM model compares the former content. The beam property data of
            // every name are the same (compared above), so the beams take the same property either way.
            var extra = expected.ShadowedPropertyBeams.Except(actual.ShadowedPropertyBeams).ToList();
            if (actual.ShadowedPropertyBeams.Except(expected.ShadowedPropertyBeams).Any() || extra.Count == 0) Fail($"shadowed beams '{a}' / '{b}'");
            var remaining = expectedWarnings.ToList();
            foreach (var w in actual.Warnings) remaining.Remove(w);
            if (remaining.Count == 0 || remaining.Count > extra.Count || remaining.Any(w => !System.Text.RegularExpressions.Regex.IsMatch(w, "^The beam property .* already exists$")))
                Fail($"shadowed beams '{a}' / '{b}' (warnings not those of the property registry)");
            expectedWarnings = expectedWarnings.Where(w => actual.Warnings.Contains(w)).ToList();
            lock (Shadowed) { Shadowed.TryGetValue(label, out int n); Shadowed[label] = n + extra.Count; }
        }
        a = string.Join(" | ", actual.Warnings.OrderBy(s => s, StringComparer.Ordinal)); b = string.Join(" | ", expectedWarnings.OrderBy(s => s, StringComparer.Ordinal));
        if (a != b) Fail($"warnings '{a}' / '{b}'");
    }

    /// <summary>R6.3 (ShadowRule): the beams not shadowed by Build Model where the conversion of the FEM model shadows them, by label.</summary>
    internal static readonly Dictionary<string, int> Shadowed = new Dictionary<string, int>(StringComparer.Ordinal);
    internal const string ShadowRule = "R6.3: the registry of Build Model compares the GPC signatures: same-name properties equal in GPC are one property, without the warning «already exists»";

    /// <summary>Prints the declared shadowing differences (one line), if any.</summary>
    internal static void PrintShadowed()
    {
        lock (Shadowed) if (Shadowed.Count > 0) Console.WriteLine($"DECLARED shadowing: {Shadowed.Values.Sum()} beams in {Shadowed.Count} models x {ShadowRule} ({string.Join(", ", Shadowed.Select(p => p.Key + " " + p.Value))})");
    }

    /// <summary>The XML document of a model in a canonical form (null beyond the 2,000,000 objects of the serializer): random (version 4)
    /// GUIDs masked; a reference of the DataContract graph (z:Ref) to a value (string, number) replaced by the value, object references
    /// renumbered by the order of the objects, value ids dropped: two equal graphs compare equal whether or not equal values are shared.</summary>
    internal static string Document(Model m, IReadOnlyDictionary<Guid, Guid> replace = null)
    {
        string text;
        try
        {
            using var stream = new MemoryStream(); GPC.Model.Persistence.ModelArchive.SaveDocument(m, stream);
            text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            // The element GUIDs of a renumbered snapshot written as those of the conversion they correspond to, before the masking.
            if (replace != null) text = AnyGuid.Replace(text, g => replace.TryGetValue(Guid.Parse(g.Value), out var to) ? to.ToString("D") : g.Value);
        }
        catch (System.Runtime.Serialization.SerializationException e) when (e.Message.Contains("Maximum number of items")) { return null; }
        return Canonical(text);
    }

    /// <summary>P2M (BGHGPC1 (i)): the document of one standalone GPC value (<see cref="GPC.Model.Persistence.ModelValueArchive"/>, root
    /// <typeparamref name="T"/>), canonical as <see cref="Document"/>.</summary>
    internal static string ValueDocument<T>(T value) where T : class
    {
        using var stream = new MemoryStream(); GPC.Model.Persistence.ModelValueArchive.Save(value, stream);
        return Canonical(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>A DataContract XML document with the random (version 4) GUIDs masked and the references linearised (see <see cref="Document"/>).</summary>
    internal static string Canonical(string text)
    {
        text = RandomGuid.Replace(text, "v4");
        System.Xml.Linq.XNamespace z = "http://schemas.microsoft.com/2003/10/Serialization/", xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var document = System.Xml.Linq.XDocument.Parse(text.TrimStart((char)0xFEFF));
        // Linear: a reference to a value without child elements (a string, a number) is replaced by the value; a reference to an object
        // keeps the reference, renumbered by the order of the objects in the document (values do not take a number).
        var elements = document.Descendants().ToArray();
        var targets = new Dictionary<string, System.Xml.Linq.XElement>(); var ordinals = new Dictionary<string, string>();
        foreach (var e in elements)
        {
            var id = e.Attribute(z + "Id"); if (id == null) continue;
            targets[id.Value] = e; if (e.HasElements) ordinals[id.Value] = "o" + ordinals.Count;
        }
        foreach (var e in elements)
        {
            var reference = e.Attribute(z + "Ref");
            if (reference != null)
            {
                var target = targets[reference.Value];
                if (target.HasElements) reference.Value = ordinals[reference.Value];
                else
                {
                    e.RemoveAttributes();
                    foreach (var a in target.Attributes().Where(a => a.Name != z + "Id" && !a.IsNamespaceDeclaration)) e.SetAttributeValue(a.Name, a.Value);
                    e.Value = target.Value;
                }
            }
            var id = e.Attribute(z + "Id");
            if (id != null) { if (ordinals.TryGetValue(id.Value, out var ordinal)) id.Value = ordinal; else id.Remove(); }
            foreach (var d in e.Attributes().Where(a => a.IsNamespaceDeclaration).ToArray()) d.Remove();
        }
        return document.ToString();
    }

    /// <summary>The owner of the element identities of a converted (or copied) model: the model revision of its element sources.</summary>
    internal static Guid ElementOwner(Model m) => Guid.Parse(m.AllElements.First().Source.ModelRevision);

    /// <param name="canonicalStates">The local section states compared as current or stale (<see cref="BridgeText"/>).</param>
    /// <param name="buildModel">Chain E (e3): the GPC input of Build Model, whose model GUID is the owner of its element identities (as the
    /// conversion of a FEM model): compared with the conversion of its FEM output with that owner, GUIDs included.</param>
    /// <summary>A label as a file name (P2K: a nickname with "|" made the writing of the difference fail).</summary>
    private static string FileName(string label) => string.Concat(label.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : c));

    internal static string Require(string label, GpcModels.Conversion attached, ModelModel fem, double tolerance, bool canonicalStates = false, bool buildModel = false)
    {
        void Fail(string what) => throw new Exception("GPC INPUT FAIL (" + label + "): " + what);
        var model = attached.Model; var owner = ElementOwner(model);
        if (!buildModel && owner == model.Guid) Fail("the transformed model has the model GUID of its source");
        if (buildModel && owner != model.Guid) Fail("the model GUID of Build Model is not the owner of its element identities");
        var converted = Conversion(fem, tolerance, owner, out var report);
        var expected = buildModel ? converted : GPC.Model.Core.ModelValues.Copy(converted, GPC.Model.Core.ModelCopyOptions.None, model.Guid);
        RequireReport(label, attached.Report, report);
        // L12: the bridge data (same beam GUIDs as the conversion with the same owner).
        RequireBridge(label, model, attached.Bridge, expected, report.Bridge, canonicalStates);
        var parts = GpcBridgeComparison.PartsDiffer(model, expected);
        if (model.AnalysisFingerprint() != expected.AnalysisFingerprint())
        {
            // The documents of both, for the difference (as for a document that differs).
            string folder0 = Path.Combine(Path.GetTempPath(), "bridghe-gpc-input-diff"); Directory.CreateDirectory(folder0);
            File.WriteAllText(Path.Combine(folder0, FileName(label) + "-gpc.xml"), Document(model) ?? ""); File.WriteAllText(Path.Combine(folder0, FileName(label) + "-conversion.xml"), Document(expected) ?? "");
            Fail("analysis fingerprint differs: " + string.Join("; ", parts.Take(12)) + "; documents in " + folder0);
        }
        if (parts.Count != 0) Fail("parts differ: " + string.Join("; ", parts.Take(12)) + (parts.Count > 12 ? $"; ... {parts.Count} in all" : ""));
        string a = Document(model), b = Document(expected);
        if (a != null && a != b)
        {
            string folder = Path.Combine(Path.GetTempPath(), "bridghe-gpc-input-diff"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, FileName(label) + "-gpc.xml"), a); File.WriteAllText(Path.Combine(folder, FileName(label) + "-conversion.xml"), b ?? "");
            var x = a.Split('\n'); var y = (b ?? "").Split('\n');
            int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            Fail($"XML document differs, both in {folder}: " + (line < 0 ? $"{x.Length} / {y.Length} lines" : $"line {line + 1}: GPC '{x[line].Trim()}' / conversion '{y[line].Trim()}'"));
        }
        return $"{model.NodesElements.Count} nodes, {model.BeamElements.Count} beams, {model.AreaElements.Count} plates, {model.LoadCases.Count} cases, {model.MovingLoads?.Paths.Count ?? 0} moving paths, parts equal, "
            + (a == null ? "XML document beyond the serializer limit" : $"XML document equal ({a.Length} characters)") + (buildModel ? "" : "; " + RequireDrafts(label, attached.Drafts, fem));
    }

    /// <summary>R6.4 (2), extension (α): the drafts of the elements a GPC input carries against the conversion of every element of its FEM
    /// model with the same identity (the identity of the conversion, as Decompose Model gives them), by the canonical texts of the capture.</summary>
    /// <summary>R6.4 (2): the conversion of a FEM model with the drafts of its elements (the conversion of every FEM value), the input of the
    /// transformers here, so that their outputs carry the drafts the comparisons check (<see cref="RequireDrafts"/>).</summary>
    internal static GpcModels.Conversion ConvertWithDrafts(ModelModel fem, double tolerance) => GpcModels.Convert(fem, tolerance).WithDrafts(FemmDrafts(fem));

    /// <summary>The drafts of the elements of a FEM model: the conversion of every FEM value (identity from its content).</summary>
    internal static GpcModelDrafts FemmDrafts(ModelModel fem) => new GpcModelDrafts(
        fem.Nodes.Select(n => GpcBeamDrafts.FromFemm(n)), fem.Beams.Select(b => GpcBeamDrafts.FromFemm(b)), fem.Plates.Select(p => GpcBeamDrafts.FromFemm(p)));

    internal static string RequireDrafts(string label, GpcModelDrafts drafts, ModelModel fem)
    {
        if (drafts == null) return "no drafts";
        if (!drafts.Fits(fem.Nodes.Count, fem.Beams.Count, fem.Plates.Count) || fem.Elements.Count != fem.Nodes.Count + fem.Beams.Count + fem.Plates.Count)
            return "drafts of a FEM model merged by ID (not compared)";
        void Same(string kind, int i, object draft, object conversion)
        {
            // PAR (A08): a stale section state compared as stale, as in the DRAFT CHECK (the draft of a beam whose property changed after Bridge
            // State Override carries the GPC fingerprint of its state, the conversion of the FEM value the FEM signature).
            string a, c;
            GpcDraftArchiveChecks.CompareStaleAsStale = true;
            try { a = GpcDraftCapture.Text(draft); c = GpcDraftCapture.Text(conversion); }
            finally { GpcDraftArchiveChecks.CompareStaleAsStale = false; }
            if (a != c) throw new Exception($"GPC INPUT FAIL ({label}): the draft of {kind} {i + 1} differs from the conversion of its FEM value\n" + GpcDraftArchiveChecks.FirstDifference(c, a));
        }
        for (int i = 0; i < fem.Nodes.Count; i++) { var id = GpcBeamDrafts.Identity(fem.Nodes[i]); Same("node", i, GpcDrafts.Reidentify(drafts.Nodes[i], id), GpcBeamDrafts.FromFemm(fem.Nodes[i], id)); }
        for (int i = 0; i < fem.Beams.Count; i++) { var id = GpcBeamDrafts.Identity(fem.Beams[i]); Same("beam", i, GpcDrafts.Reidentify(drafts.Beams[i], id), GpcBeamDrafts.FromFemm(fem.Beams[i], id)); }
        for (int i = 0; i < fem.Plates.Count; i++) { var id = GpcBeamDrafts.Identity(fem.Plates[i]); Same("plate", i, GpcDrafts.Reidentify(drafts.Plates[i], id), GpcBeamDrafts.FromFemm(fem.Plates[i], id)); }
        return $"drafts of {fem.Nodes.Count} nodes, {fem.Beams.Count} beams, {fem.Plates.Count} plates equal";
    }

    /// <summary>Regression (not native) <c>gpc-model-transforms</c>: Attach Moving Paths on the examples 01–10 (node, beam and plate loads,
    /// combinations, moving loads, stages): a new path on a new case, a path on an existing case, two paths, no path (removal); GPC = the
    /// conversion of the FEM output bit for bit, or the same error.</summary>
    internal static void Regression()
    {
        const double tolerance = 1e-6; int compared = 0;
        MovingPath Path(string caseName, double y) => new MovingPath
        {
            LoadCaseName = caseName, Divisions = 7, Target = 0, Coordinates = new double[] { 0, y, 0, 3000, y, 0, 0, y + 1000, 0 }, ConvertToStaticCases = true,
            Template = new MovingTemplate { Name = "Axle " + caseName, Vehicles = new[] { new MovingVehicle { Name = "1000 N", Loads = new MovingObject[] { new MovingPointForce { Force = new double[] { 0, 0, -1000 } } } } } },
        };
        // R6.6 lane R1: the comparison of expected and actual GPC data, without the FEM path: the transformers on the GPC examples of R0.
        var (expectedOutputs, expectedChecks) = ExpectedOnGpc(Path);
        Console.WriteLine($"GPC MODEL TRANSFORMS (R1, without FEM): {expectedOutputs} transformer outputs and refusals of the GPC examples 01-10 (R0) against their explicit expected GPC data ({expectedChecks} checks: paths, cases, units, element identities and positions, drafts, replaced properties, numbering, messages)");
        // P2R (T1/R5, 12/11): the comparison with the conversion of the FEM outputs is retired (the transformers give no FEM output since P2K/P2D);
        // the oracle is the explicit expected GPC data above (R1). P2CUT (13/11): the FEM loop (BRIDGHE_P2R_FEM_TRANSFORMS) is removed.
        Console.WriteLine("GPC MODEL TRANSFORMS PASS (R1): the explicit expected GPC data; the FEM comparison retired (P2R)");
    }

    /// <summary>R6.6 lane R1 (task 3): Attach Moving Paths, Bridge Phase Model and Bridge Construction Sequence on the GPC inputs of the examples 01–10
    /// built without FEM (<see cref="GpcNativeExamples"/>, R0), against the GPC data expected from their inputs, written here: the paths as given
    /// (case, template, shape, divisions, the static-case option), the cases of the source and of the paths, the units, the elements of the source
    /// one to one (GUID, name, position, property), the drafts carried; the replaced property on the named beams only, the others as they were; the
    /// snapshot renumbered 1..n with the GUIDs of the source, the active elements only; the refusals with their messages. Returns the outputs and
    /// refusals and the checks.</summary>
    private static (int Outputs, int Checks) ExpectedOnGpc(Func<string, double, MovingPath> path)
    {
        int outputs = 0, checks = 0;
        void Expect(bool ok, string what) { if (!ok) throw new Exception("GPC MODEL TRANSFORMS (R1) FAIL: " + what); checks++; }
        string Refusal(Func<GpcModels.Conversion> run) { try { run(); return null; } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { return e.Message; } }
        static bool At(GPC.Geometry.Point3d p, double x, double y, double z) => p.X == x && p.Y == y && p.Z == z;
        // The elements of the source one to one in the output (a transformer that keeps the elements: Attach Moving Paths, Bridge Phase Model).
        void SameElements(string label, Model source, Model output)
        {
            Expect(output.NodesElements.Count == source.NodesElements.Count && output.BeamElements.Count == source.BeamElements.Count && output.AreaElements.Count == source.AreaElements.Count,
                label + ": the element counts of the source");
            foreach (var n in source.NodesElements.Values)
                Expect(output.NodesElements.TryGetValue(n.Id, out var o) && o.Guid == n.Guid && o.Name == n.Name && o.Position.X == n.Position.X && o.Position.Y == n.Position.Y && o.Position.Z == n.Position.Z,
                    label + ": node " + n.Id + " as in the source");
            foreach (var b in source.BeamElements.Values)
                Expect(output.BeamElements.TryGetValue(b.Id, out var o) && o.Guid == b.Guid && o.Name == b.Name, label + ": beam " + b.Id + " as in the source");
            foreach (var p in source.AreaElements.Values)
                Expect(output.AreaElements.TryGetValue(p.Id, out var o) && o.Guid == p.Guid && o.Name == p.Name, label + ": plate " + p.Id + " as in the source");
        }
        foreach (var example in GpcNativeExamples.Create())
        {
            var source = example.Input; var m = source.Model; string id = example.Id;
            Expect(source.Drafts != null, id + ": the GPC input of Build Model carries the drafts");
            string existing = m.LoadCases.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault() ?? "Q";
            // Attach Moving Paths: a new case, an existing case, two paths, no path (removal).
            foreach (var (variant, paths) in new (string, MovingPath[])[] { ("new case", new[] { path("Traffic GPC", 0) }), ("existing case", new[] { path(existing, 100) }),
                ("two paths", new[] { path("Lane 1", 0), path("Lane 2", 200) }), ("no path", Array.Empty<MovingPath>()) })
            {
                string label = id + " paths " + variant;
                var output = GpcModelTransforms.ReplacePaths(source, paths, GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r1-paths:" + label));
                var o = output.Model; outputs++;
                Expect(output.Report.Units.Units == source.Report.Units.Units, label + ": the units of the source");
                Expect((o.MovingLoads?.Paths.Count ?? 0) == paths.Length, label + $": {paths.Length} paths ({o.MovingLoads?.Paths.Count ?? 0})");
                for (int i = 0; i < paths.Length; i++)
                {
                    var p = o.MovingLoads.Paths[i]; var given = paths[i]; double y = given.Coordinates[1];
                    Expect(o.LoadCases.TryGetValue(given.LoadCaseName, out var lc) && p.CaseGuid == lc.Guid, label + ": path " + i + " on its case " + given.LoadCaseName);
                    Expect(p.Template.Name == given.Template.Name && p.Template.Vehicles.Length == 1 && p.Divisions == 7 && p.Options?.ConvertToStaticCases == true, label + ": path " + i + " template, divisions, static cases");
                    Expect(At(p.Start, 0, y, 0) && At(p.End, 3000, y, 0) && At(p.Reference, 0, y + 1000, 0), label + ": path " + i + " start, end and reference (N-mm)");
                }
                foreach (var c in m.LoadCases.Keys) Expect(o.LoadCases.ContainsKey(c), label + ": the case " + c + " of the source");
                SameElements(label, m, o);
                Expect(output.Drafts != null && output.Drafts.Fits(m.NodesElements.Count, m.BeamElements.Count, m.AreaElements.Count), label + ": the drafts of the source carried");
            }
            if (m.BeamElements.Count == 0) continue;
            // Bridge Phase Model: the first beam on a stiffer property (loads cleared and kept), every beam; the refusals.
            var stiff = BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Beam("Stiff", 5, BridGHe.Common.Gpc.Builders.GpcSectionBuilders.Generic(60000, 450000000, 200000000, 100000000, 0, 0, 0, 0, 0, 0, 0),
                BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Name = "S355", Modulus = 210000 * 1.25, PoissonRatio = 0.3 }));
            string first = m.BeamElements[1].Name, signature = LegacyModelImport.PropertySignature(stiff.Property), former = LegacyModelImport.PropertySignature(m.BeamElements[1].BeamProperty);
            foreach (var (variant, names, clear) in new (string, string[], bool)[] { ("first beam, loads cleared", new[] { first }, true), ("first beam, loads kept", new[] { first }, false),
                ("every beam", m.BeamElements.Values.Select(b => b.Name).ToArray(), false) })
            {
                string label = id + " phase " + variant;
                var output = GpcModelTransforms.PhaseModel(source, "Phase", names, stiff, clear, GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r1-phase:" + label), stiff);
                var o = output.Model; outputs++;
                Expect(output.Report.Units.Units == source.Report.Units.Units, label + ": the units of the source");
                SameElements(label, m, o);
                foreach (var b in o.BeamElements.Values)
                {
                    bool replaced = names.Contains(b.Name);
                    string now = LegacyModelImport.PropertySignature(b.BeamProperty);
                    Expect(replaced ? b.BeamProperty.Name == "Stiff" && now == signature : now == LegacyModelImport.PropertySignature(m.BeamElements[b.Id].BeamProperty),
                        label + ": beam " + b.Name + (replaced ? " on the replacement «Stiff»" : " on its former property") + $" ({b.BeamProperty.Name})");
                }
                Expect(signature != former, label + ": the replacement is another property");
            }
            Expect(Refusal(() => GpcModelTransforms.PhaseModel(source, "Phase", new[] { "missing" }, stiff, true, GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r1-phase-missing:" + id), stiff)) == "Unknown Beam IDs: missing",
                id + ": phase of an unknown beam refused with «Unknown Beam IDs: missing»");
            Expect(Refusal(() => GpcModelTransforms.PhaseModel(source, " ", new[] { first }, stiff, true, GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r1-phase-name:" + id), stiff)) == "Phase model name is required.",
                id + ": phase without a name refused with «Phase model name is required.»");
            outputs += 2;
            // Bridge Construction Sequence: everything, the group «Permanent» (every element of R0's examples but the beam «Added» of 08 and 09), an
            // unknown group; renumbered 1..n with the GUIDs of the source.
            foreach (var (variant, groups) in new (string, string[])[] { ("all", null), ("group Permanent", new[] { "Permanent" }) })
            {
                string label = id + " construction " + variant;
                var output = GpcModelTransforms.ConstructionSequence(source, 10, "Phase " + variant, 7, groups, null, new[] { "*" }, null, GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r1-construction:" + label));
                var o = output.Model; outputs++;
                int beams = groups == null ? m.BeamElements.Count : m.BeamElements.Count - (id is "08-stage-activation" or "09-stage-removal" ? 1 : 0);
                Expect(o.BeamElements.Count == beams && o.AreaElements.Count == m.AreaElements.Count, label + $": {beams} beams, {m.AreaElements.Count} plates active ({o.BeamElements.Count}, {o.AreaElements.Count})");
                Expect(o.NodesElements.Keys.OrderBy(k => k).SequenceEqual(Enumerable.Range(1, o.NodesElements.Count)) && o.BeamElements.Keys.OrderBy(k => k).SequenceEqual(Enumerable.Range(1, o.BeamElements.Count)),
                    label + ": renumbered 1..n");
                Expect(o.AllElements.All(e => m.AllElements.Any(s => s.Guid == e.Guid && s.Name == e.Name)), label + ": every element with the GUID and name of one of the source");
                Expect(output.Report.Units.Units == source.Report.Units.Units, label + ": the units of the source");
            }
            Expect(Refusal(() => GpcModelTransforms.ConstructionSequence(source, 10, "Phase", 7, new[] { "Missing" }, null, new[] { "*" }, null, GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r1-construction-missing:" + id)))
                == "Unknown active group/physical member identifier.", id + ": an unknown group refused with «Unknown active group/physical member identifier.»");
            outputs++;
        }
        if (outputs < 90) throw new Exception("GPC MODEL TRANSFORMS (R1) FAIL: only " + outputs + " outputs");
        return (outputs, checks);
    }

    /// <summary>L12: the bridge data of a model as text, beam by beam in numbering order (member metadata with exact doubles, super element,
    /// local section state, composite section), and the keys that name no beam of the model. With <paramref name="canonicalStates"/> a state
    /// gives "current" or "stale" (its fingerprint equal or not to the D3 fingerprint of the property of the beam) instead of the fingerprint:
    /// the stale state of a transformer on GPC keeps the fingerprint of the former property, the conversion of the FEM output a stale marker;
    /// both are refused by the checks alike.</summary>
    internal static string BridgeText(Model model, GPC.Checkers.CompositeBridge.BridgeModelData data, bool canonicalStates)
    {
        string D(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");
        var lines = new List<string>();
        foreach (var beam in model.BeamElements.Values.OrderBy(b => b.Id))
        {
            var d = data.Beam(beam.Guid); if (d == null) continue;
            var m = d.Member; var s = d.SectionState;
            string member = m == null ? "-" : $"{m.PhysicalId}|{m.Family}|{m.Group}|{m.Segment}|{m.Span}|{D(m.StationFromMm)}|{D(m.StationToMm)}|{D(m.SectionUp.X)},{D(m.SectionUp.Y)},{D(m.SectionUp.Z)}|{m.GeometryId}";
            string state = s == null ? "-" : $"{s.Behavior}|{s.ConcreteExcluded}|{D(s.ModularRatio)}|{s.ClassificationCase}|" + (canonicalStates
                ? (s.PropertyFingerprint == GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.PropertyFingerprint(beam) ? "current" : "stale") : s.PropertyFingerprint);
            lines.Add($"{beam.Id}: member {member}; super element {d.SuperElement ?? "-"}; state {state}; composite {(d.CompositeSection == null ? "-" : "yes")}");
        }
        var beams = model.BeamElements.Values.Select(b => b.Guid).ToHashSet();
        lines.AddRange(data.Beams.Where(b => !beams.Contains(b.BeamGuid)).Select(b => "dangling " + b.BeamGuid));
        return string.Join("\n", lines);
    }

    /// <summary>The bridge data of two models equal (<see cref="BridgeText"/>).</summary>
    internal static void RequireBridge(string label, Model actual, GPC.Checkers.CompositeBridge.BridgeModelData actualData, Model expected, GPC.Checkers.CompositeBridge.BridgeModelData expectedData, bool canonicalStates)
    {
        string a = BridgeText(actual, actualData, canonicalStates), b = BridgeText(expected, expectedData, canonicalStates);
        if (a != b)
        {
            var x = a.Split('\n'); var y = b.Split('\n');
            int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            throw new Exception($"GPC INPUT FAIL ({label}): bridge data differ: " + (line < 0 ? $"{x.Length} / {y.Length} beams with data" : $"GPC '{x[line]}' / conversion '{y[line]}'"));
        }
    }

    /// <summary>Two transformed inputs equal bit for bit (analysis fingerprint, parts, XML document) with the same report, but for the
    /// signatures of the property data (former digest / GPC fingerprint) and the warnings of the conversion (only those of the transformers,
    /// "already exists", are compared).</summary>
    internal static string RequireSame(string label, GpcModels.Conversion actual, GpcModels.Conversion expected)
    {
        void Fail(string what) => throw new Exception("GPC INPUT FAIL (" + label + "): " + what);
        string Map<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> map, Func<TValue, string> text) =>
            string.Join("; ", map.Select(p => p.Key + "=" + text(p.Value)).OrderBy(s => s, StringComparer.Ordinal));
        LegacyImportReport a = actual.Report, b = expected.Report;
        if (a.Units.Units != b.Units.Units || a.RegisteredCases != b.RegisteredCases || a.Owner != b.Owner || actual.Tolerance != expected.Tolerance)
            Fail($"report: units {a.Units.Units}/{b.Units.Units}, registered cases {a.RegisteredCases}/{b.RegisteredCases}, owner {a.Owner}/{b.Owner}, tolerance {actual.Tolerance}/{expected.Tolerance}");
        foreach (var (what, x, y) in new[]
        {
            ("case origins", Map(a.CaseOrigins, o => o.ToString()), Map(b.CaseOrigins, o => o.ToString())),
            ("BridGHe angles", Map(a.BeamAngles, d => BitConverter.DoubleToInt64Bits(d).ToString("X16")), Map(b.BeamAngles, d => BitConverter.DoubleToInt64Bits(d).ToString("X16"))),
            ("native sections", Map(a.NativeSections, s => s == null ? "null" : s.GetType().Name), Map(b.NativeSections, s => s == null ? "null" : s.GetType().Name)),
            ("beam properties", Map(a.BeamProperties, d => d.Formulation + "/" + BitConverter.DoubleToInt64Bits(d.Angle).ToString("X16") + "/" + (d.Native == null ? "-" : "native")),
                Map(b.BeamProperties, d => d.Formulation + "/" + BitConverter.DoubleToInt64Bits(d.Angle).ToString("X16") + "/" + (d.Native == null ? "-" : "native"))),
            ("shadowed beams", string.Join(",", a.ShadowedPropertyBeams.OrderBy(i => i)), string.Join(",", b.ShadowedPropertyBeams.OrderBy(i => i))),
            ("transformer warnings", string.Join(" | ", a.Warnings.Where(w => w.EndsWith(" already exists", StringComparison.Ordinal)).OrderBy(s => s, StringComparer.Ordinal)),
                string.Join(" | ", b.Warnings.Where(w => w.EndsWith(" already exists", StringComparison.Ordinal)).OrderBy(s => s, StringComparer.Ordinal))),
        })
            if (x != y) Fail($"report {what} '{x}' / '{y}'");
        RequireBridge(label, actual.Model, actual.Bridge, expected.Model, expected.Bridge, false);
        var parts = GpcBridgeComparison.PartsDiffer(actual.Model, expected.Model);
        if (actual.Model.AnalysisFingerprint() != expected.Model.AnalysisFingerprint()) Fail("analysis fingerprint differs: " + string.Join("; ", parts.Take(12)));
        if (parts.Count != 0) Fail("parts differ: " + string.Join("; ", parts.Take(12)));
        string p = Document(actual.Model), q = Document(expected.Model);
        if (p != q) Fail("XML document differs");
        return $"{actual.Model.NodesElements.Count} nodes, {actual.Model.BeamElements.Count} beams, {actual.Model.LoadCases.Count} cases, XML document equal ({p?.Length ?? 0} characters)";
    }

    /// <summary>A FEM model with every case it refers to registered (element loads, combinations, stage increments, moving paths and the
    /// default "Dead"), so that its conversion has every case registered, as an input built in GPC (<see cref="GpcModels.Conversion.FromGpc"/>).</summary>
    private static ModelModel Registered(ModelModel source)
    {
        var fem = new ModelModel(source);
        var names = fem.LoadCases.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        void Register(LoadCaseModel c) { if (c != null && names.Add(c.Name)) fem.LoadCases.Add(c); }
        foreach (var load in fem.Elements.SelectMany(e => e.Loads)) Register(load.LoadCase);
        foreach (var combination in fem.LoadCombinations) foreach (var c in combination.Values.Keys) Register(c);
        foreach (var stage in fem.StagedConstruction) foreach (var combination in stage.LoadCombinations) foreach (var c in combination.Values.Keys) Register(c);
        foreach (var path in fem.MovingPaths) if (!names.Contains(path.LoadCaseName)) Register(new LoadCaseModel(path.LoadCaseName, path.LoadCaseName));
        if (!names.Contains("Dead")) Register(new LoadCaseModel(LoadCaseModel.Dead));
        return fem;
    }

}
