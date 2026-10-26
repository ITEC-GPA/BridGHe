using System;
using System.Linq;
using System.Security.Cryptography;
using BridGHe.Common.Helpers;
using BridGHe.Common.Straus;
using BridGHe.Grasshopper.DataTypes.Fem;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Analysis
{
    public sealed class MovingStaticAnalysisComponent : GH_Component
    {
        private bool _run;
        private GH_Model _model = new GH_Model();
        private string _inputHash;
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
        public MovingStaticAnalysisComponent() : base("Moving Static Analysis", "MLAnalysis", "Genera i casi statici dei percorsi mobili, esegue LSA e associa tutti i risultati agli elementi. Richiede API e modulo Moving Load R3.", Helpers.CategoryNameConstants.CATEGORY_MODEL, Helpers.CategoryNameConstants.SUBCATEGORY_LOADS) { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        { p.AddGenericParameter("Model", "M", "Modello con percorsi mobili.", GH_ParamAccess.item); p.AddTextParameter("Output path", "O", "Percorso completo .st7.", GH_ParamAccess.item); p.AddBooleanParameter("Run", "R", "Eseguire analisi.", GH_ParamAccess.item, false); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddGenericParameter("Model", "M", "Modello con risultati di tutti i casi mobili generati.", GH_ParamAccess.item); p.AddTextParameter("Cases", "C", "Nomi dei casi mobili generati.", GH_ParamAccess.list); }
        public override void CreateAttributes()
        {
            var attributes = new ComponentAttributes.ComponentOneButtonAttributes(this, "Run");
            attributes.ButtonPressed += () => { _run = true; ExpireSolution(true); };
            m_attributes = attributes;
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            GH_Model input = null; string path = null; bool run = false;
            if (!da.GetData(0, ref input) || !da.GetData(1, ref path) || !da.GetData(2, ref run)) return;
            if (input?.Value == null || input.Value.Beams.Count == 0) { _model = new GH_Model(); _inputHash = null; return; }
            try
            {
                string hash = Convert.ToHexString(SHA256.HashData(ModelArchive.Write(input.Value)));
                if (_run || run)
                {
                    _run = false;
                    var model = StrausAnalysis.Run(input.Value, path, 0, Array.Empty<string>(), Settings.Instance.Tolerance, true, out var warnings);
                    foreach (string warning in warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                    _model = GH_Model.FromModel(model, input); _inputHash = hash;
                }
                if (_inputHash != hash)
                {
                    _model = new GH_Model(); _inputHash = null;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Run required: no results for the current model, vehicle and path."); Message = "Run required"; return;
                }
                da.SetData(0, _model); da.SetDataList(1, _model.Value.GeneratedMovingCases.OrderBy(c => c.Key).Select(c => c.Value));
                Message = _model.Value.GeneratedMovingCases.Count + " positions";
            }
            catch (Exception ex) { _run = false; _model = new GH_Model(); _inputHash = null; Message = "Error"; AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
        }
        public override bool Write(GH_IWriter writer)
        {
            bool ok = base.Write(writer);
            if (ok && _inputHash != null) { writer.SetString("InputHash", _inputHash); _model.Write(writer.CreateChunk("MovingResults")); }
            return ok;
        }
        public override bool Read(GH_IReader reader)
        {
            bool ok = base.Read(reader); _run = false; _model = new GH_Model(); _inputHash = null;
            if (ok && reader.ItemExists("InputHash") && reader.ChunkExists("MovingResults"))
            { if (_model.Read(reader.FindChunk("MovingResults"))) _inputHash = reader.GetString("InputHash"); }
            return ok;
        }
        public override Guid ComponentGuid => new Guid("b6edaa43-c87c-40b4-8ce7-03260a524fd4");
    }
}
