using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace BridGHe.Grasshopper.Components.Models
{
    public class MergeGeometryComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BuildModelComponent class.
        /// </summary>
        public MergeGeometryComponent()
          : base("Merge Geometry", "MG", "Merge the input geometries", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_TOOLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Elements", "E", "The model elements", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tolerance", "T", "The geometric matching tolerance", GH_ParamAccess.item, 0.1);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "", GH_ParamAccess.list);
            pManager.AddCurveParameter("Curves", "C", "", GH_ParamAccess.list);
            pManager.AddMeshParameter("Meshes", "M", "", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            try
            {
                List<IGH_GeometricGoo> elements = new List<IGH_GeometricGoo>();
                double tolerance = 0.1;

                if (!DA.GetDataList(0, elements))
                    return;
                DA.GetData(1, ref tolerance);

                List<Point3d> nodes = new List<Point3d>();
                List<Curve> beams = new List<Curve>();
                List<Mesh> plates = new List<Mesh>();

                for (int i = 0; i < elements.Count; i++)
                {
                    if (elements[i] != null)
                    {
                        switch (elements[i])
                        {
                            case GH_Point node:
                                nodes.Add(new Point3d(node.Value));
                                break;
                            case GH_Curve curve:
                                beams.Add(curve.Value.DuplicateCurve());
                                break;
                            case GH_Mesh mesh:
                                plates.Add(mesh.Value.DuplicateMesh());
                                break;
                        }
                    }
                }

                Common.Helpers.ModelHelper.MergeGeometry(ref nodes, ref beams, ref plates, tolerance);

                DA.SetDataList(0, nodes);
                DA.SetDataList(1, beams);
                DA.SetDataList(2, plates);
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("8e321fc4-9fa5-4583-b80c-7bbcf9b3ad3c");
    }
}