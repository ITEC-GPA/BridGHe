#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using R = Rhino.Geometry;

/// <summary>
/// Chain E (e3, P2K, point 4): regression <c>gpc-build-model</c> (not native). The Build Model component on the elements of FEM models given
/// as GH data (one datum per element, in the order of the model, with the combinations and stages of the model): the GPC input it attaches
/// (Build Model on the drafts, <see cref="BridGHe.Common.Gpc.Legacy.LegacyBuildModel.Build"/>) against the conversion of its FEM output by the
/// legacy reader with the same owner (<see cref="GpcInputComparison.Require"/>): report, bridge data, analysis fingerprint, parts and the
/// XML document, bit for bit. Corpus: the N-mm models of the <c>--import-dump</c> corpus (examples 01–10 in memory, the 77 captured inputs,
/// the node, beam and plate data) and synthetic merges: a datum twice (the same goo), two branches of one datum (same GH GUID, its own loads),
/// coincident nodes, beam ends within the tolerance of a node (re-anchoring), beam ends without a node, a beam of zero length, labels.
/// </summary>
internal static class GpcBuildModelChecks
{
    private const string Captures = @"C:\Users\g.pacini\Documents\Repository\gpc-f6-claude\P2B\results\capture-1";

    /// <summary>
    /// Chain E (e3 point 5, coordinator decision 1 (b) of 5/11): the GPC input of Build Model in units other than N-mm against the conversion
    /// of its FEM output by the legacy reader: the same report and bridge data, the same graph (every element name, attribute and non-numeric
    /// text equal), and the maximum relative difference of the numbers per class (geometry, loads, section values: by the names of the
    /// elements above each number). Zero everywhere: bit for bit.
    /// </summary>
    internal static string Measure(string label, GpcModels.Conversion attached, ModelModel fem, double tolerance, out double[] maxima)
    {
        void Fail(string what) => throw new Exception("GPC BUILD MODEL UNITS FAIL (" + label + "): " + what);
        var model = attached.Model; var owner = GpcInputComparison.ElementOwner(model);
        if (owner != model.Guid) Fail("the model GUID of Build Model is not the owner of its element identities");
        var converted = GpcInputComparison.Conversion(fem, tolerance, owner, out var report);
        GpcInputComparison.RequireReport(label, attached.Report, report);
        GpcInputComparison.RequireBridge(label, model, attached.Bridge, converted, report.Bridge, false);
        var x = System.Xml.Linq.XDocument.Parse(GpcInputComparison.Document(model)).Descendants().ToArray();
        var y = System.Xml.Linq.XDocument.Parse(GpcInputComparison.Document(converted)).Descendants().ToArray();
        if (x.Length != y.Length) Fail($"{x.Length} / {y.Length} elements in the documents");
        string[] classes = { "geometry", "loads", "section values" }; maxima = new double[3]; int differing = 0;
        static int Class(System.Xml.Linq.XElement e)
        {
            string path = string.Join("/", e.AncestorsAndSelf().Select(a => a.Name.LocalName));
            if (path.Contains("Propert") || path.Contains("Section") || path.Contains("Material")) return 2;
            if (path.Contains("Load") || path.Contains("Pressure") || path.Contains("Temperature") || path.Contains("Stress") || path.Contains("Gradient") || path.Contains("InitialState")) return 1;
            return 0;
        }
        for (int i = 0; i < x.Length; i++)
        {
            string where = string.Join("/", x[i].AncestorsAndSelf().Reverse().Select(a => a.Name.LocalName));
            if (x[i].Name != y[i].Name) Fail($"element {i}: {where} / {y[i].Name.LocalName}");
            string Attributes(System.Xml.Linq.XElement e) => string.Join(" ", e.Attributes().Select(a => a.Name + "=" + a.Value));
            if (Attributes(x[i]) != Attributes(y[i])) Fail($"attributes of {where}: '{Attributes(x[i])}' / '{Attributes(y[i])}'");
            if (x[i].HasElements || y[i].HasElements || x[i].Value == y[i].Value) continue;
            const System.Globalization.NumberStyles Float = System.Globalization.NumberStyles.Float;
            if (!double.TryParse(x[i].Value, Float, System.Globalization.CultureInfo.InvariantCulture, out double p) || !double.TryParse(y[i].Value, Float, System.Globalization.CultureInfo.InvariantCulture, out double q))
                Fail($"text of {where}: '{x[i].Value}' / '{y[i].Value}'");
            double relative = Math.Abs(p - q) / Math.Max(Math.Abs(p), Math.Abs(q));
            int c = Class(x[i]); maxima[c] = Math.Max(maxima[c], relative); differing++;
            Console.WriteLine($"GPC BUILD MODEL UNITS ({label}): {classes[c]} {where} GPC {x[i].Value} / conversion {y[i].Value} (relative {relative:E2})");
        }
        return $"{x.Length} elements, {differing} numbers differ; maximum relative difference: geometry {maxima[0]:E2}, loads {maxima[1]:E2}, section values {maxima[2]:E2}";
    }

