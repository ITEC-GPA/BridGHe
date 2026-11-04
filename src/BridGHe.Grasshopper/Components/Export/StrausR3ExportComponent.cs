using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.IO;

namespace BridGHe.Grasshopper.Components.Export
{
    public class StrausR3ExportComponent : GH_Component
    {
        protected string _status;
        protected bool _run;

        /// <summary>
        /// Initializes a new instance of the StrausExportComponent class.
        /// </summary>
        public StrausR3ExportComponent()
          : base("Straus7 R3 Export", "ST7 R3", "Export to Straus7", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_EXPORT)
        {
            _status = "";
            _run = false;
        }

        public override void CreateAttributes()
        {
            ComponentAttributes.ComponentOneButtonAttributes attr = new ComponentAttributes.ComponentOneButtonAttributes(this, "Save");
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
            pManager.AddTextParameter("Output Path", "O", "The full path of the file model to create", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Status", "S", "The status of the export", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = "";
            GH_Model model = null;
            string outputPath = "";
            if (!DA.GetData("Model", ref model))
                return;
            if (!DA.GetData("Output Path", ref outputPath))
                return;

            if (Path.GetExtension(outputPath).ToLower() != ".st7")
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid extension in file name");
                return;
            }

            if (_run)
            {
                // F6A: written by the GPC Converter writer (domain preflight first); the former exporter is no longer used.
                var warnings = new List<string>();
                try
                {
                    Common.Gpc.Legacy.FemmAnalysisSeam.Export(model.Value, model.GpcInput, outputPath, Settings.Instance.Tolerance, warnings);
                    Message = "Done";
                }
                catch (Exception ex)
                {
                    Message = "Error";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                }

                foreach (string warning in warnings)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

                _run = false;
            }

            DA.SetData(0, Message);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("fa0981e2-710c-41ed-9a60-98bc102def94");
    }
}
