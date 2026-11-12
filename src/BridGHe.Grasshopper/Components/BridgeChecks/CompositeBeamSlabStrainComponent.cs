using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    [Obsolete("Use Bridge Shrinkage Action in the phase workflow.")]
    public class CompositeBeamSlabStrainComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CompositeBeamSlabStrainComponent class.
        /// </summary>
        public CompositeBeamSlabStrainComponent()
          : base("Composite Beam Strain", "CBS", "OBSOLETE: assign a strain load directly to a composite beam. Retained for existing definitions; use Bridge Shrinkage Action in the phase workflow.", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
            Message = "Obsolete";
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

            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Obsolete component retained for existing definitions. For the phase workflow use Bridge Shrinkage Action -> Bridge Phase -> Build BridGHe Model.");

            // R6.5 P1 lane 3c: the output by its draft alone (the record of the load and of its case, carried, as the reader reads the FEM load;
            // identity derived from the input draft). A refusal is an error and no output. P2K (P1): the only path, for every input.
            GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Beam(this, 0, beam, id => Common.Gpc.GpcDrafts.Reidentify(Common.Gpc.Builders.GpcDraftTransforms.WithSlabStrain(beam.Gpc, lc.Gpc), id),
                GpcOnlyElements.Lazy(() => lc.Gpc), strain));
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
