using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class BeamSectionHPropertyComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamPropertyComponent class.
        /// </summary>
        public BeamSectionHPropertyComponent()
          : base("Section H Beam Property", "BGP", "Geometric properties of a Section H beam property", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_SECTIONS)
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Section H Beam Property", "BP", "Section Property", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "Section section name", GH_ParamAccess.item);
            pManager.AddNumberParameter("Area", "A", "Section section area", GH_ParamAccess.item);
            pManager.AddNumberParameter("xg", "xg", "X coordinate of centroid", GH_ParamAccess.item);
            pManager.AddNumberParameter("yg", "yg", "Y coordinate of centroid", GH_ParamAccess.item);
            pManager.AddNumberParameter("Jxx", "Jxx", "Second moment of area  respect x-x", GH_ParamAccess.item);
            pManager.AddNumberParameter("Jyy", "Jyy", "Second moment of area  respect y-y", GH_ParamAccess.item);
            pManager.AddNumberParameter("Angle", "A", "Angle", GH_ParamAccess.item);
            pManager.AddNumberParameter("J₁₁", "J₁₁", "Second moment of area respect 1-1", GH_ParamAccess.item);
            pManager.AddNumberParameter("J₂₂", "J₂₂", "Second moment of area respect 2-2", GH_ParamAccess.item);
            pManager.AddNumberParameter("J", "J", "Torsional moment of area", GH_ParamAccess.item);
            pManager.AddNumberParameter("Height", "H", "Total height of the section", GH_ParamAccess.item);
            pManager.AddNumberParameter("Top Flange Width", "TFW", "Top Flange Width", GH_ParamAccess.item);
            pManager.AddNumberParameter("Bottom Flange Width", "BFW", "Bottom Flange Width", GH_ParamAccess.item);
            pManager.AddNumberParameter("Web Thickness", "Tw", "Web Thickness", GH_ParamAccess.item);
            pManager.AddNumberParameter("Top Flange Thickness", "TFT", "Top Flange Thickness", GH_ParamAccess.item);
            pManager.AddNumberParameter("Bottom Flange Thickness", "BFT", "Bottom Flange Thickness", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object obj = null;

            if (!DA.GetData(0, ref obj))
                return;

            // R6.5 P1 lane 2b: a section, a property or a beam gives the values from the recipe of its I section, as the former section value
            // held them (GpcSectionGeometry.Former, the expressions of CalculateSection), and its dimensions. P2K (P1): the only path.
            SolveGpcOnly(DA, obj);
        }

        /// <summary>R6.5 P1 lane 2b: the values of an I section given by its recipe (see <see cref="SolveInstance"/>); the same messages.</summary>
        private void SolveGpcOnly(IGH_DataAccess DA, object obj)
        {
            var recipe = obj switch
            {
                GH_BeamProperty bp => bp.Gpc?.Data?.Section,
                GH_BeamSection bs => bs.Gpc,
                GH_Beam b => b.Gpc?.Property?.Section,
                _ => null,
            };
            if (recipe == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input is not a beam or a beam property or a beam section");
                return;
            }
            if (recipe.Kind != Common.Gpc.BridgeSectionKind.I)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Section is not a I section");
                return;
            }
            var v = GpcOnlyProperties.Datum(this, () => Common.Gpc.GpcSectionGeometry.Former(recipe));
            if (v == null) return;
            Set(DA, v.Name, v.Area, v.XG, v.YG, v.Ixx, v.Iyy, v.AngleX1Rad, v.I11, v.I22, v.J, recipe.D, recipe.B1, recipe.B2, recipe.T3, recipe.T1, recipe.T2);
        }

        private static void Set(IGH_DataAccess DA, string name, double area, double xg, double yg, double jxx, double jyy, double angle, double j11, double j22, double j,
            double h, double tfw, double bfw, double tw, double bft, double tft)
        {
            int item = 0;
            DA.SetData(item++, name);
            DA.SetData(item++, area);
            DA.SetData(item++, xg);
            DA.SetData(item++, yg);
            DA.SetData(item++, jxx);
            DA.SetData(item++, jyy);
            DA.SetData(item++, angle);
            DA.SetData(item++, j11);
            DA.SetData(item++, j22);
            DA.SetData(item++, j);
            DA.SetData(item++, h);
            DA.SetData(item++, tfw);
            DA.SetData(item++, bfw);
            DA.SetData(item++, tw);
            DA.SetData(item++, bft);
            DA.SetData(item++, tft);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("3ee2c935-dd7b-44b8-9bc2-c705ecd78052");
    }
}