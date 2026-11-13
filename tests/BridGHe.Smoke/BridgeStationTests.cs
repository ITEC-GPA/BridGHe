using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using Rhino.Geometry;

internal static class BridgeStationTests
{
    internal static void Near(double actual, double expected, string label)
    {
        // P2R (task 39, T01): a NaN or infinite value never passes (before, NaN made the comparison false and the check passed).
        if (!double.IsFinite(actual) || !double.IsFinite(expected) || !(Math.Abs(actual - expected) <= Math.Max(1e-8, Math.Abs(expected) * 1e-12)))
            throw new Exception($"{label}: {actual:G17} != {expected:G17}");
    }

    internal static void Reject(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException ex) when (ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Expected rejection: " + message);
    }

    internal static void Run()
    {
        Gpc();
        ReplayNativeFixtureGpc();
        Console.WriteLine("PASS station grids: analytical quadratic diagrams, units, identity, endpoint anchoring, no drift, bounds, steps and jumps (GPC data alone)");
    }

    /// <summary>The analytical diagrams of <see cref="Source"/> at the physical station x: N, SF1, SF2, T, BM1, BM2 (linear and quadratic).</summary>
    private static double[] Diagram(double x) => new[] { 100 + 2 * x, 500 - .1 * x, -800 + .2 * x, 200 - 3 * x, 3000 + 500 * x - .05 * x * x, -9000 - 800 * x + .1 * x * x };

