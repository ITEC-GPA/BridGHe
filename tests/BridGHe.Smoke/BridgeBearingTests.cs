using System;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using Rhino.Geometry;
using St7API;

internal static class BridgeBearingTests
{
    internal static void Run()
    {
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input(curved: true));
        var bearings = BridgeBearings.Create(geometry, new[] { 2 }, null, new[] { 0, 1, 2 }, 23, Vector3d.Zero, Vector3d.Zero);
        Require(bearings.Nodes.Length == 3, "Selection count");
        var masks = bearings.Nodes.Select(n => n.Attributes.OfType<NodeRestraintAttributeModel>().Single().Value).ToArray();
        Require(masks[0].Tx && masks[0].Ty && masks[0].Tz && !masks[0].Rx, "Fixed translations, free rotations");
        Require(!masks[1].Tx && masks[1].Ty && masks[1].Tz && !masks[1].Ry, "Longitudinal guide");
        Require(!masks[2].Tx && !masks[2].Ty && masks[2].Tz, "Multidirectional");
        foreach (var node in bearings.Nodes)
        {
            var frame = NodeCoordinateExport.Frame(node).Value;
            Require(frame.ZAxis * Vector3d.ZAxis > 1 - 1e-12, "Default bearing frames horizontal");
            Require(NodeCoordinateExport.Frame(ModelArchive.Clone(node)) == frame, "Nodal frame persists in archive");
        }
        var elastic = BridgeBearings.Create(geometry, new[] { 1 }, new[] { 1 }, new[] { 3 }, 0, new Vector3d(10, 20, 30), new Vector3d(40, 50, 60));
        var stiffness = elastic.Nodes[0].Attributes.OfType<NodeStiffnessAttributeModel>().Single().Value;
        Require(stiffness.Tx == 10 && stiffness.Ty == 20 && stiffness.Rz == 60, "Local elastic stiffnesses");
        var conflicting = ModelArchive.Clone(bearings.Nodes[0]);
        conflicting.Attributes.Add(new NodeCoordinateAttributeModel(Plane.WorldXY));
        Reject(() => NodeCoordinateExport.Frame(conflicting));
        Reject(() => BridgeBearings.Create(geometry, new[] { 999 }, null, new[] { 2 }, 0, Vector3d.Zero, Vector3d.Zero));
        Reject(() => BridgeBearings.Create(geometry, null, null, new[] { 3 }, 0, Vector3d.Zero, Vector3d.Zero));
        Console.WriteLine("BRIDGE BEARINGS PASS: masks, curved guide frames, elastic stiffnesses, archives and conflicting frames.");
    }
    internal static void Native()
    {
        lock (StrausRuntime.Gate)
        {
            using var model = StrausRuntime.NewTemporaryModel();
            var frame = new Plane(new Point3d(100, 200, 300), new Vector3d(1, 2, 0), new Vector3d(-2, 1, 0));
            var node = new NodeModel("N", frame.Origin);
            node.Attributes.Add(new NodeCoordinateAttributeModel(frame));
            StrausRuntime.Check(St7.St7SetNodeXYZ(model.Id, 1, new[] { 100d, 200, 300 }), "Set test node");
            int ucs = NodeCoordinateExport.Create(model.Id, node, 1);
            var axis = new[] { 1d, 0, 0 };
            StrausRuntime.Check(St7.St7VectorTransformToXYZ(model.Id, ucs, new[] { 100d, 200, 300 }, axis), "Transform UCS axis");
            Require(new Vector3d(axis[0], axis[1], axis[2]) * frame.XAxis > 1 - 1e-12, "Vendor UCS X orientation");
            StrausRuntime.Check(St7.St7SetNodeRestraint6(model.Id, 1, 1, ucs, new[] { 0, 1, 1, 0, 0, 0 }, new double[6]), "Set local guided bearing");
            int readUcs = 0; var status = new int[6]; var data = new double[6];
            StrausRuntime.Check(St7.St7GetNodeRestraint6(model.Id, 1, 1, ref readUcs, status, data), "Read local guided bearing");
            Require(readUcs == ucs && status.SequenceEqual(new[] { 0, 1, 1, 0, 0, 0 }), "Vendor restraint roundtrip");
            StrausRuntime.Check(St7.St7SetNodeKTranslation3F(model.Id, 1, 1, ucs, new[] { 10d, 20, 30 }), "Set local springs");
            var k = new double[3];
            StrausRuntime.Check(St7.St7GetNodeKTranslation3F(model.Id, 1, 1, ref readUcs, k), "Read local springs");
            Require(readUcs == ucs && k.SequenceEqual(new[] { 10d, 20, 30 }), "Vendor spring roundtrip");
        }
        Console.WriteLine("BRIDGE BEARINGS NATIVE PASS: actual Straus R3 UCS, restraints and spring readback; no solver run.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE BEARINGS: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid bearing was accepted"); }
}
