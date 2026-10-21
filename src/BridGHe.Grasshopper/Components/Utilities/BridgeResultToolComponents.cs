using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Models;
using BridGHe.Common.Checks;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeResultCasesComponent : BridgeUtilityComponent
{
    public BridgeResultCasesComponent() : base("Bridge Result Cases", "ResultCases", "List cases/steps actually present in stored results, with type and record count. Includes generated moving cases.", CategoryNameConstants.SUBCATEGORY_RESULTS) { }
    public override Guid ComponentGuid => new Guid("4a919ef3-341d-444c-be04-66918e1ea8ec");
    protected override void RegisterInputParams(GH_InputParamManager p) => p.AddGenericParameter("Model", "M", "Analysed FEM model.", GH_ParamAccess.item);
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Cases", "LC", "Distinct stored case names for envelope selection.", GH_ParamAccess.list);
        p.AddTextParameter("Case by step", "Case", "Case name corresponding to each following row.", GH_ParamAccess.list);
        p.AddNumberParameter("Step", "Step", "Stored step identifier, not necessarily a physical distance/time.", GH_ParamAccess.list);
        p.AddTextParameter("Step type", "Type", "Solver step type.", GH_ParamAccess.list);
        p.AddTextParameter("Result type", "R", "Stored result class.", GH_ParamAccess.list);
        p.AddIntegerParameter("Count", "Count", "Number of result records in this row.", GH_ParamAccess.list);
        p.AddTextParameter("Units", "U", "Model unit system.", GH_ParamAccess.item);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        GH_Model model = null; if (!da.GetData(0, ref model)) return;
        var all = model.Value.Elements.SelectMany(e => e.Results).ToArray();
        if (all.Any(r => r.LoadCase == null || string.IsNullOrWhiteSpace(r.LoadCase.Name) || !double.IsFinite(r.StepNum))) throw new ArgumentException("A result has invalid case/step identity.");
        var rows = all.GroupBy(r => (Case: r.LoadCase.Name, Step: r.StepNum, Type: r.StepType, Result: r.GetType().Name)).OrderBy(g => g.Key.Case, StringComparer.Ordinal).ThenBy(g => g.Key.Step).ThenBy(g => g.Key.Type, StringComparer.Ordinal).ThenBy(g => g.Key.Result, StringComparer.Ordinal).ToArray();
        da.SetDataList(0, rows.Select(g => g.Key.Case).Distinct()); da.SetDataList(1, rows.Select(g => g.Key.Case)); da.SetDataList(2, rows.Select(g => g.Key.Step));
        da.SetDataList(3, rows.Select(g => g.Key.Type)); da.SetDataList(4, rows.Select(g => g.Key.Result)); da.SetDataList(5, rows.Select(g => g.Count())); da.SetData(6, model.Value.Units.ToString());
        if (all.Length == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The model has no stored results.");
    }
}

public sealed class BridgeResultComparisonComponent : BridgeUtilityComponent
{
    public BridgeResultComparisonComponent() : base("Bridge Result Comparison", "Compare", "Compare repeated analyses or sampled mesh studies by explicit physical keys. No interpolation. Criterion: abs(delta) <= absolute tolerance + relative tolerance * abs(reference).", CategoryNameConstants.SUBCATEGORY_RESULTS) { }
    public override Guid ComponentGuid => new Guid("16496e6c-8199-44dd-88e9-72a4c6e0ece6");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("Reference keys", "K0", "Unique physical labels, e.g. girder/station/case/quantity. Do not rely on element IDs that change with remeshing.", GH_ParamAccess.list);
        p.AddNumberParameter("Reference values", "V0", "Reference numerical values; all comparisons must use coherent units.", GH_ParamAccess.list);
        p.AddTextParameter("Candidate keys", "K1", "Same physical key set, any order.", GH_ParamAccess.list);
        p.AddNumberParameter("Candidate values", "V1", "Candidate values in the same units as the reference.", GH_ParamAccess.list);
        p.AddNumberParameter("Absolute tolerance", "Abs", "Tolerance in the units of the compared quantity.", GH_ParamAccess.item, 1e-6);
        p.AddNumberParameter("Relative tolerance", "Rel", "Dimensionless; 0.01 = 1 percent.", GH_ParamAccess.item, 0.01);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddBooleanParameter("All within tolerance", "OK", "All supplied comparisons pass; does not establish mesh convergence by itself.", GH_ParamAccess.item);
        p.AddTextParameter("Key", "K", "Reference order.", GH_ParamAccess.list);
        p.AddNumberParameter("Difference", "dV", "Candidate minus reference.", GH_ParamAccess.list);
        p.AddNumberParameter("Relative difference", "Rel", "Difference / abs(reference). NaN for non-zero difference against zero.", GH_ParamAccess.list);
        p.AddBooleanParameter("Within tolerance", "Pass", "Per-key comparison result.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var k0 = new List<string>(); var k1 = new List<string>(); var v0 = new List<double>(); var v1 = new List<double>(); double abs = 1e-6, rel = .01;
        if (!da.GetDataList(0, k0) || !da.GetDataList(1, v0) || !da.GetDataList(2, k1) || !da.GetDataList(3, v1) || !da.GetData(4, ref abs) || !da.GetData(5, ref rel)) return;
        var rows = BridgeResultComparison.Compare(k0, v0, k1, v1, abs, rel);
        da.SetData(0, rows.All(r => r.WithinTolerance)); da.SetDataList(1, rows.Select(r => r.Key)); da.SetDataList(2, rows.Select(r => r.Difference));
        da.SetDataList(3, rows.Select(r => r.RelativeDifference)); da.SetDataList(4, rows.Select(r => r.WithinTolerance));
    }
}

