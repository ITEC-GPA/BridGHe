using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;

namespace BridGHe.Grasshopper.Components.Geometry;

/// <summary>
/// R6.2 (lane B): the GPC data of Bridge Segment Sections, Bridge Segment Override and Bridge Segment Composite, built on GPC from the GPC data of
/// their inputs and from the data of the generators without FEM values (<see cref="BridgeSegmentSectionResult.Sections"/>, the bridge data of
/// the drafts). Signatures of the properties they build: those of the conversions of their FEM values (passo 53, until R6.3/R6.5).
/// </summary>
internal static class GpcSegmentSections
{
    private static bool Refusal(Exception x) => x is ArgumentException || x is NotSupportedException || x is InvalidOperationException;

    /// <summary>Bridge Segment Sections: per rule, the I section of its concio (recipe: name and dimensions B1, B2, D, T1, T2, T3 as written) and
    /// the property "Concio &lt;id&gt;" on the material and formulation of the base property; the property datum goes with the FEM property of
    /// the rule (<see cref="BridgeGpcProperties"/>). Null when the reader refuses the base property (nothing attached, as before R6.2).</summary>
    internal static (LegacyBeamProperty Property, LegacySectionRecipe Section)[] Build(BridgeSegmentSectionResult result, GH_BeamProperty baseProperty)
    {
        LegacyBeamProperty basis;
        try { basis = baseProperty.Gpc; } catch (Exception x) when (Refusal(x)) { return null; }
        var material = ((GPC.Model.ElementProperties.ElasticBeamProperty)basis.Property).Material;
        var built = new (LegacyBeamProperty, LegacySectionRecipe)[result.Rules.Length];
        for (int i = 0; i < built.Length; i++)
        {
            var (name, v) = result.Sections[i]; var rule = result.Rules[i];
            var recipe = GpcBridgeGeometryBuilders.StandardSection(BridgeSectionKind.I, name, d: v[2], b1: v[0], b2: v[1], t1: v[3], t2: v[4], t3: v[5]);
            var property = GpcBridgeGeometryBuilders.StandardProperty(rule.Property.Name, recipe, material, basis.Data.Formulation);
            BridgeGpcProperties.Attach(rule.Property, property);
            built[i] = (property, recipe);
        }
        return built;
    }

    /// <summary>Bridge Segment Override: the girders (input order) with the replacement property of their concio (Beam Override of the property,
    /// with Preserve Mass the density of the source girder's mass per length over the replacement area), the other members as copies of their
    /// drafts; the family and the concio from the bridge data of the drafts.</summary>
    internal static (List<GH_Beam> Girders, List<GH_Beam> Others) Override(BridgeSegmentOverrideResult result, IReadOnlyList<GH_Beam> beams, IReadOnlyList<string> ids,
        IReadOnlyList<GH_BeamProperty> properties, bool preserveMass)
    {
        var girders = result.Girders.Select(b => new GH_Beam(b)).ToList(); var others = result.OtherMembers.Select(b => new GH_Beam(b)).ToList();
        LegacyBeamDraft[] drafts;
        try { drafts = beams.Select(b => b.Gpc).ToArray(); } catch (Exception x) when (Refusal(x)) { return (girders, others); }
        string Key(GPC.Checkers.CompositeBridge.Layout.BridgeMemberMetadata m) => $"G{m.Group}-C{m.Segment}";
        bool Girder(LegacyBeamDraft d) => d.Bridge?.Member?.Family == GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder;
        // The replacement of every concio: the source is its first girder in input order; the FEM replacement of its first output girder
        // gives the signature of the conversion.
        var replacements = new Dictionary<string, Func<LegacyBeamProperty>>(StringComparer.Ordinal);
        int g = 0;
        foreach (var d in drafts)
        {
            if (!Girder(d)) continue;
            string key = Key(d.Bridge.Member); var fem = result.Girders[g++].BeamProperty;
            if (replacements.ContainsKey(key)) continue;
            int k = ids.ToList().IndexOf(key); var source = d.Beam.BeamProperty; var input = properties[k];
            replacements.Add(key, () => GpcBridgeGeometryBuilders.Replacement(input.Gpc, source, preserveMass));
        }
        g = 0; int o = 0;
        foreach (var d in drafts)
        {
            var draft = d;
            if (Girder(d)) { var replacement = replacements[Key(d.Bridge.Member)]; GpcBeamInputs.Attach(girders[g++], id => GpcBeamBuilders.Override(draft, replacement(), null, null, null, id)); }
            else GpcBeamInputs.Attach(others[o++], id => GpcDrafts.Reidentify(GpcDrafts.Copy(draft), id));
        }
        return (girders, others);
    }

    /// <summary>R6.5 P1 lane 1b: Bridge Segment Override for a list with a beam given by its draft alone: the checks and the partition of
    /// <see cref="BridgeSegmentOverride.ApplyGpc"/> on the drafts of the beams (same messages), then every girder with the replacement property
    /// of its concio (Beam Override of the property) and every other member as a copy of its draft, each a new beam without FEM value (the FEM
    /// path gives new copies with their results cleared) with the identity derived from its input draft (output 0 the girders, 1 the others);
    /// the Beam IDs of the girders (the names of their drafts).</summary>
    internal static (List<GH_Beam> Girders, List<GH_Beam> Others, List<string> BeamIds) GpcOnly(global::Grasshopper.Kernel.GH_Component component, IReadOnlyList<GH_Beam> beams,
        IReadOnlyList<string> ids, IReadOnlyList<GH_BeamProperty> properties, bool preserveMass)
    {
        if (beams == null || beams.Count == 0 || beams.Any(b => b == null)) throw new ArgumentException("Supply the steel beams from Bridge Member Assignment.");
        var byDraft = new Dictionary<LegacyBeamDraft, GH_Beam>(ReferenceEqualityComparer.Instance);
        var drafts = beams.Select(b => b.Gpc ?? throw new ArgumentException(GpcOnlyElements.NoDraft)).ToArray();
        for (int i = 0; i < drafts.Length; i++) byDraft.TryAdd(drafts[i], beams[i]);
        var gpc = properties?.Select(p => p?.IsValid == true ? p.Gpc : null).ToArray();
        var (girders, others) = BridgeSegmentOverride.ApplyGpc(drafts, ids, gpc, preserveMass);
        var outGirders = new List<GH_Beam>(); var outOthers = new List<GH_Beam>(); var beamIds = new List<string>();
        foreach (var (draft, replacement) in girders)
        {
            var g = GpcOnlyElements.NewBeam(component, 0, byDraft[draft], id => GpcBeamBuilders.Override(draft, replacement, null, null, null, id), replacement);
            if (g == null) continue;
            outGirders.Add(g); beamIds.Add(draft.Beam.Name);
        }
        foreach (var draft in others)
        {
            var o = GpcOnlyElements.NewBeam(component, 1, byDraft[draft], id => GpcDrafts.Reidentify(GpcDrafts.Copy(draft), id));
            if (o != null) outOthers.Add(o);
        }
        return (outGirders, outOthers, beamIds);
    }

    /// <summary>Bridge Segment Composite: a composite property output keeps the datum of its input (the same FEM value).</summary>
    internal static void TryAttach(GH_CompositeBeamProperty output, GH_CompositeBeamProperty source)
    {
        GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition datum;
        try { datum = source.Gpc; } catch (Exception x) when (Refusal(x)) { return; }
        if (datum != null) output.AttachGpc(datum);
    }
}
