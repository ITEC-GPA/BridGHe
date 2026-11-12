using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;

namespace BridGHe.Grasshopper.Components.Geometry;

/// <summary>
/// R6.2 (lane B): the GPC data of Bridge Segment Override, built on GPC from the GPC data of its inputs (the bridge data of the drafts).
/// P2K (P1): the FEM-path helpers of Bridge Segment Sections, Bridge Segment Override and Bridge Segment Composite are removed.
/// </summary>
internal static class GpcSegmentSections
{
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
}
