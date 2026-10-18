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
    public class MergeModelComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BuildModelComponent class.
        /// </summary>
        public MergeModelComponent()
          : base("Merge Model", "BM", "Build the FEM model", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_MODEL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Elements", "E", "The model elements", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tolerance", "T", "The geometric matching tolerance", GH_ParamAccess.item, 0.1);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("BridGHe Nodes", "FN", "", GH_ParamAccess.list);
            pManager.AddGenericParameter("BridGHe Beams", "FB", "", GH_ParamAccess.list);
            pManager.AddGenericParameter("BridGHe Plates", "FP", "", GH_ParamAccess.list);
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

                List<NodeModel> modelnodes = new List<NodeModel>();
                List<BeamModel> modelbeams = new List<BeamModel>();
                List<PlateModel> modelplates = new List<PlateModel>();

                for (int i = 0; i < elements.Count; i++)
                {
                    if (elements[i] != null)
                    {
                        switch (elements[i])
                        {
                            case GH_Node fnode:
                                modelnodes.Add(new NodeModel(fnode.Value));
                                break;
                            case GH_Beam fbeam:
                                modelbeams.Add(new BeamModel(fbeam.Value));
                                break;
                            case GH_Plate fplate:
                                modelplates.Add(new PlateModel(fplate.Value));
                                break;
                        }
                    }
                }

                Common.Helpers.ModelHelper.MergeModel(ref modelnodes, ref modelbeams, ref modelplates, tolerance);

                List<GH_Node> gH_Nodes = new List<GH_Node>();
                List<GH_Beam> gH_Beams = new List<GH_Beam>();
                List<GH_Plate> gH_Plates = new List<GH_Plate>();

                for (int i = 0; i < modelnodes.Count; i++)
                    gH_Nodes.Add(new GH_Node(modelnodes[i]));
                for (int i = 0; i < modelbeams.Count; i++)
                    gH_Beams.Add(new GH_Beam(modelbeams[i]));
                for (int i = 0; i < modelplates.Count; i++)
                    gH_Plates.Add(new GH_Plate(modelplates[i]));

                DA.SetDataList(0, gH_Nodes);
                DA.SetDataList(1, gH_Beams);
                DA.SetDataList(2, gH_Plates);
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
        public override Guid ComponentGuid => new Guid("e6896f43-2dff-40a5-b679-9ac79a30cbe6");
    }
}