using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public abstract class BridgeDefinitionComponent : BridgeUtilityComponent
{
    protected BridgeDefinitionComponent(string name, string nickname, string description) : base(name, nickname, description, CategoryNameConstants.SUBCATEGORY_MODEL) { }
    protected static T Get<T>(IGH_DataAccess da, int index) where T : class
    {
        BridgeDefinitionGoo goo = null;
        if (!da.GetData(index, ref goo)) return null;
        return goo?.Value as T ?? throw new ArgumentException($"Input {index + 1} requires {typeof(T).Name}.");
    }
    protected static double[] Numbers(IGH_DataAccess da, int index, params double[] fallback)
    { var values = new List<double>(); return da.GetDataList(index, values) && values.Count > 0 ? values.ToArray() : fallback; }
    protected static void Output(IGH_DataAccess da, object value) => da.SetData(0, new BridgeDefinitionGoo(value));
}

public sealed class BridgeSupportLayoutComponent : BridgeDefinitionComponent
{
    public BridgeSupportLayoutComponent() : base("Bridge Support Lines", "SupportLines", "Position abutments and piers by distances along the alignment, mm. Geometric lines only; assign restraints separately.") { }
    public override Guid ComponentGuid => new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f001");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Alignment", "A", "Deck reference alignment, mm.", GH_ParamAccess.item);
        p.AddNumberParameter("Support Stations", "S", "Distances along curve, mm. Empty = both ends. Unsorted values are sorted together with their skew angles. End supports are not added when stations are supplied.", GH_ParamAccess.list); p[1].Optional = true;
        p.AddNumberParameter("Skew Angles", "K", "Degrees towards the tangent from the local transverse line. One angle for all supports or one per supplied station.", GH_ParamAccess.list, 0);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    { p.AddGenericParameter("Support Lines", "S", "Geometric support definition; no stiffness or restraints.", GH_ParamAccess.item); p.AddNumberParameter("Span Lengths", "L", "Distances between successive support stations, mm.", GH_ParamAccess.list); p.AddPointParameter("Support Centres", "P", "Support centres on alignment.", GH_ParamAccess.list); }
    protected override void Solve(IGH_DataAccess da)
    {
        Curve curve = null; if (!da.GetData(0, ref curve)) return;
        double length = curve.GetLength(); var stations = Numbers(da, 1, 0, length); var skew = Numbers(da, 2, 0);
        if (skew.Length != 1 && skew.Length != stations.Length) throw new ArgumentException("One skew angle for all supports or one per station is required.");
        var pairs = stations.Select((s, i) => (Station: s, Skew: skew.Length == 1 ? skew[0] : skew[i])).OrderBy(p => p.Station).ToArray();
        if (pairs.Length < 2 || pairs.Any(p => !double.IsFinite(p.Station) || p.Station < 0 || p.Station > length || !double.IsFinite(p.Skew) || Math.Abs(p.Skew) >= 75) ||
            pairs.Zip(pairs.Skip(1), (a, b) => b.Station - a.Station).Any(d => d <= SimpleBridgeBuilder.JoinTolerance))
            throw new ArgumentException("At least two distinct stations within the curve and skew angles between -75 and 75 degrees are required.");
        Output(da, new BridgeSupportLayout(pairs.Select(p => p.Station).ToArray(), pairs.Select(p => p.Skew).ToArray()));
        da.SetDataList(1, pairs.Zip(pairs.Skip(1), (a, b) => b.Station - a.Station));
        da.SetDataList(2, pairs.Select(p => { curve.LengthParameter(p.Station, out double t); return curve.PointAt(t); }));
    }
}

