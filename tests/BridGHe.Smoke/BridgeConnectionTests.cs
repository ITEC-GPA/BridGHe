using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using Rhino.Geometry;
using St7API;

internal static class BridgeConnectionTests
{
    internal static void Run()
    {
        var line = new Line(Point3d.Origin, new Point3d(0, 0, 1500));
        var rigid = BridgeConnections.Create(new[] { line }, true);
        var nodes = rigid.Cast<NodeModel>().ToList(); var beams = new List<BeamModel>(); var plates = new List<PlateModel>();
        ModelHelper.MergeModel(ref nodes, ref beams, ref plates, 1e-6);
        var model = new ModelModel(ModelModel.ModelUnits.Nmm); model.AddElements(nodes);
        var links = RigidConnectionExport.Resolve(model, 1e-6);
        Require(links.Length == 1 && links[0] == (1, 2), "Rigid connectivity after merge");
        Require(RigidConnectionExport.Resolve(ModelArchive.Clone(model), 1e-6).SequenceEqual(links), "Rigid connectivity archives");
        var flexible = BridgeConnections.Create(new[] { line }, false, BridgeAssignmentTests.Property());
        Require(flexible.Length == 1 && flexible[0] is BeamModel b && b.Length == 1500, "Explicit deformable beam");
        var beam = (BeamModel)flexible[0]; var shifted = BridgeConnections.Offset(new[] { beam }, new(12, -20))[0];
        Require(shifted.Offset == new Vector2d(12, -20) && beam.Offset == Vector2d.Zero, "Offsets isolate input and use local x/y");
        Reject(() => BridgeConnections.Create(new[] { line, new Line(line.To, line.From) }, true));
        Reject(() => BridgeConnections.Create(new[] { new Line(Point3d.Origin, Point3d.Origin) }, true));
        Reject(() => BridgeConnections.Create(new[] { line }, false));
        var bad = new ModelModel(ModelModel.ModelUnits.Nmm); var collapsed = new NodeModel("A", Point3d.Origin);
        collapsed.Attributes.Add(new RigidConnectionAttributeModel(Point3d.Origin)); bad.AddElements(new[] { collapsed });
        Reject(() => RigidConnectionExport.Resolve(bad, 1e-6));
        Console.WriteLine("BRIDGE CONNECTIONS PASS: merged connectivity, archives, real deformable members, isolated offsets and duplicate/collapsed rejection.");
    }
    internal static void Native()
    {
        lock (StrausRuntime.Gate)
        {
            using var temp = StrausRuntime.NewTemporaryModel();
            var model = new ModelModel(ModelModel.ModelUnits.Nmm);
            model.AddElements(BridgeConnections.Create(new[] { new Line(Point3d.Origin, new Point3d(10, 20, 30)) }, true));
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                var p = model.Nodes[i].Position;
                StrausRuntime.Check(St7.St7SetNodeXYZ(temp.Id, i + 1, new[] { p.X, p.Y, p.Z }), "Set connection node");
            }
            RigidConnectionExport.Export(temp.Id, model, 1e-6);
            int ucs = 0, plane = -1; var connection = new int[3];
            StrausRuntime.Check(St7.St7GetRigidLink(temp.Id, 1, ref ucs, ref plane, connection), "Read native rigid link");
            Require(ucs == 1 && plane == St7.rlPlaneXYZ && connection.SequenceEqual(new[] { 2, 1, 2 }), "Vendor rigid link roundtrip");
        }
        Console.WriteLine("BRIDGE CONNECTIONS NATIVE PASS: Straus full spatial rigid link readback; no solver run.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE CONNECTIONS: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid connection accepted"); }
}
