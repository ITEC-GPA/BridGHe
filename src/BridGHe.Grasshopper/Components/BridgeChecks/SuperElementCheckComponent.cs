using BridGHe.Common.Checks;
using StressAnalysisResult = BridGHe.Common.Checks.BridgeStressResult;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.ComponentAttributes;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
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
using GPC.Utilities.Maths;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class SuperElementCheckComponent : GH_Component
    {
        protected bool _run;

        /// <summary>
        /// Initializes a new instance of the ChecksComponent class.
        /// </summary>
        public SuperElementCheckComponent()
          : base("Super Element Check", "SEC", "Check the selected Super-Element", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_DESIGN)
        {
            _run = false;
        }

        public override void CreateAttributes()
        {
            ComponentAttributes.ComponentOneButtonAttributes attr = new ComponentAttributes.ComponentOneButtonAttributes(this, "Run");
            attr.ButtonPressed += () =>
            {
                _run = true;
                ExpireSolution(true);
            };
            m_attributes = attr;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("BridGHe Model", "FM", "The BridGHe model", GH_ParamAccess.item);
            pManager.AddGenericParameter("Super-Element Beam", "SE", "The Super-Element Beam", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Web Local Instability", "WLI", "If true, web is subjected to loacel instability", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Top Flange Local Instability", "TFLI", "If true, top flange is subjected to loacel instability", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Bottom Flange Local Instability", "BLI", "If true, bottom flange is subjected to loacel instability", GH_ParamAccess.item, true);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Tension Check", "C", "The results", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Tau b", "T", "The results", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Tension Forces", "C", "The forces", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = "N–Mx–Vy";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, BridgeCheckAdapter.PlanarAssumption);
            GH_Model model = new GH_Model();
            List<GH_CompositeBeam> beams = new List<GH_CompositeBeam>();
            bool topFlangeInstab = false;
            bool webFlangeInstab = false;
            bool bottomFlangeInstab = false;

            int n = 0;
            if (!DA.GetData(n++, ref model))
                return;
            if (!DA.GetDataList(n++, beams))
                return;
            if (!DA.GetData(n++, ref webFlangeInstab))
                return;
            if (!DA.GetData(n++, ref topFlangeInstab))
                return;
            if (!DA.GetData(n++, ref bottomFlangeInstab))
                return;


            if (model.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Null model");
                Message = "Error";
                return;
            }
            if (model.Value.Units != ModelModel.ModelUnits.Nmm) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "BridgeChecks richiede modello e sezioni in N-mm-MPa; convertire il modello prima della verifica."); return; }
            List<SuperElementAttributeModel> superElementsBuffer = Common.Helpers.ModelHelper.GetSuperElements(model.Value);
            if (superElementsBuffer == null || superElementsBuffer.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Super-Element in the model");
                Message = "Error";
                return;
            }
            if (beams == null || beams.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No Beams");
                Message = "Error";
                return;
            }

            if (_run)
            {
                try
                {
                    var workflowIndexes = new BridgePhaseAssembler.ResultIndex[beams.Count];
                    var workflowStations = new double[beams.Count][];
                    (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[][] forces = new
                        (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[beams.Count][];

                    (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[][] forcesForOutput = new
                        (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[beams.Count][];

                    (double[] StrainInfiniteTime, double[] StrainIstantTime)[][] strains = new
                        (double[] StrainInfiniteTime, double[] StrainIstantTime)[beams.Count][];

                    BridgeCheckAdapter[][] sectionCheckers = new BridgeCheckAdapter[beams.Count][];
                    (double psiInfinite, double psiInstant, double psiShrinkage)[][] psis = new (double psiInfinite, double psiInstant, double psiShrinkage)[beams.Count][];

                    for (int i = 0; i < beams.Count; i++)
                    {
                        GH_CompositeBeam beam = beams[i];

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
                        double start_NInst = beam.Value.StartBeamProperty.NInstant;
                        double start_NInf = beam.Value.StartBeamProperty.NInfinite;
                        bool start_considerConcrete = beam.Value.StartBeamProperty.ConsiderConcrete;
                        bool start_considerRebar = beam.Value.StartBeamProperty.ConsiderRebar;

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
                        double end_NInst = beam.Value.EndBeamProperty.NInstant;
                        double end_NInf = beam.Value.EndBeamProperty.NInfinite;
                        bool end_considerConcrete = beam.Value.EndBeamProperty.ConsiderConcrete;
                        bool end_considerRebar = beam.Value.EndBeamProperty.ConsiderRebar;

                        MaterialModel end_concreteModel = beam.Value.EndBeamProperty.ConcreteMaterial;
                        MaterialModel end_steelModel = beam.Value.EndBeamProperty.SteelMaterial;
                        MaterialModel end_rebarModel = beam.Value.EndBeamProperty.RebarMaterial;

                        double homogenizedFactorInfinite = beam.Value.StartBeamProperty.NInfinite;
                        double homogenizedFactorInstant = beam.Value.StartBeamProperty.NInstant;

                        if (start_bfw == end_bfw && start_tfw == end_tfw && start_h == end_h && start_bft == end_bft && start_tft == end_tft && start_wt == end_wt)
                        {
                            if (start_hh == end_hh && start_b == end_b && start_topRebarsAreaTot == end_topRebarsAreaTot && start_topRebarsCover == end_topRebarsCover &&
                    start_bottomRebarsAreaTot == end_bottomRebarsAreaTot && start_bottomRebarsCover == end_bottomRebarsCover &&
                                start_verticalOffset == end_verticalOffset && start_NInst == end_NInst && start_NInf == end_NInf && beam.Value.StartBeamProperty.NShrinkage == beam.Value.EndBeamProperty.NShrinkage)
                            {
                                if (start_concreteModel == end_concreteModel && start_steelModel == end_steelModel && start_rebarModel == end_rebarModel)
                                    constantSection = true;
                            }
                        }

                        var normStationsSet = beam.Value.Results.OfType<BeamForceResultModel>()
                            .Select(result => Math.Clamp(result.NormalizedLength ? result.Station : result.Station / beam.Value.Length, 0, 1))
                            .ToHashSet();

                        List<double> stations = normStationsSet.OrderBy(value => value).ToList();

                        psis[i] = new (double psiInfinite, double psiInstant, double psiShrinkage)[stations.Count];
                        sectionCheckers[i] = new BridgeCheckAdapter[stations.Count];

                        if (constantSection)
                        {
                            try
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


                                double phiInfinite = CalculateHomogenizedFactorPhi(homogenizedFactorInfinite, steelMaterialEN1992, concreteMaterialEN1992);
                                double phiInstant = CalculateHomogenizedFactorPhi(homogenizedFactorInstant, steelMaterialEN1992, concreteMaterialEN1992);

                                ReinforcedConcreteSection reinforcedConcreteSection = BridGHe.Common.Checks.CompositeSectionFactory.Create(start_b, start_hh, concreteMaterialEN1992, rebarTop, start_b / numberRebars,
                                    start_topRebarsCover, rebarBottom, start_b / numberRebars, sectionH, steelMaterialEN1992, start_bottomRebarsCover, start_verticalOffset, beam.Value.StartBeamProperty.Name);

                                CoordinateSystem cs = GetCoordinateSystem(reinforcedConcreteSection);

                                GPC.Checkers.CompositeBridge.BridgeAnalysisOptions options = new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare,  WebBuckling = webFlangeInstab, TopFlangeBuckling = topFlangeInstab, BottomFlangeBuckling = bottomFlangeInstab };

                                for (int k = 0; k < stations.Count; k++)
                                {
                                    psis[i][k] = (phiInfinite, phiInstant, CalculateHomogenizedFactorPhi(beam.Value.StartBeamProperty.NShrinkage, steelMaterialEN1992, concreteMaterialEN1992));
                                    sectionCheckers[i][k] = new BridgeCheckAdapter(reinforcedConcreteSection, cs, options);
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new InvalidOperationException($"Sezione del superelemento {beam.Value.BeamId}: {ex.Message}", ex);
                            }
                        }
                        else
                        {
                            for (int k = 0; k < stations.Count; k++)
                            {
                                double currentStation = stations[k];
                                double width = Interpolation.GetLinearInterpolation(0, 1, start_b, end_b, currentStation);
                                double slabHeight = Interpolation.GetLinearInterpolation(0, 1, start_hh, end_hh, currentStation);
                                double cover = Interpolation.GetLinearInterpolation(0, 1, start_topRebarsCover, end_topRebarsCover, currentStation);
                                double offset = Interpolation.GetLinearInterpolation(0, 1, start_verticalOffset, end_verticalOffset, currentStation);
                                double nInfinite = Interpolation.GetLinearInterpolation(0, 1, beam.Value.StartBeamProperty.NInfinite, beam.Value.EndBeamProperty.NInfinite, currentStation);
                                double nInstant = Interpolation.GetLinearInterpolation(0, 1, beam.Value.StartBeamProperty.NInstant, beam.Value.EndBeamProperty.NInstant, currentStation);
                                double h = Interpolation.GetLinearInterpolation(0, 1.0, start_h, end_h, currentStation);
                                double wt = Interpolation.GetLinearInterpolation(0, 1.0, start_wt, end_wt, currentStation);
                                double tfw = Interpolation.GetLinearInterpolation(0, 1.0, start_tfw, end_tfw, currentStation);
                                double tft = Interpolation.GetLinearInterpolation(0, 1.0, start_tft, end_tft, currentStation);
                                double bfw = Interpolation.GetLinearInterpolation(0, 1.0, start_bfw, end_bfw, currentStation);
                                double bft = Interpolation.GetLinearInterpolation(0, 1.0, start_bft, end_bft, currentStation);

                                SectionH sectionH = new SectionH(h, wt, tfw, tft, bfw, bft, beam.Value.BeamId);

                                double fck = Interpolation.GetLinearInterpolation(0, 1.0, start_concreteModel.SpecificCompressiveStrength, end_concreteModel.SpecificCompressiveStrength, currentStation);
                                double mysS = Interpolation.GetLinearInterpolation(0, 1.0, start_steelModel.MinimumYieldStress, end_steelModel.MinimumYieldStress, currentStation);
                                double mtsS = Interpolation.GetLinearInterpolation(0, 1.0, start_steelModel.MinimumTensileStress, end_steelModel.MinimumTensileStress, currentStation);
                                double eS = Interpolation.GetLinearInterpolation(0, 1.0, start_steelModel.Modulus, end_steelModel.Modulus, currentStation);
                                double mysR = Interpolation.GetLinearInterpolation(0, 1.0, start_rebarModel.MinimumYieldStress, end_rebarModel.MinimumYieldStress, currentStation);
                                double mtsR = Interpolation.GetLinearInterpolation(0, 1.0, start_rebarModel.MinimumTensileStress, end_rebarModel.MinimumTensileStress, currentStation);
                                double eR = Interpolation.GetLinearInterpolation(0, 1.0, start_rebarModel.Modulus, end_rebarModel.Modulus, currentStation);

                                ConcreteMaterialEN1992 concreteMaterialEN1992 = new ConcreteMaterialEN1992(start_concreteModel.Name, fck,
                                    ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
                                SteelMaterialEN1992 steelMaterialEN1992 = new SteelMaterialEN1992(start_steelModel.Name, eS, mysS, mtsS,
                                    0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
                                SteelMaterialEN1992 rebarMaterial = new SteelMaterialEN1992(start_rebarModel.Name, eR, mysR, mtsR,
                                    0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);

                                double rebarsArea = Interpolation.GetLinearInterpolation(0, 1.0, start_topRebarsAreaTot, end_topRebarsAreaTot, currentStation);

                                int numberRebars = 3;
                                double rebarArea = rebarsArea / numberRebars;
                                double rebarDiameter = Math.Sqrt(rebarArea * 4 / Math.PI);

                                RebarSectionCircular rebarTop = rebarDiameter != 0 ? new RebarSectionCircular("", rebarDiameter, rebarMaterial) : null;
                                double bottomArea = Interpolation.GetLinearInterpolation(0, 1, start_bottomRebarsAreaTot, end_bottomRebarsAreaTot, currentStation);
                                double bottomCover = Interpolation.GetLinearInterpolation(0, 1, start_bottomRebarsCover, end_bottomRebarsCover, currentStation);
                                double rebarDiameterBottom = Math.Sqrt(bottomArea / numberRebars * 4 / Math.PI);
                                RebarSectionCircular rebarBottom = rebarDiameterBottom != 0 ? new RebarSectionCircular("", rebarDiameterBottom, rebarMaterial) : null;


                                double phiInfinite = CalculateHomogenizedFactorPhi(nInfinite, steelMaterialEN1992, concreteMaterialEN1992);
                                double phiInstant = CalculateHomogenizedFactorPhi(nInstant, steelMaterialEN1992, concreteMaterialEN1992);

                                ReinforcedConcreteSection reinforcedConcreteSection = BridGHe.Common.Checks.CompositeSectionFactory.Create(width, slabHeight, concreteMaterialEN1992, rebarTop, width / numberRebars,
                                    cover, rebarBottom, width / numberRebars, sectionH, steelMaterialEN1992, bottomCover, offset, beam.Value.StartBeamProperty.Name);

                                CoordinateSystem cs = GetCoordinateSystem(reinforcedConcreteSection);

                                GPC.Checkers.CompositeBridge.BridgeAnalysisOptions options = new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare,  WebBuckling = webFlangeInstab, TopFlangeBuckling = topFlangeInstab, BottomFlangeBuckling = bottomFlangeInstab };

                                double nShrinkage = Interpolation.GetLinearInterpolation(0, 1, beam.Value.StartBeamProperty.NShrinkage, beam.Value.EndBeamProperty.NShrinkage, currentStation);
                                psis[i][k] = (phiInfinite, phiInstant, CalculateHomogenizedFactorPhi(nShrinkage, steelMaterialEN1992, concreteMaterialEN1992));
                                sectionCheckers[i][k] = new BridgeCheckAdapter(reinforcedConcreteSection, cs, options);
                            }
                        }
                    }

                    for (int i = 0; i < beams.Count; i++)
                    // Parallel.For(0, beams.Count, i =>
                    {
                        GH_CompositeBeam beam = beams[i];

                        try
                        {
                            CompositeBeamModel considerBeam = beam.Value;// model.Value.Beams.FirstOrDefault(b => b.BeamId == beam.Value.BeamId);

                            CoordinateSystem cs = new CoordinateSystem(Point3d.Origin, new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0));

                            if (considerBeam != null)
                            {
                                HashSet<double> stationBuffer = new HashSet<double>();
                                for (int k = 0; k < considerBeam.Results.Count; k++)
                                {
                                    var beamForceResultModel = considerBeam.Results[k] as BeamForceResultModel;
                                    if (beamForceResultModel == null) continue;
                                    double dist = Math.Round(beamForceResultModel.NormalizedLength ? beamForceResultModel.Station * beam.Value.Length : beamForceResultModel.Station, 15);
                                    stationBuffer.Add(dist);
                                }
                                List<double> stationB = stationBuffer.ToList();
                                List<double> station = stationB.OrderBy(b => b).ToList();

                                forces[i] = new (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[station.Count];
                                forcesForOutput[i] = new (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[station.Count];
                                strains[i] = new (double[] StrainInfiniteTime, double[] StrainIstantTime)[station.Count];

                                var phaseIndex = Common.Checks.BridgePhaseAssembler.CreateIndex(considerBeam, beam.Value.Length, GeometryBase.Tolerance);
                                workflowIndexes[i] = phaseIndex;
                                workflowStations[i] = station.ToArray();
                                for (int k = 0; k < station.Count; k++)
                                {
                                    double consideredStation = station[k];

                                    ResultBeamForces[] resultBeamForcesOnlySteels = new ResultBeamForces[model.Value.LoadCombinations.Count];
                                    ResultBeamForces[] resultBeamForcesInfinites = new ResultBeamForces[model.Value.LoadCombinations.Count];
                                    ResultBeamForces[] resultBeamForcesInstants = new ResultBeamForces[model.Value.LoadCombinations.Count];
                                    ResultBeamForces[] resultBeamForcesOnlySteelsForOutput = new ResultBeamForces[model.Value.LoadCombinations.Count];
                                    ResultBeamForces[] resultBeamForcesInfinitesForOutput = new ResultBeamForces[model.Value.LoadCombinations.Count];
                                    ResultBeamForces[] resultBeamForcesInstantsForOutput = new ResultBeamForces[model.Value.LoadCombinations.Count];

                                    ResultBeamForces[] resultShrinkage = new ResultBeamForces[model.Value.LoadCombinations.Count];
                                    double[] strainNIstantArray = new double[model.Value.LoadCombinations.Count];
                                    double[] strainNInfiniteArray = new double[model.Value.LoadCombinations.Count];

                                    for (int c = 0; c < model.Value.LoadCombinations.Count; c++)
                                    {
                                        LoadCombinationModel combo = model.Value.LoadCombinations[c];

                                        var actions = phaseIndex.Assemble(consideredStation, combo, cs);
                                        resultBeamForcesOnlySteels[c] = resultBeamForcesOnlySteelsForOutput[c] = actions.Steel;
                                        resultBeamForcesInfinites[c] = resultBeamForcesInfinitesForOutput[c] = actions.Infinite;
                                        resultBeamForcesInstants[c] = resultBeamForcesInstantsForOutput[c] = actions.Instant;
                                        resultShrinkage[c] = actions.Shrinkage;
                                        strainNInfiniteArray[c] = actions.StrainInfinite;
                                        strainNIstantArray[c] = actions.StrainInstant;
                                    }

                                    forces[i][k] = (resultBeamForcesOnlySteels, resultBeamForcesInfinites, resultBeamForcesInstants, resultShrinkage);
                                    forcesForOutput[i][k] = (resultBeamForcesOnlySteelsForOutput, resultBeamForcesInfinitesForOutput, resultBeamForcesInstantsForOutput, resultShrinkage);
                                    strains[i][k] = (strainNInfiniteArray, strainNIstantArray);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            throw new InvalidOperationException($"Azioni del superelemento {beam.Value.BeamId}: {e.Message}", e);
                        }
                        //});
                    }

                    StressAnalysisResult[][][] stressAnalysisResults = new StressAnalysisResult[beams.Count][][];
                    double[][][] tauBResults = new double[beams.Count][][];

                    //for (int i = 0; i < beams.Count; i++)
                    Parallel.For(0, beams.Count, i =>
                    {
                        var beam = beams[i].Value;

                        stressAnalysisResults[i] = new StressAnalysisResult[forces[i].Length][];
                        tauBResults[i] = new double[forces[i].Length][];
                        for (int j = 0; j < forces[i].Length; j++)
                        {
                            var checker = sectionCheckers[i];
                            double psiInfinite = psis[i][j].psiInfinite;
                            double psiInstant = psis[i][j].psiInstant;
                            double[] strainIstant = strains[i][j].StrainIstantTime;
                            double[] strainInfinite = strains[i][j].StrainInfiniteTime;

                            (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage) force = forces[i][j];
                            if (model.Value.BridgeWorkflow?.Method == BridgeWorkflowMethod.LinearHistory)
                            {
                                stressAnalysisResults[i][j] = model.Value.LoadCombinations.Select(combo => checker[j].AnalyzeHistory(model.Value.BridgeWorkflow,
                                    workflowIndexes[i], workflowStations[i][j], combo, psiInfinite, psiInstant, psis[i][j].psiShrinkage, beam.StartBeamProperty.ConsiderRebar)).ToArray();
                                tauBResults[i][j] = Enumerable.Repeat(double.NaN, model.Value.LoadCombinations.Count).ToArray();
                                continue;
                            }
                            bool res = checker[j].GetLinearAnalysisResultByStages(out StressAnalysisResult[] result, out double[] tauB, psiInfinite, psiInstant, force.OnlySteel,
                                force.NInfinite, force.Ninstant, beam.StartBeamProperty.ConsiderConcrete, strainInfinite, strainIstant, beam.StartBeamProperty.ConsiderRebar, psis[i][j].psiShrinkage, force.NShrinkage);

                            if (res)
                            {
                                stressAnalysisResults[i][j] = new StressAnalysisResult[result.Length];
                                tauBResults[i][j] = new double[result.Length];
                                for (int k = 0; k < result.Length; k++)
                                {
                                    stressAnalysisResults[i][j][k] = result[k];
                                    tauBResults[i][j][k] = tauB[k];
                                }
                            }
                            else
                            {
                                stressAnalysisResults[i][j] = null;
                                tauBResults[i][j] = null;
                            }
                        }
                    });
                    //}

                    DataTree<StressAnalysisResult> resultsDataTree = new DataTree<StressAnalysisResult>();
                    for (int i = 0; i < stressAnalysisResults.Length; i++)
                    {
                        var beam = beams[i].Value;
                        string superElement = string.Join(", ", beam.Attributes.OfType<SuperElementAttributeModel>()
                            .Select(a => a.Value).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s, StringComparer.Ordinal));
                        for (int j = 0; j < stressAnalysisResults[i].Length; j++)
                            resultsDataTree.AddRange(stressAnalysisResults[i][j].Select((result, k) => result.WithSource(
                                new BridgeResultSource(beam.BeamId, workflowStations[i][j], model.Value.LoadCombinations[k].Name, superElement))), new GH_Path(i, j));
                    }

                    DataTree<double> tauBDataTree = new DataTree<double>();
                    for (int i = 0; i < tauBResults.Length; i++)
                        for (int j = 0; j < tauBResults[i].Length; j++)
                            tauBDataTree.AddRange(tauBResults[i][j], new GH_Path(i, j));

                    DataTree<ResultBeamForces> forcesTree = new DataTree<ResultBeamForces>();
                    for (int i = 0; i < forcesForOutput.Length; i++)
                        for (int j = 0; j < forcesForOutput[i].Length; j++)
                        {
                            List<ResultBeamForces> list = new List<ResultBeamForces>();
                            for (int k = 0; k < forcesForOutput[i][j].OnlySteel.Length; k++)
                            {
                                var total = forcesForOutput[i][j].OnlySteel[k] + forcesForOutput[i][j].NInfinite[k] + forcesForOutput[i][j].Ninstant[k];
                                if (forcesForOutput[i][j].NShrinkage[k] != null) total += forcesForOutput[i][j].NShrinkage[k];
                                list.Add(total);
                            }

                            forcesTree.AddRange(list, new GH_Path(i, j));
                        }

                    DA.SetDataTree(0, resultsDataTree);
                    if (model.Value.BridgeWorkflow?.Method == BridgeWorkflowMethod.LinearHistory)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "gPC LinearHistory: storico di sezione N–Mx; taglio, pioli e irrigidimenti non verificati. Strain geometrico riferito all'origine dello storico; nessuna integrazione temporale della viscosità.");
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Tau b non ha un equivalente verificato nell'API gPC attuale: output NaN.");
                    DA.SetDataTree(1, tauBDataTree);
                    DA.SetDataTree(2, forcesTree);

                    Message = "N–Mx–Vy";
                    _run = false;
                }
                catch (Exception e)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Generic error: {e.Message}");
                    Message = "Error";
                    _run = false;
                    return;
                }
            }

            _run = false;
        }

        protected double CalculateHomogenizedFactorPhi(double n, SteelMaterial steelMaterial, ConcreteMaterial concreteMaterial)
        {
            return Common.Checks.BridgeCreepCoefficient.FromModularRatio(n, steelMaterial.ElasticModulusTension, concreteMaterial.ElasticModulusCompression);
        }

        protected CoordinateSystem GetCoordinateSystem(ReinforcedConcreteSection reinforcedConcreteSection)
        {
            return new CoordinateSystem(reinforcedConcreteSection.Centroid, new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0));
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("94839122-32f0-4f3e-a30f-979a53c3eca8");
    }
}
