using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class BridgeDeckGrillageTests
{
    private static void Require(bool ok, string label) { if (!ok) throw new Exception("DECK GRILLAGE: " + label); }
    internal static void Run()
    {
        foreach (bool truss in new[] { false, true }) foreach (bool curved in new[] { false, true })
        {
            var input = SimpleBridgeTests.Input(truss, curved) with
            {
                DeckBeamStations = new[] { 1234d, 13777, 45001, 86789 }, DeckBeamLevel = -100,
                CrossfallStations = new[] { 0d, 100000 }, CrossfallPercent = new[] { -3d, 5 },
                SupportSkewDegrees = new[] { 0d, 12, -8, 0 }
            };
            var steel = SimpleBridgeBuilder.Build(input); int count = steel.Members.Count;
            var result = BridgeDeckGrillage.Build(input, steel);
            Require(result.Stations.Length == 6 && result.Strips.Length == 24 && result.Connections.Length == 18, "Explicit stations plus ends, three girders and cantilevers");
            Require(steel.Members.Count == count && result.Geometry.Members.Count == count + 24, "Steel output preserved, separate combined geometry");
            foreach (var link in result.Connections)
            {
                int i = steel.Stations.BinarySearch(result.Stations[link.Station - 1]); var frame = steel.Frames[i];
                var endpoints = steel.Members.Where(m => m.Family == BridgeMemberFamily.Girder && m.Group == link.Girder)
                    .SelectMany(m => new[] { m.Axis.From, m.Axis.To });
                Require(endpoints.Contains(link.Axis.From), "Link starts at an exact split girder node");
                Require(result.Strips.Any(s => s.Member.Axis.From == link.Axis.To || s.Member.Axis.To == link.Axis.To), "Link ends at an exact split deck node");
                Require(Math.Abs((link.Axis.To - frame.Origin) * frame.YAxis + 100) < 1e-7, "Requested normal elevation");
                Require(Math.Abs(link.Axis.Direction * frame.ZAxis) < 1e-7 && Math.Abs(link.Axis.Direction * frame.XAxis) < 1e-7, "Links follow deck normal");
            }
            foreach (var strip in result.Strips)
            {
                var frame = steel.Frames[steel.Stations.BinarySearch(strip.Member.StationFrom)];
                Require(Math.Abs(strip.Member.Axis.Direction * frame.ZAxis) < 1e-7, "Slab axes orthogonal to alignment, independent of support skew");
            }
            Require(Math.Abs(result.Strips.GroupBy(s => s.Member.Group).Sum(g => g.First().Width) - 100000) < 1e-8, "Tributary widths cover total bridge length exactly");
            Require(result.Geometry.Members.Select(m => m.Id).Distinct().Count() == count + 24, "Unique member IDs");
            var clone = ModelArchive.Clone(result.Geometry);
            Require(clone.Members.SequenceEqual(result.Geometry.Members), "Combined geometry archive roundtrip");
            var defaults = new Dictionary<int, BeamPropertyModel> { [-1] = BridgeAssignmentTests.Property() };
            var missing = BridgeMemberAssignment.Assign(result.Geometry, defaults, Array.Empty<BridgeSectionRule>());
            Require(missing.Unassigned.Length == 24, "Default steel cannot silently become concrete slab");
            defaults[8] = BridgeAssignmentTests.Property("Concrete strips");
            var assigned = BridgeMemberAssignment.Assign(result.Geometry, defaults, Array.Empty<BridgeSectionRule>());
            Require(assigned.Unassigned.Length == 0 && assigned.Beams.Length == count + 24, "Explicit family 8 assignment");
        }
        var auto = SimpleBridgeTests.Input() with { DeckBeamSpacing = 3000 };
        var autoSteel = SimpleBridgeBuilder.Build(auto); var autoResult = BridgeDeckGrillage.Build(auto, autoSteel);
        Require(autoResult.Stations.Length == 35 && autoResult.Stations.Zip(autoResult.Stations.Skip(1), (a,b) => b-a).All(d => d <= 3000), "Maximum spacing inserts all required nodes");
        Require(Math.Abs(autoResult.Connections[0].Axis.To.Z + 125) < 1e-9, "Default slab centroid elevation");
        var gradedInput = SimpleBridgeTests.Input() with { Alignment = new LineCurve(Point3d.Origin,
            new Point3d(Math.Sqrt(100000d * 100000 - 5000d * 5000),0,5000)), CrossfallPercent = new[] { 4d }, DeckBeamSpacing = 2000 };
        var gradedSteel = SimpleBridgeBuilder.Build(gradedInput); var graded = BridgeDeckGrillage.Build(gradedInput, gradedSteel);
        var tangent = gradedInput.Alignment.TangentAtStart;
        Require(graded.Strips.All(s => Math.Abs(s.Member.Axis.Direction * tangent) < 1e-7), "3D graded alignment keeps slab orthogonal to tangent");
        var edgeInput = SimpleBridgeTests.Input() with { DeckWidths = new[] { 4000d } };
        var edgeSteel = SimpleBridgeBuilder.Build(edgeInput); var edge = BridgeDeckGrillage.Build(edgeInput, edgeSteel);
        Require(edge.Strips.Length == edge.Stations.Length * 2 && edge.Strips.All(s => s.Member.Axis.Length > 0), "No zero cantilever at edge girders");
        foreach (var invalid in new[] { auto with { DeckBeamSpacing = -1 }, auto with { DeckBeamSpacing = double.NaN },
            auto with { DeckBeamLevel = -251 }, auto with { DeckBeamLevel = 1 }, auto with { DeckBeamStations = new[] { 4d, 2 } },
            auto with { DeckBeamSpacing = 1e-8 } })
        {
            bool rejected = false; try { SimpleBridgeBuilder.Build(invalid); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Invalid station/elevation/spacing rejected");
        }
        Console.WriteLine("DECK GRILLAGE PASS: solid/truss, straight/curved, exact node matching, crossfall, skew, assignment, widths and archives.");
    }
}
