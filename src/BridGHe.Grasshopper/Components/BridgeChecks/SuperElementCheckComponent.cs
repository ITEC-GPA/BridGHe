using BridGHe.Common.Checks;
using StressAnalysisResult = BridGHe.Common.Checks.BridgeStressResult;
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
            // User decision of 20/11/2026: the optional inputs of the check (the three local instability toggles, Bridge Analysis Options, ULS
            // Combinations) are in Super Element Check Options, with the number of analysis stations per element.
            pManager.AddGenericParameter("Options", "O", "Optional Super Element Check Options (local instabilities, Bridge Analysis Options, ULS Combinations, analysis stations per element); Bridge Analysis Options alone are accepted too. Without options: the three local instabilities on, Eurocode 4 rare SLS, no ULS combination, the 2 nodes of every element.", GH_ParamAccess.item);
            pManager[2].Optional = true;
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

        /// <summary>R6.4 (5), decision (i) = A of the user (7/11): the error without the GPC bridge model of Build BridGHe Model.</summary>
        /// <summary>0.6.0: what the Options input accepts, and where the former optional inputs went.</summary>
        public const string LegacyOptions = "Options richiede Super Element Check Options (oppure Bridge Analysis Options). Dalla 0.6.0 gli ingressi WLI, TFLI, BLI, Options e ULS Combinations sono in Super Element Check Options: in una definizione precedente collegarli lì.";

        public const string RequiresBridgeModel = "Super Element Check richiede il modello di ponte GPC di Build BridGHe Model (con la sua ricetta): collegare l'ingresso BridGHe Model all'uscita Model di Build BridGHe Model e premere Run.";

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

            int n = 0;
            if (!DA.GetData(n++, ref model))
                return;
            if (!DA.GetDataList(n++, beams))
                return;
            // Read as any goo, not as BridgeDetailGoo: a definition saved before 0.6.0 brings the old toggle WLI to this input, and a failed cast
            // must not fall back to the defaults in silence.
            global::Grasshopper.Kernel.Types.IGH_Goo optionsInput = null;
            var settings = Common.Checks.SuperElementCheckSettings.Default;
            if (DA.GetData(n++, ref optionsInput) && optionsInput != null)
            {
                settings = (optionsInput as BridgeDetailGoo)?.Value switch
                {
                    Common.Checks.SuperElementCheckSettings s => s,
                    GPC.Checkers.CompositeBridge.BridgeAnalysisOptions o => settings with { AnalysisOptions = o },
                    _ => null
                };
                if (settings == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, LegacyOptions);
                    Message = "Error";
                    _run = false;
                    return;
                }
            }
            if (settings.StationsPerElement < 0 || settings.StationsPerElement == 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Stazioni di analisi per elemento: 0 (tutte) oppure almeno 2.");
                Message = "Error";
                _run = false;
                return;
            }
            var suppliedOptions = settings.AnalysisOptions;
            var ulsEntries = (settings.UlsCombinations ?? Array.Empty<string>()).ToList();

            // R6.4 (5), decision (i) = A of the user (7/11): the checks run only on the GPC bridge model of Build BridGHe Model, with its recipe, by
            // the GPC workflow executor (D3); without it a clear error and no outputs (BridGHe's own phases on the FEM results of the composite beams
            // are gone). R6.4 (3): units, super elements, combinations, beam IDs and lengths from the GPC data: the GPC bridge model is N-mm by
            // construction (Build BridGHe Model refuses other units), its super elements are those of its beams (a super element takes only beams).
            var gpcBridge = model?.GpcBridge;
            if (gpcBridge == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, RequiresBridgeModel);
                Message = "Error";
                return;
            }
            var superElements = SuperElements(gpcBridge);
            if (superElements.Length == 0)
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
            // Study item 16: the combinations checked at the ultimate limit state, validated before Run so that a wrong name shows at once.
            bool[] uls;
            try { uls = BridgeCheckSelection.Match(gpcBridge.Combinations.Select(c => c.Name).ToArray(), ulsEntries); }
            catch (ArgumentException e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                Message = "Error";
                _run = false;
                return;
            }
            if (suppliedOptions != null)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Options collegate: valgono le loro scelte (norma, stato limite, Class4, coefficienti e instabilità locale); WLI, TFLI e BLI non sono usati.");

            if (_run)
            {
                try
                {
                    var records = gpcBridge.Records;
                    var combinations = gpcBridge.Combinations;
                    var casePhases = Common.Checks.BridgePhaseAssembler.CasePhases(records);
                    var workflowStations = new double[beams.Count][];
                    (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[][] forcesForOutput = new
                        (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[beams.Count][];

                    var definitions = new GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition[beams.Count][];
                    var analysisOptions = settings.Options();
                    // Study item 16: one gPC batch per limit state at every station (the combinations of the options, then those at the ULS); without
                    // ULS combinations a single batch of all combinations, as before.
                    var ulsOptions = analysisOptions with { LimitState = GPC.Checkers.CompositeBridge.BridgeLimitState.Ultimate };
                    var groups = new List<(GPC.Checkers.CompositeBridge.BridgeAnalysisOptions Options, int[] Combinations)>();
                    var otherCombinations = Enumerable.Range(0, combinations.Count).Where(c => !uls[c]).ToArray();
                    var ulsCombinations = Enumerable.Range(0, combinations.Count).Where(c => uls[c]).ToArray();
                    if (otherCombinations.Length > 0) groups.Add((analysisOptions, otherCombinations));
                    if (ulsCombinations.Length > 0) groups.Add((ulsOptions, ulsCombinations));
                    (double psiInfinite, double psiInstant, double psiShrinkage)[][] psis = new (double psiInfinite, double psiInstant, double psiShrinkage)[beams.Count][];

                    // F6 C5: the sections are checked by the GPC workflow executor (D3, one prepared plan per recipe) on the GPC results. F6 (a)/(b), R3:
                    // on the path by cases a composite property with "Consider Concrete" off excludes the concrete of its beam in the long-term,
                    // short-term and shrinkage increments (a copy of the D3 recipe with ConcreteExcludedBeamGuids, as BridGHe's phases with concrete
                    // off did). R1/R2/R4: the stations and the forces output come from the merged samples of the GPC bridge model (the numbers of the
                    // former result objects); a beam that is not in the GPC bridge model is an error.
                    bool workflowPath = records.Method == BridgeWorkflowMethod.LinearHistory || records.Phases.Any(p => p.Actions.Any(a => a.HasLocalSectionStates));
                    var ids = beams.Select(BeamId).ToArray();
                    var excluded = workflowPath ? null : Enumerable.Range(0, beams.Count).Where(i => !Start(beams[i]).ConsiderConcrete).Select(i => ids[i]).ToArray();
                    var d3 = new Common.Checks.GpcBridgeChecks(gpcBridge, excluded); // P2T request 3: the GPC bridge model alone
                    var missing = ids.Where(id => id == null || !d3.Has(id) || gpcBridge.SamplesOf(id) == null).Select(id => id ?? "(senza ID)").ToArray();
                    if (missing.Length > 0) throw new InvalidOperationException("Travi assenti dal modello di ponte GPC: " + string.Join(", ", missing));
                    // The length (the distance of Rhino between the ends, as the FEM length of the composite beam) and the super element of every beam
                    // of the bridge model.
                    var lengths = ids.Select(id => Length(gpcBridge.Beam(id))).ToArray();
                    var beamSuperElements = ids.Select(id => gpcBridge.Data.SuperElement(gpcBridge.Beam(id).Guid) ?? "").ToArray();
                    // User decision of 20/11/2026: the analysis stations of every element, a subset of the FEM output stations (by default the
                    // 2 nodes); the same subset for the sections and for the actions below. 0 keeps all of them, as before.
                    var kept = new HashSet<double>[beams.Count];
                    for (int i = 0; i < beams.Count; i++)
                    {
                        var available = gpcBridge.SamplesOf(ids[i]).Select(s => Math.Round(s.Sample.Station, 15)).Distinct().OrderBy(s => s).ToArray();
                        kept[i] = Common.Checks.BridgeCheckStations.Select(available, lengths[i], settings.StationsPerElement).ToHashSet();
                    }

                    for (int i = 0; i < beams.Count; i++)
                    {
                        GH_CompositeBeam beam = beams[i];

                        var samples = gpcBridge.SamplesOf(ids[i]).Where(s => kept[i].Contains(Math.Round(s.Sample.Station, 15))).ToArray();
                        var normStationsSet = samples.Select(s => Math.Clamp(s.Sample.Station / lengths[i], 0, 1)).ToHashSet();

                        List<double> stations = normStationsSet.OrderBy(value => value).ToList();

                        psis[i] = new (double psiInfinite, double psiInstant, double psiShrinkage)[stations.Count];
                        definitions[i] = new GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition[stations.Count];
                        // F6 (L11): the section of the checks as the GPC contract of the Checker (BridgeCompositeSectionDefinition, built with the
                        // rules of this component by GpcCompositeSections); the D3 path takes the input of the H bridge engine from it
                        // (SectionInput). Chain E (c): from the definitions the composite beam carries (start and end): a constant section is the
                        // start definition itself (steel H named by the composite property; the H name is not in the input of the engine nor in the
                        // results), a variable one interpolated from the two definitions at each station.
                        bool constantSection = false;
                        try
                        {
                            var start = Start(beam); var end = End(beam);
                            constantSection = Common.Gpc.GpcCompositeSections.IsConstant(start, end);
                            var constant = constantSection ? start : null;
                            for (int k = 0; k < stations.Count; k++)
                            {
                                var definition = constant ?? Common.Gpc.GpcCompositeSections.Definition(start, end, stations[k]);
                                var section = definition.Section;
                                var steel = section.SteelSections[0].Section.SteelMaterial; var concrete = section.ConcreteMaterial;
                                psis[i][k] = (CalculateHomogenizedFactorPhi(definition.NLongTerm, steel, concrete), CalculateHomogenizedFactorPhi(definition.NShortTerm, steel, concrete),
                                    CalculateHomogenizedFactorPhi(definition.NShrinkage, steel, concrete));
                                definitions[i][k] = definition;
                            }
                        }
                        catch (Exception ex) when (constantSection)
                        {
                            throw new InvalidOperationException($"Sezione del superelemento {ids[i]}: {ex.Message}", ex);
                        }

                    }

                    for (int i = 0; i < beams.Count; i++)
                    {
                        try
                        {
                            CoordinateSystem cs = new CoordinateSystem(Point3d.Origin, new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0));

                            HashSet<double> stationBuffer = new HashSet<double>();
                            var samples = gpcBridge.SamplesOf(ids[i]);
                            foreach (var sample in samples) stationBuffer.Add(Math.Round(sample.Sample.Station, 15));
                            List<double> station = stationBuffer.Where(kept[i].Contains).OrderBy(b => b).ToList();

                            forcesForOutput[i] = new (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[station.Count];

                            var phaseIndex = Common.Checks.BridgePhaseAssembler.CreateIndex(ids[i], samples, GeometryBase.Tolerance);
                            workflowStations[i] = station.ToArray();
                            for (int k = 0; k < station.Count; k++)
                            {
                                double consideredStation = station[k];

                                ResultBeamForces[] resultBeamForcesOnlySteelsForOutput = new ResultBeamForces[combinations.Count];
                                ResultBeamForces[] resultBeamForcesInfinitesForOutput = new ResultBeamForces[combinations.Count];
                                ResultBeamForces[] resultBeamForcesInstantsForOutput = new ResultBeamForces[combinations.Count];
                                ResultBeamForces[] resultShrinkage = new ResultBeamForces[combinations.Count];

                                for (int c = 0; c < combinations.Count; c++)
                                {
                                    var actions = phaseIndex.Assemble(consideredStation, combinations[c].Factors, casePhases, cs);
                                    resultBeamForcesOnlySteelsForOutput[c] = actions.Steel;
                                    resultBeamForcesInfinitesForOutput[c] = actions.Infinite;
                                    resultBeamForcesInstantsForOutput[c] = actions.Instant;
                                    resultShrinkage[c] = actions.Shrinkage;
                                }

                                forcesForOutput[i][k] = (resultBeamForcesOnlySteelsForOutput, resultBeamForcesInfinitesForOutput, resultBeamForcesInstantsForOutput, resultShrinkage);
                            }
                        }
                        catch (Exception e)
                        {
                            throw new InvalidOperationException($"Azioni del superelemento {ids[i]}: {e.Message}", e);
                        }
                    }

                    StressAnalysisResult[][][] stressAnalysisResults = new StressAnalysisResult[beams.Count][][];
                    double[][][] tauBResults = new double[beams.Count][][];

                    for (int i = 0; i < beams.Count; i++)
                    {
                        stressAnalysisResults[i] = new StressAnalysisResult[forcesForOutput[i].Length][];
                        tauBResults[i] = new double[forcesForOutput[i].Length][];
                    }
                    // One task per station (before, per beam): its combinations are one gPC batch, which calculates their shared phases once.
                    // Every station writes only its own slots, so the outputs keep their order.
                    var stationTasks = Enumerable.Range(0, beams.Count).SelectMany(i => Enumerable.Range(0, forcesForOutput[i].Length).Select(j => (Beam: i, Station: j))).ToArray();
                    // Study item 14 (Brecciarola, 19/11/2026): an error of one combination (e.g. an effective section that does not converge where
                    // M ≈ 0) no longer stops the component. Every station is calculated, the failed combinations leave an empty item in their place
                    // (the tree keeps the order of the combinations) and the error lists all of them, in order.
                    var failures = new System.Collections.Concurrent.ConcurrentBag<BridgeCheckSelection.Failure>();
                    Parallel.For(0, stationTasks.Length, n =>
                    {
                        int i = stationTasks[n].Beam, j = stationTasks[n].Station;
                        var startSection = Start(beams[i]); // the choices (Consider Concrete / Rebar) of the start, as before
                        var station = new StressAnalysisResult[combinations.Count];
                        foreach (var group in groups)
                        {
                            (StressAnalysisResult Result, Exception Error)[] each;
                            try
                            {
                                each = d3.CalculateEach(ids[i], workflowStations[i][j], definitions[i][j].SectionInput(startSection.ConsiderRebar, group.Options),
                                    psis[i][j].psiInfinite, psis[i][j].psiInstant, psis[i][j].psiShrinkage, group.Combinations);
                            }
                            catch (Exception e) { each = group.Combinations.Select(_ => ((StressAnalysisResult)null, e)).ToArray(); }
                            for (int k = 0; k < group.Combinations.Length; k++)
                            {
                                int c = group.Combinations[k];
                                if (each[k].Error != null) failures.Add(new BridgeCheckSelection.Failure(i, ids[i], workflowStations[i][j], c, combinations[c].Name, Text(each[k].Error)));
                                else station[c] = each[k].Result;
                            }
                        }
                        stressAnalysisResults[i][j] = station;
                        // "Tau b" has no verified equivalent in the GPC API: NaN for every combination, as on the D3 path before.
                        tauBResults[i][j] = Enumerable.Repeat(double.NaN, combinations.Count).ToArray();
                    });

                    DataTree<StressAnalysisResult> resultsDataTree = new DataTree<StressAnalysisResult>();
                    for (int i = 0; i < stressAnalysisResults.Length; i++)
                    {
                        for (int j = 0; j < stressAnalysisResults[i].Length; j++)
                            resultsDataTree.AddRange(stressAnalysisResults[i][j].Select((result, k) => result?.WithSource(
                                new BridgeResultSource(ids[i], workflowStations[i][j], combinations[k].Name, beamSuperElements[i]))), new GH_Path(i, j));
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
                    if (records.Method == BridgeWorkflowMethod.LinearHistory)
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "gPC LinearHistory: section history in N–Mx only; shear, studs and stiffeners are not checked. The geometric strain refers to the origin of the history; creep is not integrated in time.");
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Tau b has no verified counterpart in the current gPC API: the output is NaN.");
                    DA.SetDataTree(1, tauBDataTree);
                    DA.SetDataTree(2, forcesTree);

                    Message = "N–Mx–Vy";
                    if (ulsCombinations.Length > 0 && analysisOptions.LimitState != GPC.Checkers.CompositeBridge.BridgeLimitState.Ultimate)
                        Message += $" · SLU {ulsCombinations.Length}/{combinations.Count}";
                    if (!failures.IsEmpty)
                    {
                        int total = stationTasks.Length * combinations.Count;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, BridgeCheckSelection.Message(failures, total));
                        Message += $" · {failures.Count} non calcolate";
                    }
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

        /// <summary>R6.4 (3): the BridGHe ID of a composite beam: the original ID of the GPC beam it is linked to (Composite Beam from Super Element
        /// Filter or Decompose Model, Bridge Segment Composite), else (a composite beam made without a GPC beam) that of its GPC datum
        /// (<see cref="GH_CompositeBeam.BeamId"/>).</summary>
        private static string BeamId(GH_CompositeBeam beam) => beam?.AnalysedGpc is GPC.Model.Elements.BeamElement b ? b.Source.OriginalId : beam?.BeamId;

        /// <summary>The message of a failed calculation, without the wrapper of a single aggregated error.</summary>
        private static string Text(Exception e) => e is AggregateException a && a.InnerExceptions.Count == 1 ? Text(a.InnerExceptions[0]) : e.Message;

        /// <summary>The length of a beam of the GPC bridge model: the distance of Rhino between its ends (N-mm), as the FEM length.</summary>
        private static double Length(GPC.Model.Elements.BeamElement beam) =>
            new Rhino.Geometry.Point3d(beam.StartPoint.X, beam.StartPoint.Y, beam.StartPoint.Z).DistanceTo(new Rhino.Geometry.Point3d(beam.EndPoint.X, beam.EndPoint.Y, beam.EndPoint.Z));

        /// <summary>R6.4 (3): the super elements of the GPC bridge model, those of its beams (bridge data), distinct, in ordinal order.</summary>
        private static string[] SuperElements(Common.Gpc.GpcBridge bridge) => bridge.Model.BeamElements.Values.Select(b => bridge.Data.SuperElement(b.Guid))
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        /// <summary>Chain E (c): the composite section of the start (end) of the beam as the definition it carries; a definition that cannot be
        /// built is the error of the section of the super element, as before.</summary>
        private static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Start(GH_CompositeBeam beam) => Section(beam, true);
        private static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition End(GH_CompositeBeam beam) => Section(beam, false);
        private static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Section(GH_CompositeBeam beam, bool start)
        {
            try { return (start ? beam.StartDefinition : beam.EndDefinition) ?? throw new InvalidOperationException("composite beam without its composite sections"); }
            catch (Exception ex) { throw new InvalidOperationException($"Sezione del superelemento {beam?.BeamId}: {ex.Message}", ex); }
        }

        protected double CalculateHomogenizedFactorPhi(double n, SteelMaterial steelMaterial, ConcreteMaterial concreteMaterial)
        {
            return Common.Checks.BridgeCreepCoefficient.FromModularRatio(n, steelMaterial.ElasticModulusTension, concreteMaterial.ElasticModulusCompression);
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
