using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Plots;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class PlotPreviewTests
{
    internal static void Run()
    {
        void Require(bool success, string message) { if (!success) throw new Exception(message); }
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var serverField=typeof(global::Grasshopper.Instances).GetField("m_comServer",BindingFlags.NonPublic|BindingFlags.Static);
        if(serverField.GetValue(null)==null)
        {
            var server=new GH_ComponentServer();serverField.SetValue(null,server);
            var parse=typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance,null,new[]{typeof(Assembly),typeof(string)},null);
            foreach(var library in new[]{typeof(GH_Document).Assembly,assembly})parse.Invoke(server,new object[]{library,library.Location});
        }
        var type = assembly.GetType("BridGHe.Grasshopper.Components.Preview.BridgePlotPreviewComponent", true);
        using var document = new GH_Document();
        var component = (GH_Component)Activator.CreateInstance(type);
        component.CreateAttributes(); component.Attributes.Pivot = new PointF(380, 50); document.AddObject(component, false);
        var input = (Param_String)component.Params.Input[0];
        void SetSvg(params string[] values) { input.PersistentData.Clear(); input.SetPersistentData(values); }
        var index = (Param_Integer)component.Params.Input[1];
        var width = (Param_Integer)component.Params.Input[2];
        void SetIndex(int value) { index.PersistentData.Clear(); index.SetPersistentData(value); }
        void SetWidth(int value) { width.PersistentData.Clear(); width.SetPersistentData(value); }
        var state = type.GetProperty("Preview", hidden).GetValue(component);
        Bitmap Image() => (Bitmap)state.GetType().GetProperty("Image", hidden).GetValue(state);
        void Solve(bool error = false)
        {
            foreach (var parameter in component.Params.Input) parameter.ExpireSolution(false);
            component.ExpireSolution(false); GH_Document.EnableSolutions = true; document.Enabled = true;
            document.NewSolution(false, GH_SolutionMode.Silent);
            var errors = component.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            Require(error ? errors.Count > 0 : errors.Count == 0, "SVG preview: " + string.Join("; ", errors));
        }
        var plot = new BridgePlot("BridGHe — preview σ / τ", "Station [m]", "M [kNm]", new[]
        {
            new PlotSeries("SLS 1", new[]{new PlotPoint(0,0),new PlotPoint(6,120),new PlotPoint(12,-60),new PlotPoint(18,80),new PlotPoint(24,0)}),
            new PlotSeries("SLS 2", new[]{new PlotPoint(0,0),new PlotPoint(6,80),new PlotPoint(12,-90),new PlotPoint(18,120),new PlotPoint(24,0)})
        }, new[]{new[]{new PlotRange(0,0,0),new PlotRange(6,80,120),new PlotRange(12,-90,-60)},new[]{new PlotRange(12,-90,-60),new PlotRange(18,80,120),new PlotRange(24,0,0)}}, new[]{0.0,12.0,24.0});
        string svg = plot.Svg();
        SetSvg(svg); Solve();
        Require(Image() != null && Image().Width == 1200, "Native SVG rendering failed or wrong raster size.");
        Require(((GH_String)component.Params.Output[0].VolatileData.AllData(true).Single()).Value == svg, "Viewer changed the source SVG.");
        int colored = 0;
        for (int x = 0; x < Image().Width; x += 3)
            for (int y = 0; y < Image().Height; y += 3)
            { var p = Image().GetPixel(x,y); if (p.A > 0 && p.R < 245 && p.G < 245 && p.B < 245) colored++; }
        Require(colored > 500, "Native renderer returned a blank plot.");
        Directory.CreateDirectory("artifacts/plots");
        Image().Save("artifacts/plots/svg-preview.png");
        File.WriteAllText("artifacts/plots/svg-preview.svg",svg);
        component.Attributes.PerformLayout();
        Require(component.Attributes.Bounds.Width >= 600 && component.Attributes.Bounds.Height > 300, "Canvas bounds exclude the preview.");
        bool captureCanvas = Environment.GetCommandLineArgs().Contains("--preview-capture");
        if (captureCanvas)
        {
            // A real canvas needs the hidden-window RhinoCore mode selected by --preview-capture.
            // Creating it in NoWindow can terminate the process with exit code zero on Windows.
            GH_Skin.LoadSkin();
            using var canvas = new GH_Canvas();
            using var bitmap = new Bitmap(1100, 700);
            canvas.Size = bitmap.Size; canvas.Document = document; canvas.CreateControl(); canvas.Viewport.Zoom = 1;
            canvas.Refresh(); System.Windows.Forms.Application.DoEvents();
            canvas.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save("artifacts/plots/grasshopper-preview.png"); canvas.Document = null;
        }
        else Console.WriteLine("SKIPPED: GH canvas screenshot (opt in with --preview-capture); SVG pixels and component layout are tested.");
        var cached = Image(); Solve(); Require(ReferenceEquals(cached,Image()), "Unchanged SVG should reuse its raster.");
        foreach(string empty in new[]{"", "  \r\n"})
        {
            SetSvg(empty); Solve(); Require(Image()==null && !component.Params.Output[0].VolatileData.AllData(true).OfType<GH_String>().Any(g=>!string.IsNullOrEmpty(g.Value)),"An empty upstream plot must clear the old preview without a red component.");
            SetSvg(svg); Solve();
        }
        SetSvg("",svg); SetIndex(1); Solve(); Require(Image()!=null,"Empty charts must not shift subsequent indices.");
        SetSvg(svg); SetIndex(0); Solve(); cached=Image();
        SetWidth(800); Solve(); Require(!ReferenceEquals(cached,Image()) && Image().Width == 1600, "Resized preview did not update.");
        string second = svg.Replace("SLS 1", "SLS 3"); SetSvg(svg,second); SetIndex(1); Solve();
        Require(((GH_String)component.Params.Output[0].VolatileData.AllData(true).Single()).Value == second, "Multiple chart selection failed.");
        SetIndex(2); Solve(true); Require(Image() == null, "Invalid index retained a stale plot.");
        SetIndex(0);
        foreach (string invalid in new[]{"C:\\Temp\\graph.svg", "<svg>", "<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///never'>]><svg xmlns='http://www.w3.org/2000/svg'>&x;</svg>",
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'><image href='https://example.invalid/image.png'/></svg>",
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 0 100'/>"})
        { SetSvg(invalid); Solve(true); Require(Image() == null && !component.Params.Output[0].VolatileData.AllData(true).OfType<GH_String>().Any(g=>!string.IsNullOrEmpty(g.Value)), "Invalid SVG retained old preview/output: image="+(Image()!=null)+", outputs="+string.Join(" | ",component.Params.Output[0].VolatileData.AllData(true).Select(g=>g?.ToString()))); }
        SetSvg(svg); SetWidth(0); Solve(true);
        SetWidth(600); Solve();
        // Exercise the enlarged view without opening a visible window during automated tests.
        var windowType=assembly.GetType("BridGHe.Grasshopper.Display.PlotPreviewWindow",true);
        using(var window=(System.Windows.Forms.Form)Activator.CreateInstance(windowType,hidden,null,Array.Empty<object>(),null))
        {
            var update=windowType.GetMethod("UpdatePlot",hidden);update.Invoke(window,new object[]{svg});update.Invoke(window,new object[]{second});update.Invoke(window,new object[]{null});
        }
        input.PersistentData.Clear(); Solve(); Require(Image() == null, "Disconnected/missing SVG retained an old graph.");
        SetSvg(svg); Solve(); document.RemoveObject(component, false);
        Require(Image() == null, "Removing the viewer did not release its bitmap.");
        if(Environment.GetCommandLineArgs().Contains("--write-plot-example"))
        {
            using var example=new GH_Document();
            var source=new Param_String{Name="SVG di esempio",NickName="SVG demo"};source.CreateAttributes();source.Attributes.Pivot=new PointF(50,160);source.SetPersistentData(new[]{svg,second});example.AddObject(source,false);
            var viewer=(GH_Component)Activator.CreateInstance(type);viewer.CreateAttributes();viewer.Attributes.Pivot=new PointF(650,80);example.AddObject(viewer,false);viewer.Params.Input[0].AddSource(source);
            var slider=new global::Grasshopper.Kernel.Special.GH_NumberSlider();slider.CreateAttributes();slider.Attributes.Pivot=new PointF(50,230);slider.Slider.Minimum=0;slider.Slider.Maximum=1;slider.Slider.Type=global::Grasshopper.GUI.Base.GH_SliderAccuracy.Integer;slider.SetSliderValue(0);example.AddObject(slider,false);viewer.Params.Input[1].AddSource(slider);
            example.Enabled=true;example.NewSolution(false,GH_SolutionMode.Silent);
            string file=Path.Combine(ExamplePaths.Case("20-visualizzatore-grafici"),"plot-preview.ghx");Directory.CreateDirectory(Path.GetDirectoryName(file));
            Require(new GH_DocumentIO{Document=example}.SaveQuiet(file),"Could not save preview example.");
            var io=new GH_DocumentIO();Require(io.Open(file),"Could not reopen preview example.");
            using var reopened=io.Document;reopened.Enabled=true;reopened.NewSolution(false,GH_SolutionMode.Silent);
            var restored=reopened.Objects.OfType<GH_Component>().Single(c=>c.ComponentGuid==component.ComponentGuid);
            Require(restored.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0 && restored.Params.Output[0].VolatileDataCount==1,"Preview example did not recalculate after reopening.");
        }
        Console.WriteLine("PLOT PREVIEW: SVG.NET, cache, list selection, resize, invalid input, window and resource release verified; canvas capture=" + captureCanvas + ".");
    }
}