    /// <summary>R6.6 R2b: the station-grid test on Build BridGHe Model on GPC, with action sources built in GPC only (the same grids and
    /// diagrams as <see cref="Source"/>), no FEM model: union of the stations, analytical resampling, identity, endpoint anchoring, no drift,
    /// bounds and roundoff, on the merged samples of the GPC bridge model. The analytic diagrams are the oracle (they replace the former parity
    /// with the FEM merge of the same grids); the samples of the original stations are those of the source bit for bit.</summary>
    private static void Gpc()
    {
        (GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed) Grid(string name, double[] stations, Func<int, double, double[]> values = null, bool reversed = false,
            Action<GpcTestModel, GPC.Model.Elements.BeamElement[], GPC.Model.Elements.NodeElement[]> change = null) =>
            GpcWorkflowSources.Pair(name, 0, null, change, stations, values ?? ((_, x) => Diagram(x)), reversed: reversed);
        BridgePhaseDefinition[] Phases(GPC.Model.Models.Model ga, GPC.Model.Models.Model gb) => new[]
        {
            new BridgePhaseDefinition { Order = 10, Name = "Steel", State = BridgeSectionState.Steel,
                Actions = new[] { new BridgeActionDefinition { Key = "G1", GpcSource = ga, Case = "G", Behavior = BridgeActionBehavior.Steel } } },
            new BridgePhaseDefinition { Order = 20, Name = "Composite", State = BridgeSectionState.Composite,
                Actions = new[] { new BridgeActionDefinition { Key = "G2", GpcSource = gb, Case = "G", Behavior = BridgeActionBehavior.LongTerm } } }
        };
        var table = new[] { new[] { "", "G1", "G2" }, new[] { "SLS", "1", "1" } };
        int merges = 0;
        BridGHe.Common.Gpc.GpcBridgeBuild Merge((GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed) ga, (GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed) gb, string label)
        {
            var gpc = GpcBridgeMerge.Build(new[] { ga, gb }, Phases(ga.Analysed, gb.Analysed), table, label);
            merges++; return gpc;
        }
        void Rejected(Func<BridGHe.Common.Gpc.GpcBridgeBuild> merge, string message)
        {
            try { merge(); }
            catch (ArgumentException ex) when (ex.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { return; }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("GpcTestModel results not imported")) { return; }
            throw new Exception("GPC: expected rejection: " + message);
        }
        double[] gridA = { 0d, 2500, 5000, 7500, 10000 }, gridB = { 0d, 879.044934, 4000, 8500, 10000 };
        var ga = Grid("A", gridA); var gb = Grid("B", gridB);
        var before = R2bFixtures.ResultTokens(ga.Analysed).Concat(R2bFixtures.ResultTokens(gb.Analysed)).ToArray();
        var merged = Merge(ga, gb, "stations");
        var union = gridA.Concat(gridB).Distinct().OrderBy(x => x).ToArray();
        for (int k = 0; k < 2; k++)
        {
            var samples = GpcBridgeMerge.Samples(merged, k);
            BridgeWorkflowTests.Require(samples.Count == 16 && union.Length == 8, "GPC: All eight union stations must exist in both actions.");
            foreach (var (name, f) in samples)
            {
                double x = f.Station;
                BridgeWorkflowTests.Require(union.Any(u => Math.Abs(u - x) <= 1e-9), "GPC: merged station outside the union of the grids: " + x);
                Near(f.Axial, 100 + 2 * x, "GPC: Linear N");
                Near(f.Torque, 200 - 3 * x, "GPC: Linear T");
                Near(f.Shear1, 500 - .1 * x, "GPC: Linear V1");
                Near(f.Shear2, -800 + .2 * x, "GPC: Linear V2");
                Near(f.Bending1, 3000 + 500 * x - .05 * x * x, "GPC: Quadratic M1");
                Near(f.Bending2, -9000 - 800 * x + .1 * x * x, "GPC: Quadratic M2");
                // A station of the own grid of an action is its source sample, bit for bit (no resampling).
                var own = (name == "G1 [FEM]" ? gridA : gridB).Where(g => Math.Abs(g - x) <= 1e-9).ToArray();
                if (own.Length == 1)
                    BridgeWorkflowTests.Require(f.Values.Select(R2bFixtures.Bits).SequenceEqual(Diagram(own[0]).Select(R2bFixtures.Bits)), $"GPC: original sample of {name} at {x} changed");
            }
        }
        BridgeWorkflowTests.Require(before.SequenceEqual(R2bFixtures.ResultTokens(ga.Analysed).Concat(R2bFixtures.ResultTokens(gb.Analysed))), "GPC: Source models mutated.");
        BridgeWorkflowTests.Require(merged.Warnings.Any(w => w.Contains("Station alignment:")), "GPC: Resampling must be reported.");
        // Reverse element and sample order: source beam identity and original brackets must still win.
        var reordered = Merge(ga, Grid("B", gridB, reversed: true), "reordered");
        BridgeWorkflowTests.Require(GpcBridgeMerge.Tokens(reordered.Bridge.Samples).SequenceEqual(GpcBridgeMerge.Tokens(merged.Bridge.Samples)), "GPC: Order changed interpolation.");
        // Moment correction must meet BOTH source values (end moment +10000 at the end of every beam).
        double[] ends = { 0d, 10000 };
        Func<int, double, double[]> inconsistentValues = (_, x) => { var v = Diagram(x); if (x == 10000) v[5] += 10000; return v; };
        var gInconsistent = Grid("Endpoints", ends, inconsistentValues);
        var anchored = GpcBridgeMerge.Samples(Merge(ga, gInconsistent, "endpoints"), 0).Where(s => s.Case == "G2 [FEM]").Select(s => s.Sample).ToArray();
        Near(anchored.Single(f => f.Station == 5000).Bending2, -9000 - 800 * 5000 + .1 * 5000 * 5000 + 5000, "GPC: Both endpoint moments anchored");
        Near(anchored.Last().Bending2, inconsistentValues(0, 10000)[5], "GPC: Original end moment unchanged");
        // Extra target stations must not recursively alter an already resampled result.
        double[] dense = { 0d, 1000, 2500, 3000, 5000, 7500, 10000 };
        Near(GpcBridgeMerge.Samples(Merge(Grid("Dense", dense), gInconsistent, "dense"), 0)
            .Single(s => s.Sample.Station == 5000 && s.Case == "G2 [FEM]").Sample.Bending2, anchored.Single(f => f.Station == 5000).Bending2, "GPC: No recursive drift");
        // Bounds: a missing end, a duplicate sample, a station outside the beam; a concentrated load at a station between samples.
        Rejected(() => Merge(ga, Grid("Missing end", new[] { 100d, 10000 }), "missing end"), "both beam ends");
        Rejected(() => Merge(ga, Grid("B", gridB, change: (t, beams, _) => t.Forces(beams[0], "G", 879.044934, Diagram(879.044934)[0])), "duplicate"), "expected one");
        Rejected(() => Merge(ga, Grid("B", gridB, change: (t, beams, _) => t.Forces(beams[0], "G", 10001, 0)), "outside"), "outside beam bounds");
        Rejected(() => Merge(ga, Grid("Point load", ends, change: (t, beams, _) => beams[0].Assignments.Loads.Add(BridGHe.Common.Gpc.GpcBeamLoads.Concentrated(t.Model.LoadCases["G"],
            beams[0].StartPoint, beams[0].EndPoint, .5, new GPC.Geometry.Vector3d(0, 1, 0), false, null, null, 1, "test point load"))), "concentrated"), "concentrated load");
        // Roundoff of a station (1e-8 mm, the FEM fixture: 1e-12 of the normalized station) does not duplicate stations.
        double[] roundedGrid = { 0d, 2500, 5000 + 1e-8, 7500, 10000 };
        BridgeWorkflowTests.Require(GpcBridgeMerge.Samples(Merge(ga, Grid("Rounded", roundedGrid, (_, x) => Diagram(Math.Round(x))), "roundoff"), 0).Count == 10,
            "GPC: Roundoff duplicated stations.");
        Console.WriteLine($"PASS station grids on GPC: {merges} GPC merges of sources built in GPC, analytical diagrams, identity, endpoint anchoring, no drift, bounds and roundoff");
    }

