using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;

namespace BridGHe.Common.Geometry;

public sealed record BridgeSegmentCompositeResult(BeamModel[] Girders, CompositeBeamPropertyModel[] Properties, string[] SegmentIds);

/// <summary>Align one composite property per fabrication segment with the actual FEM girders, retaining stored results.</summary>
public static class BridgeSegmentComposite
{
    public static BridgeSegmentCompositeResult Map(IReadOnlyList<BeamModel> beams, IReadOnlyList<string> ids, IReadOnlyList<CompositeBeamPropertyModel> properties)
    {
        if (beams == null || beams.Count == 0 || beams.Any(b => b == null)) throw new ArgumentException("Supply bridge FEM beams with Bridge Member metadata.");
        if (ids == null || properties == null || ids.Count == 0 || ids.Count != properties.Count || ids.Any(string.IsNullOrWhiteSpace) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || properties.Any(p => p?.IsValid != true))
            throw new ArgumentException("Supply one valid composite property per distinct Segment ID; list lengths must match.");
        var rows = beams.Select(b => (beam: b, metadata: b.Attributes.OfType<BridgeMemberAttributeModel>().ToArray())).ToArray();
        if (rows.Any(r => r.metadata.Length != 1)) throw new ArgumentException("Every input beam requires exactly one Bridge Member identity.");
        var girders = rows.Where(r => r.metadata[0].Value.Family == BridgeMemberFamily.Girder)
            .OrderBy(r => r.metadata[0].Value.Group).ThenBy(r => r.metadata[0].Value.StationFrom).Select(r => r.beam).ToArray();
        string Key(BeamModel b) { var m = b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value; return $"G{m.Group}-C{m.Segment}"; }
        if (girders.Length == 0 || girders.Any(b => string.IsNullOrWhiteSpace(b.BeamId)) || girders.Select(b => b.BeamId).Distinct().Count() != girders.Length)
            throw new ArgumentException("Longitudinal FEM beam IDs must be nonempty and unique.");
        var keys = girders.Select(Key).ToArray();
        if (!keys.Distinct().ToHashSet(StringComparer.Ordinal).SetEquals(ids)) throw new ArgumentException("Segment IDs must cover exactly the fabrication segments of the supplied girders.");
        var lookup = ids.Select((key, i) => (key, property: properties[i])).ToDictionary(r => r.key, r => r.property, StringComparer.Ordinal);
        // This is a read-only association, not an override: no stiffness, loads or results are changed or cleared.
        return new(girders, keys.Select(k => lookup[k]).ToArray(), keys);
    }
}
