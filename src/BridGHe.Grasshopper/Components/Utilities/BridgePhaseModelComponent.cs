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
        var output = GH_Model.FromModel(BridgePhaseModel.Create(model.Value, name, ids, property.Value, clear), model);
        // F6 chain D: the GPC input of the output from the GPC input of the source (the analysis copies it instead of converting the FEM
        // output); if it cannot be built the analysis converts the FEM output as before.
        try
        {
            var source = model.GpcInput;
            var replacement = Common.Gpc.GpcModels.BeamProperty(property.Value, source.Report.Units);
            output.AttachInput(Common.Gpc.GpcModelTransforms.PhaseModel(source, name, ids, replacement, clear,
                Common.Gpc.GpcIdentity.Derive(InstanceGuid, "phase-model:" + source.Model.Guid.ToString("D") + ":" + da.Iteration.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is InvalidOperationException || e is System.Runtime.Serialization.SerializationException)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "GPC phase model not built (" + e.Message + "): the analysis converts the FEM model."); }
        da.SetData(0, output);
    }
}
