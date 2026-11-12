using System;
using System.Collections.Generic;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace BridGHe.Grasshopper.Components.ElementProperties
{
    public class PlatePropertyComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PlatePropertyComponent class.
        /// </summary>
        public PlatePropertyComponent()
          : base("Plate Property", "PP", "Plate properties", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_PROPERTIES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "Property name", GH_ParamAccess.item);
            int i = pManager.AddIntegerParameter("Type", "T", "The property type", GH_ParamAccess.item);
            Param_Integer mtParam = pManager[i] as Param_Integer;
            for (int t = 0; t < TypeLabels.Length; t++)
                mtParam.AddNamedValue(TypeLabels[t], t);
            i = pManager.AddNumberParameter("Membrane Thickness", "MT", "The membrane thickness value", GH_ParamAccess.item);
            pManager[i].Optional = true;
            i = pManager.AddNumberParameter("Bending Thickness", "BT", "The bending thickness value", GH_ParamAccess.item, 0);
            pManager[i].Optional = true;
            i = pManager.AddGenericParameter("Material", "M", "Material", GH_ParamAccess.item);
            pManager[i].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Property", "PP", "Plate property", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";
            int type = 0;
            double mem_thick = 0;
            double ben_thick = 0;
            GH_Material material = null;
            if (!DA.GetData("Name", ref name))
                return;
            if (!DA.GetData("Type", ref type))
                return;
            DA.GetData("Membrane Thickness", ref mem_thick);
            DA.GetData("Bending Thickness", ref ben_thick);
            bool has_material = DA.GetData("Material", ref material);

            if (type != 5)
            {
                bool missing_values = false;
                if (mem_thick == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter Membrane Thickness failed to collect data");
                    missing_values = true;
                }
                if (!has_material)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter Material failed to collect data");
                    missing_values = true;
                }
                if (missing_values)
                    return;
            }

            // R6.5 P1 lane 2a: the property by its GPC datum alone (rule of passo 75; a refused property gives an error and no output). The
            // message is the type. P2K (P1): the only path.
            Message = TypeLabel(type);
            // L2C: with the bending thickness of a membrane beside the GPC property (Bridge Deck Mesh checks it, as before).
            Helpers.GpcOnlyElements.Set(DA, 0, Helpers.GpcOnlyProperties.PlateProperty(this, () => Common.Gpc.Builders.GpcPropertyBuilders.Plate(name, type, material?.Gpc, mem_thick, ben_thick),
                Common.Gpc.Builders.GpcPropertyBuilders.PlateMembraneBending(type, ben_thick)));
        }

        /// <summary>P2K (P1): the labels of the former FEM <c>PlatePropertyType</c> by its value (0 to 6, the Type menu; its Undefined = -1 is
        /// not in the menu).</summary>
        private static readonly string[] TypeLabels = { "Shell-Thin", "Shell-Thick", "Plate-Thin", "Plate-Thick", "Membrane", "Load Patch", "Shear Panel" };

        /// <summary>P2K (P1): the message of a type, as the former enum described it (an unknown integer by its digits).</summary>
        private static string TypeLabel(int type) => type >= 0 && type < TypeLabels.Length ? TypeLabels[type]
            : type == -1 ? "Undefined" : type.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("20915977-5436-4dcb-b091-1250312caa11");
    }
}