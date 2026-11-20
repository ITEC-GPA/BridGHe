using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    /// <summary>
    /// 0.5.0 (Brecciarola study item 20, 20/11/2026): the concrete slab of a composite section given by its bars: width, height, one pitch
    /// common to the top and bottom rows and a diameter per row, the distances of the bar axes from the faces. The row areas are the areas
    /// over the slab width, (W/P)·π·d²/4, and the section of the checks has W/P bars per row of about the real diameter. It replaces Concrete
    /// Slab Section with row areas (<see cref="BeamSectionConcreteSlabComponent"/>, kept hidden for the files that use it).
    /// </summary>
    public class BeamSectionConcreteSlabBarsComponent : GH_Component
    {
        public BeamSectionConcreteSlabBarsComponent()
          : base("Concrete Slab Section", "Slab",
              "Concrete slab with top and bottom bars at one common pitch. Dimensions mm; bar diameters per row (0 disables the row); distances measured to the bar axes. Row area = W/P·π·d²/4.",
              CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_SECTIONS)
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Height", "H", "The slab height [mm]", GH_ParamAccess.item);
            pManager.AddNumberParameter("Width", "W", "The slab width [mm] (effective width of the girder)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Pitch", "P", "Pitch of the bars, the same for the top and the bottom row [mm]", GH_ParamAccess.item, 200);
            pManager.AddNumberParameter("Top diameter", "TD", "Diameter of the TOP bars [mm]. Zero disables the top row.", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Top axis distance", "TA", "Distance from the top face to the axes of the TOP bars [mm], not clear cover", GH_ParamAccess.item, 50);
            pManager.AddNumberParameter("Bottom diameter", "BD", "Diameter of the BOTTOM bars [mm]. Zero disables the bottom row.", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Bottom axis distance", "BA", "Distance from the bottom face to the axes of the BOTTOM bars [mm], not clear cover", GH_ParamAccess.item, 50);
            for (int i = 2; i < pManager.ParamCount; i++) pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Concrete Slab", "S", "The slab section", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double height = 0, width = 0, pitch = 200, topDiameter = 0, topAxis = 50, bottomDiameter = 0, bottomAxis = 50;
            if (!DA.GetData(0, ref height)) return;
            if (!DA.GetData(1, ref width)) return;
            DA.GetData(2, ref pitch);
            DA.GetData(3, ref topDiameter);
            DA.GetData(4, ref topAxis);
            DA.GetData(5, ref bottomDiameter);
            DA.GetData(6, ref bottomAxis);

            var slab = Common.Checks.ConcreteSlabSection.FromBars(height, width, pitch, topDiameter, topAxis, bottomDiameter, bottomAxis);
            if (!slab.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Use finite positive dimensions and pitch, nonnegative diameters not larger than the pitch, at most "
                    + Common.Checks.ConcreteSlabSection.MaximumBarsPerRow + " bars per row (W/P). Each bar must lie inside the slab and the top row above the bottom row, the two rows apart.");
                return;
            }
            Message = slab.TopDiameter == 0 && slab.BottomDiameter == 0 ? "no bars"
                : string.Format(System.Globalization.CultureInfo.InvariantCulture, "P {0:0.#} · {1} bars/row", slab.Pitch, slab.BarsPerRow);
            DA.SetData(0, slab);
        }

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>Do not change this ID after release.</summary>
        public override Guid ComponentGuid => new Guid("fa9989a5-f4d0-4041-a51d-f6c6fe5892f3");
    }
}