    /// <summary>R6.6 R2b: the archived example 19 (<see cref="NativeWorkflowStationTests.FixturePath"/>: the four native station grids of one
    /// beam, captured from Straus7 with reference values requested independently through St7GetBeamResultArrayPos) merged by Build BridGHe
    /// Model on GPC from sources built in GPC (one source per case, the original samples as stored results): every merged sample at the union
    /// within the tolerances of the capture, every original sample unchanged bit for bit (the GPC form of <see cref="ReplayNativeFixture"/>).</summary>
    private static void ReplayNativeFixtureGpc()
    {
        var fixture = JsonSerializer.Deserialize<NativeWorkflowStationTests.Fixture>(File.ReadAllText(NativeWorkflowStationTests.FixturePath));
        GPC.Model.Models.Model Model(NativeWorkflowStationTests.CaseSamples c, bool analyse)
        {
            var test = new GpcTestModel("Station fixture " + c.Key); test.Case("G");
            var beam = test.Beam(fixture.BeamId, test.Node("1", 0, 0, 0), test.Node("2", fixture.Length, 0, 0));
            foreach (var s in c.Original) test.Forces(beam, "G", s.Station, s.N, s.V1, s.V2, s.T, s.M1, s.M2);
            return analyse ? test.Analyse() : test.Structure();
        }
        var pairs = fixture.Cases.Select(c => (Model(c, false), Model(c, true))).ToArray();
        string[] keys = { "G1", "G2", "Q", "RITIRO" };
        var phases = pairs.Select((m, i) => new BridgePhaseDefinition
        {
            Order = 10 * (i + 1), Name = keys[i], State = i == 0 ? BridgeSectionState.Steel : BridgeSectionState.Composite,
            Actions = new[] { new BridgeActionDefinition { Key = keys[i], GpcSource = m.Item2, Case = "G", Behavior = (BridgeActionBehavior)i, Strain = i == 3 ? -200e-6 : null } }
        }).ToArray();
        BridgeWorkflowTests.Require(fixture.Cases.Select(c => c.Key).SequenceEqual(keys), "GPC: the fixture cases of example 19");
        var result = GpcBridgeMerge.Build(pairs, phases, new[] { new[] { "", "G1", "G2", "Q", "RITIRO" }, new[] { "SLS", "1", "1", "1", "1" } }, "example 19 fixture");
        int compared = 0;
        foreach (var c in fixture.Cases)
            foreach (var expected in c.NativeAtUnion)
            {
                var f = GpcBridgeMerge.Samples(result, 0).Single(s => s.Case == c.Key + " [FEM]" && Math.Abs(s.Sample.Station - expected.Station) < 1e-6).Sample;
                var errors = new[] { f.Axial - expected.N, f.Shear1 - expected.V1, f.Shear2 - expected.V2, f.Torque - expected.T, f.Bending1 - expected.M1, f.Bending2 - expected.M2 };
                BridgeWorkflowTests.Require(errors.Take(4).All(e => Math.Abs(e) < 1e-5) && errors.Skip(4).All(e => Math.Abs(e) < .01), $"GPC native regression: {c.Key}, station {expected.Station}");
                var original = c.Original.SingleOrDefault(s => Math.Abs(s.Station - f.Station) < 1e-6);
                if (original != null)
                    BridgeWorkflowTests.Require(f.Axial == original.N && f.Shear1 == original.V1 && f.Shear2 == original.V2 && f.Torque == original.T && f.Bending1 == original.M1 && f.Bending2 == original.M2,
                        "GPC: Original native sample changed.");
                compared++;
            }
        Console.WriteLine($"PASS archived example 19 on GPC: all four native station grids, including 879.044933748 mm ({compared} merged samples)");
    }

}
