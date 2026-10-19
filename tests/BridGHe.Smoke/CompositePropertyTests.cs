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
        var serverField=typeof(Grasshopper.Instances).GetField("m_comServer",BindingFlags.NonPublic|BindingFlags.Static);
        if(serverField.GetValue(null)==null)
        {
            var server=new GH_ComponentServer();serverField.SetValue(null,server);
            var parse=typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance,null,new[]{typeof(Assembly),typeof(string)},null);
            foreach(var a in new[]{typeof(GH_Document).Assembly,assembly})parse.Invoke(server,new object[]{a,a.Location});
        }
        var property=BridgeGrillageExample.Composite(new());property.ConcreteSection=new(250,3000,2000,40,1500,55);property.ConsiderRebar=true;
        Near(property.NShrinkage,property.NInfinite);Near(ModelArchive.Clone(property).NShrinkage,property.NInfinite);
        property.NShrinkage=12;Near(ModelArchive.Clone(property).NShrinkage,12);Near(new CompositeBeamPropertyModel(property).NShrinkage,12);
        property.SteelMaterial.ThermalExpansion=1.2e-5;
        var shrink=BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(property,-.0002);
        var zero=BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(property,0);
        Require(zero.FreeStrain==0 && zero.Curvature==0,"Zero eigenstrain must generate zero FEM actions.");
        var twice=BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(property,-.0004);
        Near(twice.PreStrain,2*shrink.PreStrain);Near(twice.ThermalGradient,2*shrink.ThermalGradient);
        var shProps=property.CreateHomogenizedProperty(12,"test");
        Near(property.SteelMaterial.Modulus*shProps.SectionArea*shrink.FreeStrain,shrink.EigenForce);
        Near(property.SteelMaterial.Modulus*shProps.I11*shrink.Curvature,shrink.EigenMoment);
        Require(shrink.FreeStrain<0 && shrink.PreStrain>0,"Straus prestrain contraction sign reversed.");
        using var doc=new GH_Document();
        var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.BeamPropertyCompositeComponent",true));c.CreateAttributes();doc.AddObject(c,false);
        IGH_Goo Wrap(string name,object value){var t=assembly.GetType("BridGHe.Grasshopper.DataTypes."+name,true);var g=(IGH_Goo)Activator.CreateInstance(t);t.GetProperty("Value").SetValue(g,value);return g;}
        void Generic(int i,IGH_Goo g){var p=(Param_GenericObject)c.Params.Input[i];p.PersistentData.Clear();p.PersistentData.Append(g);p.ExpireSolution(false);}
        void Number(int i,double x){var p=(Param_Number)c.Params.Input[i];p.PersistentData.Clear();p.SetPersistentData(x);p.ExpireSolution(false);}
        Generic(1,new GH_ObjectWrapper(property.ConcreteSection));Generic(2,Wrap("Fem.GH_BeamSection",property.SteelSection));Generic(3,Wrap("Fem.GH_Material",property.ConcreteMaterial));Generic(4,Wrap("Fem.GH_Material",property.SteelMaterial));Generic(5,Wrap("Fem.GH_Material",property.RebarMaterial));Number(6,7);Number(7,18);
        void Solve(){c.ExpireSolution(false);GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,string.Join(";",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));}
        BeamPropertyModel Output(int i)=>(BeamPropertyModel)c.Params.Output[i].VolatileData.AllData(true).Single().ScriptVariable();
        int ShrinkagePort()=>c.Params.Input.FindIndex(p=>p.Name=="n ritiro");
        Solve();Require(c.Params.Input.Count==11 && c.Params.Output.Count==5 && c.Params.Input[ShrinkagePort()].Optional,"Shrinkage port count or optionality changed.");
        Near(Output(3).SectionArea,Output(4).SectionArea);Near(Output(3).I11,Output(4).I11);
        var old0=Output(2);var oldInf=Output(3);Number(ShrinkagePort(),12);Solve();
        Near(Output(2).I11,old0.I11);Near(Output(3).I11,oldInf.I11);
        var expected=property.GetHomogeneizedMechanicalProperties(12);var actual=Output(4);
        Near(actual.SectionArea,expected.areaH/12);Near(actual.I11,expected.J11H/12);Near(actual.I22,expected.J22H/12);Near(actual.J,property.GetOnlySteelMechanicalProperties().Jt);
        Require(actual.Material.Modulus==property.SteelMaterial.Modulus && actual.Section.NativeSection==null && actual.Section.SectionType==SectionModel.SectionTypes.Generic && actual.IsValid,"Shrinkage property must be a valid generic steel-referenced Straus property.");
        Near(((CompositeBeamPropertyModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).NShrinkage,12);
        // Saved definitions can retain the original end position or the reordered local layout.
        foreach(int port in new[]{10,8})
        {
            var shrinkageParam=c.Params.Input[ShrinkagePort()];c.Params.Input.Remove(shrinkageParam);c.Params.Input.Insert(port,shrinkageParam);c.Params.OnParametersChanged();
            foreach(var p in c.Params.Input)p.ExpireSolution(false);
            Solve();Near(((CompositeBeamPropertyModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).NShrinkage,12);
            Near(Output(4).I11,actual.I11);
            var rebarInput=(Param_Boolean)c.Params.Input.Single(p=>p.Name=="Consider Rebars");
            rebarInput.PersistentData.Clear();rebarInput.SetPersistentData(false);
            var concreteInput=(Param_Boolean)c.Params.Input.Single(p=>p.Name=="Consider Concrete");
            bool wasOptional=concreteInput.Optional;concreteInput.Optional=true;concreteInput.PersistentData.Clear();
            var archive=new GH_IO.Serialization.GH_LooseChunk("Component");Require(c.Write(archive),"Cannot serialize composite component.");
            using(var reopenedDoc=new GH_Document())
            {
                var reopened=(GH_Component)Activator.CreateInstance(c.GetType());reopened.CreateAttributes();
                Require(reopened.Read(archive),"Cannot reopen composite component.");reopenedDoc.AddObject(reopened,false);
                Require(reopened.Params.Input.Select(p=>p.GetType()).SequenceEqual(c.Params.Input.Select(p=>p.GetType())),"Saved parameter order restored with wrong input types.");
                Require(reopened.Params.Input.Select(p=>p.InstanceGuid).SequenceEqual(c.Params.Input.Select(p=>p.InstanceGuid)),"Reopening changed input GUIDs and would break wires.");
                // Geometry/materials normally arrive from upstream components. Supply those
                // again; this roundtrip tests the three saved scalar ports, not GH_ObjectWrapper.
                for(int i=1;i<=5;i++)
                {
                    var input=(Param_GenericObject)reopened.Params.Input[i];input.PersistentData.Clear();
                    foreach(var goo in ((Param_GenericObject)c.Params.Input[i]).PersistentData.AllData(true))input.PersistentData.Append(goo.Duplicate());
                }
                reopenedDoc.Enabled=true;reopenedDoc.NewSolution(false,GH_SolutionMode.Silent);
                Require(reopened.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,string.Join(";",reopened.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
                var savedProperty=(CompositeBeamPropertyModel)reopened.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
                Near(savedProperty.NShrinkage,12);Require(savedProperty.ConsiderConcrete&&!savedProperty.ConsiderRebar,"Reopening swapped the concrete/rebar options.");
            }
            rebarInput.PersistentData.Clear();rebarInput.SetPersistentData(true);rebarInput.ExpireSolution(false);
            concreteInput.Optional=wasOptional;concreteInput.SetPersistentData(true);concreteInput.ExpireSolution(false);
        }
        var beam=new BeamModel("Preview",Rhino.Geometry.Point3d.Origin,new Rhino.Geometry.Point3d(10000,0,0),actual);
        Require(beam.Shapes.Length==2 && beam.Shapes.All(b=>b.IsValid && b.IsSolid),"Override composite preview must contain solid steel and slab.");
        Near(beam.Shapes[0].GetBoundingBox(true).Max.Z,beam.Shapes[1].GetBoundingBox(true).Min.Z);
        var archived=ModelArchive.Clone(beam);Require(archived.Shapes.Length==2 && archived.BeamProperty.Section.CompositePreview.Slab.BottomRebarArea==1500,"Archived generic section lost its physical preview.");
        Near(archived.BeamProperty.I11,actual.I11);
        var asym=ModelArchive.Clone(property);asym.SteelSection.B1=900;asym.SteelSection.B2=400;
        var composite=new CompositeBeamModel(Rhino.Geometry.Point3d.Origin,new Rhino.Geometry.Point3d(10000,0,0),asym);
        Near(composite.Shapes[0].GetBoundingBox(true).Max.Z,composite.Shapes[1].GetBoundingBox(true).Min.Z);
        Number(ShrinkagePort(),0);c.ExpireSolution(false);doc.NewSolution(false,GH_SolutionMode.Silent);Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0 && !c.Params.Output[4].VolatileData.AllData(true).Any(g=>g!=null),"Invalid shrinkage ratio produced a section.");
    }
}
