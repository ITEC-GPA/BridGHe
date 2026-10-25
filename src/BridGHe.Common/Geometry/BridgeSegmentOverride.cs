using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Geometry;

public sealed record BridgeSegmentOverrideResult(BeamModel[] Girders, BeamModel[] OtherMembers);

/// <summary>Replace longitudinal section properties by physical concio, preserving beam identity and topology.</summary>
public static class BridgeSegmentOverride
{
    public static BridgeSegmentOverrideResult Apply(IReadOnlyList<BeamModel> beams, IReadOnlyList<string> ids, IReadOnlyList<BeamPropertyModel> properties, bool preserveMass = true)
    {
        if (beams == null || beams.Count == 0 || beams.Any(b => b == null)) throw new ArgumentException("Supply the steel beams from Bridge Member Assignment.");
        if (ids == null || properties == null || ids.Count == 0 || ids.Count != properties.Count ||
            ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || properties.Any(p => p?.IsValid != true))
            throw new ArgumentException("Provide one distinct Segment ID and one valid property per concio; list lengths must match exactly.");
        var metadata = beams.Select(b => b.Attributes.OfType<BridgeMemberAttributeModel>().ToArray()).ToArray();
        if (metadata.Any(m => m.Length != 1)) throw new ArgumentException("Every beam must have exactly one Bridge Member identity from Bridge Member Assignment.");
        string Key(BridgeMemberMetadata m) => $"G{m.Group}-C{m.Segment}";
        var expected = metadata.Select(m => m[0].Value).Where(m => m.Family == BridgeMemberFamily.Girder).Select(Key).Distinct().ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(ids)) throw new ArgumentException("Segment IDs must cover exactly the longitudinal fabrication segments; no missing or extra keys are allowed.");
        var replacements = ids.Select((id, i) => (id, property: ModelArchive.Clone(properties[i]))).ToDictionary(p => p.id, p => p.property, StringComparer.Ordinal);
        foreach (var pair in replacements)
            if (string.IsNullOrWhiteSpace(pair.Value.Id)) pair.Value.Id = string.IsNullOrWhiteSpace(pair.Value.Name) ? pair.Key : pair.Value.Name;
        if (preserveMass)
        {
            foreach (var group in beams.Select((beam,i)=>(beam,meta:metadata[i][0].Value)).Where(p=>p.meta.Family==BridgeMemberFamily.Girder).GroupBy(p=>Key(p.meta)))
            {
                double mass=group.First().beam.BeamProperty.Material.Density*group.First().beam.BeamProperty.SectionArea;
                var replacement=replacements[group.Key];
                if (!double.IsFinite(mass) || mass<0 || !double.IsFinite(replacement.SectionArea) || replacement.SectionArea<=0 ||
                    group.Any(p=>Math.Abs(p.beam.BeamProperty.Material.Density*p.beam.BeamProperty.SectionArea-mass)>Math.Max(1e-20,Math.Abs(mass)*1e-10)))
                    throw new ArgumentException($"{group.Key}: preserving mass requires finite, constant source mass per length and a positive replacement area.");
                // A steel-referenced transformed area is stiffness data, not physical steel volume.
                replacement.Material.Density=mass/replacement.SectionArea;
            }
        }
        var girders = new List<BeamModel>(); var others = new List<BeamModel>();
        for (int i = 0; i < beams.Count; i++)
        {
            var beam = ModelArchive.Clone(beams[i]); beam.Results.Clear();
            var m = metadata[i][0].Value;
            if (m.Family == BridgeMemberFamily.Girder) { beam.BeamProperty = replacements[Key(m)]; girders.Add(beam); }
            else others.Add(beam);
        }
        return new(girders.ToArray(), others.ToArray());
    }
}
