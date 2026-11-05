#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    internal static void RequireReport(string label, LegacyImportReport actual, LegacyImportReport expected)
    {
        void Fail(string what) => throw new Exception("GPC INPUT FAIL (" + label + "): report " + what);
        string Map<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> map, Func<TValue, string> text) =>
            string.Join("; ", map.Select(p => p.Key + "=" + text(p.Value)).OrderBy(s => s, StringComparer.Ordinal));
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
    internal static string Document(Model m)
    {
        string text;
        try
        {
            using var stream = new MemoryStream(); GPC.Model.Persistence.ModelArchive.SaveDocument(m, stream);
            text = RandomGuid.Replace(System.Text.Encoding.UTF8.GetString(stream.ToArray()), "v4");
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

    internal static string Require(string label, GpcModels.Conversion attached, ModelModel fem, double tolerance)
    {
        void Fail(string what) => throw new Exception("GPC INPUT FAIL (" + label + "): " + what);
        var model = attached.Model; var owner = ElementOwner(model);
        if (owner == model.Guid) Fail("the transformed model has the model GUID of its source");
        var expected = GPC.Model.Core.ModelValues.Copy(Conversion(fem, tolerance, owner, out var report), GPC.Model.Core.ModelCopyOptions.None, model.Guid);
        RequireReport(label, attached.Report, report);
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
        foreach (var component in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name is "AttachMovingPathsComponent" or "BridgePhaseModelComponent"))
            foreach (var goo in component.Params.Output[0].VolatileData.AllData(true))
            {
                string name = label + " " + component.GetType().Name.Replace("Component", "") + " '" + component.NickName + "'";
                if (!(bool)goo.GetType().GetProperty("HasAttachedInput").GetValue(goo)) throw new Exception("GPC INPUT FAIL (" + name + "): no GPC input attached by the transformer");
                var attached = (GpcModels.Conversion)goo.GetType().GetProperty("GpcInput").GetValue(goo);
                var fem = (ModelModel)goo.GetType().GetProperty("Value").GetValue(goo);
                // The tolerance of the attached input: GpcInput returns it only at the current tolerance of the plug-in.
                Console.WriteLine($"GPC INPUT PASS ({name}): " + Require(name, attached, fem, attached.Tolerance)); count++;
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
        Console.WriteLine($"GPC MODEL TRANSFORMS PASS: {compared} Attach Moving Paths and {phases} Bridge Phase Model outputs (chains included), {components} Bridge Phase Model component outputs on GPC equal to the conversion of their FEM output.");
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
