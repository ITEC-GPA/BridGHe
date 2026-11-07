#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.5 P1 (coordinator, 7/11): the components of a lane replayed with their inputs given by the GPC data alone. In every definition of the
/// Build Model scope (the example scan, solved without analyses), each instance of a component of the lane is copied into an empty
/// document twice: once with the inputs it had (the FEM-backed GH types of today), once with the same inputs where every GH type of the
/// converted kinds is replaced by the type made from its GPC datum alone (<c>FromGpc</c>, no FEM value). The two copies must give the same
/// outputs: the same trees, the same messages, every GH type with the same GPC datum (the text of the datum, exact), every other value the same
/// text; the outputs of the GPC-only copy built from GPC-only inputs have no FEM value. Regression <c>gpc-only-&lt;lane&gt;</c> (not native).
/// </summary>
internal static class GpcOnlyReplay
{
    /// <summary>Lane 1, elements: the consumers and producers of nodes, beams and plates (attributes, loads, filters and tools).</summary>
    internal static readonly string[] Lane1Types =
    {
        // Attributes
        "BeamOutputStationsComponent", "BeamReleasesComponent", "BeamStringGroupComponent", "BeamSupportComponent", "BeamTaperComponent",
        "ElementIdComponent", "LoadPatchTypeComponent", "NodeRestraintsComponent", "NodeStiffnessComponent", "PlateSupportComponent", "SuperElementComponent",
        // Loads
        "BeamDistributedLoadComponent", "BeamDistributedMomentComponent", "BeamGradientLoadComponent", "BeamPointLoadComponent", "BeamPointMomentComponent",
        "BeamPreLoadComponent", "NodeDisplacementLoadComponent", "NodeLoadComponent", "NodeMomentComponent", "NodeTemperatureComponent",
        "PlateNSMassComponent", "PlatePreStressComponent", "PlatePressureComponent",
        // Tools
        "AlignBeamAxesComponent", "AlignPlateAxesComponent", "BeamFilterComponent", "BeamOverrideComponent", "ElementFilterComponent",
        "MergeGeometryComponent", "NodeFilterComponent", "PlateFilterComponent",
    };

    /// <summary>Lane 1b, bridge generators (passo 73): the components of <c>Components/Geometry</c> that build or take nodes, beams and plates.
    /// The generators without an input element (bearings, links, connection beams, deck mesh, slab grillage, member assignment) are replayed
    /// with <c>GpcOnlyElements.GeneratorsWithoutFem</c> on in the GPC-only copy: they give their elements by their drafts alone (the path of
    /// P2). The others (Beam Moment Dispatch, Beam Offsets, Segment Override, Connections in mode 2) take beams given by their drafts alone.</summary>
    internal static readonly string[] Lane1bTypes =
    {
        "BeamMomentDispatchComponent", "BridgeBeamOffsetsComponent", "BridgeBearingsComponent", "BridgeConnectionBeamsComponent", "BridgeConnectionsComponent",
        "BridgeDeckMeshComponent", "BridgeFemMembersComponent", "BridgeMemberAssignmentComponent", "BridgeRigidLinksComponent", "BridgeSegmentOverrideComponent",
        "BridgeSlabGrillageComponent",
    };

    /// <summary>The GH element types converted to their GPC data alone in lane 1.</summary>
    internal static readonly string[] ElementGoo = { "GH_Node", "GH_Beam", "GH_Plate" };

