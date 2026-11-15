using System;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class NodeLoadComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the NodeLoadComponent class.
        /// </summary>
        public NodeLoadComponent()
          : base("Node Load", "NL", "Defines a node load", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "The node where define the restraits", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            pManager.AddVectorParameter("Force", "F", "The vector force load in the global XYZ direction", GH_ParamAccess.item);
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
            Vector3d force = Vector3d.Unset;
            if (!DA.GetData("Node", ref node))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            if (!DA.GetData("Force", ref force))
                return;

            Func<Guid, Common.Gpc.Legacy.LegacyNodeDraft> build = id => Common.Gpc.Builders.GpcNodeBuilders.WithForce(node.Gpc, lc.Gpc, force.X, force.Y, force.Z, id);
            // P2K (P1): the GPC path only: the draft built on GPC from the drafts of the node and the load case record (identity derived from
            // the input draft).
            GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Node(this, 0, node, build, GpcOnlyElements.Lazy(() => lc.Gpc), force.X, force.Y, force.Z));
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("d30a7ae4-15af-49e2-a12c-a9efebc96d03");
    }
}