using System;
using BridGHe.Common.Diagnostics;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeDeflectionCamberComponent : BridgeUtilityComponent
{
    public BridgeDeflectionCamberComponent() : base("Bridge Deflection & Camber", "Camber",
        "Extract nodal displacement along a girder, subtract support chord motion and apply an explicit camber factor. N-mm.", CategoryNameConstants.SUBCATEGORY_RESULTS) { }
    public override Guid ComponentGuid => new Guid("aa3891ae-e93d-45c6-b493-5d2efea84758");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Model", "M", "Analysed model with nodal displacements.", GH_ParamAccess.item);
        p.AddGenericParameter("Bridge Geometry", "BG", "Same geometry used for the analysed model.", GH_ParamAccess.item);
        p.AddIntegerParameter("Girder", "G", "Girder ID, one-based.", GH_ParamAccess.item, 1);
        p.AddTextParameter("Case", "LC", "Exact stored case name.", GH_ParamAccess.item);
        p.AddNumberParameter("Step", "S", "Exact stored step.", GH_ParamAccess.item, 0);
        p.AddTextParameter("Step Type", "ST", "Optional exact step type; omitted only when unambiguous.", GH_ParamAccess.item, "");
        p.AddVectorParameter("Direction", "D", "Projection direction; +Z gives negative downward displacement.", GH_ParamAccess.item, Vector3d.ZAxis);
        p.AddNumberParameter("Camber Factor", "F", "Nonnegative multiplier. Camber is minus factor times selected displacement.", GH_ParamAccess.item, 1);
        p.AddBooleanParameter("Relative Camber", "R", "True uses deflection relative to support chord; false uses absolute displacement.", GH_ParamAccess.item, true);
        p.AddNumberParameter("Display Scale", "V", "Positive scale for curves only; numbers remain actual mm.", GH_ParamAccess.item, 1);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        foreach (string s in new[] { "Stations", "Absolute Displacement", "Relative Deflection", "Camber" })
            p.AddNumberParameter(s, s, "mm; paired by station.", GH_ParamAccess.list);
        p.AddCurveParameter("Deflected Curve", "U", "Polyline through actual displaced FEM nodes, with display scale.", GH_ParamAccess.item);
        p.AddCurveParameter("Camber Curve", "C", "Proposed compensation curve; no input model change.", GH_ParamAccess.item);
        p.AddTextParameter("Source", "LC", "Model/case/step provenance.", GH_ParamAccess.item);
        p.AddTextParameter("Information", "I", "Reference chord and sampling conventions.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        GH_Model model = null; BridgeGeometryGoo geometry = null; int girder = 1; string loadCase = null, type = "";
        double step = 0, factor = 1, scale = 1; bool relative = true; var direction = Vector3d.ZAxis;
        if (!da.GetData(0, ref model) || !da.GetData(1, ref geometry) || !da.GetData(3, ref loadCase)) return;
        da.GetData(2, ref girder); da.GetData(4, ref step); da.GetData(5, ref type); da.GetData(6, ref direction);
        da.GetData(7, ref factor); da.GetData(8, ref relative); da.GetData(9, ref scale);
        var r = BridgeDeflectionCamber.Evaluate(model.Value, geometry.Value, girder, loadCase, step, type, direction, factor, relative, scale);
        da.SetDataList(0, r.Stations); da.SetDataList(1, r.Absolute); da.SetDataList(2, r.Relative); da.SetDataList(3, r.Camber);
        da.SetData(4, r.Deflected); da.SetData(5, r.Cambered); da.SetData(6, r.Source); da.SetDataList(7, r.Diagnostics);
    }
}
