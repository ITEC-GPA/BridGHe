using System;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
namespace BridGHe.Grasshopper.Components.Sections
{
    public class BeamSectionHollowCircleComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamSectionHollowCircleComponent class.
        /// </summary>
        public BeamSectionHollowCircleComponent()
          : base("Hollow Circle", "HC", "Hollow circle beam section", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_SECTIONS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Diameter", "D", "The section diameter", GH_ParamAccess.item);
            pManager.AddNumberParameter("Thickness", "T", "The section thickness", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Section", "S", "The hollow circular section", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double d = 0, t = 0;
            if (!DA.GetData(0, ref d))
                return;
            if (!DA.GetData(1, ref t))
                return;

            // P2K (P1): the section by its recipe alone (a refused recipe gives an error and no section).
            Helpers.GpcOnlyElements.Set(DA, 0, Helpers.GpcOnlyProperties.Section(this, () => Common.Gpc.Builders.GpcSectionBuilders.Standard(Common.Gpc.BridgeSectionKind.HollowCircle, d, t: t)));
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("cd443ccf-2cff-4255-9787-54a0693f0805");
    }
}