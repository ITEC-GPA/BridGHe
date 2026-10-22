using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

[Serializable]
public sealed record BridgeSegments
{
    public double MaximumLength { get; init; } = 12000;
    // Key 0: common joints; keys 1..N: additional joints of a specific girder.
    public Dictionary<int, double[]> Joints { get; init; } = new();
    public override string ToString() => $"Bridge segments: {Joints.Count} joint branches, maximum length {MaximumLength:G6} mm";
}

public enum BridgeTrussPattern { X, V, Warren }

[Serializable]
public sealed record BridgeDiaphragms
{
    public double MaximumSpacing { get; init; } = 5000;
    public double[] Stations { get; init; } = Array.Empty<double>();
    public bool AtSupports { get; init; } = true;
    public double TrussHeight { get; init; } = 1500;
    public int PanelsPerBay { get; init; } = 2;
    public BridgeTrussPattern Pattern { get; init; } = BridgeTrussPattern.X;
    public bool ConnectCrossings { get; init; }
    public override string ToString() => $"Bridge diaphragms: spacing {MaximumSpacing:G6} mm, {Pattern}, {PanelsPerBay} panels/bay";
}

public sealed record SimpleBridgeInput
{
    public Curve Alignment { get; init; }
    public double[] Supports { get; init; } = Array.Empty<double>();
    public double[] GirderOffsets { get; init; } = Array.Empty<double>();
    public double[] GirderLevels { get; init; } = new[] { -1500d };
    public double[] DeckWidths { get; init; } = new[] { 6000d, 6000d };
    public double DeckThickness { get; init; } = 250;
    public double[] CrossfallStations { get; init; } = Array.Empty<double>();
    public double[] CrossfallPercent { get; init; } = new[] { 0d };
    public double[] SupportSkewDegrees { get; init; } = new[] { 0d };
    public BridgeSegments Segments { get; init; } = new();
    public BridgeDiaphragms Diaphragms { get; init; } = new();
    public double MaximumElementLength { get; init; } = 2500;
    public double ChordTolerance { get; init; } = 5;
    public double DeckBeamSpacing { get; init; }
    public double[] DeckBeamStations { get; init; } = Array.Empty<double>();
    public double? DeckBeamLevel { get; init; }
    public bool Truss { get; init; }
}

public enum BridgeMemberFamily { Girder, SolidDiaphragm, TopChord, BottomChord, Post, Diagonal, LongitudinalBracing, BracingPost, DeckTransverse }

[Serializable]
public sealed record BridgeGeometryMember(string Id, BridgeMemberFamily Family, int Group, int Segment,
    int Span, int NodeFrom, int NodeTo, Line Axis, double StationFrom, double StationTo)
{
    public string PhysicalId => $"{Family}:{Group}:{Segment}";
}

[Serializable]
public sealed record BridgeSegmentRange(int Girder, int Segment, double Start, double End);
[Serializable]
public sealed record BridgeSupportPoint(int Support, int Girder, double Station, Point3d Point);
[Serializable]
public sealed record BridgeJointPoint(int Girder, int Joint, double Station, Point3d Point);

/// <summary>Geometric axes only. No FE properties, constraints, releases, links or loads.</summary>
[Serializable]
public sealed class SimpleBridgeGeometry
{
    public List<BridgeGeometryMember> Members { get; } = new();
    public List<Point3d> Nodes { get; } = new();
    public List<BridgeSupportPoint> Supports { get; } = new();
    public List<BridgeJointPoint> Joints { get; } = new();
    public List<BridgeSegmentRange> Segments { get; } = new();
    public List<Plane> Frames { get; } = new();
    public List<double> Stations { get; } = new();
    public List<string> Diagnostics { get; } = new();
    public Mesh Deck { get; internal set; }
    public double DeckThickness { get; internal set; }
    public Polyline LeftEdge { get; internal set; }
    public Polyline RightEdge { get; internal set; }
}
