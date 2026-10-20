using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeBearingsComponent : BridgeUtilityComponent
{
    private readonly List<(Plane Frame, string Label)> preview = new();
    public BridgeBearingsComponent() : base("Bridge Bearings", "Bearings",
        "Bridge bearing nodes with explicit local restraints or grounded springs, exported to Straus Cartesian UCS. No automatic stability claim.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("39cdb74c-d0d7-4c25-90be-3799a2787e0b");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Bridge Geometry", "BG", "Simple Bridge typed geometry in mm.", GH_ParamAccess.item);
        p.AddIntegerParameter("Supports", "S", "Support IDs, 1-based. Empty selects all.", GH_ParamAccess.list); p[1].Optional = true;
        p.AddIntegerParameter("Girders", "G", "Girder IDs, 1-based. Empty selects all.", GH_ParamAccess.list); p[2].Optional = true;
        p.AddIntegerParameter("Types", "T", "0 fixed translations, 1 longitudinal guide (free X), 2 multidirectional (free X/Y), 3 elastic. Rotations free except elastic stiffness. One or one per selected point.", GH_ParamAccess.list, 2);
        p.AddNumberParameter("Guide Angle", "A", "Rotation about bearing normal, degrees.", GH_ParamAccess.item, 0);
        p.AddVectorParameter("Translation Stiffness", "Kt", "Elastic X/Y/Z stiffnesses, N/mm.", GH_ParamAccess.item, Vector3d.Zero);
        p.AddVectorParameter("Rotation Stiffness", "Kr", "Elastic X/Y/Z rotational stiffnesses, N mm/rad.", GH_ParamAccess.item, Vector3d.Zero);
        p.AddPlaneParameter("Orientation", "F", "Optional shared orientation, origin reset at each bearing. Default: X horizontal alignment tangent, Y left, Z global up.", GH_ParamAccess.item); p[7].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Nodes", "N", "Connect to Build Model with beam elements. No additional global restraint should be superimposed unintentionally.", GH_ParamAccess.list);
        p.AddPlaneParameter("Frames", "F", "Actual restraint frames X/Y/Z.", GH_ParamAccess.list);
        p.AddTextParameter("Labels", "ID", "Support/girder/type order.", GH_ParamAccess.list);
    }
    protected override void BeforeSolveInstance() { base.BeforeSolveInstance(); preview.Clear(); }
    protected override void Solve(IGH_DataAccess da)
    {
        BridgeGeometryGoo g = null; var supports = new List<int>(); var girders = new List<int>(); var kinds = new List<int>();
        double angle = 0; var kt = Vector3d.Zero; var kr = Vector3d.Zero; var frame = Plane.Unset;
        if (!da.GetData(0, ref g) || g?.Value == null) return;
        da.GetDataList(1, supports); da.GetDataList(2, girders); da.GetDataList(3, kinds); da.GetData(4, ref angle);
        da.GetData(5, ref kt); da.GetData(6, ref kr); bool hasFrame = da.GetData(7, ref frame);
        var result = BridgeBearings.Create(g.Value, supports, girders, kinds, angle, kt, kr, hasFrame ? frame : null);
        preview.AddRange(result.Frames.Zip(result.Labels, (f, l) => (f, l)));
        da.SetDataList(0, result.Nodes.Select(n => new GH_Node(n))); da.SetDataList(1, result.Frames); da.SetDataList(2, result.Labels);
    }
    public override BoundingBox ClippingBox
    { get { var b = BoundingBox.Empty; foreach (var p in preview) { b.Union(p.Frame.Origin + new Vector3d(500, 500, 500)); b.Union(p.Frame.Origin - new Vector3d(500, 500, 500)); } return b; } }
    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        foreach (var p in preview)
        {
            args.Display.DrawArrow(new Line(p.Frame.Origin, p.Frame.Origin + 500 * p.Frame.XAxis), Color.IndianRed);
            args.Display.DrawArrow(new Line(p.Frame.Origin, p.Frame.Origin + 500 * p.Frame.YAxis), Color.SeaGreen);
            args.Display.DrawArrow(new Line(p.Frame.Origin, p.Frame.Origin + 500 * p.Frame.ZAxis), Color.SteelBlue);
            args.Display.DrawDot(p.Frame.Origin, p.Label, Color.DarkSlateGray, Color.White);
        }
    }
}
