using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

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

                // R6.5 P1 lane 3c: the merge on the drafts of the inputs, the outputs by their drafts alone, identities by content; a refusal is
                // the error of the component. P2K (P1): the only path, for every input.
                GpcOnlyModels.ParseElements(elements.Where(e => e != null), out List<GH_Node> gpcNodes, out List<GH_Beam> gpcBeams, out List<GH_Plate> gpcPlates);
                var merged = Common.Gpc.Builders.GpcDraftMerge.Merge(gpcNodes.Select(n => n.Gpc).ToArray(), gpcBeams.Select(b => b.Gpc).ToArray(), gpcPlates.Select(p => p.Gpc).ToArray(), tolerance,
                    (output, parameters) => GpcOnlyElements.ContentIdentity(ComponentGuid, output, parameters));
                DA.SetDataList(0, merged.Nodes.Select(d => GH_Node.FromGpc(d)));
                DA.SetDataList(1, merged.Beams.Select(d => GH_Beam.FromGpc(d)));
                DA.SetDataList(2, merged.Plates.Select(d => GH_Plate.FromGpc(d)));
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