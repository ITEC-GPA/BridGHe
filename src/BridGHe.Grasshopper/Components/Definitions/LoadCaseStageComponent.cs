using System;
using System.Collections.Generic;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace BridGHe.Grasshopper.Components.Definitions
{
    public class LoadCaseStageComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the LoadCaseComponent class.
        /// </summary>
        public LoadCaseStageComponent()
          : base("Load Case Stage", "LCS", "A load case stage definition", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_DEFINITIONS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The load case name", GH_ParamAccess.item);
            pManager.AddGenericParameter("Operations", "Os", "Operations of a stage", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("LoadCaseStage", "LCS", "The defined load case stage", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";
            List<GH_LoadCaseStageOperation> operations = new List<GH_LoadCaseStageOperation>();
            if (!DA.GetData(0, ref name))
                return;
            if (!DA.GetDataList(1, operations))
                return;

            LoadCaseModel.Stage stage = new LoadCaseModel.Stage(name);
            foreach (GH_LoadCaseStageOperation op in operations)
            {
                stage.Operations.Add(new LoadCaseModel.Stage.Operation(op.Value));
            }
            DA.SetData(0, new GH_LoadCaseStage(stage));
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("5698330d-d3b6-4c35-bbd8-096aee7dfbb3");
    }
}