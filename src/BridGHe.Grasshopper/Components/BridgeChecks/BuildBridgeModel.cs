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
            if (models.Any(m => m?.Value == null) || phases.Any(p => p?.Value is not BridgePhaseDefinition)) throw new ArgumentException("Connect models and Bridge Phase objects to their corresponding inputs.");
            string Cell(IGH_Goo goo) => goo?.ScriptVariable() is IFormattable n ? n.ToString(null, CultureInfo.InvariantCulture) : goo?.ScriptVariable()?.ToString() ?? "";
            var result = BridgeWorkflowBuilder.Build(models.Select(m => m.Value).ToArray(), phases.Select(p => (BridgePhaseDefinition)p.Value),
                combinations.Branches.Select(b => b.Select(Cell).ToArray()).ToArray(), (BridgeWorkflowMethod)method, Settings.Instance.Tolerance, additional);
            da.SetData(0, GH_Model.FromModel(result.Model, models[0]));
            da.SetDataList(1, result.Summary); da.SetDataList(2, result.Warnings);
            foreach (var warning in result.Warnings.Where(w => w.Contains("not active") || w.Contains("primary shrinkage only"))) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            Message = method == 0 ? "Cumulative" : "Linear history";
        }
        catch (Exception e) { Message = "Error"; AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}
