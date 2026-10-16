using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Results
{
    public class PlateResultsComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PlateResultsComponent class.
        /// </summary>
        public PlateResultsComponent()
          : base("Plate Results", "PR", "The Plate analysis results", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_RESULTS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The plate with results", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Load cases", "LC", "The load case name", GH_ParamAccess.list);
            pManager.AddTextParameter("Nodes", "N", "Node id of the results", GH_ParamAccess.list);
            pManager.AddNumberParameter("Steps", "S", "The steps", GH_ParamAccess.list);
            pManager.AddVectorParameter("Force", "F", "Internal direct force per length (X = F11 direct force, Y = F22 direct force, Z = F12 shear force) (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Force Max", "FMAX", "The maximum principal force per length (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Force Min", "FMIN", "The minimum principal force per length (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Force Angle", "FANG", "The angle from the area local 1 axis to the direction of the maximum principal force (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Force Von Mises", "FVM", "The internal Von Mises force per length (>0 st7)", GH_ParamAccess.list);
            pManager.AddVectorParameter("Moment", "M", "The internal moment per length (X = M11 bending moment, Y = M22 bending moment, Z = M12 twisting moment) (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Moment Max", "MMAX", "The maximum principal moment per length (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Moment Min", "MMIN", "The minimum principal moment per length (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Moment Angle", "MANG", "The angle from the area local 1 axis to the direction of the maximum principal moment (>0 st7)", GH_ParamAccess.list);
            pManager.AddVectorParameter("Shear Force", "SF", "The transverse shear force per length (X = V13, Y = V23) (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Shear Force Max", "VMAX", "The maximum transverse shear force (only sap2000)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Shear Force Angle", "VANG", "The angle from the area local 1 axis to the direction of Vmax (only sap2000)", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_PlateResults plate = null;
            if (!DA.GetData(0, ref plate))
                return;

            List<string> lc = new List<string>();
            List<string> nodes = new List<string>();
            List<double> steps = new List<double>();
            List<Vector3d> forces = new List<Vector3d>();
            List<double> fmax = new List<double>();
            List<double> fmin = new List<double>();
            List<double> fang = new List<double>();
            List<double> fvm = new List<double>();
            List<Vector3d> moments = new List<Vector3d>();
            List<double> mmax = new List<double>();
            List<double> mmin = new List<double>();
            List<double> mang = new List<double>();
            List<Vector2d> shearForces = new List<Vector2d>();
            List<double> sf_max = new List<double>();
            List<double> sf_ang = new List<double>();
            foreach (ResultModel result in plate.Value)
            {
                lc.Add(result.LoadCase.Name);
                steps.Add(result.StepNum);
                if (result is PlateForceResultModel force)
                {
                    nodes.Add(force.NodeId);
                    forces.Add(force.Force);
                    fmax.Add(force.ForceMax);
                    fmin.Add(force.ForceMin);
                    fang.Add(force.ForceAngle);
                    fvm.Add(force.ForceVonMises);
                    moments.Add(force.Moment);
                    mmax.Add(force.MomentMax);
                    mmin.Add(force.MomentMin);
                    mang.Add(force.MomentAngle);
                    shearForces.Add(force.TransverseShearForce);
                    sf_max.Add(force.TransverseShearForceMax);
                    sf_ang.Add(force.TransverseShearForceAng);
                }
            }

            DA.SetDataList(0, lc);
            DA.SetDataList(1, nodes);
            DA.SetDataList(2, steps);
            DA.SetDataList(3, forces);
            DA.SetDataList(4, fmax);
            DA.SetDataList(5, fmin);
            DA.SetDataList(6, fang);
            DA.SetDataList(7, fvm);
            DA.SetDataList(8, moments);
            DA.SetDataList(9, mmax);
            DA.SetDataList(10, mmin);
            DA.SetDataList(11, mang);
            DA.SetDataList(12, shearForces);
            DA.SetDataList(13, sf_max);
            DA.SetDataList(14, sf_ang);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("12f10928-8575-4e6e-a16a-e299fd7f5b39");
    }
}