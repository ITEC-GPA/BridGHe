#if GPC_PILOT
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
            File.WriteAllText(Path.Combine(folder, label.Replace(' ', '-') + "-gpc.xml"), a); File.WriteAllText(Path.Combine(folder, label.Replace(' ', '-') + "-conversion.xml"), b);
            var x = a.Split('\n'); var y = b.Split('\n');
            int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            Fail($"XML document differs, both in {folder}: " + (line < 0 ? $"{x.Length} / {y.Length} lines" : $"line {line + 1}: GPC '{x[line].Trim()}' / conversion '{y[line].Trim()}'"));
        }
        return $"{model.NodesElements.Count} nodes, {model.BeamElements.Count} beams, {model.AreaElements.Count} plates, {model.Costrains.Count} rigid links, {model.LoadCases.Count} cases, {model.Groups.Count} groups, {model.PhysicalMembers.Count} members, renumbered XML document equal ({a.Length} characters)";
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
        if (a != b) Fail($"shadowed beams '{a}' / '{b}'");
        a = string.Join(" | ", actual.Warnings.OrderBy(s => s, StringComparer.Ordinal)); b = string.Join(" | ", expected.Warnings.OrderBy(s => s, StringComparer.Ordinal));
        if (a != b) Fail($"warnings '{a}' / '{b}'");
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
            text = RandomGuid.Replace(text, "v4");
        }
        catch (System.Runtime.Serialization.SerializationException e) when (e.Message.Contains("Maximum number of items")) { return null; }
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
    internal static string Require(string label, GpcModels.Conversion attached, ModelModel fem, double tolerance, bool canonicalStates = false)
    {
        void Fail(string what) => throw new Exception("GPC INPUT FAIL (" + label + "): " + what);
        var model = attached.Model; var owner = ElementOwner(model);
        if (owner == model.Guid) Fail("the transformed model has the model GUID of its source");
        var expected = GPC.Model.Core.ModelValues.Copy(Conversion(fem, tolerance, owner, out var report), GPC.Model.Core.ModelCopyOptions.None, model.Guid);
        RequireReport(label, attached.Report, report);
        // L12: the bridge data (same beam GUIDs as the conversion with the same owner).
        RequireBridge(label, model, attached.Bridge, expected, report.Bridge, canonicalStates);
        var parts = GpcBridgeComparison.PartsDiffer(model, expected);
        if (model.AnalysisFingerprint() != expected.AnalysisFingerprint()) Fail("analysis fingerprint differs: " + string.Join("; ", parts.Take(12)));
        if (parts.Count != 0) Fail("parts differ: " + string.Join("; ", parts.Take(12)) + (parts.Count > 12 ? $"; ... {parts.Count} in all" : ""));
        string a = Document(model), b = Document(expected);
        if (a != null && a != b)
        {
            string folder = Path.Combine(Path.GetTempPath(), "bridghe-gpc-input-diff"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, label.Replace(' ', '-') + "-gpc.xml"), a); File.WriteAllText(Path.Combine(folder, label.Replace(' ', '-') + "-conversion.xml"), b ?? "");
            var x = a.Split('\n'); var y = (b ?? "").Split('\n');
            int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            Fail($"XML document differs, both in {folder}: " + (line < 0 ? $"{x.Length} / {y.Length} lines" : $"line {line + 1}: GPC '{x[line].Trim()}' / conversion '{y[line].Trim()}'"));
        }
        return $"{model.NodesElements.Count} nodes, {model.BeamElements.Count} beams, {model.AreaElements.Count} plates, {model.LoadCases.Count} cases, {model.MovingLoads?.Paths.Count ?? 0} moving paths, parts equal, "
            + (a == null ? "XML document beyond the serializer limit" : $"XML document equal ({a.Length} characters)");
    }

    /// <summary>Native runners: every model transformer of the definition attached its GPC input, equal to the conversion of its FEM output.</summary>
    internal static void RequireComponents(GH_Document doc, string label)
    {
        int count = 0;
        foreach (var component in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name is "AttachMovingPathsComponent" or "BridgePhaseModelComponent" or "BridgeConstructionSequenceComponent"))
            foreach (var goo in component.Params.Output[0].VolatileData.AllData(true))
            {
                string name = label + " " + component.GetType().Name.Replace("Component", "") + " '" + component.NickName + "'";
                if (!(bool)goo.GetType().GetProperty("HasAttachedInput").GetValue(goo)) throw new Exception("GPC INPUT FAIL (" + name + "): no GPC input attached by the transformer");
                var attached = (GpcModels.Conversion)goo.GetType().GetProperty("GpcInput").GetValue(goo);
                var fem = (ModelModel)goo.GetType().GetProperty("Value").GetValue(goo);
                // The tolerance of the attached input: GpcInput returns it only at the current tolerance of the plug-in.
                Console.WriteLine($"GPC INPUT PASS ({name}): " + (component.GetType().Name == "BridgeConstructionSequenceComponent"
                    ? RequireRenumbered(name, attached, fem, attached.Tolerance) : Require(name, attached, fem, attached.Tolerance))); count++;
            }
        Console.WriteLine($"GPC INPUT ({label}): {count} transformer outputs on GPC equal to the conversion of their FEM output.");
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
        foreach (var example in NativeExamples.Create())
        {
            var fem = example.Model;
            string existing = fem.LoadCases.FirstOrDefault()?.Name ?? "Q";
            foreach (var (variant, paths) in new (string, MovingPath[])[] { ("new case", new[] { Path("Traffic GPC", 0) }), ("existing case", new[] { Path(existing, 100) }),
                ("two paths", new[] { Path("Lane 1", 0), Path("Lane 2", 200) }), ("no path", Array.Empty<MovingPath>()) })
            {
                string label = example.Id + " " + variant; string femError = null, gpcError = null; ModelModel output = null; GpcModels.Conversion gpc = null;
                try { output = MovingModelBuilder.ReplacePaths(fem, paths); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { femError = e.Message; }
                try { gpc = GpcModelTransforms.ReplacePaths(GpcModels.Convert(fem, tolerance), paths, GpcIdentity.Derive(GpcIdentity.NameSpace, "test-paths:" + label)); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { gpcError = e.Message; }
                if (femError != null || gpcError != null)
                {
                    if (femError != gpcError) throw new Exception($"GPC INPUT FAIL ({label}): FEM error '{femError}', GPC error '{gpcError}'");
                    Console.WriteLine($"GPC INPUT ({label}): same error: {femError}"); continue;
                }
                Console.WriteLine($"GPC INPUT ({label}): " + Require(label, gpc, output, tolerance)); compared++;
            }
        }
        if (compared < 36) throw new Exception($"GPC INPUT FAIL: only {compared} transformations compared");
        int phases = PhaseModels(tolerance, Path);
        int components = PhaseModelComponent(tolerance);
        int fromGpc = FromGpcRoute(tolerance, Path);
        int states = PhaseModelSectionStates(tolerance);
        int sequences = ConstructionSequences(tolerance, Path);
        Console.WriteLine($"GPC MODEL TRANSFORMS PASS: {compared} Attach Moving Paths and {phases} Bridge Phase Model outputs (chains included), {components} Bridge Phase Model component outputs on GPC equal to the conversion of their FEM output; {fromGpc} transformer outputs from inputs built in GPC (GpcModels.Conversion.FromGpc) equal to those from the conversion; {states} Bridge Phase Model outputs on beams with local section states; {sequences} Bridge Construction Sequence snapshots renumbered as the conversion of their FEM output.");
    }

    /// <summary>Bridge Construction Sequence (coordinator decision 2, 5/11: renumbered 1..n): the GPC snapshot against the conversion of the
    /// FEM snapshot (<see cref="RequireRenumbered"/>), or the same error. Sources: the examples 01–10 (half of the beams and plates in a group
    /// "PhaseA", the rest in "PhaseB": all, no bearings, group A with and without bearings, group B, a rigid link from an inactive node onto an
    /// active one, chained after Attach Moving Paths and before Bridge Phase Model) and the simple bridge of the construction tests (girders of
    /// Bridge Member Assignment, groups Girder\GroupN\SegmentM, restrained abutment: a group with the girder override and no bearings, a parent
    /// group, a physical member, everything with the override, unknown identifiers, override without members).</summary>
    private static int ConstructionSequences(double tolerance, Func<string, double, MovingPath> path)
    {
        int compared = 0;
        void Run(string label, ModelModel fem, Func<ModelModel> femSnapshot, Func<GpcModels.Conversion, GpcModels.Conversion> gpcSnapshot)
        {
            string femError = null, gpcError = null; ModelModel output = null; GpcModels.Conversion gpc = null; var source = GpcModels.Convert(fem, tolerance);
            try { output = femSnapshot(); } catch (ArgumentException e) { femError = e.Message; }
            try { gpc = gpcSnapshot(source); } catch (ArgumentException e) { gpcError = e.Message; }
            if (femError != null || gpcError != null)
            {
                if (femError != gpcError) throw new Exception($"GPC INPUT FAIL ({label}): FEM error '{femError}', GPC error '{gpcError}'");
                Console.WriteLine($"GPC INPUT ({label}): same error: {femError}"); return;
            }
            Console.WriteLine($"GPC INPUT ({label}): " + RequireRenumbered(label, gpc, output, tolerance, source)); compared++;
        }
        BridgePhaseDefinition Phase(int order, string name) => new BridgePhaseDefinition { Order = order, Name = name, Day = 7 };
        GpcModels.Conversion Snapshot(GpcModels.Conversion input, BridgePhaseDefinition phase, string[] groups, string[] ids, string[] bearings, BeamPropertyModel property, string key) =>
            GpcModelTransforms.ConstructionSequence(input, phase.Order, phase.Name, phase.Day, groups, ids, bearings, property == null ? null : GpcModels.BeamProperty(property, input.Report.Units),
                GpcIdentity.Derive(GpcIdentity.NameSpace, "test-construction:" + key));
        ModelModel Fem(ModelModel input, BridgePhaseDefinition phase, string[] groups, string[] ids, string[] bearings, BeamPropertyModel property) =>
            BridgeConstructionSequence.Create(input, phase, groups, ids, bearings, property, null).Model;

        foreach (var example in NativeExamples.Create())
        {
            var fem = new ModelModel(example.Model);
            var structure = fem.Elements.Where(e => e is BeamModel || e is PlateModel).ToList();
            for (int k = 0; k < structure.Count; k++) { structure[k].Groups.Clear(); structure[k].Groups.Add(new GroupModel(k < (structure.Count + 1) / 2 ? "PhaseA" : "PhaseB")); }
            var supported = fem.Nodes.Where(n => n.Attributes.Any(a => a is NodeRestraintAttributeModel || a is NodeStiffnessAttributeModel)).Select(n => n.NodeId).ToArray();
            var variants = new List<(string, string[], string[])> { ("all", null, new[] { "*" }), ("no bearings", null, Array.Empty<string>()), ("group A", new[] { "PhaseA" }, new[] { "*" }),
                ("group A no bearings", new[] { "PhaseA" }, Array.Empty<string>()), ("group B", new[] { "PhaseB" }, new[] { "*" }), ("unknown group", new[] { "Missing" }, new[] { "*" }) };
            if (supported.Length != 0 && supported.All(id => !string.IsNullOrEmpty(id))) variants.Add(("group A first bearing", new[] { "PhaseA" }, new[] { supported[0] }));
            foreach (var (variant, groups, bearings) in variants)
            {
                var phase = Phase(10, "Phase " + variant); string label = example.Id + " construction " + variant;
                Run(label, fem, () => Fem(fem, phase, groups, null, bearings, null), input => Snapshot(input, phase, groups, null, bearings, null, label));
            }
            // A rigid link from the last node (inactive with group A when it is not on an active element) onto the first node.
            if (fem.Nodes.Count >= 2)
            {
                var linked = new ModelModel(fem); var last = linked.Nodes[linked.Nodes.Count - 1];
                last.Attributes.Add(new RigidConnectionAttributeModel(linked.Nodes[0].Position));
                var phase = Phase(20, "Linked"); string label = example.Id + " construction rigid link";
                Run(label, linked, () => Fem(linked, phase, new[] { "PhaseA" }, null, new[] { "*" }, null), input => Snapshot(input, phase, new[] { "PhaseA" }, null, new[] { "*" }, null, label));
            }
            // Chains: Attach Moving Paths, then the snapshot, then Bridge Phase Model on the snapshot (beams with IDs).
            {
                var named = new ModelModel(fem); for (int k = 0; k < named.Beams.Count; k++) if (string.IsNullOrWhiteSpace(named.Beams[k].BeamId)) named.Beams[k].BeamId = "B" + (k + 1);
                var moving = new[] { path("Traffic GPC", 0) }; var phase = Phase(30, "Chain"); string label = example.Id + " construction chain";
                var stiffer = named.Beams.Count == 0 ? null : BridGHe.Common.Helpers.ModelArchive.Clone(named.Beams[0].BeamProperty);
                if (stiffer != null) { stiffer.Name = "Chain phase"; stiffer.Material.Modulus *= 1.5; }
                Run(label, named, () =>
                {
                    var snapshot = Fem(MovingModelBuilder.ReplacePaths(named, moving), phase, new[] { "PhaseA" }, null, new[] { "*" }, null);
                    return stiffer == null || snapshot.Beams.Count == 0 ? snapshot : BridGHe.Common.Diagnostics.BridgePhaseModel.Create(snapshot, "Chain phase", new[] { snapshot.Beams[0].BeamId }, stiffer, false);
                }, input =>
                {
                    var paths = GpcModelTransforms.ReplacePaths(input, moving, GpcIdentity.Derive(GpcIdentity.NameSpace, "test-construction-paths:" + label));
                    var snapshot = Snapshot(paths, phase, new[] { "PhaseA" }, null, new[] { "*" }, null, label);
                    return stiffer == null || snapshot.Model.BeamElements.Count == 0 ? snapshot : GpcModelTransforms.PhaseModel(snapshot, "Chain phase", new[] { snapshot.Model.BeamElements[1].Name },
                        GpcModels.BeamProperty(stiffer, snapshot.Report.Units), false, GpcIdentity.Derive(GpcIdentity.NameSpace, "test-construction-phase:" + label));
                });
            }
        }

        // The simple bridge of the construction tests: girders with physical members, nested groups, restrained abutment.
        var bridge = BridgeConstructionTests.Model(SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()));
        var composite = BridgeAssignmentTests.Property("Composite phase"); composite.Material.Modulus *= 1.2;
        foreach (var (variant, groups, ids, bearings, property) in new (string, string[], string[], string[], BeamPropertyModel)[]
        {
            ("group override", new[] { "Girder\\Group1" }, null, Array.Empty<string>(), composite), ("parent group", new[] { "Girder" }, null, new[] { "*" }, null),
            ("physical member", null, new[] { "Girder:2:1" }, new[] { "*" }, null), ("all override", null, null, new[] { "*" }, composite),
            ("group and member", new[] { "Girder\\Group1" }, new[] { "Girder:2:1" }, new[] { "*" }, null), ("unknown member", null, new[] { "Girder:9:9" }, new[] { "*" }, null),
        })
        {
            var phase = Phase(2, "Bridge " + variant); string label = "simple bridge construction " + variant;
            Run(label, bridge, () => Fem(bridge, phase, groups, ids, bearings, property), input => Snapshot(input, phase, groups, ids, bearings, property, label));
        }
        if (compared < 70) throw new Exception($"GPC INPUT FAIL: only {compared} Bridge Construction Sequence snapshots compared");
        return compared;
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

    /// <summary>Coordinator decision 3 (5/11): Bridge Phase Model on beams with local section states runs on GPC (no conversion of the FEM
    /// output). On the examples 01–10 with two beams or more, a current state on the even beams and a stale one on the odd beams (as Bridge
    /// State Override writes them): a new property on half of the beams with and without "Clear loads", and every beam on a generic section;
    /// the states equal to those of the conversion of the FEM output (current where the property did not change, stale on the replaced beams)
    /// and the models bit for bit otherwise. The listed difference (a replacement equal in the former signature only) is checked to be the
    /// only one: the same property under another name keeps the states current in the FEM path, not on GPC.</summary>
    private static int PhaseModelSectionStates(double tolerance)
    {
        int compared = 0;
        foreach (var example in NativeExamples.Create())
        {
            if (example.Model.Beams.Count < 2) continue;
            var fem = new ModelModel(example.Model);
            for (int k = 0; k < fem.Beams.Count; k++)
            {
                var beam = fem.Beams[k];
                if (string.IsNullOrWhiteSpace(beam.BeamId)) beam.BeamId = "B" + (k + 1);
                beam.Attributes.Add(new BridgeSectionStateAttributeModel(new BridgeSectionAssignment(BridGHe.Common.Checks.BridgeActionBehavior.LongTerm, k % 3 == 0, 15.5 + k,
                    k % 2 == 0 ? null : "Q", k % 2 == 0 ? BridgeSectionAssignment.Signature(beam.BeamProperty) : "former signature of another property")));
            }
            var ids = fem.Beams.Select(b => b.BeamId).Distinct().ToArray(); var half = ids.Take(Math.Max(1, ids.Length / 2)).ToArray();
            var first = fem.Beams[0].BeamProperty;
            var stiffer = BridGHe.Common.Helpers.ModelArchive.Clone(first); stiffer.Name = "Phase states"; stiffer.Material.Modulus *= 1.5;
            var generic = new BeamPropertyModel("Generic states")
            {
                Name = "Generic states", Material = BridGHe.Common.Helpers.ModelArchive.Clone(first.Material), PropertyType = BeamPropertyType.Beam,
                Section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 1.2e4, I11 = 3.1e8, I22 = 4.7e7, J = 1.3e7, ShearA1 = 8e3, ShearA2 = 6e3, AngleX1Rad = 0.3 },
            };
            foreach (var (variant, selection, property, clear) in new (string, string[], BeamPropertyModel, bool)[] { ("half", half, stiffer, false), ("half clear", half, stiffer, true), ("all generic", ids, generic, false) })
            {
                string label = example.Id + " states " + variant;
                var output = BridGHe.Common.Diagnostics.BridgePhaseModel.Create(fem, "Phase states", selection, property, clear);
                var input = GpcModels.Convert(fem, tolerance);
                var gpc = GpcModelTransforms.PhaseModel(input, "Phase states", selection, GpcModels.BeamProperty(property, input.Report.Units), clear, GpcIdentity.Derive(GpcIdentity.NameSpace, "test-states:" + label));
                int stale = gpc.Model.BeamElements.Values.Count(b => gpc.Bridge.SectionState(b.Guid) is var s && s != null && s.PropertyFingerprint != GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.PropertyFingerprint(b));
                Console.WriteLine($"GPC INPUT ({label}, {stale} stale states): " + Require(label, gpc, output, tolerance, canonicalStates: true)); compared++;
            }
            // The listed difference: the first property under another name (equal former signature) on the beams that have it.
            var renamed = BridGHe.Common.Helpers.ModelArchive.Clone(first); renamed.Name = "Same values";
            var same = fem.Beams.Where(b => b.BeamProperty.Name == first.Name).Select(b => b.BeamId).Distinct().ToArray();
            var femStates = GpcModels.Convert(BridGHe.Common.Diagnostics.BridgePhaseModel.Create(fem, "Phase same", same, renamed, false), tolerance);
            var source = GpcModels.Convert(fem, tolerance);
            var gpcStates = GpcModelTransforms.PhaseModel(source, "Phase same", same, GpcModels.BeamProperty(renamed, source.Report.Units), false, GpcIdentity.Derive(GpcIdentity.NameSpace, "test-states-same:" + example.Id));
            string State(GpcModels.Conversion c, int id)
            {
                var beam = c.Model.BeamElements[id]; var s = c.Bridge.SectionState(beam.Guid);
                return s == null ? "-" : s.PropertyFingerprint == GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.PropertyFingerprint(beam) ? "current" : "stale";
            }
            var differ = femStates.Model.BeamElements.Values.Where(b => State(femStates, b.Id) != State(gpcStates, b.Id)).Select(b => b.Name).ToArray();
            var expected = fem.Beams.Where((b, k) => k % 2 == 0 && same.Contains(b.BeamId)).Select(b => b.BeamId).ToArray();
            if (!differ.OrderBy(s => s).SequenceEqual(expected.OrderBy(s => s)) || differ.Any(n => State(femStates, femStates.Model.BeamElements.Values.First(b => b.Name == n).Id) != "current"))
                throw new Exception($"GPC INPUT FAIL ({example.Id} states same values): states differ on [{string.Join(", ", differ)}], expected the current states of the renamed beams [{string.Join(", ", expected)}]");
            Console.WriteLine($"GPC INPUT ({example.Id} states same values): listed difference only, {differ.Length} states current in the FEM path, stale on GPC");
        }
        if (compared < 12) throw new Exception($"GPC INPUT FAIL: only {compared} Bridge Phase Model outputs with section states compared");
        return compared;
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

    /// <summary>The public input of the transformers for a model built in GPC (step 32, for P2T): on the examples 01–10 with every case
    /// registered, <see cref="GpcModels.Conversion.FromGpc"/> on a copy of the converted model (units, BridGHe angles and native sections of
    /// the conversion) gives the report of the conversion (property signatures apart), and Bridge Phase Model (new property with and without
    /// "Clear loads", generic section with a principal angle, replacement with the name of the property of the first beam), Attach Moving
    /// Paths (new and existing case) and the chain Phase Model → Attach Moving Paths on it, with the replacement property built in GPC
    /// (<see cref="GpcModels.BeamProperty(GPC.Model.ElementProperties.BeamProperty, GPC.Model.Structure.Assignments.BeamFormulation, GPC.Converter.Straus7.Straus7SectionDefinition)"/>),
    /// give the outputs of the same transformers on the conversion bit for bit (or the same error).</summary>
    private static int FromGpcRoute(double tolerance, Func<string, double, MovingPath> path)
    {
        int compared = 0;
        foreach (var example in NativeExamples.Create())
        {
            var fem = Registered(example.Model);
            for (int k = 0; k < fem.Beams.Count; k++) if (string.IsNullOrWhiteSpace(fem.Beams[k].BeamId)) fem.Beams[k].BeamId = "B" + (k + 1);
            var conversion = GpcModels.Convert(fem, tolerance);
            if (conversion.Report.RegisteredCases != conversion.Model.LoadCases.Count) throw new Exception($"GPC INPUT FAIL ({example.Id} from GPC): not every case registered");
            GpcModels.Conversion Input() => GpcModels.Conversion.FromGpc(GPC.Model.Core.ModelValues.Copy(conversion.Model), conversion.Report.Units, conversion.Report.BeamAngles, tolerance, conversion.Report.NativeSections, conversion.Bridge);
            Console.WriteLine($"GPC INPUT ({example.Id} from GPC): " + RequireSame(example.Id + " from GPC", Input(), conversion)); compared++;
            var ids = fem.Beams.Select(b => b.BeamId).Distinct().ToArray(); var half = ids.Take(Math.Max(1, ids.Length / 2)).ToArray();
            var variants = new List<(string Label, Func<GpcModels.Conversion, bool, Guid, GpcModels.Conversion> Transform)>
            {
                ("paths new case", (input, _, id) => GpcModelTransforms.ReplacePaths(input, new[] { path("Traffic GPC", 0) }, id)),
                ("paths existing case", (input, _, id) => GpcModelTransforms.ReplacePaths(input, new[] { path(fem.LoadCases[0].Name, 100) }, id)),
            };
            if (fem.Beams.Count != 0)
            {
                var first = fem.Beams[0].BeamProperty;
                BeamPropertyModel Variant(string name, Action<BeamPropertyModel> change) { var p = BridGHe.Common.Helpers.ModelArchive.Clone(first); p.Name = name; change(p); return p; }
                var generic = new BeamPropertyModel("Generic phase")
                {
                    Name = "Generic phase", Material = BridGHe.Common.Helpers.ModelArchive.Clone(first.Material), PropertyType = BeamPropertyType.Beam,
                    Section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 1.2e4, I11 = 3.1e8, I22 = 4.7e7, J = 1.3e7, ShearA1 = 8e3, ShearA2 = 6e3, AngleX1Rad = 0.3 },
                };
                // The replacement: as the components give it (a former property converted), or built in GPC with its GPC fingerprint.
                LegacyBeamProperty Replacement(BeamPropertyModel femProperty, bool gpc)
                {
                    var converted = GpcModels.BeamProperty(femProperty, conversion.Report.Units);
                    return gpc ? GpcModels.BeamProperty(converted.Property, converted.Data.Formulation, converted.Data.Native) : converted;
                }
                foreach (var (label, property, selection, clear) in new (string, BeamPropertyModel, string[], bool)[]
                {
                    ("phase clear", Variant("Phase GPC", p => p.Material.Modulus *= 1.5), half, true), ("phase keep", Variant("Phase GPC", p => p.Material.Modulus *= 1.5), half, false),
                    ("phase generic", generic, half, false), ("phase same name taken", Variant(first.Name, p => p.Material.Modulus *= 2), half, false),
                })
                    variants.Add((label, (input, gpc, id) => GpcModelTransforms.PhaseModel(input, "Phase", selection, Replacement(property, gpc), clear, id)));
                variants.Add(("phase then paths", (input, gpc, id) => GpcModelTransforms.ReplacePaths(
                    GpcModelTransforms.PhaseModel(input, "Phase", half, Replacement(generic, gpc), false, GpcIdentity.Derive(id, "phase")), new[] { path("Traffic GPC", 0) }, id)));
            }
            foreach (var (variant, transform) in variants)
            {
                string label = example.Id + " from GPC " + variant; var id = GpcIdentity.Derive(GpcIdentity.NameSpace, "test-from-gpc:" + label);
                string expectedError = null, actualError = null; GpcModels.Conversion expected = null, actual = null;
                try { expected = transform(conversion, false, id); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is NotSupportedException) { expectedError = e.Message; }
                try { actual = transform(Input(), true, id); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is NotSupportedException) { actualError = e.Message; }
                if (expectedError != null || actualError != null)
                {
                    if (expectedError != actualError) throw new Exception($"GPC INPUT FAIL ({label}): error from the conversion '{expectedError}', from GPC '{actualError}'");
                    Console.WriteLine($"GPC INPUT ({label}): same error: {expectedError}"); continue;
                }
                Console.WriteLine($"GPC INPUT ({label}): " + RequireSame(label, actual, expected)); compared++;
            }
        }
        if (compared < 60) throw new Exception($"GPC INPUT FAIL: only {compared} outputs from inputs built in GPC compared");
        return compared;
    }

    /// <summary>Bridge Phase Model on the examples 01–10 with beams (IDs given to the beams without one): a new property on half of the beams
    /// with and without "Clear loads", a generic section with a principal angle (section axes, supports), a replacement with the name of the
    /// property of the first beam (kept by the first beam, or taken by it), errors; chains with Attach Moving Paths in both orders.</summary>
    private static int PhaseModels(double tolerance, Func<string, double, MovingPath> path)
    {
        int compared = 0;
        foreach (var example in NativeExamples.Create())
        {
            if (example.Model.Beams.Count == 0) continue;
            var fem = new ModelModel(example.Model);
            for (int k = 0; k < fem.Beams.Count; k++) if (string.IsNullOrWhiteSpace(fem.Beams[k].BeamId)) fem.Beams[k].BeamId = "B" + (k + 1);
            var ids = fem.Beams.Select(b => b.BeamId).Distinct().ToArray(); var units = new BridgeUnits(BridgeUnits.Parse(fem.Units.ToString()));
            var half = ids.Take(Math.Max(1, ids.Length / 2)).ToArray();
            var first = fem.Beams[0].BeamProperty;
            BeamPropertyModel Variant(string name, Action<BeamPropertyModel> change) { var p = BridGHe.Common.Helpers.ModelArchive.Clone(first); p.Name = name; change(p); return p; }
            var stiffer = Variant("Phase GPC", p => p.Material.Modulus *= 1.5);
            var sameName = Variant(first.Name, p => p.Material.Modulus *= 2);
            var generic = new BeamPropertyModel("Generic phase")
            {
                Name = "Generic phase", Material = BridGHe.Common.Helpers.ModelArchive.Clone(first.Material), PropertyType = BeamPropertyType.Beam,
                Section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 1.2e4, I11 = 3.1e8, I22 = 4.7e7, J = 1.3e7, ShearA1 = 8e3, ShearA2 = 6e3, AngleX1Rad = 0.3 },
            };
            var variants = new List<(string Label, string[] Ids, BeamPropertyModel Property, bool Clear)>
            {
                ("clear", half, stiffer, true), ("keep", half, stiffer, false), ("generic", half, generic, false), ("generic clear", ids, generic, true),
                ("same name kept", ids.Skip(1).DefaultIfEmpty(ids[0]).ToArray(), sameName, false), ("same name taken", half, sameName, false),
                ("unknown", new[] { "missing" }, stiffer, true), ("empty name", half, stiffer, false),
            };
            foreach (var (variant, selection, property, clear) in variants)
            {
                string label = example.Id + " phase " + variant, name = variant == "empty name" ? " " : "Phase " + variant;
                string femError = null, gpcError = null; ModelModel output = null; GpcModels.Conversion gpc = null;
                try { output = BridGHe.Common.Diagnostics.BridgePhaseModel.Create(fem, name, selection, property, clear); } catch (ArgumentException e) { femError = e.Message; }
                try
                {
                    var input = GpcModels.Convert(fem, tolerance);
                    gpc = GpcModelTransforms.PhaseModel(input, name, selection, GpcModels.BeamProperty(property, input.Report.Units), clear, GpcIdentity.Derive(GpcIdentity.NameSpace, "test-phase:" + label));
                }
                catch (ArgumentException e) { gpcError = e.Message; }
                if (femError != null || gpcError != null)
                {
                    if (femError != gpcError) throw new Exception($"GPC INPUT FAIL ({label}): FEM error '{femError}', GPC error '{gpcError}'");
                    Console.WriteLine($"GPC INPUT ({label}): same error: {femError}"); continue;
                }
                Console.WriteLine($"GPC INPUT ({label}): " + Require(label, gpc, output, tolerance)); compared++;
            }
            // Chains: a transformed input is the source of the next transformer (its report carries the case origins and property data).
            string existing = fem.LoadCases.FirstOrDefault()?.Name ?? "Q";
            var moving = new[] { path(existing, 100) }; var traffic = new[] { path("Traffic GPC", 0) };
            var chains = new (string Label, Func<ModelModel> Fem, Func<GpcModels.Conversion> Gpc)[]
            {
                ("paths then phase", () => BridGHe.Common.Diagnostics.BridgePhaseModel.Create(MovingModelBuilder.ReplacePaths(fem, moving), "Phase", half, stiffer, false),
                    () => { var p = GpcModelTransforms.ReplacePaths(GpcModels.Convert(fem, tolerance), moving, GpcIdentity.Derive(GpcIdentity.NameSpace, "chain-1a:" + example.Id));
                            return GpcModelTransforms.PhaseModel(p, "Phase", half, GpcModels.BeamProperty(stiffer, p.Report.Units), false, GpcIdentity.Derive(GpcIdentity.NameSpace, "chain-1b:" + example.Id)); }),
                ("phase then paths", () => MovingModelBuilder.ReplacePaths(BridGHe.Common.Diagnostics.BridgePhaseModel.Create(fem, "Phase", half, stiffer, false), traffic),
                    () => { var p = GpcModelTransforms.PhaseModel(GpcModels.Convert(fem, tolerance), "Phase", half, GpcModels.BeamProperty(stiffer, units), false, GpcIdentity.Derive(GpcIdentity.NameSpace, "chain-2a:" + example.Id));
                            return GpcModelTransforms.ReplacePaths(p, traffic, GpcIdentity.Derive(GpcIdentity.NameSpace, "chain-2b:" + example.Id)); }),
                ("cleared phase then paths", () => MovingModelBuilder.ReplacePaths(BridGHe.Common.Diagnostics.BridgePhaseModel.Create(fem, "Phase", ids, generic, true), traffic),
                    () => { var p = GpcModelTransforms.PhaseModel(GpcModels.Convert(fem, tolerance), "Phase", ids, GpcModels.BeamProperty(generic, units), true, GpcIdentity.Derive(GpcIdentity.NameSpace, "chain-3a:" + example.Id));
                            return GpcModelTransforms.ReplacePaths(p, traffic, GpcIdentity.Derive(GpcIdentity.NameSpace, "chain-3b:" + example.Id)); }),
                ("phase then phase", () => BridGHe.Common.Diagnostics.BridgePhaseModel.Create(BridGHe.Common.Diagnostics.BridgePhaseModel.Create(fem, "Phase", half, sameName, false), "Phase 2", ids, generic, false),
                    () => { var p = GpcModelTransforms.PhaseModel(GpcModels.Convert(fem, tolerance), "Phase", half, GpcModels.BeamProperty(sameName, units), false, GpcIdentity.Derive(GpcIdentity.NameSpace, "chain-4a:" + example.Id));
                            return GpcModelTransforms.PhaseModel(p, "Phase 2", ids, GpcModels.BeamProperty(generic, p.Report.Units), false, GpcIdentity.Derive(GpcIdentity.NameSpace, "chain-4b:" + example.Id)); }),
            };
            foreach (var (chain, femChain, gpcChain) in chains)
            {
                string label = example.Id + " " + chain; string femError = null, gpcError = null; ModelModel output = null; GpcModels.Conversion gpc = null;
                try { output = femChain(); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { femError = e.Message; }
                try { gpc = gpcChain(); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { gpcError = e.Message; }
                if (femError != null || gpcError != null)
                {
                    if (femError != gpcError) throw new Exception($"GPC INPUT FAIL ({label}): FEM error '{femError}', GPC error '{gpcError}'");
                    Console.WriteLine($"GPC INPUT ({label}): same error: {femError}"); continue;
                }
                Console.WriteLine($"GPC INPUT ({label}): " + Require(label, gpc, output, tolerance)); compared++;
            }
        }
        if (compared < 60) throw new Exception($"GPC INPUT FAIL: only {compared} Bridge Phase Model outputs compared");
        return compared;
    }

    /// <summary>The Bridge Phase Model component in a Grasshopper document (two iterations: with and without "Clear loads"): every output has the
    /// GPC input attached, equal to the conversion of its FEM output.</summary>
    private static int PhaseModelComponent(double tolerance)
    {
        var plugin = BridgeMergeTests.Initialize();
        IGH_Goo Goo(string type, object value)
        {
            var t = plugin.GetTypes().Single(x => x.Name == type);
            if (type != "GH_Model") return (IGH_Goo)Activator.CreateInstance(t, new[] { value });
            var goo = (IGH_Goo)Activator.CreateInstance(t); t.GetProperty("Value").SetValue(goo, value); return goo;
        }
        var example = NativeExamples.Create().First(e => e.Model.Beams.Count >= 2 && e.Model.LoadCases.Count != 0);
        var fem = new ModelModel(example.Model);
        for (int k = 0; k < fem.Beams.Count; k++) if (string.IsNullOrWhiteSpace(fem.Beams[k].BeamId)) fem.Beams[k].BeamId = "B" + (k + 1);
        var property = BridGHe.Common.Helpers.ModelArchive.Clone(fem.Beams[0].BeamProperty); property.Name = "Component phase"; property.Material.Modulus *= 1.25;
        using var doc = new GH_Document();
        var component = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "BridgePhaseModelComponent"));
        component.CreateAttributes(); doc.AddObject(component, false);
        ((Grasshopper.Kernel.Parameters.Param_GenericObject)component.Params.Input[0]).PersistentData.Append(Goo("GH_Model", fem));
        ((Grasshopper.Kernel.Parameters.Param_String)component.Params.Input[1]).SetPersistentData("Phase component");
        ((Grasshopper.Kernel.Parameters.Param_String)component.Params.Input[2]).SetPersistentData(fem.Beams.Take(Math.Max(1, fem.Beams.Count / 2)).Select(b => b.BeamId).ToArray());
        ((Grasshopper.Kernel.Parameters.Param_GenericObject)component.Params.Input[3]).PersistentData.Append(Goo("GH_BeamProperty", property));
        var clear = (Grasshopper.Kernel.Parameters.Param_Boolean)component.Params.Input[4]; clear.PersistentData.Clear(); clear.SetPersistentData(true, false);
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        var messages = component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(component.RuntimeMessages(GH_RuntimeMessageLevel.Warning)).ToArray();
        if (messages.Length != 0) throw new Exception("GPC INPUT FAIL (Bridge Phase Model component): " + string.Join("; ", messages));
        int outputs = component.Params.Output[0].VolatileData.AllData(true).Count();
        if (outputs != 2) throw new Exception($"GPC INPUT FAIL (Bridge Phase Model component): {outputs} outputs instead of 2");
        RequireComponents(doc, "component " + example.Id);
        return outputs;
    }
}
#endif
