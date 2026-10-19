using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgePhaseTableComponent : BridgeUtilityComponent
{
    public BridgePhaseTableComponent() : base("Bridge Phase Table", "PhaseTable", "Inspect chronology and Action IDs before Build BridGHe Model. Source availability and engineering compatibility are validated by Build BridGHe Model.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("f118c5fa-aae5-4638-b918-ed8f33b8f749");
    protected override void RegisterInputParams(GH_InputParamManager p) => p.AddGenericParameter("Phases", "P", "Numbered Bridge Phase objects.", GH_ParamAccess.list);
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Phases", "P", "Validated and ordered phase objects.", GH_ParamAccess.list);
        foreach (var label in new[] { "Number", "Name", "Day", "Section state", "Action ID", "Behavior", "Source", "Case", "Strain" }) p.AddTextParameter(label, label, "One row per action; activation-only phases use an empty action field.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var values = new List<BridgeWorkflowGoo>(); if (!da.GetDataList(0, values)) return;
        if (values.Any(v => v?.Value is not BridgePhaseDefinition)) throw new ArgumentException("Expected Bridge Phase objects.");
        var ordered = BridgeWorkflowTools.Ordered(values.Select(v => (BridgePhaseDefinition)v.Value));
        da.SetDataList(0, ordered.Select(p => new BridgeWorkflowGoo(p)));
        var rows = ordered.SelectMany(p => (p.Actions.Length == 0 ? new BridgeActionDefinition[] { null } : p.Actions).Select(a => new[] {
            p.Order.ToString(), p.Name, p.Day?.ToString("G", System.Globalization.CultureInfo.InvariantCulture) ?? "", p.State.ToString(), a?.Key ?? "", a?.Behavior.ToString() ?? "", a?.Source?.Name ?? "", a?.Case ?? "", a?.Strain?.ToString("G", System.Globalization.CultureInfo.InvariantCulture) ?? "" })).ToArray();
        for (int i = 0; i < 9; i++) da.SetDataList(i + 1, rows.Select(r => r[i]));
    }
}
public sealed class BridgeActionCombinationsComponent : BridgeUtilityComponent
{
    public BridgeActionCombinationsComponent() : base("Bridge Action Combinations", "ActionComb", "Create the combination table for Build BridGHe Model from explicit Action IDs and coefficients. No automatic normative factors; omitted actions receive zero.", CategoryNameConstants.SUBCATEGORY_DEFINITIONS) { }
    public override Guid ComponentGuid => new Guid("535694df-7d4b-41ab-b651-c7b1fa05e1ef");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Phases", "P", "Numbered Bridge Phase objects defining available Action IDs.", GH_ParamAccess.list);
        p.AddTextParameter("Action IDs", "IDs", "Column order in the coefficient branches.", GH_ParamAccess.list);
        p.AddTextParameter("Names", "Names", "Combination names, in the same order as sorted coefficient tree paths.", GH_ParamAccess.list);
        p.AddNumberParameter("Coefficients", "F", "One branch per combination, one coefficient per supplied Action ID. User-defined factors.", GH_ParamAccess.tree);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Table", "C", "Wire directly to Combinations of Build BridGHe Model. Header branch plus coefficient rows.", GH_ParamAccess.tree);
        p.AddTextParameter("Omitted actions", "Zero", "Action IDs omitted from headers: zero in every combination.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var phases = new List<BridgeWorkflowGoo>(); var keys = new List<string>(); var names = new List<string>();
        if (!da.GetDataList(0, phases) || !da.GetDataList(1, keys) || !da.GetDataList(2, names) || !da.GetDataTree(3, out GH_Structure<GH_Number> factors)) return;
        if (phases.Any(p => p?.Value is not BridgePhaseDefinition)) throw new ArgumentException("Expected Bridge Phase objects.");
        var input = phases.Select(p => (BridgePhaseDefinition)p.Value).ToArray();
        var rows = BridgeWorkflowTools.CombinationTable(input, keys, names, factors.Branches.Select(b => b.Select(v => v.Value).ToArray()).ToArray());
        var tree = new GH_Structure<IGH_Goo>(); for (int i = 0; i < rows.Length; i++) tree.AppendRange(rows[i].Select(s => (IGH_Goo)new GH_String(s)), new GH_Path(i));
        var omitted = input.SelectMany(p => p.Actions).Select(a => a.Key).Except(keys).ToArray(); da.SetDataTree(0, tree); da.SetDataList(1, omitted);
        if (omitted.Length > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Omitted actions have zero coefficients: " + string.Join(", ", omitted));
    }
}
public sealed class BridgeShrinkageIncrementsComponent : BridgeUtilityComponent
{
    public BridgeShrinkageIncrementsComponent() : base("Bridge Shrinkage Increments", "ShrinkInc", "Convert a supplied cumulative shrinkage history to increments for Bridge Shrinkage Action. Does not predict creep/shrinkage or select a normative law.", CategoryNameConstants.SUBCATEGORY_BRIDGE) { }
    public override Guid ComponentGuid => new Guid("5482bd94-f35a-4057-9207-fd12143e535b");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Days", "t", "Strictly increasing elapsed days.", GH_ParamAccess.list);
        p.AddNumberParameter("Cumulative strain", "eps", "Dimensionless, negative contraction, monotonically non-increasing. Not microstrain.", GH_ParamAccess.list);
        p.AddNumberParameter("Initial strain", "eps0", "Cumulative strain immediately before the first listed phase.", GH_ParamAccess.item, 0);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("Increment", "deps", "Strain increment per phase; connect by matching index to the corresponding Bridge Shrinkage Action.", GH_ParamAccess.list);
        p.AddNumberParameter("Days", "t", "Days corresponding to increments.", GH_ParamAccess.list);
        p.AddNumberParameter("Total increment", "Sum", "Sum of increments = last cumulative strain minus initial strain.", GH_ParamAccess.item);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var days = new List<double>(); var strain = new List<double>(); double initial = 0;
        if (!da.GetDataList(0, days) || !da.GetDataList(1, strain) || !da.GetData(2, ref initial)) return;
        var increments = BridgeWorkflowTools.ShrinkageIncrements(days, strain, initial);
        da.SetDataList(0, increments); da.SetDataList(1, days); da.SetData(2, increments.Sum());
    }
}
