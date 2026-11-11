#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using GPC.Converter.Straus7;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// C1 (11/11/2026): regression <c>gpc-review-fixes</c> (not native), the product fixes of the ChatGPT reviews confirmed by the coordinator
/// (INT/verifica-esiti-chatgpt-20261111.md), each with the case of its finding:
/// <list type="bullet">
/// <item>R65-C3-01: the nodes Build Model creates on the GPC-only path take the identity of the model; two models never share their GUIDs, a
/// third Build Model of their decompositions keeps four nodes; an exact copy keeps the copy semantics (merged by GH GUID);</item>
/// <item>R65-C3-02: Bridge Phase Model and Bridge Construction Sequence that swap a beam to or from a double angle rebuild every datum on the
/// principal axes (offset axes, load eccentricity, local load frames), against the conversion of the FEM output and against Build Model on
/// the drafts they output (the oracle of P2), with the Straus7 calls of the write plan recorded (no native API);</item>
/// <item>R25-01: Composite Beam on the GPC-only path refuses a composite property that is not a definition (error, no output); the FEM path keeps its rule;</item>
/// <item>P30-01: a saved model whose drafts cannot be read does not open; results that cannot be read are dropped with a notice; GUIDs without
/// drafts or of another count are damage; a former file without drafts opens;</item>
/// <item>P30-02 and P30-04: a beam-property record without its data, trailing bytes after the records and drafts, and wrong draft counts are refused;</item>
/// <item>Table Material (user decision of 11/11): the table is read with and without α and ρ, and the write plan carries the table, α and ρ.</item>
/// </list>
/// The scan of the tracked definitions with BGHGPC1 data (every datum read by the current reader) is <c>--bghgpc1-scan</c> (<see cref="Scan"/>).
/// </summary>
internal static class GpcReviewFixesC1
{
    private static Assembly plugin;
    private static readonly List<string> failures = new();
    private static int checks;
    private static void Check(bool ok, string what) { checks++; if (!ok) failures.Add(what); }
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    internal static void Regression()
    {
        plugin = BridgeMergeTests.Initialize(); failures.Clear(); checks = 0;
        CreatedNodes();
        DoubleAngleSwaps(true); DoubleAngleSwaps(false);
        CompositeBeamRefusal();
        SavedModelChunks();
        DraftArchiveRecords();
        TableMaterial();
        foreach (var f in failures) Console.WriteLine("  GPC REVIEW FIXES FAIL: " + f);
        Console.WriteLine($"GPC REVIEW FIXES: {checks} checks, {failures.Count} failures");
        if (failures.Count != 0) throw new Exception($"GPC REVIEW FIXES FAIL: {failures.Count} of {checks} checks");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // GH helpers

    private static Type T(string name) => plugin.GetType("BridGHe.Grasshopper.DataTypes." + name, true);
    private static object P(object o, string name) => o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);

    private static IGH_Goo Goo(object v) => v switch
    {
        IGH_Goo g => g, double d => new GH_Number(d), int i => new GH_Integer(i), bool b => new GH_Boolean(b), string s => new GH_String(s),
        _ => throw new ArgumentException("C1: no goo for " + v?.GetType().Name),
    };

