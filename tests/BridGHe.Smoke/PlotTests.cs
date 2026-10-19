using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BridGHe.Common.Plots;

internal static class PlotTests
{
    internal static void Run()
    {
        var samples=new[]{new PlotPoint(0,12),new PlotPoint(1,14),new PlotPoint(1,-5),new PlotPoint(2,double.NaN),new PlotPoint(3,9)};
        var plot=new BridgePlot("Bridge <A> & B","Station [mm]","τ [MPa]",new[]{new PlotSeries("Q<1",samples)});
        string svg=plot.Svg();var xml=XDocument.Parse(svg);
        if(!svg.Contains("&lt;A&gt; &amp; B")||!svg.Contains("τ")||plot.Series[0].Points.Count!=5||plot.Series[0].Points[2].Y!=-5)
            throw new Exception("Plot escaped labels or original sample preservation failed.");
        XNamespace ns="http://www.w3.org/2000/svg";
        var curve=xml.Descendants(ns+"path").Single(e=>e.Element(ns+"title")?.Value=="Q<1").Attribute("d").Value;
        if(curve.Count(c=>c=='M')!=2||curve.Count(c=>c=='L')!=2)throw new Exception("Plot must retain gaps and duplicate-position jumps.");
        var culture=System.Globalization.CultureInfo.CurrentCulture;
        try{System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo("it-IT");if(plot.Svg()!=svg)throw new Exception("Plot depends on decimal culture.");}
        finally{System.Globalization.CultureInfo.CurrentCulture=culture;}
        XDocument.Parse(new BridgePlot("Single","x","y",new[]{new PlotSeries("Q",new[]{new PlotPoint(1,0)})}).Svg());
        bool rejected=false;try{new BridgePlot("","","",new[]{new PlotSeries("",new[]{new PlotPoint(1,double.NaN)})});}catch(ArgumentException){rejected=true;}
        if(!rejected)throw new Exception("All unavailable results must fail explicitly.");
        Directory.CreateDirectory("artifacts/plots");File.WriteAllText("artifacts/plots/regression.svg",svg);
        using(var bitmap=plot.Png())bitmap.Save("artifacts/plots/regression.png",System.Drawing.Imaging.ImageFormat.Png);
        var bands=new[]{new[]{new PlotRange(0,2,4),new PlotRange(1,3,7),new PlotRange(2,double.NaN,double.NaN),new PlotRange(3,-2,6)},new[]{new PlotRange(3,-5,4),new PlotRange(4,-4,8)}};
        var engineering=new BridgePlot("BridGHe - inviluppo e progressive","Station [m]","N [kN]",new[]{new PlotSeries("SLS",samples)},bands,new[]{0.0,3.0,4.0});
        var drawing=XDocument.Parse(engineering.Svg());
        if(drawing.Descendants(ns+"polygon").Count()!=3||drawing.Descendants(ns+"path").Count(e=>(string)e.Attribute("class")=="element-boundary")!=3)
            throw new Exception("Envelope must split gaps and beam boundaries.");
        if(engineering.Envelope[0][0].Minimum!=2||!drawing.Root.Value.Contains("Min -5")||!drawing.Root.Value.Contains("Max 8"))
            throw new Exception("Envelope extrema must preserve positive minima and global values.");
        using(var bitmap=engineering.Png())bitmap.Save("artifacts/plots/engineering.png",System.Drawing.Imaging.ImageFormat.Png);
        File.WriteAllText("artifacts/plots/engineering.svg",engineering.Svg());
        Components();
        Console.WriteLine("PLOT: gaps, jumps, Unicode, escaping, culture, single point and unavailable results verified.");
    }

