using System;
using System.Linq;
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
            LoadCombinationModel combination, CoordinateSystem reference, double tolerance)
        {
            ResultBeamForces Zero() => new ResultBeamForces(0, 0, 0, 0, 0, 0, reference);
            var steel = Zero(); var infinite = Zero(); var instant = Zero();
            double strainInfinite = 0, strainInstant = 0;
            var forces = beam.Results.OfType<BeamForceResultModel>()
                .Where(f => Math.Abs((f.NormalizedLength ? f.Station * length : f.Station) - station) <= tolerance)
                .ToLookup(f => f.LoadCase.Name, StringComparer.Ordinal);
            var strains = beam.Loads.OfType<CompositeBeamSlabStrainModel>().ToLookup(l => l.LoadCase.Name, StringComparer.Ordinal);
            foreach (var entry in combination.Values)
            {
                if (entry.Value == 0) continue;
                var loadCase = entry.Key;
                if (loadCase.Stages == null || loadCase.Stages.Count != 1)
                    throw new InvalidOperationException($"Il caso {loadCase.Name} deve appartenere a una sola fase MeCheck2.");
                var phase = loadCase.Stages[0].BridgePhase;
                if (phase == LoadCaseModel.Stage.BridgePhases.StrainInfinite || phase == LoadCaseModel.Stage.BridgePhases.StrainInstant)
                {
                    double strain = strains[loadCase.Name].Sum(l => l.Value.Strain) * entry.Value;
                    if (phase == LoadCaseModel.Stage.BridgePhases.StrainInfinite) strainInfinite += strain;
                    else strainInstant += strain;
                    continue;
                }
                var matches = forces[loadCase.Name].ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException($"Caso {loadCase.Name}, stazione {station}: atteso un risultato univoco, trovati {matches.Length}.");
                var force = BridgeCheckAdapter.Scale(BridgeForceConvention.FromStraus(matches[0], reference), entry.Value);
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
