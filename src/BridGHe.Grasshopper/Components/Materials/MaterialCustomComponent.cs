using System;
using System.Collections.Generic;
using BridGHe.Common.Helpers;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using GPC.Utilities.Units;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Materials
{
    public class MaterialCustomComponent : GH_Component
    {
        private List<int> _paramsIdx;
        private List<string> _paramsName;

        /// <summary>
        /// Initializes a new instance of the MaterialCustomComponent class.
        /// </summary>
        public MaterialCustomComponent()
          : base("Custom Material", "CM", "Custom material", Helpers.CategoryNameConstants.CATEGORY_MODEL, Helpers.CategoryNameConstants.SUBCATEGORY_MATERIALS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The name of the material", GH_ParamAccess.item);
            // Kind param
            pManager.AddIntegerParameter("Kind", "K", "The kind of the material", GH_ParamAccess.item);
            Param_Integer param = (Param_Integer)pManager[1];
            Array values = Enum.GetValues(typeof(MaterialModel.MaterialKind));
            foreach (MaterialModel.MaterialKind val in values)
                param.AddNamedValue(val.GetDescription(), (int)val);
            pManager.AddIntegerParameter("Units", "U", "The measure units", GH_ParamAccess.item);
            // Mesaure units param
            Param_Integer type_param = (Param_Integer)pManager[2];
            values = Enum.GetValues(typeof(ModelModel.ModelUnits));
            foreach (ModelModel.ModelUnits val in values)
                type_param.AddNamedValue(val.GetDescription(), (int)val);
            // Material data params
            _paramsIdx = new List<int>()
            {
                pManager.AddNumberParameter("Modulus", "E", "Modulus", GH_ParamAccess.item),
                pManager.AddNumberParameter("Poisson Ratio", char.ConvertFromUtf32(0x3BD), "Poisson Ratio", GH_ParamAccess.item),
                pManager.AddNumberParameter("Thermal Expansion", char.ConvertFromUtf32(0x3B1), "Thermal Expansion", GH_ParamAccess.item),
                pManager.AddNumberParameter("Shear Modulus", "G", "Shear Modulus", GH_ParamAccess.item),
                pManager.AddBooleanParameter("Use Poisson", char.ConvertFromUtf32(0x3BD) + "?", "Use Poisson", GH_ParamAccess.item),
                pManager.AddNumberParameter("Density", char.ConvertFromUtf32(0x3C1), "Density", GH_ParamAccess.item),
                pManager.AddNumberParameter("Yielding", "fk", "Yielding", GH_ParamAccess.item),
                pManager.AddNumberParameter("Strength for Steel", "ftk", "Strength", GH_ParamAccess.item),
            };

            _paramsName = new List<string>();
            foreach (int i in _paramsIdx)
            {
                pManager[i].Optional = true;
                _paramsName.Add(pManager[i].Name);
            }
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Material", "M", "The custom material", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";
            int kind = 0;
            int units = 0;
            double modulus = 0, ni = 0, alpha = 0, g = 0, rho = 0, fk = 0, ftk = 0;
            bool use_poisson = false;

            if (!DA.GetData("Units", ref units))
                return;
            UpdateInputDescriptions((Common.Gpc.DefinitionUnits)units);

            if (!DA.GetData("Kind", ref kind))
                return;
            Message = KindLabel(kind);
            if (!DA.GetData("Name", ref name))
                return;
            if (!DA.GetData(_paramsIdx[0], ref modulus))
                return;
            if (!DA.GetData(_paramsIdx[1], ref ni))
                return;
            if (!DA.GetData(_paramsIdx[2], ref alpha))
                return;
            if (!DA.GetData(_paramsIdx[3], ref g))
                return;
            if (!DA.GetData(_paramsIdx[4], ref use_poisson))
                return;
            if (!DA.GetData(_paramsIdx[5], ref rho))
                return;
            if (!DA.GetData(_paramsIdx[6], ref fk))
                return;
            if (!DA.GetData(_paramsIdx[7], ref ftk))
                return;

            // Convert to Nmm
            // P2K (P1): the table of the former FEM ModelHelper, without the FEM type (same factors).
            Helpers.GpcOnlyProperties.UtilitiesUnits(
                (Common.Gpc.DefinitionUnits)units,
                out UnitsConvert.LengthUnits lengthUnits,
                out UnitsConvert.ForceUnits forceUnits,
                out UnitsConvert.MassUnits massUnits,
                out UnitsConvert.PressureUnits pressureUnits,
                out UnitsConvert.TemperatureUnits temperatureUnits);

            modulus = UnitsConvert.ConvertToDefaultUnits(modulus, pressureUnits, 1);
            g = UnitsConvert.ConvertToDefaultUnits(g, pressureUnits, 1);
            rho = UnitsConvert.ConvertToDefaultUnits(rho, massUnits, 1, lengthUnits, -3);
            fk = UnitsConvert.ConvertToDefaultUnits(fk, pressureUnits, 1);
            ftk = UnitsConvert.ConvertToDefaultUnits(ftk, pressureUnits, 1);

            var recipe = Common.Gpc.Builders.GpcMaterialBuilders.Custom(kind, name, modulus, ni, alpha, g, use_poisson, rho, fk, ftk);
            // P2K (P1): the material by its GPC datum alone, by kind from the recipe of the component.
            DA.SetData(0, GH_Material.FromGpc(Common.Gpc.Builders.GpcMaterialBuilders.Material(recipe), recipe.Kind));
        }

        /// <summary>P2K (P1): the kinds of the former FEM <c>MaterialModel.MaterialKind</c> (same labels and integers), in the order of its
        /// <c>Enum.GetValues</c> (by unsigned value: Undefined = -1 last), for the Kind menu and the message.</summary>
        private static readonly (string Label, int Value)[] Kinds = { ("Steel", 0), ("Concrete", 1), ("Aluminium", 2), ("Rebar", 3), ("Undefined", -1) };

        /// <summary>P2K (P1): the label of a kind, as the former enum named it (an unknown integer by its digits).</summary>
        private static string KindLabel(int kind)
        {
            foreach (var (label, value) in Kinds)
                if (value == kind) return label;
            return kind.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private void UpdateInputDescriptions(Common.Gpc.DefinitionUnits units)
        {
            switch (units)
            {
                //do not change the variables here. also default units in utilities are affected
                case Common.Gpc.DefinitionUnits.Knm:
                    Params.Input[_paramsIdx[0]].NickName = _paramsName[0] + " (kPa)";
                    Params.Input[_paramsIdx[2]].NickName = _paramsName[2] + " (1/C)";
                    Params.Input[_paramsIdx[3]].NickName = _paramsName[3] + " (kPa)";
                    Params.Input[_paramsIdx[5]].NickName = _paramsName[5] + " (ton/m³)";
                    Params.Input[_paramsIdx[6]].NickName = _paramsName[6] + " (kPa)";
                    Params.Input[_paramsIdx[7]].NickName = _paramsName[7] + " (kPa)";
                    break;
                case Common.Gpc.DefinitionUnits.SI:
                    Params.Input[_paramsIdx[0]].NickName = _paramsName[0] + " (Pa)";
                    Params.Input[_paramsIdx[2]].NickName = _paramsName[2] + " (1/C)";
                    Params.Input[_paramsIdx[3]].NickName = _paramsName[3] + " (Pa)";
                    Params.Input[_paramsIdx[5]].NickName = _paramsName[5] + " (kg/m³)";
                    Params.Input[_paramsIdx[6]].NickName = _paramsName[6] + " (Pa)";
                    Params.Input[_paramsIdx[7]].NickName = _paramsName[7] + " (Pa)";
                    break;
                case Common.Gpc.DefinitionUnits.Nmm:
                    Params.Input[_paramsIdx[0]].NickName = _paramsName[0] + " (MPa)";
                    Params.Input[_paramsIdx[2]].NickName = _paramsName[2] + " (1/C)";
                    Params.Input[_paramsIdx[3]].NickName = _paramsName[3] + " (MPa)";
                    Params.Input[_paramsIdx[5]].NickName = _paramsName[5] + " (ton/mm³)";
                    Params.Input[_paramsIdx[6]].NickName = _paramsName[6] + " (MPa)";
                    Params.Input[_paramsIdx[7]].NickName = _paramsName[7] + " (MPa)";
                    break;
                case Common.Gpc.DefinitionUnits.IPS:
                    Params.Input[_paramsIdx[0]].NickName = _paramsName[0] + " (psi)";
                    Params.Input[_paramsIdx[2]].NickName = _paramsName[2] + " (1/F)";
                    Params.Input[_paramsIdx[3]].NickName = _paramsName[3] + " (psi)";
                    Params.Input[_paramsIdx[5]].NickName = _paramsName[5] + " (lb/in³)";
                    Params.Input[_paramsIdx[6]].NickName = _paramsName[6] + " (psi)";
                    Params.Input[_paramsIdx[7]].NickName = _paramsName[7] + " (psi)";
                    break;
            }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("9404d0c7-cca7-4f29-b7f5-baf001386675");

        // P2K (P1): the FEM enums the registration of the ports names, shadowed by private types of the same name, members, integers and
        // descriptions (Common/Models): the ports and menus are those of before, token for token (contract audit), with no FEM type.
        private static class ModelModel
        {
            internal enum ModelUnits
            {
                [System.ComponentModel.Description("kNm (m, kN, T, kPa, s, kJ, C)")] Knm,
                [System.ComponentModel.Description("SI (m, N, kg, Pa, s, J, C)")] SI,
                [System.ComponentModel.Description("Nmm (mm, N, T, MPa, s, mJ, C)")] Nmm,
                [System.ComponentModel.Description("PSI (in, lbf, lb, psi, s, btu, F)")] IPS
            }
        }
        private static class MaterialModel
        {
            internal enum MaterialKind { Undefined = -1, Steel, Concrete, Aluminium, Rebar }
        }
    }
}