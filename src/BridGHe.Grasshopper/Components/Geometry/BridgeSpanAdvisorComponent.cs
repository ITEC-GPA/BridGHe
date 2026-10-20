using System;
using System.Collections.Generic;
using System.Drawing;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSpanAdvisorComponent : BridgeUtilityComponent
{
    private readonly List<Point3d> preview = new();
    public BridgeSpanAdvisorComponent() : base("Bridge Span Advisor", "SpanAdvice",
        "Geometric span layouts outside forbidden XY regions. All lengths in mm; user-defined limits, no structural verification.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("16259f7a-7258-4bae-84b7-1d275034ea27");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Alignment", "A", "Open 3D reference alignment, mm.", GH_ParamAccess.item);
        p.AddCurveParameter("Forbidden Regions", "R", "Closed horizontal boundaries; their XY footprints exclude support reference points.", GH_ParamAccess.list); p[1].Optional = true;
        p.AddNumberParameter("Minimum Span", "Min", "User-selected minimum arc length, mm.", GH_ParamAccess.item, 15000);
        p.AddNumberParameter("Maximum Span", "Max", "User-selected maximum arc length, mm; not a verified structural limit.", GH_ParamAccess.item, 60000);
        p.AddNumberParameter("Target Span", "L", "Preferred arc length, mm.", GH_ParamAccess.item, 35000);
        p.AddNumberParameter("Candidate Grid", "G", "Candidate station spacing, mm. Finer grid improves search resolution.", GH_ParamAccess.item, 1000);
        p.AddNumberParameter("Clearance", "C", "Minimum XY distance from exclusion boundaries, mm. Include foundation footprint if required.", GH_ParamAccess.item, 0);
        p.AddIntegerParameter("Alternatives", "N", "Number of ranked layouts, 1..10.", GH_ParamAccess.item, 3);
        p.AddNumberParameter("Pier Penalty", "P", "Nonnegative cost for each internal support; larger values favor fewer piers.", GH_ParamAccess.item, 1);
        p.AddNumberParameter("End Span Ratio", "E", "Target end-span/target ratio, (0,1].", GH_ParamAccess.item, 1);
        p.AddNumberParameter("Required Supports", "S", "Optional mandatory arc-length stations, mm. Start/end are always required.", GH_ParamAccess.list); p[10].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("Support Stations", "S", "One branch per alternative; select a branch for Simple Bridge.", GH_ParamAccess.tree);
        p.AddPointParameter("Support Points", "P", "3D reference support points; preview shows the best alternative.", GH_ParamAccess.tree);
        p.AddNumberParameter("Span Lengths", "L", "Arc lengths, mm, one branch per alternative.", GH_ParamAccess.tree);
        p.AddNumberParameter("Scores", "C", "Dimensionless geometric scores, lower is preferred.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Search resolution, objective and limitations.", GH_ParamAccess.list);
    }
    protected override void BeforeSolveInstance() { base.BeforeSolveInstance(); preview.Clear(); }
    protected override void Solve(IGH_DataAccess da)
    {
        Curve alignment = null; var regions = new List<Curve>(); var required = new List<double>();
        double min = 15000, max = 60000, target = 35000, grid = 1000, clearance = 0, penalty = 1, ratio = 1; int count = 3;
        if (!da.GetData(0, ref alignment)) return;
        da.GetDataList(1, regions); da.GetData(2, ref min); da.GetData(3, ref max); da.GetData(4, ref target);
        da.GetData(5, ref grid); da.GetData(6, ref clearance); da.GetData(7, ref count);
        da.GetData(8, ref penalty); da.GetData(9, ref ratio); da.GetDataList(10, required);
        var result = BridgeSpanAdvisor.Suggest(alignment, regions, min, max, target, grid, clearance, count, penalty, ratio, required);
        var stations = new GH_Structure<GH_Number>(); var points = new GH_Structure<GH_Point>(); var lengths = new GH_Structure<GH_Number>(); var scores = new List<double>();
        for (int i = 0; i < result.Alternatives.Length; i++)
        {
            var a = result.Alternatives[i]; var path = new GH_Path(i);
            foreach (double s in a.Stations) stations.Append(new GH_Number(s), path);
            foreach (var point in a.Points) points.Append(new GH_Point(point), path);
            foreach (double l in a.Lengths) lengths.Append(new GH_Number(l), path);
            scores.Add(a.Score);
        }
        if (result.Alternatives.Length > 0) preview.AddRange(result.Alternatives[0].Points);
        else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, result.Diagnostics[0]);
        da.SetDataTree(0, stations); da.SetDataTree(1, points); da.SetDataTree(2, lengths); da.SetDataList(3, scores); da.SetDataList(4, result.Diagnostics);
        Message = $"{result.Alternatives.Length} layouts";
    }
    public override BoundingBox ClippingBox => preview.Count == 0 ? BoundingBox.Empty : new BoundingBox(preview);
    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        for (int i = 0; i < preview.Count; i++)
            args.Display.DrawDot(preview[i], $"S{i + 1}", Color.Teal, Color.White);
    }
}
