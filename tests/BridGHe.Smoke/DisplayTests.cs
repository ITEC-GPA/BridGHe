using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Helpers;
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
        // R6.6 R2a: the same beam given by its GPC draft alone (B1, 12 m along X, the homogenised property n 12 «Shrink» of the composite section
        // with the slab 250 × 3000 and the two bar rows, 2000 at cover 40 and 1500 at cover 55, Consider Rebar): the preview component, the
        // preferences and the datum unchanged (the SHA-256 of its GPC archive).
        var numbers=R2aPropertyFixtures.Grillage("Shrink") with { SlabHeight=250, SlabWidth=3000, TopArea=2000, TopCover=40, BottomArea=1500, BottomCover=55, ConsiderRebar=true };
        var shrink=BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Homogenised(GpcCompositeDefinitions.Create(numbers),12,"",GpcCompositeDefinitions.AnalysisSteel(numbers),"Steel");
        var draft=BridGHe.Common.Gpc.Builders.GpcBeamBuilders.New(new GPC.Geometry.Point3d(0,0,0),new GPC.Geometry.Point3d(12000,0,0),shrink,null,0,0,0,R2aDesignFixtures.Id("display:B1"),"B1");
        Require(draft.Property.Section.CompositePreview && draft.Beam.BeamProperty.Name=="Shrink","The GPC beam must carry the homogenised property with its composite preview.");
        // P2CA (13/11/2026): the schematic reinforcement rows on the GPC draft (ModelPreview.DraftReinforcementLines, P11 of P2D), the same expected
        // values as the former check on the FEM beam (six bar lines of 12 m, the two rows).
        var lines=(Line[])renderer.GetMethod("DraftReinforcementLines").Invoke(null,new object[]{draft});Require(lines.Length==6 && lines.All(l=>Math.Abs(l.Length-12000)<1e-8),"Two schematic reinforcement rows must survive the generic override.");
        var beam=R2aDesignFixtures.Beam(draft);
        byte[] Archive(){var a=BridGHe.Common.Gpc.GpcDraftArchive.Write(draft);return a.Data.Concat(a.Records).ToArray();}
        var bytes=Archive();
        try
        {
            using var doc=new GH_Document();
            GH_Component Component(string name){var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.Preview."+name,true));c.CreateAttributes();doc.AddObject(c,false);return c;}
            var settings=Component("DisplaySettingsComponent");var preview=Component("PreviewModelComponent");
            var data=(Param_GenericObject)preview.Params.Input[0];data.PersistentData.Append(beam);preview.Params.Input[1].AddSource(settings.Params.Output[0]);
            GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
            Require(preview.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0 && preview.ClippingBox.IsValid,"Local preview component failed.");
            Require(ReferenceEquals(original,preferences.GetProperty("Current").GetValue(null)),"Local display settings changed global preferences.");
            Require(Archive().SequenceEqual(bytes),"Display components modified FEM data.");
            var global=Options();Set(global,"Automatic",false);preferences.GetMethod("Set").Invoke(null,new[]{global,(object)false});
            Require(!(bool)renderer.GetProperty("Automatic").GetValue(null),"Global automatic preview switch ignored.");
            if(!Environment.GetCommandLineArgs().Contains("--preview-capture"))return;
            // Render the same two-row beam through Rhino's real display pipeline.
            var renderDoc=RhinoDoc.ActiveDoc??RhinoDoc.Create(null);
            var view=renderDoc.Views.ActiveView??renderDoc.Views.Add("BridGHe Preview Test",DefinedViewportProjection.Perspective,new System.Drawing.Rectangle(0,0,1200,700),false);
            view.ActiveViewport.SetCameraDirection(new Vector3d(-1,-1,-.5),true);view.ActiveViewport.ZoomBoundingBox(preview.ClippingBox);
            Exception drawingError=null;
            EventHandler<DrawEventArgs> draw=(_,e)=>{try{renderer.GetMethod("DraftMeshes").Invoke(null,new[]{(object)draft,e.Display,options});renderer.GetMethod("DraftWires").Invoke(null,new[]{(object)draft,e.Display,e.Viewport,System.Drawing.Color.DarkBlue,options});}catch(Exception ex){drawingError=ex;}};
            DisplayPipeline.PostDrawObjects+=draw;
            try{using var bitmap=DisplayPipeline.DrawToBitmap(view.ActiveViewport,1200,700);Require(bitmap!=null,"Rhino returned no viewport image.");Directory.CreateDirectory("artifacts/preview");bitmap.Save("artifacts/preview/composite-beam.png");}
            finally{DisplayPipeline.PostDrawObjects-=draw;}
            if(drawingError!=null)throw drawingError;
            Require(Archive().SequenceEqual(bytes),"Rendering modified the FEM model.");
        }
        finally{preferences.GetMethod("Set").Invoke(null,new[]{original,(object)false});}
    }
}
