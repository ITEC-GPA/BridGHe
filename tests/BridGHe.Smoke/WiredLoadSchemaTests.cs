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
            // R6.6 R2a: the plate, the beam and the load case given by their GPC data alone (no FEM value): the component takes the GPC path.
            if(patch)
            {
                var patchProperty=BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Plate("P",5,null,0,0);
                var plate=BridGHe.Common.Gpc.Builders.GpcPlateBuilders.Create(new[]{(0.0,0.0,0.0),(1000.0,0.0,0.0),(1000.0,1000.0,0.0),(0.0,1000.0,0.0)},patchProperty,null,0,0,"",R2aDesignFixtures.Id("wired:plate"));
                ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append(R2aDesignFixtures.Plate(plate));
            }
            else
            {
                var property=BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Beam("P",5,BridGHe.Common.Gpc.Builders.GpcSectionBuilders.Standard(BridGHe.Common.Gpc.BridgeSectionKind.SolidCircle,100),
                    R2aDesignFixtures.Material("Undefined","S",210000,.3));
                var beam=BridGHe.Common.Gpc.Builders.GpcBeamBuilders.New(new GPC.Geometry.Point3d(0,0,0),new GPC.Geometry.Point3d(1000,0,0),property,null,0,0,0,R2aDesignFixtures.Id("wired:beam"));
                ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append(R2aDesignFixtures.Beam(beam));
                ((Param_GenericObject)c.Params.Input[1]).PersistentData.Append(R2aDesignFixtures.Case(BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("Q",false)));
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
                // The action read on the GPC datum of the output (no FEM value): patch type 3; one linear piece of the distributed action
                // from PA = 10 at the start to PB = 20 at the end (schema 1, Keystone: PA at a = 0 to PB at 1 - b = 1), force or moment only.
                var output=restored.Params.Output[0].VolatileData.AllData(true).Single();
                if(R2aDesignFixtures.HasFem(output)) throw new Exception(name+": the output must have no FEM value.");
                if(patch)
                {
                    var draft=R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacyPlateDraft>(output);
                    if((int)((GPC.Model.Structure.Assignments.ShellPhysicalData)draft.Plate.Assignments.Physical).LoadPatch.Kind!=3) throw new Exception("Wrong patch schema.");
                }
                else
                {
                    var draft=R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacyBeamDraft>(output);
                    bool moment=name=="BeamDistributedMomentComponent";
                    double Force(GPC.Model.Loads.LineLoad l)=>Math.Sqrt(l.F1*l.F1+l.F2*l.F2+l.F3*l.F3);
                    double Moment(GPC.Model.Loads.LineLoad l)=>Math.Sqrt(l.M1*l.M1+l.M2*l.M2+l.M3*l.M3);
                    var piece=draft.Beam.Assignments.Loads.OfType<GPC.Model.Loads.Assignments.BeamLoadAssignment>().Single();
                    var (s,e)=(piece.StartIntensity,piece.EndIntensity);
                    if(draft.Records.Loads!=1 || draft.Records.LoadCases.Single().Name!="Q" || piece.Start!=0 || piece.End!=1
                        || Math.Abs((moment?Moment(s):Force(s))-10)>1e-9 || Math.Abs((moment?Moment(e):Force(e))-20)>1e-9 || (moment?Force(s)+Force(e):Moment(s)+Moment(e))!=0)
                        throw new Exception("Changed distributed action.");
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
