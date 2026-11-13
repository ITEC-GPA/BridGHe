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
        var phase = new BridgePhaseDefinition { Order = 2, Name = "Getto", Day = 28, State = BridgeSectionState.Composite };
        Gpc(geometry, phase);
    }
    /// <summary>R6.6 R2b: Bridge Construction Sequence on GPC (<c>GpcModelTransforms.ConstructionSequence</c>) on the bridge model built in
    /// GPC (<see cref="GpcSimpleBridge"/>, public input <c>FromGpc</c>) with a phase property built in GPC, no FEM model: group selection,
    /// explicit phase property, inactive nodes pruned and bearings removed, input isolated, physical selection, unknown groups and bearings
    /// rejected. The expected snapshots are written from the input (they replace the former parity with the FEM snapshots): the beams of the
    /// selected group or physical member in input order, with their GUIDs, names and end points; the nodes those beams use, at their positions
    /// and without restraint; the physical members of those beams. The native stage of the FEM snapshot (Stage, group activation, the
    /// partial and ungrouped cases) has no GPC counterpart: the GPC snapshot drops stages and their freedom case (retired with the FEM path).</summary>
    private static void Gpc(SimpleBridgeGeometry geometry, BridgePhaseDefinition phase)
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
        // L12: the members are bridge data by beam GUID (the snapshot keeps the GUIDs of its source).
        string[] Members(GPC.Model.Models.Model m) => m.BeamElements.Values.OrderBy(b => b.Id).Select(b => input.Bridge.Member(b.Guid)?.PhysicalId).Where(id => id != null).Distinct().ToArray();
        Require(physical.BeamElements.Count == 40 && Members(physical).Single() == "Girder:2:1", "GPC: Physical selection independent of beam ordering");
        Reject(() => Snapshot(new[] { "missing" }, null, null, "missing group"));
        Reject(() => Snapshot(null, null, new[] { "missing" }, "missing bearing"));
        // The expected snapshots from the input: the selected beams in input order and the nodes they use.
        var inputBeams = input.Model.BeamElements.Values.OrderBy(b => b.Id).ToArray();
        void Expected(GPC.Model.Models.Model snap, Func<GPC.Model.Elements.BeamElement, bool> selected, string label)
        {
            var want = inputBeams.Where(selected).ToArray();
            var beams = snap.BeamElements.Values.OrderBy(b => b.Id).ToArray(); var nodes = snap.NodesElements.Values.OrderBy(n => n.Id).ToArray();
            static bool Same(GPC.Geometry.Point3d a, GPC.Geometry.Point3d b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
            Require(want.Length == 40 && beams.Length == want.Length && beams.Zip(want).All(p => p.First.Guid == p.Second.Guid && p.First.Name == p.Second.Name
                && Same(p.First.StartPoint, p.Second.StartPoint) && Same(p.First.EndPoint, p.Second.EndPoint)), $"GPC {label}: the selected beams of the input, in order, with their GUIDs, names and ends");
            var used = want.SelectMany(b => new[] { b.StartPoint, b.EndPoint }).ToArray();
            var wantNodes = input.Model.NodesElements.Values.OrderBy(n => n.Id).Where(n => used.Any(p => Same(p, n.Position))).ToArray();
            Require(nodes.Length == wantNodes.Length && nodes.Zip(wantNodes).All(p => p.First.Name == p.Second.Name && Same(p.First.Position, p.Second.Position)),
                $"GPC {label}: the nodes of the selected beams, in order, at their positions");
            Require(Members(snap).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(want.Select(b => input.Bridge.Member(b.Guid).PhysicalId).Distinct().OrderBy(x => x, StringComparer.Ordinal)),
                $"GPC {label}: the physical members of the selected beams");
        }
        Expected(snapshot, b => b.Groups.Values.Any(g => g.Name.StartsWith("Girder\\Group1\\", StringComparison.Ordinal)), "group snapshot");
        Expected(physical, b => input.Bridge.Member(b.Guid)?.PhysicalId == "Girder:2:1", "physical snapshot");
        Console.WriteLine("BRIDGE CONSTRUCTION PASS (GPC data alone): active groups/physical members, bearing removal, phase override, node pruning; snapshots equal to the selection of the input (beams, nodes, positions, physical members).");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE CONSTRUCTION: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid phase selection accepted"); }
}
