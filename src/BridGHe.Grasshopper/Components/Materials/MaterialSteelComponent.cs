using System;
using System.Windows.Forms;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace BridGHe.Grasshopper.Components.Materials
{
    public class MaterialSteelComponent : GH_Component
    {
        protected static string[] _grades = { "S235", "S275", "S355", "S450" };

        /// <summary>
        /// Initializes a new instance of the SteelMaterialComponent class.
        /// </summary>
        public MaterialSteelComponent()
          : base("Steel", "SM", "Steel Material", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_MATERIALS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The name of the material", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Grade", "G", "The steel grade", GH_ParamAccess.item);
            Param_Integer param = (Param_Integer)pManager[1];
            for (int i = 0; i < _grades.Length; i++)
                param.AddNamedValue(_grades[i], i);
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
            int grade = 0;
            if (!DA.GetData("Name", ref name))
                return;
            if (!DA.GetData("Grade", ref grade))
                return;

            Message = _grades[grade];

            var recipe = Common.Gpc.Builders.GpcMaterialBuilders.Steel(name, grade);
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
        public override Guid ComponentGuid => new Guid("5b123f35-7685-415b-9028-5b0fd4d67626");
    }
}