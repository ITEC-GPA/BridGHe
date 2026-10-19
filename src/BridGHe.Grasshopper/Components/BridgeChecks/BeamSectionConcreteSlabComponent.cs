using BridGHe.Common.Models;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class BeamSectionConcreteSlabComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamSectionConcreteSlabComponent class.
        /// </summary>
        public BeamSectionConcreteSlabComponent()
          : base("Concrete Slab Section", "Slab", "Concrete slab with independent top and bottom reinforcement rows. Dimensions mm, total row areas mm²; covers are measured to bar axes.", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_SECTIONS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Height", "H", "The section height", GH_ParamAccess.item);
            pManager.AddNumberParameter("Width", "W", "The section width", GH_ParamAccess.item);
            pManager.AddNumberParameter("Rebar area", "RA", "Total TOP row area over the slab width [mm²]. Zero disables this row.", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Top concrete cover", "TCC", "Distance from the top face to the TOP bar axes [mm], not clear cover", GH_ParamAccess.item, 50);
            pManager.AddNumberParameter("Bottom rebar area", "BRA", "Total BOTTOM row area over the slab width [mm²]. Zero disables this row.", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Bottom concrete cover", "BCC", "Distance from the bottom face to the BOTTOM bar axes [mm], not clear cover", GH_ParamAccess.item, 50);
            for (int i = 2; i < pManager.ParamCount; i++) pManager[i].Optional = true;
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
            double topCover = 50;
            double bottomArea = 0;
            double bottomCover = 50;

            if (!DA.GetData(0, ref height))
                return;
            if (!DA.GetData(1, ref width))
                return;
            DA.GetData(2, ref topArea);
            DA.GetData(3, ref topCover);
            DA.GetData(4, ref bottomArea);
            DA.GetData(5, ref bottomCover);

            var sectionConcreteSlabModel = new SectionConcreteSlabModel(height, width, topArea, topCover, bottomArea, bottomCover);
            if (!sectionConcreteSlabModel.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Use finite positive dimensions and nonnegative areas. Active row axes must lie inside the slab; the top row must be above the bottom row.");
                return;
            }

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
