using System;
using System.Linq;
using BridGHe.Common.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSupportStationsComponent : BridgeDefinitionComponent
{
    public BridgeSupportStationsComponent():base("Bridge Support Stations","SupportStations","Prepare support distances on any open alignment. Optional helper: sort, merge near duplicates and derive spans and overhangs. No fabrication or mesh inputs."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f024");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddCurveParameter("Alignment","A","Open curve, mm.",GH_ParamAccess.item);p.AddNumberParameter("Support Stations","S","Distances along curve, mm. Empty = equal spans.",GH_ParamAccess.list);p[1].Optional=true;p[1].DataMapping=GH_DataMapping.Flatten;p.AddIntegerParameter("Equal Span Count","N","Used only if no stations are supplied.",GH_ParamAccess.item,3);p.AddBooleanParameter("End Supports","E","Add both curve ends. False preserves overhangs from explicit stations.",GH_ParamAccess.item,true);p.AddNumberParameter("Station Tolerance","T","Snap to ends and merge duplicates within this distance, mm.",GH_ParamAccess.item,1);}
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {p.AddCurveParameter("Alignment","A","Original curve.",GH_ParamAccess.item);p.AddNumberParameter("Span Lengths","L","Actual span lengths, mm.",GH_ParamAccess.list);p.AddNumberParameter("Start Overhang","OS","mm.",GH_ParamAccess.item);p.AddNumberParameter("End Overhang","OE","mm.",GH_ParamAccess.item);p.AddNumberParameter("Support Stations","S","Sorted distances, mm. Connect to Bridge Support Lines.",GH_ParamAccess.list);p.AddPointParameter("Support Points","P","On alignment.",GH_ParamAccess.list);p.AddIntegerParameter("Span Count","N","Actual span count.",GH_ParamAccess.item);p.AddNumberParameter("Length","LT","Curve length, mm.",GH_ParamAccess.item);p.AddTextParameter("Information","I","Normalization applied.",GH_ParamAccess.list);}
    protected override void Solve(IGH_DataAccess da)
    {Curve a=null;if(!da.GetData(0,ref a))return;int n=3;bool ends=true;double tol=1;da.GetData(2,ref n);da.GetData(3,ref ends);da.GetData(4,ref tol);var r=BridgeAlignmentInputs.Create(a,Numbers(da,1),n,ends,null,tol);da.SetData(0,r.Alignment);da.SetDataList(1,r.Spans);da.SetData(2,r.StartOverhang);da.SetData(3,r.EndOverhang);da.SetDataList(4,r.Supports);da.SetDataList(5,r.SupportPoints);da.SetData(6,r.Spans.Length);da.SetData(7,r.Length);da.SetDataList(8,r.Information);}
}

public sealed class BridgeNormalizeJointsComponent : BridgeDefinitionComponent
{
    public BridgeNormalizeJointsComponent():base("Bridge Normalize Joint Stations","NormalizeJoints","Normalize supplied joint distances: sort, merge near duplicates and omit bridge ends. mm."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f025");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddCurveParameter("Alignment","A","Open curve, mm.",GH_ParamAccess.item);p.AddNumberParameter("Joint Stations","J","Distances along curve, mm.",GH_ParamAccess.list);p[1].Optional=true;p[1].DataMapping=GH_DataMapping.Flatten;p.AddNumberParameter("Station Tolerance","T","Snap and merge distance, mm.",GH_ParamAccess.item,1);}
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddNumberParameter("Joint Stations","J","Internal joint distances, mm; common branch {0}.",GH_ParamAccess.tree);
    protected override void Solve(IGH_DataAccess da)
    {Curve a=null;if(!da.GetData(0,ref a))return;double tol=1;da.GetData(2,ref tol);var tree=new GH_Structure<GH_Number>();var path=new GH_Path(0);tree.EnsurePath(path);tree.AppendRange(BridgeAlignmentInputs.Create(a,null,1,true,Numbers(da,1),tol).Joints.Select(s=>new GH_Number(s)),path);da.SetDataTree(0,tree);}
}

public sealed class BridgeDiaphragmChoiceComponent : BridgeDefinitionComponent
{
    public BridgeDiaphragmChoiceComponent():base("Bridge Diaphragm Choice","DiaphragmChoice","Optional switch between two explicit diaphragm definitions. Both alternatives remain visible on the canvas."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f026");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddBooleanParameter("Use Truss","T","False: solid alternative. True: truss alternative.",GH_ParamAccess.item,false);p.AddGenericParameter("Solid Diaphragms","S","From Bridge Solid Diaphragms.",GH_ParamAccess.item);p.AddGenericParameter("Truss Diaphragms","T","From Bridge Truss Diaphragms.",GH_ParamAccess.item);}
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddGenericParameter("Diaphragms","D","Chosen geometry definition.",GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {bool truss=false;da.GetData(0,ref truss);var value=Get<BridgeDiaphragmLayout>(da,truss?2:1);if(value!=null)Output(da,value);}
}
