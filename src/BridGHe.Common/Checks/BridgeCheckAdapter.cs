using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Checkers.CompositeBridge;
using GPC.Checkers.CompositeBridge.History;
using BridGHe.Common.Models;
using GPC.Geometry;
using GPC.Model.Materials;
using GPC.Model.Results;
using GPC.Model.Sections;
using GPC.Model.Sections.Concrete;

namespace BridGHe.Common.Checks
{
    /// <summary>Adapter from Straus section forces to the gPC bridge engine.
    /// Geometry is in mm; actions are N and Nmm in the supplied reference system.</summary>
    public sealed class BridgeCheckAdapter
    {
        private readonly ReinforcedConcreteSection section;
        private readonly CoordinateSystem reference;
        private readonly BridgeAnalysisOptions options;
        public const string PlanarAssumption = "Verifica nel piano N–Mx–Vy: torsione T, flessione sull'asse debole M2 e taglio trasversale V1 sono trascurati nelle verifiche di sezione e dei dettagli; rimangono nei risultati FEM. I relativi effetti e interazioni non sono verificati.";
        public const string LocalStateAssumption = "Stati di rigidezza locali assegnati dal segno del momento campionato nel caso di riferimento: ipotesi preliminare, non delimitazione normativa della fessurazione. Ogni incremento meccanico usa il proprio stato e baricentro lordo. Il ritiro resta non fessurato se il suo modello non contiene override di stato. Le aste a segno variabile sono approssimate uniformemente secondo la politica scelta; verificare la sensibilità alla mesh.";
        private readonly bool ignoreSecondaryActions;

        public BridgeCheckAdapter(ReinforcedConcreteSection section, CoordinateSystem reference,
            BridgeAnalysisOptions options = null, bool ignoreSecondaryActions = true)
        {
            this.section = section ?? throw new ArgumentNullException(nameof(section));
            this.reference = reference ?? throw new ArgumentNullException(nameof(reference));
            this.options = options ?? new BridgeAnalysisOptions
            { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare };
            this.ignoreSecondaryActions = ignoreSecondaryActions;
        }

        private BridgeStressResult Result(HBridgeAnalysisResult analysis, int stage) => new(analysis, stage, null,
            ignoreSecondaryActions ? new[] { PlanarAssumption } : Array.Empty<string>());

        private HBridgeInput Input(bool concrete, bool rebars, bool buckling)
        {
            if (section.SteelSections.Count != 1 || !(section.SteelSections[0].Section.SectionShape is SectionH h))
                throw new NotSupportedException("L'adattatore BridgeChecks supporta una singola sezione ad H con soletta rettangolare.");
            var positioned = section.SteelSections[0];
            if (Math.Abs(positioned.Rotation) > 1e-10 || Math.Abs(section.Jxy) > 1e-10 * (section.Jxx + section.Jyy))
                throw new NotSupportedException("Sezione ruotata o non simmetrica: adattamento del verificatore da completare.");
            var rows = section.Rebars.GroupBy(b => Math.Round(b.Position.Y, 8)).OrderByDescending(g => g.Key).ToArray();
            if (rows.Length > 2) throw new NotSupportedException("Sono supportate al massimo due file di armatura.");
            BridgeRebarRow Row(int index)
            {
                if (!rebars || index >= rows.Length) return new BridgeRebarRow();
                var bars = rows[index].ToArray();
                if (bars.Any(b => Math.Abs(b.RebarSection.Diameter - bars[0].RebarSection.Diameter) > 1e-8))
                    throw new NotSupportedException("Diametri diversi nella stessa fila non supportati dall'adattatore.");
                return new BridgeRebarRow
                {
                    Enabled = true,
                    Diameter = bars[0].RebarSection.Diameter,
                    Pitch = section.Width / bars.Length,
                    AxisDistance = index == 0 ? section.Height - bars[0].Position.Y : bars[0].Position.Y
                };
            }
            var steel = positioned.Section.SteelMaterial;
            var rebar = section.Rebars.FirstOrDefault()?.RebarSection.RebarMaterial ?? steel;
            return new HBridgeInput
            {
                Materials = new BridgeMaterialSet((ConcreteMaterialEN1992)section.ConcreteMaterial, steel, rebar),
                Geometry = new HSectionDimensions
                {
                    SlabWidth = section.Width,
                    SlabHeight = section.Height,
                    WebHeight = h.HeightWeb,
                    WebThickness = h.ThicknessWeb,
                    TopWidth = h.LenghtTopFlange,
                    TopThickness = h.ThicknessTopFlange,
                    BottomWidth = h.LenghtBottomFlange,
                    BottomThickness = h.ThicknessBottomFlange
                },
                TopRebars = Row(0),
                BottomRebars = Row(1),
                Options = options with
                {
                    Class4 = buckling && options.Class4,
                    CommonLoadY = reference.Origin.Y
                }
            };
        }

