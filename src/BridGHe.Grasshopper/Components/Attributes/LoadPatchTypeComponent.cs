using BridGHe.Common.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Grasshopper.ComponentAttributes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace BridGHe.Grasshopper.Components.Attributes
{
    public class LoadPatchTypeComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the LoadPatchTypeComponent class.
        /// </summary>
        public LoadPatchTypeComponent()
          : base("Load Patch Type", "LPT", "The Plate Load Path Type", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        public override void CreateAttributes()
        {
            ComponentWithImageAttributes attr = new ComponentWithImageAttributes(this, Properties.Resources.LoadPatchTypeAuto1);
            m_attributes = attr;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The load patch plate where to define the type", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Type", "T", "Load Type", GH_ParamAccess.item);
            Param_Integer type_param = (Param_Integer)pManager[1];
            Array values = Enum.GetValues(typeof(LoadPatchTypeAttributeModel.PatchType));
            foreach (LoadPatchTypeAttributeModel.PatchType val in values)
                type_param.AddNamedValue(val.GetDescription(), (int)val);
            pManager.AddIntegerParameter("Edges", "E", "The edges index to select", GH_ParamAccess.list);
            pManager[2].Optional = true;
            pManager.AddNumberParameter("Weights", "W", "The edge weights", GH_ParamAccess.list);
            pManager[3].Optional = true;
        }

        private void SetTypeImage(int type)
        {
            switch (type)
            {
                case 0:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.LoadPatchTypeAuto1;
                    break;
                case 1:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.LoadPatchTypeAuto2;
                    break;
                case 2:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.LoadPatchTypeAuto3;
                    break;
                case 3:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.LoadPatchTypeAuto4;
                    break;
                case 4:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.LoadPatchTypeAngleSplit;
                    break;
                case 5:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.LoadPatchTypeManual;
                    break;
            }
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The modified plate", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Plate plate = null;
            int type = 0;
            List<int> edges = new List<int>();
            List<double> weights = new List<double>();
            if (!DA.GetData("Plate", ref plate))
                return;
            if (!DA.GetData("Type", ref type))
                return;
            DA.GetDataList("Edges", edges);
            DA.GetDataList("Weights", weights);

            SetTypeImage(type);

            // P2K (P1): the corners of the plate from its GPC draft (the points of the former FEM value).
            int corners = plate.Gpc?.Corners.Count ?? 0;
            if (edges.Count > corners)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The number of the Edges can't be greater of the plate edges number");
                return;
            }
            if (weights.Count > corners)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The number of the Weights can't be greater of the plate edges number");
                return;
            }

            if ((type == Auto1 || type == Auto2 || type == Auto3) && edges.Count != 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The selected load patch type requires one edge");
                return;
            }
            if ((type == Auto3 || type == AngleSplit) && corners == 3) // Tri3
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "This type of load patch is not applicable to Tri3 elements");
                return;
            }
            if (type == AngleSplit && (edges.Count != 2 ||
                Math.Abs(edges[0] - edges[1]) != 1 && Math.Abs(edges[0] - edges[1]) != 3))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The Angle Split type requires two adjacent edge set");
                return;
            }
            if (type == Manual && edges.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The Manual type requires to specify edge(s) and weight(s)");
                return;
            }
            if (type == Manual && edges.Count != weights.Count(w => w > 0))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The number of edges must be equals to the number of weights");
                return;
            }

            // P2K (P1): the four edges and weights of the former attribute data (same arrays, same identity parameters).
            int[] selectedEdges = new int[4];
            double[] edgeWeights = new double[4];
            for (int i = 0; i < edges.Count; i++)
                selectedEdges[i] = edges[i];
            for (int i = 0; i < weights.Count; i++)
                edgeWeights[i] = weights[i];
            // P2K (P1): the type by its former enum name (an integer out of range as its text, as the former ToString).
            string patchType = Common.Gpc.Builders.GpcBeamBuilders.Code(PatchTypeNames, type);

            Func<Guid, Common.Gpc.Legacy.LegacyPlateDraft> build = id => Common.Gpc.Builders.GpcPlateBuilders.WithLoadPatch(plate.Gpc, patchType, selectedEdges, edgeWeights, id);
            // P2K (P1): the GPC path only: the draft built on GPC (identity derived from the input draft).
            GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Plate(this, 0, plate, build, patchType, selectedEdges, edgeWeights));
        }

        // P2K (P1): the former LoadPatchTypeAttributeModel.PatchType: names (the GPC builder reads them) and integers.
        private static readonly string[] PatchTypeNames = { "Auto1", "Auto2", "Auto3", "Auto4", "AngleSplit", "Manual" };
        private const int Auto1 = 0, Auto2 = 1, Auto3 = 2, AngleSplit = 4, Manual = 5;

        public override bool Read(GH_IReader reader)
        {
            bool success = base.Read(reader);
            // A wired type has no internal value while sources are being restored.
            SetTypeImage(((Param_Integer)Params.Input[1]).PersistentData.Branches.SelectMany(b => b).FirstOrDefault()?.Value ?? 0);
            return success;
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("1dcb6c8b-8bd7-4e5b-a6a1-76c68a8f729b");

        // Private menu choices preserve the published values, order and descriptions.
        private static class LoadPatchTypeAttributeModel
        {
            internal enum PatchType { Auto1, Auto2, Auto3, Auto4, [System.ComponentModel.Description("Angle Split")] AngleSplit, Manual }
        }
    }
}
