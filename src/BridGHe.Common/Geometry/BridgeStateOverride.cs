using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Geometry;

public static class BridgeStateOverride
{
    // Mixed policy: 0=reject, 1=uncracked, 2=cracked. Zero policy: 0=uncracked, 1=cracked.
    public static BridgeSegmentOverrideResult Apply(IReadOnlyList<BeamModel> beams, IReadOnlyList<string> ids,
        IReadOnlyList<CompositeBeamPropertyModel> properties, IReadOnlyList<BeamMomentState> states,
        BridgeActionBehavior behavior, int mixedPolicy = 0, int zeroPolicy = 0, bool preserveMass = true)
    {
        if (behavior is not (BridgeActionBehavior.LongTerm or BridgeActionBehavior.ShortTerm) || mixedPolicy is < 0 or > 2 || zeroPolicy is < 0 or > 1)
            throw new ArgumentException("Only mechanical n infinity/n0 states are supported; shrinkage remains a separate uncracked model.");
        if (beams == null || beams.Any(b => b?.IsValid != true)) throw new ArgumentException("Provide valid steel reference beams and properties.");
        var mapped = BridgeSegmentComposite.Map(beams, ids, properties);
        if (states == null || states.Any(s => s?.Source?.IsValid != true || !Enum.IsDefined(s.Sign) || string.IsNullOrWhiteSpace(s.Case)) ||
            states.Select(s => s.Case).Distinct(StringComparer.Ordinal).Count() != 1 || states.Select(s => s.Source.BeamId).Distinct(StringComparer.Ordinal).Count() != states.Count ||
            !mapped.Girders.Select(b => b.BeamId).ToHashSet(StringComparer.Ordinal).SetEquals(states.Select(s => s.Source.BeamId)))
            throw new ArgumentException("One moment state is required for each girder FEM ID, without additional beams.");
        var map = states.ToDictionary(s => s.Source.BeamId, StringComparer.Ordinal);
        var original = ModelArchive.Clone(beams.ToArray()); // one graph clone preserves shared load cases
        var propertyMap = mapped.Girders.Select((b,i) => (b.BeamId, Property: mapped.Properties[i])).ToDictionary(p => p.BeamId, p => p.Property);
        var girders = new List<BeamModel>(); var others = new List<BeamModel>();
        var variants = new Dictionary<(string Segment, bool Cracked, double Mass), BeamPropertyModel>();
        foreach (var b in original)
        {
            b.Results.Clear();
            if (!map.TryGetValue(b.BeamId, out var state)) { others.Add(b); continue; }
            var source = state.Source;
            bool Equal(double a, double c) => Math.Abs(a-c) <= Math.Max(1e-9, Math.Max(Math.Abs(a), Math.Abs(c))*1e-10);
            if (source.PointFrom.DistanceTo(b.PointFrom)>1e-5 || source.PointTo.DistanceTo(b.PointTo)>1e-5 || source.Offset != b.Offset || source.AngleDeg != b.AngleDeg ||
                !Equal(source.BeamProperty.SectionArea,b.BeamProperty.SectionArea) || !Equal(source.BeamProperty.I11,b.BeamProperty.I11) ||
                !Equal(source.BeamProperty.I22,b.BeamProperty.I22) || !Equal(source.BeamProperty.Material.Modulus,b.BeamProperty.Material.Modulus) ||
                !Equal(source.BeamProperty.J,b.BeamProperty.J) || Math.Abs(source.BeamProperty.Material.Density-b.BeamProperty.Material.Density)>1e-20)
                throw new ArgumentException($"Beam {b.BeamId}: reference geometry or steel stiffness changed. Recalculate model 0.");
            if (state.Sign == BeamMomentSign.Missing || state.Sign == BeamMomentSign.Mixed && mixedPolicy == 0)
                throw new ArgumentException($"Beam {b.BeamId}: {state.Sign}. Obtain FEM results or explicitly choose a mixed-beam policy/refine the common mesh.");
            bool cracked = state.Sign == BeamMomentSign.Negative || state.Sign == BeamMomentSign.Mixed && mixedPolicy == 2 || state.Sign == BeamMomentSign.Zero && zeroPolicy == 1;
            var full = propertyMap[b.BeamId];
            if (!full.ConsiderConcrete || !full.ConsiderRebar) throw new ArgumentException("Supply full composite definitions with concrete and reinforcement enabled.");
            double n = behavior == BridgeActionBehavior.LongTerm ? full.NInfinite : full.NInstant;
            double mass = b.BeamProperty.Material.Density*b.BeamProperty.SectionArea;
            var meta = b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value;
            string segment = $"G{meta.Group}-C{meta.Segment}";
            var key = (segment, cracked, preserveMass ? mass : 0d);
            if (!variants.TryGetValue(key,out var replacement))
            {
                var section = ModelArchive.Clone(full); section.ConsiderConcrete = !cracked;
                replacement = section.CreateHomogenizedProperty(n, $"_{behavior}_{(cracked ? "C" : "U")}");
                replacement.Name = segment + replacement.Name; replacement.Id = replacement.Name;
                if (preserveMass) replacement.Material.Density = mass / replacement.SectionArea;
                if (!double.IsFinite(replacement.Material.Density) || replacement.Material.Density < 0) throw new ArgumentException("Invalid physical mass per length.");
                variants.Add(key,replacement);
            }
            b.BeamProperty = replacement;
            foreach (var old in b.Attributes.OfType<BridgeSectionStateAttributeModel>().ToArray()) b.Attributes.Remove(old);
            b.Attributes.Add(new BridgeSectionStateAttributeModel(new(behavior,cracked,n,state.Case,BridgeSectionAssignment.Signature(replacement))));
            girders.Add(b);
        }
        return new(girders.ToArray(),others.ToArray());
    }
}