        private BridgePhase Phase(ResultBeamForces forces, BridgePhaseKind kind, double phi, string name)
        {
            if (forces == null) throw new ArgumentNullException(nameof(forces));
            // Project onto the checker plane without changing the source FEM forces.
            // Strict callers may reject out-of-plane actions using dimensional tolerances.
            var global = forces.ToGlobalCoordinateSystem();
            if (new[] { global.N, global.V1, global.V2, global.T, global.M1, global.M2 }.Any(value => !double.IsFinite(value)))
                throw new ArgumentException("Azioni di sezione non finite.", nameof(forces));
            const double forceNoiseN = 1e-6, momentNoiseNmm = 1e-3;
            if (!ignoreSecondaryActions && (Math.Abs(global.M2) > momentNoiseNmm || Math.Abs(global.T) > momentNoiseNmm || Math.Abs(global.V1) > forceNoiseN))
                throw new NotSupportedException($"Il motore gPC adottato copre N–Mx–Vy. Flessione deviata, taglio trasversale e torsione richiedono un verificatore dedicato. " +
                    $"M2={global.M2:G6}, T={global.T:G6} N mm (tolleranza numerica {momentNoiseNmm:G}); V1={global.V1:G6} N (tolleranza {forceNoiseN:G}).");
            if (phi < 0 || !double.IsFinite(phi)) throw new ArgumentOutOfRangeException(nameof(phi), "Il rapporto modulare deve dare phi >= 0.");
            return new BridgePhase
            {
                Name = name,
                Kind = kind,
                ForceKN = global.N / 1000,
                MomentKNm = global.M1 / 1e6,
                ShearKN = global.V2 / 1000,
                Phi = phi,
                // Straus end forces are about the gross centroid of each phase section.
                // The bridge engine transports them to the effective centroid if required.
                Reference = BridgeLoadReference.GrossCentroid
            };
        }

        public bool GetLinearAnalysisResultByStages(out BridgeStressResult steelResult, out BridgeStressResult infiniteResult,
            out BridgeStressResult result, out double tauB, double phiInfinite, double phiInstant,
            ResultBeamForces steel, ResultBeamForces infinite, ResultBeamForces instant, bool concrete)
        {
            var analysis = Analyze(phiInfinite, phiInstant, steel, infinite, instant, concrete, true, 0, 0);
            steelResult = Result(analysis, 0);
            infiniteResult = Result(analysis, 1);
            result = Result(analysis, analysis.Stages.Count - 1);
            tauB = double.NaN;
            return true;
        }

        public bool GetLinearAnalysisResultByStages(out BridgeStressResult[] results, out double[] tauB,
            double phiInfinite, double phiInstant, ResultBeamForces[] steel, ResultBeamForces[] infinite,
            ResultBeamForces[] instant, bool concrete, double[] strainInfinite, double[] strainInstant, bool rebars, double? phiShrinkage = null, ResultBeamForces[] shrinkageForces = null)
        {
            int count = steel.Length;
            if (infinite.Length != count || instant.Length != count || strainInfinite.Length != count || strainInstant.Length != count)
                throw new ArgumentException("Le serie di fasi e deformazioni devono avere la stessa lunghezza.");
            results = new BridgeStressResult[count]; tauB = new double[count];
            var analyses = Calculate(Enumerable.Range(0, count).Select(i => (Func<HBridgeInput>)(() => AnalysisInput(phiInfinite, phiInstant, steel[i], infinite[i],
                instant[i], concrete, rebars, strainInfinite[i], strainInstant[i], phiShrinkage, shrinkageForces?[i]))).ToArray());
            for (int i = 0; i < count; i++)
            {
                results[i] = Result(analyses[i], analyses[i].Stages.Count - 1);
                tauB[i] = double.NaN;
            }
            return true;
        }

