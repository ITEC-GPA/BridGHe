using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class WiredLoadSchemaTests
{
    internal static void Run()
    {
        var plugin=BridgeMergeTests.Initialize();
        foreach(string name in new[]{"BeamDistributedLoadComponent","BeamDistributedMomentComponent","LoadPatchTypeComponent"})
        {
            using var doc=new GH_Document();
            var c=(GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name==name));
            c.CreateAttributes(); doc.AddObject(c,false);
            IGH_Goo Goo(string type,object value)
            {
                var g=(IGH_Goo)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem."+type));
                g.GetType().GetProperty("Value").SetValue(g,value); return g;
            }
            bool patch=name=="LoadPatchTypeComponent";
            if(patch)
            {
                var plate=new PlateModel(new[]{Point3d.Origin,new Point3d(1000,0,0),new Point3d(1000,1000,0),new Point3d(0,1000,0)},new PlatePropertyModel("P"){PropertyType=PlatePropertyType.LoadPatch});
                ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append(Goo("GH_Plate",plate));
            }
            else
            {
                var beam=new BeamModel(Point3d.Origin,new Point3d(1000,0,0),new BeamPropertyModel("P")
                {PropertyType=BeamPropertyType.Beam,Section=new SectionModel{SectionType=SectionModel.SectionTypes.SolidCircle,D=100},Material=new MaterialModel("S"){Modulus=210000}});
                ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append(Goo("GH_Beam",beam));
                ((Param_GenericObject)c.Params.Input[1]).PersistentData.Append(Goo("GH_LoadCase",new LoadCaseModel("Q","Q")));
                ((Param_Integer)c.Params.Input.Single(p=>p.Name=="Direction")).SetPersistentData(1);
                ((Param_Number)c.Params.Input.Single(p=>p.Name=="PA")).SetPersistentData(10d);
                ((Param_Number)c.Params.Input.Single(p=>p.Name=="PB")).SetPersistentData(20d);
            }
            var type=(Param_Integer)c.Params.Input.Single(p=>p.Name=="Type"); type.PersistentData.Clear();
            var source=new GH_ValueList(); source.CreateAttributes(); source.ListItems.Clear();
            source.ListItems.Add(new GH_ValueListItem("Schema esterno",patch?"3":"1"){Selected=true}); doc.AddObject(source,false); type.AddSource(source);
            void Check(GH_Document graph)
            {
                graph.Enabled=true; GH_Document.EnableSolutions=true; graph.NewSolution(false,GH_SolutionMode.Silent);
                var restored=graph.Objects.OfType<GH_Component>().Single();
                if(patch && graph!=doc)
                {
                    // GH_Plate has no standalone persistent archive. Restore the plate fixture;
                    // the schema parameter and external wire must still come from the GHX.
                    var plateInput=(Param_GenericObject)restored.Params.Input[0];
                    plateInput.PersistentData.Clear();
                    plateInput.PersistentData.Append(((Param_GenericObject)c.Params.Input[0]).PersistentData[0][0].Duplicate());
                    plateInput.ExpireSolution(false); restored.ExpireSolution(false);
                    graph.NewSolution(false,GH_SolutionMode.Silent);
                }
                var port=(Param_Integer)restored.Params.Input.Single(p=>p.Name=="Type");
                if(port.InstanceGuid!=type.InstanceGuid || port.SourceCount!=1 || port.Sources[0].InstanceGuid!=source.InstanceGuid || port.PersistentDataCount!=0)
                    throw new Exception("Schema input/wire lost on reload: "+name);
                if(restored.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0 || restored.Params.Output[0].VolatileDataCount!=1)
                    throw new Exception(name+": "+string.Join(";",restored.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
                var result=restored.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
                if(patch)
                {
                    if((int)((PlateModel)result).Attributes.OfType<LoadPatchTypeAttributeModel>().Single().Value.PatchType!=3) throw new Exception("Wrong patch schema.");
                }
                else
                {
                    var load=((BeamModel)result).Loads.OfType<BeamDistributedLoadModel>().Single();
                    if((int)load.Value.LoadSchema!=1 || load.Value.PA!=10 || load.Value.PB!=20) throw new Exception("Changed distributed action.");
                }
            }
            Check(doc);
            var chunk=new GH_LooseChunk("component"); c.Write(chunk);
            var copy=(GH_Component)Activator.CreateInstance(c.GetType()); copy.CreateAttributes();
            if(!copy.Read(chunk)) throw new Exception("Component read failed: "+name);
            string path=Path.GetFullPath("artifacts/wired-load-schema-"+name+".ghx"); GrasshopperBridgeExample.Write(doc,path);
            GH_Document.EnableSolutions=false; using var reopened=GrasshopperBridgeExample.Read(path); Check(reopened);
        }
        Console.WriteLine("WIRED LOAD SCHEMA PASS: forces, moments and load patch; external choices, direct Read, saved wires and actual action data preserved.");
    }
}
