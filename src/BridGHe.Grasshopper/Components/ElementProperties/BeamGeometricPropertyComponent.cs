using System;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.ElementProperties
{
    public class BeamGeometricPropertyComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamPropertyComponent class.
        /// </summary>
        public BeamGeometricPropertyComponent()
          : base("Beam Geometric Property", "BGP", "Beam geometric properties", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_PROPERTIES)
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam Property", "BP", "Beam Property", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "Beam section name", GH_ParamAccess.item);
            pManager.AddNumberParameter("Area", "A", "Beam section area", GH_ParamAccess.item);
            pManager.AddNumberParameter("xg", "xg", "X coordinate of centroid", GH_ParamAccess.item);
            pManager.AddNumberParameter("yg", "yg", "Y coordinate of centroid", GH_ParamAccess.item);
            pManager.AddNumberParameter("Jxx", "Jxx", "Second moment of area  respect x-x", GH_ParamAccess.item);
            pManager.AddNumberParameter("Jyy", "Jyy", "Second moment of area  respect y-y", GH_ParamAccess.item);
            pManager.AddNumberParameter("Angle", "A", "Angle", GH_ParamAccess.item);
            pManager.AddNumberParameter("J₁₁", "J₁₁", "Second moment of area respect 1-1", GH_ParamAccess.item);
            pManager.AddNumberParameter("J₂₂", "J₂₂", "Second moment of area respect 2-2", GH_ParamAccess.item);
            pManager.AddNumberParameter("W₁₁", "W₁₁", "Elastic section modulus respect 1-1", GH_ParamAccess.item);
            pManager.AddNumberParameter("W₂₂", "W₂₂", "Elastic section modulus respect 2-2", GH_ParamAccess.item);
            pManager.AddNumberParameter("ρ₁₁", "ρ₁₁", "Radius of gyration respect 1-1", GH_ParamAccess.item);
            pManager.AddNumberParameter("ρ₂₂", "ρ₂₂", "Radius of gyration respect 2-2", GH_ParamAccess.item);
            pManager.AddNumberParameter("J", "J", "Torsional moment of area", GH_ParamAccess.item);
        }

        /// <summary>R6.5 P1 lane 2a: the outputs from the values of the former section value (<c>GpcSectionGeometry.Former</c>), in the order of
        /// the outputs.</summary>
        private static void SetFormer(IGH_DataAccess DA, Common.Gpc.GpcSectionGeometry.FormerValues v)
        {
            int item = 0;
            DA.SetData(item++, v.Name); DA.SetData(item++, v.Area); DA.SetData(item++, v.XG); DA.SetData(item++, v.YG); DA.SetData(item++, v.Ixx);
            DA.SetData(item++, v.Iyy); DA.SetData(item++, v.AngleX1Rad); DA.SetData(item++, v.I11); DA.SetData(item++, v.I22); DA.SetData(item++, v.W11);
            DA.SetData(item++, v.W22); DA.SetData(item++, v.Rho11); DA.SetData(item++, v.Rho22); DA.SetData(item++, v.J);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_BeamProperty gH_BeamProperty = null;

            if (!DA.GetData(0, ref gH_BeamProperty))
                return;

            // R6.5 P1 lane 2a: the values the former section value of the property held, from the recipe of its section (rule of passo 75): a
            // generic section gives the empty name and zeros, as the former property; a recipe without the values (composite) or a property
            // without a recipe gives an error and no outputs. P2K (P1): the only path.
            var former = Helpers.GpcOnlyProperties.Datum(this, () =>
            {
                var recipe = gH_BeamProperty.Gpc?.Data?.Section ?? throw new NotSupportedException("GpcPropertyWithoutSectionRecipe: the property has no section recipe.");
                return recipe.Kind == Common.Gpc.BridgeSectionKind.Generic ? new Common.Gpc.GpcSectionGeometry.FormerValues(string.Empty, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
                    : Common.Gpc.GpcSectionGeometry.Former(recipe) with { Name = gH_BeamProperty.Gpc.Property.Name };
            });
            if (former != null) SetFormer(DA, former);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("8969e7d9-57da-478e-818c-949f6e93c883");
    }
}