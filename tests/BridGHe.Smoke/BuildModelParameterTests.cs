using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class BuildModelParameterTests
{
    internal static void Run()
    {
        var plugin=BridgeMergeTests.Initialize();
        using var doc=new GH_Document();
        var component=(GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name=="BuildModelComponent"));
        component.CreateAttributes(); doc.AddObject(component,false);
        var beam=new BeamModel("1",Point3d.Origin,new Point3d(1000,0,0),BridgeAssignmentTests.Property("P"));
        var goo=(IGH_Goo)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name=="GH_Beam"),new object[]{beam,null});
        var elements=new Param_GenericObject(); elements.CreateAttributes(); elements.PersistentData.Append(goo); doc.AddObject(elements,false);
        component.Params.Input[0].AddSource(elements);
        var stages=new Param_GenericObject(); stages.CreateAttributes(); doc.AddObject(stages,false); component.Params.Input[4].AddSource(stages);
        void Check(GH_Document d)
        {
            GH_Document.EnableSolutions=true; d.Enabled=true; d.NewSolution(false,GH_SolutionMode.Silent);
            var c=d.Objects.OfType<GH_Component>().Single();
            var errors=c.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            if(errors.Count>0) throw new Exception(string.Join("\n",errors));
            var model=(ModelModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
            if(model.Beams.Count!=1 || model.Nodes.Count!=2 || c.Params.Input[4].Sources.Single().Attributes.GetTopLevel.DocObject is GH_Component)
                throw new Exception("Standalone optional phase parameter must preserve the assembled model.");
        }
        Check(doc);
        string path=Path.GetFullPath("artifacts/build-model-parameters.ghx"); GrasshopperBridgeExample.Write(doc,path);
        using var reopened=GrasshopperBridgeExample.Read(path); Check(reopened);
        Console.WriteLine("BUILD MODEL PARAMETERS PASS: standalone optional staged-construction input solves and survives reopen.");
    }
}
