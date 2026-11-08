using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.Helpers;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class NodeMomentComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the NodeMomentComponent class.
        /// </summary>
        public NodeMomentComponent()
          : base("Node Moment", "NM", "Defines a node moment", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "The node where define the restraits", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            pManager.AddVectorParameter("Moment", "M", "The vector moment in the global XYZ direction", GH_ParamAccess.item);
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
            Vector3d moment = Vector3d.Unset;
            if (!DA.GetData("Node", ref node))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            if (!DA.GetData("Moment", ref moment))
                return;

            NodeMomentLoadModel load = new NodeMomentLoadModel(lc.Value, new Vector3d(moment.X, moment.Y, moment.Z));
            Func<Guid, Common.Gpc.Legacy.LegacyNodeDraft> build = id => Common.Gpc.Builders.GpcNodeBuilders.WithMoment(node.Gpc, lc.Gpc, moment.X, moment.Y, moment.Z, id);
            // R6.5 P1 lane 1a: a node given by its GPC draft alone gives the draft built on GPC (identity derived from the input draft).
            // R6.5 P1 lane 2a: a load case given by its record alone gives the same output, without the FEM value (rule of passo 75).
            if (node.Value == null || lc.Value == null) { GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Node(this, 0, node, build, GpcOnlyElements.Lazy(() => lc.Gpc), moment.X, moment.Y, moment.Z)); return; }
            GH_Node newNode = new GH_Node(node);
            newNode.Value.Loads.Add(load);
            // R6.2 lane A (dual rule until R6.5): the GPC draft built on GPC from the drafts of the inputs, identity of the conversion of the FEM output.
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
        public override Guid ComponentGuid => new Guid("fcbadad8-e4a9-447a-bc4c-c4cd6fce479a");
    }
}