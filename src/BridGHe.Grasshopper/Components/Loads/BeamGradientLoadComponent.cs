using System;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class BeamGradientLoadComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamGradientLoadComponent class.
        /// </summary>
        public BeamGradientLoadComponent()
          : base("Beam Gradient Load", "BGL", "Defines the beam gradient load", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam where add the point force load", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            pManager.AddNumberParameter("Axis 1", "A1", "The gradient load on local axis 1", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Axis 2", "A2", "The gradient load on local axis 2", GH_ParamAccess.item, 0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The modified beam", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Beam beam = null;
            GH_LoadCase lc = null;
            double a1 = 0, a2 = 0;
            if (!DA.GetData("Beam", ref beam))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            if (!DA.GetData("Axis 1", ref a1))
                return;
            if (!DA.GetData("Axis 2", ref a2))
                return;

            BeamGradientLoadModel.LoadData load_value = new BeamGradientLoadModel.LoadData
            {
                Axis1 = a1,
                Axis2 = a2
            };

            BeamGradientLoadModel load = new BeamGradientLoadModel(lc.Value, load_value);
            GH_Beam newBeam = new GH_Beam(beam);
            newBeam.Value.Loads.Add(load);
            DA.SetData(0, newBeam);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("461ba509-c137-468a-bb8b-5a5d3666039e");
    }
}