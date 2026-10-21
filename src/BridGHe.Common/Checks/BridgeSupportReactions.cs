using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Checks;

/// <summary>Linear static reactions combined from the same incremental sources and coefficients as the bridge workflow.</summary>
public static class BridgeSupportReactions
{
    public static double SignedKN(ModelModel model, IEnumerable<BridgePhaseDefinition> definitions,
        Point3d support, string combination, Vector3d upward, double tolerance)
    {
        if (model?.BridgeWorkflow == null || model.Units != ModelModel.ModelUnits.Nmm) throw new ArgumentException("Supply a Build BridGHe Model result in N-mm.");
        if (!support.IsValid || !upward.IsValid || !upward.Unitize() || !double.IsFinite(tolerance) || tolerance <= 0)
            throw new ArgumentException("Invalid support point, upward direction or tolerance.");
        var phases = definitions?.ToArray() ?? throw new ArgumentNullException(nameof(definitions));
        var actions = phases.SelectMany(p => p.Actions).ToDictionary(a => a.Key, StringComparer.Ordinal);
        var records = model.BridgeWorkflow.Phases.SelectMany(p => p.Actions).ToArray();
        if (!actions.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(records.Select(r => r.Key)))
            throw new ArgumentException("Phases do not match the assembled bridge actions.");
        var combos = model.LoadCombinations.Where(c => c.Name == combination).ToArray();
        if (combos.Length != 1) throw new ArgumentException("Expected one combination named " + combination);
        var factors = combos[0].Values.ToDictionary(v => v.Key.Name, v => v.Value, StringComparer.Ordinal);
        double reaction = 0;
        foreach (var record in records)
        {
            var action = actions[record.Key];
            if (record.SourceCase != action.Case || record.Behavior != action.Behavior || record.Strain != action.Strain)
                throw new ArgumentException("Changed phase definition: rebuild the bridge model before reading reactions.");
            if (record.ForceCase == null) continue; // Explicit primary-only eigenstrain has no global FEM reaction.
            if (!factors.TryGetValue(record.ForceCase, out double factor) || factor == 0) continue;
            if (!double.IsFinite(factor)) throw new ArgumentException("Non-finite combination coefficient.");
            if (action.BeamIds.Length != 0) throw new ArgumentException("Global support reactions cannot be apportioned to a subset of beam IDs.");
            var source = action.Source ?? throw new ArgumentException("Missing FEM source for " + action.Key);
            if (source.Units != ModelModel.ModelUnits.Nmm || source.Name != record.SourceName)
                throw new ArgumentException("The reaction source does not match the assembled action " + action.Key);
            var nodes = source.Nodes.Where(n => n.Position.DistanceTo(support) <= tolerance).ToArray();
            if (nodes.Length != 1) throw new ArgumentException($"Action {action.Key}: expected one node at the support, found {nodes.Length}.");
            var samples = nodes[0].Results.OfType<NodeReactResultModel>().Where(r => r.LoadCase?.Name == action.Case).ToArray();
            if (samples.Length != 1 || samples[0].StepNum != 0 || !samples[0].Force.IsValid)
                throw new ArgumentException($"Action {action.Key}: expected one finite linear-static reaction for case {action.Case}. Missing/multiple steps are not zero reactions.");
            reaction += factor * (samples[0].Force * upward);
        }
        if (!double.IsFinite(reaction)) throw new ArgumentException("Non-finite combined reaction.");
        return reaction / 1000;
    }
}
