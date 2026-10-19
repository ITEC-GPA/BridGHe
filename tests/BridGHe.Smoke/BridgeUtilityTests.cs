using System;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using Rhino.Geometry;

internal static class BridgeUtilityTests
{
    private static int checks;
    internal static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
    private static void Reject(Action call, string message)
    {
        try { call(); } catch (ArgumentException) { checks++; return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }
    internal static ModelModel Source()
    {
        var model = BridgeWorkflowTests.Source("Controlled result samples N mm", 100);
        var q = new LoadCaseModel("Q", "Q"); model.LoadCases.Add(q);
        foreach (var b in model.Beams)
            foreach (var f in b.Results.OfType<BeamForceResultModel>().ToArray())
                b.Results.Add(new BeamForceResultModel(q, f.Station * b.Length, 2, "Moving", false) { AxialForce = -50, Vx = 13, Vy = -17, Torque = 19, Mx = -23, My = -2 * f.My });
        var node = new NodeModel("A", Point3d.Origin); node.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, false, false, false));
        node.Results.Add(new NodeReactResultModel(model.LoadCases[0]) { Force = new Vector3d(10, 20, 100), Moment = new Vector3d(1, 2, 3) });
        node.Results.Add(new NodeReactResultModel(q, 2, "Moving") { Force = new Vector3d(50, 60, -120), Moment = new Vector3d(4, 5, 6) });
        node.Results.Add(new NodeDisplResultModel(model.LoadCases[0]) { Displacement = new Vector3d(1, 2, -10), Rotation = new Vector3d(.1, .2, .3) });
        node.Results.Add(new NodeDisplResultModel(q, 2, "Moving") { Displacement = new Vector3d(3, 4, 8), Rotation = new Vector3d(.4, .5, .6) });
        model.AddElements(new[] { node }); return model;
    }
    internal static MovingPath Path() => new()
    {
        LoadCaseName = "Prototype", Shape = 0, Coordinates = new[] { 0d, 0, 0, 20000, 0, 0, 0, 1000, 0 },
        Template = new MovingTemplate { Vehicles = new[] { new MovingVehicle { Loads = new MovingObject[] { new MovingPointForce { Force = new[] { 0d, 0, -100000 } } } } } }
    };
    internal static void Run()
    {
        checks = 0; var model = Source(); var before = ModelArchive.Write(model);
        var issues = BridgeModelAudit.Inspect(model, 1e-6);
        Assert(!issues.Any(i => i.Severity == "Error"), "Audit rejected valid input.");
        Assert(issues.Any(i => i.Code == "DISCONNECTED"), "Audit missed disconnected girders.");
        Assert(!issues.Any(i => i.Code == "ISOLATED_NODE"), "Audit missed connected support.");
        var bad = ModelArchive.Clone(model); bad.AddElements(new[] { new NodeModel("Floating", new Point3d(700, 900, 800)) });
        Assert(BridgeModelAudit.Inspect(bad, 1e-6).Any(i => i.Code == "ISOLATED_NODE" && i.Element == "Floating"), "Isolated node not detected.");
        bad.Beams[0].PointTo = bad.Beams[0].PointFrom;
        Assert(BridgeModelAudit.Inspect(bad, 1e-6).Any(i => i.Code == "INVALID_BEAM"), "Zero length missed.");
        Reject(() => BridgeModelAudit.Inspect(model, 0), "zero tolerance");
        var property = ModelArchive.Clone(model.Beams[0].BeamProperty); property.Section.SectionArea *= 2;
        var phase = BridgePhaseModel.Create(model, "Long term", new[] { "B0" }, property, true);
        Assert(phase.Beams[0].SectionAreaForTest() == property.SectionArea && phase.Beams[1].BeamProperty.SectionArea == model.Beams[1].BeamProperty.SectionArea, "Phase selection/property failed.");
        Assert(phase.Elements.All(e => e.Results.Count == 0 && e.Loads.Count == 0) && phase.LoadCases.Count == 0, "Phase retained stale data.");
        Assert(phase.Beams.Select(b => b.BeamId).SequenceEqual(model.Beams.Select(b => b.BeamId)) && phase.Beams[0].PointFrom == model.Beams[0].PointFrom, "Phase changed IDs/geometry.");
        phase.Beams[0].BeamProperty.Section.SectionArea += 1;
        Assert(property.SectionArea != phase.Beams[0].BeamProperty.SectionArea, "Phase property aliases input.");
        Assert(BridgePhaseModel.Create(model, "Q", new[] { "B0" }, property, false).LoadCases.Count == 2, "Retain-loads option lost cases.");
        Reject(() => BridgePhaseModel.Create(model, "P", new[] { "missing" }, property, true), "unknown beam");
        Reject(() => BridgePhaseModel.Create(model, "P", Array.Empty<string>(), property, true), "empty selection");
        var env = BridgeResultEnvelope.Evaluate(model, 0, null, null, 5, 2);
        Assert(env.Length == 6 && env.All(r => r.Case == "Q" && r.Step == 2 && r.StepType == "Moving" && r.Value < 0), "Signed absolute beam envelope incorrect.");
        Assert(env[1].Station == 5000 && env.All(r => r.Components.SequenceEqual(new[] { -50d, 13, -17, 19, -23, r.Value })), "Normalized stations/concomitants lost.");
        Assert(BridgeResultEnvelope.Evaluate(model, 0, null, null, 5, 1).All(r => r.Case == "G"), "Maximum envelope incorrect.");
        Assert(BridgeResultEnvelope.Evaluate(model, 0, new[] { "B0" }, new[] { "G" }, 0, 0).Length == 3, "Selection failed.");
        var reaction = BridgeResultEnvelope.Evaluate(model, 1, new[] { "A" }, null, 2, 0).Single();
        Assert(reaction.Value == -120 && reaction.Components[0] == 50 && reaction.Case == "Q", "Bearing reactions are not concomitant.");
        Assert(BridgeResultEnvelope.Evaluate(model, 2, null, null, 2, 2).Single().Value == -10, "Displacement sign lost.");
        Reject(() => BridgeResultEnvelope.Evaluate(model, 0, null, new[] { "missing" }, 0, 0), "unknown case");
        Reject(() => BridgeResultEnvelope.Evaluate(model, 0, new[] { "missing" }, null, 0, 0), "unknown element");
        Reject(() => BridgeResultEnvelope.Evaluate(model, 0, null, null, 6, 0), "invalid component");
        bad = ModelArchive.Clone(model); bad.Beams[0].Results.RemoveAt(3);
        Assert(BridgeResultEnvelope.Evaluate(bad, 0, null, null, 5, 2).Any(r => r.Coverage.StartsWith("Missing")), "Incomplete case grid hidden.");
        bad = ModelArchive.Clone(model); bad.Beams[0].Results.Add(bad.Beams[0].Results[0]);
        Reject(() => BridgeResultEnvelope.Evaluate(bad, 0, null, null, 0, 0), "duplicate sample");
        bad = ModelArchive.Clone(model); ((BeamForceResultModel)bad.Beams[0].Results[0]).AxialForce = double.NaN;
        Reject(() => BridgeResultEnvelope.Evaluate(bad, 0, null, null, 5, 2), "NaN concomitant");
        var action = new BridgeActionDefinition { Key = "G1", Source = model, Case = "G" };
        var shrink = new BridgeActionDefinition { Key = "CS", PrimaryOnly = true, Strain = -200e-6, Behavior = BridgeActionBehavior.Shrinkage };
        var p1 = new BridgePhaseDefinition { Order = 10, Name = "Steel", State = BridgeSectionState.Steel, Day = 1, Actions = new[] { action } };
        var p2 = new BridgePhaseDefinition { Order = 20, Name = "Shrinkage", State = BridgeSectionState.Composite, Day = 28, Actions = new[] { shrink } };
        var table = BridgeWorkflowTools.CombinationTable(new[] { p2, p1 }, new[] { "CS", "G1" }, new[] { "SLS" }, new[] { new[] { .7, 1d } });
        Assert(table[0].SequenceEqual(new[] { "Combination", "G1", "CS" }) && table[1].SequenceEqual(new[] { "SLS", "1", "0.7" }), "Combination keys reordered incorrectly.");
        var built = BridgeWorkflowBuilder.Build(new[] { model }, new[] { p2, p1 }, table, BridgeWorkflowMethod.Compatible, 1e-6);
        Assert(built.Model.LoadCombinations.Single().Values.Any(v => Math.Abs(v.Value - .7) < 1e-15), "Generated table not accepted by builder.");
        Assert(BridgeWorkflowTools.CombinationTable(new[] { p1, p2 }, new[] { "G1" }, new[] { "SLS" }, new[] { new[] { 1d } })[1][2] == "0", "Omitted action not zero.");
        Reject(() => BridgeWorkflowTools.Ordered(new[] { p1, p2 with { Order = 10 } }), "duplicate phase");
        Reject(() => BridgeWorkflowTools.Ordered(new[] { p1, p2 with { Day = 0 } }), "backward day");
        Reject(() => BridgeWorkflowTools.CombinationTable(new[] { p1 }, new[] { "bad" }, new[] { "S" }, new[] { new[] { 1d } }), "unknown action");
        Reject(() => BridgeWorkflowTools.CombinationTable(new[] { p1 }, new[] { "G1" }, new[] { "S" }, new[] { new[] { double.NaN } }), "NaN coefficient");
        var increments = BridgeWorkflowTools.ShrinkageIncrements(new[] { 7d, 28, 100 }, new[] { -100e-6, -200e-6, -250e-6 }, -50e-6);
        Assert(Math.Abs(increments.Sum() + 200e-6) < 1e-16 && Math.Abs(increments[0] + 50e-6) < 1e-16, "Shrinkage increment balance incorrect.");
        Reject(() => BridgeWorkflowTools.ShrinkageIncrements(new[] { 1d, 1 }, new[] { -1e-6, -2e-6 }, 0), "duplicate day");
        Reject(() => BridgeWorkflowTools.ShrinkageIncrements(new[] { 1d, 2 }, new[] { -2e-6, -1e-6 }, 0), "non-monotonic shrinkage");
        var compare = BridgeResultComparison.Compare(new[] { "a", "b", "c" }, new[] { 100d, 0, -100 }, new[] { "c", "a", "b" }, new[] { -100d, 100.5, 1e-8 }, 1e-6, .01);
        Assert(compare.All(r => r.WithinTolerance) && double.IsNaN(compare[1].RelativeDifference) && compare[0].Difference == .5, "Keyed numerical comparison failed.");
        Assert(!BridgeResultComparison.Compare(new[] { "a" }, new[] { 0d }, new[] { "a" }, new[] { 1d }, 1e-6, .01)[0].WithinTolerance, "Near-zero mismatch passed.");
        Reject(() => BridgeResultComparison.Compare(new[] { "a" }, new[] { 1d }, new[] { "b" }, new[] { 1d }, 0, 0), "different physical keys");
        Reject(() => BridgeResultComparison.Compare(new[] { "a" }, new[] { 1d }, new[] { "a" }, new[] { double.NaN }, 0, 0), "NaN candidate");
        var prototype = Path(); var lanes = BridgeMovingLanes.Create(prototype, new[] { -3000d, 0, 3000 }, new[] { "L1", "L2", "L3" });
        Assert(lanes[0].Coordinates[1] == -3000 && lanes[2].Coordinates[7] == 4000, "Lane offset coordinates incorrect.");
        lanes[0].Template.Vehicles[0].Loads.ClearForTest();
        Assert(prototype.Template.Vehicles[0].Loads.Length == 1 && lanes[1].Template.Vehicles[0].Loads[0] is MovingPointForce, "Lane templates alias.");
        prototype.Shape = 1; Reject(() => BridgeMovingLanes.Create(prototype, new[] { 0d }, new[] { "C" }), "curved prototype");
        prototype.Shape = 0; Reject(() => BridgeMovingLanes.Create(prototype, new[] { 0d, 0 }, new[] { "C", "D" }), "duplicate lane");
        Assert(ModelArchive.Write(model).SequenceEqual(before), "Utilities mutated source model.");
        Console.WriteLine($"BRIDGE UTILITIES PASS: {checks} checks; no Straus solver invoked.");
    }
    private static double SectionAreaForTest(this BeamModel beam) => beam.BeamProperty.SectionArea;
    private static void ClearForTest(this MovingObject[] loads) { loads[0] = new MovingHeatSource { SizeX = 1, SizeY = 1, Heat = 10 }; }
}
