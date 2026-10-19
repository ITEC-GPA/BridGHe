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
            var shrink=new LoadCaseModel("Shrink","Shrink"){Stages=new(){new("Shrink"){BridgePhase=LoadCaseModel.Stage.BridgePhases.StrainInfinite}}};
            combo.Values.Add(shrink,1);beam.Loads.Add(new CompositeBeamSlabStrainModel(shrink,new(){Strain=-0.0002}));
            Set(c,0,Wrap("Fem.GH_Model",model));Set(c,1,Wrap("GH_CompositeBeam",beam));Solve(doc,c,true);
            var results=c.Params.Output[0].VolatileData.AllData(true).Select(g=>(BridgeStressResult)g.ScriptVariable()).ToArray();Require(results.Length==3,"Expected three checked stations.");
            for(int i=0;i<3;i++){var input=results[i].Analysis.Input;var row=input.BottomRebars;Require(row.Enabled,"Bottom row lost before gPC checker.");Near(results[i].Stage.Contributions.Last().HomogenizationN,12+(tapered?i:0));Near(row.AxisDistance,55+(tapered?5*i:0));Near(Math.PI*row.Diameter*row.Diameter/4*input.Geometry.SlabWidth/row.Pitch,1500+(tapered?500*i:0));}
        }
    }
}
