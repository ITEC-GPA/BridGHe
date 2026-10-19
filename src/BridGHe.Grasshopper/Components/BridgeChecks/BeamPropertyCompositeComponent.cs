using BridGHe.Common.Models;
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

            var slab = (concreteSection as GH_Goo<object>)?.Value as SectionConcreteSlabModel;
            if (slab == null || !slab.IsValid || steelSection?.Value == null || steelMaterial?.Value == null || concreteMaterial?.Value == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid slab, steel section or materials."); return; }
            if (slab.TotalRebarArea > 0 && rebarMaterial?.Value == null)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Reinforcement material is required when reinforcement area is positive."); return; }

            CompositeBeamPropertyModel compositeBeamPropertyModel = new CompositeBeamPropertyModel()
            {
                Name = name,
                ConcreteSection = slab,
                SteelSection = steelSection.Value,
                SteelMaterial = steelMaterial.Value,
                ConcreteMaterial = concreteMaterial.Value,
                RebarMaterial = rebarMaterial?.Value ?? steelMaterial.Value,
                VerticalOffset = 0,
                NInstant = n0,
                NInfinite = ninf,
                NShrinkage = nShrinkage,
                ConsiderConcrete = considerConcrete,
                ConsiderRebar = considerRebars,
            };
            GH_CompositeBeamProperty section = new GH_CompositeBeamProperty(compositeBeamPropertyModel);

            SectionModel steelSectionModel = new SectionModel()
            {
                SectionType = SectionModel.SectionTypes.I,
                D = steelSection.Value.D,
                B1 = steelSection.Value.B1,
                B2 = steelSection.Value.B2,
                T1 = steelSection.Value.T1,
                T2 = steelSection.Value.T2,
                T3 = steelSection.Value.T3,
            };
            steelSectionModel.CalculateSection();
            BeamPropertyModel onlySteelBeamPropModel = new BeamPropertyModel()
            {
                Material = steelMaterial.Value,
                Section = steelSectionModel,
                PropertyType = BeamPropertyType.Beam,
                Name = name + "OnlySteel",
            };
            GH_BeamProperty steelSectionProp = new GH_BeamProperty(onlySteelBeamPropModel);

            DA.SetData(0, section);
            DA.SetData(1, steelSectionProp);
            DA.SetData(2, !considerConcrete && !considerRebars ? steelSectionProp : new GH_BeamProperty(compositeBeamPropertyModel.CreateHomogenizedProperty(n0, "_N0_istant")));
            DA.SetData(3, !considerConcrete && !considerRebars ? steelSectionProp : new GH_BeamProperty(compositeBeamPropertyModel.CreateHomogenizedProperty(ninf, "_N_infinite")));
            DA.SetData(4, !considerConcrete && !considerRebars ? steelSectionProp : new GH_BeamProperty(compositeBeamPropertyModel.CreateHomogenizedProperty(nShrinkage, "_N_shrinkage")));
        }

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
