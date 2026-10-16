using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using System;

namespace BridGHe.Grasshopper.Components.Definitions
{
    public class LoadCaseStageOperationComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the LoadCaseComponent class.
        /// </summary>
        public LoadCaseStageOperationComponent()
          : base("Load Case Stage Op", "LCSO", "A load case stage operation definition", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_DEFINITIONS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            int i = pManager.AddIntegerParameter("OperationCode", "OC", "The operation code", GH_ParamAccess.item);
            Param_Integer mtParam = pManager[i] as Param_Integer;
            foreach (Enum en in Enum.GetValues(typeof(LoadCaseModel.Stage.Operation.Codes)))
                mtParam.AddNamedValue(en.GetDescription(), (int)(LoadCaseModel.Stage.Operation.Codes)en);

            i = pManager.AddIntegerParameter("Object type", "OT", "The operation type", GH_ParamAccess.item);
            mtParam = pManager[i] as Param_Integer;
            foreach (Enum en in Enum.GetValues(typeof(LoadCaseModel.Stage.Operation.ObjectTypes)))
                mtParam.AddNamedValue(en.GetDescription(), (int)(LoadCaseModel.Stage.Operation.ObjectTypes)en);

            pManager.AddTextParameter("Object name", "ON", "Object name", GH_ParamAccess.item);
            pManager.AddNumberParameter("Age", "A", "Age", GH_ParamAccess.item, 0);
            pManager[pManager.ParamCount - 1].Optional = true;
            i = pManager.AddIntegerParameter("Type", "T", "The type", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            mtParam = pManager[i] as Param_Integer;
            foreach (Enum en in Enum.GetValues(typeof(LoadCaseModel.Stage.Operation.TypeCodes)))
                mtParam.AddNamedValue(en.GetDescription(), (int)(LoadCaseModel.Stage.Operation.TypeCodes)en);

            pManager.AddTextParameter("Name", "N", "Name", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddNumberParameter("ScaleFactor", "SF", "Scale factor", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Load Case Stage Op", "LCSO", "The defined load case stage operation", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int operationCode = -1;
            int objectType = -1;
            string objectName = "";
            double age = 0;
            int type = 0;
            string name = "";
            double scaleFactor = 1;

            if (!DA.GetData(0, ref operationCode))
                return;
            if (!DA.GetData(1, ref objectType))
                return;
            if (!DA.GetData(2, ref objectName))
                return;
            DA.GetData(3, ref age);
            DA.GetData(4, ref type);
            DA.GetData(5, ref name);
            DA.GetData(6, ref scaleFactor);

            LoadCaseModel.Stage.Operation operation = new LoadCaseModel.Stage.Operation((LoadCaseModel.Stage.Operation.Codes)operationCode,
                (LoadCaseModel.Stage.Operation.ObjectTypes)objectType, objectName, age, (LoadCaseModel.Stage.Operation.TypeCodes)type, name, scaleFactor);
            DA.SetData(0, new GH_LoadCaseStageOperation(operation));
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("785fff3f-6d81-4e5d-bf4d-cba31acedc58");
    }
}