public sealed class BridgeGirderLayoutComponent : BridgeDefinitionComponent
{
    public BridgeGirderLayoutComponent() : base("Bridge Girder Axes", "GirderAxes", "Transverse locations and reference elevations of the longitudinal girder axes, in mm. No sections are assigned.") { }
    public override Guid ComponentGuid => new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f002");
    protected override void RegisterInputParams(GH_InputParamManager p)
    { p.AddNumberParameter("Transverse Offsets", "Y", "2..32 increasing offsets from alignment: negative right, positive left, mm.", GH_ParamAccess.list); p.AddNumberParameter("Axis Levels", "Z", "One level for all girders or one per offset, mm along deck normal. Negative below deck top. These are reference axes, not automatically inferred centroids.", GH_ParamAccess.list, -1500); }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    { p.AddGenericParameter("Girder Axes", "G", "Layout for fabrication and geometry assembly.", GH_ParamAccess.item); p.AddIntegerParameter("Girder Count", "N", "Number of girders.", GH_ParamAccess.item); }
    protected override void Solve(IGH_DataAccess da)
    {
        var offsets = Numbers(da, 0); var levels = Numbers(da, 1, -1500);
        if (offsets.Length < 2 || offsets.Length > 32 || offsets.Any(v => !double.IsFinite(v)) || offsets.Zip(offsets.Skip(1), (a,b) => b-a).Any(v => v <= SimpleBridgeBuilder.JoinTolerance) ||
            levels.Any(v => !double.IsFinite(v)) || levels.Length != 1 && levels.Length != offsets.Length)
            throw new ArgumentException("Provide 2..32 increasing transverse offsets and one finite axis level or one per girder.");
        Output(da, new BridgeGirderLayout(offsets, levels)); da.SetData(1, offsets.Length);
    }
}

public sealed class BridgeRegularGirdersComponent : BridgeDefinitionComponent
{
    public BridgeRegularGirdersComponent() : base("Bridge Regular Girders", "RegularGirders", "Create symmetric transverse offsets from girder count and spacing, mm.") { }
    public override Guid ComponentGuid => new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f003");
    protected override void RegisterInputParams(GH_InputParamManager p)
    { p.AddIntegerParameter("Girder Count", "N", "Number of girders, 2..32.", GH_ParamAccess.item, 3); p.AddNumberParameter("Girder Spacing", "S", "Centre-to-centre spacing, mm.", GH_ParamAccess.item, 3500); p.AddNumberParameter("Explicit Offsets", "Y", "Optional increasing transverse offsets, mm, replacing regular spacing.", GH_ParamAccess.list); p[2].Optional=true; }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddNumberParameter("Transverse Offsets", "Y", "Increasing offsets, centred on alignment, mm.", GH_ParamAccess.list);
    protected override void Solve(IGH_DataAccess da)
    { int count=3; double spacing=3500; da.GetData(0, ref count); da.GetData(1, ref spacing); var values=Numbers(da,2); if(values.Length>0){if(values.Length<2||values.Length>32||values.Any(v=>!double.IsFinite(v))||values.Zip(values.Skip(1),(a,b)=>b-a).Any(d=>d<=SimpleBridgeBuilder.JoinTolerance))throw new ArgumentException("Explicit offsets must be increasing, finite; 2..32 girders.");da.SetDataList(0,values);return;} if (count<2 || count>32 || !double.IsFinite(spacing) || spacing<=0) throw new ArgumentException("Count 2..32 and positive spacing required."); da.SetDataList(0, Enumerable.Range(0,count).Select(i => (i-(count-1)/2d)*spacing)); }
}

