using BridGHe.Common.Models;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;

namespace BridGHe.Grasshopper.Components.MeCheck2
{
    public class BeamSectionConcreteSlabComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamSectionConcreteSlabComponent class.
        /// </summary>
        public BeamSectionConcreteSlabComponent()
          : base("Concrete Slab Section", "S", "Defines an concrete slab section", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MECHECK2)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Height", "H", "The section height", GH_ParamAccess.item);
            pManager.AddNumberParameter("Width", "W", "The section width", GH_ParamAccess.item);
            pManager.AddNumberParameter("Rebar area", "RA", "The rebar area", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Top concrete cover", "TCC", "The concrete cover from the top of the slab", GH_ParamAccess.item, 50);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Concrete Slab", "S", "The slab section", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double height = 0;
            double width = 0;
            double topArea = 0;
            double topCover = 0;

            if (!DA.GetData(0, ref height))
                return;
            if (!DA.GetData(1, ref width))
                return;
            if (!DA.GetData(2, ref topArea))
                return;
            if (!DA.GetData(3, ref topCover))
                return;

            SectionConcreteSlabModel sectionConcreteSlabModel = new SectionConcreteSlabModel(height, width, topArea, topCover);

            DA.SetData(0, sectionConcreteSlabModel);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("1a539226-24ae-4d3f-8075-4f54cd1b9f68");
    }
}