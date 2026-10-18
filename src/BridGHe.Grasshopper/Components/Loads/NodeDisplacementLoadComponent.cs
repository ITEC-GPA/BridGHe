using System;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class NodeDisplacementLoadComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the NodeLoadComponent class.
        /// </summary>
        public NodeDisplacementLoadComponent()
          : base("Node Displacement", "ND", "Defines a node displacement", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "The node where define the displacement", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            pManager.AddVectorParameter("Translation", "T", "The translation in the global XYZ direction", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddVectorParameter("Rotation", "R", "The rotation in the global XYZ direction", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;
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
            Vector3d translation = Vector3d.Zero;
            Vector3d rotation = Vector3d.Zero;
            if (!DA.GetData("Node", ref node))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            DA.GetData("Translation", ref translation);
            DA.GetData("Rotation", ref rotation);

            if (translation == Vector3d.Zero && rotation == Vector3d.Zero)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Define at least a displacement");
                DA.AbortComponentSolution();
                return;
            }

            NodeDisplacementLoadModel load = new NodeDisplacementLoadModel(lc.Value,
                            new NodeDisplacementLoadModel.LoadData(translation, rotation));
            GH_Node newNode = new GH_Node(node);
            newNode.Value.Loads.Add(load);
            DA.SetData("Node", newNode);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("ee96d3ac-9028-4ddd-a4a9-3e49333fc283");
    }
}