using System;
using System.Collections.Generic;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace BridGHe.Grasshopper.Components.Definitions
{
    public class LoadCaseComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the LoadCaseComponent class.
        /// </summary>
        public LoadCaseComponent()
          : base("Load Case", "LC", "A load case definition", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_DEFINITIONS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The load case name", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Gravity NSM", "NSM", "Apply gravity to non-structural mass", GH_ParamAccess.item, false);

            //pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("LoadCase", "LC", "The defined load case", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";
            bool nsm = false;

            if (!DA.GetData(0, ref name))
                return;
            if (!DA.GetData(1, ref nsm))
                return;


            // P2K (P1): the check of the former Dead case kept, with its message (the GPC name of the former LoadCaseModel.Dead).
            if (name == Common.Gpc.Builders.GpcCaseBuilders.Dead && nsm)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid nsm");
                return;
            }

            Message = name;

            // P2K (P1): the case by its record alone, built on GPC from the inputs.
            GpcOnlyElements.Set(DA, 0, GpcOnlyProperties.Case(this, () => Common.Gpc.Builders.GpcCaseBuilders.Case(name, nsm)));
        }

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        public override Guid ComponentGuid => new Guid("977e4b26-8094-4d71-b165-f0f73b2282cd");
    }
}