using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using GPC.Geometry;
using GPC.Model.Elements;
using GPC.Model.Results.Locations;
using GPC.Model.Results.Queries;
using D3Method = GPC.Checkers.CompositeBridge.BridgeWorkflowMethod;

namespace BridGHe.Common.Checks
{
    /// <summary>
    /// F6 C5: the section checks of Super Element Check with the GPC workflow executor (D3) on the GPC bridge model of Build Bridge Model
    /// (<see cref="GpcBridge"/>, built by <see cref="GpcBridgeBuilder"/>), as <c>--c5-compare</c> verified them against BridGHe's own phases (50: every total bit for bit;
    /// 99: within 2e-6 MPa, the start of the class-4 iterations changes with one increment per action). One plan per workflow
    /// (<see cref="BridgeWorkflowExecutor.Prepare"/>, cached by the recipe object: a new bridge model gives a new recipe and a new plan); for every
    /// section the BridGHe section input without phases, and per combination an evaluation at the station of the dataset, in BridGHe's
    /// reference frame (−1, −2, 3 of the principal axes), about the gross centroid, with the φ of the section.
    /// </summary>
    public sealed class GpcBridgeChecks
    {
        private static readonly ConditionalWeakTable<BridgeWorkflowDefinition, BridgeWorkflowPlan> plans = new ConditionalWeakTable<BridgeWorkflowDefinition, BridgeWorkflowPlan>();
        /// <summary>R3: the recipe copies with concrete excluded on given beams, per recipe and set of beams (one plan each).</summary>
        private static readonly ConditionalWeakTable<BridgeWorkflowDefinition, Dictionary<string, BridgeWorkflowDefinition>> excludedRecipes = new ConditionalWeakTable<BridgeWorkflowDefinition, Dictionary<string, BridgeWorkflowDefinition>>();
        private readonly GpcBridge bridge;
        private readonly BridgeWorkflowPlan plan;
        private readonly Dictionary<string, (BeamElement Beam, CoordinateSystem Principal, double[] Stations, string Fingerprint)> beams;
        private readonly IReadOnlyDictionary<Guid, ResultSelection> selections;
        private readonly IReadOnlyDictionary<Guid, double>[] factors;
        private readonly string[] assumptions;

        /// <param name="concreteExcludedBeamIds">F6 (a)/(b), R3: the beams whose composite property has «Consider Concrete» off on the path by
        /// cases (Compatible without local states): their long-term, short-term and shrinkage increments are checked on the concrete-excluded
        /// section, as BridGHe's phases did (BridgeCheckAdapter.GetLinearAnalysisResultByStages with concrete off):
        /// a copy of the recipe with these beams in <see cref="BridgeWorkflowActionDefinition.ConcreteExcludedBeamGuids"/> of those actions.</param>
        /// <summary>TRANSITIONAL (removed with the FeMM models): the combinations, BridGHe angles and records from the merged FEM model.</summary>
        public GpcBridgeChecks(GpcBridge bridge, ModelModel merged, IReadOnlyCollection<string> concreteExcludedBeamIds = null)
            : this(bridge, concreteExcludedBeamIds, merged.LoadCombinations.Select(c => c.Values.Select(p => (p.Key.Name, p.Value)).ToArray()).ToArray(),
                  merged.Beams.Select(b => b.AngleDeg).ToArray(), merged.BridgeWorkflow.Phases.Any(p => p.Actions.Any(a => a.HasLocalSectionStates))) { }

        /// <summary>P2T request 3: the checks on the GPC bridge model alone: the combinations of the bridge model (the table of Build BridGHe Model,
        /// in its order), the BridGHe angles and the records of the actions kept by <see cref="GpcBridge"/>.</summary>
        public GpcBridgeChecks(GpcBridge bridge, IReadOnlyCollection<string> concreteExcludedBeamIds = null)
            : this(bridge ?? throw new ArgumentNullException(nameof(bridge)), concreteExcludedBeamIds,
                  bridge.Combinations.Select(c => c.Factors.ToArray()).ToArray(),
                  bridge.BeamAngles.ToArray(), bridge.Records.Phases.Any(p => p.Actions.Any(a => a.HasLocalSectionStates))) { }

