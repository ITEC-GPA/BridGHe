using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class BridgeStationTests
{
    internal static ModelModel Source(string name, double[] stations, bool normalized = false)
    {
        var model = BridgeWorkflowTests.Source(name, 0);
        foreach (var beam in model.Beams)
        {
            beam.Results.Clear();
            foreach (double x in stations)
                beam.Results.Add(new BeamForceResultModel(model.LoadCases[0], normalized ? x / beam.Length : x, 0, "", normalized)
                {
                    AxialForce = 100 + 2 * x, Torque = 200 - 3 * x,
                    Vx = 500 - .1 * x, Mx = 3000 + 500 * x - .05 * x * x,
                    Vy = -800 + .2 * x, My = -9000 - 800 * x + .1 * x * x
                });
        }
        return model;
    }

    internal static BridgeWorkflowBuildResult Build(ModelModel a, ModelModel b) =>
        BridgeWorkflowBuilder.Build(new[] { a, b }, new[]
        {
            new BridgePhaseDefinition { Order = 10, Name = "Steel", State = BridgeSectionState.Steel,
                Actions = new[] { new BridgeActionDefinition { Key = "G1", Source = a, Case = "G", Behavior = BridgeActionBehavior.Steel } } },
            new BridgePhaseDefinition { Order = 20, Name = "Composite", State = BridgeSectionState.Composite,
                Actions = new[] { new BridgeActionDefinition { Key = "G2", Source = b, Case = "G", Behavior = BridgeActionBehavior.LongTerm } } }
        }, new[] { new[] { "", "G1", "G2" }, new[] { "SLS", "1", "1" } }, BridgeWorkflowMethod.Compatible, 1e-6);

    internal static void Near(double actual, double expected, string label)
    {
        if (Math.Abs(actual - expected) > Math.Max(1e-8, Math.Abs(expected) * 1e-12))
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
        var a = Source("A", new[] { 0d, 2500, 5000, 7500, 10000 }, true);
        var b = Source("B", new[] { 0d, 879.044934, 4000, 8500, 10000 });
        var originalA = ModelArchive.Write(a);
        var originalB = ModelArchive.Write(b);
        var merged = Build(a, b);
        foreach (var beam in merged.Model.Beams)
        {
            var forces = beam.Results.OfType<BeamForceResultModel>().ToArray();
            BridgeWorkflowTests.Require(forces.Length == 16, "All eight union stations must exist in both actions.");
            foreach (var f in forces)
            {
                double x = f.Station;
                Near(f.AxialForce, 100 + 2 * x, "Linear N");
                Near(f.Torque, 200 - 3 * x, "Linear T");
                Near(f.Vx, 500 - .1 * x, "Linear V1");
                Near(f.Vy, -800 + .2 * x, "Linear V2");
                Near(f.Mx, 3000 + 500 * x - .05 * x * x, "Quadratic M1");
                Near(f.My, -9000 - 800 * x + .1 * x * x, "Quadratic M2");
                BridgeWorkflowTests.Require(!f.NormalizedLength, "Output stations must be distances.");
            }
        }
        BridgeWorkflowTests.Require(ModelArchive.Write(a).SequenceEqual(originalA) && ModelArchive.Write(b).SequenceEqual(originalB), "Source models mutated.");
        BridgeWorkflowTests.Require(merged.Warnings.Any(w => w.Contains("Station alignment:")), "Resampling must be reported.");
        // Reverse element and sample order: source beam identity and original brackets must still win.
        var reordered = ModelArchive.Clone(b);
        var first = reordered.Beams[0];
        reordered.Elements.Remove(first);
        reordered.Elements.Add(first);
        reordered = ModelArchive.Clone(reordered);
        foreach (var beam in reordered.Beams)
        {
            var values = beam.Results.Reverse().ToArray();
            beam.Results.Clear(); foreach (var f in values) beam.Results.Add(f);
        }
        var other = Build(a, reordered).Model;
        BridgeWorkflowTests.Require(other.Beams.SelectMany(x => x.Results.OfType<BeamForceResultModel>()).Select(x => x.My)
            .SequenceEqual(merged.Model.Beams.SelectMany(x => x.Results.OfType<BeamForceResultModel>()).Select(x => x.My)), "Order changed interpolation.");
        // Moment correction must meet BOTH source values, even if the end moments do not
        // match integration of a linear shear (distributed couples / non-polynomial response).
        var inconsistent = Source("Endpoints", new[] { 0d, 10000 });
        foreach (var beam in inconsistent.Beams) ((BeamForceResultModel)beam.Results[1]).My += 10000;
        var anchored = Build(a, inconsistent).Model.Beams[0].Results.OfType<BeamForceResultModel>().Where(f => f.LoadCase.Name == "G2 [FEM]").ToArray();
        Near(anchored.Single(f => f.Station == 5000).My, -9000 - 800 * 5000 + .1 * 5000 * 5000 + 5000, "Both endpoint moments anchored");
        Near(anchored.Last().My, ((BeamForceResultModel)inconsistent.Beams[0].Results[1]).My, "Original end moment unchanged");
        // Extra target stations must not recursively alter an already resampled result.
        var denser = Source("Dense", new[] { 0d, 1000, 2500, 3000, 5000, 7500, 10000 });
        Near(Build(denser, inconsistent).Model.Beams[0].Results.OfType<BeamForceResultModel>()
            .Single(f => f.Station == 5000 && f.LoadCase.Name == "G2 [FEM]").My, anchored.Single(f => f.Station == 5000).My, "No recursive drift");
        Reject(() => Build(a, Source("Missing end", new[] { 100d, 10000 })), "both beam ends");
        var duplicate = ModelArchive.Clone(b);
        duplicate.Beams[0].Results.Add(duplicate.Beams[0].Results[1]);
        Reject(() => Build(a, duplicate), "expected one");
        var invalid = ModelArchive.Clone(b); ((BeamForceResultModel)invalid.Beams[0].Results[1]).Station = double.NaN;
        Reject(() => Build(a, invalid), "outside beam bounds");
        var multipleSteps = ModelArchive.Clone(b);
        multipleSteps.Beams[0].Results.Add(new BeamForceResultModel(multipleSteps.LoadCases[0], 3000, 1, "Step"));
        Reject(() => Build(a, multipleSteps), "one FEM step");
        var concentrated = Source("Point load", new[] { 0d, 10000 });
        concentrated.Beams[0].Loads.Add(new BeamPointLoadModel(concentrated.LoadCases[0],
            new() { A = .5, P = new Vector3d(0, 1, 0) }, BeamPointLoadModel.LoadType.Force));
        Reject(() => Build(a, concentrated), "concentrated load");
        var rounded = ModelArchive.Clone(a);
        foreach (var beam in rounded.Beams)
            ((BeamForceResultModel)beam.Results[2]).Station += 1e-12; // normalized roundoff
        BridgeWorkflowTests.Require(Build(a, rounded).Model.Beams[0].Results.Count == 10, "Roundoff duplicated stations.");
        ReplayNativeFixture();
        Console.WriteLine("PASS station grids: analytical quadratic diagrams, units, identity, endpoint anchoring, no drift, bounds, steps and jumps");
    }

    private static void ReplayNativeFixture()
    {
        // Captured by --workflow-stations-native --capture-station-fixture from example 19,
        // with reference values requested independently through St7GetBeamResultArrayPos.
        var fixture = JsonSerializer.Deserialize<NativeWorkflowStationTests.Fixture>(File.ReadAllText(NativeWorkflowStationTests.FixturePath));
        var models = fixture.Cases.Select(c =>
        {
            var m = BridgeWorkflowTests.Source(c.Key, 0);
            m.Elements.Clear();
            var beam = new BeamModel(fixture.BeamId, Point3d.Origin, new Point3d(fixture.Length, 0, 0),
                BridgeGrillageExample.LongProperty(new(), 0)) { BeamId = fixture.BeamId };
            foreach (var s in c.Original)
                beam.Results.Add(new BeamForceResultModel(m.LoadCases[0], s.Station, 0, "", false)
                    { AxialForce = s.N, Vx = s.V1, Vy = s.V2, Torque = s.T, Mx = s.M1, My = s.M2 });
            m.AddElements(new[] { beam });
            return ModelArchive.Clone(m);
        }).ToArray();
        var result = NativeWorkflowStationTests.Merge(models);
        foreach (var c in fixture.Cases)
        foreach (var expected in c.NativeAtUnion)
        {
            var f = result.Model.Beams.Single().Results.OfType<BeamForceResultModel>()
                .Single(f => f.LoadCase.Name == c.Key + " [FEM]" && Math.Abs(f.Station - expected.Station) < 1e-6);
            var errors = new[] { f.AxialForce - expected.N, f.Vx - expected.V1, f.Vy - expected.V2,
                f.Torque - expected.T, f.Mx - expected.M1, f.My - expected.M2 };
            BridgeWorkflowTests.Require(errors.Take(4).All(e => Math.Abs(e) < 1e-5) && errors.Skip(4).All(e => Math.Abs(e) < .01),
                $"Native regression: {c.Key}, station {expected.Station}");
            var original = c.Original.SingleOrDefault(s => Math.Abs(s.Station - f.Station) < 1e-6);
            if (original != null)
                BridgeWorkflowTests.Require(f.AxialForce == original.N && f.Vx == original.V1 && f.Vy == original.V2 &&
                    f.Torque == original.T && f.Mx == original.M1 && f.My == original.M2, "Original native sample changed.");
        }
        Console.WriteLine("PASS archived example 19: all four native station grids, including 879.044933748 mm");
    }
}
