using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using Rhino.Geometry;

internal static class SimpleBridgeTests
{
    private static int checks;
    internal static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException("Simple Bridge: " + message); checks++; }
    private static void Near(double value, double expected, double tolerance, string name) =>
        Require(Math.Abs(value - expected) <= tolerance, $"{name}: {value:G17} vs {expected:G17}");
    internal static SimpleBridgeInput Input(bool truss = false, bool curved = false) => new()
    {
        Alignment = curved ? new ArcCurve(new Arc(Plane.WorldXY, 250000, .4)) : new LineCurve(Point3d.Origin, new Point3d(100000, 0, 0)),
        Supports = new[] { 0d, 30000, 70000, 100000 }, GirderOffsets = new[] { -4000d, 0, 4000 },
        Segments = new BridgeSegments { MaximumLength = 0 }, Truss = truss
    };

    private static void Reject(SimpleBridgeInput input, string contains)
    {
        try { SimpleBridgeBuilder.Build(input); }
        catch (ArgumentException e) when (e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase)) { checks++; return; }
        throw new InvalidOperationException("Expected validation: " + contains);
    }

    private static void Topology(SimpleBridgeGeometry result)
    {
        Require(result.Members.Select(m => m.Id).Distinct().Count() == result.Members.Count, "Unique member IDs");
        Require(result.Members.Select(m => (Math.Min(m.NodeFrom, m.NodeTo), Math.Max(m.NodeFrom, m.NodeTo))).Distinct().Count() == result.Members.Count, "No duplicate undirected members");
        Require(result.Members.All(m => m.Axis.From == result.Nodes[m.NodeFrom - 1] && m.Axis.To == result.Nodes[m.NodeTo - 1]), "Axes share exact topology endpoints");
        var adjacency = Enumerable.Range(1, result.Nodes.Count).ToDictionary(n => n, _ => new List<int>());
        foreach (var m in result.Members) { adjacency[m.NodeFrom].Add(m.NodeTo); adjacency[m.NodeTo].Add(m.NodeFrom); }
        var reached = new HashSet<int> { 1 }; var queue = new Queue<int>(); queue.Enqueue(1);
        while (queue.Count > 0)
            foreach (int n in adjacency[queue.Dequeue()]) if (reached.Add(n)) queue.Enqueue(n);
        Require(reached.Count == result.Nodes.Count, "Whole geometric member graph connected");
        Require(result.Supports.All(s => result.Members.Where(m => m.Family == BridgeMemberFamily.Girder && m.Group == s.Girder)
            .Any(m => m.Axis.From.DistanceTo(s.Point) <= 1e-5 || m.Axis.To.DistanceTo(s.Point) <= 1e-5)), "Each support splits its girder");
    }

    internal static void Run()
    {
        checks = 0;
        var input = Input(); var simple = SimpleBridgeBuilder.Build(input);
        Require(simple.Stations.Count == 41 && simple.Nodes.Count == 123, "Straight 100 m grid, 2.5 m segments, three girders");
        Require(simple.Members.Count(m => m.Family == BridgeMemberFamily.Girder) == 120, "120 longitudinal pieces");
        Require(simple.Members.Count(m => m.Family == BridgeMemberFamily.SolidDiaphragm) == 42, "21 diaphragm lines, two bays");
        Require(simple.Supports.Count == 12 && simple.Segments.Count == 3, "Four support lines; one fabrication segment per girder");
        Require(simple.Deck.IsValid && simple.Deck.IsClosed, "Closed slab mesh");
        using (var volume = VolumeMassProperties.Compute(simple.Deck)) Near(Math.Abs(volume.Volume), 100000d * 12000 * 250, 1, "Deck analytical volume mm3");
        Topology(simple);
        Near(simple.LeftEdge[0].Y, 6000, 1e-9, "Local left convention");
        Near(simple.RightEdge[0].Y, -6000, 1e-9, "Local right convention");
        Require(input.Alignment.IsValid && input.Alignment.PointAtStart == Point3d.Origin, "Source alignment untouched");
        var repeat = SimpleBridgeBuilder.Build(input);
        Require(simple.Members.Select(m => m.Id).SequenceEqual(repeat.Members.Select(m => m.Id)) &&
            simple.Nodes.SequenceEqual(repeat.Nodes), "Deterministic IDs and geometry");

        var segmented = SimpleBridgeBuilder.Build(input with { Segments = new BridgeSegments
        {
            Joints = new() { [0] = new[] { 24000d, 36000, 60000, 84000 }, [2] = new[] { 18000d } }
        } });
        Require(segmented.Segments.Count == 16 && segmented.Joints.Count == 13, "Common plus staggered girder joints");
        Require(segmented.Segments.Any(s => s.Girder == 1 && s.Start == 24000 && s.End == 36000), "Fabrication segment crosses the 30 m pier");
        var acrossPier = segmented.Members.Where(m => m.Family == BridgeMemberFamily.Girder && m.Group == 1 && m.Segment == 2).ToArray();
        Require(acrossPier.Select(m => m.Span).Distinct().Count() == 2, "One fabrication segment spans two structural spans");
        Topology(segmented);
        var automatic = SimpleBridgeBuilder.Build(input with { Segments = new BridgeSegments { MaximumLength = 12000 } });
        Require(automatic.Segments.Count == 27 && automatic.Segments.All(s => s.End - s.Start <= 12000), "Automatic maximum segment length independent of spans");

        var skewed = SimpleBridgeBuilder.Build(input with { SupportSkewDegrees = new[] { 0d, 20, -15, 0 } });
        foreach (var s in skewed.Supports)
        {
            double angle = new[] { 0d, 20, -15, 0 }[s.Support - 1] * Math.PI / 180;
            Near(s.Station, input.Supports[s.Support - 1] + input.GirderOffsets[s.Girder - 1] * Math.Tan(angle), 2e-6, "Analytical skew intersection");
        }
        Topology(skewed);

        var curved = SimpleBridgeBuilder.Build(Input(curved: true));
        foreach (var m in curved.Members.Where(m => m.Family == BridgeMemberFamily.Girder))
        {
            double radius = 250000 - input.GirderOffsets[m.Group - 1];
            Near(Math.Sqrt(m.Axis.From.X * m.Axis.From.X + m.Axis.From.Y * m.Axis.From.Y), radius, 1e-6, "Concentric circular girder");
            Require(m.Axis.Length <= input.MaximumElementLength + 1e-5, "Offset axis length limit on curve");
            double middle = (m.StationFrom + m.StationTo) / 2 / 250000;
            var exact = new Point3d(radius * Math.Cos(middle), radius * Math.Sin(middle), -1500);
            Require(exact.DistanceTo(m.Axis.PointAt(.5)) <= input.ChordTolerance, "Circular chord error");
        }
        Topology(curved);
        var banked = SimpleBridgeBuilder.Build(input with { CrossfallStations = new[] { 0d, 50000, 100000 },
            CrossfallPercent = new[] { -2d, 4, 1 }, GirderLevels = new[] { -1500d, -2000, -1500 } });
        for (int i = 0; i < banked.Stations.Count; i++)
        {
            double s = banked.Stations[i];
            double expected = s <= 50000 ? -2 + 6 * s / 50000 : 4 - 3 * (s - 50000) / 50000;
            Near(banked.Frames[i].XAxis.Z / banked.Frames[i].XAxis.Y * 100, expected, 1e-9, "Interpolated crossfall percent");
        }
        var rampCurve = new LineCurve(Point3d.Origin, new Point3d(100000, 0, 3000));
        var ramp = SimpleBridgeBuilder.Build(input with { Alignment = rampCurve, Supports = new[] { 0, rampCurve.GetLength() / 2, rampCurve.GetLength() } });
        Near(ramp.Frames[0].ZAxis.Z, 3000 / rampCurve.GetLength(), 1e-12, "3D tangent and arc-length stations");
        Topology(ramp);

        var compact = Input(truss: true) with { Diaphragms = new BridgeDiaphragms { MaximumSpacing = 0, Stations = new[] { 50000d }, AtSupports = false } };
        foreach (var pattern in Enum.GetValues<BridgeTrussPattern>())
        {
            var result = SimpleBridgeBuilder.Build(compact with { Diaphragms = compact.Diaphragms with { Pattern = pattern } });
            Topology(result);
            Require(result.Members.Count(m => m.Family == BridgeMemberFamily.Diagonal) == (pattern == BridgeTrussPattern.Warren ? 4 : 8), "Pattern diagonal count " + pattern);
            Require(result.Members.Count(m => m.Family == BridgeMemberFamily.Post) == (pattern == BridgeTrussPattern.Warren ? 6 : 8), "Pattern post count " + pattern);
        }
        var unjoined = SimpleBridgeBuilder.Build(compact);
        var joined = SimpleBridgeBuilder.Build(compact with { Diaphragms = compact.Diaphragms with { ConnectCrossings = true } });
        var diagonal = unjoined.Members.First(m => m.Family == BridgeMemberFamily.Diagonal);
        var crossing = diagonal.Axis.PointAt(.5);
        Require(unjoined.Nodes.All(n => n.DistanceTo(crossing) > 1e-5), "X crossing has no accidental node");
        Require(joined.Nodes.Any(n => n.DistanceTo(crossing) < 1e-5) && joined.Nodes.Count == unjoined.Nodes.Count + 4, "Connected X adds exactly four bay-panel centre nodes");
        Topology(joined);
        Topology(SimpleBridgeBuilder.Build(Input(true, true)));
        var overhang = SimpleBridgeBuilder.Build(input with { Supports = new[] { 5000d, 30000, 70000, 95000 }, SupportSkewDegrees = new[] { 15d } });
        Require(overhang.Members.Any(m => m.Family == BridgeMemberFamily.Girder && m.Span == 0) &&
            overhang.Members.Any(m => m.Family == BridgeMemberFamily.Girder && m.Span == 4), "Skew ends and overhangs retained");
        Topology(overhang);
        var noSupportDiaphragms = SimpleBridgeBuilder.Build(input with { Diaphragms = new() { AtSupports = false } });
        Require(noSupportDiaphragms.Members.Where(m => m.Family == BridgeMemberFamily.SolidDiaphragm)
            .All(m => !input.Supports.Contains(m.StationFrom)), "At Supports=false excludes automatic support diaphragms");
        var explicitSupport = SimpleBridgeBuilder.Build(input with { Diaphragms = new() { AtSupports = false, Stations = new[] { 30000d } } });
        Require(explicitSupport.Members.Count(m => m.Family == BridgeMemberFamily.SolidDiaphragm) == 2, "Explicit stations override support omission");
        Reject(input with { GirderOffsets = new[] { 0d, 0 } }, "Offsets");
        Reject(input with { Supports = new[] { 30000d, 0, 100000 } }, "Support stations");
        Reject(input with { MaximumElementLength = double.NaN }, "Maximum element");
        Reject(input with { Alignment = new LineCurve(Point3d.Origin, new Point3d(0, 0, 100000)) }, "vertical");
        Reject(input with { Alignment = new PolylineCurve(new[] { Point3d.Origin, new Point3d(50000, 0, 0), new Point3d(50000, 50000, 0) }) }, "continuous tangent");
        Reject(input with { SupportSkewDegrees = new[] { 20d } }, "intersect");
        Reject(input with { Segments = new() { Joints = new() { [4] = new[] { 12000d } } } }, "branch");
        Reject(input with { CrossfallStations = new[] { 1000d, 100000 }, CrossfallPercent = new[] { 0d, 2 } }, "covering");
        Reject(input with { Truss = true, Diaphragms = new() { TrussHeight = 3000 } }, "slab");
        Reject(input with { ChordTolerance = -1 }, "Chord tolerance");
        Console.WriteLine($"SIMPLE BRIDGE GEOMETRY PASS: {checks} assertions; straight/curved/graded/skew layouts, joints, topology, truss patterns and invalid inputs");
    }
}
