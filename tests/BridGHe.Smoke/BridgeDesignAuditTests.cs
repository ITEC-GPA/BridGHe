using System;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class BridgeDesignAuditTests
{
    internal static void Run()
    {
#if GPC_PILOT
        Gpc();
    }

    /// <summary>R6.6 R2a: the audit of a model given by its GPC input (<see cref="BridgeModelAudit.Inspect(BridGHe.Common.Gpc.GpcModels.Conversion, double, SimpleBridgeGeometry, System.Collections.Generic.IReadOnlyList{BridGHe.Common.Gpc.GpcModels.Conversion}, GPC.Model.Models.Model)"/>,
    /// the path of P2): the bridge of <c>BridgeConstructionTests.Model</c> built on GPC (the beams of the assignment on GPC data, one node per
    /// geometric node, the nodes at x = 0 restrained in translation), Build Model on GPC. The same codes; on GPC data a conflicting bearing frame,
    /// a collapsed rigid link and a malformed physical ID are refused by the builders or by Build Model (the audit documents that these
    /// cannot happen there), and the invalid identity diagnosed without throwing is a girder whose station range is reversed.</summary>
    private static void Gpc()
    {
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        var plans = R2aGeneratorFixtures.Assign(geometry, new System.Collections.Generic.Dictionary<int, BridGHe.Common.Gpc.Legacy.LegacyBeamProperty> { [-1] = R2aDesignFixtures.RectangleProperty() }).Plans;
        var beams = R2aGeneratorFixtures.Drafts(plans, "audit");
        var nodes = geometry.Nodes.Select((p, i) =>
        {
            var n = BridGHe.Common.Gpc.Builders.GpcNodeBuilders.Create(p.X, p.Y, p.Z, (i + 1).ToString(), null, R2aGeneratorFixtures.Id("audit node " + i));
            return Math.Abs(p.X) < 1e-8 ? BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithRestraint(n, true, true, true, false, false, false, R2aGeneratorFixtures.Id("audit restraint " + i)) : n;
        }).ToArray();
        BridGHe.Common.Gpc.GpcModels.Conversion Model(System.Collections.Generic.IEnumerable<BridGHe.Common.Gpc.Legacy.LegacyBeamDraft> b, System.Collections.Generic.IEnumerable<BridGHe.Common.Gpc.Legacy.LegacyNodeDraft> n = null) =>
            R2aGeneratorFixtures.Build("Bridge design test", n ?? nodes, b, 1e-6);
        var model = Model(beams);
        Require(!BridgeModelAudit.Inspect(model, 1e-6, geometry).Any(i => i.Severity == "Error"), "Complete bridge assignment accepted");
        var b0 = beams[0];
        var moved = BridGHe.Common.Gpc.Builders.GpcBeamBuilders.Placed(b0, b0.Beam.Name, b0.Beam.StartPoint, new GPC.Geometry.Point3d(b0.Beam.EndPoint.X + 1, b0.Beam.EndPoint.Y, b0.Beam.EndPoint.Z));
        var phase = Model(new[] { moved }.Concat(beams.Skip(1)));
        Require(BridgeModelAudit.Inspect(model, 1e-6, null, new[] { phase }).Any(i => i.Code == "PHASE_GEOMETRY_MISMATCH"), "Changed phase geometry detected");
        var missing = Model(beams.Skip(1));
        Require(BridgeModelAudit.Inspect(missing, 1e-6, geometry).Any(i => i.Code == "UNASSIGNED_BRIDGE_MEMBER"), "Partial physical member coverage detected");
        var turned = BridGHe.Common.Gpc.Builders.GpcBeamBuilders.Override(b0, null, null, b0.AngleDeg + 10, null, R2aGeneratorFixtures.Id("audit turned"));
        Require(BridgeModelAudit.Inspect(Model(new[] { turned }.Concat(beams.Skip(1))), 1e-6).Any(i => i.Code == "SECTION_AXIS_CHANGED"), "Local axis drift detected");
        var rotated = Plane.WorldXY; rotated.Rotate(.2, Vector3d.ZAxis);
        var framed = BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithFrame(nodes[0], 0, 0, 0, 1, 0, 0, 0, 1, 0, R2aGeneratorFixtures.Id("audit frame"));
        R2aGeneratorFixtures.Reject(() => BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithFrame(framed, 0, 0, 0, rotated.XAxis.X, rotated.XAxis.Y, rotated.XAxis.Z,
            rotated.YAxis.X, rotated.YAxis.Y, rotated.YAxis.Z, R2aGeneratorFixtures.Id("audit conflicting frame")), "Conflicting bearing axes");
        var p0 = geometry.Nodes[0];
        var collapsed = BridGHe.Common.Gpc.Builders.GpcBridgeGeometryBuilders.RigidNode(p0, "1", p0, R2aGeneratorFixtures.Id("audit collapsed"));
        R2aGeneratorFixtures.Reject(() => Model(beams, new[] { collapsed }.Concat(nodes.Skip(1))), "Collapsed link");
        int g = Array.FindIndex(plans, p => p.Family == BridgeMemberFamily.Girder);
        R2aGeneratorFixtures.Reject(() => BridGHe.Common.Gpc.Builders.GpcBridgeGeometryBuilders.Assigned(plans[g] with { PhysicalId = null }, R2aGeneratorFixtures.Id("audit no identity")),
            "Malformed identity (no physical ID)");
        var reversed = BridGHe.Common.Gpc.Builders.GpcBridgeGeometryBuilders.Assigned(plans[g] with { StationTo = plans[g].StationFrom - 1 }, R2aGeneratorFixtures.Id("audit reversed"));
        Require(BridgeModelAudit.Inspect(Model(beams.Select((b, i) => i == g ? reversed : b)), 1e-6, geometry).Any(i => i.Code == "INVALID_BRIDGE_IDENTITY"), "Malformed identity is diagnosed without throwing");
        Require(BridgeModelAudit.Inspect(model, 1e-6, null, new BridGHe.Common.Gpc.GpcModels.Conversion[] { null }).Any(i => i.Code == "EMPTY_PHASE_MODEL"), "Missing phase is reported explicitly");
        Console.WriteLine("BRIDGE DESIGN AUDIT PASS: missing assignment, local axis drift, phase geometry, bearing frame conflicts and collapsed links.");
#else
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()); var model = BridgeConstructionTests.Model(geometry);
        Require(!BridgeModelAudit.Inspect(model, 1e-6, geometry).Any(i => i.Severity == "Error"), "Complete bridge assignment accepted");
        var phase = new ModelModel(model); phase.Beams[0].PointTo += Vector3d.XAxis;
        Require(BridgeModelAudit.Inspect(model, 1e-6, null, new[] { phase }).Any(i => i.Code == "PHASE_GEOMETRY_MISMATCH"), "Changed phase geometry detected");
        var missing = new ModelModel(ModelModel.ModelUnits.Nmm);
        missing.AddElements(model.Elements.Where(e => !ReferenceEquals(e, model.Beams[0])));
        Require(BridgeModelAudit.Inspect(missing, 1e-6, geometry).Any(i => i.Code == "UNASSIGNED_BRIDGE_MEMBER"), "Partial physical member coverage detected");
        model.Beams[0].AngleDeg += 10;
        Require(BridgeModelAudit.Inspect(model, 1e-6).Any(i => i.Code == "SECTION_AXIS_CHANGED"), "Local axis drift detected");
        var node = model.Nodes[0]; node.Attributes.Add(new NodeCoordinateAttributeModel(Plane.WorldXY));
        var rotated = Plane.WorldXY; rotated.Rotate(.2, Vector3d.ZAxis);
        node.Attributes.Add(new NodeCoordinateAttributeModel(rotated));
        Require(BridgeModelAudit.Inspect(model, 1e-6).Any(i => i.Code == "CONFLICTING_BEARING_FRAME"), "Conflicting bearing axes");
        node.Attributes.Add(new RigidConnectionAttributeModel(node.Position));
        Require(BridgeModelAudit.Inspect(model, 1e-6).Any(i => i.Code == "INVALID_RIGID_CONNECTION"), "Collapsed link");
        var attribute = model.Beams[0].Attributes.OfType<BridgeMemberAttributeModel>().Single();
        attribute.Value = attribute.Value with { PhysicalId = null };
        Require(BridgeModelAudit.Inspect(model, 1e-6, geometry).Any(i => i.Code == "INVALID_BRIDGE_IDENTITY"), "Malformed identity is diagnosed without throwing");
        Require(BridgeModelAudit.Inspect(model, 1e-6, null, new ModelModel[] { null }).Any(i => i.Code == "EMPTY_PHASE_MODEL"), "Missing phase is reported explicitly");
        Console.WriteLine("BRIDGE DESIGN AUDIT PASS: missing assignment, local axis drift, phase geometry, bearing frame conflicts and collapsed links.");
#endif
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE AUDIT: " + text); }
}
