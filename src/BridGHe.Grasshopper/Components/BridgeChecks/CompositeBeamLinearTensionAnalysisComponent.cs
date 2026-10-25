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
            GH_CompositeBeam beam = null;
            BeamForceResultModel beamForceResultOnlySteel = null;
            BeamForceResultModel beamForceResultInfinite = null;
            BeamForceResultModel beamForceResultInstant = null;
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



            bool constantSection = false;

            double start_bfw = beam.Value.StartBeamProperty.SteelSection.B1;       // bottom flange width
            double start_tfw = beam.Value.StartBeamProperty.SteelSection.B2;       // top flange width
            double start_h = beam.Value.StartBeamProperty.SteelSection.D;          // heigth
            double start_bft = beam.Value.StartBeamProperty.SteelSection.T1;       // Bottom flange thickness
            double start_tft = beam.Value.StartBeamProperty.SteelSection.T2;        // Top flange thickness
            double start_wt = beam.Value.StartBeamProperty.SteelSection.T3;        // Web thickness

            double start_hh = beam.Value.StartBeamProperty.ConcreteSection.Height;      // heigth
            double start_b = beam.Value.StartBeamProperty.ConcreteSection.Width;       // base
            double start_topRebarsAreaTot = beam.Value.StartBeamProperty.ConcreteSection.TopRebarArea;
            double start_topRebarsCover = beam.Value.StartBeamProperty.ConcreteSection.TopCover;
            double start_bottomRebarsAreaTot = beam.Value.StartBeamProperty.ConcreteSection.BottomRebarArea;
            double start_bottomRebarsCover = beam.Value.StartBeamProperty.ConcreteSection.BottomCover;

            double start_verticalOffset = beam.Value.StartBeamProperty.VerticalOffset;

            MaterialModel start_concreteModel = beam.Value.StartBeamProperty.ConcreteMaterial;
            MaterialModel start_steelModel = beam.Value.StartBeamProperty.SteelMaterial;
            MaterialModel start_rebarModel = beam.Value.StartBeamProperty.RebarMaterial;

            double end_bfw = beam.Value.EndBeamProperty.SteelSection.B1;       // bottom flange width
            double end_tfw = beam.Value.EndBeamProperty.SteelSection.B2;       // top flange width
            double end_h = beam.Value.EndBeamProperty.SteelSection.D;          // heigth
            double end_bft = beam.Value.EndBeamProperty.SteelSection.T1;       // Bottom flange thickness
            double end_tft = beam.Value.EndBeamProperty.SteelSection.T2;        // Top flange thickness
            double end_wt = beam.Value.EndBeamProperty.SteelSection.T3;        // Web thickness

            double end_hh = beam.Value.EndBeamProperty.ConcreteSection.Height;      // heigth
            double end_b = beam.Value.EndBeamProperty.ConcreteSection.Width;       // base
            double end_topRebarsAreaTot = beam.Value.EndBeamProperty.ConcreteSection.TopRebarArea;
            double end_topRebarsCover = beam.Value.EndBeamProperty.ConcreteSection.TopCover;
            double end_bottomRebarsAreaTot = beam.Value.EndBeamProperty.ConcreteSection.BottomRebarArea;
            double end_bottomRebarsCover = beam.Value.EndBeamProperty.ConcreteSection.BottomCover;

            double end_verticalOffset = beam.Value.EndBeamProperty.VerticalOffset;

            MaterialModel end_concreteModel = beam.Value.EndBeamProperty.ConcreteMaterial;
            MaterialModel end_steelModel = beam.Value.EndBeamProperty.SteelMaterial;
            MaterialModel end_rebarModel = beam.Value.EndBeamProperty.RebarMaterial;

            if (start_bfw == end_bfw && start_tfw == end_tfw && start_h == end_h && start_bft == end_bft && start_tft == end_tft && start_wt == end_wt)
            {
                if (start_hh == end_hh && start_b == end_b && start_topRebarsAreaTot == end_topRebarsAreaTot && start_topRebarsCover == end_topRebarsCover &&
                    start_bottomRebarsAreaTot == end_bottomRebarsAreaTot && start_bottomRebarsCover == end_bottomRebarsCover &&
                    start_verticalOffset == end_verticalOffset)
                {
                    if (start_concreteModel == end_concreteModel && start_steelModel == end_steelModel && start_rebarModel == end_rebarModel)
                        constantSection = true;
                }
            }

            if (constantSection)
            {
                SectionH sectionH = new SectionH(start_h, start_wt, start_tfw, start_tft, start_bfw, start_bft, beam.Value.BeamId);

                ConcreteMaterialEN1992 concreteMaterialEN1992 = new ConcreteMaterialEN1992(start_concreteModel.Name, start_concreteModel.SpecificCompressiveStrength,
                    ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
                SteelMaterialEN1992 steelMaterialEN1992 = new SteelMaterialEN1992(start_steelModel.Name, start_steelModel.Modulus, start_steelModel.MinimumYieldStress, start_steelModel.MinimumTensileStress,
                    0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
                SteelMaterialEN1992 rebarMaterial = new SteelMaterialEN1992(start_rebarModel.Name, start_rebarModel.Modulus, start_rebarModel.MinimumYieldStress, start_rebarModel.MinimumTensileStress,
                    0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);

                int numberRebars = 3;
                double rebarAreaTop = start_topRebarsAreaTot / numberRebars;
                double rebarDiameterTop = Math.Sqrt(rebarAreaTop * 4 / Math.PI);

                RebarSectionCircular rebarTop = rebarDiameterTop != 0 ? new RebarSectionCircular("", rebarDiameterTop, rebarMaterial) : null;
                double rebarDiameterBottom = Math.Sqrt(start_bottomRebarsAreaTot / numberRebars * 4 / Math.PI);
                RebarSectionCircular rebarBottom = rebarDiameterBottom != 0 ? new RebarSectionCircular("", rebarDiameterBottom, rebarMaterial) : null;
                ReinforcedConcreteSection reinforcedConcreteSection = BridGHe.Common.Checks.CompositeSectionFactory.Create(start_b, start_hh, concreteMaterialEN1992, rebarTop, start_b / numberRebars,
                    start_topRebarsCover, rebarBottom, start_b / numberRebars, sectionH, steelMaterialEN1992, start_bottomRebarsCover, start_verticalOffset, beam.Value.StartBeamProperty.Name);


                double phiInfinite = CalculateHomogenizedFactorPhi(homogenizedFactorInfinite, steelMaterialEN1992, concreteMaterialEN1992);
                double phiInstant = CalculateHomogenizedFactorPhi(homogenizedFactorInstant, steelMaterialEN1992, concreteMaterialEN1992);

                var cs = GetCoordinateSystem(reinforcedConcreteSection);

                GPC.Checkers.CompositeBridge.BridgeAnalysisOptions options = new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare,  WebBuckling = webFlangeInstab, TopFlangeBuckling = topFlangeInstab, BottomFlangeBuckling = bottomFlangeInstab };

                ResultBeamForces resultBeamForcesOnlySteel = Common.Checks.BridgeForceConvention.FromStraus(beamForceResultOnlySteel, cs);
                ResultBeamForces resultBeamForcesInfinite = Common.Checks.BridgeForceConvention.FromStraus(beamForceResultInfinite, cs);
                ResultBeamForces resultBeamForcesInstant = Common.Checks.BridgeForceConvention.FromStraus(beamForceResultInstant, cs);

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
