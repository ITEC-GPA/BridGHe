using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeLongitudinalBracingComponent : BridgeUtilityComponent
{
    private readonly List<Line> preview = new();
    public BridgeLongitudinalBracingComponent() : base("Bridge Longitudinal Bracing", "LongBracing",
        "Add longitudinal plan bracing to typed geometry, with real attachment nodes and optional physical offset posts. mm.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("4321ef9f-4f45-467a-beb3-aa92bdc86ce3");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Bridge Geometry", "BG", "Source geometry, unchanged.", GH_ParamAccess.item);
        p.AddNumberParameter("Stations", "S", "Ordered panel boundaries along alignment, mm.", GH_ParamAccess.list);
        p.AddIntegerParameter("Bays", "B", "Bay IDs 1..girders-1; empty selects all.", GH_ParamAccess.list); p[2].Optional = true;
        p.AddIntegerParameter("Pattern", "P", "0 X, 1 V, 2 alternating single diagonals.", GH_ParamAccess.item, 0);
        p.AddNumberParameter("Normal Offset", "Z", "Offset from girder axes along deck normal, mm. Nonzero generates physical posts (family 7).", GH_ParamAccess.item, 0);
        p.AddBooleanParameter("Connect X", "X", "Split actual 3D intersections; reject skew diagonals that do not intersect.", GH_ParamAccess.item, false);
        p.AddIntegerParameter("Omitted Panels", "O", "Panel numbers to omit, 1-based.", GH_ParamAccess.list); p[6].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Bridge Geometry", "BG", "Augmented geometry for Member Assignment. Existing girder axes split at attachment points.", GH_ParamAccess.item);
        p.AddLineParameter("Added Members", "L", "Families 6 and 7, with no assigned section or stiffness.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Generation notes.", GH_ParamAccess.list);
    }
    protected override void BeforeSolveInstance() { base.BeforeSolveInstance(); preview.Clear(); }
    protected override void Solve(IGH_DataAccess da)
    {
        BridgeGeometryGoo geometry = null; var stations = new List<double>(); var bays = new List<int>(); var omit = new List<int>();
        int pattern = 0; double offset = 0; bool connect = false;
        if (!da.GetData(0, ref geometry) || !da.GetDataList(1, stations)) return;
        da.GetDataList(2, bays); da.GetData(3, ref pattern); da.GetData(4, ref offset); da.GetData(5, ref connect); da.GetDataList(6, omit);
        var result = BridgeLongitudinalBracing.Add(geometry.Value, stations, bays, (BridgeTrussPattern)pattern, offset, connect, omit);
        preview.AddRange(result.Members.Where(m => m.Family is BridgeMemberFamily.LongitudinalBracing or BridgeMemberFamily.BracingPost).Select(m => m.Axis));
        da.SetData(0, new BridgeGeometryGoo(result)); da.SetDataList(1, preview); da.SetDataList(2, result.Diagnostics);
    }
    public override BoundingBox ClippingBox => preview.Count == 0 ? BoundingBox.Empty : new BoundingBox(preview.SelectMany(l => new[] { l.From, l.To }));
    public override void DrawViewportWires(IGH_PreviewArgs args)
    { foreach (var line in preview) args.Display.DrawLine(line, Color.DarkGoldenrod, 2); }
}
