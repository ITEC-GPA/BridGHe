using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.BridgeChecks;

internal static class WorkflowInputs
{
    internal static string Case(object value) => value switch {
        GH_LoadCase c => c.Value?.Name, CaseModel c => c.Name,
        GH_String s => s.Value, string s => s,
        GH_ObjectWrapper w => Case(w.Value), _ => throw new ArgumentException("Connect a load case or its exact name.") };
    internal static void Catalogue(GH_Component component, int port, ModelModel model)
    {
        if (model == null) return;
        var names = model.Beams.SelectMany(b => b.Results).OfType<BeamForceResultModel>().Select(f => f.LoadCase.Name).Distinct().OrderBy(n => n).ToList();
        foreach (var list in component.Params.Input[port].Sources.OfType<GH_ValueList>())
        {
            list.ListMode = GH_ValueListMode.DropDown; ComponentsHelper.SetValueList(list, names);
        }
    }
}

public sealed class BridgeActionComponent : GH_Component
{
    public BridgeActionComponent() : base("Bridge Action", "Action", "Bind one incremental FEM case to a stable action identifier and a section behavior.", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("56b74e2e-962a-4ad6-905e-105647144ea1");
    protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Action ID", "ID", "Stable identifier used by combination headers; independent of phase number and source model order.", GH_ParamAccess.item);
        p.AddGenericParameter("Model", "M", "Analysed incremental source model, N-mm-MPa.", GH_ParamAccess.item);
        p.AddGenericParameter("Case", "LC", "Load case object or exact analysed case name. A Value List offers the available cases.", GH_ParamAccess.item);
        p.AddIntegerParameter("Behavior", "B", "0 Steel; 1 Long term n infinity; 2 Short term n0. Use Bridge Shrinkage Action for shrinkage.", GH_ParamAccess.item, 0);
        p.AddTextParameter("Beam IDs", "IDs", "Optional selection of beam IDs. Empty applies this action to all beams.", GH_ParamAccess.list); p[4].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Action", "A", "Incremental action for Bridge Phase.", GH_ParamAccess.item);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        string key = null; GH_Model model = null; object lc = null; int behavior = 0; var ids = new List<string>();
        if (!da.GetData(0, ref key) || !da.GetData(1, ref model)) return;
        if (model?.Value == null || !model.Value.Beams.Any(b => b.Results.OfType<BeamForceResultModel>().Any()))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Awaiting analysed FEM results. Run the source Straus analysis first."); return; }
        WorkflowInputs.Catalogue(this, 2, model?.Value);
        if (!da.GetData(2, ref lc) || !da.GetData(3, ref behavior)) return;
        da.GetDataList(4, ids);
        try
        {
            if (string.IsNullOrWhiteSpace(key) || model?.Value == null || behavior < 0 || behavior > 2) throw new ArgumentException("Supply an action ID, an analysed model and a behavior from 0 to 2.");
            da.SetData(0, new BridgeWorkflowGoo(new BridgeActionDefinition { Key = key, Source = model.Value, Case = WorkflowInputs.Case(lc), Behavior = (BridgeActionBehavior)behavior, BeamIds = ids.ToArray() }));
            Message = key;
        }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class BridgeShrinkageActionComponent : GH_Component
{
    public BridgeShrinkageActionComponent() : base("Bridge Shrinkage Action", "ShrinkageAction", "One shrinkage action: concrete strain increment plus secondary FEM forces. A single combination coefficient scales both.", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("1c7b96b8-3bfb-4df0-a672-cae395330a79");
    protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Action ID", "ID", "Single stable identifier for both shrinkage contributions in combinations.", GH_ParamAccess.item);
        p.AddGenericParameter("Model", "M", "Source of secondary FEM forces calculated on the n shrinkage section.", GH_ParamAccess.item); p[1].Optional = true;
        p.AddGenericParameter("Case", "LC", "Secondary FEM load case object or exact name.", GH_ParamAccess.item); p[2].Optional = true;
        p.AddNumberParameter("Strain increment", "Δεcs", "Concrete shrinkage increment [dimensionless], negative for contraction. Supply the same increment to Composite Shrinkage Actions before Straus. Never enter cumulative shrinkage twice.", GH_ParamAccess.item);
        p.AddTextParameter("Beam IDs", "IDs", "Beams with this composite action; empty means all beams. Select longitudinal composite girders in a grillage.", GH_ParamAccess.list); p[4].Optional = true;
        p.AddBooleanParameter("Primary Only", "P", "Explicitly omit global restraint forces. Only valid when Model/Case are not used for FEM forces.", GH_ParamAccess.item, false);
        p.AddIntegerParameter("Response", "R", "0 n shrinkage (default); 1 n0 for an explicitly instantaneous concrete strain increment. FEM forces, when present, must use the same response.", GH_ParamAccess.item, 0);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Action", "A", "Unified shrinkage action for Bridge Phase.", GH_ParamAccess.item);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        string key = null; GH_Model model = null; object lc = null; double strain = 0; bool primary = false; int response = 0; var ids = new List<string>();
        if (!da.GetData(0, ref key) || !da.GetData(3, ref strain) || !da.GetData(5, ref primary) || !da.GetData(6, ref response)) return;
        da.GetData(1, ref model); WorkflowInputs.Catalogue(this, 2, model?.Value); da.GetData(2, ref lc); da.GetDataList(4, ids);
        if (!primary && model != null && (model.Value == null || !model.Value.Beams.Any(b => b.Results.OfType<BeamForceResultModel>().Any())))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Awaiting secondary shrinkage FEM results. Run Straus first."); return; }
        try
        {
            if (string.IsNullOrWhiteSpace(key) || !double.IsFinite(strain) || strain > 0) throw new ArgumentException("Supply a stable ID and a finite, non-positive concrete shrinkage increment.");
            if (response is < 0 or > 1) throw new ArgumentException("Response must be 0 (n shrinkage) or 1 (n0).");
            if (!primary && (model?.Value == null || lc == null)) throw new ArgumentException("Connect secondary FEM results, or explicitly enable Primary Only.");
            if (primary && lc != null) throw new ArgumentException("Primary Only cannot contain a FEM case.");
            if (primary) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Only section autostresses: global restraint effects are excluded.");
            da.SetData(0, new BridgeWorkflowGoo(new BridgeActionDefinition { Key = key, Source = model?.Value, Case = lc == null ? null : WorkflowInputs.Case(lc),
                Behavior = response == 0 ? BridgeActionBehavior.Shrinkage : BridgeActionBehavior.ShortTerm, Strain = strain, BeamIds = ids.ToArray(), PrimaryOnly = primary }));
            Message = key;
        }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}

public sealed class BridgeConstructionPhaseComponent : GH_Component
{
    public BridgeConstructionPhaseComponent() : base("Bridge Phase", "Phase", "Numbered construction phase containing incremental actions. Order is independent of source model order.", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("0118806b-7e8b-4d0e-8949-fe5965c650e5");
    protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddIntegerParameter("Number", "N", "Unique chronological order, e.g. 10, 20, 30.", GH_ParamAccess.item);
        p.AddTextParameter("Name", "Name", "Readable phase label. Renaming does not change action identifiers.", GH_ParamAccess.item);
        p.AddIntegerParameter("Section State", "S", "0 Steel; 1 Composite active; 2 Concrete excluded (LinearHistory only). This describes the section, not automatic activation of global Straus elements.", GH_ParamAccess.item, 1);
        p.AddGenericParameter("Actions", "A", "List of Bridge Action / Bridge Shrinkage Action objects. Empty allows an activation-only phase.", GH_ParamAccess.list); p[3].Optional = true;
        p.AddNumberParameter("Day", "t", "Optional elapsed days, metadata only: no automatic age-dependent creep model.", GH_ParamAccess.item); p[4].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Phase", "P", "Numbered phase for Build BridGHe Model.", GH_ParamAccess.item);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        int order = 0, state = 1; string name = null; double day = 0; var values = new List<BridgeWorkflowGoo>();
        if (!da.GetData(0, ref order) || !da.GetData(1, ref name) || !da.GetData(2, ref state)) return;
        da.GetDataList(3, values); bool hasDay = da.GetData(4, ref day);
        if (values.Any(v => v?.Value == null))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Awaiting valid actions from upstream components."); return; }
        if (order < 0 || string.IsNullOrWhiteSpace(name) || state < 0 || state > 2 || hasDay && (!double.IsFinite(day) || day < 0)
            || values.Any(v => v?.Value is not BridgeActionDefinition))
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid phase number, name, state, day or action object."); return; }
        // One component can emit multiple phases via GH list matching; the explicit unique order identifies each instance.
        byte[] id = InstanceGuid.ToByteArray();
        // Id must survive renumbering: each tree iteration has a stable slot, independent of Number.
        var iteration = BitConverter.GetBytes(da.Iteration); for (int i = 0; i < 4; i++) id[i] ^= iteration[i];
        da.SetData(0, new BridgeWorkflowGoo(new BridgePhaseDefinition { Id = new Guid(id), Order = order, Name = name, State = (BridgeSectionState)state,
            Day = hasDay ? day : null, Actions = values.Select(v => (BridgeActionDefinition)v.Value).ToArray() }));
        Message = $"{order} · {name}";
    }
}
