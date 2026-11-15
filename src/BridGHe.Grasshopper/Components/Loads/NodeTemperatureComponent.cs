using BridGHe.Common.Helpers;
using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using BridGHe.Grasshopper.Helpers;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class NodeTemperatureComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the NodeTemperatureComponent class.
        /// </summary>
        public NodeTemperatureComponent()
          : base("Node Temperature", "NT", "Defines a node temperature load", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "The node where define the restraits", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Type", "T", "The type of temperature", GH_ParamAccess.item);
            Param_Integer param_type = (Param_Integer)pManager[2];
            string[] types = Enum.GetNames(typeof(TemperatureChoices.LoadType));
            for (int i = 0; i < types.Length; i++)
                param_type.AddNamedValue(types[i], i);
            pManager.AddNumberParameter("Value", "V", "The value of the temperature", GH_ParamAccess.item);
        }

        // P2K (P1): the names of the former NodeTemperatureLoadModel.LoadType (Reference = 0, Fixed = 1, Initial = 2): the Type choices and the
        // kind the GPC builder reads.
        private static readonly string[] TemperatureTypes = { "Reference", "Fixed", "Initial" };

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "The modified node", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Node node = null;
            GH_LoadCase lc = null;
            int type = 0;
            double value = 0;
            if (!DA.GetData("Node", ref node))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            if (!DA.GetData("Type", ref type))
                return;
            if (!DA.GetData("Value", ref value))
                return;

            // P2K (P1): the former type by name (an integer out of range as its text, as the former enum ToString).
            string kind = Common.Gpc.Builders.GpcBeamBuilders.Code(TemperatureTypes, type);
            Func<Guid, Common.Gpc.Legacy.LegacyNodeDraft> build = id => Common.Gpc.Builders.GpcNodeBuilders.WithTemperature(node.Gpc, lc.Gpc, kind, value, id);
            // P2K (P1): the GPC path only: the draft built on GPC from the drafts of the node and the load case record (identity derived from
            // the input draft).
            GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Node(this, 0, node, build, GpcOnlyElements.Lazy(() => lc.Gpc), kind, value));
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("65b74dea-1358-43a8-9fe4-571ab8ac7f11");

        // Private menu choices preserve the published values, order and descriptions.
        private static class TemperatureChoices
        {
            internal enum LoadType { Reference, Fixed, Initial }
        }
    }
}