        private GpcBridgeChecks(GpcBridge bridge, IReadOnlyCollection<string> concreteExcludedBeamIds, (string Case, double Factor)[][] combinations, double[] angles, bool local)
        {
            this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            var model = bridge.Model; var workflow = Excluding(bridge.Workflow, model, concreteExcludedBeamIds);
            plan = plans.GetValue(workflow, w => BridgeWorkflowExecutor.Prepare(w, model, new[] { model }));
            var caseName = model.LoadCases.Values.ToDictionary(c => c.Guid, c => c.Name);
            var caseGuid = model.LoadCases.Values.ToDictionary(c => c.Name, c => c.Guid, StringComparer.Ordinal);
            var forceCases = workflow.Phases.SelectMany(p => p.Actions).Where(a => a.ForceCaseGuid.HasValue).Select(a => a.ForceCaseGuid.Value).Distinct().ToArray();
            selections = forceCases.ToDictionary(g => g, g => new ResultSelection { Dataset = bridge.Dataset, Case = caseName[g] });
            Guid Case(string name) => caseGuid.TryGetValue(name, out var g) ? g : throw new ArgumentException("GpcBridgeCombinationCaseNotInModel: " + name);
            factors = combinations.Select(c => (IReadOnlyDictionary<Guid, double>)c.ToDictionary(p => Case(p.Case), p => p.Factor)).ToArray();
            beams = new Dictionary<string, (BeamElement, CoordinateSystem, double[], string)>(StringComparer.Ordinal);
            for (int k = 0; k < angles.Length; k++)
            {
                var beam = model.BeamElements[k + 1];
                var stations = forceCases.Length == 0 ? Array.Empty<double>()
                    : ResultQueries.BeamSamples(beam, bridge.Dataset, caseName[forceCases[0]]).Select(s => s.ParametricDistance).Distinct().OrderBy(x => x).ToArray();
                beams[beam.Source.OriginalId] = (beam, GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, angles[k]), stations, BridgeWorkflowDefinition.PropertyFingerprint(beam));
            }
            // The assumptions of BridGHe's results (BridgeCheckAdapter): the plane of the check, and the local states when the recipe has them.
            assumptions = local ? new[] { BridgeCheckAdapter.PlanarAssumption, BridgeCheckAdapter.LocalStateAssumption } : new[] { BridgeCheckAdapter.PlanarAssumption };
        }

        /// <summary>Tests: what the constructor derived (combination factors by case GUID, principal frames, stations, property fingerprints,
        /// assumptions), doubles by their bits, to compare the two constructors bit for bit.</summary>
        public string Signature()
        {
            string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");
            var text = new System.Text.StringBuilder();
            foreach (var f in factors) text.Append("C:").Append(string.Join(",", f.Select(p => p.Key + "=" + Bits(p.Value)))).Append('\n');
            foreach (var (id, (beam, principal, stations, fingerprint)) in beams)
                text.Append("B:").Append(id).Append(':').Append(beam.Id).Append(':').Append(string.Join(",", new[] { principal.V1, principal.V2, principal.V3 }.SelectMany(v => new[] { v.X, v.Y, v.Z }).Select(Bits)))
                    .Append(':').Append(string.Join(",", stations.Select(Bits))).Append(':').Append(fingerprint).Append('\n');
            text.Append("A:").Append(string.Join("|", assumptions));
            return text.ToString();
        }

        public bool Has(string beamId) => beams.ContainsKey(beamId);

