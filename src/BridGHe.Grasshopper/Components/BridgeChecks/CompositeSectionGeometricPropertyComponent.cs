using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class CompositeSectionGeometricPropertyComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamPropertyComponent class.
        /// </summary>
        public CompositeSectionGeometricPropertyComponent()
          : base("Composite Geometric Property", "BGP", "Section geometric properties", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_PROPERTIES)
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Composite Beam Property", "CBP", "Composite Section Property", GH_ParamAccess.item);
            pManager.AddNumberParameter("n", "HC", "Homogenization coefficient", GH_ParamAccess.item, 15);
            pManager.AddBooleanParameter("Consider Concrete", "CC", "If true, consider concrete", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Consider Rebars", "CR", "If true, consider rebars", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Consider Steel Section", "CSS", "If true, consider the steel section", GH_ParamAccess.item, true);
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
            pManager.AddNumberParameter("Jxx", "Jxx", "Second moment of area respect x-x", GH_ParamAccess.item);
            pManager.AddNumberParameter("Jyy", "Jyy", "Second moment of area respect y-y", GH_ParamAccess.item);
            pManager.AddNumberParameter("Angle", "A", "Angle", GH_ParamAccess.item);
            pManager.AddNumberParameter("J₁₁", "J₁₁", "Second moment of area respect 1-1", GH_ParamAccess.item);
            pManager.AddNumberParameter("J₂₂", "J₂₂", "Second moment of area respect 2-2", GH_ParamAccess.item);
            pManager.AddNumberParameter("Sxx", "Sxx", "First moment of area respect x-x", GH_ParamAccess.item);
            pManager.AddNumberParameter("Syy", "Syy", "First moment of area respect y-y", GH_ParamAccess.item);
            pManager.AddNumberParameter("J", "J", "Torsional moment of area", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_CompositeBeamProperty gH_CompositeBeamProperty = null;
            double n = 0;
            bool considerConcrete = false;
            bool considerRebars = false;
            bool considerSteel = false;

            if (!DA.GetData(0, ref gH_CompositeBeamProperty))
                return;
            if (!DA.GetData(1, ref n))
                return;
            DA.GetData(2, ref considerConcrete);
            DA.GetData(3, ref considerRebars);
            DA.GetData(4, ref considerSteel);

            Common.Models.CompositeBeamPropertyModel compositeBeamPropertyModel = gH_CompositeBeamProperty.Value;

            var propOS = compositeBeamPropertyModel.GetOnlySteelMechanicalProperties();
            var propN0 = compositeBeamPropertyModel.GetHomogeneizedMechanicalProperties(n, considerConcrete, considerRebars, considerSteel);

            string name = compositeBeamPropertyModel.Name;
            double area = propN0.areaH;
            double j11 = propN0.J11H;
            double j22 = propN0.J22H;
            double jxx = propN0.JxxH;
            double jyy = propN0.JyyH;
            double j = propOS.Jt;
            double xg = propN0.centroidH.X;
            double yg = propN0.centroidH.Y;
            double angle = propN0.angleX;
            double sx = propN0.SxH;
            double sy = propN0.SyH;

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
            DA.SetData(item++, sx);
            DA.SetData(item++, sy);
            DA.SetData(item++, j);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("2515010a-be42-4530-aa44-5a21b0c0c8dc");
    }
}