    private static void Set(IGH_Param param, object value)
    {
        param.RemoveAllSources();
        var persistent = param.GetType().GetProperty("PersistentData").GetValue(param);
        persistent.GetType().GetMethod("Clear").Invoke(persistent, null);
        var append = persistent.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(GH_Path));
        foreach (var v in value is object[] list ? list : new[] { value }) append.Invoke(persistent, new object[] { Goo(v), new GH_Path(0) });
    }

    /// <summary>A component solved with the given inputs (by index): its outputs (copies), errors and warnings.</summary>
    private static (List<IGH_Goo>[] Outputs, string[] Errors, string[] Warnings) Solve(string type, Dictionary<int, object> inputs)
    {
        using var doc = new GH_Document();
        var c = BridgeSegmentExcelTests.Add(doc, type);
        foreach (var input in inputs) Set(c.Params.Input[input.Key], input.Value);
        BridgeSegmentExcelTests.Solve(doc, true);
        return (c.Params.Output.Select(p => p.VolatileData.AllData(true).Select(g => g.Duplicate()).ToList()).ToArray(),
            c.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToArray(), c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToArray());
    }

    private static IGH_Goo One(string type, Dictionary<int, object> inputs)
    {
        var r = Solve(type, inputs);
        return r.Outputs[0].FirstOrDefault() ?? throw new Exception("C1: " + type + " gives no output: " + string.Join(" / ", r.Errors));
    }

    /// <summary>The FEM-backed GH type as the components make it: the constructor from the FEM value (optional parameters at their defaults).</summary>
    private static IGH_Goo Make(string type, object value)
    {
        var ctor = T(type).GetConstructors().Where(c => c.GetParameters().Length >= 1 && c.GetParameters()[0].ParameterType.IsInstanceOfType(value)
            && c.GetParameters().Skip(1).All(p => p.HasDefaultValue)).OrderBy(c => c.GetParameters().Length).First();
        return (IGH_Goo)ctor.Invoke(new[] { value }.Concat(ctor.GetParameters().Skip(1).Select(p => p.DefaultValue)).ToArray());
    }

    /// <summary>A beam by its draft alone (the GPC-only path), with the GH GUID given.</summary>
    private static IGH_Goo GpcOnlyBeam(BeamModel fem, Guid gh) =>
        (IGH_Goo)T("Fem.GH_Beam").GetMethod("FromGpc").Invoke(null, new object[] { (LegacyBeamDraft)P(Make("Fem.GH_Beam", fem), "Gpc"), (Guid?)gh });

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // R65-C3-01

    private static void CreatedNodes()
    {
        var m2 = NativeExamples.Create().Single(e => e.Id == "02-simply-supported").Model;
        var property = m2.Beams[0].BeamProperty;
        int nmm = (int)ModelModel.ModelUnits.Nmm;
        IGH_Goo Build(string name, params IGH_Goo[] elements) => One("BuildModelComponent", new Dictionary<int, object> { [0] = elements.Cast<object>().ToArray(), [1] = name, [2] = nmm });
        List<IGH_Goo> Nodes(IGH_Goo model) => Solve("DecomposeModelComponent", new Dictionary<int, object> { [0] = model }).Outputs[0];
        GpcModelDrafts Drafts(IGH_Goo model) => (GpcModelDrafts)P(model, "GpcDrafts");
        Guid[] Guids(IGH_Goo model) => (Guid[])model.GetType().GetMethod("ElementGuidsOf").Invoke(model, new object[] { Drafts(model) });
        static string Key(GPC.Geometry.Point3d p) => R(p.X) + " " + R(p.Y) + " " + R(p.Z);

        var beamA = GpcOnlyBeam(new BeamModel(new Point3d(0, 0, 0), new Point3d(1000, 0, 0), property), new Guid("c1000000-0000-4000-8000-00000000000a"));
        var beamB = GpcOnlyBeam(new BeamModel(new Point3d(0, 1000, 0), new Point3d(1000, 1000, 0), property), new Guid("c1000000-0000-4000-8000-00000000000b"));
        var a = Build("C1 two models", beamA); var b = Build("C1 two models", beamB);
        Check(P(a, "Value") == null && P(b, "Value") == null, "R65-C3-01: Build Model on GPC-only beams gives models without FEM value");
        var nodesA = Nodes(a); var nodesB = Nodes(b);
        Check(nodesA.Count == 2 && nodesB.Count == 2, $"R65-C3-01: each model has the two nodes it created ({nodesA.Count}, {nodesB.Count})");
        var ghA = nodesA.Select(n => (Guid)P(n, "Guid")).ToArray(); var ghB = nodesB.Select(n => (Guid)P(n, "Guid")).ToArray();
        Check(!ghA.Intersect(ghB).Any(), "R65-C3-01: the created nodes of two models have other GH GUIDs");
        Check(!Drafts(a).Nodes.Select(d => d.Node.Guid).Intersect(Drafts(b).Nodes.Select(d => d.Node.Guid)).Any(), "R65-C3-01: and other draft GUIDs");
        // The former identity (by name alone, kept on the FEM path) gave them the same GUIDs: what the finding saw.
        var units = new BridgeUnits(DefinitionUnits.Nmm);
        GpcModels.Conversion Direct(IGH_Goo beam, bool gpcOnly) => LegacyBuildModel.Build("C1 two models", units, 0.001, Array.Empty<(Guid, LegacyNodeDraft)>(),
            new[] { ((Guid)P(beam, "Guid"), (LegacyBeamDraft)P(beam, "Gpc")) }, Array.Empty<(Guid, LegacyPlateDraft)>(), Array.Empty<LegacyCombinationDraft>(), Array.Empty<LegacyStageDraft>(), null, gpcOnly);
        var formerA = Direct(beamA, false).Drafts.Nodes.Select(d => d.Node.Guid).ToArray(); var formerB = Direct(beamB, false).Drafts.Nodes.Select(d => d.Node.Guid).ToArray();
        Check(formerA.SequenceEqual(formerB) && formerA[0] == GpcIdentity.Derive(GpcIdentity.NameSpace, "gpc-build-model-node:1"), "R65-C3-01: the FEM path keeps the former identity of a created node (unchanged)");
        Check(Direct(beamA, true).Drafts.Nodes.Select(d => d.Node.Guid).SequenceEqual(Drafts(a).Nodes.Select(d => d.Node.Guid)), "R65-C3-01: the component gives the GPC-only identity");
        // Determinism: the same inputs give the same identities.
        var again = Build("C1 two models", beamA);
        Check(Guids(again).SequenceEqual(Guids(a)) && Drafts(again).Nodes.Select(d => d.Node.Guid).SequenceEqual(Drafts(a).Nodes.Select(d => d.Node.Guid)), "R65-C3-01: the same inputs give the same GUIDs");

        // A third Build Model of the four nodes, a different restraint on each.
        var patterns = new[] { new object[] { true, false, false }, new object[] { false, true, false }, new object[] { false, false, true }, new object[] { true, true, true } };
        var four = nodesA.Concat(nodesB).ToList();
        var restrained = four.Select((n, i) => One("NodeRestraintsComponent", new Dictionary<int, object> { [0] = n, [1] = patterns[i] })).ToList();
        Check(restrained.Select(n => (Guid)P(n, "Guid")).SequenceEqual(four.Select(n => (Guid)P(n, "Guid"))), "R65-C3-01: Node Restraints keeps the GH GUID of its node");
        var c = Build("C1 third", restrained.ToArray());
        var drafts = Drafts(c); var model = (GPC.Model.Models.Model)P(c, "Gpc");
        var positions = drafts.Nodes.Select(d => Key(d.Node.Position)).ToArray();
        Check(drafts.Nodes.Count == 4 && positions.Distinct().Count() == 4, $"R65-C3-01: the third model has four nodes at four positions ({drafts.Nodes.Count}: {string.Join("; ", positions)})");
        Check(model.NodesElements.Count == 4 && model.NodesElements.Values.Select(n => n.Guid).Distinct().Count() == 4, "R65-C3-01: four nodes in the third model, four domain GUIDs");
        Check(drafts.Nodes.Select(d => d.Node.Guid).Distinct().Count() == 4, "R65-C3-01: four draft GUIDs in the third model");
        var guids = Guids(c);
        Check(guids.Distinct().Count() == 4 && guids.SequenceEqual(restrained.Select(n => (Guid)P(n, "Guid"))), "R65-C3-01: four GH GUIDs in the third model, those of its inputs");
        for (int i = 0; i < 4; i++)
        {
            var input = ((LegacyNodeDraft)P(restrained[i], "Gpc")).Node;
            var output = drafts.Nodes.SingleOrDefault(d => Key(d.Node.Position) == Key(input.Position))?.Node;
            string Restraint(GPC.Model.Elements.NodeElement n) => n == null ? "none" : string.Join(";", n.Assignments.Restrains.Select(r => string.Join(",", r.Restrain.Restrains.Select(x => x.GetType().Name + ":" + GPC.Model.Core.ModelValues.Fingerprint(new object[] { x })))));
            Check(output != null && output.Assignments.Restrains.Count == 1 && Restraint(output) == Restraint(input), $"R65-C3-01: node {i + 1} keeps its own restraint ({Restraint(output)})");
        }
        var counts = ((GpcModels.Conversion)P(c, "GpcInput")).Report.BuildModel;
        Check(counts.Merges[0] == 0 && counts.PositionMerges == 0, "R65-C3-01: no merge in the third model (" + counts + ")");

        // An exact copy: the same inputs, the same identities; its nodes merge into those of the original by GH GUID (the 0.3.0 rule, self-merges).
        var copy = Build("C1 two models", beamA);
        var nodesCopy = Nodes(copy);
        Check(nodesCopy.Select(n => (Guid)P(n, "Guid")).SequenceEqual(ghA), "R65-C3-01: an exact copy gives its created nodes the same GH GUIDs");
        var d = Build("C1 copies", nodesA.Concat(nodesCopy).ToArray());
        var dc = ((GpcModels.Conversion)P(d, "GpcInput")).Report.BuildModel;
        Check(Drafts(d).Nodes.Count == 2 && dc.Merges[0] == 2 && dc.SelfMerges[0] == 2, "R65-C3-01: the nodes of an exact copy merge by GH GUID, as copies of one datum (" + dc + ")");
        var e = Build("C1 copies restrained", One("NodeRestraintsComponent", new Dictionary<int, object> { [0] = nodesA[0], [1] = patterns[0] }),
            One("NodeRestraintsComponent", new Dictionary<int, object> { [0] = nodesCopy[0], [1] = patterns[1] }));
        Check(Drafts(e).Nodes.Count == 1 && Drafts(e).Nodes[0].Node.Assignments.Restrains.Count == 2, "R65-C3-01: a node of an exact copy with another restraint merges into the original with both restraints (0.3.0 rule)");
        Console.WriteLine($"GPC REVIEW FIXES R65-C3-01: created nodes A {string.Join(",", ghA)}, B {string.Join(",", ghB)}; third model {drafts.Nodes.Count} nodes, {counts}; copies {dc}");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // R65-C3-02

    private static readonly MaterialModel Steel = new MaterialModel("S355") { Name = "S355", Kind = MaterialModel.MaterialKind.Steel, Modulus = 210000, PoissonRatio = 0.3, Density = 7.85e-9, ThermalExpansion = 1.2e-5 };

    private static BeamPropertyModel Ordinary(string name = "PO") { var s = new SectionModel { SectionType = SectionModel.SectionTypes.I, D = 500, B1 = 200, B2 = 200, T1 = 20, T2 = 20, T3 = 10 }; s.CalculateSection(); return new BeamPropertyModel(name) { Name = name, Material = Steel, Section = s, PropertyType = BeamPropertyType.Beam }; }

    /// <summary>The double angle of the Angle component (mirror Left, gap A; mirror set before the calculation, as the former component).</summary>
    private static BeamPropertyModel DoubleAngle(string name = "P2L")
    {
        var s = new SectionModel { SectionType = SectionModel.SectionTypes.Angle, B = 150, T1 = 10, D = 100, T2 = 12, Mirror = SectionModel.MirrorTypes.Left, MirrorGapA = 8 }; s.CalculateSection();
        return new BeamPropertyModel(name) { Name = name, Material = Steel, Section = s, PropertyType = BeamPropertyType.Beam };
    }

    private static void Loads(BeamModel beam, LoadCaseModel q, bool support)
    {
        var global = new CoordinateSystemModel("G");
        beam.Loads.Add(new BeamDistributedLoadModel(q, new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.Uniform, PA = -2, LoadDirection = BeamDistributedLoadModel.LoadDirection.Y }, BeamDistributedLoadModel.LoadType.Force));
        beam.Loads.Add(new BeamDistributedLoadModel(q, new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.Uniform, PA = -3, LoadDirection = BeamDistributedLoadModel.LoadDirection.Z, CoordinateSystem = global }, BeamDistributedLoadModel.LoadType.Force));
        beam.Loads.Add(new BeamPointLoadModel(q, new() { P = new Vector3d(0, -5, 1), A = 0.4 }, BeamPointLoadModel.LoadType.Force));
        beam.Loads.Add(new BeamPointLoadModel(q, new() { P = new Vector3d(1, 0, -4), A = 0.6, CoordinateSystem = global }, BeamPointLoadModel.LoadType.Force));
        if (support) beam.Attributes.Add(new BeamSupportAttributeModel(new BeamSupportAttributeModel.AttributeData { SpringType = BeamSupportAttributeModel.SpringType.Simple, Stiffness = 3, Gap = 0,
            SimpleSpringType = BeamSupportAttributeModel.SimpleSpringTypes.TensionCompression, LocalAxisDirection = BeamSupportAttributeModel.LocalAxisDirection.positive2Axis,
            SpringTensionOrientation = BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis }));
    }

    /// <summary>Two beams of an ordinary I property: B1 at 30° with the offset (5,7), B2 at 0° without offset, each with a local and a global
    /// distributed load, a local and a global point load and a distributed support.</summary>
    private static ModelModel SwapModel(bool support)
    {
        var q = new LoadCaseModel("2", "Q");
        var nodes = new[] { new NodeModel("1", new Point3d(0, 0, 0)), new NodeModel("2", new Point3d(3000, 0, 0)), new NodeModel("3", new Point3d(6000, 0, 0)) };
        nodes[0].Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true)); nodes[2].Attributes.Add(new NodeRestraintAttributeModel(true, true, true, false, false, false));
        var ordinary = Ordinary();
        var b1 = new BeamModel(nodes[0].Position, nodes[1].Position, ordinary, null, 30) { BeamId = "B1", Offset = new Vector2d(5, 7) };
        var b2 = new BeamModel(nodes[1].Position, nodes[2].Position, ordinary, null, 0) { BeamId = "B2" };
        Loads(b1, q, support); Loads(b2, q, support);
        var model = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = "C1 swaps" };
        model.AddElements(nodes.Cast<ElementModel>().Concat(new[] { b1, b2 }).ToArray());
        return model;
    }

    /// <summary>What hangs on the axes of every beam, bit for bit, by number (Build Model names a beam without Element ID by its number): the property, section and offset axes, the offset, every load
    /// (eccentricity, frame of the local components) and the axes of the supports.</summary>
    private static string AxesText(GPC.Model.Models.Model m)
    {
        static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");
        static string V(GPC.Geometry.Vector3d v) => v == null ? "-" : B(v.X) + "," + B(v.Y) + "," + B(v.Z);
        static string Cs(GPC.Geometry.CoordinateSystem c) => c == null ? "-" : V(c.V1) + "/" + V(c.V2) + "/" + V(c.V3);
        static string L(GPC.Model.Loads.Load l) => l == null ? "-" : l switch { GPC.Model.Loads.PointLoad p => "point " + Cs(p.CoordinateSystem), GPC.Model.Loads.LineLoad x => "line " + Cs(x.CoordinateSystem), _ => l.GetType().Name };
        var s = new StringBuilder();
        foreach (var beam in m.BeamElements.Values.OrderBy(x => x.Id))
        {
            var a = beam.Assignments;
            s.Append("beam ").Append(beam.Id).Append(" property ").Append(beam.BeamProperty?.Name).Append(" section ").Append(Cs(a.SectionAxes)).Append(" offset axes ").Append(Cs(a.OffsetAxes))
                .Append(" offset ").Append(V(a.OffsetI)).Append(' ').Append(V(a.OffsetJ)).Append('\n');
            foreach (var l in a.Loads) s.Append("  load ").Append(l.SourceRecord).Append(" eccentricity ").Append(V(l.Eccentricity)).Append(' ').Append(L(l.Concentrated)).Append(' ').Append(L(l.StartIntensity)).Append(' ').Append(L(l.EndIntensity)).Append('\n');
            if (a.Physical != null) foreach (var support in a.Physical.Supports) s.Append("  support ").Append(Cs(support.Axes)).Append('\n');
        }
        return s.ToString();
    }

    /// <summary>Build Model on the drafts of a transformed input (the oracle of P2: the same model from the drafts the transformer gives).</summary>
    private static GpcModels.Conversion FromDrafts(GpcModels.Conversion c, string name)
    {
        var d = c.Drafts ?? throw new Exception("C1: the transformed input carries no drafts");
        return LegacyBuildModel.Build(name, c.Report.Units, c.Tolerance, d.Nodes.Select(n => (n.Node.Guid, n)).ToList(), d.Beams.Select(b => (b.Beam.Guid, b)).ToList(),
            d.Plates.Select(p => (p.Plate.Guid, p)).ToList(), Array.Empty<LegacyCombinationDraft>(), Array.Empty<LegacyStageDraft>(), null, true);
    }

    private static void DoubleAngleSwaps(bool supports)
    {
        const double tolerance = 1e-6;
        var fem = SwapModel(supports); var ids = new[] { "B1", "B2" };
        var units = new BridgeUnits(DefinitionUnits.Nmm);
        var doubleAngle = DoubleAngle(); var ordinary = Ordinary(); var stiffer = Ordinary("PO stiffer"); stiffer.Material = new MaterialModel(Steel) { Modulus = 1.5 * Steel.Modulus };
        int phases = 0;
        foreach (bool clear in new[] { false, true })
        {
            // ordinary → double angle → ordinary, and ordinary → ordinary (bit for bit as before the fix: the turn does not change).
            var femTo = BridGHe.Common.Diagnostics.BridgePhaseModel.Create(fem, "To 2L", ids, doubleAngle, clear);
            var gpcTo = GpcModelTransforms.PhaseModel(GpcInputComparison.ConvertWithDrafts(fem, tolerance), "To 2L", ids, GpcModels.BeamProperty(doubleAngle, units), clear, GpcIdentity.Derive(GpcIdentity.NameSpace, "c1-phase-to:" + clear));
            var femBack = BridGHe.Common.Diagnostics.BridgePhaseModel.Create(femTo, "Back", ids, ordinary, clear);
            var gpcBack = GpcModelTransforms.PhaseModel(gpcTo, "Back", ids, GpcModels.BeamProperty(ordinary, gpcTo.Report.Units), clear, GpcIdentity.Derive(GpcIdentity.NameSpace, "c1-phase-back:" + clear));
            var femSame = BridGHe.Common.Diagnostics.BridgePhaseModel.Create(fem, "Same", ids, stiffer, clear);
            var gpcSame = GpcModelTransforms.PhaseModel(GpcInputComparison.ConvertWithDrafts(fem, tolerance), "Same", ids, GpcModels.BeamProperty(stiffer, units), clear, GpcIdentity.Derive(GpcIdentity.NameSpace, "c1-phase-same:" + clear));
            foreach (var (label, gpc, femOutput) in new[] { ("phase to double angle", gpcTo, femTo), ("phase back to ordinary", gpcBack, femBack), ("phase ordinary to ordinary", gpcSame, femSame) })
            {
                string name = label + (clear ? " clear loads" : " keep loads") + (supports ? ", support" : ", no support");
                try { Console.WriteLine($"GPC REVIEW FIXES R65-C3-02 ({name}): " + GpcInputComparison.Require("C1 " + name, gpc, femOutput, tolerance)); Check(true, name); }
                catch (Exception ex) { Check(false, $"R65-C3-02 ({name}): against the conversion of the FEM output: {ex.Message}"); }
                Oracle(name, gpc);
                phases++;
            }
            Check(!AxesText(gpcTo.Model).Split('\n')[0].Contains(" offset axes - "), "R65-C3-02: the beam with an offset has offset axes after the swap");
        }

        // Bridge Construction Sequence: the girders of the simple bridge (an offset, the loads and a support on the first girder beam) on the
        // double angle, and the snapshot back on an ordinary property.
        var bridge = BridgeConstructionTests.Model(SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()));
        var girder = bridge.Beams.First(b => b.Attributes.OfType<BridgeMemberAttributeModel>().Any());
        girder.Offset = new Vector2d(5, 7); girder.AngleDeg = 30; Loads(girder, bridge.LoadCases[0], supports);
        BridgePhaseDefinition Phase(int order, string name) => new BridgePhaseDefinition { Order = order, Name = name, Day = 7 };
        var source = GpcInputComparison.ConvertWithDrafts(bridge, tolerance);
        var femSnapshot = BridgeConstructionSequence.Create(bridge, Phase(2, "C1 2L"), null, null, new[] { "*" }, doubleAngle, null).Model;
        var gpcSnapshot = GpcModelTransforms.ConstructionSequence(source, 2, "C1 2L", 7, null, null, new[] { "*" }, GpcModels.BeamProperty(doubleAngle, units), GpcIdentity.Derive(GpcIdentity.NameSpace, "c1-sequence-to:" + supports));
        var femBackSnapshot = BridgeConstructionSequence.Create(femSnapshot, Phase(3, "C1 back"), null, null, new[] { "*" }, ordinary, null).Model;
        var gpcBackSnapshot = GpcModelTransforms.ConstructionSequence(gpcSnapshot, 3, "C1 back", 7, null, null, new[] { "*" }, GpcModels.BeamProperty(ordinary, units), GpcIdentity.Derive(GpcIdentity.NameSpace, "c1-sequence-back:" + supports));
        foreach (var (label0, gpc, femOutput, from) in new[] { ("sequence to double angle", gpcSnapshot, femSnapshot, source), ("sequence back to ordinary", gpcBackSnapshot, femBackSnapshot, gpcSnapshot) })
        {
            string label = label0 + (supports ? ", support" : ", no support");
            try { Console.WriteLine($"GPC REVIEW FIXES R65-C3-02 ({label}): " + GpcInputComparison.RequireRenumbered("C1 " + label, gpc, femOutput, tolerance, from)); Check(true, label); }
            catch (Exception ex) { Check(false, $"R65-C3-02 ({label}): against the conversion of the FEM output: {ex.Message}"); }
            Oracle(label, gpc);
        }
        Console.WriteLine($"GPC REVIEW FIXES R65-C3-02: {phases} Bridge Phase Model and 2 Bridge Construction Sequence outputs against the conversion of their FEM output and Build Model on their drafts");
    }

    /// <summary>The oracle of P2 for a transformed input: Build Model on its drafts gives the same beams on the same axes, and the same
    /// Straus7 calls (the write plan of the Converter, recorded without the native API).</summary>
    private static void Oracle(string label, GpcModels.Conversion transformed)
    {
        GpcModels.Conversion built;
        try { built = FromDrafts(transformed, transformed.Model.Name); }
        catch (Exception ex) { Check(false, $"R65-C3-02 ({label}): Build Model on the drafts refuses them: {ex.Message}"); return; }
        string a = AxesText(transformed.Model), b = AxesText(built.Model);
        Check(a == b, $"R65-C3-02 ({label}): the axes of the transformed model and of Build Model on its drafts differ\n" + GpcDraftArchiveChecks.FirstDifference(a, b));
        // The Converter refuses a distributed support on a section with a principal angle (PrincipalAngleWithTaperGradientOrSupportRequiresExplicitAxisConvention):
        // the same refusal on both sides then (the cases without the support compare the calls).
        static string Calls(GpcModels.Conversion c) { try { return string.Join("\n", Straus7Recorder.Record(c)); } catch (NotSupportedException ex) { return "REFUSED " + ex.Message; } }
        string x = Calls(transformed), y = Calls(built);
        Check(x == y, $"R65-C3-02 ({label}): the Straus7 calls of the transformed model and of Build Model on its drafts differ\n" + GpcDraftArchiveChecks.FirstDifference(x, y));
        Console.WriteLine($"GPC REVIEW FIXES R65-C3-02 ({label}): Build Model on the drafts: {built.Model.BeamElements.Count} beams on the same axes ({a.Split('\n').Length - 1} lines), {x.Split('\n').Length} Straus7 calls equal");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // R25-01

    private static void CompositeBeamRefusal()
    {
        var steel = One("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var concrete = One("MaterialConcreteComponent", new Dictionary<int, object> { [0] = "C35/45", [1] = 7 });
        var rebar = One("MaterialRebarComponent", new Dictionary<int, object> { [0] = "B450C" });
        var section = One("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        var slab = One("BeamSectionConcreteSlabComponent", new Dictionary<int, object> { [0] = 250.0, [1] = 2500.0, [2] = 1000.0, [3] = 50.0, [4] = 800.0, [5] = 50.0 });
        var valid = One("BeamPropertyCompositeComponent", new Dictionary<int, object> { [0] = "CB", [1] = slab, [2] = section, [3] = concrete, [4] = steel, [5] = rebar });
        var value = BridGHe.Common.Helpers.ModelArchive.Clone((CompositeBeamPropertyModel)P(valid, "Value")); value.NInstant = 0; // not a definition: a modular ratio must be positive
        var refused = Make("GH_CompositeBeamProperty", value);
        string refusal = null;
        try { _ = P(refused, "Definition"); } catch (TargetInvocationException ex) { refusal = ex.InnerException?.Message; }
        Check(refusal != null, "R25-01: the composite property with n0 = 0 is not a definition");
        var m2 = NativeExamples.Create().Single(e => e.Id == "02-simply-supported").Model;
        var femBeam = Make("Fem.GH_Beam", m2.Beams[1]);
        var gpcBeam = GpcOnlyBeam(m2.Beams[1], new Guid("c1000000-0000-4000-8000-0000000000c1"));
        foreach (var (label, start, end, port) in new[] { ("start valid, end refused", valid, refused, "Section End"), ("start refused, end valid", refused, valid, "Section Start") })
        {
            var r = Solve("CompositeBeamComponent", new Dictionary<int, object> { [0] = gpcBeam, [1] = start, [2] = end });
            Check(r.Outputs[0].Count == 0, $"R25-01 ({label}): no output on the GPC-only path");
            Check(r.Errors.Length == 1 && r.Errors[0].StartsWith(port + ": ", StringComparison.Ordinal) && r.Errors[0].EndsWith(refusal ?? "?", StringComparison.Ordinal),
                $"R25-01 ({label}): the error names the section and gives the message of the definition ({string.Join(" / ", r.Errors)})");
            // The FEM path keeps its rule: the composite beam without the definitions (the checks that read it give the error).
            var f = Solve("CompositeBeamComponent", new Dictionary<int, object> { [0] = femBeam, [1] = start, [2] = end });
            Check(f.Outputs[0].Count == 1 && f.Errors.Length == 0 && P(f.Outputs[0][0], "Value") != null, $"R25-01 ({label}): the FEM path gives its composite beam as before");
            Console.WriteLine($"GPC REVIEW FIXES R25-01 ({label}): GPC-only '{string.Join(" / ", r.Errors)}', FEM path {f.Outputs[0].Count} output");
        }
        var both = Solve("CompositeBeamComponent", new Dictionary<int, object> { [0] = gpcBeam, [1] = valid, [2] = valid });
        Check(both.Outputs[0].Count == 1 && both.Errors.Length == 0 && P(both.Outputs[0][0], "StartDefinition") != null, "R25-01: two valid sections still give the composite beam on the GPC-only path");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // P30-01

    private static GH_IO.Serialization.GH_LooseChunk Written(IGH_Goo model)
    {
        var chunk = new GH_IO.Serialization.GH_LooseChunk("model");
        if (!(bool)model.GetType().GetMethod("Write", new[] { typeof(GH_IO.Serialization.GH_IWriter) }).Invoke(model, new object[] { chunk })) throw new Exception("C1: model not written");
        return chunk;
    }

    private static GH_IO.Serialization.GH_LooseChunk Copy(GH_IO.Serialization.GH_LooseChunk chunk)
    {
        var copy = new GH_IO.Serialization.GH_LooseChunk("read"); copy.Deserialize_Binary(chunk.Serialize_Binary()); return copy;
    }

    private static GH_IO.Serialization.GH_LooseChunk WithItem(GH_IO.Serialization.GH_LooseChunk chunk, string item, Func<byte[], byte[]> change)
    {
        var copy = Copy(chunk); var bytes = copy.GetByteArray(item); copy.RemoveItem(item);
        var changed = change(bytes); if (changed != null) copy.SetByteArray(item, changed);
        return copy;
    }

    private static (bool Read, IGH_Goo Model) Read(GH_IO.Serialization.GH_LooseChunk chunk)
    {
        var model = (IGH_Goo)Activator.CreateInstance(T("Fem.GH_Model"));
        return ((bool)model.GetType().GetMethod("Read").Invoke(model, new object[] { chunk }), model);
    }

    private static byte[] Truncated(byte[] b) => b.Take(b.Length / 2).ToArray();
    private static byte[] Corrupted(byte[] b) { var c = b.ToArray(); for (int i = c.Length / 3; i < Math.Min(c.Length, c.Length / 3 + 64); i++) c[i] ^= 0x5A; return c; }

    private static void SavedModelChunks()
    {
        var m2 = NativeExamples.Create().Single(e => e.Id == "02-simply-supported").Model;
        int nmm = (int)ModelModel.ModelUnits.Nmm;
        string drafts = GpcModelDrafts.Chunk, results = GpcResultsFile.Chunk;
        // A model without FEM value (Build Model on GPC-only beams), and one with its FEM value (the drafts of Build Model beside it).
        var gpcOnly = One("BuildModelComponent", new Dictionary<int, object> { [0] = new object[] { GpcOnlyBeam(m2.Beams[0], Guid.NewGuid()), GpcOnlyBeam(m2.Beams[1], Guid.NewGuid()) }, [1] = "C1 saved", [2] = nmm });
        var withFem = One("BuildModelComponent", new Dictionary<int, object> { [0] = m2.Elements.Where(x => x is NodeModel || x is BeamModel).Select(x => (object)(x is NodeModel n ? Make("Fem.GH_Node", n) : Make("Fem.GH_Beam", (BeamModel)x))).ToArray(), [1] = "C1 saved", [2] = nmm });
        foreach (var (label, model) in new[] { ("without FEM value", gpcOnly), ("with FEM value", withFem) })
        {
            var chunk = Written(model);
            Check(chunk.ItemExists(drafts), $"P30-01 ({label}): the drafts are saved with the model");
            var (ok, intact) = Read(Copy(chunk));
            Check(ok && P(intact, "GpcDrafts") != null, $"P30-01 ({label}): the intact model opens with its drafts");
            foreach (var (damage, change) in new (string, Func<byte[], byte[]>)[] { ("truncated", Truncated), ("corrupted", Corrupted), ("trailing byte", x => x.Concat(new byte[] { 0 }).ToArray()) })
                Check(!Read(WithItem(chunk, drafts, change)).Read, $"P30-01 ({label}): drafts {damage}: the model does not open");
            var (formerOk, former) = Read(WithItem(chunk, drafts, _ => null));
            if (label == "with FEM value") Check(formerOk && P(former, "Value") != null, "P30-01: a former file without drafts opens (with its FEM value)");
            else Check(!formerOk, "P30-01: a model without FEM value whose GUIDs have no drafts is damaged");
        }
        // GUIDs of another count (lane 3c) and no GUIDs at all with drafts (allowed: the GUIDs of the drafts).
        {
            var chunk = Written(gpcOnly);
            var wrong = Copy(chunk); wrong.RemoveItem("ElementGuids"); wrong.SetString("ElementGuids", "[\"" + Guid.NewGuid() + "\"]");
            Check(!Read(wrong).Read, "P30-01: GUIDs of another count are damage");
            var none = Copy(chunk); none.RemoveItem("ElementGuids"); none.SetString("ElementGuids", "[]");
            Check(Read(none).Read, "P30-01: drafts without saved GUIDs open (the GUIDs of the drafts)");
            // The analysis components keep their model without drafts: it is written without GUIDs too (they attach both again from the input).
            var cache = new GH_IO.Serialization.GH_LooseChunk("cache");
            Check((bool)gpcOnly.GetType().GetMethod("Write", new[] { typeof(GH_IO.Serialization.GH_IWriter), typeof(bool) }).Invoke(gpcOnly, new object[] { cache, false }) && !cache.ItemExists(drafts)
                && cache.GetString("ElementGuids") == "[]" && Read(Copy(cache)).Read, "P30-01: a model written without drafts has no GUIDs and opens (the cache of the analysis components)");
        }
        // Results that cannot be read: the model opens without them and says so; the copies keep the notice; Decompose Model warns.
        var test = new GpcTestModel("C1 results"); var n1 = test.Node("1", 0, 0, 0); var n2 = test.Node("2", 1000, 0, 0); test.Restrain(n1, true, true, true, true, true, true);
        test.Beam("1", n1, n2); test.Case("Dead"); test.Displacement(n2, "Dead", 0, 0, -1);
        var analysed = test.Analyse();
        foreach (var (label, model) in new[] { ("without FEM value", (IGH_Goo)T("Fem.GH_Model").GetMethod("FromGpc").Invoke(null, new object[] { GpcModels.Convert(m2, 1e-6) })),
            ("with FEM value", (IGH_Goo)T("Fem.GH_Model").GetMethod("FromModel").Invoke(null, new object[] { m2, null })) })
        {
            model.GetType().GetMethod("AttachGpc").Invoke(model, new object[] { analysed });
            var chunk = Written(model);
            Check(chunk.ItemExists(results), $"P30-01 ({label}): the results are saved with the model");
            var (ok, intact) = Read(Copy(chunk));
            Check(ok && (bool)P(intact, "HasAnalysedGpc") && P(intact, "ResultsNotice") == null, $"P30-01 ({label}): intact results are read");
            foreach (var (damage, change) in new (string, Func<byte[], byte[]>)[] { ("truncated", Truncated), ("corrupted", Corrupted) })
            {
                var (read, damaged) = Read(WithItem(chunk, results, change));
                string notice = (string)P(damaged, "ResultsNotice");
                Check(read && !(bool)P(damaged, "HasAnalysedGpc") && notice != null && (string)P(damaged, "LegacyNotice") == notice,
                    $"P30-01 ({label}): results {damage}: the model opens without them, with the notice ({notice})");
                Check((string)P(damaged.Duplicate(), "ResultsNotice") == notice, $"P30-01 ({label}): results {damage}: a copy keeps the notice");
                var decomposed = Solve("DecomposeModelComponent", new Dictionary<int, object> { [0] = damaged });
                Check(decomposed.Warnings.Contains(notice), $"P30-01 ({label}): results {damage}: Decompose Model says the results could not be read ({string.Join(" / ", decomposed.Warnings)})");
            }
        }
        Console.WriteLine("GPC REVIEW FIXES P30-01: damaged drafts refused, damaged results dropped with the notice, GUIDs checked");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // P30-02, P30-04

    private static bool Refused(Action read, string expected = null)
    {
        try { read(); return false; }
        catch (Exception ex) when (ex is System.Runtime.Serialization.SerializationException || ex is System.Xml.XmlException || ex is EndOfStreamException || ex is InvalidDataException)
        { return expected == null || ex.Message.StartsWith(expected, StringComparison.Ordinal); }
    }

    private static void DraftArchiveRecords()
    {
        var fem = ImportDump.BeamData(ModelModel.ModelUnits.Nmm, null);
        var property = GpcModels.BeamProperty(fem.Beams[0].BeamProperty, GpcBeamDrafts.DraftUnits);
        // P30-02: a beam-property record without the data of the property.
        var (data, records) = GpcDraftArchive.Write(property);
        Check(GpcDraftArchive.ReadBeamProperty(data, records).Data != null, "P30-02: a beam property reads back with its data");
        var xml = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(records));
        System.Xml.Linq.Extensions.Remove(xml.Root.Elements().Where(e => e.Name.LocalName == "Property"));
        var withoutData = Encoding.UTF8.GetBytes(xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
        Check(Refused(() => GpcDraftArchive.ReadBeamProperty(data, withoutData), "BridgheDraftRecordsMissingBeamProperty"), "P30-02: a beam-property record without its data is refused (BridgheDraftRecordsMissingBeamProperty)");
        // P30-04: bytes after the records (whitespace, text, an element, a zero) of every kind of record.
        var node = GpcBeamDrafts.FromFemm(fem.Nodes[0]); var beam = GpcBeamDrafts.FromFemm(fem.Beams[1]);
        var (nd, nr) = GpcDraftArchive.Write(node); var (bd, br) = GpcDraftArchive.Write(beam);
        var caseRecords = GpcDraftArchive.Write(beam.Records.LoadCases[0]);
        var kinds = new (string Kind, byte[] Records, Action<byte[]> Read)[]
        {
            ("node", nr, r => GpcDraftArchive.ReadNode(nd, r)), ("beam", br, r => GpcDraftArchive.ReadBeam(bd, r)), ("beam-property", records, r => GpcDraftArchive.ReadBeamProperty(data, r)),
            ("case", caseRecords, r => GpcDraftArchive.ReadCase(r)),
        };
        foreach (var (kind, r, read) in kinds)
        {
            Check(!Refused(() => read(r)), $"P30-04 ({kind}): the records as written read back");
            foreach (var (tail, bytes) in new[] { ("a space", new byte[] { 0x20 }), ("a newline", new byte[] { 0x0A }), ("text", Encoding.ASCII.GetBytes("x")), ("an element", Encoding.ASCII.GetBytes("<a/>")), ("a zero", new byte[] { 0 }) })
                Check(Refused(() => read(r.Concat(bytes).ToArray())), $"P30-04 ({kind}): records followed by {tail} are refused");
        }
        // P30-04: the drafts of a model (BGHDRF2 and BGHDRF1) with bytes after the drafts, counts smaller or larger than the drafts written.
        var model = new GpcModelDrafts(fem.Nodes.Select(n => GpcBeamDrafts.FromFemm(n)), fem.Beams.Select(b => GpcBeamDrafts.FromFemm(b)), Array.Empty<LegacyPlateDraft>());
        var packed = GpcModelDrafts.Write(model);
        Check(GpcModelDrafts.Read(packed).Beams.Count == model.Beams.Count, "P30-04: the drafts of a model read back");
        byte[] raw = Unpack(packed);
        byte[] Counts(byte[] r, int beams) { var c = r.ToArray(); BitConverter.GetBytes(beams).CopyTo(c, 4); return c; }
        Check(Refused(() => GpcModelDrafts.Read(Pack(raw.Concat(new byte[] { 0 }).ToArray())), "BridgheModelDraftsTrailingData"), "P30-04: BGHDRF2 with a byte after the drafts is refused");
        Check(Refused(() => GpcModelDrafts.Read(Pack(Counts(raw, model.Beams.Count - 1))), "BridgheModelDraftsTrailingData"), "P30-04: BGHDRF2 with a beam count smaller than the beams written is refused");
        Check(Refused(() => GpcModelDrafts.Read(Pack(Counts(raw, model.Beams.Count + 1)))), "P30-04: BGHDRF2 with a beam count larger than the beams written is refused");
        var former = FormerDrafts(model);
        Check(GpcModelDrafts.Read(Deflated(former)).Beams.Count == model.Beams.Count, "P30-04: BGHDRF1 reads back");
        Check(Refused(() => GpcModelDrafts.Read(Deflated(former.Concat(new byte[] { 7 }).ToArray())), "BridgheModelDraftsTrailingData"), "P30-04: BGHDRF1 with a byte after the drafts is refused");
        Console.WriteLine("GPC REVIEW FIXES P30-02/P30-04: records without data, trailing bytes and wrong counts refused");
    }

    private static readonly byte[] Header2 = Encoding.ASCII.GetBytes("BGHDRF2"), Header1 = Encoding.ASCII.GetBytes("BGHDRF1");

    private static byte[] Unpack(byte[] packed)
    {
        int start = Header2.Length + 1 + 4; int length = BitConverter.ToInt32(packed, Header2.Length + 1);
        var raw = new byte[length];
        using var decoder = new System.IO.Compression.BrotliDecoder();
        decoder.Decompress(packed.AsSpan(start), raw, out _, out int inflated);
        if (inflated != length) throw new Exception("C1: BGHDRF2 not unpacked");
        return raw;
    }

    private static byte[] Pack(byte[] raw)
    {
        var packed = new byte[Header2.Length + 1 + 4 + System.IO.Compression.BrotliEncoder.GetMaxCompressedLength(raw.Length)];
        Header2.CopyTo(packed, 0); packed[Header2.Length] = 1; BitConverter.GetBytes(raw.Length).CopyTo(packed, Header2.Length + 1);
        if (!System.IO.Compression.BrotliEncoder.TryCompress(raw, packed.AsSpan(Header2.Length + 5), out int written, 5, 24)) throw new Exception("C1: not packed");
        return packed.Take(Header2.Length + 5 + written).ToArray();
    }

    /// <summary>The raw stream of BGHDRF1: the counts and, for every draft, its two chunks of the final writer with their lengths.</summary>
    private static byte[] FormerDrafts(GpcModelDrafts d)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write(d.Nodes.Count); w.Write(d.Beams.Count); w.Write(d.Plates.Count);
        void Pair((byte[] Data, byte[] Records) p) { w.Write(p.Data.Length); w.Write(p.Data); w.Write(p.Records.Length); w.Write(p.Records); }
        foreach (var n in d.Nodes) Pair(GpcDraftArchive.Write(n)); foreach (var b in d.Beams) Pair(GpcDraftArchive.Write(b)); foreach (var p in d.Plates) Pair(GpcDraftArchive.Write(p));
        w.Flush(); return s.ToArray();
    }

    private static byte[] Deflated(byte[] raw)
    {
        using var s = new MemoryStream(); s.Write(Header1); s.WriteByte(1);
        using (var z = new System.IO.Compression.DeflateStream(s, System.IO.Compression.CompressionLevel.Fastest, true)) z.Write(raw);
        return s.ToArray();
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Table Material

    internal static readonly double[] Stresses = { 0, 235, 360 }, Strains = { 0, 235.0 / 210000, 0.15 };
    internal const double Alpha = 1.2e-5, Rho = 7.85e-9;

    /// <summary>The Table Material component with the table and, optionally, α and ρ.</summary>
    internal static IGH_Goo TableMaterialOutput(double? alpha, double? rho, out string[] errors)
    {
        var inputs = new Dictionary<int, object> { [0] = "C1 table", [1] = true, [2] = 0.3, [3] = 80000.0, [6] = Stresses.Cast<object>().ToArray(), [7] = Strains.Cast<object>().ToArray() };
        if (alpha.HasValue) inputs[4] = alpha.Value;
        if (rho.HasValue) inputs[5] = rho.Value;
        var r = Solve("MaterialTableComponent", inputs);
        errors = r.Errors;
        return r.Outputs[0].FirstOrDefault();
    }

    private static void TableMaterial()
    {
        if (plugin == null) plugin = BridgeMergeTests.Initialize();
        var table = Stresses.Select((s, i) => (stress: s, strain: Strains[i])).ToList();
        foreach (var (label, alpha, rho) in new (string, double?, double?)[] { ("without α and ρ", null, null), ("with α", Alpha, null), ("with ρ", null, Rho), ("with α and ρ", Alpha, Rho) })
        {
            var goo = TableMaterialOutput(alpha, rho, out var errors);
            Check(goo != null && errors.Length == 0, $"Table Material ({label}): a material, no error ({string.Join(" / ", errors)})");
            if (goo == null) continue;
            var femValue = (MaterialModel)P(goo, "Value"); var gpc = (GPC.Model.Materials.Material)P(goo, "Gpc");
            Check(femValue.StressStrainTable != null && femValue.StressStrainTable.Select(t => (t.stress, t.strain)).SequenceEqual(table) && femValue.ThermalExpansion == (alpha ?? 0) && femValue.Density == (rho ?? 0),
                $"Table Material ({label}): the FEM value has the table, α and ρ");
            // Everything else as before: the datum is the one of the recipe of 0.3.0 with these α and ρ.
            var recipe = BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Table("C1 table", true, 0.3, 80000, alpha ?? 0, rho ?? 0, table);
            Check(GpcDraftCapture.Text(gpc) == GpcDraftCapture.Text(BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Material(recipe)), $"Table Material ({label}): the GPC datum is the material of the recipe");
            var data = gpc.AnalysisData;
            Check(data?.StressStrainPairs != null && data.StressStrainPairs.Length == 2 * table.Count, $"Table Material ({label}): the GPC material carries the table");
            // The write plan of the Converter (recorded): the table on the beam and the shell, α and ρ in the material data.
            var calls = Straus7Recorder.Record(GpcModels.Convert(TableModel(femValue), 1e-6));
            string Find(string method) => calls.FirstOrDefault(c => c.StartsWith(method + "(", StringComparison.Ordinal));
            string beamProperty = Find("CreateBeamProperty"), plateProperty = Find("CreatePlateProperty");
            Check(beamProperty != null && plateProperty != null && calls.Count(c => c.StartsWith("SetMaterialLaw(", StringComparison.Ordinal)) == 2, $"Table Material ({label}): the beam and shell properties and their two tables are written");
            Check(alpha == null || beamProperty.Contains(R(Alpha)) && plateProperty.Contains(R(Alpha)), $"Table Material ({label}): α written ({beamProperty} | {plateProperty})");
            Check(rho == null || beamProperty.Contains(R(Rho)) && plateProperty.Contains(R(Rho)), $"Table Material ({label}): ρ written");
            Console.WriteLine($"GPC REVIEW FIXES Table Material ({label}): {beamProperty} | {plateProperty} | {Find("SetMaterialLaw")}");
        }
    }

    /// <summary>A beam (solid rectangle) and a thin shell of a material, restrained (the model of the Table Material checks).</summary>
    internal static ModelModel TableModel(MaterialModel material)
    {
        var lc = new LoadCaseModel("P", "P");
        var model = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = "C1 table" }; model.LoadCases.Add(lc);
        var section = new SectionModel { SectionType = SectionModel.SectionTypes.SolidRectangle, B = 100, D = 200 }; section.CalculateSection();
        var beamProperty = new BeamPropertyModel("C1 table beam") { Name = "C1 table beam", Material = material, Section = section, PropertyType = BeamPropertyType.Beam };
        var a = new NodeModel("A", new Point3d(0, 0, 0)); a.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
        var b = new NodeModel("B", new Point3d(3000, 0, 0)); b.Loads.Add(new NodeForceLoadModel(lc, new Vector3d(1000, 0, -1000)));
        var corners = new[] { new Point3d(0, -3000, 0), new Point3d(1000, -3000, 0), new Point3d(1000, -2000, 0), new Point3d(0, -2000, 0) };
        var elements = new List<ElementModel> { a, b, new BeamModel("1", a.Position, b.Position, beamProperty) };
        for (int k = 0; k < 4; k++) { var n = new NodeModel("S" + k, corners[k]); n.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true)); elements.Add(n); }
        elements.Add(new PlateModel("1", corners, new PlatePropertyModel("1") { Name = "C1 table shell", Material = material, PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 20, BendingThickness = 20 }));
        model.AddElements(elements.ToArray());
        return model;
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // The scan of the tracked definitions (P30-02: every BGHGPC1 datum of the examples read by the current reader)

    /// <summary><c>--bghgpc1-scan</c>: every tracked definition of the example folder (git ls-files of the worktree) opened as an archive (not
    /// solved): every chunk with BGHGPC1 data (the records of a datum, the drafts of a model, its results) read by the current reader, by kind.</summary>
    internal static int Scan(string listFile)
    {
        BridgeMergeTests.Initialize();
        var files = File.ReadAllLines(listFile).Where(l => l.EndsWith(".gh", StringComparison.OrdinalIgnoreCase) || l.EndsWith(".ghx", StringComparison.OrdinalIgnoreCase)).ToList();
        int withData = 0, bad = 0;
        foreach (var file in files)
        {
            var archive = new GH_IO.Serialization.GH_Archive();
            if (!archive.ReadFromFile(Path.GetFullPath(file))) { Console.WriteLine($"BGHGPC1 SCAN {file}: NOT READABLE"); bad++; continue; }
            var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal); var errors = new List<string>();
            void Count(string k) => kinds[k] = kinds.TryGetValue(k, out int n) ? n + 1 : 1;
            void Try(string kind, string where, Action read)
            {
                try { read(); Count(kind); }
                catch (Exception ex) { Count(kind + " FAILED"); if (errors.Count < 5) errors.Add(where + ": " + kind + ": " + ex.GetType().Name + " " + ex.Message); }
            }
            void Walk(GH_IO.Serialization.GH_IChunk chunk, string where)
            {
                byte[] Item(string name) => chunk.Items.FirstOrDefault(i => i.Name == name && i.Type == GH_IO.Types.GH_Types.gh_bytearray)?._bytearray;
                byte[] data = Item("Data"), records = Item("BridGHeRecords");
                if (records != null)
                {
                    string kind = "?"; bool report = false;
                    try { var root = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(records)).Root; kind = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Kind")?.Value ?? "?"; report = root.Elements().Any(e => e.Name.LocalName == "HasReport" && e.Value == "true"); }
                    catch (Exception) { }
                    Try(kind, where, kind switch
                    {
                        "node" => () => GpcDraftArchive.ReadNode(data, records), "beam" => () => GpcDraftArchive.ReadBeam(data, records), "plate" => () => GpcDraftArchive.ReadPlate(data, records),
                        "material" => () => GpcDraftArchive.ReadMaterial(data, records, out _), "case" => () => GpcDraftArchive.ReadCase(records), "combination" => () => GpcDraftArchive.ReadCombination(records),
                        "stage" => () => GpcDraftArchive.ReadStage(records), "section" => () => GpcDraftArchive.ReadSection(records), "beam-property" => () => GpcDraftArchive.ReadBeamProperty(data, records),
                        "plate-property" => () => GpcDraftArchive.ReadPlateProperty(data, records),
                        "model" => report ? () => GpcDraftArchive.ReadConversion(data, records) : () => GpcDraftArchive.ReadModel(data, records),
                        _ => () => throw new InvalidDataException("unknown kind"),
                    });
                }
                else if (data != null && !GpcGooArchive.IsLegacy(data))
                    Try("data only", where, () => { try { GpcDraftArchive.ReadMaterial(data); } catch (Exception) { GpcDraftArchive.ReadComposite(data); } });
                if (Item(GpcModelDrafts.Chunk) is byte[] drafts) Try("model drafts", where, () => GpcModelDrafts.Read(drafts));
                if (Item(GpcResultsFile.Chunk) is byte[] results) Try("model results", where, () => GpcResultsFile.FromBytes(results));
                foreach (var c in chunk.Chunks) Walk(c, where + "/" + c.Name + (c.Index > 0 ? "[" + c.Index + "]" : ""));
            }
            Walk(archive.GetRootNode, "");
            if (kinds.Count == 0) continue;
            withData++; if (kinds.Keys.Any(k => k.EndsWith(" FAILED", StringComparison.Ordinal))) bad++;
            Console.WriteLine($"BGHGPC1 SCAN {file}: " + string.Join(", ", kinds.Select(k => k.Key + " " + k.Value)) + (errors.Count == 0 ? "" : " | " + string.Join(" | ", errors)));
        }
        Console.WriteLine($"BGHGPC1 SCAN: {files.Count} tracked definitions, {withData} with BGHGPC1 data, {bad} with a datum not read");
        return bad == 0 ? 0 : 1;
    }
}

/// <summary>C1: the write API of the Converter as one interface, recorded without the native API (<see cref="Straus7Recorder"/>).</summary>
public interface IC1Straus7Recording : IStraus7ModelWriteApi, IStraus7WriteApi, IStraus7DistributionWriteApi, IStraus7MaterialWriteApi, IStraus7NativeSectionWriteApi,
    IStraus7SectionGeometryWriteApi, IStraus7SectionMeshWriteApi, IStraus7InertiaWriteApi, IStraus7ConstraintWriteApi, IStraus7SurfacePressureWriteApi { }

/// <summary>C1: every call of the write plan of a GPC input (<see cref="Straus7ModelWritePlan"/>) as text (exact doubles, arrays, the
/// fingerprint of a native section): what Straus7 would receive, compared without the native API.</summary>
public class Straus7Recorder : DispatchProxy
{
    private readonly List<string> calls = new();

    internal static List<string> Record(GpcModels.Conversion input)
    {
        var plan = Straus7ModelWritePlan.Prepare(input.Model, null, null, input.Report.NativeSections);
        var api = DispatchProxy.Create<IC1Straus7Recording, Straus7Recorder>();
        plan.Write(api, input.Model);
        return ((Straus7Recorder)(object)api).calls;
    }

    private static string F(object a) => a switch
    {
        null => "null", double d => d.ToString("R", CultureInfo.InvariantCulture), float f => ((double)f).ToString("R", CultureInfo.InvariantCulture), string s => "'" + s + "'",
        Straus7SectionDefinition n => "native:" + n.Fingerprint(), Array x => "[" + string.Join(" ", x.Cast<object>().Select(F)) + "]", Enum e => e.GetType().Name + "." + e,
        IFormattable v => v.ToString(null, CultureInfo.InvariantCulture), _ => a.GetType().Name + ":" + a,
    };

    protected override object Invoke(MethodInfo method, object[] args)
    {
        calls.Add(method.Name + "(" + string.Join(", ", (args ?? Array.Empty<object>()).Select(F)) + ")");
        return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
}
#endif
