using System;
using BridGHe.Common.Models;
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
          : base("Beam Geometric Property", "BGP", "Beam geometric properties", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_PROPERTIES)
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

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_BeamProperty gH_BeamProperty = null;

            if (!DA.GetData(0, ref gH_BeamProperty))
                return;

            BeamPropertyModel beamPropertyModel = gH_BeamProperty.Value;

            string name = string.Empty;
            double area = 0;
            double j11 = 0;
            double j22 = 0;
            double jxx = 0;
            double jyy = 0;
            double w11 = 0;
            double w22 = 0;
            double j = 0;
            double xg = 0;
            double yg = 0;
            double angle = 0;
            double rho1 = 0;
            double rho2 = 0;

            //double ShearL1 = 0;
            //double ShearL2 = 0;
            //double ShearA1 = 0;
            //double ShearA2 = 0;

            if (beamPropertyModel.SectionType != SectionModel.SectionTypes.Generic)
            {
                name = beamPropertyModel.Name;
                area = beamPropertyModel.SectionArea;
                xg = beamPropertyModel.Centroid.X;
                yg = beamPropertyModel.Centroid.Y;
                jxx = beamPropertyModel.Ixx;
                jyy = beamPropertyModel.Iyy;
                angle = beamPropertyModel.AngleX1Rad;
                j11 = beamPropertyModel.I11;
                j22 = beamPropertyModel.I22;
                w11 = beamPropertyModel.W11;
                w22 = beamPropertyModel.W22;
                rho1 = beamPropertyModel.Rho11;
                rho2 = beamPropertyModel.Rho22;
                j = beamPropertyModel.J;
                // ShearL1 = beamPropertyModel.ShearL1;
                // ShearL2 = beamPropertyModel.ShearL2;
                // ShearA1 = beamPropertyModel.ShearA1;
                // ShearA2 = beamPropertyModel.ShearA1;
            }

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
            DA.SetData(item++, w11);
            DA.SetData(item++, w22);
            DA.SetData(item++, rho1);
            DA.SetData(item++, rho2);
            DA.SetData(item++, j);
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