    /// <summary>The kN-m native section of core-native (<c>Program.CheckNativeSectionAnalysis</c>) through Build Model, measured as above.</summary>
    internal static void Single(string label, ModelModel fem) => Run(label, fem);

    internal static void Regression() => Run(null, null);

    private static void Run(string singleLabel, ModelModel singleModel)
    {
        var plugin = BridgeMergeTests.Initialize();
        var settings = plugin.GetType("BridGHe.Grasshopper.Settings");
        var instance = settings.GetProperty("Instance").GetValue(null);
        var toleranceProperty = settings.GetProperty("Tolerance");
        // The tolerance of the plug-in (the model tolerance of the active Rhino document, 0.001 without one), the same for both paths.
        double pluginTolerance = (double)toleranceProperty.GetValue(instance);
        int compared = 0, measured = 0; var totals = new List<string>(); var unitMaxima = new double[3];
        IGH_Goo Goo(string type, object value, Guid? guid = null)
        {
            var goo = (IGH_Goo)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem." + type));
            goo.GetType().GetProperty("Value").SetValue(goo, value);
            if (guid.HasValue) goo.GetType().GetField("_guid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(goo, guid.Value);
            return goo;
        }
        IGH_Goo Element(ElementModel e, Guid? guid = null) => e switch { NodeModel n => Goo("GH_Node", n, guid), BeamModel b => Goo("GH_Beam", b, guid), PlateModel p => Goo("GH_Plate", p, guid), _ => throw new NotSupportedException(e.GetType().Name) };
        void Build(string label, IEnumerable<IGH_Goo> elements, IEnumerable<LoadCombinationModel> combinations, IEnumerable<StagedConstructionModel> stages, double unused,
            ModelModel.ModelUnits modelUnits = ModelModel.ModelUnits.Nmm)
        {
            double tolerance = pluginTolerance;
            using var doc = new GH_Document();
            var component = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "BuildModelComponent"));
            component.CreateAttributes(); doc.AddObject(component, false);
            var input = (Param_GenericObject)component.Params.Input[0];
            foreach (var e in elements) input.PersistentData.Append(e);
            var name = (Param_String)component.Params.Input[1]; name.PersistentData.Clear(); name.SetPersistentData(label);
            var units = (Param_Integer)component.Params.Input[2]; units.PersistentData.Clear(); units.SetPersistentData((int)modelUnits);
            foreach (var c in combinations) ((dynamic)component.Params.Input[3]).PersistentData.Append((dynamic)Goo("GH_LoadCombination", c));
            foreach (var s in stages) ((Param_GenericObject)component.Params.Input[4]).PersistentData.Append(Goo("GH_StagedConstruction", s));
            GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
            var messages = component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(component.RuntimeMessages(GH_RuntimeMessageLevel.Remark)).ToArray();
            var output = component.Params.Output[0].VolatileData.AllData(true).SingleOrDefault();
            if (output == null)
            {
                // The FEM path refused the input: the GPC path gives the same error (the remark is not given then).
                Console.WriteLine($"GPC BUILD MODEL ({label}): no model, '{string.Join("; ", messages)}'"); compared++; return;
            }
            if (!(bool)output.GetType().GetProperty("HasAttachedInput").GetValue(output))
            {
                // Build Model on GPC refused it: the conversion of the FEM model must refuse it with the same message.
                var fem0 = (ModelModel)output.GetType().GetProperty("Value").GetValue(output);
                string error = null;
                try { GpcInputComparison.Conversion(fem0, tolerance, Guid.NewGuid()); } catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException) { error = ex.Message; }
                if (error == null || !messages.Contains("GPC Build Model: " + error)) throw new Exception($"GPC BUILD MODEL FAIL ({label}): no GPC input; messages '{string.Join("; ", messages)}', conversion '{error ?? "converted"}'");
                Console.WriteLine($"GPC BUILD MODEL ({label}): the same error, '{error}'"); compared++; return;
            }
            var attached = (GpcModels.Conversion)output.GetType().GetProperty("GpcInput").GetValue(output);
            var fem = (ModelModel)output.GetType().GetProperty("Value").GetValue(output);
            if (modelUnits != ModelModel.ModelUnits.Nmm)
            {
                // Decision 1 (b): measured, not required bit for bit (the N-mm models are).
                string measure = Measure(label, attached, fem, tolerance, out var maxima);
                for (int c = 0; c < 3; c++) unitMaxima[c] = Math.Max(unitMaxima[c], maxima[c]);
                Console.WriteLine($"GPC BUILD MODEL UNITS PASS ({label}, {modelUnits}): {measure}; {attached.Report.BuildModel}"); measured++; compared++;
                return;
            }
            string result = GpcInputComparison.Require(label, attached, fem, tolerance, buildModel: true);
            totals.Add(attached.Report.BuildModel.ToString());
            compared++;
            if (compared % 20 == 0 || label.StartsWith("synthetic", StringComparison.Ordinal)) Console.WriteLine($"GPC BUILD MODEL PASS ({label}): {result}; {attached.Report.BuildModel}");
        }
        {
            void Model(string label, ModelModel fem, double tolerance)
            {
                if (fem.Elements.Any(e => e.Results.Count != 0)) { fem = new ModelModel(fem); foreach (var e in fem.Elements) e.Results.Clear(); }
                if (fem.MovingPaths.Count != 0) return; // moving paths come after Build Model (Attach Moving Paths)
                Build(label, fem.Elements.Select(e => Element(e)), fem.LoadCombinations, fem.StagedConstruction, tolerance, fem.Units);
            }
            if (singleModel != null)
            {
                Model(singleLabel, singleModel, 0);
                Console.WriteLine($"GPC BUILD MODEL UNITS ({singleLabel}): maximum relative difference geometry {unitMaxima[0]:E2}, loads {unitMaxima[1]:E2}, section values {unitMaxima[2]:E2}");
                return;
            }
            // e3 point 5: the node, beam and plate data in every unit system (N-mm bit for bit, the others measured).
            foreach (ModelModel.ModelUnits u in Enum.GetValues(typeof(ModelModel.ModelUnits)))
            {
                string suffix = u == ModelModel.ModelUnits.Nmm ? "" : "-" + u;
                Model("nodes" + suffix, ImportDump.NodeData(u), 1e-3);
                Model("nodes-labelled-rigid" + suffix, ImportDump.NodeData(u, true), 1e-3);
                Model("beams" + suffix, ImportDump.BeamData(u, null), 1e-3);
                // The former copy of a load patch reads four edges (the import-dump corpus writes two): four here.
                var plateData = ImportDump.PlateData(u);
                foreach (var patch in plateData.Plates.SelectMany(p => p.Attributes).OfType<LoadPatchTypeAttributeModel>()) patch.Value.SelectedEdges = new[] { 1, 3, 0, 0 };
                Model("plates" + suffix, plateData, 1e-3);
            }
            // e3 point 5: what the corpus lacks outside N-mm: a restraint frame away from the global origin (its axes unitised again by the
            // normalisation) and an explicit section with shear-centre offsets and a principal angle (built from its definition), kN-m.
            {
                var fem = ImportDump.BeamData(ModelModel.ModelUnits.Knm, null);
                var generic = new SectionModel { Name = "G", SectionType = SectionModel.SectionTypes.Generic, SectionArea = 0.012, I11 = 3.1e-4, I22 = 7.7e-5, J = 2.3e-6, Centroid = new R.Point2d(0.01, -0.02),
                    ShearL1 = 0.013, ShearL2 = -0.007, ShearA1 = 0.005, ShearA2 = 0.004, AngleX1Rad = 0.3 };
                fem.Beams.Last().BeamProperty = new BeamPropertyModel("PG") { Name = "PG", Material = fem.Beams.First().BeamProperty.Material, Section = generic, PropertyType = BeamPropertyType.Beam };
                var node = fem.Nodes[2];
                node.Attributes.Add(new NodeCoordinateAttributeModel(new R.Plane(node.Position, new R.Vector3d(1, 2, 0), new R.Vector3d(-2, 1, 0.5))));
                node.Attributes.Add(new NodeRestraintAttributeModel(false, false, true, false, false, false));
                Model("synthetic units", fem, 1e-3);
            }

            // Synthetic merges and re-anchoring.
            var beamModel = ImportDump.BeamData(ModelModel.ModelUnits.Nmm, null);
            var beams = beamModel.Beams.ToArray();
            var q = new LoadCaseModel("9", "Q2"); var dead = new LoadCaseModel(LoadCaseModel.Dead);
            var nodeGoo = beamModel.Nodes.Select(n => Element(n)).ToList();
            var same = Element(beams[2]);
            // A datum with a bridge member merged with itself: the former error (two members), on both paths.
            var member = Element(beams[1]);
            Build("synthetic self-merge with a member", beamModel.Nodes.Select(n => Element(n)).Concat(new[] { member, member }), Array.Empty<LoadCombinationModel>(), Array.Empty<StagedConstructionModel>(), 1e-3);
            var branchGuid = Guid.NewGuid();
            // Two branches of a datum without a bridge member (with one, the former merge gives the error above).
            var plain = new BeamModel(beams[0]); foreach (var a in plain.Attributes.OfType<BridgeMemberAttributeModel>().ToList()) plain.Attributes.Remove(a);
            var branchA = new BeamModel(plain); branchA.Loads.Add(new BeamDistributedLoadModel(q, new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.Uniform, PA = -3, LoadDirection = BeamDistributedLoadModel.LoadDirection.Z }, BeamDistributedLoadModel.LoadType.Force));
            var branchB = new BeamModel(plain); branchB.Loads.Add(new BeamPointLoadModel(dead, new() { P = new R.Vector3d(0, 0, -7), A = 0.5 }, BeamPointLoadModel.LoadType.Force));
            branchB.Attributes.Add(new ElementIdAttributeModel("B-Z"));
            var nodeGuid = Guid.NewGuid();
            var nodeA = new NodeModel(beamModel.Nodes[1]); nodeA.Loads.Add(new NodeForceLoadModel(q, new R.Vector3d(0, 0, -1)));
            var nodeB = new NodeModel(beamModel.Nodes[1]); nodeB.Loads.Add(new NodeForceLoadModel(q, new R.Vector3d(0, 0, -1))); nodeB.Attributes.Add(new NodeRestraintAttributeModel(false, false, true, false, false, false));
            var near = new NodeModel("near", beamModel.Nodes[2].Position + new R.Vector3d(0.0004, 0, 0)); near.Attributes.Add(new ElementIdAttributeModel("N-near")); near.Loads.Add(new NodeForceLoadModel(dead, new R.Vector3d(1, 0, 0)));
            var shifted = new BeamModel(beams[2]) { PointFrom = beams[2].PointFrom + new R.Vector3d(0, 0.0003, 0), PointTo = beams[2].PointTo + new R.Vector3d(0.5, 0.5, 0.5) };
            var zero = new BeamModel(beams[0]) { PointTo = beams[0].PointFrom + new R.Vector3d(0.0002, 0, 0) };
            var elements = nodeGoo.Concat(new[] { Element(nodeA, nodeGuid), Element(nodeB, nodeGuid), Element(near), same, same, Element(branchA, branchGuid), Element(branchB, branchGuid), Element(shifted), Element(zero) }).ToList();
            Build("synthetic merges", elements, Array.Empty<LoadCombinationModel>(), Array.Empty<StagedConstructionModel>(), 1e-3);
            foreach (var example in NativeExamples.Create()) Model("memory-" + example.Id, example.Model, 1e-6);
            // BRIDGHE_BUILD_MODEL_QUICK=1: without the 77 captured models (iterations; the gates run them).
            if (Directory.Exists(Captures) && Environment.GetEnvironmentVariable("BRIDGHE_BUILD_MODEL_QUICK") != "1")
                foreach (var file in Directory.GetFiles(Captures, "*.bgmodel").OrderBy(f => f, StringComparer.Ordinal))
                {
                    string info = Path.ChangeExtension(file, ".txt");
                    double tolerance = File.Exists(info) ? double.Parse(File.ReadAllLines(info)[1], System.Globalization.CultureInfo.InvariantCulture) : 1e-6;
                    Model("capture-" + Path.GetFileNameWithoutExtension(file), BridGHe.Common.Helpers.ModelArchive.Read<ModelModel>(File.ReadAllBytes(file)), tolerance);
                }
        }
        // P2K (coordinator, item 3): BridgeModelData.Save() of two equal data differs only by the identity GUIDs (random, version 4) that GPC
        // geometry values write and never read back (here the section-up vector of the member metadata); Fingerprint() does not see them.
        byte[] Save()
        {
            var data = new GPC.Checkers.CompositeBridge.BridgeModelData();
            data.SetMember(new Guid("0f3b0c1e-6a43-4a51-9d1b-6c4e1c2b9a10"), new GPC.Checkers.CompositeBridge.Layout.BridgeMemberMetadata("Girder:1:1", GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder,
                1, 1, 1, 0, 1000, new GPC.Geometry.Vector3d(0, 0, 1), "G"));
            return data.Save();
        }
        var v4 = new System.Text.RegularExpressions.Regex("[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}");
        string x = System.Text.Encoding.UTF8.GetString(Save()), y = System.Text.Encoding.UTF8.GetString(Save());
        if (v4.Replace(x, "v4") != v4.Replace(y, "v4")) throw new Exception("GPC BUILD MODEL FAIL: BridgeModelData.Save differs beyond the GUIDs of the geometry values");
        Console.WriteLine($"GPC BUILD MODEL: BridgeModelData.Save of equal data {(x == y ? "identical" : "different only by the version 4 GUIDs of the geometry values")} ({v4.Matches(x).Count} in the archive)");
        Console.WriteLine($"GPC BUILD MODEL UNITS: {measured} runs in units other than N-mm, maximum relative difference geometry {unitMaxima[0]:E2}, loads {unitMaxima[1]:E2}, section values {unitMaxima[2]:E2}");
        Console.WriteLine($"GPC BUILD MODEL PASS: {compared} Build Model runs, the GPC input equal to the conversion of the FEM output bit for bit in N-mm, measured in the other units (or the same error)");
    }
}
#endif
