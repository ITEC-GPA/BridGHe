#if GPC_PILOT
using System;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.6 lane R2b, regression <c>r2b-merge-forces</c> (not native): the explicit, GPC-only counterparts of the native parity checks
/// <see cref="GpcMergeCheck"/> and <see cref="GpcSecForcesCheck"/> (they compare the GPC merge and the Super Element Check forces of 50 and 99
/// with the former FEM path and stay transitional on those natives). Sources built in GPC (<see cref="GpcWorkflowSources"/>: beams B0 and B1,
/// stations 0, 5000, 10000; G1 steel N = 100 + i, BM2 = 1e5·(1 + x/L), support reactions 1000 N at the four end nodes; G2 long term N = 30 + i,
/// BM2 = 3e4·(1 + x/L), reactions 300 N); combinations C1 = G1 + G2 and C2 = 1.35 G1 + 1.5 G2.
/// <list type="bullet">
/// <item>Merge constraints: an action with a case and no analysed GPC source is refused with its message; the merged samples are the source
/// samples bit for bit; no resampling warning.</item>
/// <item>Reactions: every support and every combination, explicit (C1 1.3 kN, C2 1.8 kN, with the sign of the up vector); a location that is
/// no support gives the same error for every combination; a support without the reaction of an active case, and a duplicate reaction, refused.</item>
/// <item>Super Element Check forces: on the GH model given by the GPC input of the bridge model and the composite beam given by its GPC datum,
/// the «Tension Forces» tree has one branch (beam, station) per merged station and one force per combination, equal to the sum of the factored
/// samples in the checker frame (−x, −y) of the BridGHe convention (<see cref="BridgeForceConvention"/>: N, SF1, SF2, T along x, y, z and
/// moments BM2 about y, −BM1 about z; the axial force is the section scalar, tension positive, unchanged by the frame; the moments rotate:
/// N' = N, M1' = −BM2).</item>
/// </list>
/// </summary>
internal static class R2bMergeForcesChecks
{
    internal static void Regression()
    {
        var assembly = BridgeMergeTests.Initialize();
        int checks = 0;
        void Require(bool ok, string what) { if (!ok) throw new Exception("R2B MERGE FORCES FAIL: " + what); checks++; }
        var steel = GpcWorkflowSources.Pair("Merge steel", 100, 1000); var longTerm = GpcWorkflowSources.Pair("Merge long term", 30, 300);
        var data = new GPC.Checkers.CompositeBridge.BridgeModelData(); data.SetSuperElement(steel.Input.BeamElements.Values.Single(b => b.Name == "B0").Guid, "Main");
        BridgePhaseDefinition[] Phases(GPC.Model.Models.Model g1, GPC.Model.Models.Model g2) => new[]
        {
            new BridgePhaseDefinition { Order = 10, Name = "Steel", State = BridgeSectionState.Steel, Actions = new[] { new BridgeActionDefinition { Key = "G1", GpcSource = g1, Case = "G", Behavior = BridgeActionBehavior.Steel } } },
            new BridgePhaseDefinition { Order = 20, Name = "Composite", State = BridgeSectionState.Composite, Actions = new[] { new BridgeActionDefinition { Key = "G2", GpcSource = g2, Case = "G", Behavior = BridgeActionBehavior.LongTerm } } },
        };
        var table = new[] { new[] { "", "G1", "G2" }, new[] { "C1", "1", "1" }, new[] { "C2", "1.35", "1.5" } };
        var phases = Phases(steel.Analysed, longTerm.Analysed);
        var build = GpcBridgeBuilder.Build(new[] { GpcBridgeMerge.Reference(steel, data), GpcBridgeMerge.Reference(longTerm) }, phases, table, BridgeWorkflowMethod.Compatible, 1e-6, false,
            GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r2b-merge-forces"), structureInput: true);
        // Merge constraints.
        bool refused = false;
        try { GpcBridgeMerge.Build(new[] { steel, longTerm }, new[] { phases[0], phases[1] with { Actions = new[] { phases[1].Actions[0] with { GpcSource = null } } } }, table, "without source"); }
        catch (ArgumentException e) when (e.Message.Contains("needs its source model") || e.Message.Contains("has no analysed GPC model")) { refused = true; }
        Require(refused, "an action with a case and no analysed GPC source is refused");
        double[] grid = { 0, 5000, 10000 };
        for (int k = 0; k < 2; k++)
        {
            R2bFixtures.RequireSamples(build, k, "G1 [FEM]", grid, x => new[] { 100d + k, 0, 0, 0, 0, 100 * 1000 * (1 + x / 10000) }, "merge");
            R2bFixtures.RequireSamples(build, k, "G2 [FEM]", grid, x => new[] { 30d + k, 0, 0, 0, 0, 30 * 1000 * (1 + x / 10000) }, "merge");
            checks += 2;
        }
        Require(!build.Warnings.Any(w => w.Contains("Station alignment")), "no resampling on equal grids: " + string.Join(" / ", build.Warnings));
        // Reactions at every support and combination.
        var supports = new[] { (0d, 0d), (10000d, 0d), (0d, 2000d), (10000d, 2000d) }.Select(p => new Rhino.Geometry.Point3d(p.Item1, p.Item2, 0)).ToArray();
        double Read(Rhino.Geometry.Point3d at, string combination, BridgePhaseDefinition[] definitions = null, Rhino.Geometry.Vector3d? up = null) =>
            BridgeSupportReactions.SignedKN(build.Bridge, definitions ?? phases, at, combination, up ?? Rhino.Geometry.Vector3d.ZAxis, 1e-6);
        foreach (var s in supports)
        {
            Require(Math.Abs(Read(s, "C1") - 1.3) < 1e-12 && Math.Abs(Read(s, "C2") - 1.8) < 1e-12, $"reactions at {s}: C1 1.3 kN, C2 1.8 kN");
            Require(Math.Abs(Read(s, "C2", up: -Rhino.Geometry.Vector3d.ZAxis) + 1.8) < 1e-12, $"uplift sign at {s}");
        }
        string Error(Func<double> f) { try { f(); return null; } catch (ArgumentException e) { return e.Message; } }
        var off = new Rhino.Geometry.Point3d(5000, 0, 0);
        string e1 = Error(() => Read(off, "C1")), e2 = Error(() => Read(off, "C2"));
        Require(e1 != null && e1 == e2, "a location that is no support: the same error for every combination: " + e1 + " / " + e2);
        var missing = Phases(steel.Analysed, GpcWorkflowSources.Source("Merge long term", 30, 300, reactionAt: n => n != 1));
        Require(Error(() => Read(supports[0], "C1", missing)) != null && Math.Abs(Read(supports[1], "C1", missing) - 1.3) < 1e-12, "a support without the reaction of an active case refused, the others read");
        bool duplicate = false;
        try { Read(supports[0], "C1", Phases(steel.Analysed, GpcWorkflowSources.Source("Merge long term", 30, 300, (t, beams, nodes) => t.Reaction(nodes[0], "G", 0, 0, 300)))); }
        catch (ArgumentException) { duplicate = true; }
        catch (InvalidOperationException e) when (e.Message.StartsWith("GpcTestModel results not imported")) { duplicate = true; }
        Require(duplicate, "a duplicate reaction refused");

        // Super Element Check «Tension Forces» on GPC data alone.
        var model = R2bFixtures.Model(build);
        var definition = GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(new BridgeGrillageExample.Parameters()));
        var draft = BridGHe.Common.Gpc.Builders.GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(10000, 0, 0), GpcFixtures.RectangleProperty(), null, 0, 0, 0,
            GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r2b-merge-forces:B0"), "B0");
        var datum = Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.GpcCompositeBeam", true), draft, definition, definition, (System.Collections.Generic.IReadOnlyList<string>)Array.Empty<string>());
        var beam = R2aDesignFixtures.Goo("GH_CompositeBeam", datum);
        using var doc = new GH_Document();
        var c = (GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.SuperElementCheckComponent", true)); c.CreateAttributes(); doc.AddObject(c, false);
        void Set(int port, IGH_Goo value) { var p = (Param_GenericObject)c.Params.Input[port]; p.PersistentData.Clear(); p.PersistentData.Append(value); p.ExpireSolution(false); }
        Set(0, model); Set(1, beam);
        c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true); c.ExpireSolution(false);
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, "Super Element Check: " + string.Join("; ", c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        var output = c.Params.Output[2].VolatileData;
        Require(output.PathCount == grid.Length, $"{output.PathCount} branches, {grid.Length} merged stations");
        double[][] factors = { new[] { 1d, 1 }, new[] { 1.35, 1.5 } };
        for (int j = 0; j < grid.Length; j++)
        {
            int b = output.Paths.IndexOf(new Grasshopper.Kernel.Data.GH_Path(0, j));
            Require(b >= 0, "branch {0;" + j + "}");
            var items = output.get_Branch(b).Cast<object>().Select(o => (GPC.Model.Results.ResultBeamForces)((o as IGH_Goo)?.ScriptVariable() ?? o)).ToArray();
            Require(items.Length == 2, $"station {grid[j]}: one force per combination");
            double x = grid[j];
            for (int k = 0; k < 2; k++)
            {
                double n = factors[k][0] * 100 + factors[k][1] * 30, bm2 = factors[k][0] * 100 * 1000 * (1 + x / 10000) + factors[k][1] * 30 * 1000 * (1 + x / 10000);
                var f = items[k];
                static bool Near(double a, double e) => Math.Abs(a - e) <= 1e-9 * Math.Max(1, Math.Abs(e));
                Require(Near(f.N, n) && Near(f.M1, -bm2) && Near(f.V1, 0) && Near(f.V2, 0) && Near(f.T, 0) && Near(f.M2, 0),
                    $"station {x}, combination C{k + 1}: N {f.N} V1 {f.V1} V2 {f.V2} T {f.T} M1 {f.M1} M2 {f.M2}; expected N {n}, M1 {-bm2}, the rest 0");
            }
        }
        Console.WriteLine($"R2B MERGE FORCES PASS: {checks} checks on GPC data alone (merge constraints, samples, reactions at 4 supports x 2 combinations with the limit cases, Super Element Check forces at 3 stations x 2 combinations in the checker frame).");
    }
}
#endif