    private static object Prop(object o, string name) => o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);

    /// <summary>The GH type made from the GPC datum alone of a FEM-backed one, or the same goo (another kind, or no datum).</summary>
    private static IGH_Goo GpcOnly(IGH_Goo goo, ISet<string> kinds)
    {
        if (goo == null || !kinds.Contains(goo.GetType().Name) || goo.ScriptVariable() == null) return goo;
        var datum = Prop(goo, "Gpc");
        if (datum == null) return goo;
        var from = goo.GetType().GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static);
        var args = from.GetParameters().Length == 2 ? new[] { datum, Prop(goo, "Guid") } : new[] { datum };
        var only = (IGH_Goo)from.Invoke(null, args);
        foreach (var name in new[] { "AnalysedGpc", "AnalysedGpcModel" })
            if (goo.GetType().GetProperty(name) is { CanWrite: true } p) p.SetValue(only, p.GetValue(goo));
        return only;
    }

    /// <summary>A copy of a component (its settings and persistent data) in a new document.</summary>
    private static GH_Component Copy(GH_Component c, GH_Document into)
    {
        var archive = new GH_Archive();
        if (!archive.AppendObject(c, "component")) throw new Exception("GPC-ONLY REPLAY: " + c.Name + " not written");
        var copy = (GH_Component)Grasshopper.Instances.ComponentServer.EmitObject(c.ComponentGuid);
        copy.CreateAttributes();
        if (!archive.ExtractObject(copy, "component")) throw new Exception("GPC-ONLY REPLAY: " + c.Name + " not read");
        into.AddObject(copy, false);
        return copy;
    }

    private static void SetInput(IGH_Param param, IGH_Structure data, Func<IGH_Goo, IGH_Goo> map)
    {
        param.RemoveAllSources();
        var persistent = param.GetType().GetProperty("PersistentData")?.GetValue(param) ?? throw new Exception("GPC-ONLY REPLAY: input " + param.Name + " is not persistent");
        persistent.GetType().GetMethod("Clear").Invoke(persistent, null);
        var append = persistent.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(GH_Path));
        var ensure = persistent.GetType().GetMethod("EnsurePath", new[] { typeof(GH_Path) });
        for (int b = 0; b < data.PathCount; b++)
        {
            var path = data.get_Path(b);
            var branch = data.get_Branch(b);
            ensure?.Invoke(persistent, new object[] { path });
            foreach (var item in branch) append.Invoke(persistent, new object[] { item == null ? null : map((IGH_Goo)item), path });
        }
    }

    /// <summary>The text of an output: a GH type with a GPC datum by the text of its datum with every GUID replaced by its ordinal in the text
    /// (coordinator, 7/11: the outputs without a FEM value take identities derived from the input draft, the FEM-backed ones the identity of
    /// the conversion of their FEM value until P2, so the lane test compares the data modulo GUIDs, the criterion of the captures).
    /// <para>Declared rule (identity only): the bridge data of a beam draft are keyed by the GUID of the beam, so their fingerprint (the line
    /// «bridge &lt;GUID&gt; &lt;fingerprint&gt;» of the text) changes with the identity; the line is compared by the bridge values of the beam
    /// instead (<see cref="GpcDraftCapture.BridgeValues"/>: member, super element, section state, composite sections; no GUID), as the captures
    /// compare a renamed Build Model output (R6.3).</para></summary>
    private static string Text(IGH_Goo goo)
    {
        if (goo == null) return "null";
        var datum = Prop(goo, "Gpc");
        if (datum == null) return goo.GetType().Name + " " + goo;
        string text = GpcNullValueChecks.TextOf(datum);
        if (datum is BridGHe.Common.Gpc.Legacy.LegacyBeamDraft { Bridge: { } bridge })
        {
            var one = new GPC.Checkers.CompositeBridge.BridgeModelData(); one.Set(bridge);
            string line = "bridge " + bridge.BeamGuid + " " + one.Fingerprint();
            if (text.Contains(line, StringComparison.Ordinal))
                text = text.Replace(line, "bridge " + bridge.BeamGuid + " values " + string.Join(" ; ", GpcDraftCapture.BridgeValues(one)));
        }
        // The same for the bridge data of the report of a draft (the line «bridge <count> <fingerprint>»).
        var report = datum switch
        {
            BridGHe.Common.Gpc.Legacy.LegacyBeamDraft b => b.Report, BridGHe.Common.Gpc.Legacy.LegacyNodeDraft n => n.Report,
            BridGHe.Common.Gpc.Legacy.LegacyPlateDraft p => p.Report, _ => null,
        };
        if (report?.Bridge is { } data)
        {
            string line = "bridge " + data.Count + " " + data.Fingerprint();
            if (text.Contains(line, StringComparison.Ordinal))
                text = text.Replace(line, "bridge " + data.Count + " values " + string.Join(" ; ", GpcDraftCapture.BridgeValues(data)));
        }
        return goo.GetType().Name + " " + GpcDraftCapture.WithoutGuids(text);
    }


    private sealed class Run
    {
        internal List<string> Outputs = new List<string>();
        /// <summary>The same texts with their GUIDs (the determinism of the identities derived without a FEM value).</summary>
        internal List<string> Raw = new List<string>();
        internal List<string> Messages = new List<string>();
        internal int FemOutputs;
    }

    /// <summary>Lane 1b: the switch of the generators without FEM values (<c>GpcOnlyElements.GeneratorsWithoutFem</c>), on for the GPC-only
    /// copies of the generators lane only.</summary>
    private static PropertyInfo generators;
    private static bool generatorsLane;

    private static Run Solve(GH_Component original, Func<IGH_Goo, IGH_Goo> map, ISet<string> kinds, bool gpcOnly = false)
    {
        var doc = new GH_Document();
        bool on = gpcOnly && generatorsLane;
        try
        {
            var copy = Copy(original, doc);
            for (int i = 0; i < original.Params.Input.Count; i++)
                SetInput(copy.Params.Input[i], original.Params.Input[i].VolatileData, map);
            if (on) generators.SetValue(null, true);
            try { doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent); }
            finally { if (on) generators.SetValue(null, false); }
            var run = new Run();
            foreach (var level in new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark })
                foreach (var m in copy.RuntimeMessages(level)) run.Messages.Add(level + ": " + m);
            foreach (var p in copy.Params.Output)
            {
                var data = p.VolatileData;
                for (int b = 0; b < data.PathCount; b++)
                {
                    var branch = data.get_Branch(b);
                    run.Outputs.Add($"{p.Name} {data.get_Path(b)} {branch.Count}"); run.Raw.Add(run.Outputs[^1]);
                    foreach (var item in branch)
                    {
                        var goo = item as IGH_Goo;
                        run.Outputs.Add(Text(goo));
                        var datum = goo == null ? null : Prop(goo, "Gpc");
                        run.Raw.Add(datum == null ? run.Outputs[^1] : goo.GetType().Name + " " + GpcNullValueChecks.TextOf(datum));
                        if (goo != null && kinds.Contains(goo.GetType().Name) && goo.ScriptVariable() != null) run.FemOutputs++;
                    }
                }
            }
            return run;
        }
        finally { doc.Dispose(); }
    }

    /// <summary>One replay of a component instance: the copy with the inputs of today and the copy with GPC-only inputs give the same outputs
    /// modulo GUIDs and the same messages; the GPC-only outputs have no FEM value; a second GPC-only copy gives the same GUIDs.</summary>
    private static void Replay(GH_Component c, string where, ISet<string> kinds, IDictionary<string, (int Instances, int Items)> byType, List<string> failures)
    {
        Run fem, gpc, again;
        try { fem = Solve(c, g => g.Duplicate(), kinds); }
        catch (Exception ex) { failures.Add(where + ": the copy with the inputs of today fails: " + ex.Message); return; }
        try { gpc = Solve(c, g => GpcOnly(g, kinds), kinds, true); again = Solve(c, g => GpcOnly(g, kinds), kinds, true); }
        catch (Exception ex) { failures.Add(where + ": the copy with GPC-only inputs fails: " + ex.GetBaseException().Message); return; }
        var (n, items) = byType.TryGetValue(c.GetType().Name, out var v) ? v : (0, 0);
        byType[c.GetType().Name] = (n + 1, items + fem.Outputs.Count);
        if (!fem.Messages.SequenceEqual(gpc.Messages)) failures.Add(where + ": messages " + string.Join(" / ", fem.Messages) + " -> " + string.Join(" / ", gpc.Messages));
        int diff = fem.Outputs.Zip(gpc.Outputs).Count(p => p.First != p.Second);
        if (fem.Outputs.Count != gpc.Outputs.Count || diff != 0)
        {
            var first = fem.Outputs.Zip(gpc.Outputs).FirstOrDefault(p => p.First != p.Second);
            failures.Add($"{where}: outputs {fem.Outputs.Count} -> {gpc.Outputs.Count}, {diff} different; first (FEM-backed -> GPC-only):" + Environment.NewLine
                + Differences(first.First ?? "<none>", first.Second ?? "<none>"));
        }
        if (gpc.FemOutputs != 0) failures.Add($"{where}: {gpc.FemOutputs} outputs with a FEM value from GPC-only inputs");
        if (!gpc.Raw.SequenceEqual(again.Raw)) failures.Add($"{where}: two GPC-only copies give other GUIDs (identities not deterministic)");
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // In-memory cases (coordinator, 7/11): the component types of the lane that no example of the scope uses.

    private static Assembly plugin;
    private static Type T(string name) => plugin.GetType("BridGHe.Grasshopper.DataTypes." + name, true);

    /// <summary>The FEM-backed GH type as the components make it: the constructor from the FEM value (optional parameters at their defaults).</summary>
    private static IGH_Goo Make(string type, object value)
    {
        var t = T(type);
        var ctor = t.GetConstructors().Where(c => c.GetParameters().Length >= 1 && c.GetParameters()[0].ParameterType.IsInstanceOfType(value)
            && c.GetParameters().Skip(1).All(p => p.HasDefaultValue)).OrderBy(c => c.GetParameters().Length).First();
        return (IGH_Goo)ctor.Invoke(new[] { value }.Concat(ctor.GetParameters().Skip(1).Select(p => p.DefaultValue)).ToArray());
    }

    private static IGH_Goo Goo(object v) => v switch
    {
        IGH_Goo g => g,
        double d => new GH_Number(d),
        int i => new GH_Integer(i),
        bool b => new GH_Boolean(b),
        string s => new GH_String(s),
        Rhino.Geometry.Vector3d x => new GH_Vector(x),
        Rhino.Geometry.Point3d p => new GH_Point(p),
        Rhino.Geometry.Line l => new GH_Line(l),
        _ => throw new ArgumentException("GPC-ONLY REPLAY: no goo for " + v?.GetType().Name),
    };

    /// <summary>The in-memory cases of lane 1: component type, label, inputs by index (one value, or an array for a list; the others keep their
    /// defaults). Elements of the examples 01 (beams, nodes), 02 (a beam with a distributed load) and 03 (a quadrilateral plate, and a triangle on
    /// three of its corners) with a load case of their own.</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane1Cases()
    {
        var examples = NativeExamples.Create().ToDictionary(e => e.Id, e => e.Model);
        var m1 = examples["01-cantilever"]; var m2 = examples["02-simply-supported"]; var m3 = examples["03-plate-patch"];
        var beam = Make("Fem.GH_Beam", m2.Beams[1]); var beam2 = Make("Fem.GH_Beam", m2.Beams[2]); var loaded = Make("Fem.GH_Beam", m2.Beams[0]);
        var node = Make("Fem.GH_Node", m1.Nodes.Last()); var node2 = Make("Fem.GH_Node", m1.Nodes.First());
        var quad = m3.Plates.Single();
        var plate = Make("Fem.GH_Plate", quad);
        var tri = Make("Fem.GH_Plate", new PlateModel("2", quad.Points.Take(3).ToArray(), quad.PlateProperty));
        var lc = Make("Fem.GH_LoadCase", new LoadCaseModel("Replay", "Replay"));
        var along = m2.Beams[1].PointTo - m2.Beams[1].PointFrom;
        var cases = new List<(string, string, Dictionary<int, object>)>
        {
            ("BeamReleasesComponent", "partial releases", new Dictionary<int, object> { [0] = loaded, [3] = 0.0, [9] = 5.0, [12] = 0.0 }),
            ("BeamStringGroupComponent", "two beams", new Dictionary<int, object> { [0] = new object[] { beam, loaded }, [1] = 3 }),
            ("BeamSupportComponent", "compression only", new Dictionary<int, object> { [0] = beam, [1] = 1000.0, [2] = 1, [3] = true, [4] = 0.5 }),
            ("BeamTaperComponent", "taper", new Dictionary<int, object> { [0] = loaded, [1] = 1.0, [2] = 0.8, [3] = 0.5, [4] = 0.6, [5] = 0, [6] = 2 }),
            ("LoadPatchTypeComponent", "Auto1 on a quadrilateral", new Dictionary<int, object> { [0] = plate, [1] = 0, [2] = new object[] { 1 } }),
            ("LoadPatchTypeComponent", "Angle Split on a triangle (refused)", new Dictionary<int, object> { [0] = tri, [1] = 4, [2] = new object[] { 1, 2 } }),
            ("LoadPatchTypeComponent", "too many edges (refused)", new Dictionary<int, object> { [0] = tri, [1] = 5, [2] = new object[] { 0, 1, 2, 3 }, [3] = new object[] { 1.0, 1.0, 1.0, 1.0 } }),
            ("NodeStiffnessComponent", "springs", new Dictionary<int, object> { [0] = node, [1] = new Rhino.Geometry.Vector3d(1, 2, 3), [2] = new Rhino.Geometry.Vector3d(4, 5, 6) }),
            ("PlateSupportComponent", "bottom face", new Dictionary<int, object> { [0] = plate, [1] = 500.0, [2] = true, [3] = 1 }),
            ("BeamDistributedMomentComponent", "uniform, axis 2", new Dictionary<int, object> { [0] = loaded, [1] = lc, [2] = 0, [3] = 1, [6] = 2.5 }),
            ("BeamPointLoadComponent", "local", new Dictionary<int, object> { [0] = loaded, [1] = lc, [3] = 1.5, [5] = -2.0, [6] = 0.25 }),
            ("BeamPointMomentComponent", "local", new Dictionary<int, object> { [0] = beam, [1] = lc, [4] = 3.0, [6] = 0.5 }),
            ("NodeDisplacementLoadComponent", "refused (0.4.0)", new Dictionary<int, object> { [0] = node, [1] = lc, [2] = new Rhino.Geometry.Vector3d(0, 0, 1) }),
            ("NodeLoadComponent", "force", new Dictionary<int, object> { [0] = node, [1] = lc, [2] = new Rhino.Geometry.Vector3d(1, 0, -5) }),
            ("NodeMomentComponent", "moment", new Dictionary<int, object> { [0] = node, [1] = lc, [2] = new Rhino.Geometry.Vector3d(0, 2, 0) }),
            ("NodeTemperatureComponent", "temperature", new Dictionary<int, object> { [0] = node, [1] = lc, [2] = 0, [3] = 15.0 }),
            ("PlateNSMassComponent", "mass", new Dictionary<int, object> { [0] = plate, [1] = lc, [2] = 1.2, [3] = 1.0, [4] = new Rhino.Geometry.Vector3d(0, 0, 0.1) }),
            ("PlatePreStressComponent", "prestress", new Dictionary<int, object> { [0] = plate, [1] = lc, [2] = 1.0, [3] = 2.0, [4] = 0.0 }),
            ("PlatePressureComponent", "local pressure", new Dictionary<int, object> { [0] = plate, [1] = lc, [3] = 0, [4] = new GH_Number(3.0), [5] = false }),
            ("AlignBeamAxesComponent", "axis 3 against the beam (reversed)", new Dictionary<int, object> { [0] = loaded, [1] = 2, [2] = -along }),
            ("AlignBeamAxesComponent", "axis 3 along the beam", new Dictionary<int, object> { [0] = loaded, [1] = 2, [2] = along }),
            ("AlignBeamAxesComponent", "axis 1 on a direction", new Dictionary<int, object> { [0] = loaded, [1] = 0, [2] = new Rhino.Geometry.Vector3d(0, 1, 1) }),
            ("AlignBeamAxesComponent", "axis 2 on a direction", new Dictionary<int, object> { [0] = beam, [1] = 1, [2] = new Rhino.Geometry.Vector3d(0, -1, 2) }),
            ("AlignBeamAxesComponent", "local x on a direction", new Dictionary<int, object> { [0] = beam, [1] = 3, [2] = new Rhino.Geometry.Vector3d(0.3, 1, -1) }),
            ("AlignBeamAxesComponent", "local y on a direction", new Dictionary<int, object> { [0] = beam, [1] = 4, [2] = new Rhino.Geometry.Vector3d(0, 1, 0) }),
            ("AlignBeamAxesComponent", "axis 1 along the beam (warning)", new Dictionary<int, object> { [0] = beam, [1] = 0, [2] = along }),
            ("AlignPlateAxesComponent", "axis 3 against the normal (reversed)", new Dictionary<int, object> { [0] = plate, [1] = 2, [2] = new Rhino.Geometry.Vector3d(0, 0, -1) }),
            ("AlignPlateAxesComponent", "axis 3 along the normal", new Dictionary<int, object> { [0] = plate, [1] = 2, [2] = new Rhino.Geometry.Vector3d(0, 0, 1) }),
            ("AlignPlateAxesComponent", "axis 1 along the normal (warning)", new Dictionary<int, object> { [0] = plate, [1] = 0, [2] = new Rhino.Geometry.Vector3d(0, 0, 1) }),
            ("AlignPlateAxesComponent", "axis 1 already aligned", new Dictionary<int, object> { [0] = plate, [1] = 0, [2] = new Rhino.Geometry.Vector3d(1, 0, 0) }),
            ("ElementFilterComponent", "find a beam and a node", new Dictionary<int, object> { [0] = new object[] { node, beam, plate, node2 }, [1] = new object[] { beam, node2 } }),
            ("MergeGeometryComponent", "elements (no geometry)", new Dictionary<int, object> { [0] = new object[] { beam, node } }),
            ("NodeFilterComponent", "by position", new Dictionary<int, object> { [0] = new object[] { node, node2 }, [2] = new object[] { m1.Nodes.Last().Position }, [3] = 0 }),
            ("PlateFilterComponent", "by position and property", new Dictionary<int, object> { [0] = new object[] { plate, tri }, [2] = new object[] { quad.Points[3] }, [3] = 0, [4] = new object[] { "none" } }),
            ("BeamFilterComponent", "by end point", new Dictionary<int, object> { [0] = new object[] { beam, beam2, loaded }, [3] = new object[] { m2.Beams[2].PointTo }, [4] = 0 }),
            ("ElementIdComponent", "a node", new Dictionary<int, object> { [0] = node, [1] = "N9" }),
            ("ElementIdComponent", "a plate", new Dictionary<int, object> { [0] = plate, [1] = "P9" }),
        };
        return cases;
    }

    /// <summary>The in-memory cases of lane 1b: the hidden components no example uses (Bridge Member Assignment, Bridge Connections in its
    /// three modes) and the cases the examples do not reach (elastic and mixed bearings, beams given by their drafts alone through Beam
    /// Offsets and Moment Dispatch), on the Simple Bridge of the Smoke (<c>SimpleBridgeTests.Input</c>) and the beams of the example 02.</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane1bCases()
    {
        var examples = NativeExamples.Create().ToDictionary(e => e.Id, e => e.Model);
        var m2 = examples["02-simply-supported"];
        var beam = Make("Fem.GH_Beam", m2.Beams[1]); var loaded = Make("Fem.GH_Beam", m2.Beams[0]);
        var geometry = Make("BridgeGeometryGoo", BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()));
        var steel = Make("Fem.GH_BeamProperty", BridgeAssignmentTests.Property());
        var lines = new object[] { new Rhino.Geometry.Line(0, 0, 0, 0, 0, 400), new Rhino.Geometry.Line(1000, 0, 0, 1000, 0, 400) };
        return new List<(string, string, Dictionary<int, object>)>
        {
            ("BridgeMemberAssignmentComponent", "one default property", new Dictionary<int, object> { [0] = geometry, [1] = new object[] { steel }, [2] = new object[] { -1 } }),
            ("BridgeMemberAssignmentComponent", "girders only (unassigned members)", new Dictionary<int, object> { [0] = geometry, [1] = new object[] { steel }, [2] = new object[] { 0 } }),
            ("BridgeConnectionsComponent", "mode 0, rigid links", new Dictionary<int, object> { [0] = 0, [1] = lines }),
            ("BridgeConnectionsComponent", "mode 1, deformable members", new Dictionary<int, object> { [0] = 1, [1] = lines, [2] = steel }),
            ("BridgeConnectionsComponent", "mode 2, beam offsets", new Dictionary<int, object> { [0] = 2, [3] = new object[] { beam, loaded }, [4] = new Rhino.Geometry.Vector3d(12, -20, 0) }),
            ("BridgeBeamOffsetsComponent", "two beams", new Dictionary<int, object> { [0] = new object[] { beam, loaded }, [1] = new Rhino.Geometry.Vector3d(0, 35, 0) }),
            ("BridgeRigidLinksComponent", "two links", new Dictionary<int, object> { [0] = lines }),
            ("BridgeConnectionBeamsComponent", "two members", new Dictionary<int, object> { [0] = lines, [1] = steel }),
            ("BridgeBearingsComponent", "elastic bearings on support 1", new Dictionary<int, object> { [0] = geometry, [1] = new object[] { 1 }, [3] = new object[] { 3 },
                [5] = new Rhino.Geometry.Vector3d(10, 20, 30), [6] = new Rhino.Geometry.Vector3d(0, 4, 0) }),
            ("BridgeBearingsComponent", "mixed types, guide angle", new Dictionary<int, object> { [0] = geometry, [2] = new object[] { 2 }, [3] = new object[] { 0, 1, 2 }, [4] = 23.0 }),
            ("BeamMomentDispatchComponent", "beams without results (Missing)", new Dictionary<int, object> { [0] = new object[] { beam, loaded }, [1] = "Dead" }),
        };
    }

    private static void InMemory(string lane, ISet<string> kinds, IDictionary<string, (int Instances, int Items)> byType, List<string> failures, out int count)
    {
        count = 0;
        foreach (var (type, label, inputs) in lane == "generators" ? Lane1bCases() : Lane1Cases())
        {
            using var doc = new GH_Document();
            var c = BridgeSegmentExcelTests.Add(doc, type);
            foreach (var input in inputs)
            {
                var tree = new GH_Structure<IGH_Goo>();
                var path = new GH_Path(0);
                foreach (var v in input.Value is object[] list ? list : new[] { input.Value }) tree.Append(Goo(v), path);
                SetInput(c.Params.Input[input.Key], tree, g => g);
            }
            BridgeSegmentExcelTests.Solve(doc, true);
            Replay(c, "in memory: " + c.Name + " (" + label + ")", kinds, byType, failures);
            count++;
        }
    }

    internal static int Regression(string lane)
    {
        var types = lane switch { "elements" => Lane1Types, "generators" => Lane1bTypes, _ => throw new ArgumentException("Unknown lane: " + lane) };
        var kinds = new HashSet<string>(lane switch { "elements" or "generators" => ElementGoo, _ => Array.Empty<string>() }, StringComparer.Ordinal);
        BridgeExampleMigration.Initialize();
        plugin = BridgeMergeTests.Initialize();
        generatorsLane = lane == "generators";
        generators = plugin.GetType("BridGHe.Grasshopper.Helpers.GpcOnlyElements", true).GetProperty("GeneratorsWithoutFem", BindingFlags.Public | BindingFlags.Static)
            ?? throw new Exception("GPC-ONLY REPLAY: GpcOnlyElements.GeneratorsWithoutFem not found");
        var byType = new SortedDictionary<string, (int Instances, int Items)>(StringComparer.Ordinal);
        var failures = new List<string>();
        int files = 0;
        // A substring of the definitions to replay (diagnosis only; the regression replays them all).
        string only = Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_FILES");
        foreach (var file in BuildModelScan.ScopeFiles().Where(f => string.IsNullOrEmpty(only) || f.Contains(only, StringComparison.OrdinalIgnoreCase)))
        {
            string label = Path.GetRelativePath(ExamplePaths.Root, file);
            using var doc = GrasshopperBridgeExample.Read(file); doc.FilePath = file;
            // No analysis runs (the Run button is never pressed: the definitions may name outputs in the main checkout).
            foreach (var o in doc.Objects.OfType<IGH_ActiveObject>())
                if (o.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent") o.Locked = true;
            BridgeSegmentExcelTests.Solve(doc, true);
            files++;
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => types.Contains(c.GetType().Name)).ToArray())
            {
                bool converted = c.Params.Input.Any(p => p.VolatileData.AllData(true).Any(g => g != null && kinds.Contains(g.GetType().Name)));
                // Lane 1b: every instance with data (a generator has no element input; its GPC-only copy runs with the switch on).
                if (!converted && !(generatorsLane && c.Params.Input.Any(p => !p.VolatileData.IsEmpty))) continue;
                Replay(c, $"{label}: {c.Name} [{c.NickName}]", kinds, byType, failures);
            }
        }
        var fromExamples = new HashSet<string>(byType.Keys, StringComparer.Ordinal);
        int cases = 0;
        if (lane is "elements" or "generators") InMemory(lane, kinds, byType, failures, out cases);
        foreach (var t in types.Where(t => !fromExamples.Contains(t))) Console.WriteLine("GPC-ONLY " + lane + ": not met in the examples (in-memory cases only): " + t);
        foreach (var t in types.Where(t => !byType.ContainsKey(t))) failures.Add("no case for " + t);
        foreach (var p in byType) Console.WriteLine($"GPC-ONLY {lane}: {p.Key} {p.Value.Instances} instances, {p.Value.Items} output lines");
        foreach (var f in failures.Take(60)) Console.WriteLine("  " + f);
        if (failures.Count != 0) throw new Exception($"GPC-ONLY {lane} FAIL: {failures.Count} differences or failures (first: {failures[0]})");
        Console.WriteLine($"GPC-ONLY {lane} PASS: {files} definitions and {cases} in-memory cases, {byType.Sum(p => p.Value.Instances)} instances of {byType.Count} components " +
            "replayed with GPC-only inputs: the same outputs modulo GUIDs and the same messages, no FEM value, deterministic identities");
        return 0;
    }

    /// <summary>The regression of lane 1 (<c>gpc-only-elements</c>).</summary>
    internal static void Elements() => Regression("elements");

    /// <summary>The regression of lane 1b (<c>gpc-only-generators</c>).</summary>
    internal static void Generators() => Regression("generators");

    /// <summary>The first differing lines of two texts (at most six), with their numbers.</summary>
    private static string Differences(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        var lines = new List<string>();
        for (int i = 0; i < Math.Max(x.Length, y.Length) && lines.Count < 6; i++)
        {
            string p = i < x.Length ? x[i] : "<end>", q = i < y.Length ? y[i] : "<end>";
            if (p != q) lines.Add($"  line {i + 1}" + Environment.NewLine + "  before " + Short(p) + Environment.NewLine + "  after  " + Short(q));
        }
        return lines.Count == 0 ? "  (same lines)" : string.Join(Environment.NewLine, lines);
    }

    private static string Short(string s) => s == null ? "null" : s.Length <= 240 ? s : s.Substring(0, 240) + "…";
}
#endif