        /// <summary>
        /// The gPC analyses of the combinations of this section in one batch (<see cref="HBridgeSection.Calculate(IReadOnlyList{HBridgeInput}, System.Threading.CancellationToken)"/>):
        /// the phases they share are calculated once, with the numbers of separate analyses. An error is raised as the loop of separate analyses
        /// raised it, the first one in the order of the combinations, from building an input or from its analysis
        /// </summary>
        private static HBridgeAnalysisResult[] Calculate(IReadOnlyList<Func<HBridgeInput>> inputs)
        {
            int count = inputs.Count, built = count; var data = new List<HBridgeInput>(count); Exception buildError = null;
            for (int i = 0; i < count; i++)
            {
                try { data.Add(inputs[i]()); }
                catch (Exception e) { buildError = e; built = i; break; }
            }
            var calculated = HBridgeSection.Calculate(data);
            var analyses = new HBridgeAnalysisResult[count];
            for (int i = 0; i < built; i++)
            {
                if (calculated[i].Error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(calculated[i].Error).Throw();
                analyses[i] = calculated[i].Analysis;
            }
            if (buildError != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(buildError).Throw();
            return analyses;
        }

        private HBridgeAnalysisResult Analyze(double phiInfinite, double phiInstant, ResultBeamForces steel,
            ResultBeamForces infinite, ResultBeamForces instant, bool concrete, bool rebars, double strainInfinite, double strainInstant, double? phiShrinkage = null, ResultBeamForces shrinkageForces = null) =>
            HBridgeSection.Calculate(AnalysisInput(phiInfinite, phiInstant, steel, infinite, instant, concrete, rebars, strainInfinite, strainInstant, phiShrinkage, shrinkageForces));

        private HBridgeInput AnalysisInput(double phiInfinite, double phiInstant, ResultBeamForces steel,
            ResultBeamForces infinite, ResultBeamForces instant, bool concrete, bool rebars, double strainInfinite, double strainInstant, double? phiShrinkage = null, ResultBeamForces shrinkageForces = null)
        {
            var data = Input(concrete, rebars, true);
            var kind = concrete ? BridgePhaseKind.Composite : BridgePhaseKind.ConcreteExcluded;
            var phases = new List<BridgePhase> { Phase(steel, BridgePhaseKind.SteelOnly, 0, "Only Steel"),
                Phase(infinite, kind, phiInfinite, "Infinite Time"), Phase(instant, kind, phiInstant, "Instant Time") };
            if (shrinkageForces != null) phases.Add(Phase(shrinkageForces, kind, phiShrinkage ?? phiInfinite, "Shrinkage FEM"));
            if (!concrete && (strainInfinite != 0 || strainInstant != 0)) throw new NotSupportedException("Ritiro con soletta esclusa: definire prima la sequenza fisica delle fasi.");
            if (strainInfinite != 0) phases.Add(new BridgePhase { Name = "Strain Infinite", Kind = BridgePhaseKind.Shrinkage, Phi = phiShrinkage ?? phiInfinite, ShrinkageMicrostrain = strainInfinite * 1e6 });
            if (strainInstant != 0) phases.Add(new BridgePhase { Name = "Strain Instant", Kind = BridgePhaseKind.Shrinkage, Phi = phiInstant, ShrinkageMicrostrain = strainInstant * 1e6 });
            return data with { Phases = phases.ToArray() };
        }

        public BridgeStressResult GetSelfTension(double strain, double phi)
        {
            var data = Input(true, true, false) with
            {
                Phases = new[] { new BridgePhase {
                Name = "Self tension", Kind = BridgePhaseKind.Shrinkage, Phi = phi, ShrinkageMicrostrain = strain * 1e6 } }
            };
            return Result(HBridgeSection.Calculate(data), 0);
        }

        /// <summary>Sequential section history. Effective moduli affect new increments; this is not an age-dependent creep integrator.</summary>
        public BridgeStressResult AnalyzeHistory(BridgeWorkflow workflow, BridgePhaseAssembler.ResultIndex index, double station,
            LoadCombinationModel combination, double phiInfinite, double phiInstant, double phiShrinkage, bool rebars)
        {
            if (workflow?.Method != BridgeWorkflowMethod.LinearHistory) throw new ArgumentException("A LinearHistory workflow is required.");
            return AnalyzeWorkflow(workflow,index,station,combination,phiInfinite,phiInstant,phiShrinkage,rebars);
        }

        /// <summary>Per-action local states keep the FEM gross centroid and checker phase consistent.
        /// Compatible uses cumulative contributions, including the explicitly uncracked shrinkage model.</summary>
        public BridgeStressResult AnalyzeWorkflow(BridgeWorkflow workflow, BridgePhaseAssembler.ResultIndex index, double station,
            LoadCombinationModel combination, double phiInfinite, double phiInstant, double phiShrinkage, bool rebars)
        {
            var data = WorkflowInput(workflow, index, station, combination, phiInfinite, phiInstant, phiShrinkage, rebars);
            return WorkflowResult(workflow, workflow.Method == BridgeWorkflowMethod.LinearHistory
                ? HBridgeHistoryResults.Calculate(data, new HBridgeHistoryOptions { MaterialMode = HistoryMaterialMode.Linear }) : HBridgeSection.Calculate(data));
        }

        /// <summary>The workflow of every combination of the station, as <see cref="AnalyzeWorkflow(BridgeWorkflow, BridgePhaseAssembler.ResultIndex, double, LoadCombinationModel, double, double, double, bool)"/>
        /// of each one in order; the cumulative analyses run in one gPC batch, which calculates their shared phases once</summary>
        public BridgeStressResult[] AnalyzeWorkflow(BridgeWorkflow workflow, BridgePhaseAssembler.ResultIndex index, double station,
            IReadOnlyList<LoadCombinationModel> combinations, double phiInfinite, double phiInstant, double phiShrinkage, bool rebars)
        {
            if (workflow?.Method == BridgeWorkflowMethod.LinearHistory)
                return combinations.Select(combination => AnalyzeWorkflow(workflow, index, station, combination, phiInfinite, phiInstant, phiShrinkage, rebars)).ToArray();
            return Calculate(combinations.Select(combination => (Func<HBridgeInput>)(() => WorkflowInput(workflow, index, station, combination, phiInfinite,
                phiInstant, phiShrinkage, rebars))).ToArray()).Select(analysis => WorkflowResult(workflow, analysis)).ToArray();
        }

        private BridgeStressResult WorkflowResult(BridgeWorkflow workflow, HBridgeAnalysisResult analysis)
        {
            var result = Result(analysis, analysis.Stages.Count - 1);
            return workflow.Phases.Any(p => p.Actions.Any(a => a.HasLocalSectionStates))
                ? new BridgeStressResult(analysis, analysis.Stages.Count - 1, null, result.Assumptions.Append(LocalStateAssumption)) : result;
        }

        private HBridgeInput WorkflowInput(BridgeWorkflow workflow, BridgePhaseAssembler.ResultIndex index, double station,
            LoadCombinationModel combination, double phiInfinite, double phiInstant, double phiShrinkage, bool rebars)
        {
            if (workflow == null) throw new ArgumentNullException(nameof(workflow));
            var factors = combination.Values.ToDictionary(p => p.Key.Name, p => p.Value, StringComparer.Ordinal);
            double Coefficient(string name) => name != null && factors.TryGetValue(name, out var factor) ? factor : 0;
            var phases = new List<BridgePhase>();
            foreach (var stage in workflow.Phases.OrderBy(p => p.Order))
            {
                var kind = stage.State switch {
                    BridgeSectionState.Steel => BridgePhaseKind.SteelOnly,
                    BridgeSectionState.Composite => BridgePhaseKind.Composite,
                    _ => BridgePhaseKind.ConcreteExcluded };
                int before = phases.Count;
                (BridgePhaseKind Kind, double Phi)? unloadedState = null;
                foreach (var action in stage.Actions)
                {
                    var actionKind = action.ConcreteExcludedBeamIds?.Contains(index.BeamId, StringComparer.Ordinal) == true ? BridgePhaseKind.ConcreteExcluded : kind;
                    double factor = Coefficient(action.ForceCase), strainFactor = Coefficient(action.StrainCase);
                    if (action.ForceCase != null && action.StrainCase != null && factor != strainFactor)
                        throw new ArgumentException($"Action {action.Key}: FEM and shrinkage coefficients differ.");
                    double phi = action.Behavior switch { BridgeActionBehavior.LongTerm => phiInfinite,
                        BridgeActionBehavior.Shrinkage => phiShrinkage, BridgeActionBehavior.ShortTerm => phiInstant, _ => 0 };
                    string name = $"{stage.Order} · {stage.Name} / {action.Key}";
                    // Alternative positions with coefficient zero must not consume a GPC
                    // calculation stage each. Keep one unloaded local activation per run
                    // of identical states; preserve changes of stiffness and active increments.
                    var state = (actionKind, phi);
                    if (action.ForceCase != null && (factor != 0 || action.HasLocalSectionStates && unloadedState != state))
                        phases.Add(Phase(index.ReadForce(action.ForceCase, station, reference, factor), actionKind, phi, name + " / FEM"));
                    if (action.ForceCase != null)
                        unloadedState = factor == 0 && action.HasLocalSectionStates ? state : null;
                    double strain = index.ReadStrain(action.StrainCase, strainFactor);
                    if (strain != 0) phases.Add(new BridgePhase { Name = name + " / strain", Kind = BridgePhaseKind.Shrinkage,
                        Phi = phi, ShrinkageMicrostrain = strain * 1e6 });
                    if (strain != 0) unloadedState = null;
                }
                // Activation is a construction event even when every action coefficient is zero.
                if (phases.Count == before) phases.Add(new BridgePhase { Name = $"{stage.Order} · {stage.Name}", Kind = kind,
                    Phi = kind == BridgePhaseKind.SteelOnly ? 0 : phiInstant });
            }
            return Input(true, rebars, true) with { Phases = phases.ToArray() };
        }

        public static ResultBeamForces Scale(ResultBeamForces f, double factor) => new ResultBeamForces(
            f.N * factor, f.V1 * factor, f.V2 * factor, f.T * factor, f.M1 * factor, f.M2 * factor, f.CoordinateSystem, f.Id, f.Name);
    }
}
