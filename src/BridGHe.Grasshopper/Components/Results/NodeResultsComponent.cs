using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Results
{
    public class NodeResultsComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the NodeResultsComponent class.
        /// </summary>
        public NodeResultsComponent()
          : base("Node Results", "NR", "The Node analysis results", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_RESULTS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "The node where to read the results", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Load cases D", "LCD", "The load case name for displacement/rotations", GH_ParamAccess.list);
            pManager.AddNumberParameter("Steps D", "SD", "The step  for displacement/rotations", GH_ParamAccess.list);
            pManager.AddVectorParameter("Displacements", "D", "The node displacement", GH_ParamAccess.list);
            pManager.AddVectorParameter("Rotations", "R", "The node rotation", GH_ParamAccess.list);
            pManager.AddTextParameter("Load cases F", "LCF", "The load case name for forces/moments", GH_ParamAccess.list);
            pManager.AddNumberParameter("Steps F", "SF", "The step for forces/moments", GH_ParamAccess.list);
            pManager.AddVectorParameter("Forces", "F", "The node reaction force", GH_ParamAccess.list);
            pManager.AddVectorParameter("Moments", "M", "The node moment", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_NodeResults node = null;
            if (!DA.GetData(0, ref node))
                return;

            List<string> lcD = new List<string>();
            List<double> stepsD = new List<double>();
            List<Vector3d> displacements = new List<Vector3d>();
            List<Vector3d> rotations = new List<Vector3d>();
            List<string> lcF = new List<string>();
            List<double> stepsF = new List<double>();
            List<Vector3d> forces = new List<Vector3d>();
            List<Vector3d> moments = new List<Vector3d>();
            foreach (ResultModel result in node.Value)
            {
                if (result is NodeDisplResultModel displ)
                {
                    lcD.Add(result.LoadCase.Name);
                    stepsD.Add(result.StepNum);
                    displacements.Add(displ.Displacement);
                    rotations.Add(displ.Rotation);
                }
                else if (result is NodeReactResultModel react)
                {
                    lcF.Add(result.LoadCase.Name);
                    stepsF.Add(result.StepNum);
                    forces.Add(react.Force);
                    moments.Add(react.Moment);
                }
            }

            DA.SetDataList(0, lcD);
            DA.SetDataList(1, stepsD);
            DA.SetDataList(2, displacements);
            DA.SetDataList(3, rotations);
            DA.SetDataList(4, lcF);
            DA.SetDataList(5, stepsF);
            DA.SetDataList(6, forces);
            DA.SetDataList(7, moments);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("b34af03b-9b25-4e88-8e5c-a90e8a52f830");
    }
}