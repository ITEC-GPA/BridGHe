using System;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class BridgeMeshTests
{
    internal static PlatePropertyModel DeckProperty() => new("Slab")
    {
        PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 250, BendingThickness = 250,
        Material = new MaterialModel("C") { Modulus = 34000, PoissonRatio = .2 }
    };
    internal static void Bracing()
    {
        var source = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        int count = source.Members.Count;
        var stations = new[] { 0d, 12345, 30000 };
        var unconnected = BridgeLongitudinalBracing.Add(source, stations, null, BridgeTrussPattern.X, 0, false);
        Require(unconnected.Members.Count(m => m.Family == BridgeMemberFamily.LongitudinalBracing) == 8, "X diagonal count");
        var connected = BridgeLongitudinalBracing.Add(source, stations, null, BridgeTrussPattern.X, 0, true);
        Require(connected.Members.Count(m => m.Family == BridgeMemberFamily.LongitudinalBracing) == 16, "Connected X splits four diagonals per bay/panel");
        var offset = BridgeLongitudinalBracing.Add(source, stations, new[] { 1 }, BridgeTrussPattern.Warren, -500, false);
        Require(offset.Members.Count(m => m.Family == BridgeMemberFamily.BracingPost) == 6, "Shared offset posts are not duplicated");
        var both = BridgeLongitudinalBracing.Add(offset, stations, new[] { 1 }, BridgeTrussPattern.Warren, 500, false);
        Require(both.Members.Select(m => m.Id).Distinct().Count() == both.Members.Count, "Chained upper/lower bracing IDs unique");
        var v = BridgeLongitudinalBracing.Add(source, stations, new[] { 1 }, BridgeTrussPattern.V, 0, false);
        foreach (var g in new[] { unconnected, connected, offset, both, v })
        {
            Require(g.Members.All(m => m.Axis.From == g.Nodes[m.NodeFrom - 1] && m.Axis.To == g.Nodes[m.NodeTo - 1]), "Exact endpoint connectivity");
            Require(g.Members.Select(m => (Math.Min(m.NodeFrom, m.NodeTo), Math.Max(m.NodeFrom, m.NodeTo))).Distinct().Count() == g.Members.Count, "No duplicate undirected members");
        }
        Require(source.Members.Count == count, "Source geometry unchanged");
        var omitted = BridgeLongitudinalBracing.Add(source, stations, null, BridgeTrussPattern.X, 0, false, new[] { 2 });
        Require(omitted.Members.Count(m => m.Family == BridgeMemberFamily.LongitudinalBracing) == 4, "Panel exclusion");
        Reject(() => BridgeLongitudinalBracing.Add(source, new[] { 30000d, 10000 }, null, BridgeTrussPattern.X, 0, false));
        Console.WriteLine("BRIDGE BRACING PASS: X/V/Warren, intersections, shared posts, upper/lower chaining, exclusions and exact topology.");
    }
    internal static void Deck()
    {
        var source = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        int sourceCount = source.Members.Count;
        var result = BridgeDeckMesh.Create(source, DeckProperty(), 2500, 1500, new[] { 17321d, 60000 }, true);
        using var area = AreaMassProperties.Compute(result.Mesh);
        Require(Math.Abs(area.Area - 1.2e9) < 1, "Analytical rectangular deck area");
        Require(result.Mesh.IsValid && !result.Mesh.IsClosed, "Open valid FEM mid-surface");
        Require(result.Mesh.Vertices.ToPoint3dArray().All(p => Math.Abs(p.Z + 125) < 1e-8), "Slab mid-surface at -125 mm");
        Require(result.Stations.Contains(17321) && result.Plates.SelectMany(p => p.Groups).Select(g => g.ToString()).Distinct().Count() == 3, "Pour boundary splits mesh and groups");
        Require(result.Plates.Length == result.Mesh.Faces.Count && result.Mesh.Faces.All(f => f.IsTriangle), "Actual triangular plate topology");
        Require(result.Connections.Length == result.Stations.Length * 3, "One beam/deck connector per girder and row");
        foreach (var c in result.Connections)
        {
            Require(result.Geometry.Members.Any(m => m.Axis.From.DistanceTo(c.From) < 1e-5 || m.Axis.To.DistanceTo(c.From) < 1e-5), "Connector starts at actual FEM beam node");
            Require(result.Mesh.Vertices.ToPoint3dArray().Any(p => p.DistanceTo(c.To) < 1e-5), "Connector ends at actual FEM plate node");
        }
        Require(source.Members.Count == sourceCount, "Deck remeshing does not mutate source");
        var curved = BridgeDeckMesh.Create(SimpleBridgeBuilder.Build(SimpleBridgeTests.Input(curved: true)), DeckProperty(), 5000, 2500, null, true);
        Require(curved.Mesh.IsValid && curved.Plates.Length > 0, "Curved deck mesh");
        var badProperty = DeckProperty(); badProperty.MembraneThickness = 200;
        Reject(() => BridgeDeckMesh.Create(source, badProperty, 2500, 1500, null, true));
        Reject(() => BridgeDeckMesh.Create(source, DeckProperty(), 0, 1500, null, true));
        Console.WriteLine("BRIDGE DECK PASS: analytical area, midsurface, pour groups, curved mesh, plate connectivity and input isolation.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE MESH: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid mesh input accepted"); }
}
