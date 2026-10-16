using System;
using System.Linq;
using System.Collections.Generic;
using BridGHe.Common.Models;
using GPC.Geometry;
using GPC.Model.Results;

namespace BridGHe.Common.Checks
{
    public sealed record BridgePhaseActions(ResultBeamForces Steel, ResultBeamForces Infinite, ResultBeamForces Instant,
        double StrainInfinite, double StrainInstant);

    public static class BridgePhaseAssembler
    {
        public static BridgePhaseActions Assemble(ElementModel beam, double length, double station,
            LoadCombinationModel combination, CoordinateSystem reference, double tolerance) =>
            CreateIndex(beam, length, tolerance).Assemble(station, combination, reference);

        public static ResultIndex CreateIndex(ElementModel beam, double length, double tolerance) => new ResultIndex(beam, length, tolerance);

        /// <summary>Build once after results are attached, then reuse for every station and combination.</summary>
        public sealed class ResultIndex
        {
            private readonly Dictionary<string, (double Station, BeamForceResultModel Force)[]> _forces;
            private readonly Dictionary<string, double> _strains;
            private readonly double _tolerance;

            internal ResultIndex(ElementModel beam, double length, double tolerance)
            {
                if (beam == null) throw new ArgumentNullException(nameof(beam));
                if (!double.IsFinite(length) || length <= 0) throw new ArgumentOutOfRangeException(nameof(length));
                if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
                _tolerance = tolerance;
                _forces = beam.Results.OfType<BeamForceResultModel>().GroupBy(f => f.LoadCase.Name, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Select(f => (Station: f.NormalizedLength ? f.Station * length : f.Station, Force: f))
                        .OrderBy(f => f.Station).ToArray(), StringComparer.Ordinal);
                if (_forces.Values.SelectMany(f => f).Any(f => !double.IsFinite(f.Station)))
                    throw new ArgumentException("Stazione di risultato non finita.");
                _strains = beam.Loads.OfType<CompositeBeamSlabStrainModel>().GroupBy(l => l.LoadCase.Name, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Sum(l => l.Value.Strain), StringComparer.Ordinal);
            }

            private BeamForceResultModel ForceAt(string name, double station)
            {
                int count = 0;
                BeamForceResultModel result = null;
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

            public BridgePhaseActions Assemble(double station, LoadCombinationModel combination, CoordinateSystem reference)
            {
                if (!double.IsFinite(station)) throw new ArgumentOutOfRangeException(nameof(station));
                ResultBeamForces Zero() => new ResultBeamForces(0, 0, 0, 0, 0, 0, reference);
                var steel = Zero(); var infinite = Zero(); var instant = Zero();
                double strainInfinite = 0, strainInstant = 0;
                foreach (var entry in combination.Values)
                {
                    if (entry.Value == 0) continue;
                    var loadCase = entry.Key;
                    if (loadCase.Stages == null || loadCase.Stages.Count != 1)
                        throw new InvalidOperationException($"Il caso {loadCase.Name} deve appartenere a una sola fase MeCheck2.");
                    var phase = loadCase.Stages[0].BridgePhase;
                    if (phase == LoadCaseModel.Stage.BridgePhases.StrainInfinite || phase == LoadCaseModel.Stage.BridgePhases.StrainInstant)
                    {
                        _strains.TryGetValue(loadCase.Name, out double unscaledStrain);
                        double strain = unscaledStrain * entry.Value;
                        if (phase == LoadCaseModel.Stage.BridgePhases.StrainInfinite) strainInfinite += strain;
                        else strainInstant += strain;
                        continue;
                    }
                    var force = BridgeCheckAdapter.Scale(BridgeForceConvention.FromStraus(ForceAt(loadCase.Name, station), reference), entry.Value);
                    switch (phase)
                    {
                        case LoadCaseModel.Stage.BridgePhases.OnlySteel: steel += force; break;
                        case LoadCaseModel.Stage.BridgePhases.NInfinite: infinite += force; break;
                        case LoadCaseModel.Stage.BridgePhases.NInstant: instant += force; break;
                        default: throw new InvalidOperationException($"Fase non assegnata per il caso {loadCase.Name}.");
                    }
                }
                return new BridgePhaseActions(steel, infinite, instant, strainInfinite, strainInstant);
            }
        }
    }
}
