using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.BridgeChecks;

public class BuildBridgeModel : GH_Component
{
    protected bool _run;
    public BuildBridgeModel() : base("Build BridGHe Model", "BuildBridge", "Assemble numbered phases and incremental actions. One combination column per action, including unified shrinkage.", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("16695837-3f63-4305-a1ff-4491826dc3fe");
    protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
    public override void CreateAttributes()
    {
        var attributes = new ComponentAttributes.ComponentOneButtonAttributes(this, "Run");
        attributes.ButtonPressed += () => { _run = true; ExpireSolution(true); };
        m_attributes = attributes;
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Models", "M", "Reference model(s), N-mm-MPa, with matching beam IDs/geometry. Action objects carry their own analysed sources; list order does not identify cases.", GH_ParamAccess.list);
        p.AddGenericParameter("Phases", "P", "List of numbered Bridge Phase objects. Sorted by Number, with unique action identifiers.", GH_ParamAccess.list);
        p.AddGenericParameter("Combinations", "C", "Table/tree: header label + action IDs, followed by combination name + coefficients. A shrinkage coefficient applies to both primary strain and secondary FEM forces.", GH_ParamAccess.tree);
        p.AddIntegerParameter("Calculation", "Calc", "0 Cumulative: existing numerical method grouped by section behavior; 1 LinearHistory: gPC sequential N–Mx section history. Neither runs global construction stages automatically.", GH_ParamAccess.item, 0);
        p.AddBooleanParameter("Additional Phase Members", "AP", "Allow later FEM sources to contain additional members (e.g. transverse slab strips). Checks include only the FIRST reference model's beams; all must exist with identical IDs/axes in every phase. Full source reactions remain available via Phases.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Model", "M", "Assembled model with source results and phase recipe.", GH_ParamAccess.item);
        p.AddTextParameter("Phases", "P", "Ordered phase/action summary for a Panel.", GH_ParamAccess.list);
        p.AddTextParameter("Diagnostics", "D", "Unused actions and scope of the selected calculation.", GH_ParamAccess.list);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var models = new List<GH_Model>(); var phases = new List<BridgeWorkflowGoo>(); int method = 0;
        if (!da.GetDataList(0, models) || !da.GetDataList(1, phases) || !da.GetDataTree(2, out GH_Structure<IGH_Goo> combinations) || !da.GetData(3, ref method)) return;
        bool additional = false; da.GetData(4, ref additional);
        if (!_run) return;
        _run = false;
        try
        {
            if (models.Any(m => !WorkflowInputs.HasModel(m)) || phases.Any(p => p?.Value is not BridgePhaseDefinition)) throw new ArgumentException("Connect models and Bridge Phase objects to their corresponding inputs.");
            string Cell(IGH_Goo goo) => goo?.ScriptVariable() is IFormattable n ? n.ToString(null, CultureInfo.InvariantCulture) : goo?.ScriptVariable()?.ToString() ?? "";
            var definitions = phases.Select(p => (BridgePhaseDefinition)p.Value).ToArray();
            var table = combinations.Branches.Select(b => b.Select(Cell).ToArray()).ToArray();
            // L3b: a model given by its GPC input alone, or an action without a FEM source: the bridge model on the GPC data alone; the output is
            // given by the input of its structure with the bridge attached; a build that fails is the error of the component, with no output.
            if (GpcOnlyModels.Any(models) || definitions.SelectMany(d => d.Actions).Any(a => a.Source == null && !string.IsNullOrWhiteSpace(a.Case)))
            {
                var gpcBuilt = Common.Gpc.GpcBridgeBuilder.Build(models.Select(m => Common.Gpc.GpcBridgeSource.Reference(m.GpcInput, m.GpcActionResults)).ToArray(), definitions, table,
                    (BridgeWorkflowMethod)method, Settings.Instance.Tolerance, additional, Common.Gpc.GpcIdentity.Derive(InstanceGuid, "bridge-model"), structureInput: true);
                var only = GH_Model.FromGpc(gpcBuilt.Input); only.AttachBridge(gpcBuilt.Bridge);
                var onlyDrafts = Common.Gpc.GpcBridgeBuilder.ModelDrafts(models[0].GpcDrafts, gpcBuilt.Bridge.Records);
                if (onlyDrafts != null) only.AttachDrafts(onlyDrafts);
                Finish(da, only, gpcBuilt.Summary, gpcBuilt.Warnings, method);
                return;
            }
            var result = BridgeWorkflowBuilder.Build(models.Select(m => m.Value).ToArray(), definitions, table, (BridgeWorkflowMethod)method, Settings.Instance.Tolerance, additional);
            var output = GH_Model.FromModel(result.Model, models[0]);
            // F6 chain C: the GPC bridge model (merged actions as a GPC dataset, D3 recipe) for the checks with the GPC workflow executor, built
            // from the GPC models only: the input model of every reference, the analysed GPC model of every action source. The merged FEM model
            // above stays the Model output for the components that still read it (until the GH types carry GPC objects).
            // R6.4 (4): the summary and the diagnostics of the GPC build when it is built (the same rows as the FEM build, compared by the
            // regression gpc-bridge-model); those of the FEM build when it is not (the FEM path of the checks, until the decision on it).
            string[] summary = result.Summary, warnings = result.Warnings;
            try
            {
                var references = models.Select(m => Common.Gpc.GpcBridgeSource.Reference(m.GpcInput, m.GpcActionResults)).ToArray();
                var built = Common.Gpc.GpcBridgeBuilder.Build(references, definitions, table, (BridgeWorkflowMethod)method, Settings.Instance.Tolerance, additional,
                    Common.Gpc.GpcIdentity.Derive(InstanceGuid, "bridge-model"));
                output.AttachBridge(built.Bridge); summary = built.Summary; warnings = built.Warnings;
                // R6.4 (2), extension (α): the drafts of the elements of the merged model (those of the first reference model without their
                // loads, with the slab strains of the strain actions), for Decompose Model and Super Element Filter.
                var drafts = Common.Gpc.GpcBridgeBuilder.ModelDrafts(models[0].GpcDrafts, built.Bridge.Records);
                if (drafts != null) output.AttachDrafts(drafts);
                // Until R6.5 the test host compares them with those of the FEM build on every run (GpcDraftCheck).
                Common.Gpc.GpcDraftCheck.Attached("Build BridGHe Model summary", string.Join("\n", summary) + "\n--\n" + string.Join("\n", warnings),
                    () => string.Join("\n", result.Summary) + "\n--\n" + string.Join("\n", result.Warnings));
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is InvalidOperationException || e is KeyNotFoundException || e is System.Runtime.Serialization.SerializationException)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Modello di ponte GPC non costruito (" + e.Message + "): Super Element Check richiede il modello di ponte GPC e non verifica questo modello."); }
            Finish(da, output, summary, warnings, method);
        }
        catch (Exception e) { Message = "Error"; AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }

    private void Finish(IGH_DataAccess da, GH_Model output, string[] summary, string[] warnings, int method)
    {
        da.SetData(0, output);
        da.SetDataList(1, summary); da.SetDataList(2, warnings);
        foreach (var warning in warnings.Where(w => w.Contains("not active") || w.Contains("primary shrinkage only"))) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        Message = method == 0 ? "Cumulative" : "Linear history";
    }
}
