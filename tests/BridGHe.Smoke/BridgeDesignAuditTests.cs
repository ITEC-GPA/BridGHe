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
        Console.WriteLine("BRIDGE DESIGN AUDIT PASS: missing assignment, local axis drift, phase geometry, bearing frame conflicts and collapsed links.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE AUDIT: " + text); }
}
