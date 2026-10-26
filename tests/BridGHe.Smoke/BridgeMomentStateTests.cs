using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;

internal static class BridgeMomentStateTests
{
    internal static void Run()
    {
        int count=0;
        void Check(bool condition,string message){if(!condition)throw new Exception("MOMENT STATE: "+message);count++;}
        void Reject(Action action){try{action();}catch(ArgumentException){count++;return;}throw new Exception("Expected invalid state rejection.");}
        var steel=BridgeSegmentSectionsTests.BaseProperty();steel.Material.Density=7.85e-9;
        var geometry=SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        var schedule=BridgeSegmentSections.Create(geometry,steel,null);
        var all=BridgeMemberAssignment.Assign(geometry,new Dictionary<int,BeamPropertyModel>{[-1]=steel},schedule.Rules).Beams;
        var beams=all.Where(b=>b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Family==BridgeMemberFamily.Girder).ToArray();
        var lc=new LoadCaseModel("Dead","Dead");
        for(int i=0;i<beams.Length;i++)foreach(double s in new[]{0d,.5,1d})
            beams[i].Results.Add(new BeamForceResultModel(lc,s,0,"",true){My=(i%5) switch{0=>1e8,1=>-1e8,2=>(s-.5)*2e8,3=>500,_=>1e8},Mx=-1e8});
        beams[4].Results.Clear();
        var states=BeamMomentClassification.Classify(beams,"Dead");
        Check(states.Take(5).Select(s=>s.Sign).SequenceEqual(new[]{BeamMomentSign.Positive,BeamMomentSign.Negative,BeamMomentSign.Mixed,BeamMomentSign.Zero,BeamMomentSign.Missing}),"five classes, tolerance and BM2 default");
        Check(BeamMomentClassification.Classify(beams,"Dead",component:0)[0].Sign==BeamMomentSign.Negative,"BM1 selection independent");
        Check(BeamMomentClassification.Classify(beams,"Dead",sign:-1)[0].Sign==BeamMomentSign.Negative,"orientation sign explicit");
        Reject(()=>BeamMomentClassification.Classify(beams,"Dead",-1));
        Reject(()=>BeamMomentClassification.Classify(new[]{beams[0],beams[0]},"Dead"));
        var bad=ModelArchive.Clone(beams[0]);bad.Results.RemoveAt(0);Reject(()=>BeamMomentClassification.Classify(new[]{bad},"Dead"));
        bad=ModelArchive.Clone(beams[0]);bad.Results.Add(bad.Results[0]);Reject(()=>BeamMomentClassification.Classify(new[]{bad},"Dead"));
        var ids=schedule.Rules.Select(r=>$"G{r.Group}-C{r.Segment}").ToArray();
        var props=ids.Select(id=>{var p=BridgeGrillageExample.Composite(new());p.SteelSection=ModelArchive.Clone(steel.Section);p.ConsiderRebar=true;p.ConcreteSection=new(250,3000,1600,40);return p;}).ToArray();
        BridgeSegmentOverrideResult Apply(IReadOnlyList<BeamMomentState> s,int mix=2)=>BridgeStateOverride.Apply(all,ids,props,s,BridgeActionBehavior.LongTerm,mix);
        Reject(()=>Apply(states));
        foreach(double s in new[]{0d,.5,1d})beams[4].Results.Add(new BeamForceResultModel(lc,s,0,"",true){My=1e8});
        states=BeamMomentClassification.Classify(beams,"Dead");Reject(()=>Apply(states,0));
        var original=ModelArchive.Write(all);var result=Apply(states);
        var first=result.Girders[0];var second=result.Girders[1];
        Check(first.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Segment==second.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Segment,"same physical concio fixture");
        Check(first.BeamProperty.SectionArea>second.BeamProperty.SectionArea && first.BeamProperty.I11>second.BeamProperty.I11,"same concio has distinct uncracked/cracked stiffness");
        Check(result.Girders.All(b=>b.BeamProperty.Section.CompositePreview!=null),"composite preview retained");
        Check(result.Girders.All(b=>b.Results.Count==0) && ModelArchive.Write(all).SequenceEqual(original),"no source mutations or stale output results");
        foreach(var b in result.Girders){var old=beams.Single(x=>x.BeamId==b.BeamId);Check(Math.Abs(b.BeamProperty.Material.Density*b.BeamProperty.SectionArea-old.BeamProperty.Material.Density*old.BeamProperty.SectionArea)<1e-14,"physical mass retained");}
        Check(ModelArchive.Clone(second).Attributes.OfType<BridgeSectionStateAttributeModel>().Single().Value.ConcreteExcluded,"local state archive");
        var stale=states.ToArray();stale[0]=states[0] with {Source=ModelArchive.Clone(beams[0])};stale[0].Source.BeamProperty.Material.Modulus=1;Reject(()=>Apply(stale));
        Reject(()=>Apply(states.Skip(1).ToArray()));
        Reject(()=>BridgeStateOverride.Apply(all,ids,props,states,BridgeActionBehavior.Shrinkage));
        // Merge a controlled N/M fixture. Native Straus is never invoked.
        var model0=new ModelModel(ModelModel.ModelUnits.Nmm);model0.AddElements(all);model0.LoadCases.Add(lc);
        var model1=new ModelModel(ModelModel.ModelUnits.Nmm);model1.AddElements(result.Girders.Concat(result.OtherMembers));model1.LoadCases.Add(lc);
        foreach(var m in new[]{model0,model1})foreach(var b in m.Beams){b.Results.Clear();foreach(double s in new[]{0d,1d})b.Results.Add(new BeamForceResultModel(lc,s,0,"",true){AxialForce=100000,My=-1e8});}
        var phase0=new BridgePhaseDefinition{Order=0,Name="Steel",Actions=new[]{new BridgeActionDefinition{Key="G",Source=model0,Case="Dead",Behavior=BridgeActionBehavior.Steel}}};
        var phase1=new BridgePhaseDefinition{Order=1,Name="Composite",State=BridgeSectionState.Composite,Actions=new[]{new BridgeActionDefinition{Key="Q",Source=model1,Case="Dead",Behavior=BridgeActionBehavior.LongTerm}}};
        var phase2=new BridgePhaseDefinition{Order=2,Name="Shrinkage U",State=BridgeSectionState.Composite,Actions=new[]{new BridgeActionDefinition{Key="R",Behavior=BridgeActionBehavior.Shrinkage,Strain=-200e-6,PrimaryOnly=true}}};
        var merged=BridgeWorkflowBuilder.Build(new[]{model0,model1},new[]{phase0,phase1,phase2},new[]{new[]{"Name","G","Q","R"},new[]{"SLS","0","1","0"}},BridgeWorkflowMethod.Compatible,1e-6).Model;
        var wf=ModelArchive.Clone(merged).BridgeWorkflow;
        Check(wf.Phases[1].Actions[0].ConcreteExcludedBeamIds.Contains(second.BeamId),"per-action cracked identity survives merge/archive");
        Check(!wf.Phases[2].Actions[0].HasLocalSectionStates,"shrinkage remains uncracked");
        var adapter=new BridgeCheckAdapter(props[0].GetReinforcedConcreteSection(),GPC.Geometry.CoordinateSystem.Global,new(){Class4=false,LimitState=BridgeLimitState.Ultimate});
        var mb=merged.Beams.Single(b=>b.BeamId==second.BeamId);
        Check(BridgePhaseAssembler.CreateIndex(new CompositeBeamModel{BeamId=mb.BeamId},mb.Length,1e-6).BeamId==mb.BeamId,"composite checker beam retains FEM identity");
        double phi=props[0].NInfinite/props[0].NInstant-1;
        var response=adapter.AnalyzeWorkflow(wf,BridgePhaseAssembler.CreateIndex(mb,mb.Length,1e-6),0,merged.LoadCombinations[0],phi,0,phi,true);
        var contribution=response.Stage.Contributions.Single(c=>c.Name.Contains("Q / FEM"));
        var phases=response.Analysis.Input.Phases;
        var q=phases.Single(p=>p.Name.Contains("Q / FEM"));
        Check(q.Kind==BridgePhaseKind.ConcreteExcluded && q.Reference==BridgeLoadReference.GrossCentroid,"checker uses actual cracked gross centroid");
        Check(q.ForceKN==100 && q.MomentKNm==-100,"raw FEM N/M retained about their own centroid");
        var gross=HBridgeSection.GrossPhaseProperties(response.Analysis.Input,q);
        Check(Math.Abs(gross.Y-second.BeamProperty.Section.Centroid.Y)<1e-6,"FEM and checker cracked centroids agree");
        Check(Math.Abs(gross.Area/second.BeamProperty.SectionArea-1)<1e-10,"FEM and checker cracked areas agree");
        double expected=100000/gross.Area-(-1e8)*(-100-gross.Y)/gross.Ix;
        Check(Math.Abs(contribution.SteelStress(-100)-expected)<1e-7,"N/A - M(y-yG)/I agrees with the section stress");
        Check(phases.Last().Kind==BridgePhaseKind.Composite,"uncracked shrinkage activation kept separately");
        Check(!contribution.HasConcrete,"cracked mechanical contribution excludes concrete");
        Check(response.Assumptions.Contains(BridgeCheckAdapter.LocalStateAssumption),"local-state assumptions travel into report results");
        // More than 20 alternative local-state actions must not exceed GPC's stage limit.
        // Zero-coefficient alternatives preserve activation, not one duplicate stage per position.
        foreach(var method in new[]{BridgeWorkflowMethod.Compatible,BridgeWorkflowMethod.LinearHistory})
        {
            var index=BridgePhaseAssembler.CreateIndex(mb,mb.Length,1e-6);
            var baseline=adapter.AnalyzeWorkflow(wf with{Method=method},index,0,merged.LoadCombinations[0],phi,0,phi,true);
            foreach(int activeAt in new[]{0,20,40})
            {
                var alternatives=Enumerable.Range(0,40).Select(i=>wf.Phases[1].Actions[0] with{Key="INACTIVE_"+i,ForceCase=wf.Phases[0].Actions[0].ForceCase}).ToList();
                alternatives.Insert(activeAt,wf.Phases[1].Actions[0]);
                var expanded=wf with{Method=method,Phases=new[]{wf.Phases[0],wf.Phases[1] with{Actions=alternatives.ToArray()},wf.Phases[2]}};
                var actual=adapter.AnalyzeWorkflow(expanded,index,0,merged.LoadCombinations[0],phi,0,phi,true);
                Check(actual.Analysis.Stages.Count<=5,"Inactive local alternatives coalesced; "+method);
                Check(Math.Abs(actual.Point8_SteelSigma-baseline.Point8_SteelSigma)<1e-8&&Math.Abs(actual.Point1_ConcreteSigma-baseline.Point1_ConcreteSigma)<1e-8,"Alternative count and ordering leave stresses unchanged; "+method);
            }
        }
        var edited=ModelArchive.Clone(model1);edited.Beams.First(b=>b.Attributes.OfType<BridgeSectionStateAttributeModel>().Any()).BeamProperty.Material.Modulus=1;
        Reject(()=>BridgeWorkflowBuilder.Build(new[]{model0,edited},new[]{phase0,phase1 with {Actions=new[]{phase1.Actions[0] with {Source=edited}}}},new[]{new[]{"Name","G","Q"},new[]{"SLS","1","1"}},BridgeWorkflowMethod.Compatible,1e-6));
        Console.WriteLine($"MOMENT STATE PASS: {count} assertions; sampled sign, same-concio variants, mass, mesh, archive, per-action checker state and uncracked shrinkage. No native FEM.");
    }
}
