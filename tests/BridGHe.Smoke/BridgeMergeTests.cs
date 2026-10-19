using System;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Checks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class BridgeMergeTests
{
    internal static void Run()
    {
        void Require(bool value,string text){if(!value)throw new Exception(text);}
        ModelModel Model(int phase)
        {
            var m=new ModelModel(ModelModel.ModelUnits.Nmm);var lc=new LoadCaseModel("G","G");var strain=new LoadCaseModel("S","S");m.LoadCases.Add(lc);m.LoadCases.Add(strain);
            foreach(int i in phase==0?new[]{0,1}:new[]{1,0})
            {
                var b=new BeamModel("B"+(i+1),new Point3d(0,1000*i,0),new Point3d(10000,1000*i,0),BridgeGrillageExample.LongProperty(new(),phase)){BeamId="B"+(i+1)};b.Attributes.Add(new SuperElementAttributeModel("Main"));
                foreach(double x in new[]{0.0,.5,1})b.Results.Add(new BeamForceResultModel(lc,x,0,"",true){AxialForce=(phase+1)*100+(i+1)*10+x,My=1e7});
                b.Loads.Add(new CompositeBeamSlabStrainModel(strain,new(){Strain=-(phase+1)*.0001}));m.AddElements(new[]{b});
            }
            return m;
        }
        var models=new[]{Model(0),Model(1)};var before=models.Select(ModelArchive.Write).ToArray();
        var aligned=BridgeMergeValidation.Align(models,1e-6);Require(aligned[1][0].BeamId=="B1","Beam matching still depends on list order.");
        var reversed=ModelArchive.Clone(models[1]);(reversed.Beams[0].PointFrom,reversed.Beams[0].PointTo)=(reversed.Beams[0].PointTo,reversed.Beams[0].PointFrom);
        bool rejected=false;try{BridgeMergeValidation.Align(new[]{models[0],reversed},1e-6);}catch(ArgumentException){rejected=true;}Require(rejected,"Reversed local axes were accepted.");
        using var doc=new GH_Document();var assembly=Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.BuildBridgeModel",true));c.CreateAttributes();doc.AddObject(c,false);
        var param=(Param_GenericObject)c.Params.Input[0];foreach(var model in models){var type=assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model",true);var g=(IGH_Goo)Activator.CreateInstance(type);type.GetProperty("Value").SetValue(g,model);param.PersistentData.Append(g);}
        var combos=(Param_GenericObject)c.Params.Input[1];foreach(string s in new[]{"","1-G","2-G","1-S","2-S"})combos.PersistentData.Append(new GH_String(s),new GH_Path(0));foreach(string s in new[]{"123","1","1","1","1"})combos.PersistentData.Append(new GH_String(s),new GH_Path(1));
        void Cases(int port,params string[] cases){var p=(Param_String)c.Params.Input[port];p.PersistentData.Clear();foreach(var s in cases)p.PersistentData.Append(new GH_String(s));p.ExpireSolution(false);}
        Cases(2,"1-G");Cases(3,"2-G");Cases(5,"1-S","2-S");
        void Solve(){c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);}
        Solve();Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,string.Join(";",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        var merged=(ModelModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        foreach(var beam in merged.Beams)
        {
            var f=beam.Results.OfType<BeamForceResultModel>().Single(r=>r.LoadCase.Name=="2-G" && r.Station==5000);
            Require(!f.NormalizedLength && Math.Abs(f.AxialForce-(beam.BeamId=="B1"?210.5:220.5))<1e-12,"Wrong beam identity or normalized station after merge.");
            var strains=beam.Loads.OfType<CompositeBeamSlabStrainModel>().OrderBy(l=>l.LoadCase.Name).ToArray();
            Require(strains.Length==2 && strains[0].LoadCase.Name=="1-S" && strains[0].Value.Strain==-.0001 && strains[1].LoadCase.Name=="2-S" && strains[1].Value.Strain==-.0002,"Duplicate strain names lost their source model.");
        }
        Require(merged.LoadCombinations.Single().Values.Count==4,"Numeric combination name was mistaken for a coefficient.");
        Require(models.Select((m,i)=>ModelArchive.Write(m).SequenceEqual(before[i])).All(v=>v),"Merge mutated source models.");
        Cases(3);Cases(7,"2-G");Solve();Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,"Shrinkage force phase rejected.");
        var withShrinkage=(ModelModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();Require(withShrinkage.LoadCases.Single(l=>l.Name=="2-G").Stages.Single().BridgePhase==LoadCaseModel.Stage.BridgePhases.NShrinkage,"Shrinkage FEM silently merged into N infinite.");
        Cases(7);Cases(3,"2-G");
        Cases(4,"1-G");Solve();Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(e=>e.Contains("more than one phase")),"Ambiguous phase assignment accepted.");
        Cases(4,"9-MISSING");Solve();Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(e=>e.Contains("Unknown phase")),"Unknown selected case accepted.");
        Cases(4);Cases(3);Solve();Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(e=>e.Contains("exactly one phase")),"Active combination with unassigned phase accepted.");
    }
}