public sealed class BridgeDeckLayoutComponent : BridgeDefinitionComponent
{
    public BridgeDeckLayoutComponent() : base("Bridge Slab Layout", "SlabLayout", "Slab envelope, crossfall and reference elevation. Meshing is configured separately. All lengths in mm.") { }
    public override Guid ComponentGuid => new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f004");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Side Widths", "W", "Left and right distances from alignment to deck edge, mm. One value = symmetric deck.", GH_ParamAccess.list, 6000);
        p.AddNumberParameter("Thickness", "H", "Slab thickness along deck normal, mm.", GH_ParamAccess.item, 250);
        p.AddNumberParameter("Crossfall Percent", "%", "One constant percentage, or one percentage per Crossfall Station. Positive raises the left edge.", GH_ParamAccess.list, 0);
        p.AddNumberParameter("Crossfall Stations", "S", "Only for variable crossfall: stations corresponding to percentages, covering the entire alignment, mm.", GH_ParamAccess.list); p[3].Optional=true;
        p.AddNumberParameter("Slab Axis Level", "Z", "Optional level from deck top, mm, within -Thickness..0. Default: slab mid-plane (-Thickness/2).", GH_ParamAccess.item); p[4].Optional=true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Slab Layout", "D", "Geometric slab definition. Material and weight are assigned separately.", GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {
        var widths=Numbers(da,0,6000); double thickness=250,level=0; da.GetData(1,ref thickness); bool hasLevel=da.GetData(4,ref level);
        var slopes=Numbers(da,2,0); var stations=Numbers(da,3);
        if (widths.Length is not (1 or 2) || widths.Any(w=>!double.IsFinite(w)||w<=0) || !double.IsFinite(thickness)||thickness<=0 ||
            slopes.Any(s=>!double.IsFinite(s)||Math.Abs(s)>30) || (slopes.Length==1 ? stations.Length!=0 : stations.Length!=slopes.Length) ||
            hasLevel && (!double.IsFinite(level)||level < -thickness || level>0)) throw new ArgumentException("Invalid slab width, thickness, crossfall profile or reference level.");
        Output(da,new BridgeDeckLayout(widths,thickness,stations,slopes,hasLevel?level:null));
    }
}

public sealed class BridgeJointSetComponent : BridgeDefinitionComponent
{
    public BridgeJointSetComponent() : base("Bridge Joint Stations", "JointStations", "A list of fabrication joint stations, optionally restricted to selected girders. No tree paths are required.") { }
    public override Guid ComponentGuid => new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f005");
    protected override void RegisterInputParams(GH_InputParamManager p)
    { p.AddNumberParameter("Joint Stations", "S", "Distances along alignment, mm. These are physical fabrication joints, not FEM releases.", GH_ParamAccess.list); p[0].Optional=true; p.AddIntegerParameter("Girder IDs", "G", "Optional IDs starting at 1. Empty or 0 applies to all girders.", GH_ParamAccess.list); p[1].Optional=true; }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Joint Set", "J", "Connect one or several sets to Bridge Fabrication Segments.", GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    { var s=Numbers(da,0); var g=new List<int>(); da.GetDataList(1,g); if(s.Any(v=>!double.IsFinite(v)||v<0)||g.Any(v=>v<0)) throw new ArgumentException("Non-negative stations and girder IDs required."); Output(da,new BridgeJointSet(s,g.ToArray())); }
}

public sealed class BridgeFabricationSegmentsComponent : BridgeDefinitionComponent
{
    public BridgeFabricationSegmentsComponent() : base("Bridge Fabrication Segments", "Fabrication", "Define physical girder segments BEFORE meshing. IDs and ranges feed Excel and section assignment directly.") { }
    public override Guid ComponentGuid => new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f006");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Alignment", "A", "Bridge alignment, mm.", GH_ParamAccess.item);
        p.AddGenericParameter("Girder Axes", "G", "From Bridge Girder Axes.", GH_ParamAccess.item);
        p.AddGenericParameter("Joint Sets", "J", "Optional Bridge Joint Stations objects. Explicit stations replace automatic subdivision for the affected girder.", GH_ParamAccess.list); p[2].Optional=true;
        p.AddNumberParameter("Maximum Segment Length", "L", "Equal subdivision when no explicit joints apply. mm; 0 = one segment per girder. Independent of spans and FEM mesh.", GH_ParamAccess.item,12000);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Fabrication Plan", "C", "Physical segments, usable by assembly, section template and section reader.", GH_ParamAccess.item);
        p.AddIntegerParameter("Segment Count", "N", "Total count, all girders.", GH_ParamAccess.item);
        p.AddTextParameter("Segment IDs", "ID", "G1-C1 etc., ordered by girder and segment.", GH_ParamAccess.list);
        p.AddNumberParameter("Start Stations", "S0", "mm, same order as IDs.", GH_ParamAccess.list);
        p.AddNumberParameter("End Stations", "S1", "mm, same order as IDs.", GH_ParamAccess.list);
        p.AddNumberParameter("Segment Lengths", "L", "Length along alignment, mm (not chord length).", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        Curve a=null; if(!da.GetData(0,ref a))return; var g=Get<BridgeGirderLayout>(da,1); if(g==null)return;
        var goos=new List<BridgeDefinitionGoo>(); da.GetDataList(2,goos); double maximum=12000; da.GetData(3,ref maximum);
        var sets=goos.Select(v=>v.Value as BridgeJointSet ?? throw new ArgumentException("Joint Sets requires Bridge Joint Stations."));
        var plan=BridgeFabricationPlan.Create(a.GetLength(),g.Offsets.Length,BridgeFabricationPlan.Joints(sets,maximum));
        Output(da,plan); da.SetData(1,plan.Ranges.Length); da.SetDataList(2,plan.Ranges.Select(BridgeSegmentSections.Id));
        da.SetDataList(3,plan.Ranges.Select(s=>s.Start)); da.SetDataList(4,plan.Ranges.Select(s=>s.End)); da.SetDataList(5,plan.Ranges.Select(s=>s.End-s.Start)); Message=$"{plan.Ranges.Length} conci";
    }
}

