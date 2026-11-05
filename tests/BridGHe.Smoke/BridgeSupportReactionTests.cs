using System;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class BridgeSupportReactionTests
{
    internal static void Run()
    {
        var fem = Run(false);
#if GPC_PILOT
        // P2T: the same test with the analysed GPC models built in GPC on the actions (GpcSource): the merge and the reactions read the GPC
        // rows. The FEM sources stay only because Build BridGHe Model still validates them (until the FeMM removal); parity bit for bit.
        var gpc = Run(true);
        Require(fem.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(gpc.Select(BitConverter.DoubleToInt64Bits)), "GPC reactions equal to the FEM ones bit for bit");
#endif
    }

    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }

    /// <returns>The reactions read (both combinations, both directions), for the parity of the two paths.</returns>
    private static double[] Run(bool gpcSources)
    {
        int checks = 0; string path = gpcSources ? "GPC: " : "";
        void Require(bool ok, string message) { if (!ok) throw new Exception(path + message); checks++; }
        var steel = BridgeWorkflowTests.Source("Steel", 100); var shrink = BridgeWorkflowTests.Source("Shrinkage", 20);
        foreach (var model in new[] { steel, shrink })
        {
        model.AddElements(model.Beams.SelectMany(b => new[] { b.PointFrom, b.PointTo }).Distinct().Select((p, i) => new NodeModel((i + 1).ToString(), p)).ToArray());
        foreach (var node in model.Nodes)
            node.Results.Add(new NodeReactResultModel(model.LoadCases.Single()) { Force = new Vector3d(0, 0, model == steel ? 1000 : -200) });
        }
        var a = new BridgeActionDefinition { Key = "G1", Source = steel, Case = "G", Behavior = BridgeActionBehavior.Steel };
        var b = new BridgeActionDefinition { Key = "R", Source = shrink, Case = "G", Behavior = BridgeActionBehavior.Shrinkage, Strain = -200e-6 };
#if GPC_PILOT
        if (gpcSources)
        {
            a = a with { GpcSource = GpcWorkflowSources.Source("Steel", 100, 1000) };
            b = b with { GpcSource = GpcWorkflowSources.Source("Shrinkage", 20, -200) };
        }
#endif
        BridgePhaseDefinition[] Phases(BridgeActionDefinition shrinkage) => new[] { new BridgePhaseDefinition { Order = 10, Name = "Steel", State = BridgeSectionState.Steel, Actions = new[] { a } },
            new BridgePhaseDefinition { Order = 20, Name = "Shrinkage", State = BridgeSectionState.Composite, Actions = new[] { shrinkage } } };
        var phases = Phases(b);
        var built = BridgeWorkflowBuilder.Build(new[] { steel, shrink }, phases,
            new[] { new[] { "Combination", "G1", "R" }, new[] { "ALL", "1.5", "2" }, new[] { "G", "1", "0" } }, BridgeWorkflowMethod.Compatible, 1e-6).Model;
        double Read(string combo = "ALL", Vector3d? up = null, BridgePhaseDefinition[] definitions = null) => BridgeSupportReactions.SignedKN(built, definitions ?? phases, Point3d.Origin, combo, up ?? Vector3d.ZAxis, 1e-6);
        var read = new[] { Read(), Read(up: -Vector3d.ZAxis), Read("G"), Read("G", -Vector3d.ZAxis) };
        Require(Math.Abs(Read() - 1.1) < 1e-12, "Combination applied twice, incorrect sign or N/kN conversion");
        Require(Math.Abs(Read(up: -Vector3d.ZAxis) + 1.1) < 1e-12, "Uplift must retain its sign");
        if (!gpcSources)
        {
            var node0 = shrink.Nodes.Single(n => n.Position == Point3d.Origin); var sample = node0.Results.Single(); node0.Results.Clear();
            Require(Read("G") == 1, "Inactive missing case must not be read");
            bool missing = false; try { Read(); } catch (ArgumentException) { missing = true; } Require(missing, "Missing reaction silently treated as zero");
            node0.Results.Add(sample); node0.Results.Add(sample);
            bool duplicate = false; try { Read(); } catch (ArgumentException) { duplicate = true; } Require(duplicate, "Duplicate reaction accepted"); node0.Results.RemoveAt(1);
        }
#if GPC_PILOT
        else
        {
            // The GPC source of R without the reaction at the support (node 1), then with a second reaction sample there.
            var withoutReaction = Phases(b with { GpcSource = GpcWorkflowSources.Source("Shrinkage", 20, -200, reactionAt: n => n != 1) });
            Require(Read("G", definitions: withoutReaction) == 1, "Inactive missing case must not be read");
            bool missing = false; try { Read(definitions: withoutReaction); } catch (ArgumentException) { missing = true; } Require(missing, "Missing reaction silently treated as zero");
            bool duplicate = false;
            try { Read(definitions: Phases(b with { GpcSource = GpcWorkflowSources.Source("Shrinkage", 20, -200, (t, beams, nodes) => t.Reaction(nodes[0], "G", 0, 0, -200)) })); }
            catch (ArgumentException) { duplicate = true; }
            catch (InvalidOperationException e) when (e.Message.StartsWith("GpcTestModel results not imported")) { duplicate = true; }
            Require(duplicate, "Duplicate reaction accepted");
        }
#endif

        // The component part: the phases go in as persistent data, whose copies (BridgeWorkflowGoo.Duplicate, an archive round trip) drop
        // the GPC source ([IgnoreDataMember]); here the component reads the FEM sources on both paths.
        var plugin = BridgeMergeTests.Initialize(); using var doc = new GH_Document();
        var c = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "BridgeDetailStationsComponent"));
        c.CreateAttributes(); doc.AddObject(c, false);
        IGH_Goo Wrap(string type, object value)
        {
            var t = plugin.GetType("BridGHe.Grasshopper.DataTypes." + type, true);
            if (type == "Fem.GH_Model") { var g = (IGH_Goo)Activator.CreateInstance(t); t.GetProperty("Value").SetValue(g, value); return g; }
            return (IGH_Goo)Activator.CreateInstance(t, value);
        }
        ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append(Wrap("Fem.GH_Model", built));
        var fixture = BridgeCheckSummaryTests.SampleResult();
        var input = (Param_GenericObject)c.Params.Input[1];
        foreach (double station in new[] { 0d, 5000, 10000 })
            input.PersistentData.Append(new GH_ObjectWrapper(fixture.WithSource(new BridgeResultSource("B0", station, "ALL"))), new GH_Path(0, (int)station));
        var locations = (Param_Point)c.Params.Input[2]; locations.PersistentData.Append(new GH_Point(Point3d.Origin));
        foreach (var phase in phases) ((Param_GenericObject)c.Params.Input[3]).PersistentData.Append(Wrap("BridgeWorkflowGoo", phase));
        void Solve() { foreach (var p in c.Params.Input) p.ExpireSolution(false); c.ExpireSolution(false); GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent); }
        Solve(); Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, string.Join(";", c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        Require(c.Params.Output[0].VolatileDataCount == 1 && c.Params.Output[0].VolatileData.Paths.Single().Indices.SequenceEqual(new[] { 0, 0 }), "Selection changed original path");
        Require(Math.Abs((double)c.Params.Output[1].VolatileData.AllData(true).Single().ScriptVariable() - 1.1) < 1e-12, "GH reaction pairing failed");
        Require((int)c.Params.Output[3].VolatileData.AllData(true).Single().ScriptVariable() == 1, "Girder start not identified");
        ((Param_Vector)c.Params.Input[4]).PersistentData.Clear(); ((Param_Vector)c.Params.Input[4]).SetPersistentData(-Vector3d.ZAxis); Solve();
        Require((double)c.Params.Output[1].VolatileData.AllData(true).Single().ScriptVariable() == 0 && c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count == 1, "Uplift falsely converted to compression");
        ((Param_GenericObject)c.Params.Input[3]).PersistentData.Clear(); locations.PersistentData.Clear(); locations.PersistentData.Append(new GH_Point(new Point3d(5000, 0, 0))); Solve();
        Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, "Selection-only errors: " + string.Join(";", c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        Require(c.Params.Output[1].VolatileDataCount == 0 && c.Params.Output[2].VolatileDataCount == 0, "Selection-only mode invented a reaction");
        locations.PersistentData.Clear(); locations.PersistentData.Append(new GH_Point(new Point3d(4500, 0, 0))); Solve();
        Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 1, "Missing station silently interpolated");
        Console.WriteLine($"BRIDGE DETAIL STATIONS PASS{(gpcSources ? " (GPC sources built in GPC)" : "")}: {checks} checks; reaction coefficients, shrinkage, uplift, missing data and original tree paths.");
        return read;
    }
}
