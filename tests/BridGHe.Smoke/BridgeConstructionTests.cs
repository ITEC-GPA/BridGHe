using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class BridgeConstructionTests
{
    internal static ModelModel Model(SimpleBridgeGeometry geometry)
    {
        var model = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = "Bridge design test" };
        var assignment = BridgeMemberAssignment.Assign(geometry, new Dictionary<int, BeamPropertyModel> { [-1] = BridgeAssignmentTests.Property() }, null);
        model.AddElements(assignment.Beams);
        var nodes = geometry.Nodes.Select((p, i) => new NodeModel((i + 1).ToString(), p)).ToArray();
        foreach (var node in nodes.Where(n => Math.Abs(n.Position.X) < 1e-8))
            node.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, false, false, false));
        model.AddElements(nodes); model.LoadCases.Add(new LoadCaseModel("G1", "G1")); return model;
    }
    internal static void Run()
    {
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        var source = Model(geometry); int beamCount = source.Beams.Count, nodeCount = source.Nodes.Count;
        var phase = new BridgePhaseDefinition { Order = 2, Name = "Getto", Day = 28, State = BridgeSectionState.Composite };
        var snapshot = BridgeConstructionSequence.Create(source, phase, new[] { "Girder\\Group1" }, null, Array.Empty<string>(),
            BridgeAssignmentTests.Property("Composite phase"), null);
        Require(snapshot.Model.Beams.Count == 40 && snapshot.Model.Plates.Count == 0, "Group selects only G1");
        Require(snapshot.Model.Beams.All(b => b.BeamProperty.Id == "Composite phase"), "Explicit phase property");
        Require(snapshot.Model.Nodes.Count == 41 && !snapshot.Model.Nodes.Any(n => n.Attributes.OfType<NodeRestraintAttributeModel>().Any()), "Inactive nodes pruned and bearings removed");
        Require(snapshot.Stage == null, "Native stage withheld for changed bearings/properties");
        Require(source.Beams.Count == beamCount && source.Nodes.Count == nodeCount && source.Beams.All(b => b.BeamProperty.Id == "P"), "Input model isolated");
        var increment = new LoadCombinationModel("G1"); increment.Values.Add(source.LoadCases[0], 1);
        var native = BridgeConstructionSequence.Create(source, phase, new[] { "Girder\\Group1" }, null, new[] { "*" }, null, new[] { increment });
        Require(native.Stage != null && native.Stage.Groups.Any(g => g.FullName == "Girder") &&
            native.Stage.Groups.Any(g => g.FullName == "Girder\\Group1\\Segment1"), "Native activation includes leaf and ancestors");
        Require(native.Stage.LoadCombinations.Single().Values.Single().Value == 1, "Native load increment preserved");
        var physical = BridgeConstructionSequence.Create(source, phase, null, new[] { "Girder:2:1" }, new[] { "*" }, null, null);
        Require(physical.Model.Beams.Count == 40 && physical.ActivePhysicalIds.Single() == "Girder:2:1", "Physical selection independent of beam ordering");
        Reject(() => BridgeConstructionSequence.Create(source, phase, new[] { "missing" }, null, null, null, null));
        Reject(() => BridgeConstructionSequence.Create(source, phase, null, null, new[] { "missing" }, null, null));
        Console.WriteLine("BRIDGE CONSTRUCTION PASS: active groups/physical members, bearing removal, phase override, node pruning and native-stage eligibility.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE CONSTRUCTION: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid phase selection accepted"); }
}
