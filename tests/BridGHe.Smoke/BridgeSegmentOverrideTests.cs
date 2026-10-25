using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

internal static class BridgeSegmentOverrideTests
{
    private static void Check(bool ok,string message){if(!ok)throw new Exception("SEGMENT OVERRIDE: "+message);}
    private static void Reject(Action action){try{action();}catch(ArgumentException){return;}throw new Exception("Expected invalid segment override rejection.");}
    internal static void Run()
    {
        var geometry=SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());var p=BridgeSegmentSectionsTests.BaseProperty();p.Material.Density=7.85e-9;
        var schedule=BridgeSegmentSections.Create(geometry,p,null);
        var beams=BridgeMemberAssignment.Assign(geometry,new Dictionary<int,BeamPropertyModel>{[-1]=p},schedule.Rules).Beams;
        var ids=schedule.Rules.Select(r=>$"G{r.Group}-C{r.Segment}").ToArray();
        var properties=ids.Select((id,i)=>{var copy=ModelArchive.Clone(p);copy.Section.T1=40+i*10;copy.Section.CalculateSection();return copy;}).ToArray();
        var lc=new LoadCaseModel("TEST","TEST");
        foreach(var b in beams)
        {
            b.Loads.Add(new BeamDistributedLoadModel(lc,new(){LoadSchema=BeamDistributedLoadModel.LoadSchema.Uniform,PA=-1,LoadDirection=BeamDistributedLoadModel.LoadDirection.Y},BeamDistributedLoadModel.LoadType.Force));
            b.Results.Add(new BeamForceResultModel(lc,0,0,""));
        }
        var result=BridgeSegmentOverride.Apply(beams,ids.Reverse().ToArray(),properties.Reverse().ToArray());
        Check(result.Girders.Length+result.OtherMembers.Length==beams.Length,"all steel members retained");
        foreach(var beam in result.Girders.Concat(result.OtherMembers))
        {
            var m=beam.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value;var original=beams.Single(b=>b.BeamId==beam.BeamId);
            Check(beam.PointFrom==original.PointFrom && beam.PointTo==original.PointTo && beam.AngleDeg==original.AngleDeg && beam.Offset==original.Offset,"geometry and IDs preserved");
            Check(beam.Results.Count==0 && original.Results.Count==1 && beam.Loads.Count==1,"only copied results cleared, input and loads retained");
            Check(beam.BeamProperty.T1==(m.Family==BridgeMemberFamily.Girder?40+(m.Group-1)*10:30),"replacement is keyed by concio rather than list order");
            Check(Math.Abs(beam.BeamProperty.Material.Density*beam.BeamProperty.SectionArea/(original.BeamProperty.Material.Density*original.BeamProperty.SectionArea)-1)<1e-12,"physical mass per length is preserved independently of transformed area");
        }
        result.Girders[0].BeamProperty.Material.Modulus=1;
        Check(properties.All(v=>v.Material.Modulus==210000) && beams.All(b=>b.BeamProperty.Material.Modulus==210000),"no alias into input properties or models");
        Reject(()=>BridgeSegmentOverride.Apply(beams,ids,properties.Take(1).ToArray()));
        Reject(()=>BridgeSegmentOverride.Apply(beams,ids.Select(_=>ids[0]).ToArray(),properties));
        Reject(()=>BridgeSegmentOverride.Apply(beams,new[]{"G99-C1"}.Concat(ids.Skip(1)).ToArray(),properties));
        Reject(()=>BridgeSegmentOverride.Apply(new[]{new BeamModel()},ids,properties));
        var fullDensity=BridgeSegmentOverride.Apply(beams,ids,properties,false);
        Check(fullDensity.Girders.All(b=>b.BeamProperty.Material.Density==p.Material.Density),"explicit opt-out retains replacement density");
        Console.WriteLine("SEGMENT OVERRIDE PASS: keyed replacement, identity/geometry/load preservation, stale results invalidation, independent copies and invalid-input rejection.");
    }
}
