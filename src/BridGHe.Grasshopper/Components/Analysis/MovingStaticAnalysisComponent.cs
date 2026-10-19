using System;
using BridGHe.Common.Straus;
using BridGHe.Grasshopper.DataTypes.Fem;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Analysis
{
    public sealed class MovingStaticAnalysisComponent : GH_Component
    {
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
        public MovingStaticAnalysisComponent() : base("Moving Static Analysis", "MLAnalysis", "Genera i casi statici dei percorsi mobili, esegue LSA e associa tutti i risultati agli elementi. Richiede API e modulo Moving Load R3.", Helpers.CategoryNameConstants.CATEGORY_MODEL, Helpers.CategoryNameConstants.SUBCATEGORY_LOADS) { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        { p.AddGenericParameter("Model", "M", "Modello con percorsi mobili.", GH_ParamAccess.item); p.AddTextParameter("Output path", "O", "Percorso completo .st7.", GH_ParamAccess.item); p.AddBooleanParameter("Run", "R", "Eseguire analisi.", GH_ParamAccess.item, false); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddGenericParameter("Model", "M", "Modello con risultati di tutti i casi mobili generati.", GH_ParamAccess.item); p.AddTextParameter("Cases", "C", "Nomi dei casi mobili generati.", GH_ParamAccess.list); }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            GH_Model input = null; string path = null; bool run = false;
            if (!da.GetData(0, ref input) || !da.GetData(1, ref path) || !da.GetData(2, ref run) || !run) return;
            try
            {
                var model = StrausAnalysis.Run(input.Value, path, 0, Array.Empty<string>(), Settings.Instance.Tolerance, true, out var warnings);
                foreach (string warning in warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                da.SetData(0, GH_Model.FromModel(model, input)); da.SetDataList(1, model.GeneratedMovingCases.Values);
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
        }
        public override Guid ComponentGuid => new Guid("b6edaa43-c87c-40b4-8ce7-03260a524fd4");
    }
}
