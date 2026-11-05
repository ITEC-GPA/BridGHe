using BridGHe.Common.Checks;
using StressAnalysisResult = BridGHe.Common.Checks.BridgeStressResult;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GPC.Checkers.Concrete.Attributes;
using GPC.Checkers.Concrete.Checkers;
using GPC.Checkers.Concrete.Results;
using GPC.Checkers.Concrete.SectionSolvers;
using GPC.Geometry;
using GPC.Model.Materials;
using GPC.Model.Results;
using GPC.Model.Sections;
using GPC.Model.Sections.Concrete;
using GPC.Model.Sections.Rebar;
using GPC.Model.Standards;
using System;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class CompositeBeamLinearTensionAnalysisComponent_OBSOLETE : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MeshRemap class.
        /// </summary>
        public CompositeBeamLinearTensionAnalysisComponent_OBSOLETE()
          : base("Composite Section Linear Tension Analysis", "CSLTA", "Composite Section Linear Tension Analysis", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_DESIGN)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beams", "B", "The model beams definitions", GH_ParamAccess.item);
            pManager.AddGenericParameter("Loads Only steel Phase", "L", "The loads to check for only steel section, one external force", GH_ParamAccess.item);
            pManager.AddGenericParameter("Loads Infinte Time Phase", "L", "The loads to check for whole section with n=15/18, one external force", GH_ParamAccess.item);
            pManager.AddGenericParameter("Loads Instant Time Phase", "L", "The loads to check for whole section with n=6/7, one external force", GH_ParamAccess.item);
            pManager.AddNumberParameter("Homogenization coefficient Infinite", "HC Inf", "Load homogenization coefficient for infinite time load cases", GH_ParamAccess.item, 18);
            pManager.AddNumberParameter("Homogenization coefficient Istant", "HC Ist", "Load homogenization coefficient for instant time load cases", GH_ParamAccess.item, 6);
            pManager.AddBooleanParameter("Top Flange Subject To Local Instability", "TFLI", "If true, top flange is subjected to loacel instability", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Web Subject To Local Instability", "WLI", "If true, web is subjected to loacel instability", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Bottom Flange Subject To Local Instability", "BLI", "If true, bottom flange is subjected to loacel instability", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Consider concrete", "CC", "If true, consider concrete for calculation of coordiante system and force resultant", GH_ParamAccess.item, true);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Only Steel Buckling", "R", "Analysis results", GH_ParamAccess.item);
            pManager.AddGenericParameter("Infinite Buckling", "R", "Analysis results", GH_ParamAccess.item);
            pManager.AddGenericParameter("Total Buckling", "R", "Analysis results", GH_ParamAccess.item);
            pManager.AddGenericParameter("TauB", "R", "Analysis results", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GpcReleaseGuard.Warn(this);
            GH_CompositeBeam beam = null;
            GH_ObjectWrapper beamForceResultOnlySteel = null;
            GH_ObjectWrapper beamForceResultInfinite = null;
            GH_ObjectWrapper beamForceResultInstant = null;
            double homogenizedFactorInfinite = 0;
            double homogenizedFactorInstant = 0;
            bool topFlangeInstab = false;
            bool webFlangeInstab = false;
            bool bottomFlangeInstab = false;
            bool considerConcrete = false;

            int count = 0;
            if (!DA.GetData(count++, ref beam))
                return;
            if (!DA.GetData(count++, ref beamForceResultOnlySteel))
                return;
            if (!DA.GetData(count++, ref beamForceResultInfinite))
                return;
            if (!DA.GetData(count++, ref beamForceResultInstant))
                return;
            if (!DA.GetData(count++, ref homogenizedFactorInfinite))
                return;
            if (!DA.GetData(count++, ref homogenizedFactorInstant))
                return;
            if (!DA.GetData(count++, ref topFlangeInstab))
                return;
            if (!DA.GetData(count++, ref webFlangeInstab))
                return;
            if (!DA.GetData(count++, ref bottomFlangeInstab))
                return;
            if (!DA.GetData(count++, ref considerConcrete))
                return;



            // Chain E (c): the composite sections from the definitions the composite beam carries (start and end), with the constant-section
            // rule of Super Element Check (GpcCompositeSections.IsConstant by value; the former rule compared the material instances); the section
            // of a constant beam is that of its start definition: three bars per row of a third of the area at the pitch of a third of the width,
            // EN 1992 materials from fck, E, fy, fu with an ultimate strain of 0.1 (the former construction of this component; the steel H is
            // named by the composite property instead of the beam, a name that is not in the results).
            var startDefinition = beam.StartDefinition; var endDefinition = beam.EndDefinition;
            bool constantSection = startDefinition != null && endDefinition != null && Common.Gpc.GpcCompositeSections.IsConstant(startDefinition, endDefinition);

            if (constantSection)
            {
                ReinforcedConcreteSection reinforcedConcreteSection = startDefinition.Section;
                SteelMaterial steelMaterialEN1992 = reinforcedConcreteSection.SteelSections[0].Section.SteelMaterial;
                ConcreteMaterial concreteMaterialEN1992 = reinforcedConcreteSection.ConcreteMaterial;


                double phiInfinite = CalculateHomogenizedFactorPhi(homogenizedFactorInfinite, steelMaterialEN1992, concreteMaterialEN1992);
                double phiInstant = CalculateHomogenizedFactorPhi(homogenizedFactorInstant, steelMaterialEN1992, concreteMaterialEN1992);

                var cs = GetCoordinateSystem(reinforcedConcreteSection);

                GPC.Checkers.CompositeBridge.BridgeAnalysisOptions options = new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare,  WebBuckling = webFlangeInstab, TopFlangeBuckling = topFlangeInstab, BottomFlangeBuckling = bottomFlangeInstab };

                // F6 (a)/(b), R10: the forces of the Force component (numbers, no FEM result object).
                static ResultBeamForces Force(GH_ObjectWrapper goo, CoordinateSystem frame) => goo?.Value is Common.Checks.BridgeForce f
                    ? Common.Checks.BridgeForceConvention.FromStraus(f.Force, f.Case, frame) : throw new ArgumentException("Connect the output of the Force component.");
                ResultBeamForces resultBeamForcesOnlySteel = Force(beamForceResultOnlySteel, cs);
                ResultBeamForces resultBeamForcesInfinite = Force(beamForceResultInfinite, cs);
                ResultBeamForces resultBeamForcesInstant = Force(beamForceResultInstant, cs);

                BridgeCheckAdapter sectionChecker = new BridgeCheckAdapter(reinforcedConcreteSection, cs, options);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, BridgeCheckAdapter.PlanarAssumption);

                bool res = sectionChecker.GetLinearAnalysisResultByStages(
                    out StressAnalysisResult resultOnlySteelBuckling,
                    out StressAnalysisResult resultInfiniteBuckling,
                    out StressAnalysisResult resultBuckling, out double TauB,
                    phiInfinite, phiInstant, resultBeamForcesOnlySteel, resultBeamForcesInfinite, resultBeamForcesInstant,
                    considerConcrete);

                if (res)
                {
                    int index = 0;
                    DA.SetData(index++, resultOnlySteelBuckling);
                    DA.SetData(index++, resultInfiniteBuckling);
                    DA.SetData(index++, resultBuckling);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "TauB non ha un equivalente verificato nell'API gPC attuale: output NaN.");
                    DA.SetData(index++, TauB);
                }
            }
            else
            {
            }
        }

        protected double CalculateHomogenizedFactorPhi(double n, SteelMaterial steelMaterial, ConcreteMaterial concreteMaterial)
        {
            return Common.Checks.BridgeCreepCoefficient.FromModularRatio(n, steelMaterial.ElasticModulusTension, concreteMaterial.ElasticModulusCompression);
        }

        protected CoordinateSystem GetCoordinateSystem(ReinforcedConcreteSection reinforcedConcreteSection)
        {
            return new CoordinateSystem(reinforcedConcreteSection.GetHomogenizedCentroid(out double _, out double _), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("a487d0e7-bb07-4049-90a5-8cd2aa40b183");
    }
}
