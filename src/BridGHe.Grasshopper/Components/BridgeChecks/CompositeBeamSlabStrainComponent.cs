using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class CompositeBeamSlabStrainComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CompositeBeamSlabStrainComponent class.
        /// </summary>
        public CompositeBeamSlabStrainComponent()
          : base("Composite Beam Strain", "CBS", "Assign a strain load to a composite beam", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam where add the strain load", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            pManager.AddNumberParameter("Strain", "S", "The strain value", GH_ParamAccess.item);
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
            double strain = 0;

            if (!DA.GetData(0, ref beam))
                return;
            if (!DA.GetData(1, ref lc))
                return;
            if (!DA.GetData(2, ref strain))
                return;

            CompositeBeamSlabStrainModel.LoadData load_value = new CompositeBeamSlabStrainModel.LoadData()
            {
                Strain = strain,
            };
            CompositeBeamSlabStrainModel load = new CompositeBeamSlabStrainModel(lc.Value, load_value);
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
        public override Guid ComponentGuid => new Guid("adf0a668-9fee-4d5d-8665-57aa769319b0");
    }
}