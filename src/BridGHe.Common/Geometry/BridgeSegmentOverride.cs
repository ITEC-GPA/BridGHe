using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Geometry;


/// <summary>Replace longitudinal section properties by physical concio, preserving beam identity and topology.</summary>
public static class BridgeSegmentOverride
{

    /// <summary>
    /// R6.5 P1 lane 1b: <see cref="Apply"/> on the GPC drafts of the beams (beams given by their drafts alone, no FEM value): the same checks
    /// in the same order with the same messages, on GPC data (the bridge member of a draft for its identity; with Preserve Mass the mass per
    /// length and the replacement area as the FEM path computes them, <see cref="Gpc.GpcSectionMass"/>), and the partition in input order:
    /// every girder with the replacement property of its concio (<see cref="Gpc.Builders.GpcBridgeGeometryBuilders.Replacement"/>, the first
    /// girder of the concio as the mass source, as the FEM path), the other members unchanged. <paramref name="properties"/>: the GPC datum of
    /// each property, null for an invalid one.
    /// </summary>
    public static ((Gpc.Legacy.LegacyBeamDraft Draft, Gpc.Legacy.LegacyBeamProperty Replacement)[] Girders, Gpc.Legacy.LegacyBeamDraft[] Others) ApplyGpc(
        IReadOnlyList<Gpc.Legacy.LegacyBeamDraft> beams, IReadOnlyList<string> ids, IReadOnlyList<Gpc.Legacy.LegacyBeamProperty> properties, bool preserveMass = true)
    {
        if (beams == null || beams.Count == 0 || beams.Any(b => b == null)) throw new ArgumentException("Supply the steel beams from Bridge Member Assignment.");
        if (ids == null || properties == null || ids.Count == 0 || ids.Count != properties.Count ||
            ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || properties.Any(p => p?.Property == null))
            throw new ArgumentException("Provide one distinct Segment ID and one valid property per concio; list lengths must match exactly.");
        if (beams.Any(b => b.Bridge?.Member == null)) throw new ArgumentException("Every beam must have exactly one Bridge Member identity from Bridge Member Assignment.");
        string Key(GPC.Checkers.CompositeBridge.Layout.BridgeMemberMetadata m) => $"G{m.Group}-C{m.Segment}";
        bool Girder(Gpc.Legacy.LegacyBeamDraft d) => d.Bridge.Member.Family == GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder;
        var expected = beams.Where(Girder).Select(d => Key(d.Bridge.Member)).Distinct().ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(ids)) throw new ArgumentException("Segment IDs must cover exactly the longitudinal fabrication segments; no missing or extra keys are allowed.");
        var byKey = ids.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => properties[p.i], StringComparer.Ordinal);
        if (preserveMass)
        {
            foreach (var group in beams.Where(Girder).GroupBy(d => Key(d.Bridge.Member)))
            {
                double mass = Gpc.GpcSectionMass.MassPerLength((GPC.Model.ElementProperties.ElasticBeamProperty)group.First().Beam.BeamProperty);
                double area = Gpc.GpcSectionMass.LegacyArea(((GPC.Model.ElementProperties.ElasticBeamProperty)byKey[group.Key].Property).Section);
                if (!double.IsFinite(mass) || mass < 0 || !double.IsFinite(area) || area <= 0 ||
                    group.Any(d => Math.Abs(Gpc.GpcSectionMass.MassPerLength((GPC.Model.ElementProperties.ElasticBeamProperty)d.Beam.BeamProperty) - mass) > Math.Max(1e-20, Math.Abs(mass) * 1e-10)))
                    throw new ArgumentException($"{group.Key}: preserving mass requires finite, constant source mass per length and a positive replacement area.");
            }
        }
        // The replacement of every concio once, from its first girder in input order (the FEM path: the density of the first girder).
        var replacements = new Dictionary<string, Gpc.Legacy.LegacyBeamProperty>(StringComparer.Ordinal);
        var girders = new List<(Gpc.Legacy.LegacyBeamDraft, Gpc.Legacy.LegacyBeamProperty)>(); var others = new List<Gpc.Legacy.LegacyBeamDraft>();
        foreach (var d in beams)
        {
            if (!Girder(d)) { others.Add(d); continue; }
            string key = Key(d.Bridge.Member);
            if (!replacements.TryGetValue(key, out var replacement))
                replacements.Add(key, replacement = Gpc.Builders.GpcBridgeGeometryBuilders.Replacement(byKey[key], d.Beam.BeamProperty, preserveMass));
            girders.Add((d, replacement));
        }
        return (girders.ToArray(), others.ToArray());
    }
}
