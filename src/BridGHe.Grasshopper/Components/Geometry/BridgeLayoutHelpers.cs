using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.DataTypes;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSpanStationsComponent : BridgeDefinitionComponent
{
    public BridgeSpanStationsComponent():base("Bridge Span Stations","SpanStations","Support stations from span lengths and end overhangs, mm. This helper is optional when stations are supplied directly."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f017");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddIntegerParameter("Span Count","N","Number of equal spans if no explicit lengths are supplied.",GH_ParamAccess.item,3);p.AddNumberParameter("Uniform Span Length","L","mm, used with Span Count.",GH_ParamAccess.item,30000);p.AddNumberParameter("Span Lengths","Ls","Optional unequal span lengths, mm. Replaces regular spans.",GH_ParamAccess.list);p[2].Optional=true;p.AddNumberParameter("Start Overhang","OS","mm before first support.",GH_ParamAccess.item,0);p.AddNumberParameter("End Overhang","OE","mm after last support.",GH_ParamAccess.item,0);}
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {p.AddNumberParameter("Support Stations","S","Distances from alignment start, mm.",GH_ParamAccess.list);p.AddNumberParameter("Span Lengths","L","Actual span lengths, mm.",GH_ParamAccess.list);p.AddNumberParameter("Alignment Length","LT","Total spans plus end overhangs, mm.",GH_ParamAccess.item);}
    protected override void Solve(IGH_DataAccess da)
    {int n=3;double l=30000,os=0,oe=0;da.GetData(0,ref n);da.GetData(1,ref l);da.GetData(3,ref os);da.GetData(4,ref oe);var spans=Numbers(da,2);if(spans.Length==0){if(n<1||n>100)throw new ArgumentException("Span Count must be 1..100.");spans=Enumerable.Repeat(l,n).ToArray();}if(spans.Length>100||spans.Any(v=>!double.IsFinite(v)||v<=0)||!double.IsFinite(os)||os<0||!double.IsFinite(oe)||oe<0)throw new ArgumentException("Positive spans and non-negative end overhangs required.");double x=os;var s=new List<double>{x};foreach(double v in spans){x+=v;s.Add(x);}da.SetDataList(0,s);da.SetDataList(1,spans);da.SetData(2,x+oe);}
}

public sealed class BridgeAlignmentCurveComponent : BridgeDefinitionComponent
{
    public BridgeAlignmentCurveComponent():base("Bridge Alignment Curve","AlignmentCurve","Generate a straight or circular deck alignment from length, radius, origin and heading. A user-drawn curve can replace this component."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f018");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddNumberParameter("Length","L","Total arc length, mm.",GH_ParamAccess.item);p.AddNumberParameter("Radius","R","0 straight; positive left, negative right curve, mm. Arc below 180 degrees.",GH_ParamAccess.item,0);p.AddPointParameter("Origin","O","Alignment start, mm.",GH_ParamAccess.item,Point3d.Origin);p.AddNumberParameter("Heading","H","Initial heading from global X about global Z, degrees.",GH_ParamAccess.item,0);p.AddCurveParameter("Custom Alignment","C","Optional existing curve, replacing the generated curve. Its length must agree with Length. Alternatively connect your curve directly to the layouts.",GH_ParamAccess.item);p[4].Optional=true;}
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddCurveParameter("Alignment","A","Deck top reference curve.",GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {double l=0,r=0,h=0;var o=Point3d.Origin;Curve custom=null;if(!da.GetData(0,ref l))return;da.GetData(1,ref r);da.GetData(2,ref o);da.GetData(3,ref h);da.GetData(4,ref custom);da.SetData(0,BridgeParametricLayout.Create(1,l,Array.Empty<double>(),2,3500,Array.Empty<double>(),1500,1500,0,0,r,o,h,custom).Alignment);}
}

public sealed class BridgeDeckWidthsComponent : BridgeDefinitionComponent
{
    public BridgeDeckWidthsComponent():base("Bridge Deck Widths","DeckWidths","Deck edge distances from girder positions and lateral cantilevers, mm."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f019");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddNumberParameter("Girder Offsets","Y","Signed transverse positions, mm.",GH_ParamAccess.list);p.AddNumberParameter("Left Cantilever","L","Distance beyond the leftmost girder, mm.",GH_ParamAccess.item,1500);p.AddNumberParameter("Right Cantilever","R","Distance beyond the rightmost girder, mm.",GH_ParamAccess.item,1500);}
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddNumberParameter("Side Widths","W","Left then right distance from alignment, mm.",GH_ParamAccess.list);
    protected override void Solve(IGH_DataAccess da)
    {var y=Numbers(da,0);double l=1500,r=1500;da.GetData(1,ref l);da.GetData(2,ref r);if(y.Length<2||y.Any(v=>!double.IsFinite(v))||!double.IsFinite(l)||l<0||!double.IsFinite(r)||r<0||y.Max()+l<=0||-y.Min()+r<=0)throw new ArgumentException("Valid girder offsets and non-negative cantilevers required; deck must contain the alignment.");da.SetDataList(0,new[]{y.Max()+l,-y.Min()+r});}
}

public sealed class BridgeBracingStationsComponent : BridgeDefinitionComponent
{
    public BridgeBracingStationsComponent():base("Bridge Bracing Stations","BracingStations","Panel stations within spans and overhangs, including all supports, mm."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f020");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddCurveParameter("Alignment","A","Bridge curve.",GH_ParamAccess.item);p.AddNumberParameter("Support Stations","S","Distances along alignment, mm.",GH_ParamAccess.list);p.AddNumberParameter("Maximum Panel Length","L","mm; at most 1000 panels.",GH_ParamAccess.item,6000);}
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddNumberParameter("Bracing Stations","S","Panel boundaries, mm.",GH_ParamAccess.list);
    protected override void Solve(IGH_DataAccess da)
    {Curve a=null;double spacing=6000;if(!da.GetData(0,ref a))return;da.GetData(2,ref spacing);double length=a.GetLength();var stations=Numbers(da,1);if(!double.IsFinite(spacing)||spacing<=0||length/spacing>1000||stations.Any(s=>!double.IsFinite(s)||s<0||s>length))throw new ArgumentException("Valid support distances and positive spacing with at most 1000 panels required.");var boundaries=new List<double>();foreach(double s in stations.Append(0).Append(length).OrderBy(s=>s))if(boundaries.Count==0||s-boundaries[^1]>SimpleBridgeBuilder.JoinTolerance)boundaries.Add(s);var values=new SortedSet<double>(boundaries);for(int i=0;i+1<boundaries.Count;i++){int n=Math.Max(1,(int)Math.Ceiling((boundaries[i+1]-boundaries[i]-SimpleBridgeBuilder.JoinTolerance)/spacing));for(int j=1;j<n;j++)values.Add(boundaries[i]+(boundaries[i+1]-boundaries[i])*j/n);}if(values.Count>1001)throw new ArgumentException("At most 1000 bracing panels.");da.SetDataList(0,values);}
}

public sealed class BridgeBearingPatternComponent : BridgeDefinitionComponent
{
    public BridgeBearingPatternComponent():base("Bridge Bearing Pattern","BearingPattern","Suggested bearing directions: first girder fixed at first support and guided at others; remaining bearings multidirectional. Review this proposal for the actual bridge."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f021");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddNumberParameter("Support Stations","S","One entry per support.",GH_ParamAccess.list);p.AddNumberParameter("Girder Offsets","G","One entry per girder.",GH_ParamAccess.list);p.AddIntegerParameter("Bearing Types","T","Optional override: one or one per support/girder. 0 fixed translations, 1 longitudinal guide, 2 multidirectional, 3 elastic. Rotations are not fixed.",GH_ParamAccess.list);p[2].Optional=true;}
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddIntegerParameter("Bearing Types","T","Support-major, girder-minor order; connect to Bridge Bearings.",GH_ParamAccess.list);
    protected override void Solve(IGH_DataAccess da)
    {var s=Numbers(da,0);var g=Numbers(da,1);var types=new List<int>();da.GetDataList(2,types);if(s.Length<2||g.Length<2||types.Any(t=>t<0||t>3)||types.Count>0&&types.Count!=1&&types.Count!=s.Length*g.Length)throw new ArgumentException("At least two supports and girders; one type or one per bearing required.");da.SetDataList(0,types.Count>0?types:Enumerable.Range(0,s.Length).SelectMany(i=>Enumerable.Range(0,g.Length).Select(j=>j==0?(i==0?0:1):2)));}
}

public sealed class BridgeJointTreeComponent : BridgeDefinitionComponent
{
    public BridgeJointTreeComponent():base("Bridge Joint Tree","JointTree","Import an existing joint tree: branch {0} common, {girder ID} additional joints. For manual input use Bridge Joint Stations instead."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f022");
    protected override void RegisterInputParams(GH_InputParamManager p){p.AddNumberParameter("Joint Tree","J","Joint stations in mm, paths {0} or {girder ID}.",GH_ParamAccess.tree);p[0].Optional=true;}
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddGenericParameter("Joint Sets","J","Typed sets for Bridge Fabrication Segments.",GH_ParamAccess.list);
    protected override void Solve(IGH_DataAccess da)
    {var sets=new List<BridgeDefinitionGoo>();if(da.GetDataTree(0,out GH_Structure<GH_Number> tree))for(int i=0;i<tree.PathCount;i++){var path=tree.Paths[i];if(path.Length!=1||path[0]<0)throw new ArgumentException("Joint tree paths must be {0} or {girder ID}.");sets.Add(new(new BridgeJointSet(tree.Branches[i].Select(v=>v.Value).ToArray(),new[]{path[0]})));}da.SetDataList(0,sets);}
}

public sealed class BridgeCrossfallTreeComponent : BridgeDefinitionComponent
{
    public BridgeCrossfallTreeComponent():base("Bridge Crossfall Table","CrossfallTable","Convert a station/percentage table to separate profile lists. Direct constant slopes connect straight to Bridge Slab Layout."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f023");
    protected override void RegisterInputParams(GH_InputParamManager p){p.AddNumberParameter("Crossfall Table","T","One value for constant slope, or one station/percent pair per branch.",GH_ParamAccess.tree);p[0].Optional=true;}
    protected override void RegisterOutputParams(GH_OutputParamManager p){p.AddNumberParameter("Percentages","%","Crossfall percentages.",GH_ParamAccess.list);p.AddNumberParameter("Stations","S","Empty for constant slope, otherwise profile stations, mm.",GH_ParamAccess.list);}
    protected override void Solve(IGH_DataAccess da)
    {if(!da.GetDataTree(0,out GH_Structure<GH_Number> tree)||tree.DataCount==0){da.SetDataList(0,new[]{0d});return;}if(tree.PathCount==1&&tree.DataCount==1){da.SetDataList(0,new[]{tree.Branches[0][0].Value});return;}if(tree.Branches.Any(b=>b.Count!=2))throw new ArgumentException("One constant value or one station/percent pair per branch required.");da.SetDataList(0,tree.Branches.Select(b=>b[1].Value));da.SetDataList(1,tree.Branches.Select(b=>b[0].Value));}
}
