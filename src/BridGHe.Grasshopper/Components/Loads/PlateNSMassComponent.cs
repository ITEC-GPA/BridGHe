using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class PlateNSMassComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PlatePressureComponent class.
        /// </summary>
        public PlateNSMassComponent()
          : base("Plate non structural Mass", "PNSM", "Assign a plate non structural mass", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_LOADS)
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The plate where add the pressure", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mass", "M", "The value of the surface mass [Mass/(Length*Length)]", GH_ParamAccess.item);
            pManager.AddNumberParameter("Dyn factor", "DF", "The dynamic factor of the structural mass", GH_ParamAccess.item);
            pManager.AddVectorParameter("Offset", "O", "Offset of the mass", GH_ParamAccess.item, Vector3d.Zero);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The plate with the NS mass applied", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Plate plate = null;
            GH_LoadCase lc = null;
            double mass = 0;
            double dynFactor = 0;
            Vector3d offset = Vector3d.Unset;

            if (!DA.GetData("Plate", ref plate))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            if (!DA.GetData("Mass", ref mass))
                return;
            if (!DA.GetData("Dyn factor", ref dynFactor))
                return;
            if (!DA.GetData("Offset", ref offset))
                return;

            PlateNSMassModel.LoadData load_value = new PlateNSMassModel.LoadData()
            {
                Mass = mass,
                DynFactor = dynFactor,
                Offset = offset
            };
            PlateNSMassModel load = new PlateNSMassModel(lc.Value, load_value);
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
        public override Guid ComponentGuid => new Guid("ca774fef-a0a3-4df4-97be-51ba543c6d05");
    }
}