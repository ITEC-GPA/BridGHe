using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
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
                double val = Convert.ToDouble(((GH_String)obj).Value);
                value = new Vector3d(0, 0, val);
            }
            else if (obj.GetType() == typeof(GH_Vector))
            {
                value = ((GH_Vector)obj).Value;
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid Value");
                return;
            }

            PlatePressureModel.LoadData load_value = new PlatePressureModel.LoadData()
            {
                Face = (PlatePressureModel.LoadFace)face,
                CoordinateSystem = cs?.Value,
                P = value,
                Projected = projected
            };
            PlatePressureModel load = new PlatePressureModel(lc.Value, load_value);
            GH_Plate newPlate = new GH_Plate(plate);
            newPlate.Value.Loads.Add(load);
            DA.SetData(0, newPlate);
        }

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