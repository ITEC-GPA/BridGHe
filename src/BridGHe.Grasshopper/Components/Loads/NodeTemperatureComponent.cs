using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using BridGHe.Common.Models;
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
            string[] types = Enum.GetNames(typeof(NodeTemperatureLoadModel.LoadType));
            for (int i = 0; i < types.Length; i++)
                param_type.AddNamedValue(types[i], i);
            pManager.AddNumberParameter("Value", "V", "The value of the temperature", GH_ParamAccess.item);
        }

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

            NodeTemperatureLoadModel load = new NodeTemperatureLoadModel(lc.Value, value, (NodeTemperatureLoadModel.LoadType)type);
            string kind = ((NodeTemperatureLoadModel.LoadType)type).ToString();
            Func<Guid, Common.Gpc.Legacy.LegacyNodeDraft> build = id => Common.Gpc.Builders.GpcNodeBuilders.WithTemperature(node.Gpc, lc.Gpc, kind, value, id);
            // R6.5 P1 lane 1a: a node given by its GPC draft alone gives the draft built on GPC (identity derived from the input draft).
            // R6.5 P1 lane 2a: a load case given by its record alone gives the same output, without the FEM value (rule of passo 75).
            if (node.Value == null || lc.Value == null) { GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Node(this, 0, node, build, GpcOnlyElements.Lazy(() => lc.Gpc), kind, value)); return; }
            GH_Node newNode = new GH_Node(node);
            newNode.Value.Loads.Add(load);
            // R6.2 lane A (dual rule until R6.5): the GPC draft built on GPC from the drafts of the inputs (the former type by name), identity of the
            // conversion of the FEM output.
            GpcBuilderAttach.Node(newNode, () => build(Common.Gpc.GpcBeamDrafts.Identity(newNode.Value)));
            DA.SetData("Node", newNode);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("65b74dea-1358-43a8-9fe4-571ab8ac7f11");
    }
}