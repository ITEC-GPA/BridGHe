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
            GpcReleaseGuard.Warn(this);
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
                    var definitions = new GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition[beams.Count][];
                    var analysisOptions = new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard = GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState = GPC.Checkers.CompositeBridge.BridgeLimitState.Rare, WebBuckling = webFlangeInstab, TopFlangeBuckling = topFlangeInstab, BottomFlangeBuckling = bottomFlangeInstab };
                    (double psiInfinite, double psiInstant, double psiShrinkage)[][] psis = new (double psiInfinite, double psiInstant, double psiShrinkage)[beams.Count][];

                    // F6 C5: with the GPC bridge model of Build Bridge Model the sections are checked by the GPC workflow executor (D3, one prepared plan
                    // per recipe) on the GPC results; BridGHe's own phases otherwise. F6 (a)/(b), R3: on the path by cases a composite property with
                    // "Consider Concrete" off excludes the concrete of its beam in the long-term, short-term and shrinkage increments (a copy of the D3
                    // recipe with ConcreteExcludedBeamGuids, as BridGHe's phases with concrete off). R1/R2/R4: on the D3 path the stations and the forces output come from the
                    // merged samples of the GPC bridge model (the numbers of the former result objects), never from the FEM results of the beams; a beam
                    // that is not in the GPC bridge model is an error, not a fallback.
                    var gpcBridge = model.GpcBridge;
                    bool workflowPath = model.Value.BridgeWorkflow?.Method == BridgeWorkflowMethod.LinearHistory ||
                        model.Value.BridgeWorkflow?.Phases.Any(p => p.Actions.Any(a => a.HasLocalSectionStates)) == true;
                    Common.Checks.GpcBridgeChecks d3 = null;
                    if (gpcBridge != null && model.Value.BridgeWorkflow != null)
                    {
                        var excluded = workflowPath ? null : beams.Where(b => !b.Value.StartBeamProperty.ConsiderConcrete).Select(b => b.Value.BeamId).ToArray();
                        d3 = new Common.Checks.GpcBridgeChecks(gpcBridge, excluded); // P2T request 3: the GPC bridge model alone
                        var missing = beams.Where(b => !d3.Has(b.Value.BeamId) || gpcBridge.SamplesOf(b.Value.BeamId) == null).Select(b => b.Value.BeamId).ToArray();
                        if (missing.Length > 0) throw new InvalidOperationException("Travi assenti dal modello di ponte GPC: " + string.Join(", ", missing));
                    }
                    if (gpcBridge != null && d3 == null) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Verifiche con le fasi BridGHe: modello di ponte GPC senza ricetta.");
                    IReadOnlyList<(string Case, BeamForceSample Sample)> GpcSamples(GH_CompositeBeam b) => d3 != null ? gpcBridge.SamplesOf(b.Value.BeamId) : null;

                    for (int i = 0; i < beams.Count; i++)
                    {
                        GH_CompositeBeam beam = beams[i];

                        bool constantSection = Common.Gpc.GpcCompositeSections.IsConstant(beam.Value.StartBeamProperty, beam.Value.EndBeamProperty);

                        var samples = GpcSamples(beam);
                        var normStationsSet = (samples != null ? samples.Select(s => Math.Clamp(s.Sample.Station / beam.Value.Length, 0, 1))
                            : beam.Value.Results.OfType<BeamForceResultModel>()
                            .Select(result => Math.Clamp(result.NormalizedLength ? result.Station : result.Station / beam.Value.Length, 0, 1)))
                            .ToHashSet();

                        List<double> stations = normStationsSet.OrderBy(value => value).ToList();

                        psis[i] = new (double psiInfinite, double psiInstant, double psiShrinkage)[stations.Count];
                        sectionCheckers[i] = new BridgeCheckAdapter[stations.Count];
                        definitions[i] = new GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition[stations.Count];
                        // F6 (L11): the section of the checks as the GPC contract of the Checker (BridgeCompositeSectionDefinition, built with the
                        // rules of this component by GpcCompositeSections); the D3 path takes the input of the H bridge engine from it
                        // (SectionInput), BridGHe's phases (fallback) an adapter on the same section.
                        try
                        {
                            var constant = constantSection ? Common.Gpc.GpcCompositeSections.Definition(beam.Value.StartBeamProperty, beam.Value.BeamId) : null;
                            for (int k = 0; k < stations.Count; k++)
                            {
                                var definition = constant ?? Common.Gpc.GpcCompositeSections.Definition(beam.Value.StartBeamProperty, beam.Value.EndBeamProperty, stations[k], beam.Value.BeamId);
                                var section = definition.Section;
                                var steel = section.SteelSections[0].Section.SteelMaterial; var concrete = section.ConcreteMaterial;
                                psis[i][k] = (CalculateHomogenizedFactorPhi(definition.NLongTerm, steel, concrete), CalculateHomogenizedFactorPhi(definition.NShortTerm, steel, concrete),
                                    CalculateHomogenizedFactorPhi(definition.NShrinkage, steel, concrete));
                                definitions[i][k] = definition;
                                sectionCheckers[i][k] = new BridgeCheckAdapter(section, GetCoordinateSystem(section), analysisOptions);
                            }
                        }
                        catch (Exception ex) when (constantSection)
                        {
                            throw new InvalidOperationException($"Sezione del superelemento {beam.Value.BeamId}: {ex.Message}", ex);
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
                                var samples = GpcSamples(beam);
                                if (samples != null)
                                    foreach (var sample in samples) stationBuffer.Add(Math.Round(sample.Sample.Station, 15));
                                else
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

                                var phaseIndex = samples != null ? Common.Checks.BridgePhaseAssembler.CreateIndex(beam.Value.BeamId, samples, GeometryBase.Tolerance)
                                    : Common.Checks.BridgePhaseAssembler.CreateIndex(considerBeam, beam.Value.Length, GeometryBase.Tolerance);
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

                    for (int i = 0; i < beams.Count; i++)
                    {
                        stressAnalysisResults[i] = new StressAnalysisResult[forces[i].Length][];
                        tauBResults[i] = new double[forces[i].Length][];
                    }
                    // One task per station (before, per beam): its combinations are one gPC batch, which calculates their shared phases once.
                    // Every station writes only its own slots, so the outputs keep their order.
                    var stationTasks = Enumerable.Range(0, beams.Count).SelectMany(i => Enumerable.Range(0, forces[i].Length).Select(j => (Beam: i, Station: j))).ToArray();
                    Parallel.For(0, stationTasks.Length, n =>
                    {
                        int i = stationTasks[n].Beam, j = stationTasks[n].Station;
                        var beam = beams[i].Value;
                        {
                            var checker = sectionCheckers[i];
                            double psiInfinite = psis[i][j].psiInfinite;
                            double psiInstant = psis[i][j].psiInstant;
                            double[] strainIstant = strains[i][j].StrainIstantTime;
                            double[] strainInfinite = strains[i][j].StrainInfiniteTime;

                            (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage) force = forces[i][j];
                            if (d3 != null)
                            {
                                stressAnalysisResults[i][j] = d3.Calculate(beam.BeamId, workflowStations[i][j], definitions[i][j].SectionInput(beam.StartBeamProperty.ConsiderRebar, analysisOptions),
                                    psiInfinite, psiInstant, psis[i][j].psiShrinkage);
                                tauBResults[i][j] = Enumerable.Repeat(double.NaN, model.Value.LoadCombinations.Count).ToArray();
                                return;
                            }
                            if (model.Value.BridgeWorkflow?.Method == BridgeWorkflowMethod.LinearHistory ||
                                model.Value.BridgeWorkflow?.Phases.Any(p => p.Actions.Any(a => a.HasLocalSectionStates)) == true)
                            {
                                stressAnalysisResults[i][j] = checker[j].AnalyzeWorkflow(model.Value.BridgeWorkflow,
                                    workflowIndexes[i], workflowStations[i][j], model.Value.LoadCombinations, psiInfinite, psiInstant, psis[i][j].psiShrinkage, beam.StartBeamProperty.ConsiderRebar);
                                tauBResults[i][j] = Enumerable.Repeat(double.NaN, model.Value.LoadCombinations.Count).ToArray();
                                return;
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
