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
#if GPC_PILOT
        Gpc(geometry, phase, snapshot.Model, physical.Model);
#endif
        var sharedGroups = new ModelModel(source);
        foreach (var beam in sharedGroups.Beams) { beam.Groups.Clear(); beam.Groups.Add(new GroupModel("AllSteel")); }
        var partial = BridgeConstructionSequence.Create(sharedGroups, phase, null, new[] { "Girder:2:1" }, new[] { "*" }, null, new[] { increment });
        Require(partial.Stage == null && partial.Model.Beams.Count == 40, "Partial physical selection cannot activate an entire shared native group");
        sharedGroups.Beams[0].Groups.Clear();
        Require(BridgeConstructionSequence.Create(sharedGroups, phase, null, null, new[] { "*" }, null, new[] { increment }).Stage == null,
            "Ungrouped structural elements cannot be represented by group activation");
        Reject(() => BridgeConstructionSequence.Create(source, phase, new[] { "missing" }, null, null, null, null));
        Reject(() => BridgeConstructionSequence.Create(source, phase, null, null, new[] { "missing" }, null, null));
        Console.WriteLine("BRIDGE CONSTRUCTION PASS: active groups/physical members, bearing removal, phase override, node pruning and native-stage eligibility.");
    }
#if GPC_PILOT
    /// <summary>P2T: Bridge Construction Sequence on GPC (<c>GpcModelTransforms.ConstructionSequence</c>, chain D) on the bridge model built in
    /// GPC (<see cref="GpcSimpleBridge"/>, public input <c>FromGpc</c>) with a phase property built in GPC: the same checks as the FEM ones on the
    /// snapshot model (group selection, explicit phase property, inactive nodes pruned and bearings removed, input isolated, physical selection,
    /// unknown groups and bearings rejected), then the parity with the FEM snapshots by correspondence (BridGHe IDs and positions; the GPC
    /// output keeps the GUIDs of its source and is renumbered 1..n): the same beams and nodes in the same order, the same positions, no
    /// restraint, the same physical members. The native stage of the FEM snapshot (Stage, group activation) has no GPC counterpart: the
    /// GPC snapshot drops stages and their freedom case, so the stage checks stay on the FEM path.</summary>
    private static void Gpc(SimpleBridgeGeometry geometry, BridgePhaseDefinition phase, ModelModel femSnapshot, ModelModel femPhysical)
    {
        var input = GpcSimpleBridge.Input(geometry);
        int beamCount = input.Model.BeamElements.Count, nodeCount = input.Model.NodesElements.Count;
        var composite = BridGHe.Common.Gpc.GpcModels.BeamProperty(new GpcTestModel("Construction properties").Property("Composite phase", area: 20000),
            GPC.Model.Structure.Assignments.BeamFormulation.StraightTwoNode);
        GPC.Model.Models.Model Snapshot(string[] groups, string[] ids, string[] bearings, string key) =>
            BridGHe.Common.Gpc.GpcModelTransforms.ConstructionSequence(input, phase.Order, phase.Name, phase.Day, groups, ids, bearings, groups == null ? null : composite,
                BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace, "p2t-construction:" + key)).Model;
        var snapshot = Snapshot(new[] { "Girder\\Group1" }, null, Array.Empty<string>(), "group");
        Require(snapshot.BeamElements.Count == 40 && snapshot.AreaElements.Count == 0, "GPC: Group selects only G1");
        Require(snapshot.BeamElements.Values.All(b => b.BeamProperty.Name == "Composite phase"), "GPC: Explicit phase property");
        Require(snapshot.NodesElements.Count == 41 && !snapshot.NodesElements.Values.Any(n => n.Assignments.Restrains.Count != 0), "GPC: Inactive nodes pruned and bearings removed");
        Require(snapshot.Stages.Count == 0 && snapshot.FreedomCases.Count == 0, "GPC: stages and their freedom case go");
        Require(input.Model.BeamElements.Count == beamCount && input.Model.NodesElements.Count == nodeCount && input.Model.BeamElements.Values.All(b => b.BeamProperty.Name == "P"),
            "GPC: Input model isolated");
        var physical = Snapshot(null, new[] { "Girder:2:1" }, new[] { "*" }, "physical");
        string[] Members(GPC.Model.Models.Model m) => m.PhysicalMembers.Values.SelectMany(d => d.Parts).Where(p => p.BridgeMetadata != null).Select(p => p.BridgeMetadata.PhysicalId).Distinct().ToArray();
        Require(physical.BeamElements.Count == 40 && Members(physical).Single() == "Girder:2:1", "GPC: Physical selection independent of beam ordering");
        Reject(() => Snapshot(new[] { "missing" }, null, null, "missing group"));
        Reject(() => Snapshot(null, null, new[] { "missing" }, "missing bearing"));
        // Parity (removed with the FeMM models), by correspondence: BridGHe IDs and positions in the order of the outputs.
        void Same(GPC.Model.Models.Model gpc, ModelModel fem, string label)
        {
            var beams = gpc.BeamElements.Values.OrderBy(b => b.Id).ToArray(); var nodes = gpc.NodesElements.Values.OrderBy(n => n.Id).ToArray();
            Require(beams.Select(b => b.Name).SequenceEqual(fem.Beams.Select(b => b.BeamId)), $"GPC {label}: the beams of the FEM snapshot, in order");
            Require(nodes.Select(n => n.Name).SequenceEqual(fem.Nodes.Select(n => n.NodeId)), $"GPC {label}: the nodes of the FEM snapshot, in order");
            Require(nodes.Zip(fem.Nodes).All(p => p.First.Position.X == p.Second.Position.X && p.First.Position.Y == p.Second.Position.Y && p.First.Position.Z == p.Second.Position.Z),
                $"GPC {label}: node positions of the FEM snapshot");
            Require(nodes.Zip(fem.Nodes).All(p => (p.First.Assignments.Restrains.Count != 0) == p.Second.Attributes.OfType<NodeRestraintAttributeModel>().Any()), $"GPC {label}: restraints of the FEM snapshot");
            Require(Members(gpc).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(fem.Beams.SelectMany(b => b.Attributes.OfType<BridgeMemberAttributeModel>()).Select(a => a.Value.PhysicalId).Distinct()
                .OrderBy(x => x, StringComparer.Ordinal)), $"GPC {label}: physical members of the FEM snapshot");
            Require(beams.Select(b => b.Guid).Distinct().Count() == beams.Length && beams.All(b => input.Model.BeamElements.Values.Any(s => s.Guid == b.Guid && s.Name == b.Name)),
                $"GPC {label}: the beams keep the GUIDs of their source");
        }
        Same(snapshot, femSnapshot, "group snapshot"); Same(physical, femPhysical, "physical snapshot");
        Console.WriteLine("BRIDGE CONSTRUCTION GPC PASS: the snapshots of the bridge model built in GPC equal the FEM ones by correspondence (beams, nodes, positions, restraints, physical members).");
    }
#endif
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE CONSTRUCTION: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid phase selection accepted"); }
}
