using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public interface IBridgeSegments
{
    IReadOnlyList<BridgeSegmentRange> FabricationSegments { get; }
}

[Serializable] public sealed record BridgeFamilyProperty(int Family, BridGHe.Common.Models.BeamPropertyModel Property);

[Serializable] public sealed record BridgeSupportLayout(double[] Stations, double[] Skew);
[Serializable] public sealed record BridgeGirderLayout(double[] Offsets, double[] Levels);
[Serializable] public sealed record BridgeDeckLayout(double[] Widths, double Thickness, double[] SlopeStations, double[] Slopes, double? BeamLevel);
[Serializable] public sealed record BridgeMeshSettings(double MaximumLength, double ChordTolerance, double SlabSpacing, double[] SlabStations);
[Serializable] public sealed record BridgeDiaphragmLayout(BridgeDiaphragms Settings, bool Truss);
[Serializable] public sealed record BridgeJointSet(double[] Stations, int[] Girders);

/// <summary>Fabrication identity before meshing, section assignment or FEM analysis. All stations in mm.</summary>
[Serializable]
public sealed record BridgeFabricationPlan(double Length, int GirderCount, BridgeSegments Settings, BridgeSegmentRange[] Ranges) : IBridgeSegments
{
    public IReadOnlyList<BridgeSegmentRange> FabricationSegments => Ranges;
    public static BridgeFabricationPlan Create(double length, int count, BridgeSegments settings)
    {
        const double tol = SimpleBridgeBuilder.JoinTolerance;
        if (!double.IsFinite(length) || length <= tol || count < 2 || count > 32 || settings == null ||
            !double.IsFinite(settings.MaximumLength) || settings.MaximumLength < 0 || settings.Joints == null)
            throw new ArgumentException("Fabrication: valid length, 2..32 girders and non-negative maximum segment length required.");
        foreach (var pair in settings.Joints)
            if (pair.Key < 0 || pair.Key > count || pair.Value == null || pair.Value.Length > 20000 ||
                pair.Value.Any(s => !double.IsFinite(s) || s < 0 || s > length + tol))
                throw new ArgumentException("Fabrication joints: stations must lie on the alignment; girder IDs are 1..N, or 0 for all.");
        var ranges = new List<BridgeSegmentRange>();
        for (int g = 1; g <= count; g++)
        {
            var explicitJoints = settings.Joints.GetValueOrDefault(0, Array.Empty<double>())
                .Concat(settings.Joints.GetValueOrDefault(g, Array.Empty<double>())).ToArray();
            int pieces = settings.MaximumLength == 0 || explicitJoints.Length > 0 ? 1 :
                (int)Math.Min(20001d, Math.Ceiling(length / settings.MaximumLength));
            if (pieces > 20000) throw new ArgumentException("Too many fabrication segments; increase Maximum Length.");
            var values = explicitJoints.Length > 0 ? explicitJoints : Enumerable.Range(0, pieces + 1).Select(i => length * i / pieces);
            var stations = new List<double>();
            foreach (double s in values.Append(0).Append(length).OrderBy(s => s))
            {
                double v = Math.Clamp(s, 0, length);
                if (stations.Count == 0 || v - stations[^1] > tol) stations.Add(v);
            }
            if (stations.Count > 20000) throw new ArgumentException("Too many fabrication joint stations.");
            for (int i = 0; i + 1 < stations.Count; i++) ranges.Add(new(g, i + 1, stations[i], stations[i + 1]));
        }
        return new(length, count, settings, ranges.ToArray());
    }

    public static BridgeSegments Joints(IEnumerable<BridgeJointSet> sets, double maximumLength)
    {
        var joints = new Dictionary<int, List<double>>();
        foreach (var set in sets)
        {
            if (set == null || set.Girders == null || set.Stations == null) throw new ArgumentException("Invalid joint set.");
            foreach (int g in set.Girders.Length == 0 ? new[] { 0 } : set.Girders.Distinct())
            {
                if (g < 0) throw new ArgumentException("Girder IDs must be positive, or 0 for all girders.");
                if (!joints.ContainsKey(g)) joints[g] = new();
                joints[g].AddRange(set.Stations);
            }
        }
        return new() { MaximumLength = maximumLength, Joints = joints.ToDictionary(p => p.Key, p => p.Value.Distinct().OrderBy(v => v).ToArray()) };
    }
}

[Serializable]
public sealed record BridgeDefinition(Curve Alignment, BridgeSupportLayout Supports, BridgeGirderLayout Girders,
    BridgeDeckLayout Deck, BridgeFabricationPlan Fabrication, BridgeDiaphragmLayout Diaphragms)
{
    public SimpleBridgeInput ToInput(BridgeMeshSettings mesh)
    {
        if (Alignment == null || Supports == null || Girders == null || Deck == null || Fabrication == null || Diaphragms == null || mesh == null)
            throw new ArgumentException("Connect alignment, supports, girders, deck, fabrication plan, diaphragms and mesh settings.");
        if (Math.Abs(Alignment.GetLength() - Fabrication.Length) > SimpleBridgeBuilder.JoinTolerance || Girders.Offsets.Length != Fabrication.GirderCount)
            throw new ArgumentException("Fabrication plan belongs to a different alignment length or girder count. Reconnect the current alignment and girders.");
        return new SimpleBridgeInput
        {
            Alignment = Alignment, Supports = Supports.Stations, SupportSkewDegrees = Supports.Skew,
            GirderOffsets = Girders.Offsets, GirderLevels = Girders.Levels, DeckWidths = Deck.Widths,
            DeckThickness = Deck.Thickness, CrossfallStations = Deck.SlopeStations, CrossfallPercent = Deck.Slopes,
            DeckBeamLevel = Deck.BeamLevel, Segments = Fabrication.Settings, Diaphragms = Diaphragms.Settings,
            Truss = Diaphragms.Truss, MaximumElementLength = mesh.MaximumLength, ChordTolerance = mesh.ChordTolerance,
            DeckBeamSpacing = mesh.SlabSpacing, DeckBeamStations = mesh.SlabStations
        };
    }
}
