using System;
using System.Collections.Generic;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeAlignmentInputsComponent : BridgeUtilityComponent
{
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    public BridgeAlignmentInputsComponent() : base("Bridge Alignment Inputs", "AlignmentInputs",
        "Prepare Bridge Parametric Layout from an open alignment and support distances. Sorts stations, merges duplicates and derives spans/overhangs from actual arc length. All lengths in mm.",
        CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("750d7fac-a9b7-4fbb-8c11-c291e99a5cb8");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Alignment", "A", "Open alignment curve in mm; 3D arc length and curve direction define stations.", GH_ParamAccess.item);
        p.AddNumberParameter("Support Stations", "S", "Distances in mm from curve start. Unordered values and duplicates are normalized. Empty: equal spans.", GH_ParamAccess.list); p[1].Optional = true; p[1].DataMapping = GH_DataMapping.Flatten;
        p.AddIntegerParameter("Span Count", "N", "Number of equal spans when no Support Stations are given. Ignored with explicit support stations.", GH_ParamAccess.item, 3);
        p.AddBooleanParameter("End Supports", "E", "Add supports at both curve ends. False: first/last explicit station define the end overhangs. With no stations, equal spans always cover the full curve.", GH_ParamAccess.item, true);
        p.AddNumberParameter("Joint Stations", "J", "Optional common fabrication-joint distances in mm. Sorted and merged; curve ends omitted. Empty uses Maximum Segment Length in Bridge Segments.", GH_ParamAccess.list); p[4].Optional = true; p[4].DataMapping = GH_DataMapping.Flatten;
        p.AddNumberParameter("Maximum Segment Length", "LC", "Forward to Bridge Segments > Maximum Length. Independent of support stations. Zero means a single concio per girder when no joints are given.", GH_ParamAccess.item, 12000);
        p.AddNumberParameter("Maximum Element Length", "LF", "Forward to Simple Bridge > Maximum Element Length. Positive FEM mesh length in mm; it does not count fabrication segments.", GH_ParamAccess.item, 2500);
        p.AddNumberParameter("Station Tolerance", "Tol", "mm: snap near-end stations and merge near duplicates. Does not approximate the calculated span lengths.", GH_ParamAccess.item, 1);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddCurveParameter("Alignment", "A", "Connect to Bridge Parametric Layout > Custom Alignment.", GH_ParamAccess.item);
        p.AddNumberParameter("Span Lengths", "L", "Exact derived span lengths; connect to Bridge Parametric Layout > Span Lengths.", GH_ParamAccess.list);
        p.AddNumberParameter("Start Overhang", "OS", "Connect to Bridge Parametric Layout > Start Overhang, mm.", GH_ParamAccess.item);
        p.AddNumberParameter("End Overhang", "OE", "Connect to Bridge Parametric Layout > End Overhang, mm.", GH_ParamAccess.item);
        p.AddNumberParameter("Support Stations", "S", "Sorted support distances along the curve, mm.", GH_ParamAccess.list);
        p.AddPointParameter("Support Points", "P", "Support positions on the reference curve; preview only, not FEM bearing nodes.", GH_ParamAccess.list);
        p.AddIntegerParameter("Span Count", "N", "Actual number of spans.", GH_ParamAccess.item);
        p.AddNumberParameter("Joint Stations", "J", "Common joints, mm. Connect to Bridge Segments > Joints; always emits the explicit branch {0}.", GH_ParamAccess.tree);
        p.AddNumberParameter("Maximum Segment Length", "LC", "Connect to Bridge Segments > Maximum Length, mm.", GH_ParamAccess.item);
        p.AddNumberParameter("Maximum Element Length", "LF", "Connect to Simple Bridge > Maximum Element Length, mm.", GH_ParamAccess.item);
        p.AddNumberParameter("Length", "Ltot", "Full 3D alignment arc length, mm.", GH_ParamAccess.item);
        p.AddTextParameter("Information", "I", "Normalization applied, support convention and units.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        Curve alignment = null; var supports = new List<double>(); var joints = new List<double>();
        int spans = 3; bool ends = true; double segment = 12000, mesh = 2500, tolerance = 1;
        if (!da.GetData(0, ref alignment)) return;
        if (Params.Input[0].VolatileDataCount != 1) throw new ArgumentException("Connect exactly one alignment curve.");
        da.GetDataList(1, supports); da.GetData(2, ref spans); da.GetData(3, ref ends); da.GetDataList(4, joints);
        da.GetData(5, ref segment); da.GetData(6, ref mesh); da.GetData(7, ref tolerance);
        if (!double.IsFinite(segment) || segment < 0 || !double.IsFinite(mesh) || mesh <= 0)
            throw new ArgumentException("Maximum Segment Length must be non-negative and Maximum Element Length must be positive, in mm.");
        var result = BridgeAlignmentInputs.Create(alignment, supports, spans, ends, joints, tolerance);
        da.SetData(0, result.Alignment); da.SetDataList(1, result.Spans); da.SetData(2, result.StartOverhang); da.SetData(3, result.EndOverhang);
        da.SetDataList(4, result.Supports); da.SetDataList(5, result.SupportPoints); da.SetData(6, result.Spans.Length);
        var jointTree = new GH_Structure<GH_Number>(); jointTree.EnsurePath(new GH_Path(0));
        jointTree.AppendRange(Array.ConvertAll(result.Joints, value => new GH_Number(value)), new GH_Path(0));
        da.SetDataTree(7, jointTree); da.SetData(8, segment); da.SetData(9, mesh); da.SetData(10, result.Length); da.SetDataList(11, result.Information);
        Message = $"{result.Spans.Length} spans | {result.Supports.Length} supports";
    }
}
