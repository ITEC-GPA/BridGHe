using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Checks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.BridgeChecks;

public abstract class BridgeStiffenerPlotComponent : BridgePlotComponent
{
    protected abstract StiffenerPlotRole Role { get; }
    protected override string[] Quantities => BridgeStiffenerPlotChecks.Choices(Role);
    protected override int DefaultQuantity => 0;
    protected override bool DiscreteSamples => true;
    private CheckPlotDisplay display;
    private string unit;
    private readonly List<double> requestedStations = new();
    private readonly GH_Structure<GH_String> governingChecks = new(), unavailableChecks = new();
    private bool configuringList;
    private int missingResults;
    protected override string PlotNote => missingResults>0?$"WR incompleti/non disponibili: {missingResults} risultati stazione/combinazione. Lacune, non zeri; consultare Unavailable checks.":null;

    protected BridgeStiffenerPlotComponent(string name,string nick,string description) : base(name,nick,description)
    { Params.ParameterSourcesChanged += (_,_) => ConfigureValueList(); }

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        base.RegisterInputParams(p);
        p[3].Name="Check";p[3].NickName="Check";
        p[3].Description="0: maximum native WR of all applicable checks. Other choices select one gPC check. Connect a Value List to populate choices automatically.";
        p.AddIntegerParameter("Display","D","0 WR; 1 demand; 2 resistance/limit; 3 demand and resistance/limit. The all-check envelope always uses WR, never mixed physical units.",GH_ParamAccess.item,0);
        foreach(var (name,index) in new[]{("WR",0),("Domanda",1),("Resistenza / limite",2),("Domanda e resistenza / limite",3)})
            ((Param_Integer)p[9]).AddNamedValue(name,index);
        p.AddNumberParameter("Stations","S","Actual stiffener locations [m], measured along the ordered Super-Element Beam from its start. Must match existing result stations within 0.000001 m; no interpolation. At a beam joint both sides are retained.",GH_ParamAccess.list);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        base.RegisterOutputParams(p);
        p.AddTextParameter("Governing checks","Gov","Native check(s) governing each location and combination; original {beam;station} paths. Ties are retained.",GH_ParamAccess.tree);
        p.AddTextParameter("Unavailable checks","Missing","Missing native WR or absent checks, with gPC notes. An incomplete all-check envelope is a gap, never zero or an approval. Original paths and combination order.",GH_ParamAccess.tree);
    }
    public override void AddedToDocument(GH_Document document)
    { base.AddedToDocument(document);ConfigureValueList(); }
    private void ConfigureValueList()
    {
        if(configuringList||Params.Input.Count<4)return;
        configuringList=true;
        try
        {
            var choices=Quantities;
            foreach(var list in Params.Input[3].Sources.OfType<GH_ValueList>())
            {
                if(list.ListMode==GH_ValueListMode.DropDown && list.SelectedItems.Count==1 && list.ListItems.Count==choices.Length
                    && list.ListItems.Select((v,i)=>v.Name==choices[i]&&v.Expression==i.ToString(CultureInfo.InvariantCulture)).All(v=>v))continue;
                int selected=Array.FindIndex(choices,n=>list.ListItems.Any(v=>v.Selected&&v.Name==n));
                list.ListMode=GH_ValueListMode.DropDown;list.ListItems.Clear();
                for(int i=0;i<choices.Length;i++)list.ListItems.Add(new GH_ValueListItem(choices[i],i.ToString(CultureInfo.InvariantCulture)){Selected=i==Math.Max(0,selected)});
                list.ExpireSolution(false);Params.Input[3].ExpireSolution(false);
            }
        }
        finally{configuringList=false;}
    }
    protected override void BeforeRead(IGH_DataAccess da,int quantity)
    {
        int mode=0;da.GetData(9,ref mode);
        if(!Enum.IsDefined((CheckPlotDisplay)mode))throw new ArgumentException("Display must be 0 WR, 1 demand, 2 resistance/limit or 3 comparison.");
        display=quantity==0?CheckPlotDisplay.Utilization:(CheckPlotDisplay)mode;
        unit=display==CheckPlotDisplay.Utilization?"WR":null;
        governingChecks.Clear();unavailableChecks.Clear();requestedStations.Clear();missingResults=0;
        da.GetDataList(10,requestedStations);
        if(requestedStations.Count==0||requestedStations.Any(s=>!double.IsFinite(s)||s<0))
            throw new ArgumentException("Supply actual stiffener Stations in metres from the start of this super-element.");
    }
    private static bool Matches(double a,double b)=>Math.Abs(a-b)<=1e-6;
    protected override bool AcceptStation(double station)=>requestedStations.Any(s=>Matches(s,station));
    protected override Reading ReadValue(object result,int quantity,GH_Path path,string combination)
    {
        if(result is not BridgeStressResult r)throw new ArgumentException("Expected a cumulative gPC BridgeStressResult from Detail Check.");
        var reading=BridgeStiffenerPlotChecks.Read(r,Role,quantity==0?null:Quantities[quantity],display);
        if(display!=CheckPlotDisplay.Utilization && reading.Unit!="—")
        {
            if(unit!=null&&unit!=reading.Unit)throw new ArgumentException("Selected gPC check has inconsistent units between results.");
            unit=reading.Unit;
        }
        governingChecks.Append(new GH_String(reading.Governing.Length==0?"":combination+" / "+r.Stage.Name+" / "+reading.Governing),path);
        unavailableChecks.Append(new GH_String(reading.Unavailable.Length==0?"":combination+" / "+r.Stage.Name+" / "+reading.Unavailable),path);
        return new(reading.Value,reading.Limit);
    }
    protected override void CompleteRead(IGH_DataAccess da,IEnumerable<double> stations)
    {
        da.SetDataTree(4,governingChecks);da.SetDataTree(5,unavailableChecks);
        missingResults=unavailableChecks.AllData(true).Count(s=>!string.IsNullOrEmpty(s.ScriptVariable() as string));
        if(missingResults>0)AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,$"{missingResults} location/combination results have no complete numeric WR. See Unavailable checks; gaps are not zero.");
        var available=stations.ToArray();
        var unmatched=requestedStations.Where(s=>!available.Any(x=>Matches(s,x))).Distinct().ToArray();
        if(unmatched.Length>0)throw new ArgumentException("No result station at [m]: "+string.Join(", ",unmatched.Select(s=>s.ToString("G12",CultureInfo.InvariantCulture)))+". Add the required FEM output stations and recalculate the checks; this plot does not interpolate.");
    }
    protected override string Unit(int quantity)=>unit??"—";
    protected override string PrimaryLabel(int quantity)=>display==CheckPlotDisplay.DemandAndResistance?" / Ed":"";
    protected override string LimitLabel(int quantity)=>display==CheckPlotDisplay.DemandAndResistance?" / Rd":null;
    protected override IEnumerable<double> FixedLevels(int quantity)=>display==CheckPlotDisplay.Utilization?new[]{1.0}:Array.Empty<double>();
}

public sealed class BridgeIntermediateStiffenerPlotComponent : BridgeStiffenerPlotComponent
{
    public BridgeIntermediateStiffenerPlotComponent() : base("Bridge Intermediate Stiffener Plot","IntStiffPlot","Plot individual native gPC checks or their maximum WR at actual intermediate stiffener stations. Missing WR makes the envelope incomplete.") { }
    public override Guid ComponentGuid => new Guid("712219f9-3d5a-4ab1-9c85-68cf18287a8f");
    protected override StiffenerPlotRole Role => StiffenerPlotRole.Intermediate;
}
public sealed class BridgeSupportStiffenerPlotComponent : BridgeStiffenerPlotComponent
{
    public BridgeSupportStiffenerPlotComponent() : base("Bridge Support Stiffener Plot","SupStiffPlot","Plot individual native gPC bearing/end-post checks or their maximum WR at actual bearing stiffener stations. Includes both end-post pairs when enabled.") { }
    public override Guid ComponentGuid => new Guid("f1eb442d-9a41-4f39-b48c-46a08c811db9");
    protected override StiffenerPlotRole Role => StiffenerPlotRole.Support;
}
