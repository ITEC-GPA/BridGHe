using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;

internal static class DisplayTests
{
    internal static void Run()
    {
        void Require(bool test,string message){if(!test)throw new Exception(message);}
        var assembly=Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var optionsType=assembly.GetType("BridGHe.Grasshopper.Display.DisplayOptions",true);
        var preferences=assembly.GetType("BridGHe.Grasshopper.Display.DisplayPreferences",true);
        var renderer=assembly.GetType("BridGHe.Grasshopper.Display.ModelPreview",true);
        var original=preferences.GetProperty("Current").GetValue(null);
        object Options()=>Activator.CreateInstance(optionsType);
        void Set(object options,string name,object value)=>optionsType.GetProperty(name).SetValue(options,value);
        var options=Options();Set(options,"LoadCase"," G1 ");
        Require((bool)optionsType.GetMethod("Matches").Invoke(options,new object[]{"g1"}) && !(bool)optionsType.GetMethod("Matches").Invoke(options,new object[]{"G2"}),"Exact load case filter failed.");
        foreach(var (name,value) in new[]{("SymbolScale",0.0),("LoadScale",double.NaN),("Transparency",1.1)})
        {var bad=Options();Set(bad,name,value);bool failed=false;try{optionsType.GetMethod("Validate").Invoke(bad,null);}catch(TargetInvocationException e)when(e.InnerException is ArgumentException){failed=true;}Require(failed,"Invalid display option accepted: "+name);}
        var p=BridgeGrillageExample.Composite(new());p.ConcreteSection=new(250,3000,2000,40,1500,55);p.ConsiderRebar=true;
        var beam=new BeamModel("B1",Point3d.Origin,new Point3d(12000,0,0),p.CreateHomogenizedProperty(12,"Shrink"));
        var lines=(Line[])renderer.GetMethod("ReinforcementLines").Invoke(null,new object[]{beam});Require(lines.Length==6 && lines.All(l=>Math.Abs(l.Length-12000)<1e-8),"Two schematic reinforcement rows must survive the generic override.");
        var bytes=ModelArchive.Write(beam);
        try
        {
            using var doc=new GH_Document();
            GH_Component Component(string name){var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.Preview."+name,true));c.CreateAttributes();doc.AddObject(c,false);return c;}
            var settings=Component("DisplaySettingsComponent");var preview=Component("PreviewModelComponent");
            var data=(Param_GenericObject)preview.Params.Input[0];data.PersistentData.Append(new GH_ObjectWrapper(beam));preview.Params.Input[1].AddSource(settings.Params.Output[0]);
            GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
            Require(preview.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0 && preview.ClippingBox.IsValid,"Local preview component failed.");
            Require(ReferenceEquals(original,preferences.GetProperty("Current").GetValue(null)),"Local display settings changed global preferences.");
            Require(ModelArchive.Write(beam).SequenceEqual(bytes),"Display components modified FEM data.");
            var global=Options();Set(global,"Automatic",false);preferences.GetMethod("Set").Invoke(null,new[]{global,(object)false});
            Require(!(bool)renderer.GetProperty("Automatic").GetValue(null),"Global automatic preview switch ignored.");
            if(!Environment.GetCommandLineArgs().Contains("--preview-capture"))return;
            // Render the same two-row beam through Rhino's real display pipeline.
            var renderDoc=RhinoDoc.ActiveDoc??RhinoDoc.Create(null);
            var view=renderDoc.Views.ActiveView??renderDoc.Views.Add("BridGHe Preview Test",DefinedViewportProjection.Perspective,new System.Drawing.Rectangle(0,0,1200,700),false);
            view.ActiveViewport.SetCameraDirection(new Vector3d(-1,-1,-.5),true);view.ActiveViewport.ZoomBoundingBox(preview.ClippingBox);
            Exception drawingError=null;
            EventHandler<DrawEventArgs> draw=(_,e)=>{try{renderer.GetMethod("Meshes").Invoke(null,new[]{(object)beam,e.Display,options});renderer.GetMethod("Wires").Invoke(null,new[]{(object)beam,e.Display,e.Viewport,System.Drawing.Color.DarkBlue,options});}catch(Exception ex){drawingError=ex;}};
            DisplayPipeline.PostDrawObjects+=draw;
            try{using var bitmap=DisplayPipeline.DrawToBitmap(view.ActiveViewport,1200,700);Require(bitmap!=null,"Rhino returned no viewport image.");Directory.CreateDirectory("artifacts/preview");bitmap.Save("artifacts/preview/composite-beam.png");}
            finally{DisplayPipeline.PostDrawObjects-=draw;}
            if(drawingError!=null)throw drawingError;
            Require(ModelArchive.Write(beam).SequenceEqual(bytes),"Rendering modified the FEM model.");
        }
        finally{preferences.GetMethod("Set").Invoke(null,new[]{original,(object)false});}
    }
}
