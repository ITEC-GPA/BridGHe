using BridGHe.Common.Checks;
using StressAnalysisResult = BridGHe.Common.Checks.BridgeStressResult;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using GPC.Checkers.Concrete.Attributes;
using GPC.Checkers.Concrete.Checkers;
using GPC.Checkers.Concrete.Results;
using GPC.Checkers.Concrete.SectionSolvers;
using GPC.Geometry;
using GPC.Model.Materials;
using GPC.Model.Sections.Concrete;
using GPC.Model.Standards;
using System;
using System.Linq;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class CompositeBeamSelfTensionComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MeshRemap class.
        /// </summary>
        public CompositeBeamSelfTensionComponent()
          : base("Composite Section Self Tension", "CSST", "Composite Section Linear Tension Analysis", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_DESIGN)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Composite Beam Property", "CBP", "Composite Section Property", GH_ParamAccess.item);
            pManager.AddNumberParameter("Homogenization coefficient", "HC", "Load homogenization coefficient for load case", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Strain", "ε", "The strain applicated to the concrete slab", GH_ParamAccess.item, 0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Result", "R", "Analysis results", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_CompositeBeamProperty gH_CompositeBeamProperty = null;
            double n = 0;
            double strain = 0;

            if (!DA.GetData(0, ref gH_CompositeBeamProperty))
                return;
            if (!DA.GetData(1, ref n))
                return;
            if (!DA.GetData(2, ref strain))
                return;

            CompositeBeamPropertyModel compositeBeamPropertyModel = gH_CompositeBeamProperty.Value;
            ReinforcedConcreteSection reinforcedConcreteSection = compositeBeamPropertyModel.GetReinforcedConcreteSection();

            double phi = CalculateHomogenizedFactorPhi(n, reinforcedConcreteSection.SteelSections.First().Section.SteelMaterial, reinforcedConcreteSection.ConcreteMaterial);
            var cs = GetCoordinateSystem(reinforcedConcreteSection, phi);

            GPC.Checkers.CompositeBridge.BridgeAnalysisOptions options = new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare };


            BridgeCheckAdapter sectionChecker = new BridgeCheckAdapter(reinforcedConcreteSection, cs, options);

            StressAnalysisResult result = sectionChecker.GetSelfTension(strain, phi);

            DA.SetData(0, result);
        }

        protected double CalculateHomogenizedFactorPhi(double n, SteelMaterial steelMaterial, ConcreteMaterial concreteMaterial)
        {
            return Common.Checks.BridgeCreepCoefficient.FromModularRatio(n, steelMaterial.ElasticModulusTension, concreteMaterial.ElasticModulusCompression);
        }

        protected CoordinateSystem GetCoordinateSystem(ReinforcedConcreteSection reinforcedConcreteSection, double phi)
        {
            return new CoordinateSystem(reinforcedConcreteSection.GetHomogenizedCentroid(phi, out double _, out double _), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("e7cea623-0b51-485d-ab60-6ef6850945a6");
    }
}
