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

        /// <summary>R6.4 (5), decision (i) = A of the user (7/11): the error without the GPC bridge model of Build BridGHe Model.</summary>
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
            // Until R6.5 the test host compares the GPC readings with those of the merged FEM model on every solution (GpcDraftCheck).
            if (model.Value != null) Common.Gpc.GpcDraftCheck.Attached("Super Element Check super elements", string.Join("\n", superElements), () => string.Join("\n", FemSuperElements(model.Value)));
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

            if (_run)
            {
                try
                {
                    var records = gpcBridge.Records;
                    var combinations = gpcBridge.Combinations;
                    var casePhases = Common.Checks.BridgePhaseAssembler.CasePhases(records);
                    if (model.Value != null) Common.Gpc.GpcDraftCheck.Attached("Super Element Check combinations", CombinationsText(combinations, casePhases), () => FemCombinationsText(model.Value));
                    var workflowStations = new double[beams.Count][];
                    (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[][] forcesForOutput = new
                        (ResultBeamForces[] OnlySteel, ResultBeamForces[] NInfinite, ResultBeamForces[] Ninstant, ResultBeamForces[] NShrinkage)[beams.Count][];

                    var definitions = new GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition[beams.Count][];
                    var analysisOptions = new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard = GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState = GPC.Checkers.CompositeBridge.BridgeLimitState.Rare, WebBuckling = webFlangeInstab, TopFlangeBuckling = topFlangeInstab, BottomFlangeBuckling = bottomFlangeInstab };
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
                    Common.Gpc.GpcDraftCheck.Attached("Super Element Check beams", BeamsText(ids, lengths, beamSuperElements), () => BeamsText(beams.Select(b => b.Value?.BeamId).ToArray(),
                        beams.Select(b => b.Value?.Length ?? double.NaN).ToArray(), beams.Select(b => FemSuperElement(b.Value)).ToArray()));

                    for (int i = 0; i < beams.Count; i++)
                    {
                        GH_CompositeBeam beam = beams[i];

                        var samples = gpcBridge.SamplesOf(ids[i]);
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
                            List<double> station = stationBuffer.OrderBy(b => b).ToList();

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
                    Parallel.For(0, stationTasks.Length, n =>
                    {
                        int i = stationTasks[n].Beam, j = stationTasks[n].Station;
                        var startSection = Start(beams[i]); // the choices (Consider Concrete / Rebar) of the start, as before
                        stressAnalysisResults[i][j] = d3.Calculate(ids[i], workflowStations[i][j], definitions[i][j].SectionInput(startSection.ConsiderRebar, analysisOptions),
                            psis[i][j].psiInfinite, psis[i][j].psiInstant, psis[i][j].psiShrinkage);
                        // "Tau b" has no verified equivalent in the GPC API: NaN for every combination, as on the D3 path before.
                        tauBResults[i][j] = Enumerable.Repeat(double.NaN, combinations.Count).ToArray();
                    });

                    DataTree<StressAnalysisResult> resultsDataTree = new DataTree<StressAnalysisResult>();
                    for (int i = 0; i < stressAnalysisResults.Length; i++)
                    {
                        for (int j = 0; j < stressAnalysisResults[i].Length; j++)
                            resultsDataTree.AddRange(stressAnalysisResults[i][j].Select((result, k) => result.WithSource(
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

        /// <summary>R6.4 (3): the BridGHe ID of a composite beam: the original ID of the GPC beam it is linked to (Composite Beam from Super Element
        /// Filter or Decompose Model, Bridge Segment Composite), else (a composite beam made without a GPC beam) that of its value, until R6.5.</summary>
        private static string BeamId(GH_CompositeBeam beam) => beam?.AnalysedGpc is GPC.Model.Elements.BeamElement b ? b.Source.OriginalId : beam?.Value?.BeamId;

        /// <summary>The length of a beam of the GPC bridge model: the distance of Rhino between its ends (N-mm), as the FEM length.</summary>
        private static double Length(GPC.Model.Elements.BeamElement beam) =>
            new Rhino.Geometry.Point3d(beam.StartPoint.X, beam.StartPoint.Y, beam.StartPoint.Z).DistanceTo(new Rhino.Geometry.Point3d(beam.EndPoint.X, beam.EndPoint.Y, beam.EndPoint.Z));

        /// <summary>R6.4 (3): the super elements of the GPC bridge model, those of its beams (bridge data), distinct, in ordinal order.</summary>
        private static string[] SuperElements(Common.Gpc.GpcBridge bridge) => bridge.Model.BeamElements.Values.Select(b => bridge.Data.SuperElement(b.Guid))
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();

        // Until R6.5: the FEM readings the test host compares the GPC ones with (the merged FEM model of Build BridGHe Model, the composite beams).
        private static string[] FemSuperElements(ModelModel model) => Common.Helpers.ModelHelper.GetSuperElements(model).Select(a => a.Value)
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        private static string FemSuperElement(CompositeBeamModel beam) => beam == null ? "" : string.Join(", ", beam.Attributes.OfType<SuperElementAttributeModel>()
            .Select(a => a.Value).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s, StringComparer.Ordinal));
        private static string BeamsText(string[] ids, double[] lengths, string[] superElements) =>
            string.Join("\n", ids.Select((id, i) => id + " " + BitConverter.DoubleToInt64Bits(lengths[i]).ToString("X16") + " " + superElements[i]));
        private static string CombinationsText(IReadOnlyList<(string Name, IReadOnlyList<(string Case, double Factor)> Factors)> combinations, IReadOnlyDictionary<string, BridgeCasePhase> phases) =>
            string.Join("\n", combinations.Select(c => c.Name + ": " + string.Join("; ", c.Factors.Select(f => f.Case + " " + BitConverter.DoubleToInt64Bits(f.Factor).ToString("X16") + " " +
                (phases.TryGetValue(f.Case, out var p) ? p.ToString() : "-")))));
        private static string FemCombinationsText(ModelModel model) =>
            string.Join("\n", model.LoadCombinations.Select(c => c.Name + ": " + string.Join("; ", c.Values.Select(v => v.Key.Name + " " + BitConverter.DoubleToInt64Bits(v.Value).ToString("X16") + " " +
                (v.Key.Stages?.Count == 1 ? v.Key.Stages[0].BridgePhase.ToString() : "-")))));

        /// <summary>Chain E (c): the composite section of the start (end) of the beam as the definition it carries; a definition that cannot be
        /// built is the error of the section of the super element, as before.</summary>
        private static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Start(GH_CompositeBeam beam) => Section(beam, true);
        private static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition End(GH_CompositeBeam beam) => Section(beam, false);
        private static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Section(GH_CompositeBeam beam, bool start)
        {
            try { return (start ? beam.StartDefinition : beam.EndDefinition) ?? throw new InvalidOperationException("composite beam without its composite sections"); }
            catch (Exception ex) { throw new InvalidOperationException($"Sezione del superelemento {beam.Value?.BeamId}: {ex.Message}", ex); }
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
