using System;
using System.Collections.Generic;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Models
{
    public class NodeComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the NodeComponent class.
        /// </summary>
        public NodeComponent()
          : base("Node", "N", "Define a Node", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_MODEL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Point", "P", "The node point coordinate", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "The generated node", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Point3d point = Point3d.Unset;
            if (!DA.GetData("Point", ref point))
                return;

            // R6.5 P1 lane 3c: with the switch of the generators (the path of P2) the node by its draft alone, identity by content (decision 1 of passo 73).
            if (GpcOnlyElements.GeneratorsWithoutFem)
            {
                var alone = GpcOnlyElements.Generated(this, () => Common.Gpc.Builders.GpcNodeBuilders.Create(point.X, point.Y, point.Z, "", null, GpcOnlyElements.ContentIdentity(ComponentGuid, 0, point)));
                if (alone != null) DA.SetData("Node", GH_Node.FromGpc(alone));
                return;
            }

            var node = new GH_Node(point);
            // R6.2 lane A (dual rule until R6.5): the GPC draft of a new node built on GPC (position, no ID, no groups), identity of the
            // conversion of the FEM output.
            GpcBuilderAttach.Node(node, () => Common.Gpc.Builders.GpcNodeBuilders.Create(point.X, point.Y, point.Z, "", null, Common.Gpc.GpcBeamDrafts.Identity(node.Value)));
            DA.SetData("Node", node);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("9abf8116-70e5-4061-b0f6-b1934a306f07");
    }
}