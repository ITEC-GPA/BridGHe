using System;
using System.Collections.Generic;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Materials
{
    public class MaterialConcreteComponent : GH_Component
    {
        protected static string[] _grades = { "C8/10", "C12/15", "C16/20", "C20/25", "C25/30", "C28/35", "C32/40", "C35/45", "C40/50",
            "C45/55", "C50/60", "C55/67", "C60/75", "C70/85", "C80/95", "C90/105" };

        /// <summary>
        /// Initializes a new instance of the MaterialConcreteComponent class.
        /// </summary>
        public MaterialConcreteComponent()
          : base("Concrete", "CM", "Concrete Material", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_MATERIALS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The name of the material", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Grade", "G", "The steel grade", GH_ParamAccess.item);
            Param_Integer param = pManager[1] as Param_Integer;
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

            var recipe = Common.Gpc.Builders.GpcMaterialBuilders.Concrete(name, grade);
            // P2K (P1): the material by its GPC datum alone, by kind from the recipe of the component.
            DA.SetData(0, GH_Material.FromGpc(Common.Gpc.Builders.GpcMaterialBuilders.Material(recipe), recipe.Kind));
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("c1dfa83d-e67e-4957-a375-da114a455158");
    }
}