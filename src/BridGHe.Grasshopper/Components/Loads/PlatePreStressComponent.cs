using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class PlatePreStressComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PlatePreStressComponent class.
        /// </summary>
        public PlatePreStressComponent()
          : base("Plate PreStress", "PPre", "Assign prestress in plate using its local coordinate system", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The plate where add the pressure", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            pManager.AddNumberParameter("Px", "px", "Value of prestress in x local direction", GH_ParamAccess.item);
            pManager.AddNumberParameter("Py", "py", "Value of prestress in y local direction", GH_ParamAccess.item);
            pManager.AddNumberParameter("Pz", "pz", "Value of prestress in z local direction", GH_ParamAccess.item);
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

            if (!DA.GetData("Plate", ref plate))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            double px = 0;
            if (!DA.GetData("Px", ref px))
                return;
            double py = 0;
            if (!DA.GetData("Py", ref py))
                return;
            double pz = 0;
            if (!DA.GetData("Pz", ref pz))
                return;

            Vector3d value = new Vector3d(px, py, pz);

            PlatePreStressModel.LoadData load_value = new PlatePreStressModel.LoadData()
            {
                LocalPressure = value
            };
            PlatePreStressModel load = new PlatePreStressModel(lc.Value, load_value);
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
        public override Guid ComponentGuid => new Guid("9d0160c6-e491-4a12-a289-a4b3c4cc6811");
    }
}