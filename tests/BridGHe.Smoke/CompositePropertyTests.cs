using System;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class CompositePropertyTests
{
    internal static void Run()
    {
        void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        void Near(double a,double b)=>Require(Math.Abs(a-b)<=1e-12*Math.Max(1,Math.Abs(b)), $"{a} != {b}");
        var assembly=Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var property=BridgeGrillageExample.Composite(new());property.ConcreteSection=new(250,3000,2000,40,1500,55);property.ConsiderRebar=true;
        Near(property.NShrinkage,property.NInfinite);Near(ModelArchive.Clone(property).NShrinkage,property.NInfinite);
        property.NShrinkage=12;Near(ModelArchive.Clone(property).NShrinkage,12);Near(new CompositeBeamPropertyModel(property).NShrinkage,12);
        using var doc=new GH_Document();
        var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.BeamPropertyCompositeComponent",true));c.CreateAttributes();doc.AddObject(c,false);
        IGH_Goo Wrap(string name,object value){var t=assembly.GetType("BridGHe.Grasshopper.DataTypes."+name,true);var g=(IGH_Goo)Activator.CreateInstance(t);t.GetProperty("Value").SetValue(g,value);return g;}
        void Generic(int i,IGH_Goo g){var p=(Param_GenericObject)c.Params.Input[i];p.PersistentData.Clear();p.PersistentData.Append(g);p.ExpireSolution(false);}
        void Number(int i,double x){var p=(Param_Number)c.Params.Input[i];p.PersistentData.Clear();p.SetPersistentData(x);p.ExpireSolution(false);}
        Generic(1,new GH_ObjectWrapper(property.ConcreteSection));Generic(2,Wrap("Fem.GH_BeamSection",property.SteelSection));Generic(3,Wrap("Fem.GH_Material",property.ConcreteMaterial));Generic(4,Wrap("Fem.GH_Material",property.SteelMaterial));Generic(5,Wrap("Fem.GH_Material",property.RebarMaterial));Number(6,7);Number(7,18);
        void Solve(){c.ExpireSolution(false);GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,string.Join(";",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));}
        BeamPropertyModel Output(int i)=>(BeamPropertyModel)c.Params.Output[i].VolatileData.AllData(true).Single().ScriptVariable();
        Solve();Require(c.Params.Input.Count==11 && c.Params.Output.Count==5 && c.Params.Input[10].Optional,"Shrinkage ports changed existing indices.");
        Near(Output(3).SectionArea,Output(4).SectionArea);Near(Output(3).I11,Output(4).I11);
        var old0=Output(2);var oldInf=Output(3);Number(10,12);Solve();
        Near(Output(2).I11,old0.I11);Near(Output(3).I11,oldInf.I11);
        var expected=property.GetHomogeneizedMechanicalProperties(12);var actual=Output(4);
        Near(actual.SectionArea,expected.areaH/12);Near(actual.I11,expected.J11H/12);Near(actual.I22,expected.J22H/12);Near(actual.J,property.GetOnlySteelMechanicalProperties().Jt);
        Require(actual.Material.Modulus==property.SteelMaterial.Modulus && actual.Section.NativeSection==null && actual.IsValid,"Shrinkage property must be a valid generic steel-referenced Straus property.");
        Near(((CompositeBeamPropertyModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).NShrinkage,12);
        Number(10,0);c.ExpireSolution(false);doc.NewSolution(false,GH_SolutionMode.Silent);Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0 && !c.Params.Output[4].VolatileData.AllData(true).Any(g=>g!=null),"Invalid shrinkage ratio produced a section.");
    }
}
