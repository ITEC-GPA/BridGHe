using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Linq;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class BeamPropertyCompositeComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CompositeSectionComponent class.
        /// </summary>
        public BeamPropertyCompositeComponent()
          : base("Composite Beam Property", "CS", "Composite H + reinforced slab. Steel-referenced Straus properties for steel-only, instant, long-term and shrinkage phases (N-mm-MPa).", Helpers.CategoryNameConstants.CATEGORY_CHECKS, Helpers.CategoryNameConstants.SUBCATEGORY_PROPERTIES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The name of the section", GH_ParamAccess.item, "");
            pManager.AddGenericParameter("Concrete Slab Section", "CSS", "Concrete slab geometry", GH_ParamAccess.item);
            pManager.AddGenericParameter("Steel Section", "SS", "I section geometry", GH_ParamAccess.item);
            pManager.AddGenericParameter("Concrete Material", "CM", "Material of the concrete slab", GH_ParamAccess.item);
            pManager.AddGenericParameter("Steel Material", "SM", "Material of the steel section profile", GH_ParamAccess.item);
            int i = pManager.AddGenericParameter("Reinforcement Material", "RM", "Material of the rebars", GH_ParamAccess.item);
            pManager[i].Optional = true;

            pManager.AddNumberParameter("n0", "n0", "Homogenization coefficient for instant time loads", GH_ParamAccess.item, 6);
            pManager.AddNumberParameter("n ∞", "n ∞", "Homogenization coefficient for infinite time loads", GH_ParamAccess.item, 18);
            i = pManager.AddNumberParameter("n ritiro", "n sh", "Shrinkage modular ratio Es/Ec,eff. If omitted, uses n ∞ for compatibility.", GH_ParamAccess.item);
            pManager[i].Optional = true;
            pManager.AddBooleanParameter("Consider Concrete", "CC", "If true, consider concrete in the n0 and n ∞ phases", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Consider Rebars", "CR", "If true, consider rebars in the composite phases", GH_ParamAccess.item, true);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Composite Section", "CS", "The composite section for BridGHe analysis", GH_ParamAccess.item);
            pManager.AddGenericParameter("Only Steel Property", "CS", "The steel section", GH_ParamAccess.item);
            pManager.AddGenericParameter("n0 Beam Section", "CS", "The homogenized beam section with n0 coefficient", GH_ParamAccess.item);
            pManager.AddGenericParameter("n ∞ Beam Section", "CS", "The homogenized beam section with n ∞ coefficient", GH_ParamAccess.item);
            pManager.AddGenericParameter("n ritiro Beam Section", "CS sh", "Steel-referenced generic Straus property for shrinkage: A, I11, I22 and steel torsion J. Connect to Beam Override.", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";
            object concreteSection = null;
            GH_BeamSection steelSection = null;
            DataTypes.Fem.GH_Material concreteMaterial = null;
            DataTypes.Fem.GH_Material steelMaterial = null;
            DataTypes.Fem.GH_Material rebarMaterial = null;
            double n0 = 0;
            double ninf = 0;
            bool considerConcrete = true;
            bool considerRebars = true;

            if (!DA.GetData(0, ref name))
                return;
            if (!DA.GetData(1, ref concreteSection))
                return;
            if (!DA.GetData(2, ref steelSection))
                return;
            if (!DA.GetData(3, ref concreteMaterial))
                return;
            if (!DA.GetData(4, ref steelMaterial))
                return;
            DA.GetData(5, ref rebarMaterial);
            if (!DA.GetData(6, ref n0))
                return;
            if (!DA.GetData(7, ref ninf))
                return;
            double nShrinkage = ninf;
            // Read() retains the port order saved by each document.
            DA.GetData("n ritiro", ref nShrinkage);
            DA.GetData("Consider Concrete", ref considerConcrete);
            DA.GetData("Consider Rebars", ref considerRebars);

            if (!double.IsFinite(n0) || n0 <= 0 || !double.IsFinite(ninf) || ninf <= 0 || !double.IsFinite(nShrinkage) || nShrinkage <= 0)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Modular ratios must be finite and positive."); return; }
            // F6 (0.4.0): the composite section is a GPC definition with a name (no example or archived revision has an empty one, scan of 5/11).
            if (string.IsNullOrWhiteSpace(name))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Name required: connect a name for the composite section (it identifies the section in the checks and in the reports)."); return; }

            // P2K (P10): the slab by its GPC datum (Concrete Slab Section); TRANSITIONAL (P2-taglio): the former slab value of the in-memory tests.
            var slab = (concreteSection as GH_Goo<object>)?.Value switch
            {
                Common.Checks.ConcreteSlabSection s => s,
                _ => null,
            };
            // R6.5 P1 lane 2b (P2K: every input): the five outputs by their GPC data alone; a refusal gives an error and no outputs.
            SolveGpcOnly(DA, name, slab, steelSection, concreteMaterial, steelMaterial, rebarMaterial, n0, ninf, nShrinkage, considerConcrete, considerRebars);
        }

        /// <summary>R6.5 P1 lane 2b: the outputs on the GPC data of the inputs alone, the same checks and messages; a builder that refuses gives an
        /// error and no outputs (no FEM value to fall back on).</summary>
        private void SolveGpcOnly(IGH_DataAccess DA, string name, Common.Checks.ConcreteSlabSection slab, GH_BeamSection steelSection, DataTypes.Fem.GH_Material concreteMaterial,
            DataTypes.Fem.GH_Material steelMaterial, DataTypes.Fem.GH_Material rebarMaterial, double n0, double ninf, double nShrinkage, bool considerConcrete, bool considerRebars)
        {
            // An input is missing when it has neither a FEM value nor a GPC datum (the datum of a FEM-backed input is read inside the builders,
            // where a refusal of its conversion is an error on the component).
            if (slab == null || !slab.IsValid || steelSection == null || steelSection.Value == null && steelSection.Gpc == null || Missing(steelMaterial) || Missing(concreteMaterial))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid slab, steel section or materials."); return; }
            if (slab.TotalRebarArea > 0 && Missing(rebarMaterial))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Reinforcement material is required when reinforcement area is positive."); return; }
            var definition = Helpers.GpcOnlyProperties.Datum(this, () => Common.Gpc.Builders.GpcPropertyBuilders.Composite(name, steelSection.Gpc, slab.Width, slab.Height,
                slab.TopRebarArea, slab.TopCover, slab.BottomRebarArea, slab.BottomCover, concreteMaterial.Gpc, steelMaterial.Gpc, rebarMaterial?.Gpc, n0, ninf, nShrinkage,
                considerConcrete, considerRebars, slab.BarsPerRow));
            if (definition == null) return;
            var steel = Helpers.GpcOnlyProperties.BeamProperty(this, () => Common.Gpc.Builders.GpcPropertyBuilders.OnlySteel(name, steelSection.Gpc, steelMaterial.Gpc, steelMaterial.GpcKind));
            if (steel == null) return;
            GH_BeamProperty Homogenised(double n, string suffix) => !considerConcrete && !considerRebars ? steel
                : Helpers.GpcOnlyProperties.BeamProperty(this, () => Common.Gpc.Builders.GpcPropertyBuilders.Homogenised(definition, n, suffix, steelMaterial.Gpc, steelMaterial.GpcKind));
            var outputs = new[] { Homogenised(n0, "_N0_istant"), Homogenised(ninf, "_N_infinite"), Homogenised(nShrinkage, "_N_shrinkage") };
            if (outputs.Any(o => o == null)) return;
            DA.SetData(0, GH_CompositeBeamProperty.FromGpc(definition));
            DA.SetData(1, steel);
            DA.SetData(2, outputs[0]);
            DA.SetData(3, outputs[1]);
            DA.SetData(4, outputs[2]);
        }

        private static bool Missing(DataTypes.Fem.GH_Material m) => m == null || m.Value == null && m.Gpc == null;

        public override bool Read(GH_IReader reader)
        {
            // GH restores static parameter data by index. Match the saved order first,
            // otherwise a Boolean archive can be read into the numeric shrinkage port.
            var parameters = Params.Input.Skip(8).ToDictionary(p => p.Name, StringComparer.Ordinal);
            var savedNames = Enumerable.Range(8, 3)
                .Select(i => reader.FindChunk("param_input", i)?.GetString("Name"))
                .Where(name => name != null).ToArray();
            if (savedNames.Length >= 2 && savedNames.Distinct().Count() == savedNames.Length &&
                savedNames.All(parameters.ContainsKey))
            {
                var ordered = savedNames.Concat(parameters.Keys.Except(savedNames)).ToArray();
                for (int i = 0; i < ordered.Length; i++) Params.Input[8 + i] = parameters[ordered[i]];
                Params.OnParametersChanged();
            }
            return base.Read(reader);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("f87c7066-e509-4646-9ef5-79691675cbf4");
    }
}
