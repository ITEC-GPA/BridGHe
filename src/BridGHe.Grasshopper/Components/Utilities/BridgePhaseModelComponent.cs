using System;
using System.Collections.Generic;
using BridGHe.Common.Diagnostics;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgePhaseModelComponent : BridgeUtilityComponent
{
    public BridgePhaseModelComponent() : base("Bridge Phase Model", "PhaseModel", "Independent FEM copy for an incremental analysis: override selected beam properties, retain IDs and geometry, invalidate all results and old stage/combination recipes.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("6ed9466e-9e5a-4059-a570-ae1461537a36");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Model", "M", "Base steel FEM model.", GH_ParamAccess.item);
        p.AddTextParameter("Name", "Name", "Name of this independent analysis model.", GH_ParamAccess.item);
        p.AddTextParameter("Beam IDs", "IDs", "Explicit selection of composite longitudinal beams.", GH_ParamAccess.list);
        p.AddGenericParameter("Property", "BP", "Beam property for the selected response n0/n infinity/n shrinkage. Units must match the model.", GH_ParamAccess.item);
        p.AddBooleanParameter("Clear loads", "Clear", "True clears loads, cases and moving paths for a new increment. False retains source loads; results, combinations and stages are always cleared.", GH_ParamAccess.item, true);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Model", "M", "New model, ready to add incremental loads and analyse. Geometry preview uses the replacement section.", GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {
        GH_Model model = null; GH_BeamProperty property = null; string name = null; bool clear = true; var ids = new List<string>();
        if (!da.GetData(0, ref model) || !da.GetData(1, ref name) || !da.GetDataList(2, ids) || !da.GetData(3, ref property) || !da.GetData(4, ref clear)) return;
        da.SetData(0, GH_Model.FromModel(BridgePhaseModel.Create(model.Value, name, ids, property.Value, clear), model));
    }
}
