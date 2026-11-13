using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Straus;
using Rhino.Geometry;

internal static class BridgeConnectionTests
{
    internal static void Run()
    {
        Gpc();
    }

    /// <summary>R6.6 R2a: the connections by their GPC drafts (the builders of the component on the path of P2, in the order of
    /// <c>BridgeConnections.Create</c>): the rigid pair resolved by Build Model on GPC (the rigid link of the model), the archive of the drafts, the
    /// connection beam on a property given by its GPC datum, the offset on GPC (Beam Override of the offset), the refusals of the checks of
    /// <c>BridgeConnections.Create</c> on lines and on the GPC property flag, and the collapsed link refused by Build Model.</summary>
    private static (BridGHe.Common.Gpc.Legacy.LegacyNodeDraft A, BridGHe.Common.Gpc.Legacy.LegacyNodeDraft B) Rigid(Line line, int i, string tag) =>
        (BridGHe.Common.Gpc.Builders.GpcBridgeGeometryBuilders.RigidNode(line.From, $"Rigid{i}A", line.To, R2aGeneratorFixtures.Id(tag + " A")),
         BridGHe.Common.Gpc.Builders.GpcBridgeGeometryBuilders.RigidNode(line.To, $"Rigid{i}B", null, R2aGeneratorFixtures.Id(tag + " B")));

    private static (int From, int To)[] Links(BridGHe.Common.Gpc.GpcModels.Conversion input) =>
        input.Model.Costrains.Values.OfType<GPC.Model.Constraints.RigidLink>().SelectMany(l => l.EndNodes.Select(e => (l.StartNode.Id, e.Id))).ToArray();

    private static void Gpc()
    {
        var line = new Line(Point3d.Origin, new Point3d(0, 0, 1500));
        var (a, b) = Rigid(line, 1, "connection");
        var links = Links(R2aGeneratorFixtures.Build("connections", new[] { a, b }, null, 1e-6));
        Require(links.Length == 1 && links[0] == (1, 2), "Rigid connectivity after merge");
        Require(Links(R2aGeneratorFixtures.Build("connections", new[] { R2aGeneratorFixtures.Clone(a), R2aGeneratorFixtures.Clone(b) }, null, 1e-6)).SequenceEqual(links), "Rigid connectivity archives");
        var property = R2aDesignFixtures.RectangleProperty();
        var beam = BridGHe.Common.Gpc.Builders.GpcBridgeGeometryBuilders.ConnectionBeam(line.From, line.To, property, "Connection1", R2aGeneratorFixtures.Id("connection beam"));
        Require(beam.Beam.Name == "Connection1" && R2aGeneratorFixtures.Distance(beam.Beam.StartPoint, beam.Beam.EndPoint) == 1500 && beam.Records.Groups.SequenceEqual(new[] { "DeformableConnections" }),
            "Explicit deformable beam");
        var shifted = BridGHe.Common.Gpc.Builders.GpcBeamBuilders.Override(beam, null, null, null, (12, -20), R2aGeneratorFixtures.Id("connection offset"));
        Require(shifted.Beam.Assignments.OffsetI is { X: 12, Y: -20, Z: 0 } && beam.Beam.Assignments.OffsetI == null, "Offsets isolate input and use local x/y");
        Reject(() => BridgeConnections.Create(new[] { line, new Line(line.To, line.From) }, true));
        Reject(() => BridgeConnections.Create(new[] { new Line(Point3d.Origin, Point3d.Origin) }, true));
        Reject(() => BridgeConnections.Create(new[] { line }, false, null, beamOrTrussOnGpc: false));
        var collapsed = BridGHe.Common.Gpc.Builders.GpcBridgeGeometryBuilders.RigidNode(Point3d.Origin, "A", Point3d.Origin, R2aGeneratorFixtures.Id("collapsed"));
        Reject(() => R2aGeneratorFixtures.Build("collapsed", new[] { collapsed }, null, 1e-6));
        Console.WriteLine("BRIDGE CONNECTIONS PASS: merged connectivity, archives, real deformable members, isolated offsets and duplicate/collapsed rejection.");
    }
    /// <summary>F6A: the spatial rigid link written by the GPC Converter (export component path) and read back with the Converter API.
    /// R6.6 R2a (GPC_PILOT): the rigid pair by its GPC drafts, Build Model on GPC and Export of the GPC-only input (no FEM model).</summary>
    internal static void Native()
    {
        string folder = Path.GetFullPath(Path.Combine("artifacts", "connections-native", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "connection.st7");
        var (a, b) = Rigid(new Line(Point3d.Origin, new Point3d(10, 20, 30)), 1, "connection native");
        var input = R2aGeneratorFixtures.Build("connection native", new[] { a, b }, null, 1e-6);
        BridGHe.Common.Gpc.Legacy.GpcAnalysisSeam.Export(input, file, new System.Collections.Generic.List<string>());
        using var api = new GPC.Converter.Straus7.Straus7NativeApi(); api.OpenModelReadOnly(file, folder);
        Require(api.Count(GPC.Converter.Straus7.Straus7Entity.Link) == 1, "One native link");
        var link = api.ReadLink(1);
        Require(link.Type == 5 && link.Ucs == 1 && link.Plane == 0 && link.Nodes.SequenceEqual(new[] { 1, 2 }), "Vendor rigid link roundtrip");
        api.CloseModel();
        Console.WriteLine("BRIDGE CONNECTIONS NATIVE PASS: spatial rigid link written by the GPC Converter and read back; no solver run.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE CONNECTIONS: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid connection accepted"); }
}