public abstract class BridgeDiaphragmDefinitionComponent : BridgeDefinitionComponent
{
    private bool truss => this is BridgeTrussDiaphragmsComponent;
    protected BridgeDiaphragmDefinitionComponent(bool truss) : base(truss?"Bridge Truss Diaphragms":"Bridge Solid Diaphragms",truss?"TrussDiaphragms":"SolidDiaphragms","Diaphragm stations and geometry, mm. Sections and releases are assigned separately.") { }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Stations","S","Explicit distances along alignment, mm. Empty uses Maximum Spacing.",GH_ParamAccess.list);p[0].Optional=true;
        p.AddNumberParameter("Maximum Spacing","L","Automatic spacing within each span/overhang, mm. 0 = explicit and support lines only.",GH_ParamAccess.item,5000);
        p.AddBooleanParameter("At Supports","AS","Include support lines and their skew.",GH_ParamAccess.item,true);
        if(!truss)return;
        p.AddNumberParameter("Truss Height","H","Total height centred on girder axes, mm.",GH_ParamAccess.item,1500);
        p.AddIntegerParameter("Panels per Bay","P","Panels between adjacent girders.",GH_ParamAccess.item,2);
        p.AddIntegerParameter("Pattern","T","X, V or Warren.",GH_ParamAccess.item,0);
        foreach(BridgeTrussPattern t in Enum.GetValues<BridgeTrussPattern>())((Param_Integer)p[5]).AddNamedValue(t.ToString(),(int)t);
        p.AddBooleanParameter("Connect X","X","Split intersecting diagonals at their common centre. False = no connection at the crossing.",GH_ParamAccess.item,false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddGenericParameter("Diaphragms","D","Connect to Bridge Geometry Assembly.",GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {
        var stations=Numbers(da,0).Distinct().OrderBy(v=>v).ToArray();double spacing=5000,height=1500;bool supports=true,crossing=false;int panels=2,pattern=0;
        da.GetData(1,ref spacing);da.GetData(2,ref supports);if(truss){da.GetData(3,ref height);da.GetData(4,ref panels);da.GetData(5,ref pattern);da.GetData(6,ref crossing);}
        if(!double.IsFinite(spacing)||spacing<0||stations.Any(v=>!double.IsFinite(v)||v<0)||!double.IsFinite(height)||height<=0||panels<1||panels>100||!Enum.IsDefined((BridgeTrussPattern)pattern)) throw new ArgumentException("Invalid diaphragm spacing, stations or truss geometry.");
        Output(da,new BridgeDiaphragmLayout(new BridgeDiaphragms{Stations=stations,MaximumSpacing=spacing,AtSupports=supports,TrussHeight=height,PanelsPerBay=panels,Pattern=(BridgeTrussPattern)pattern,ConnectCrossings=crossing},truss));
    }
}
public sealed class BridgeSolidDiaphragmsComponent : BridgeDiaphragmDefinitionComponent
{ public BridgeSolidDiaphragmsComponent():base(false){} public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f007"); }
public sealed class BridgeTrussDiaphragmsComponent : BridgeDiaphragmDefinitionComponent
{ public BridgeTrussDiaphragmsComponent():base(true){} public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f008"); }

public sealed class BridgeGeometryAssemblyComponent : BridgeDefinitionComponent
{
    public BridgeGeometryAssemblyComponent():base("Bridge Geometry Assembly","BridgeAssembly","Assemble independent geometric definitions. No mesh, material, load or stiffness is created here."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f009");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Alignment","A","Deck top reference alignment, mm.",GH_ParamAccess.item);
        foreach(var name in new[]{"Support Lines","Girder Axes","Slab Layout","Fabrication Plan","Diaphragms"})p.AddGenericParameter(name,name,"Typed definition from the corresponding Bridge component.",GH_ParamAccess.item);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddGenericParameter("Bridge Definition","B","Connect to Bridge Grillage Mesh.",GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {
        Curve a=null;if(!da.GetData(0,ref a))return;
        var s=Get<BridgeSupportLayout>(da,1);var g=Get<BridgeGirderLayout>(da,2);var d=Get<BridgeDeckLayout>(da,3);var c=Get<BridgeFabricationPlan>(da,4);var t=Get<BridgeDiaphragmLayout>(da,5);
        if(s==null||g==null||d==null||c==null||t==null)return;
        var definition=new BridgeDefinition(a,s,g,d,c,t);definition.ToInput(new(2500,5,0,Array.Empty<double>()));Output(da,definition);
    }
}

public sealed class BridgeMeshSettingsComponent : BridgeDefinitionComponent
{
    public BridgeMeshSettingsComponent():base("Bridge Mesh Settings","MeshSettings","Control discretization without changing physical fabrication segments. Lengths in mm."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f010");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Maximum Element Length","L","Maximum longitudinal chord length, mm.",GH_ParamAccess.item,2500);
        p.AddNumberParameter("Chord Tolerance","T","Maximum deviation used by adaptive curve subdivision, mm.",GH_ParamAccess.item,5);
        p.AddNumberParameter("Slab Beam Spacing","DS","Maximum transverse slab-beam spacing, mm. 0 uses every refined longitudinal station.",GH_ParamAccess.item,0);
        p.AddNumberParameter("Slab Beam Stations","DX","Optional explicit slab distances, mm; replaces spacing. Ends are always included.",GH_ParamAccess.list);p[3].Optional=true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddGenericParameter("Mesh Settings","M","All intersections are resolved by the same mesher.",GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {double l=2500,t=5,s=0;da.GetData(0,ref l);da.GetData(1,ref t);da.GetData(2,ref s);if(!double.IsFinite(l)||l<=0||!double.IsFinite(t)||t<=0||!double.IsFinite(s)||s<0)throw new ArgumentException("Positive element length and tolerance, non-negative slab spacing required.");Output(da,new BridgeMeshSettings(l,t,s,Numbers(da,3).Distinct().OrderBy(v=>v).ToArray()));}
}

public sealed class BridgeGrillageMeshComponent : SimpleBridgeComponent
{
    public BridgeGrillageMeshComponent():base(false,"Bridge Grillage Mesh","GrillageMesh"){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f011");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddGenericParameter("Bridge Definition","B","From Bridge Geometry Assembly.",GH_ParamAccess.item);p.AddGenericParameter("Mesh Settings","M","From Bridge Mesh Settings.",GH_ParamAccess.item);p.AddBooleanParameter("Labels","ID","Show fabrication-joint and support labels in preview.",GH_ParamAccess.item,false);}
    protected override void Solve(IGH_DataAccess da)
    {
        BridgeDefinitionGoo b=null,m=null;bool labels=false;if(!da.GetData(0,ref b)||!da.GetData(1,ref m))return;da.GetData(2,ref labels);
        if(b?.Value is not BridgeDefinition definition||m?.Value is not BridgeMeshSettings mesh)throw new ArgumentException("Connect Bridge Definition and Mesh Settings.");
        Publish(da,definition.ToInput(mesh),labels);
    }
}
