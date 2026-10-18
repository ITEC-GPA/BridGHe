using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class CompositeSectionComponent_OBSOLETE : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CompositeSectionComponent class.
        /// </summary>
        public CompositeSectionComponent_OBSOLETE()
          : base("Composite Beam Property", "CBP", "Define a mixed section composed by two base sections and 2 materials", Helpers.CategoryNameConstants.CATEGORY_CHECKS, Helpers.CategoryNameConstants.SUBCATEGORY_BRIDGE)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The name of the section", GH_ParamAccess.item);
            pManager.AddGenericParameter("Concrete Section", "CS", "Concrete slab geometry", GH_ParamAccess.item);
            pManager.AddGenericParameter("Steel Section", "SS", "I section geometry", GH_ParamAccess.item);
            pManager.AddGenericParameter("Concrete Material", "CM", "Material of the concrete slab", GH_ParamAccess.item);
            pManager.AddGenericParameter("Steel Material", "SM", "Material of the steel section profile", GH_ParamAccess.item);
            pManager.AddGenericParameter("Reinforcement Material", "RM", "Material of the rebars", GH_ParamAccess.item);
            pManager.AddNumberParameter("Concrete Offset", "CO", "The distance between top flange and concrete slab", GH_ParamAccess.item, 0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Composite Section", "CS", "The composite section", GH_ParamAccess.item);
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
            double concreteOffset = 0;

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
            if (!DA.GetData(5, ref rebarMaterial))
                return;
            DA.GetData(6, ref concreteOffset);

            // TODO: Dati che si possono prelevare dagli input dei materiali
            // SpecificCompressiveStrength
            // MinimumYieldStress
            // MinimumYieldStress (rebar)
            // steel.Modulus / concrete.Modulus

            CompositeBeamPropertyModel compositeBeamPropertyModel = new CompositeBeamPropertyModel()
            {
                Name = name,
                ConcreteSection = (concreteSection as GH_Goo<object>).Value as SectionConcreteSlabModel,
                SteelSection = steelSection.Value,
                SteelMaterial = steelMaterial.Value,
                ConcreteMaterial = concreteMaterial.Value,
                RebarMaterial = rebarMaterial.Value,
                VerticalOffset = concreteOffset,
            };

            GH_CompositeBeamProperty section = new GH_CompositeBeamProperty(compositeBeamPropertyModel);

            DA.SetData(0, section);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("ee8b7912-f07c-4a17-8a26-e75959ed080f");
    }
}