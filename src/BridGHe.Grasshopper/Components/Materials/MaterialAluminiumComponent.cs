using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace BridGHe.Grasshopper.Components.Materials
{
    public class MaterialAluminiumComponent : GH_Component
    {
        protected static string[] _types = { "Wrought", "Cast-Mold", "Cast-Sand" };

        /// <summary>
        /// Initializes a new instance of the MaterialAluminiumComponent class.
        /// </summary>
        public MaterialAluminiumComponent()
          : base("Aluminium", "AM", "Aluminium material", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_MATERIALS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The name of the material", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Type", "T", "The aluminium type", GH_ParamAccess.item);
            Param_Integer param = (Param_Integer)pManager[1];
            for (int i = 0; i < _types.Length; i++)
                param.AddNamedValue(_types[i], i);
            pManager.AddTextParameter("Alloy", "A", "The aluminium alloy designation", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Material", "M", "The Steel material", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";
            int type_idx = 0;
            string alloy = "";
            if (!DA.GetData("Name", ref name))
                return;
            if (!DA.GetData("Type", ref type_idx))
                return;
            if (!DA.GetData("Alloy", ref alloy))
                return;

            // User decision of 6/11: fu = 262 MPa on GPC (0.3.0 stored the shear value 165.5 as the tensile ultimate strength).
            var recipe = Common.Gpc.Builders.GpcMaterialBuilders.Aluminium(name, type_idx, alloy);
            // R6.2 part C (§1bis, regola doppia): the GPC material by kind from the recipe of the component, attached to the FEM value made from
            // the same recipe (transitional until R6.5).
            var goo = new GH_Material(Common.Gpc.Builders.GpcMaterialBuilders.Femm(recipe));
            goo.AttachGpc(Common.Gpc.Builders.GpcMaterialBuilders.Material(recipe));
            DA.SetData(0, goo);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("876df96b-8734-4247-be25-e84a8f302d68");
    }
}