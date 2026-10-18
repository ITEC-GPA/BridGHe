using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using BridGHe.Common.Plots;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using GPC.Model.Results;

namespace BridGHe.Grasshopper.Components.BridgeChecks;

public abstract class BridgePlotComponent : GH_Component
{
    private bool run;
    private string renderedSvg, renderedPath;
    protected abstract string[] Quantities { get; }
    protected virtual bool ReferenceLines => false;
    protected abstract double Value(object result,int quantity);
    protected abstract string Unit(int quantity);
    protected BridgePlotComponent(string name,string nick,string description):base(name,nick,description,"BridGHe-Checks","Bridge plots") { }
    protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
    public override void CreateAttributes()
    {
        var attributes=new ComponentAttributes.ComponentOneButtonAttributes(this,"Draw");
        attributes.ButtonPressed+=()=>{run=true;ExpireSolution(true);};m_attributes=attributes;
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("BridGHe Model","M","Model with ordered load combinations, N-mm units",GH_ParamAccess.item);
        p.AddGenericParameter("Super-Element Beam","SE","Ordered beam segments; lengths and result stations define the abscissas",GH_ParamAccess.list);
        p.AddGenericParameter("Results","R","Tree {beam index; station index}, items in model combination order",GH_ParamAccess.tree);
        p.AddIntegerParameter("Quantity","Q","Quantity to draw",GH_ParamAccess.item,Math.Min(4,Quantities.Length-1));
        for(int i=0;i<Quantities.Length;i++)((Param_Integer)p[3]).AddNamedValue(Quantities[i],i);
        p.AddTextParameter("Combinations","C","Names to draw; empty selects all",GH_ParamAccess.list);p[4].Optional=true;
        p.AddBooleanParameter("Show Min/Max Combo","MM","Include the combinations governing the global minimum and maximum",GH_ParamAccess.item,true);
        if(ReferenceLines){p.AddNumberParameter("Additional values","AV","Horizontal reference levels in the displayed units",GH_ParamAccess.list);p[6].Optional=true;}
        p.AddTextParameter("File Path","P","Optional .svg or .png destination; SVG is always returned",GH_ParamAccess.item);p[p.ParamCount-1].Optional=true;
        p.AddTextParameter("Graphic Title","T","Chart title",GH_ParamAccess.item,"");
        p.AddBooleanParameter("Keep Running","Run","Draw on every update; otherwise use the Draw button",GH_ParamAccess.item,false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("SVG","SVG","Vector chart, with original samples and no smoothing",GH_ParamAccess.item);
        p.AddTextParameter("File","P","Written destination, if requested",GH_ParamAccess.item);
        p.AddNumberParameter("Minimum","Min","Minimum finite value among plotted series",GH_ParamAccess.item);
        p.AddNumberParameter("Maximum","Max","Maximum finite value among plotted series",GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        GH_Model model=null;var beams=new List<GH_CompositeBeam>();int quantity=0;
        var selected=new List<string>();var levels=new List<double>();bool governing=true,live=false;string file="",title="";
        if(!da.GetData(0,ref model)||!da.GetDataList(1,beams)||!da.GetDataTree(2,out GH_Structure<IGH_Goo> tree)||!da.GetData(3,ref quantity))return;
        da.GetDataList(4,selected);da.GetData(5,ref governing);int offset=6;
        if(ReferenceLines)da.GetDataList(offset++,levels);
        da.GetData(offset++,ref file);da.GetData(offset++,ref title);da.GetData(offset,ref live);
        if(!run&&!live)return;
        run=false;
        try
        {
            if(model?.Value==null||model.Value.Units!=ModelModel.ModelUnits.Nmm)throw new ArgumentException("Plot inputs require an N-mm model.");
            if(quantity<0||quantity>=Quantities.Length)throw new ArgumentOutOfRangeException(nameof(quantity));
            var names=model.Value.LoadCombinations.Select(c=>c.Name).ToArray();
            if(names.Length==0||names.Distinct(StringComparer.Ordinal).Count()!=names.Length)throw new ArgumentException("Distinct combination names are required.");
            var unknown=selected.Except(names,StringComparer.Ordinal).ToArray();if(unknown.Length>0)throw new ArgumentException("Unknown combinations: "+string.Join(", ",unknown));
            var stations=beams.Select(b=>b.Value.Results.OfType<BeamForceResultModel>()
                .Select(f=>Math.Round(f.NormalizedLength?f.Station*b.Value.Length:f.Station,15)).Distinct().OrderBy(x=>x).ToArray()).ToArray();
            var origins=new double[beams.Count];for(int i=1;i<beams.Count;i++)origins[i]=origins[i-1]+beams[i-1].Value.Length;
            var rows=new List<(int Beam,int Station,double X,double[] Values)>();
            for(int i=0;i<tree.PathCount;i++)
            {
                var path=tree.Paths[i].Indices;
                if(path.Length!=2||path[0]<0||path[0]>=beams.Count||path[1]<0||path[1]>=stations[path[0]].Length)
                    throw new ArgumentException("Invalid beam/station path: "+tree.Paths[i]);
                var branch=tree.Branches[i];if(branch.Count!=names.Length)throw new ArgumentException("Combination count mismatch at "+tree.Paths[i]);
                rows.Add((path[0],path[1],origins[path[0]]+stations[path[0]][path[1]],branch.Select(g=>Value(g is GH_ObjectWrapper w?w.Value:g?.ScriptVariable(),quantity)).ToArray()));
            }
            rows=rows.OrderBy(r=>r.Beam).ThenBy(r=>r.Station).ToList();
            var include=new HashSet<string>(selected.Count==0?names:selected,StringComparer.Ordinal);
            var finite=rows.SelectMany(r=>r.Values.Select((v,i)=>(Value:v,Index:i))).Where(p=>double.IsFinite(p.Value)).ToArray();
            if(finite.Length==0)throw new ArgumentException("No finite values for this quantity.");
            if(governing){include.Add(names[finite.MinBy(p=>p.Value).Index]);include.Add(names[finite.MaxBy(p=>p.Value).Index]);}
            var series=new List<PlotSeries>();
            foreach(var group in rows.GroupBy(r=>r.Beam))
                for(int c=0;c<names.Length;c++)if(include.Contains(names[c]))
                {
                    var points=new List<PlotPoint>();int last=-2;
                    foreach(var row in group)
                    {
                        if(last>=0&&row.Station!=last+1)points.Add(new PlotPoint(row.X,double.NaN));
                        points.Add(new PlotPoint(row.X,row.Values[c]));last=row.Station;
                    }
                    series.Add(new PlotSeries(names[c],points));
                }
            var values=series.SelectMany(s=>s.Points).Where(p=>double.IsFinite(p.Y)).ToArray();
            foreach(double level in levels)series.Add(new PlotSeries("Reference "+level.ToString("G5",System.Globalization.CultureInfo.InvariantCulture),new[]{new PlotPoint(values.Min(p=>p.X),level),new PlotPoint(values.Max(p=>p.X),level)}));
            var plot=new BridgePlot(string.IsNullOrWhiteSpace(title)?Quantities[quantity]:title,"Station [mm]",Quantities[quantity]+" ["+Unit(quantity)+"]",series);
            string svg=plot.Svg();
            if(!string.IsNullOrWhiteSpace(file))
            {
                file=Path.GetFullPath(file);string ext=Path.GetExtension(file).ToLowerInvariant();
                if(ext!=".svg"&&ext!=".png")throw new ArgumentException("Choose .svg or .png for the chart.");
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                if(ext==".svg"){if(!File.Exists(file)||File.ReadAllText(file)!=svg)File.WriteAllText(file,svg);}
                else if(renderedSvg!=svg||renderedPath!=file||!File.Exists(file))
                {using var bitmap=plot.Png();bitmap.Save(file,System.Drawing.Imaging.ImageFormat.Png);renderedSvg=svg;renderedPath=file;}
                da.SetData(1,file);
            }
            da.SetData(0,svg);da.SetData(2,values.Min(p=>p.Y));da.SetData(3,values.Max(p=>p.Y));
            Message=$"{values.Length} samples";
        }
        catch(Exception e){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,e.Message);}
    }
}
