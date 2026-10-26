using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeConnectionsComponent : BridgeUtilityComponent
{
    public override GH_Exposure Exposure => GH_Exposure.hidden;
    private readonly List<Line> preview = new();
    public BridgeConnectionsComponent() : base("Bridge Connections", "Connections",
        "Create native spatial rigid links, explicitly deformable beam/truss members, or assign beam section offsets. N-mm.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("66a0d167-4e5b-4ea1-9327-935c772fd97b");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddIntegerParameter("Mode", "M", "0 rigid links, 1 deformable members, 2 beam section offsets.", GH_ParamAccess.item, 0);
        p.AddLineParameter("Connections", "L", "Connection axes; required in modes 0/1. Endpoints must join the intended FEM vertices.", GH_ParamAccess.list); p[1].Optional = true;
        p.AddGenericParameter("Property", "P", "Explicit Beam/Truss property, required only in mode 1.", GH_ParamAccess.item); p[2].Optional = true;
        p.AddGenericParameter("Beams", "B", "Beams to duplicate and modify in mode 2; stored results are cleared.", GH_ParamAccess.list); p[3].Optional = true;
        p.AddVectorParameter("Offset", "O", "Mode 2: local section x/y offsets, mm; Z must be zero. Sets the offset, does not increment it.", GH_ParamAccess.item, Vector3d.Zero);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Elements", "E", "Nodes with rigid links or beam elements: connect to Build Model. Rigid link attributes survive model merging.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Chosen mechanical representation.", GH_ParamAccess.item);
    }
    protected override void BeforeSolveInstance() { base.BeforeSolveInstance(); preview.Clear(); }
    protected override void Solve(IGH_DataAccess da)
    {
        int mode = 0; var lines = new List<Line>(); GH_BeamProperty property = null; var beams = new List<GH_Beam>(); var offset = Vector3d.Zero;
        da.GetData(0, ref mode); da.GetDataList(1, lines); da.GetData(2, ref property); da.GetDataList(3, beams); da.GetData(4, ref offset);
        if (mode < 0 || mode > 2 || offset.Z != 0) throw new ArgumentException("Mode must be 0..2 and offset Z must be zero.");
        ElementModel[] elements;
        if (mode == 2)
        {
            if (lines.Count > 0 || property != null) throw new ArgumentException("Offset mode accepts Beams and Offset only.");
            elements = BridgeConnections.Offset(beams.Select(b => b.Value).ToArray(), new(offset.X, offset.Y));
        }
        else
        {
            if (beams.Count > 0 || offset != Vector3d.Zero || mode == 0 && property != null)
                throw new ArgumentException("Connection mode accepts lines and, for deformable members only, Property.");
            elements = BridgeConnections.Create(lines, mode == 0, property?.Value); preview.AddRange(lines);
        }
        da.SetDataList(0, elements.Select(e => e is NodeModel n ? (IGH_Goo)new GH_Node(n) : new GH_Beam((BeamModel)e)));
        da.SetData(1, mode == 0 ? "Native full spatial rigid links (kinematic constraints), without artificial penalty stiffness." :
            mode == 1 ? "Deformable beam/truss members with the explicitly assigned property." : "Local section offsets assigned; input beams unchanged and copied results cleared.");
    }
    public override BoundingBox ClippingBox => preview.Count == 0 ? BoundingBox.Empty : new BoundingBox(preview.SelectMany(l => new[] { l.From, l.To }));
    public override void DrawViewportWires(IGH_PreviewArgs args)
    { foreach (var line in preview) args.Display.DrawLine(line, Color.MediumPurple, 3); }
}
