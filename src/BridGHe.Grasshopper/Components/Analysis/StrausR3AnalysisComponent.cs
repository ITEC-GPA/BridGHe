using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace BridGHe.Grasshopper.Components.Analysis
{
    public class StrausR3AnalysisComponent : GH_Component
    {
        protected bool _run;
        private GH_Model _model;
        // F6 C4: the analysed GPC model next to the .st7 (BGHR2), restored at the first solution after opening.
        private readonly Helpers.SavedGpcResults _saved = new Helpers.SavedGpcResults();
        // R6.4 (2), option 1 of P2Q: the drafts of the analysed model are those of the input, attached again after opening (not saved).
        private readonly Helpers.AnalysisDrafts _drafts = new Helpers.AnalysisDrafts();
        private bool _just_loaded;

        /// <summary>
        /// Initializes a new instance of the StrausAnalysisComponent class.
        /// </summary>
        public StrausR3AnalysisComponent()
          : base("Straus R3 Analysis", "SA", "Model Analisys with Straus7", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_ANALYSIS)
        {
            _run = false;
            _model = new GH_Model();
            _just_loaded = false;
        }

        public override void CreateAttributes()
        {
            ComponentAttributes.ComponentOneButtonAttributes attr = new ComponentAttributes.ComponentOneButtonAttributes(this, "Run");
            attr.ButtonPressed += () =>
            {
                _run = true;
                ExpireSolution(true);
            };
            m_attributes = attr;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Model", "M", "The FEM model", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Solver", "ST", "The type of solver to use", GH_ParamAccess.item, 0);
            Param_Integer solverParam = (Param_Integer)pManager[1];
            for (int i = 0; i < Common.Helpers.StrausR3Helper.Solvers.Length; i++)
                solverParam.AddNamedValue(Common.Helpers.StrausR3Helper.Solvers[i], i);
            pManager.AddTextParameter("Analysis Results", "AR", "The analysis result cases or combination to returns", GH_ParamAccess.list);
            pManager[2].Optional = true;
            pManager.AddTextParameter("Output Path", "O", "The full path of the file model to create", GH_ParamAccess.item);
            int n = pManager.AddBooleanParameter("Keep Running", "K", "Run analysis automatically when input is chaged", GH_ParamAccess.item, false);
            pManager[n].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Model", "M", "The FEM model", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = "";
            GH_Model model = null;
            int solver = 0;
            string outputPath = "";
            bool keepRunning = false;
            if (!DA.GetData("Model", ref model))
                return;
            if (!DA.GetData("Solver", ref solver))
                return;
            if (!DA.GetData("Output Path", ref outputPath))
                return;
            DA.GetData("Keep Running", ref keepRunning);

            if (Path.GetExtension(outputPath).ToLower() != ".st7")
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid extension in file name");
                return;
            }

            List<string> names;
            // R6.4 (1): the result names from the GPC input of the model (an input that cannot be converted gives its error here).
            try { names = Common.Gpc.GpcResultSelection.Catalog(model.GpcInput?.Model, solver).Select(e => e.Name).ToList(); }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); return; }

            // Connect the ValueList component on the analysis results input item
            IEnumerable<IGH_Param> keys_analysisResultLists = Params.Input[2].Sources.
                Where(s => s.GetType() == typeof(GH_ValueList));
            foreach (GH_ValueList vallist in keys_analysisResultLists.Cast<GH_ValueList>())
            {
                if (vallist.ListMode != GH_ValueListMode.CheckList)
                    vallist.ListMode = GH_ValueListMode.CheckList;

                ComponentsHelper.SetValueList(vallist, names);
                vallist.NickName = "Results";
            }

            var requested = new List<string>();
            DA.GetDataList("Analysis Results", requested);
            // R6.5 P1 lane 3e: a model given by its GPC input alone is analysed from it and gives a GPC-only model with the analysed GPC model.
            bool gpcOnly = GpcOnlyModels.WithoutFem(model);
            if (_run || keepRunning)
            {
                _run = false;
                try
                {
                    List<string> warnings; GPC.Model.Models.Model analysed;
                    if (gpcOnly)
                    {
                        var input = GpcOnlyModels.InputOf(model);
                        analysed = Common.Straus.StrausAnalysis.Run(input, outputPath, solver, requested, false, out warnings);
                        _model = GpcOnlyModels.SameElements(input, model, input);
                    }
                    else
                    {
                        var result = Common.Straus.StrausAnalysis.Run(model.Value, model.GpcInput, outputPath, solver, requested, Settings.Instance.Tolerance, false, out warnings, out analysed);
                        _model = GH_Model.FromModel(result, model);
                    }
                    _model.AttachGpc(analysed);
                    // R6.4 (2), extension (α): the output keeps the elements of its input (with results): it carries the drafts of the input.
                    _drafts.Record(model, _model);
                    foreach (string warning in warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                    string saved = _saved.Record(_model, outputPath); if (saved != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, saved);
                    Message = "Done";
                }
                catch (Exception ex)
                {
                    _model = new GH_Model(); _saved.Record(null, null); _drafts.Clear();
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    Message = "Error";
                    if (gpcOnly) return; // lane 3: a refusal gives an error and no output
                }
            }
            if (_saved.Pending) { string restored = _saved.Restore(_model, outputPath); if (restored != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, restored); }
            if (_drafts.Pending) { string changed = _drafts.Restore(model, _model); if (changed != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, changed); }
            // R6.1 (decision 1 of 6/11): the analysed model of a former file is read without its results.
            if (_model.LegacyNotice != null && !_model.HasAnalysedGpc) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _model.LegacyNotice);
            DA.SetData(0, _model);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "Reset", (s, e) => { _model = new GH_Model(); _saved.Record(null, null); _drafts.Clear(); });
        }

        public override bool Write(GH_IWriter writer)
        {
            bool success = base.Write(writer);
            if (!success)
                return false;

            _model.Write(writer, false);
            _saved.Write(writer);
            _drafts.Write(writer);

            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            bool success = base.Read(reader);
            if (!success)
                return false;

            _model.Read(reader);
            _saved.Read(reader);
            _drafts.Read(reader);
            _just_loaded = true;

            return true;
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            if (_just_loaded)
            {
                if (!Params.Input[0].VolatileData.IsEmpty)
                {
                    GH_Model input_model = (GH_Model)Params.Input[0].VolatileData.get_Branch(0)[0];
                    ModelHelper.RestoreModelGuids(input_model, ref _model);
                    _just_loaded = false;
                }
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("c9985217-7bc3-4eb0-a8a1-d251e17c8140");
    }
}
