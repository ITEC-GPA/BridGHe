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
            // F6 (a)/(b), R8: the reaction rows of the analysed GPC model of the action (the numbers the analysis wrote into the FEM results,
            // zero without sign as the FEM copies of the GH data), when the Bridge Action component attached it; the FEM results otherwise.
            var forces = action.GpcSource != null ? GpcReactions(action, support, tolerance)
                : FemReactions(action, source.Nodes.Where(n => n.Position.DistanceTo(support) <= tolerance).ToArray());
            if (forces.Length != 1 || !forces[0].IsValid)
                throw new ArgumentException($"Action {action.Key}: expected one finite linear-static reaction for case {action.Case}. Missing/multiple steps are not zero reactions.");
            reaction += factor * (forces[0] * upward);
        }
        if (!double.IsFinite(reaction)) throw new ArgumentException("Non-finite combined reaction.");
        return reaction / 1000;
    }

    /// <summary>The linear-static reactions (step 0) of the action case at the one source node at the support.</summary>
    private static Vector3d[] FemReactions(BridgeActionDefinition action, NodeModel[] nodes)
    {
        if (nodes.Length != 1) throw new ArgumentException($"Action {action.Key}: expected one node at the support, found {nodes.Length}.");
        var samples = nodes[0].Results.OfType<NodeReactResultModel>().Where(r => r.LoadCase?.Name == action.Case).ToArray();
        return samples.Length == 1 && samples[0].StepNum != 0 ? new[] { Vector3d.Unset } : samples.Select(r => r.Force).ToArray();
    }

    private static Vector3d[] GpcReactions(BridgeActionDefinition action, Point3d support, double tolerance)
    {
        var gpc = action.GpcSource; var catalog = Gpc.GpcResultCatalog.Of(gpc) ?? throw new ArgumentException("Missing GPC results for " + action.Key);
        var nodes = gpc.NodesElements.Values.Where(n => new Point3d(n.Position.X, n.Position.Y, n.Position.Z).DistanceTo(support) <= tolerance).ToArray();
        if (nodes.Length != 1) throw new ArgumentException($"Action {action.Key}: expected one node at the support, found {nodes.Length}.");
        return Gpc.GpcResultRows.Node(catalog, gpc, nodes[0]).Where(r => r.Reaction != null && catalog.Entries[r.Entry].Label == action.Case)
            .Select(r => new Vector3d(Gpc.GpcResultRows.Shown(r.Reaction[0]), Gpc.GpcResultRows.Shown(r.Reaction[1]), Gpc.GpcResultRows.Shown(r.Reaction[2]))).ToArray();
    }
}
