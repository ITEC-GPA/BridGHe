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
        Gpc();
    }

    /// <summary>R6.6 R2a: Bridge Segment Override on beams given by their drafts alone (the path of P2): the beams of the assignment on GPC data
    /// (base property and conci by their GPC data), each with a load and linked to an analysed GPC beam (its results), through the component
    /// (outputs by their drafts alone) and through <see cref="BridgeSegmentOverride.ApplyGpc"/> for the refusals and the density opt-out.</summary>
    private static void Gpc()
    {
        const double density=7.85e-9;
        var geometry=SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());var p=R2aDesignFixtures.BaseProperty("Base",density);
        var schedule=BridgeSegmentSections.CreateGpc(geometry,p,null);
        var plans=R2aGeneratorFixtures.Assign(geometry,new Dictionary<int,BridGHe.Common.Gpc.Legacy.LegacyBeamProperty>{[-1]=p},schedule.Rules).Plans;
        var ids=schedule.Rules.Select(r=>$"G{r.Group}-C{r.Segment}").ToArray();
        var properties=ids.Select((id,i)=>R2aDesignFixtures.BaseProperty("Base",density,40+i*10)).ToArray();
        var lc=BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("TEST",false);
        var beams=R2aGeneratorFixtures.Drafts(plans,"segment override").Select((d,i)=>BridGHe.Common.Gpc.Builders.GpcBeamBuilders.WithDistributedLoad(d,lc,
            BridGHe.Common.Gpc.BridgeLoadSchema.Uniform,false,"Y",-1,0,0,0,0,0,null,R2aGeneratorFixtures.Id("segment override load:"+i))).ToArray();
        var analysed=R2aGeneratorFixtures.Analysed("segment override");
        var inputs=beams.Select(b=>{var g=R2aDesignFixtures.Beam(b);g.GetType().GetProperty("AnalysedGpc").SetValue(g,analysed.Beam);g.GetType().GetProperty("AnalysedGpcModel").SetValue(g,analysed.Model);return g;}).ToArray();
        using var doc=new Grasshopper.Kernel.GH_Document();
        var c=BridgeSegmentExcelTests.Add(doc,"BridgeSegmentOverrideComponent");
        R2aDesignFixtures.Input(c,0,inputs);
        var idParam=(Grasshopper.Kernel.Parameters.Param_String)c.Params.Input[1];idParam.PersistentData.Clear();idParam.SetPersistentData(ids.Reverse().Cast<object>().ToArray());
        R2aDesignFixtures.Input(c,2,properties.Reverse().Select(R2aDesignFixtures.Property).ToArray());
        BridgeSegmentExcelTests.Solve(doc);
        var girders=R2aDesignFixtures.Outputs(c,0);var others=R2aDesignFixtures.Outputs(c,1);
        Check(R2aDesignFixtures.GpcOnly(girders)&&R2aDesignFixtures.GpcOnly(others)&&girders.Length+others.Length==beams.Length,"all steel members retained");
        foreach(var output in girders.Concat(others))
        {
            var beam=R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacyBeamDraft>(output);
            var m=R2aGeneratorFixtures.Member(beam);int k=Array.FindIndex(beams,b=>b.Beam.Name==beam.Beam.Name);var original=beams[k];
            Check(R2aGeneratorFixtures.Distance(beam.Beam.StartPoint,original.Beam.StartPoint)==0 && R2aGeneratorFixtures.Distance(beam.Beam.EndPoint,original.Beam.EndPoint)==0
                && beam.AngleDeg==original.AngleDeg && beam.Beam.Assignments.OffsetI==null && original.Beam.Assignments.OffsetI==null,"geometry and IDs preserved");
            Check(R2aDesignFixtures.Get<object>(output,"AnalysedGpc")==null && R2aDesignFixtures.Get<object>(inputs[k],"AnalysedGpc")!=null && beam.Records.Loads==1 && beam.Beam.Assignments.Loads.Count==original.Beam.Assignments.Loads.Count,
                "only copied results cleared, input and loads retained");
            Check(R2aGeneratorFixtures.T1(beam)==(m.Family==GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder?40+(m.Group-1)*10:30),"replacement is keyed by concio rather than list order");
            Check(Math.Abs(BridGHe.Common.Gpc.GpcSectionMass.MassPerLength(R2aGeneratorFixtures.Elastic(beam))/BridGHe.Common.Gpc.GpcSectionMass.MassPerLength(R2aGeneratorFixtures.Elastic(original))-1)<1e-12,
                "physical mass per length is preserved independently of transformed area");
            Check(properties.All(v=>!ReferenceEquals(v.Property,beam.Beam.BeamProperty)&&!ReferenceEquals(R2aDesignFixtures.MaterialOf(v),R2aGeneratorFixtures.Elastic(beam).Material))
                && !ReferenceEquals(original.Beam.BeamProperty,beam.Beam.BeamProperty),"no alias into input properties or models");
        }
        Check(properties.All(v=>R2aDesignFixtures.MaterialOf(v).ElasticModulusTension==210000) && beams.All(b=>R2aGeneratorFixtures.Elastic(b).Material.ElasticModulusTension==210000),"no alias into input properties or models");
        Reject(()=>BridgeSegmentOverride.ApplyGpc(beams,ids,properties.Take(1).ToArray()));
        Reject(()=>BridgeSegmentOverride.ApplyGpc(beams,ids.Select(_=>ids[0]).ToArray(),properties));
        Reject(()=>BridgeSegmentOverride.ApplyGpc(beams,new[]{"G99-C1"}.Concat(ids.Skip(1)).ToArray(),properties));
        Reject(()=>BridgeSegmentOverride.ApplyGpc(new[]{GpcFixtures.SingleBeam("segment override without member")},ids,properties));
        var fullDensity=BridgeSegmentOverride.ApplyGpc(beams,ids,properties,false);
        Check(fullDensity.Girders.All(g=>R2aDesignFixtures.MaterialOf(g.Replacement).Density==density),"explicit opt-out retains replacement density");
        Console.WriteLine("SEGMENT OVERRIDE PASS: keyed replacement, identity/geometry/load preservation, stale results invalidation, independent copies and invalid-input rejection.");
    }
}
