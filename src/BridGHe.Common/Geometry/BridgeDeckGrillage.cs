using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public sealed record BridgeDeckStrip(BridgeGeometryMember Member, double Width);
public sealed record BridgeDeckConnection(int Station, int Girder, Line Axis);
public sealed record BridgeDeckGrillageResult(SimpleBridgeGeometry Geometry, double[] Stations,
    BridgeDeckStrip[] Strips, BridgeDeckConnection[] Connections);

/// <summary>Transverse slab strips and explicit eccentric connection axes, in mm.
/// Connections are not FEM beams: pass them to Bridge Connections to choose their behaviour.</summary>
public static class BridgeDeckGrillage
{
    internal static double[] RequestedStations(SimpleBridgeInput input, double length)
    {
        if (input.DeckBeamStations.Length > 0)
            return input.DeckBeamStations.Concat(new[] { 0d, length }).OrderBy(s => s).ToArray();
        if (input.DeckBeamSpacing == 0) return Array.Empty<double>();
        // The canonical longitudinal grid can snap its end by JoinTolerance.
        // Near an exact multiple, raw Ceiling could then select a different number
        // of strips in the steel and slab passes (e.g. 30 versus 31 on a circular arc).
        double count = Math.Max(1, Math.Ceiling((length - SimpleBridgeBuilder.JoinTolerance) / input.DeckBeamSpacing));
        if (count > 19999) throw new ArgumentException("Too many deck beam stations; increase spacing.");
        return Enumerable.Range(0, (int)count + 1).Select(i => length * i / count).ToArray();
    }

    public static BridgeDeckGrillageResult Build(SimpleBridgeInput input, SimpleBridgeGeometry steel)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(steel);
        BridgeGeometryTools.Validate(steel);
        double length = steel.Stations[^1], level = input.DeckBeamLevel ?? -input.DeckThickness / 2;
        var requested = RequestedStations(input, length);
        // The builder already inserted these stations before chord refinement. Use its
        // canonical values to retain exact common endpoints, including almost-coincident stations.
        var indices = requested.Length == 0 ? Enumerable.Range(0, steel.Stations.Count).ToArray() :
            requested.Select(s => FindStation(steel.Stations, s)).Distinct().OrderBy(i => i).ToArray();
        var stations = indices.Select(i => steel.Stations[i]).ToArray();
        double left = input.DeckWidths[0], right = input.DeckWidths.Length == 1 ? left : input.DeckWidths[1];
        var offsets = new[] { -right }.Concat(input.GirderOffsets).Concat(new[] { left }).ToArray();
        using var editor = new BridgeGeometryEditor(steel);
        var result = editor.Geometry;
        var strips = new List<BridgeDeckStrip>(); var links = new List<BridgeDeckConnection>();
        for (int i = 0; i < indices.Length; i++)
        {
            double s = stations[i]; var frame = steel.Frames[indices[i]];
            double tributaryStart = i == 0 ? s : (stations[i - 1] + s) / 2;
            double tributaryEnd = i == stations.Length - 1 ? s : (s + stations[i + 1]) / 2;
            double width = tributaryEnd - tributaryStart;
            for (int j = 0; j + 1 < offsets.Length; j++)
            {
                if (offsets[j + 1] - offsets[j] <= SimpleBridgeBuilder.JoinTolerance) continue;
                int a = editor.Node(frame.PointAt(offsets[j], level)), b = editor.Node(frame.PointAt(offsets[j + 1], level));
                var member = new BridgeGeometryMember($"DeckTransverse:{i + 1:D4}:{j + 1:D2}", BridgeMemberFamily.DeckTransverse,
                    i + 1, j + 1, 0, a, b, new(result.Nodes[a - 1], result.Nodes[b - 1]), s, s);
                result.Members.Add(member); strips.Add(new(member, width));
            }
            for (int g = 0; g < input.GirderOffsets.Length; g++)
            {
                double girderLevel = input.GirderLevels.Length == 1 ? input.GirderLevels[0] : input.GirderLevels[g];
                int a = editor.Node(frame.PointAt(input.GirderOffsets[g], girderLevel));
                if (a > steel.Nodes.Count) throw new ArgumentException("Build the steel geometry with the same deck stations before creating the grillage.");
                int b = editor.Node(frame.PointAt(input.GirderOffsets[g], level));
                links.Add(new(i + 1, g + 1, new(result.Nodes[a - 1], result.Nodes[b - 1])));
            }
        }
        if (result.Members.Count > 200000) throw new ArgumentException("At most 200000 generated grillage members.");
        result.Diagnostics.Add($"{strips.Count} transverse slab axes, {links.Count} eccentric connection axes; family 8 requires an explicit concrete property.");
        result.Diagnostics.Add("Deck strips follow the local transverse axis, including crossfall, independently of support skew. Widths are tributary lengths along the reference alignment; curved/skew decks require engineering calibration.");
        result.Diagnostics.Add("Connection lines must be assigned explicitly using Bridge Connections; the Grillage Geometry contains no implicit rigid links.");
        return new(result, stations, strips.ToArray(), links.ToArray());
    }

    private static int FindStation(List<double> stations, double value)
    {
        int i = stations.BinarySearch(value);
        if (i >= 0) return i;
        i = ~i;
        if (i > 0 && Math.Abs(stations[i - 1] - value) <= SimpleBridgeBuilder.JoinTolerance) return i - 1;
        if (i < stations.Count && Math.Abs(stations[i] - value) <= SimpleBridgeBuilder.JoinTolerance) return i;
        throw new ArgumentException("Deck beam station is missing from the steel geometry; rebuild with the same inputs.");
    }
}
