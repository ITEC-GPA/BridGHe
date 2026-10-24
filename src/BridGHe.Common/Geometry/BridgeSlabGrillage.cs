using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Geometry;

public sealed record BridgeSlabGrillageResult(BeamModel[] Beams, double[] Widths);

public static class BridgeSlabGrillage
{
    /// <summary>Transverse strips with actual tributary lengths, also for nonuniform stations.</summary>
    public static BridgeSlabGrillageResult Create(SimpleBridgeGeometry geometry, MaterialModel material, string name)
    {
        BridgeGeometryTools.Validate(geometry);
        if (material?.IsValid != true || material.Kind != MaterialModel.MaterialKind.Concrete ||
            !double.IsFinite(material.Modulus) || material.Modulus<=0 || !double.IsFinite(material.ShearModulus) || material.ShearModulus<=0)
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
            var section=new SectionModel { SectionType=SectionModel.SectionTypes.SolidRectangle,D=thickness,B=b-a };
            section.CalculateSection();
            var property=new BeamPropertyModel { Id=$"Slab{groups[i].Key}",Name=$"{name} S{groups[i].Key}",
                PropertyType=BeamPropertyType.Beam,Section=section,Material=material };
            rules.Add(new BridgeSectionRule { Family=8,Group=groups[i].Key,Start=0,End=length,Property=property });
        }
        var assignment=BridgeMemberAssignment.Assign(subset,new Dictionary<int,BeamPropertyModel>(),rules);
        if(assignment.Unassigned.Length!=0) throw new InvalidOperationException("Unassigned transverse slab members.");
        // These are an additional beam set, assembled beside independently numbered steel.
        // Retain geometric identities in metadata, but let Build Model allocate global FEM IDs.
        foreach(var beam in assignment.Beams)
            foreach(var id in beam.Attributes.OfType<ElementIdAttributeModel>().ToArray()) beam.Attributes.Remove(id);
        return new(assignment.Beams,assignment.Beams.Select(b=>widths[b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Group]).ToArray());
    }
}
