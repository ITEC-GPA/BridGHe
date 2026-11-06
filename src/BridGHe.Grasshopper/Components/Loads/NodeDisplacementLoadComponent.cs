using System;
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

            // R6.2 lane A (user decision of 6/11): prescribed displacements are refused in 0.4.0. The component keeps its GUID and ports only to
            // open the existing files and gives no output (no FEM value either): the 0.3.0 exporter applied them on restrained nodes only, from a
            // single case, to every linear case; the correct behaviour (a displacement acting only in its load case) comes after the release.
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, Common.Gpc.Legacy.LegacyModelImport.PrescribedDisplacementNotSupported);
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