using System;
using System.Linq;
using System.Collections.Generic;
using GPC.Geometry;
using GPC.Model.Results;
using BridGHe.Common.Models;

namespace BridGHe.Common.Checks
{
    public sealed record BridgePhaseActions(ResultBeamForces Steel, ResultBeamForces Infinite, ResultBeamForces Instant,
        double StrainInfinite, double StrainInstant, ResultBeamForces Shrinkage = null);

    public static class BridgePhaseAssembler
    {


        /// <summary>F6 (a)/(b), R4: the index of a beam of the GPC bridge model, from its merged samples (case name, station mm, BridGHe
        /// convention: the numbers of the former result objects). No slab strain: the GPC workflow executor takes the strains from its recipe.</summary>
        public static ResultIndex CreateIndex(string beamId, IEnumerable<(string Case, BeamForceSample Sample)> samples, double tolerance) => new ResultIndex(beamId, samples, tolerance);

        /// <summary>Build once after results are attached, then reuse for every station and combination.</summary>
        public sealed class ResultIndex
        {
            public string BeamId { get; }
            private readonly Dictionary<string, (double Station, BeamForceSample Force)[]> _forces;
            private readonly double _tolerance;


            internal ResultIndex(string beamId, IEnumerable<(string Case, BeamForceSample Sample)> samples, double tolerance)
            {
                if (samples == null) throw new ArgumentNullException(nameof(samples));
                if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
                BeamId = beamId; _tolerance = tolerance;
                _forces = samples.GroupBy(f => f.Case, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Select(f => (Station: f.Sample.Station, Force: f.Sample)).OrderBy(f => f.Station).ToArray(), StringComparer.Ordinal);
                if (_forces.Values.SelectMany(f => f).Any(f => !double.IsFinite(f.Station)))
                    throw new ArgumentException("Stazione di risultato non finita.");
            }

            private BeamForceSample ForceAt(string name, double station)
            {
                int count = 0;
                BeamForceSample result = default;
                if (_forces.TryGetValue(name, out var values))
                {
                    int low = 0, high = values.Length;
                    while (low < high)
                    {
                        int mid = low + (high - low) / 2;
                        if (values[mid].Station < station - _tolerance) low = mid + 1;
                        else high = mid;
                    }
                    for (int i = low; i < values.Length && values[i].Station <= station + _tolerance; i++)
                        if (Math.Abs(values[i].Station - station) <= _tolerance) { result = values[i].Force; count++; }
                }
                if (count != 1) throw new InvalidOperationException($"Caso {name}, stazione {station}: atteso un risultato univoco, trovati {count}.");
                return result;
            }

            public ResultBeamForces ReadForce(string name, double station, CoordinateSystem reference, double coefficient) =>
                name == null || coefficient == 0 ? new ResultBeamForces(0, 0, 0, 0, 0, 0, reference) :
                BridgeCheckAdapter.Scale(BridgeForceConvention.FromStraus(ForceAt(name, station), name, reference), coefficient);

            /// <summary>P2CE (13/11/2026, phase 2a): always 0, as the index of the GPC samples has always given (the slab strains were read only
            /// by the FEM index, deleted in phase 1; the GPC workflow executor takes the strains from its recipe).</summary>
            public double ReadStrain(string name, double coefficient) => 0;


            /// <summary>R6.4 (5), decision (i) = A: the actions of a combination of the GPC bridge model at a station, from its combination table
            /// (<see cref="Gpc.GpcBridge.Combinations"/>: case names and coefficients in order, zeros included) and the phase of every merged
            /// case from the records of the recipe (<see cref="Gpc.GpcBridge.Records"/>: "key [FEM]" in the behaviour of its action, "key
            /// [strain]" a strain increment), with the same sums in the same order as the former FEM <c>Assemble(station, combination, reference)</c> (deleted in P2CE phase 1)
            /// on the merged FEM model (whose cases had these phases as their one stage).</summary>
            public BridgePhaseActions Assemble(double station, IEnumerable<(string Case, double Factor)> factors, IReadOnlyDictionary<string, BridgeCasePhase> phases, CoordinateSystem reference)
            {
                if (!double.IsFinite(station)) throw new ArgumentOutOfRangeException(nameof(station));
                if (factors == null) throw new ArgumentNullException(nameof(factors));
                if (phases == null) throw new ArgumentNullException(nameof(phases));
                ResultBeamForces Zero() => new ResultBeamForces(0, 0, 0, 0, 0, 0, reference);
                var steel = Zero(); var infinite = Zero(); var instant = Zero();
                ResultBeamForces shrinkage = null;
                double strainInfinite = 0, strainInstant = 0;
                foreach (var (name, factor) in factors)
                {
                    if (factor == 0) continue;
                    if (name == null || !phases.TryGetValue(name, out var phase)) throw new InvalidOperationException($"Fase non assegnata per il caso {name}.");
                    if (phase == BridgeCasePhase.StrainInfinite || phase == BridgeCasePhase.StrainInstant)
                    {
                        double unscaledStrain = 0; // P2CE phase 2a: no strain in the index of the GPC samples (as before: an empty table)
                        double strain = unscaledStrain * factor;
                        if (phase == BridgeCasePhase.StrainInfinite) strainInfinite += strain;
                        else strainInstant += strain;
                        continue;
                    }
                    var force = BridgeCheckAdapter.Scale(BridgeForceConvention.FromStraus(ForceAt(name, station), name, reference), factor);
                    switch (phase)
                    {
                        case BridgeCasePhase.OnlySteel: steel += force; break;
                        case BridgeCasePhase.NInfinite: infinite += force; break;
                        case BridgeCasePhase.NInstant: instant += force; break;
                        case BridgeCasePhase.NShrinkage: shrinkage = (shrinkage ?? Zero()) + force; break;
                        default: throw new InvalidOperationException($"Fase non assegnata per il caso {name}.");
                    }
                }
                return new BridgePhaseActions(steel, infinite, instant, strainInfinite, strainInstant, shrinkage);
            }
        }

        /// <summary>R6.4 (5): the phase of every merged case of a bridge recipe, as <see cref="BridgeWorkflowBuilder"/> gave the cases of the
        /// merged FEM model: the force case of an action in its behaviour (steel, long term, short term, shrinkage), its strain case a strain
        /// increment (short term: instantaneous, otherwise long term).</summary>
        public static IReadOnlyDictionary<string, BridgeCasePhase> CasePhases(BridgeWorkflow records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            var phases = new Dictionary<string, BridgeCasePhase>(StringComparer.Ordinal);
            foreach (var a in records.Phases.SelectMany(p => p.Actions))
            {
                if (a.ForceCase != null) phases[a.ForceCase] = a.Behavior switch
                {
                    BridgeActionBehavior.Steel => BridgeCasePhase.OnlySteel,
                    BridgeActionBehavior.LongTerm => BridgeCasePhase.NInfinite,
                    BridgeActionBehavior.ShortTerm => BridgeCasePhase.NInstant,
                    _ => BridgeCasePhase.NShrinkage,
                };
                if (a.StrainCase != null) phases[a.StrainCase] = a.Behavior == BridgeActionBehavior.ShortTerm ? BridgeCasePhase.StrainInstant : BridgeCasePhase.StrainInfinite;
            }
            return phases;
        }
    }

    /// <summary>R6.4 (5): the phase of a merged case of the bridge recipe (the former BridgePhases of its FEM case).</summary>
    public enum BridgeCasePhase { OnlySteel, NInfinite, NInstant, NShrinkage, StrainInfinite, StrainInstant }
}
