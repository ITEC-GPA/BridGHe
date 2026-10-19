using System;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeModelAuditComponent : BridgeUtilityComponent
{
    public BridgeModelAuditComponent() : base("Bridge Model Audit", "Audit", "Read-only model diagnostics: IDs, geometry, properties and disconnected parts. Does not certify stiffness stability or validate every load.", CategoryNameConstants.SUBCATEGORY_TOOLS) { }
    public override Guid ComponentGuid => new Guid("7f95faaf-b353-4a1a-9e3f-c9189baa9272");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Model", "M", "FEM model to inspect.", GH_ParamAccess.item);
        p.AddNumberParameter("Tolerance", "Tol", "Geometric coincidence tolerance in model length units.", GH_ParamAccess.item, 1e-6);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBooleanParameter("No input errors", "OK", "No errors found by the listed checks; warnings remain separate.", GH_ParamAccess.item);
        foreach (var label in new[] { "Severity", "Code", "Element", "Description" }) p.AddTextParameter(label, label, label, GH_ParamAccess.list);
        p.AddGenericParameter("Model", "M", "Unmodified input model; use Preview for geometry.", GH_ParamAccess.item);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        GH_Model model = null; double tolerance = 1e-6;
        if (!da.GetData(0, ref model) || !da.GetData(1, ref tolerance)) return;
        var items = BridgeModelAudit.Inspect(model.Value, tolerance);
        da.SetData(0, !items.Any(i => i.Severity == "Error")); da.SetDataList(1, items.Select(i => i.Severity));
        da.SetDataList(2, items.Select(i => i.Code)); da.SetDataList(3, items.Select(i => i.Element)); da.SetDataList(4, items.Select(i => i.Description)); da.SetData(5, model);
        if (items.Length > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{items.Length} diagnostics: inspect the report outputs.");
    }
}
