using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Geometry;

public sealed record BridgeSlabGrillageResult(BeamModel[] Beams, double[] Widths)
{
    /// <summary>R6.2 (lane B): the plans of the GPC drafts of <see cref="Beams"/> (same order), their Element IDs removed as above.</summary>
    public BridgeMemberPlan[] Plans { get; init; } = Array.Empty<BridgeMemberPlan>();
}

public static class BridgeSlabGrillage
{
    /// <summary>Transverse strips with actual tributary lengths, also for nonuniform stations.</summary>
    /// <param name="analysisMaterial">R6.2 (lane B): the GPC analysis material of the concrete (<c>GpcMaterials.Analysis</c> of
    /// the datum of the GH material); given, the strip properties also get their GPC datum built on GPC (<see cref="BridgeGpcProperties"/>).</param>
    /// <param name="gpcMaterial">R6.5 P1 lane 2a: the GPC material given by its GPC datum alone: checked on the datum (a concrete with finite
    /// positive E and G), the strips have their GPC properties alone and the result has no FEM beams, only the plans (rule of passo 75). P2CE
    /// (13/11/2026, phase 1): the FEM material parameter and its FEM strips removed (every caller gave none).</param>
    public static BridgeSlabGrillageResult Create(SimpleBridgeGeometry geometry, string name, GPC.Model.Materials.Material analysisMaterial = null,
        GPC.Model.Materials.Material gpcMaterial = null)
    {
        BridgeGeometryTools.Validate(geometry);
        if (!(gpcMaterial is GPC.Model.Materials.ConcreteMaterialEuropeanCommon && analysisMaterial != null && double.IsFinite(gpcMaterial.ElasticModulusCompression)
                && gpcMaterial.ElasticModulusCompression > 0 && double.IsFinite(gpcMaterial.GetShearModule()) && gpcMaterial.GetShearModule() > 0))
            throw new ArgumentException("A valid concrete material in N-mm is required.");
        var subset=ModelArchive.Clone(geometry);
        subset.Members.RemoveAll(m => m.Family != BridgeMemberFamily.DeckTransverse);
        var groups=subset.Members.GroupBy(m => m.Group).OrderBy(g => g.First().StationFrom).ToArray();
        if (groups.Length<2) throw new ArgumentException("Connect Grillage Geometry containing transverse slab members.");
        double length=geometry.Stations[^1], thickness=geometry.DeckThickness;
        var rules=new List<BridgeSectionRule>(); var widths=new Dictionary<int,double>();
        for(int i=0;i<groups.Length;i++)
        {
            double s=groups[i].First().StationFrom;
            if(groups[i].Any(m=>m.StationFrom!=s || m.StationTo!=s)) throw new ArgumentException("Inconsistent slab station group.");
            double a=i==0?s:(groups[i-1].First().StationFrom+s)/2;
            double b=i==groups.Length-1?s:(s+groups[i+1].First().StationFrom)/2;
            if(b<=a) throw new ArgumentException("Slab stations must be distinct and increasing.");
            widths.Add(groups[i].Key,b-a);
            // R6.5 P1 lane 2a: the strip property by its GPC datum alone.
            rules.Add(new BridgeSectionRule { Family=8,Group=groups[i].Key,Start=0,End=length,GpcProperty=Gpc.Builders.GpcBridgeGeometryBuilders.StandardProperty($"{name} S{groups[i].Key}",
                    Gpc.Builders.GpcBridgeGeometryBuilders.StandardSection(Gpc.BridgeSectionKind.SolidRectangle,"",d:thickness,b:b-a),analysisMaterial,
                    GPC.Model.Structure.Assignments.BeamFormulation.StraightTwoNode, "Concrete") }); // L2C: the kind of the concrete
        }
        var assignment=BridgeMemberAssignment.Assign(subset,null,rules); // P2CE: no FEM defaults (before: an empty table, the same)
        if(assignment.Unassigned.Length!=0) throw new InvalidOperationException("Unassigned transverse slab members.");
        return new(Array.Empty<BeamModel>(),assignment.Plans.Select(p=>widths[p.Group]).ToArray()) { Plans=assignment.Plans.Select(p=>p with { Label=false }).ToArray() };
    }
}
