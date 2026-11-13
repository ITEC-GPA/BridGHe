using System;
using System.Linq;
using BridGHe.Common.Checks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class BridgeSupportReactionTests
{
    internal static void Run()
    {
        Gpc();
    }

    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }

    /// <summary>R6.6 R2b: the support reactions of Bridge Detail Stations on GPC data alone, no FEM model: the GPC bridge model of Build BridGHe
    /// Model (its GPC input and the bridge, as the component on GPC data alone) from sources built in GPC with the reactions of case G at the
    /// beam end nodes (Steel +1000 N, Shrinkage −200 N), and the reactions read on it alone (SignedKN on GpcBridge). Explicit expected values
    /// (they replace the former parity with the FEM path): ALL = 1.5·1000 + 2·(−200) N = 1.1 kN, G = 1·1000 N = 1 kN, both with the sign of the
    /// up vector; the inactive missing case, the missing and the duplicate reaction; then the component on the GH model given by the GPC input
    /// of the bridge model with the bridge attached.</summary>
    private static void Gpc()
    {
        int checks = 0;
        void Require(bool ok, string message) { if (!ok) throw new Exception("GPC: " + message); checks++; }
        var pSteel = GpcWorkflowSources.Pair("Steel", 100, 1000); var pShrink = GpcWorkflowSources.Pair("Shrinkage", 20, -200);
        var a = new BridgeActionDefinition { Key = "G1", GpcSource = pSteel.Analysed, Case = "G", Behavior = BridgeActionBehavior.Steel };
        var b = new BridgeActionDefinition { Key = "R", GpcSource = pShrink.Analysed, Case = "G", Behavior = BridgeActionBehavior.Shrinkage, Strain = -200e-6 };
        BridgePhaseDefinition[] Phases(BridgeActionDefinition shrinkage) => new[] { new BridgePhaseDefinition { Order = 10, Name = "Steel", State = BridgeSectionState.Steel, Actions = new[] { a } },
            new BridgePhaseDefinition { Order = 20, Name = "Shrinkage", State = BridgeSectionState.Composite, Actions = new[] { shrinkage } } };
        var table = new[] { new[] { "Combination", "G1", "R" }, new[] { "ALL", "1.5", "2" }, new[] { "G", "1", "0" } };
        var phases = Phases(b);
        var build = R2bFixtures.Bridge(new[] { pSteel, pShrink }, phases, table, "support reactions");
        var bridge = build.Bridge;
        double Read(string combo = "ALL", Vector3d? up = null, BridgePhaseDefinition[] definitions = null) =>
            BridgeSupportReactions.SignedKN(bridge, definitions ?? phases, Point3d.Origin, combo, up ?? Vector3d.ZAxis, 1e-6);
        Require(Math.Abs(Read() - 1.1) < 1e-12, "Combination applied twice, incorrect sign or N/kN conversion");
        Require(Math.Abs(Read(up: -Vector3d.ZAxis) + 1.1) < 1e-12, "Uplift must retain its sign");
        Require(Math.Abs(Read("G") - 1) < 1e-12 && Math.Abs(Read("G", -Vector3d.ZAxis) + 1) < 1e-12, "Combination G: 1 x 1000 N");
        // The GPC source of R without the reaction at the support (node 1), then with a second reaction sample there.
        var withoutReaction = Phases(b with { GpcSource = GpcWorkflowSources.Source("Shrinkage", 20, -200, reactionAt: n => n != 1) });
        Require(Read("G", definitions: withoutReaction) == 1, "Inactive missing case must not be read");
        bool missing = false; try { Read(definitions: withoutReaction); } catch (ArgumentException) { missing = true; } Require(missing, "Missing reaction silently treated as zero");
        bool duplicate = false;
        try { Read(definitions: Phases(b with { GpcSource = GpcWorkflowSources.Source("Shrinkage", 20, -200, (t, beams, nodes) => t.Reaction(nodes[0], "G", 0, 0, -200)) })); }
        catch (ArgumentException) { duplicate = true; }
        catch (InvalidOperationException e) when (e.Message.StartsWith("GpcTestModel results not imported")) { duplicate = true; }
        Require(duplicate, "Duplicate reaction accepted");

        // The component part: the GH model given by the GPC input of the bridge model with the bridge attached (Build BridGHe Model on GPC
        // data alone), the phases with their GPC sources (kept by BridgeWorkflowGoo.Duplicate since P2F 5/11).
        var plugin = BridgeMergeTests.Initialize(); using var doc = new GH_Document();
        var c = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "BridgeDetailStationsComponent"));
        c.CreateAttributes(); doc.AddObject(c, false);
        var modelGoo = R2bFixtures.Model(build);
        Require(R2bFixtures.Get<object>(modelGoo, "GpcBridge") != null, "the GH model carries the GPC bridge model");
        ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append(modelGoo);
        var fixture = BridgeCheckSummaryTests.SampleResult();
        var input = (Param_GenericObject)c.Params.Input[1];
        foreach (double station in new[] { 0d, 5000, 10000 })
            input.PersistentData.Append(new GH_ObjectWrapper(fixture.WithSource(new BridgeResultSource("B0", station, "ALL"))), new GH_Path(0, (int)station));
        var locations = (Param_Point)c.Params.Input[2]; locations.PersistentData.Append(new GH_Point(Point3d.Origin));
        var workflowType = plugin.GetType("BridGHe.Grasshopper.DataTypes.BridgeWorkflowGoo", true);
        foreach (var phase in phases) ((Param_GenericObject)c.Params.Input[3]).PersistentData.Append((IGH_Goo)Activator.CreateInstance(workflowType, phase));
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
        Console.WriteLine($"BRIDGE DETAIL STATIONS PASS (GPC data alone): {checks} checks; reaction coefficients, shrinkage, uplift, missing data and original tree paths.");
    }
}
