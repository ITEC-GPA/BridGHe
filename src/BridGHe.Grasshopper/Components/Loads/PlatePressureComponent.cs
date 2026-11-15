using System;
using System.Collections.Generic;
using BridGHe.Grasshopper.Components.Parameters;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class PlatePressureComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PlatePressureComponent class.
        /// </summary>
        public PlatePressureComponent()
          : base("Plate Pressure", "PP", "Assign a plate pressure", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The plate where add the pressure", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);

            CoordinateSystemParam cs_param = new CoordinateSystemParam
            {
                Optional = true
            };
            pManager.AddParameter(cs_param, "Coordinate System", "CS", "The coordinate system of the load", GH_ParamAccess.item);

            pManager.AddIntegerParameter("Face", "F", "Face", GH_ParamAccess.item, 0);
            Param_Integer type_param = (Param_Integer)pManager[3];
            type_param.AddNamedValue("Top", 0);
            type_param.AddNamedValue("Bottom", 1);

            pManager.AddGenericParameter("Value", "V", "The value of pressure", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Projected", "P", "Tell if the pressure value is projected", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The modified plate", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Plate plate = null;
            GH_LoadCase lc = null;
            GH_CoordinateSystem cs = null;
            int face = 0;
            if (!DA.GetData("Plate", ref plate))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            bool has_cs = DA.GetData("Coordinate System", ref cs);
            if (!DA.GetData("Face", ref face))
                return;
            object obj = null;
            if (!DA.GetData("Value", ref obj))
                return;
            bool projected = false;
            if (!DA.GetData("Projected", ref projected))
                return;

            Vector3d value = Vector3d.Unset;
            if (obj.GetType() == typeof(GH_Number))
            {
                double val = ((GH_Number)obj).Value;
                value = new Vector3d(0, 0, val);
            }
            else if (obj.GetType() == typeof(GH_String))
            {
                // P2CI (MU-02, ChatGPT task 74): the text read as before (Convert.ToDouble: the current number format; null is 0, an empty string is invalid); a text
                // that is not a number now says so instead of the generic exception of the solution (the same outcome: an error, no output).
                string text = ((GH_String)obj).Value;
                double val;
                try { val = Convert.ToDouble(text); }
                catch (Exception x) when (x is FormatException || x is OverflowException)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Value '" + text + "' is not a valid number in the current number format.");
                    return;
                }
                value = new Vector3d(0, 0, val);
            }
            else if (obj.GetType() == typeof(GH_Vector))
            {
                value = ((GH_Vector)obj).Value;
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Value must be a number, numeric text or a vector. Connect one of these types.");
                return;
            }

            // P2K (P1): the face by the name of the former PlatePressureModel.LoadFace (Top = 0, Bottom = 1; an integer out of range as its text,
            // as the former enum ToString).
            string faceName = Common.Gpc.Builders.GpcBeamBuilders.Code(LoadFaces, face);
            Func<Guid, Common.Gpc.Legacy.LegacyPlateDraft> build = id => Common.Gpc.Builders.GpcPlateBuilders.WithPressure(plate.Gpc, lc.Gpc, cs != null, faceName, value.X, value.Y, value.Z, projected, id);
            // P2K (P1): the GPC path only: the draft built on GPC from the drafts of the plate and the load case record (local without a
            // coordinate system, global on the former face by name with one; whether a system is given is read from the input), identity
            // derived from the input draft.
            GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Plate(this, 0, plate, build, GpcOnlyElements.Lazy(() => lc.Gpc), cs != null, faceName, value.X, value.Y, value.Z, projected));
        }

        private static readonly string[] LoadFaces = { "Top", "Bottom" };

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("8e75d7d6-5661-4311-8919-2d64561bc085");
    }
}