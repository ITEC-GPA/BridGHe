using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Attributes
{
    public class NodeStiffnessComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the NodeRestraintsComponent class.
        /// </summary>
        public NodeStiffnessComponent()
          : base("Stiffness", "NS", "Define a node stiffness", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "The node where define the restraits", GH_ParamAccess.item);
            int i = 0;
            i = pManager.AddVectorParameter("Transational", "T", "The transational stiffness", GH_ParamAccess.item);
            pManager[i].Optional = true;
            i = pManager.AddVectorParameter("Rotational", "R", "The rotational stiffness", GH_ParamAccess.item);
            pManager[i].Optional = true;
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
            Vector3d trans = Vector3d.Unset;
            Vector3d rot = Vector3d.Unset;
            if (!DA.GetData("Node", ref node))
                return;
            DA.GetData("Transational", ref trans);
            DA.GetData("Rotational", ref rot);

            if (trans == Vector3d.Unset && rot == Vector3d.Unset)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Insert a value of stiffness");
                DA.AbortComponentSolution();
                return;
            }

            GH_Node newNode = new GH_Node(node);

            NodeStiffnessAttributeModel.AttributeData attr = new NodeStiffnessAttributeModel.AttributeData();
            if (trans != Vector3d.Unset)
            {
                attr.Tx = trans.X;
                attr.Ty = trans.Y;
                attr.Tz = trans.Z;
            }
            if (rot != Vector3d.Unset)
            {
                attr.Rx = rot.X;
                attr.Ry = rot.Y;
                attr.Rz = rot.Z;
            }

            newNode.Value.Attributes.Add(new NodeStiffnessAttributeModel(attr));

            DA.SetData("Node", newNode);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("90a26129-a25e-4396-b677-b661d86246c2");
    }
}