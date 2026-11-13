using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>Regression build-model-parameters: Build Model with a standalone (empty) optional Staged constructions parameter solves, keeps the
/// assembled model and survives a save and reopen. R6.6 R0 (GPC_PILOT): the beam is given by its GPC draft alone (Beam builder, no FEM value):
/// the model is a GPC-only model (no FEM value) with one beam and its two end nodes, and the model reopened is the same model (identity and
/// content).</summary>
internal static class BuildModelParameterTests
{
    internal static void Run()
    {
        var plugin=BridgeMergeTests.Initialize();
        using var doc=new GH_Document();
        var component=(GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name=="BuildModelComponent"));
        component.CreateAttributes(); doc.AddObject(component,false);
        var beamType=plugin.GetTypes().Single(t=>t.Name=="GH_Beam");
        var goo=(IGH_Goo)beamType.GetMethod("FromGpc").Invoke(null,new object[]{GpcFixtures.SingleBeam(),null});
        var elements=new Param_GenericObject(); elements.CreateAttributes(); elements.PersistentData.Append(goo); doc.AddObject(elements,false);
        component.Params.Input[0].AddSource(elements);
        var stages=new Param_GenericObject(); stages.CreateAttributes(); doc.AddObject(stages,false); component.Params.Input[4].AddSource(stages);
        string identity=null;
        void Check(GH_Document d)
        {
            GH_Document.EnableSolutions=true; d.Enabled=true; d.NewSolution(false,GH_SolutionMode.Silent);
            var c=d.Objects.OfType<GH_Component>().Single();
            var errors=c.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            if(errors.Count>0) throw new Exception(string.Join("\n",errors));
            var output=c.Params.Output[0].VolatileData.AllData(true).Single();
            if(output.GetType().GetProperty("Value").GetValue(output)!=null) throw new Exception("A beam given by its GPC draft alone must give a GPC-only model.");
            var model=((BridGHe.Common.Gpc.GpcModels.Conversion)output.GetType().GetProperty("GpcInput").GetValue(output)).Model;
            if(model.BeamElements.Count!=1 || model.NodesElements.Count!=2 || c.Params.Input[4].Sources.Single().Attributes.GetTopLevel.DocObject is GH_Component)
                throw new Exception("Standalone optional phase parameter must preserve the assembled model.");
            string now=model.Guid.ToString("D")+"|"+GPC.Model.Models.ModelRevisions.AnalysisFingerprint(model);
            if(identity!=null&&identity!=now) throw new Exception("The reopened definition must give the same GPC model (identity and content).");
            identity=now;
        }
        Check(doc);
        string path=Path.GetFullPath("artifacts/build-model-parameters.ghx"); GrasshopperBridgeExample.Write(doc,path);
        using var reopened=GrasshopperBridgeExample.Read(path); Check(reopened);
        Console.WriteLine("BUILD MODEL PARAMETERS PASS: standalone optional staged-construction input solves and survives reopen.");
    }
}
