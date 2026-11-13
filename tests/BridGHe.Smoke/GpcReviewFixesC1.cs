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
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
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
/// principal axes (offset axes, load eccentricity, local load frames), against Build Model on the drafts they output (the oracle of P2), with
/// the Straus7 calls of the write plan recorded (no native API);</item>
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

    /// <summary>A beam by its draft alone (the GPC-only path), with the GH GUID given.</summary>
    private static IGH_Goo GpcOnlyBeam(LegacyBeamDraft draft, Guid gh) => (IGH_Goo)T("Fem.GH_Beam").GetMethod("FromGpc").Invoke(null, new object[] { draft, (Guid?)gh });

    /// <summary>P2CB (13/11): a beam of the elastic beam property of the examples between two points of the XY plane, as the components give it
    /// (before: a FEM beam on the property of example 02).</summary>
    private static LegacyBeamDraft ElasticBeam(double x0, double y0, double x1, double y1, string tag) =>
        GpcBeamBuilders.New(new GPC.Geometry.Point3d(x0, y0, 0), new GPC.Geometry.Point3d(x1, y1, 0), GpcNativeExamples.Property(), null, 0, 0, 0, R4bInputs.Id("c1:" + tag), "");

    /// <summary>P2CB (13/11): example 02 of the GPC catalogue (the inputs of Build Model and its GPC input; before: the FEM example 02).</summary>
    private static GpcNativeExamples.Example Example02() => GpcNativeExamples.Create().Single(e => e.Id == "02-simply-supported");

    /// <summary>The units input of Build Model: N-mm (the former <c>ModelUnits.Nmm</c>).</summary>
    private const int Nmm = 2;

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // R65-C3-01

    private static void CreatedNodes()
    {
        IGH_Goo Build(string name, params IGH_Goo[] elements) => One("BuildModelComponent", new Dictionary<int, object> { [0] = elements.Cast<object>().ToArray(), [1] = name, [2] = Nmm });
        List<IGH_Goo> Nodes(IGH_Goo model) => Solve("DecomposeModelComponent", new Dictionary<int, object> { [0] = model }).Outputs[0];
        GpcModelDrafts Drafts(IGH_Goo model) => (GpcModelDrafts)P(model, "GpcDrafts");
        Guid[] Guids(IGH_Goo model) => (Guid[])model.GetType().GetMethod("ElementGuidsOf").Invoke(model, new object[] { Drafts(model) });
        static string Key(GPC.Geometry.Point3d p) => R(p.X) + " " + R(p.Y) + " " + R(p.Z);

        var beamA = GpcOnlyBeam(ElasticBeam(0, 0, 1000, 0, "a"), new Guid("c1000000-0000-4000-8000-00000000000a"));
        var beamB = GpcOnlyBeam(ElasticBeam(0, 1000, 1000, 1000, "b"), new Guid("c1000000-0000-4000-8000-00000000000b"));
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

    /// <summary>The steel of the swaps (Steel: S355, E as given, ν 0.3, density 7.85E-9, α 1.2E-5).</summary>
    private static GPC.Model.Materials.Material Steel(double modulus = 210000) =>
        GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Kind = "Steel", Name = "S355", Modulus = modulus, PoissonRatio = 0.3, Density = 7.85e-9, ThermalExpansion = 1.2e-5 });

    /// <summary>The ordinary I 500 (flanges 200 × 20, web 10) of Beam Property (type Beam).</summary>
    private static LegacyBeamProperty Ordinary(string name = "PO", double modulus = 210000) =>
        GpcPropertyBuilders.Beam(name, 5, GpcSectionBuilders.Standard(BridgeSectionKind.I, 500, b1: 200, b2: 200, t1: 20, t2: 20, t3: 10), Steel(modulus), "Steel");

    /// <summary>The double angle of the Angle component (Double Sections: the former mirror Left, gap A 8).</summary>
    private static LegacyBeamProperty DoubleAngle(string name = "P2L") =>
        GpcPropertyBuilders.Beam(name, 5, GpcSectionBuilders.Standard(BridgeSectionKind.Angle, 100, b: 150, t1: 10, t2: 12, mirror: 2, gapA: 8), Steel(), "Steel");

    /// <summary>The loads of a beam in Q: a local distributed load along Y (−2), a global distributed load along Z (−3), a local point load
    /// (0, −5, 1) at 0.4 and, with <paramref name="support"/>, a distributed support (+2 axis, k 3). P2CB: the global point load of the former
    /// FEM beam ((1, 0, −4) at 0.6) has no component (Beam Point Load gives its loads in the principal axes): not in the GPC model.</summary>
    private static LegacyBeamDraft Loads(GpcNativeExamples.Builder b, LegacyBeamDraft beam, bool support)
    {
        beam = GpcBeamBuilders.WithDistributedLoad(beam, b.Case("Q"), BridgeLoadSchema.Uniform, false, "Y", -2, 0, 0, 0, 0, 0, null, b.Next("local distributed"));
        beam = GpcBeamBuilders.WithDistributedLoad(beam, b.Case("Q"), BridgeLoadSchema.Uniform, false, "Z", -3, 0, 0, 0, 0, 0, true, b.Next("global distributed"));
        beam = GpcBeamBuilders.WithPointLoad(beam, b.Case("Q"), false, 0.4, new GPC.Geometry.Vector3d(0, -5, 1), b.Next("local point"));
        if (support) beam = GpcBeamBuilders.WithSupport(beam, 3, 0, "positive2Axis", "TensionCompression", b.Next("support"));
        return beam;
    }

    /// <summary>Two beams of an ordinary I property: B1 at 30° with the offset (5,7), B2 at 0° without offset, each with a local and a global
    /// distributed load, a local point load and a distributed support (the GPC data of the components; before: a FEM model).</summary>
    private static GpcModels.Conversion SwapModel(bool support)
    {
        var b = new GpcNativeExamples.Builder("c1-swaps-" + support);
        int n1 = b.Node(0, 0, 0, "1", null), n2 = b.Node(3000, 0, 0, "2", null), n3 = b.Node(6000, 0, 0, "3", null);
        b.Restrain(n1, true, true, true, true, true, true); b.Restrain(n3, true, true, true, false, false, false);
        var ordinary = Ordinary();
        // The former BeamId: the BridGHe ID and Element ID of each beam (Build Model names the beam after it; the phase models select by it).
        int b1 = R4bInputs.Beam(b, n1, n2, ordinary, null, "B1", "B1", 30, 5, 7), b2 = R4bInputs.Beam(b, n2, n3, ordinary, null, "B2", "B2");
        foreach (int k in new[] { b1, b2 }) b.Beams[k] = Loads(b, b.Beams[k], support);
        return R4bInputs.Build(b, "C1 swaps");
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
        var source = SwapModel(supports); var ids = new[] { "B1", "B2" };
        var doubleAngle = DoubleAngle(); var ordinary = Ordinary(); var stiffer = Ordinary("PO stiffer", 1.5 * 210000);
        static Guid Id(string tag) => GpcIdentity.Derive(GpcIdentity.NameSpace, tag);
        int phases = 0;
        foreach (bool clear in new[] { false, true })
        {
            // ordinary → double angle → ordinary, and ordinary → ordinary (bit for bit as before the fix: the turn does not change).
            var gpcTo = GpcModelTransforms.PhaseModel(source, "To 2L", ids, doubleAngle, clear, Id("c1-phase-to:" + clear));
            var gpcBack = GpcModelTransforms.PhaseModel(gpcTo, "Back", ids, ordinary, clear, Id("c1-phase-back:" + clear));
            var gpcSame = GpcModelTransforms.PhaseModel(source, "Same", ids, stiffer, clear, Id("c1-phase-same:" + clear));
            foreach (var (label, gpc) in new[] { ("phase to double angle", gpcTo), ("phase back to ordinary", gpcBack), ("phase ordinary to ordinary", gpcSame) })
            {
                string name = label + (clear ? " clear loads" : " keep loads") + (supports ? ", support" : ", no support");
                // P2CB (13/11, rule U3): the check against the conversion of the FEM output of 0.3.0 Phase Model is retired by name (P2rRetiredP2cb).
                Oracle(name, gpc);
                phases++;
            }
            Check(!AxesText(gpcTo.Model).Split('\n')[0].Contains(" offset axes - "), "R65-C3-02: the beam with an offset has offset axes after the swap");
        }

        // Bridge Construction Sequence (P2CB, 13/11, rule U3): its two outputs were the girders of the FEM simple bridge (an offset, the loads and a
        // support on the first girder beam) on the double angle and back, against the FEM sequence and Build Model on their drafts; the GPC sequence
        // needs the members of Bridge Member Assignment, which the GPC simple bridge gives without drafts: retired by name (P2rRetiredP2cb).
        Console.WriteLine($"GPC REVIEW FIXES R65-C3-02: {phases} Bridge Phase Model outputs against Build Model on their drafts");
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
        // P2CB (13/11, rule U3): the refused section was a FEM value (the composite property of the grillage fixture with n0 = 0, given to the GH
        // type of a FEM composite property): the GPC components give no composite property that is not a definition. Its checks (not a definition,
        // no output and the error of each end) are retired by name with the FEM types (P2rRetiredP2cb); two valid sections stay.
        var gpcBeam = GpcOnlyBeam(Example02().Inputs.Beams[1], new Guid("c1000000-0000-4000-8000-0000000000c1"));
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
        var example = Example02();
        string drafts = GpcModelDrafts.Chunk, results = GpcResultsFile.Chunk;
        // A model without FEM value (Build Model on GPC-only beams), and one with its FEM value (the drafts of Build Model beside it).
        // A model without FEM value (Build Model on GPC-only beams). P2CB (13/11, rule U3): the model with its FEM value (Build Model on FEM-backed
        // elements) is retired by name with the FEM types (P2rRetiredP2cb).
        var gpcOnly = One("BuildModelComponent", new Dictionary<int, object> { [0] = new object[] { GpcOnlyBeam(example.Inputs.Beams[0], Guid.NewGuid()), GpcOnlyBeam(example.Inputs.Beams[1], Guid.NewGuid()) }, [1] = "C1 saved", [2] = Nmm });
        foreach (var (label, model) in new[] { ("without FEM value", gpcOnly) })
        {
            var chunk = Written(model);
            Check(chunk.ItemExists(drafts), $"P30-01 ({label}): the drafts are saved with the model");
            var (ok, intact) = Read(Copy(chunk));
            Check(ok && P(intact, "GpcDrafts") != null, $"P30-01 ({label}): the intact model opens with its drafts");
            foreach (var (damage, change) in new (string, Func<byte[], byte[]>)[] { ("truncated", Truncated), ("corrupted", Corrupted), ("trailing byte", x => x.Concat(new byte[] { 0 }).ToArray()) })
                Check(!Read(WithItem(chunk, drafts, change)).Read, $"P30-01 ({label}): drafts {damage}: the model does not open");
            var (formerOk, former) = Read(WithItem(chunk, drafts, _ => null));
            // P2R (T3): Build Model gives no FEM value since P2K, also on FEM-backed elements, and nothing writes FemmR6 since P2D: without its drafts
            // either model is damaged (GUIDs without drafts, P30-01). A former development file (its datum, FemmR6 and the GUIDs of its FEM elements,
            // no drafts) opens with the drafts and GUIDs of its FemmR6: regression p2d (P2D).
            Check(!formerOk, $"P30-01 ({label}): a model without FEM value whose GUIDs have no drafts is damaged");
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
        foreach (var (label, model) in new[] { ("without FEM value", (IGH_Goo)T("Fem.GH_Model").GetMethod("FromGpc").Invoke(null, new object[] { example.Input })) })
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
        // P2CB (13/11): the drafts of example 02 of the GPC catalogue (before: the drafts of the FEM beam data of ImportDump).
        var example = Example02().Inputs;
        var property = GpcNativeExamples.Property(); // the property of the beams of example 02
        // P30-02: a beam-property record without the data of the property.
        var (data, records) = GpcDraftArchive.Write(property);
        Check(GpcDraftArchive.ReadBeamProperty(data, records).Data != null, "P30-02: a beam property reads back with its data");
        var xml = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(records));
        System.Xml.Linq.Extensions.Remove(xml.Root.Elements().Where(e => e.Name.LocalName == "Property"));
        var withoutData = Encoding.UTF8.GetBytes(xml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
        Check(Refused(() => GpcDraftArchive.ReadBeamProperty(data, withoutData), "BridgheDraftRecordsMissingBeamProperty"), "P30-02: a beam-property record without its data is refused (BridgheDraftRecordsMissingBeamProperty)");
        // P30-04: bytes after the records (whitespace, text, an element, a zero) of every kind of record.
        var node = example.Nodes[0]; var beam = example.Beams[1];
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
        var model = new GpcModelDrafts(example.Nodes, example.Beams, Array.Empty<LegacyPlateDraft>());
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
            var gpc = (GPC.Model.Materials.Material)P(goo, "Gpc");
            // P2R (T3): Table Material gives no FEM value since P2K/P2D (before: the FEM value had the table, α and ρ); the datum is checked below.
            Check(P(goo, "Value") == null, $"Table Material ({label}): no FEM value");
            // Everything else as before: the datum is the one of the recipe of 0.3.0 with these α and ρ.
            var recipe = BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Table("C1 table", true, 0.3, 80000, alpha ?? 0, rho ?? 0, table);
            Check(GpcDraftCapture.Text(gpc) == GpcDraftCapture.Text(BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Material(recipe)), $"Table Material ({label}): the GPC datum is the material of the recipe");
            var data = gpc.AnalysisData;
            Check(data?.StressStrainPairs != null && data.StressStrainPairs.Length == 2 * table.Count, $"Table Material ({label}): the GPC material carries the table");
            // The write plan of the Converter (recorded): the table on the beam and the shell, α and ρ in the material data.
            // P2R (T3): the model of the checks on a placeholder material, with the GPC material of the output (as the native check). P2CB: the model
            // is the GPC data of the components (before: the conversion of a FEM model).
            var conversion = TableModel(Placeholder());
            GpcReviewFixesC1Native.SwapMaterial(conversion.Model, gpc);
            var calls = Straus7Recorder.Record(conversion);
            string Find(string method) => calls.FirstOrDefault(c => c.StartsWith(method + "(", StringComparison.Ordinal));
            string beamProperty = Find("CreateBeamProperty"), plateProperty = Find("CreatePlateProperty");
            Check(beamProperty != null && plateProperty != null && calls.Count(c => c.StartsWith("SetMaterialLaw(", StringComparison.Ordinal)) == 2, $"Table Material ({label}): the beam and shell properties and their two tables are written");
            Check(alpha == null || beamProperty.Contains(R(Alpha)) && plateProperty.Contains(R(Alpha)), $"Table Material ({label}): α written ({beamProperty} | {plateProperty})");
            Check(rho == null || beamProperty.Contains(R(Rho)) && plateProperty.Contains(R(Rho)), $"Table Material ({label}): ρ written");
            Console.WriteLine($"GPC REVIEW FIXES Table Material ({label}): {beamProperty} | {plateProperty} | {Find("SetMaterialLaw")}");
        }
    }

    /// <summary>The placeholder material of the Table Material checks (E 210000, ν 0.3; the GPC material of the output takes its place).</summary>
    internal static GPC.Model.Materials.Material Placeholder() =>
        GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Name = "C1 table placeholder", Modulus = 210000, PoissonRatio = 0.3 });

    /// <summary>A beam (solid rectangle 100 × 200) and a thin shell (20) of a material, restrained (the model of the Table Material checks), as
    /// the components give it (P2CB, 13/11: before, a FEM model).</summary>
    internal static GpcModels.Conversion TableModel(GPC.Model.Materials.Material material, string beamProperty = "C1 table beam", string shellProperty = "C1 table shell")
    {
        var b = new GpcNativeExamples.Builder("c1-table-" + beamProperty);
        int a = b.Node(0, 0, 0, "A", null); b.Restrain(a, true, true, true, true, true, true);
        int e = b.Node(3000, 0, 0, "B", null); b.Force(e, "P", 1000, 0, -1000);
        R4bInputs.Beam(b, a, e, GpcPropertyBuilders.Beam(beamProperty, 5, GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, 200, b: 100), material), null, "1", "1");
        var corners = new[] { (0.0, -3000.0, 0.0), (1000.0, -3000.0, 0.0), (1000.0, -2000.0, 0.0), (0.0, -2000.0, 0.0) };
        for (int k = 0; k < 4; k++) { int n = b.Node(corners[k].Item1, corners[k].Item2, corners[k].Item3, "S" + k, null); b.Restrain(n, true, true, true, true, true, true); }
        b.Plates.Add(GpcPlateBuilders.Create(corners, GpcPropertyBuilders.Plate(shellProperty, 0, material, 20, 20), null, 0, 0, "1", b.Next("plate")));
        return R4bInputs.Build(b, "C1 table");
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
