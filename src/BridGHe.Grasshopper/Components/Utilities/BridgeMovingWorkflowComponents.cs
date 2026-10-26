using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.MovingLoads;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeStraightMovingPathComponent : BridgeUtilityComponent
{
    public BridgeStraightMovingPathComponent() : base("Bridge Straight Moving Path", "SlabPath", "Straight traffic path on the actual transverse slab axes; checks vehicle width and rejects curved/warped geometry.", CategoryNameConstants.SUBCATEGORY_LOADS) { }
    public override Guid ComponentGuid => new Guid("3fdba0df-48ec-41d3-8b9e-3f2e3663fd32");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Grillage Geometry", "G", "Bridge Grillage Mesh output including transverse slab members.", GH_ParamAccess.item);
        p.AddGenericParameter("Template", "T", "Explicit one-lane moving template, in model units.", GH_ParamAccess.item);
        p.AddTextParameter("Case", "C", "Name of the source moving path.", GH_ParamAccess.item, "Q_MOV");
        p.AddNumberParameter("Lane offset", "Y", "Offset from alignment [mm], positive towards the left transverse frame axis.", GH_ParamAccess.item, 0);
        p.AddIntegerParameter("Divisions", "D", "Number of path patches. Refine to assess moving-position convergence.", GH_ParamAccess.item, 30);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Path", "P", "Native Straus moving path targeting beams.", GH_ParamAccess.item);
        p.AddCurveParameter("Axis", "C", "Path at the actual slab beam elevation; preview only.", GH_ParamAccess.item);
        p.AddNumberParameter("Step", "dx", "Path length / divisions [mm].", GH_ParamAccess.item);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        object input = null; MovingGoo template = null; string name = null; double offset = 0; int count = 30;
        if (!da.GetData(0, ref input) || !da.GetData(1, ref template) || !da.GetData(2, ref name) || !da.GetData(3, ref offset) || !da.GetData(4, ref count)) return;
        var geometry = input is IGH_Goo goo ? goo.ScriptVariable() as SimpleBridgeGeometry : input as SimpleBridgeGeometry;
        if (geometry == null || template?.Value is not MovingTemplate t) throw new ArgumentException("Connect Grillage Geometry and a Moving Load Template.");
        var path = BridgeMovingWorkflow.StraightPath(geometry, t, name, offset, count); var c = path.Coordinates;
        var axis = new LineCurve(new Point3d(c[0], c[1], c[2]), new Point3d(c[3], c[4], c[5]));
        da.SetData(0, new MovingGoo(path)); da.SetData(1, axis); da.SetData(2, axis.GetLength() / count);
    }
}

public sealed class BridgeMovingCombinationsComponent : BridgeUtilityComponent
{
    public BridgeMovingCombinationsComponent() : base("Bridge Moving Combinations", "MovingComb", "Append alternative moving positions to an explicit base table. Exactly one position per added combination; no normative coefficients or force envelopes.", CategoryNameConstants.SUBCATEGORY_LOADS) { }
    public override Guid ComponentGuid => new Guid("9a43a3f7-3d36-4ba1-b7d1-1bfb973d4015");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Base table", "T", "Existing combination tree: header and coefficient rows.", GH_ParamAccess.tree);
        p.AddTextParameter("Moving cases", "C", "Cases output of Moving Static Analysis, in its original order.", GH_ParamAccess.list);
        p.AddTextParameter("Base combination", "B", "Exact existing row to accompany each alternative traffic position.", GH_ParamAccess.item);
        p.AddNumberParameter("Traffic factor", "F", "Explicit non-negative multiplier; no normative default is implied.", GH_ParamAccess.item, 1);
        p.AddBooleanParameter("Isolated positions", "I", "Also create one traffic-only diagnostic combination per position.", GH_ParamAccess.item, false);
        p.AddTextParameter("Action prefix", "P", "Unique prefix for generated Action IDs.", GH_ParamAccess.item, "Q_MOV");
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Table", "T", "Expanded combination tree for Build BridGHe Model.", GH_ParamAccess.tree);
        p.AddTextParameter("Action IDs", "IDs", "Connect to Bridge Action / Action ID with Cases in matching order.", GH_ParamAccess.list);
        p.AddTextParameter("Cases", "C", "Connect to Bridge Action / Case; no manually typed result names.", GH_ParamAccess.list);
        p.AddTextParameter("Mapping", "M", "Action ID = native FEM position.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var cases = new List<string>(); string basis = null, prefix = null; double factor = 1; bool isolated = false;
        if (!da.GetDataTree(0, out GH_Structure<IGH_Goo> table) || !da.GetDataList(1, cases) || !da.GetData(2, ref basis)
            || !da.GetData(3, ref factor) || !da.GetData(4, ref isolated) || !da.GetData(5, ref prefix)) return;
        if (cases.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Run Moving Static Analysis to populate the alternatives."); return; }
        string Cell(IGH_Goo g) => g?.ScriptVariable() is IFormattable n ? n.ToString(null, CultureInfo.InvariantCulture) : g?.ScriptVariable()?.ToString() ?? "";
        var result = BridgeMovingWorkflow.Combinations(table.Branches.Select(b => b.Select(Cell).ToArray()).ToArray(), cases, basis, factor, isolated, prefix);
        var tree = new GH_Structure<GH_String>();
        for (int i = 0; i < result.Rows.Length; i++) tree.AppendRange(result.Rows[i].Select(s => new GH_String(s)), new GH_Path(i));
        da.SetDataTree(0, tree); da.SetDataList(1, result.ActionIds); da.SetDataList(2, result.Cases);
        da.SetDataList(3, result.ActionIds.Zip(result.Cases, (id, c) => id + " = " + c));
    }
}
