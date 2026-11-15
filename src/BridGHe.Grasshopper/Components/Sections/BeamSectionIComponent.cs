using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;

namespace BridGHe.Grasshopper.Components.Sections
{
    public class BeamSectionIComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamSectionIComponent class.
        /// </summary>
        public BeamSectionIComponent()
          : base("I Section", "I", "Defines an I section", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_SECTIONS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Bottom flange width", "BFW", "The bottom flange width", GH_ParamAccess.item);
            pManager.AddNumberParameter("Top flange width", "TFW", "The top flange width", GH_ParamAccess.item);
            pManager.AddNumberParameter("Height", "H", "The total section height", GH_ParamAccess.item);
            pManager.AddNumberParameter("Bottom flange thickness", "BFT", "The bottom flange thickness", GH_ParamAccess.item);
            pManager.AddNumberParameter("Top flange thickness", "TFT", "The top flange thickness", GH_ParamAccess.item);
            pManager.AddNumberParameter("Web thickness", "WT", "The web thickness", GH_ParamAccess.item);
            pManager.AddNumberParameter("Radius", "R", "The radius", GH_ParamAccess.item, 0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Section", "S", "The I section", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double b1 = 0, b2 = 0, d = 0, t1 = 0, t2 = 0, t3 = 0, r = 0;
            if (!DA.GetData(0, ref b1))
                return;
            if (!DA.GetData(1, ref b2))
                return;
            if (!DA.GetData(2, ref d))
                return;
            if (!DA.GetData(3, ref t1))
                return;
            if (!DA.GetData(4, ref t2))
                return;
            if (!DA.GetData(5, ref t3))
                return;
            if (!DA.GetData(6, ref r))
                return;

            // L2C (limit (a) of passo 77): the recipe with the fillet radius R, as the former section held it.
            // P2K (P1): the section by its recipe alone (a refused recipe gives an error and no section).
            Helpers.GpcOnlyElements.Set(DA, 0, Helpers.GpcOnlyProperties.Section(this, () => Common.Gpc.Builders.GpcSectionBuilders.Standard(Common.Gpc.BridgeSectionKind.I, d, b1: b1, b2: b2, t1: t1, t2: t2, t3: t3, r: r)));
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7a5fa90e-77d2-4eb7-904e-320f03fd790c");
    }
}