    private static void Components()
    {
        var assembly=System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        using var doc=new Grasshopper.Kernel.GH_Document();
        var component=(Grasshopper.Kernel.GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.BridgeForcePlotComponent",true));
        component.CreateAttributes();doc.AddObject(component,false);
        var model=new BridGHe.Common.Models.ModelModel(BridGHe.Common.Models.ModelModel.ModelUnits.Nmm);
        model.AddLoadCombinations(new[]{new BridGHe.Common.Models.LoadCombinationModel("Positive"),new BridGHe.Common.Models.LoadCombinationModel("Negative")});
        model.AddElements(new[]{new BridGHe.Common.Models.NodeModel("1",Rhino.Geometry.Point3d.Origin)});
        var modelGoo=(Grasshopper.Kernel.Types.IGH_Goo)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model",true));
        modelGoo.GetType().GetProperty("Value").SetValue(modelGoo,model);
        var beam=new BridGHe.Common.Models.CompositeBeamModel { PointFrom=Rhino.Geometry.Point3d.Origin, PointTo=new Rhino.Geometry.Point3d(3000,0,0) };
        var load=new BridGHe.Common.Models.LoadCaseModel("1","Q");
        foreach(int s in new[]{0,1000,3000})beam.Results.Add(new BridGHe.Common.Models.BeamForceResultModel(load,s,0,""));
        var beamGoo=(Grasshopper.Kernel.Types.IGH_Goo)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.GH_CompositeBeam",true),new object[]{beam,null});
        ((Grasshopper.Kernel.Parameters.Param_GenericObject)component.Params.Input[0]).PersistentData.Append(modelGoo);
        ((Grasshopper.Kernel.Parameters.Param_GenericObject)component.Params.Input[1]).PersistentData.Append(beamGoo);
        var data=((Grasshopper.Kernel.Parameters.Param_GenericObject)component.Params.Input[2]).PersistentData;
        var q=(Grasshopper.Kernel.Parameters.Param_Integer)component.Params.Input[3];q.PersistentData.Clear();q.SetPersistentData(0);
        var live=(Grasshopper.Kernel.Parameters.Param_Boolean)component.Params.Input[8];live.PersistentData.Clear();live.SetPersistentData(true);
        void Row(int station,double positive,double negative)
        {
            foreach(double n in new[]{positive,negative})data.Append(new Grasshopper.Kernel.Types.GH_ObjectWrapper(new GPC.Model.Results.ResultBeamForces(n,0,0,0,0,0,GPC.Geometry.CoordinateSystem.Global)),new Grasshopper.Kernel.Data.GH_Path(0,station));
        }
        void Solve()
        {
            component.Params.Input[2].ExpireSolution(false);
            component.ExpireSolution(false);Grasshopper.Kernel.GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,Grasshopper.Kernel.GH_SolutionMode.Silent);
            var errors=component.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error);
            if(errors.Count>0||component.Params.Output[0].VolatileDataCount!=1)throw new Exception("Plot component failed: "+string.Join(";",errors));
        }
        Row(0,12000,-5000);Solve();
        double min=((Grasshopper.Kernel.Types.GH_Number)component.Params.Output[2].VolatileData.AllData(true).Single()).Value;
        if(min!=-5)throw new Exception("Wrong force conversion or minimum in single-branch plot.");
        Row(2,18000,-2000);Solve();
        ((Grasshopper.Kernel.Parameters.Param_String)component.Params.Input[4]).SetPersistentData("Positive");
        var governing=(Grasshopper.Kernel.Parameters.Param_Boolean)component.Params.Input[5];governing.PersistentData.Clear();governing.SetPersistentData(false);Solve();
        min=((Grasshopper.Kernel.Types.GH_Number)component.Params.Output[2].VolatileData.AllData(true).Single()).Value;
        if(min!=12)throw new Exception("Positive envelope must not be replaced with zero.");
        string svg=((Grasshopper.Kernel.Types.GH_String)component.Params.Output[0].VolatileData.AllData(true).Single()).Value;
        File.WriteAllText("artifacts/plots/component.svg",svg);
        if(!svg.Contains("Station [m]")||!svg.Contains("Grey envelope: all combinations")||!svg.Contains("Min -5")||!svg.Contains("Max 18"))
            throw new Exception("Engineering plot must retain all-combination envelope when selecting one curve.");
        var xml=XDocument.Parse(svg);XNamespace ns="http://www.w3.org/2000/svg";
        if(xml.Descendants(ns+"polygon").Count()!=2)throw new Exception("Missing station must split the envelope in the GH plot.");
    }

    internal static void Benchmark()
    {
        const int curves=30,points=500;
        var data=Enumerable.Range(0,curves).Select(c=>new PlotSeries("C"+c,Enumerable.Range(0,points).Select(i=>new PlotPoint(i*100,Math.Sin(i*.01+c)*100)).ToArray())).ToArray();
        var timer=Stopwatch.StartNew();long start=GC.GetAllocatedBytesForCurrentThread();
        var plot=new BridgePlot("After","mm","MPa",data);string svg=plot.Svg();using(var image=plot.Png()) { }
        double afterMs=timer.Elapsed.TotalMilliseconds;long afterBytes=GC.GetAllocatedBytesForCurrentThread()-start;
        File.WriteAllText("artifacts/plots/benchmark.json",System.Text.Json.JsonSerializer.Serialize(new{curves,points,afterMs,afterBytes,svgBytes=svg.Length}));
        Console.WriteLine($"PLOT BENCHMARK {curves*points} samples: SVG+PNG {afterMs:F1} ms/{afterBytes} B.");
    }
}

