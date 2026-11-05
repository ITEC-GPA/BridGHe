using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Checks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class SlabReinforcementTests
{
    internal static void Run()
    {
        void Require(bool ok, string why) { if (!ok) throw new Exception(why); }
        void Near(double actual, double expected) => Require(Math.Abs(actual-expected)<1e-8*Math.Max(1,Math.Abs(expected)), $"{actual} != {expected}");
        var legacy=ModelArchive.Read<SectionConcreteSlabModel>(File.ReadAllBytes("tests/BridGHe.Smoke/Fixtures/slab-single-row.bgha"));
        Require(legacy.Height==250 && legacy.Width==3000 && legacy.TopRebarArea==2000 && legacy.TopCover==40 && legacy.BottomRebarArea==0 && legacy.IsValid,"Legacy slab archive changed.");
        var slab=new SectionConcreteSlabModel(250,3000,2000,40,1500,55);
        var copy=ModelArchive.Clone(slab);
        Require(copy.BottomRebarArea==1500 && copy.BottomCover==55 && copy.TotalRebarArea==3500,"Two-row archive lost bottom reinforcement.");
        foreach(var invalid in new[]{new SectionConcreteSlabModel(250,3000,2000,40,-1,55),new(250,3000,2000,40,1500,250),new(250,3000,2000,200,1500,60),new(250,3000,2000,40,double.NaN,55)})
            Require(!invalid.IsValid,"Invalid reinforcement row accepted.");
        var prop=BridgeGrillageExample.Composite(new());prop.ConcreteSection=slab;prop.ConsiderRebar=true;
        var rc=prop.GetReinforcedConcreteSection();
        Near(rc.Rebars.Where(b=>Math.Abs(b.Position.Y-210)<1e-9).Sum(b=>b.RebarSection.Area),2000);
        Near(rc.Rebars.Where(b=>Math.Abs(b.Position.Y-55)<1e-9).Sum(b=>b.RebarSection.Area),1500);
        prop.ConcreteSection=legacy;var old=prop.GetHomogeneizedMechanicalProperties(prop.NInfinite);
        prop.ConcreteSection=new(250,3000,2000,40,0,55);var same=prop.GetHomogeneizedMechanicalProperties(prop.NInfinite);
        Require(old.Equals(same),"Zero bottom reinforcement changes existing numerical properties.");
#if GPC_PILOT
        GpcRows(prop,legacy,slab,Require,Near);
#endif

        var assembly=Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        IGH_Goo Wrap(string name,object value) { var t=assembly.GetType("BridGHe.Grasshopper.DataTypes."+name,true);var g=(IGH_Goo)Activator.CreateInstance(t);t.GetProperty("Value").SetValue(g,value);return g; }
        GH_Component Component(GH_Document d,string name) { var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks."+name,true));c.CreateAttributes();d.AddObject(c,false);return c; }
        void Set(GH_Component c,int port,IGH_Goo value) {var p=(Param_GenericObject)c.Params.Input[port];p.PersistentData.Clear();p.PersistentData.Append(value);p.ExpireSolution(false);}
        void Solve(GH_Document d,GH_Component c,bool button=false) {if(button){c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);}GH_Document.EnableSolutions=true;d.Enabled=true;d.NewSolution(false,GH_SolutionMode.Silent);Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,string.Join("; ",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));}
        using(var doc=new GH_Document())
        {
            var c=Component(doc,"BeamSectionConcreteSlabComponent");
            void Number(int i,double value) {var p=(Param_Number)c.Params.Input[i];p.PersistentData.Clear();p.PersistentData.Append(new GH_Number(value));p.ExpireSolution(false);}
            Number(0,250);Number(1,3000);Solve(doc,c);
            Require(((SectionConcreteSlabModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).TotalRebarArea==0,"Default optional rows should be disabled.");
            Number(2,2000);Number(3,40);Number(4,1500);Number(5,55);Solve(doc,c);
            Require(((SectionConcreteSlabModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).BottomCover==55 && c.Params.Input.Skip(2).All(p=>p.Optional),"GH bottom ports or optional flags invalid.");
        }
        foreach(bool tapered in new[]{false,true})
        {
            using var doc=new GH_Document();var c=Component(doc,"SuperElementCheckComponent");
            var start=BridgeGrillageExample.Composite(new());start.ConsiderRebar=true;start.ConcreteSection=slab;start.NShrinkage=12;
            var end=ModelArchive.Clone(start);if(tapered){end.ConcreteSection.BottomRebarArea=2500;end.ConcreteSection.BottomCover=65;end.NShrinkage=14;}
            var beam=new CompositeBeamModel(Point3d.Origin,new Point3d(10000,0,0),start,null,end){BeamId="B1"};
            var source=new BeamModel("B1",Point3d.Origin,new Point3d(10000,0,0),BridgeGrillageExample.LongProperty(new(),0));source.Attributes.Add(new SuperElementAttributeModel("Main"));
            var model=new ModelModel(ModelModel.ModelUnits.Nmm);model.AddElements(new[]{source});
            var combo=new LoadCombinationModel("Test");model.LoadCombinations.Add(combo);
            for(int phase=0;phase<3;phase++){var lc=BridgeGrillageExample.Case("Phase"+phase,phase);model.LoadCases.Add(lc);combo.Values.Add(lc,1);foreach(double x in new[]{0.0,0.5,1.0})beam.Results.Add(new BeamForceResultModel(lc,x,0,""){NormalizedLength=true,My=1e7});}
            var shrinkFem=new LoadCaseModel("Shrink FEM","Shrink FEM"){Stages=new(){new("Shrink FEM"){BridgePhase=LoadCaseModel.Stage.BridgePhases.NShrinkage}}};combo.Values.Add(shrinkFem,1);
            foreach(double x in new[]{0.0,.5,1.0})beam.Results.Add(new BeamForceResultModel(shrinkFem,x,0,"",true){My=5e6});
            var shrink=new LoadCaseModel("Shrink","Shrink"){Stages=new(){new("Shrink"){BridgePhase=LoadCaseModel.Stage.BridgePhases.StrainInfinite}}};
            combo.Values.Add(shrink,1);beam.Loads.Add(new CompositeBeamSlabStrainModel(shrink,new(){Strain=-0.0002}));
            Set(c,0,Wrap("Fem.GH_Model",model));Set(c,1,Wrap("GH_CompositeBeam",beam));Solve(doc,c,true);
            var results=c.Params.Output[0].VolatileData.AllData(true).Select(g=>(BridgeStressResult)g.ScriptVariable()).ToArray();Require(results.Length==3,"Expected three checked stations.");
            for(int i=0;i<3;i++){var input=results[i].Analysis.Input;var row=input.BottomRebars;Require(row.Enabled,"Bottom row lost before gPC checker.");Require(results[i].Analysis.Stages.Count==5,"Expected steel, long, instant, shrinkage FEM and autostress contributions.");Near(results[i].Stage.Contributions[3].HomogenizationN,12+(tapered?i:0));Near(results[i].Stage.Contributions.Last().HomogenizationN,12+(tapered?i:0));Near(row.AxisDistance,55+(tapered?5*i:0));Near(Math.PI*row.Diameter*row.Diameter/4*input.Geometry.SlabWidth/row.Pitch,1500+(tapered?500*i:0));}
#if GPC_PILOT
            GpcSuperElement(assembly,tapered,start,end,results,Require,Near);
#endif
        }
#if GPC_PILOT
        Console.WriteLine($"SLAB REINFORCEMENT GPC PASS: {gpcChecks} checks on definitions built in GPC (rows of bars, analysis section, invalid rows) and on Super Element Check with D3 on a GPC bridge model, equal to the FEM path.");
#endif
    }
#if GPC_PILOT
    // P2U (F6, chain E (c) and C5): the same checks on the composite section as the GPC contract of the Checker (BridgeCompositeSectionDefinition)
    // built directly in GPC (GpcCompositeDefinitions), and Super Element Check on the GPC path (D3 on the GPC bridge model of Build BridGHe
    // Model, from an action source built in GPC; the composite beam carries definitions built in GPC). Parity (removed with the FeMM models):
    // the definitions equal bit for bit to those of the FEM properties; the D3 checks equal to the BridGHe phases of the FEM fixture.
    // Still FEM until chain E: the slab type of Beam Section Concrete Slab (SectionConcreteSlabModel, its archive and its component), the
    // values of the composite beam and of the GH model (the merged FEM model carries the combinations).
    private static int gpcChecks;

    private static GpcCompositeDefinitions.Numbers Numbers(CompositeBeamPropertyModel p)
    {
        var s=p.ConcreteSection;
        return GpcCompositeDefinitions.Grillage(new()) with{Name=p.Name,SlabWidth=s.Width,SlabHeight=s.Height,TopArea=s.TopRebarArea,TopCover=s.TopCover,BottomArea=s.BottomRebarArea,
            BottomCover=s.BottomCover,ConsiderRebar=p.ConsiderRebar,NShortTerm=p.NInstant,NLongTerm=p.NInfinite,NShrinkage=p.NShrinkage};
    }

    /// <summary>The rows of bars of a definition built in GPC: both rows in the analysis section (one bar per row, whole area) and in the section
    /// of the checks (three bars per row) at their covers; rows that are not valid rejected; a zero bottom row whatever its cover gives the
    /// values of a section without it.</summary>
    private static void GpcRows(CompositeBeamPropertyModel fixture,SectionConcreteSlabModel legacy,SectionConcreteSlabModel slab,Action<bool,string> require,Action<double,double> near)
    {
        CompositeBeamPropertyModel With(SectionConcreteSlabModel s){var p=ModelArchive.Clone(fixture);p.ConcreteSection=ModelArchive.Clone(s);return p;}
        var two=With(slab);var definition=GpcCompositeDefinitions.Create(Numbers(two));
        foreach(var section in new[]{definition.AnalysisSection(),definition.Section})
        {
            near(section.Rebars.Where(b=>Math.Abs(b.Position.Y-210)<1e-9).Sum(b=>b.RebarSection.Area),2000);
            near(section.Rebars.Where(b=>Math.Abs(b.Position.Y-55)<1e-9).Sum(b=>b.RebarSection.Area),1500);
        }
        require(definition.RebarRows.BottomAreaMm2==1500 && definition.RebarRows.BottomCoverMm==55 && definition.Copy().RebarRows.BottomAreaMm2==1500,"GPC: Two-row definition lost bottom reinforcement.");
        foreach(var (area,cover) in new[]{(-1.0,55.0),(double.NaN,55.0),(1500,double.NaN)})
        {
            bool rejected=false;try{GpcCompositeDefinitions.Create(Numbers(two) with{BottomArea=area,BottomCover=cover});}catch(ArgumentException){rejected=true;}
            require(rejected,"GPC: Invalid reinforcement row accepted.");
        }
        var without=GpcCompositeDefinitions.Create(Numbers(With(legacy)));var zero=GpcCompositeDefinitions.Create(Numbers(With(new(250,3000,2000,40,0,55))));
        var a=BridGHe.Common.Gpc.GpcCompositeSections.AnalysisProperties(without,without.NLongTerm,true,true,true);var b=BridGHe.Common.Gpc.GpcCompositeSections.AnalysisProperties(zero,zero.NLongTerm,true,true,true);
        require(a.Equals(b),"GPC: Zero bottom reinforcement changes existing numerical properties.");
        // Parity (removed with the FeMM models).
        GpcCompositeDefinitions.RequireSame(definition,BridGHe.Common.Gpc.GpcCompositeSections.Definition(two,two.Name),"two rows");
        GpcCompositeDefinitions.RequireSame(without,BridGHe.Common.Gpc.GpcCompositeSections.Definition(With(legacy),fixture.Name),"legacy slab");
        require(a.Equals(With(legacy).GetHomogeneizedMechanicalProperties(without.NLongTerm)),"GPC: analysis values differ from the FEM property.");
        gpcChecks+=12;
    }

    /// <summary>
    /// Super Element Check on the D3 path: the GH model carries the GPC bridge model of Build BridGHe Model (GpcBridgeMerge) of a source built
    /// in GPC with the forces of the FEM fixture (BM2 1e7 for the three phases, 5e6 for the FEM shrinkage case, at 0, 5 and 10 m) and the
    /// recipe steel → long term → short term → shrinkage (FEM case and slab strain −0.0002); the composite beam carries definitions built in
    /// GPC (constant, or tapered: bottom row 1500 → 2500 mm² at cover 55 → 65, n,sh 12 → 14). Same assertions: three stations, bottom row
    /// enabled at its interpolated cover and area, five stages (steel, long, instant, shrinkage FEM, autostress), n,sh of the shrinkage
    /// contributions. Parity: the same stages, ratios, rows and stresses as the BridGHe phases of the FEM fixture.
    /// </summary>
    private static void GpcSuperElement(System.Reflection.Assembly assembly,bool tapered,CompositeBeamPropertyModel start,CompositeBeamPropertyModel end,BridgeStressResult[] fem,
        Action<bool,string> require,Action<double,double> near)
    {
        var stations=new[]{0d,5000,10000};var phaseCases=new[]{"Phase0","Phase1","Phase2"};
        // The FEM source of the merged FEM model (parity, and the combinations the GH model still carries) and its GPC twin.
        var source=new ModelModel(ModelModel.ModelUnits.Nmm){Name="Slab reinforcement source"};
        var femBeam=new BeamModel("B1",Point3d.Origin,new Point3d(10000,0,0),BridgeGrillageExample.LongProperty(new(),0)){BeamId="B1"};
        femBeam.Attributes.Add(new SuperElementAttributeModel("Main")); // the super-elements of the check, read from the merged FEM model (chain E)
        foreach(var name in phaseCases.Append("Shrink FEM"))
        {
            var lc=new LoadCaseModel(name,name);source.LoadCases.Add(lc);
            foreach(double s in stations)femBeam.Results.Add(new BeamForceResultModel(lc,s,0,"",false){My=name=="Shrink FEM"?5e6:1e7});
        }
        source.AddElements(new[]{femBeam});
        GPC.Model.Models.Model Gpc(bool analyse)
        {
            var test=new GpcTestModel("Slab reinforcement source");
            foreach(var name in phaseCases.Append("Shrink FEM"))test.Case(name);
            var beam=test.Beam("B1",test.Node("1",0,0,0),test.Node("2",10000,0,0));
            foreach(var name in phaseCases.Append("Shrink FEM"))foreach(double s in stations)test.Forces(beam,name,s,0,bm2:name=="Shrink FEM"?5e6:1e7);
            return analyse?test.Analyse():test.Structure();
        }
        var input=Gpc(false);var gpc=Gpc(true);
        BridgePhaseDefinition Phase(int order,BridgeSectionState state,BridgeActionDefinition action)=>new(){Order=order,Name="Phase "+order,State=state,Actions=new[]{action}};
        BridgeActionDefinition Action(string key,string lc,BridgeActionBehavior behavior,double? strain=null)=>new(){Key=key,Source=source,GpcSource=gpc,Case=lc,Behavior=behavior,Strain=strain};
        var phases=new[]{Phase(10,BridgeSectionState.Steel,Action("G","Phase0",BridgeActionBehavior.Steel)),Phase(20,BridgeSectionState.Composite,Action("L","Phase1",BridgeActionBehavior.LongTerm)),
            Phase(30,BridgeSectionState.Composite,Action("I","Phase2",BridgeActionBehavior.ShortTerm)),Phase(40,BridgeSectionState.Composite,Action("R","Shrink FEM",BridgeActionBehavior.Shrinkage,-0.0002))};
        var table=new[]{new[]{"","G","L","I","R"},new[]{"Test","1","1","1","1"}};
        var femPhases=phases.Select(p=>p with{Actions=p.Actions.Select(a=>a with{GpcSource=null}).ToArray()}).ToArray();
        var merged=BridgeWorkflowBuilder.Build(new[]{source},femPhases,table,BridgeWorkflowMethod.Compatible,1e-6);
        var bridge=GpcBridgeMerge.Build(new[]{(input,gpc)},phases,table,"slab reinforcement "+tapered);
        GpcBridgeMerge.RequireSame(bridge,merged,"slab reinforcement "+tapered);
        // The composite beam: FEM properties as its value until chain E, the definitions built in GPC (parity: those of the FEM properties).
        var startDefinition=GpcCompositeDefinitions.Create(Numbers(start));var endDefinition=tapered?GpcCompositeDefinitions.Create(Numbers(end)):startDefinition;
        GpcCompositeDefinitions.RequireSame(startDefinition,BridGHe.Common.Gpc.GpcCompositeSections.Definition(start,start.Name),"slab reinforcement start");
        GpcCompositeDefinitions.RequireSame(endDefinition,BridGHe.Common.Gpc.GpcCompositeSections.Definition(end,end.Name),"slab reinforcement end");
        IGH_Goo Wrap(string name,object value){var t=assembly.GetType("BridGHe.Grasshopper.DataTypes."+name,true);var g=(IGH_Goo)Activator.CreateInstance(t);t.GetProperty("Value").SetValue(g,value);return g;}
        var model=Wrap("Fem.GH_Model",merged.Model);model.GetType().GetMethod("AttachBridge").Invoke(model,new object[]{bridge.Bridge});
        var beam=(IGH_Goo)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.GH_CompositeBeam",true),
            new object[]{new CompositeBeamModel(Point3d.Origin,new Point3d(10000,0,0),start,null,end){BeamId="B1"},null});
        beam.GetType().GetProperty("StartDefinition").SetValue(beam,startDefinition);beam.GetType().GetProperty("EndDefinition").SetValue(beam,endDefinition);
        using var doc=new GH_Document();
        var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.SuperElementCheckComponent",true));c.CreateAttributes();doc.AddObject(c,false);
        void Set(int port,IGH_Goo value){var p=(Param_GenericObject)c.Params.Input[port];p.PersistentData.Clear();p.PersistentData.Append(value);p.ExpireSolution(false);}
        Set(0,model);Set(1,beam);
        c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);
        GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
        require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,"GPC: "+string.Join("; ",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        require(!c.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Any(m=>m.Contains("fasi BridGHe")),"GPC: Super Element Check fell back to the BridGHe phases.");
        var results=c.Params.Output[0].VolatileData.AllData(true).Select(g=>(BridgeStressResult)g.ScriptVariable()).ToArray();require(results.Length==3,"GPC: Expected three checked stations.");
        for(int i=0;i<3;i++)
        {
            var input3=results[i].Analysis.Input;var row=input3.BottomRebars;require(row.Enabled,"GPC: Bottom row lost before gPC checker.");
            require(results[i].Analysis.Stages.Count==5,"GPC: Expected steel, long, instant, shrinkage FEM and autostress contributions: "+results[i].Analysis.Stages.Count);
            near(results[i].Stage.Contributions[3].HomogenizationN,12+(tapered?i:0));near(results[i].Stage.Contributions.Last().HomogenizationN,12+(tapered?i:0));
            near(row.AxisDistance,55+(tapered?5*i:0));near(Math.PI*row.Diameter*row.Diameter/4*input3.Geometry.SlabWidth/row.Pitch,1500+(tapered?500*i:0));
            // Parity (removed with the FeMM models): the BridGHe phases of the FEM fixture.
            var f=fem[i];
            require(results[i].Stage.Contributions.Select(x=>x.HomogenizationN).SequenceEqual(f.Stage.Contributions.Select(x=>x.HomogenizationN)),"GPC: modular ratios of the contributions differ from the BridGHe phases.");
            near(results[i].Point1_ConcreteSigma,f.Point1_ConcreteSigma);near(results[i].Point8_SteelSigma,f.Point8_SteelSigma);
        }
        gpcChecks+=3+3*9;
    }
#endif
}