        private static BridgeWorkflowDefinition Excluding(BridgeWorkflowDefinition workflow, GPC.Model.Models.Model model, IReadOnlyCollection<string> beamIds)
        {
            if (beamIds == null || beamIds.Count == 0) return workflow;
            var ids = beamIds.Distinct(StringComparer.Ordinal).OrderBy(i => i, StringComparer.Ordinal).ToArray();
            var cache = excludedRecipes.GetValue(workflow, _ => new Dictionary<string, BridgeWorkflowDefinition>(StringComparer.Ordinal));
            string key = string.Join("|", ids);
            lock (cache)
            {
                if (cache.TryGetValue(key, out var known)) return known;
                var guids = model.BeamElements.Values.ToDictionary(b => b.Source.OriginalId, b => b.Guid, StringComparer.Ordinal);
                var excluded = ids.Select(i => guids.TryGetValue(i, out var g) ? g : throw new ArgumentException("GpcBridgeExcludedBeamNotInModel: " + i)).ToHashSet();
                var copy = BridgeWorkflowDefinition.Read(workflow.Save());
                foreach (var a in copy.Phases.SelectMany(p => p.Actions))
                {
                    if (a.Behavior == GPC.Checkers.CompositeBridge.BridgeActionBehavior.Steel) continue;
                    var applies = a.BeamGuids.Length == 0 ? excluded : excluded.Where(a.BeamGuids.Contains).ToHashSet();
                    a.ConcreteExcludedBeamGuids = a.ConcreteExcludedBeamGuids.Union(applies).OrderBy(g => g).ToArray();
                }
                cache.Add(key, copy);
                return copy;
            }
        }

        /// <summary>The checks of one section for every combination of the merged model, in their order.</summary>
        /// <param name="stationMm">The BridGHe station (distance from the start of the beam); the evaluation uses the dataset station nearest to it.</param>
        public BridgeStressResult[] Calculate(string beamId, double stationMm, HBridgeInput section, double longTermPhi, double shortTermPhi, double shrinkagePhi)
        {
            var (beam, principal, stations, fingerprint) = beams[beamId];
            if (stations.Length == 0) throw new InvalidOperationException("GpcBridgeBeamWithoutResults: " + beamId);
            double length = beam.Length, station = stations.OrderBy(s => Math.Abs(s * length - stationMm)).First();
            if (Math.Abs(station * length - stationMm) > Math.Max(1e-3, length * 1e-9))
                throw new InvalidOperationException($"GpcBridgeStationNotInDataset: beam {beamId}, station {stationMm:R} mm (nearest {station * length:R} mm)");
            var point = beam.StartPoint + (beam.EndPoint - beam.StartPoint) * station;
            if (beam.Assignments.OffsetI != null) point += (beam.Assignments.OffsetAxes ?? beam.Assignments.SectionAxes).ToGlobal(beam.Assignments.OffsetI);
            var axes = new CoordinateSystem(point, principal.V1 * -1, principal.V2 * -1, principal.V3);
            var input = section with { Phases = Array.Empty<BridgePhase>() };
            BridgeWorkflowEvaluation Evaluation(int c) => new BridgeWorkflowEvaluation
            {
                BeamGuid = beam.Guid, PhysicalPropertyFingerprint = fingerprint, Station = station, Side = SectionSide.Unspecified, SectionInput = input, SectionAxes = axes,
                LoadReference = BridgeLoadReference.GrossCentroid, Factors = factors[c], Results = selections,
                LongTermPhi = longTermPhi, ShortTermPhi = shortTermPhi, ShrinkagePhi = shrinkagePhi,
            };
            var results = new BridgeStressResult[factors.Length];
            if (plan.Method == D3Method.LinearHistory)
            {
                for (int c = 0; c < factors.Length; c++) { var a = plan.Calculate(Evaluation(c)); results[c] = new BridgeStressResult(a, a.Stages.Count - 1, null, assumptions); }
                return results;
            }
            // Compatible: the combinations of the section in one gPC batch (shared phases calculated once, numbers of separate analyses), as BridGHe.
            var built = new List<HBridgeInput>(factors.Length); var warnings = new List<IReadOnlyList<string>>(factors.Length);
            for (int c = 0; c < factors.Length; c++) { built.Add(plan.BuildInput(Evaluation(c), out var w)); warnings.Add(w); }
            var calculated = HBridgeSection.Calculate(built);
            for (int c = 0; c < factors.Length; c++)
            {
                if (calculated[c].Error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(calculated[c].Error).Throw();
                var analysis = calculated[c].Analysis;
                foreach (var stage in analysis.Stages) stage.Warnings.AddRange(warnings[c]);
                results[c] = new BridgeStressResult(analysis, analysis.Stages.Count - 1, null, assumptions);
            }
            return results;
        }
    }
}