public sealed class BridgeCheckSummaryComponent : BridgeUtilityComponent
{
    private const string LabelsDescription = "Optional custom label per result. Leave empty for automatic beam/local station/combination labels. Results without FEM provenance use their tree position and phase.";
    public BridgeCheckSummaryComponent() : base("Bridge Check Summary", "CheckSummary", "Tabulate existing GPC checks across sections, phases and combinations. Preserves original statuses/notes and exposes missing ratios; does not add engineering verifications.", CategoryNameConstants.SUBCATEGORY_RESULTS) { }
    public override Guid ComponentGuid => new Guid("e0447314-cb48-4c24-8e65-6508ba9baa90");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Results", "R", "BridgeStressResult objects from Section/Detail/Super Element Check. Flatten deliberately if combining sections.", GH_ParamAccess.list);
        p.AddTextParameter("Labels", "ID", LabelsDescription, GH_ParamAccess.list); p[1].Optional = true;
    }
    public override bool Read(GH_IO.Serialization.GH_IReader reader)
    {
        bool success = base.Read(reader);
        if (success)
        {
            // Existing definitions store Optional=false and the old tooltip in their input archive.
            Params.Input[1].Optional = true;
            Params.Input[1].Description = LabelsDescription;
        }
        return success;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        foreach (var s in new[] { "Label", "Phase", "Check" }) p.AddTextParameter(s, s, s, GH_ParamAccess.list);
        p.AddNumberParameter("Ratio", "Eta", "Native ratio; NaN means not available, never treated as zero.", GH_ParamAccess.list);
        foreach (var s in new[] { "Status", "Units", "Notes" }) p.AddTextParameter(s, s, "Unmodified GPC " + s.ToLowerInvariant() + ".", GH_ParamAccess.list);
        p.AddNumberParameter("Maximum finite ratio", "Max", "Largest reported finite ratio. NaN when no numeric ratios exist; not an overall approval.", GH_ParamAccess.item);
        p.AddIntegerParameter("Unavailable ratios", "Missing", "Count of absent/non-finite ratios requiring interpretation of Status/Notes.", GH_ParamAccess.item);
        p.AddTextParameter("Governing labels", "Critical", "Labels / phases / checks attaining the maximum finite ratio.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var results = new List<GH_ObjectWrapper>(); var labels = new List<string>();
        if (!da.GetDataList(0, results)) return;
        if (results.Count == 0 || results.Any(r => r?.Value is not BridgeStressResult)) throw new ArgumentException("Supply BridgeStressResult objects from a bridge checker.");
        da.GetDataList(1, labels);
        if (labels.Count == 0)
            labels.AddRange(results.Select((r, i) => BridgeResultLabels.Format((BridgeStressResult)r.Value,
                BridgeResultLabelFormat.Complete, $"{da.ParameterTargetPath(0)}[{i}]")));
        else if (results.Count != labels.Count || labels.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Leave Labels empty for automatic labels, or supply one non-empty custom label per result in the same branch.");
        var rows = results.SelectMany((r, i) => { var result = (BridgeStressResult)r.Value; return BridgeGpc.Checks(result.Stage).Select(c => (Label: labels[i], Phase: result.Stage.Name, Check: c)); }).ToArray();
        double Ratio(int i) => rows[i].Check.Ratio ?? double.NaN;
        da.SetDataList(0, rows.Select(r => r.Label)); da.SetDataList(1, rows.Select(r => r.Phase)); da.SetDataList(2, rows.Select(r => r.Check.Name));
        da.SetDataList(3, rows.Select((_, i) => Ratio(i))); da.SetDataList(4, rows.Select(r => r.Check.Status.ToString())); da.SetDataList(5, rows.Select(r => r.Check.Unit)); da.SetDataList(6, rows.Select(r => r.Check.Note));
        var finite = Enumerable.Range(0, rows.Length).Where(i => double.IsFinite(Ratio(i))).ToArray(); double max = finite.Length == 0 ? double.NaN : finite.Max(Ratio);
        da.SetData(7, max); da.SetData(8, rows.Length - finite.Length); da.SetDataList(9, finite.Where(i => Ratio(i) == max).Select(i => rows[i].Label + " / " + rows[i].Phase + " / " + rows[i].Check.Name));
        if (rows.Length == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No local checks are available in these results.");
    }
}
