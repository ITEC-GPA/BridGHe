using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Checks;

/// <summary>Linear static reactions combined from the same incremental sources and coefficients as the bridge workflow.</summary>
public static class BridgeSupportReactions
{

    /// <summary>P2T request 3: the signed support reaction [kN] of a combination on the GPC bridge model alone (its records and combinations),
    /// from the reaction rows of the analysed GPC source of every action (same rules and messages as the former reading; the source is the
    /// analysed GPC model of the action, its name the one the record keeps).
    /// 0.7.0: <paramref name="reactionsByAction"/>, when given, keeps the reaction of every action read at this support, so the combinations of one
    /// solve read each action once (an envelope action reads all its positions in every combination). Use one dictionary per support point, bridge,
    /// phases and tolerance, and only within one solve.</summary>
    public static double SignedKN(Gpc.GpcBridge bridge, IEnumerable<BridgePhaseDefinition> definitions,
        Point3d support, string combination, Vector3d upward, double tolerance, IDictionary<string, Vector3d[]> reactionsByAction = null)
    {
        if (bridge?.Records == null) throw new ArgumentException("Supply a Build BridGHe Model result in N-mm.");
        var combos = bridge.Combinations.Where(c => c.Name == combination).ToArray();
        Func<BridgeActionDefinition, BridgeWorkflowAction, Vector3d[]> reactions = Read;
        if (reactionsByAction != null)
            reactions = (action, record) =>
            {
                if (!reactionsByAction.TryGetValue(action.Key, out var read)) reactionsByAction[action.Key] = read = Read(action, record);
                return read;
            };
        return SignedKN(bridge.Records, combos.Select(c => c.Factors.AsEnumerable()).ToArray(), definitions, support, combination, upward, tolerance, reactions);

        Vector3d[] Read(BridgeActionDefinition action, BridgeWorkflowAction record)
        {
            var source = action.GpcSource ?? throw new ArgumentException("Missing GPC source for " + action.Key);
            var catalog = Gpc.GpcResultCatalog.Of(source);
            if (catalog == null || catalog.Units.Units != Gpc.DefinitionUnits.Nmm || source.Name != record.SourceName)
                throw new ArgumentException("The reaction source does not match the assembled action " + action.Key);
            return GpcReactions(action, support, tolerance);
        }
    }

    private static double SignedKN(BridgeWorkflow workflow, IEnumerable<(string Case, double Factor)>[] combos, IEnumerable<BridgePhaseDefinition> definitions,
        Point3d support, string combination, Vector3d upward, double tolerance, Func<BridgeActionDefinition, BridgeWorkflowAction, Vector3d[]> reactions)
    {
        if (!support.IsValid || !upward.IsValid || !upward.Unitize() || !double.IsFinite(tolerance) || tolerance <= 0)
            throw new ArgumentException("Invalid support point, upward direction or tolerance.");
        var phases = definitions?.ToArray() ?? throw new ArgumentNullException(nameof(definitions));
        var actions = phases.SelectMany(p => p.Actions).ToDictionary(a => a.Key, StringComparer.Ordinal);
        var records = workflow.Phases.SelectMany(p => p.Actions).ToArray();
        // 0.7.0: the envelope actions of Bridge Moving Envelope («prefix@rule») are not in the phases: their reaction comes from their positions
        var real = records.Where(r => !MovingLoads.BridgeMovingEnvelope.IsEnvelope(r.Key)).ToArray();
        if (!actions.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(real.Select(r => r.Key)))
            throw new ArgumentException("Phases do not match the assembled bridge actions.");
        if (combos.Length != 1) throw new ArgumentException("Expected one combination named " + combination);
        var factors = combos[0].ToDictionary(v => v.Case, v => v.Factor, StringComparer.Ordinal);
        double reaction = 0;
        foreach (var record in records)
        {
            if (MovingLoads.BridgeMovingEnvelope.TryParse(record.Key, out var prefix, out _))
            {
                // the largest compression among the positions of the envelope (the factor of a moving load is never negative)
                if (!factors.TryGetValue(record.ForceCase, out double envelopeFactor) || envelopeFactor == 0) continue;
                if (!double.IsFinite(envelopeFactor) || envelopeFactor < 0) throw new ArgumentException("Envelope action with a negative or non-finite coefficient: " + record.Key);
                double largest = double.NegativeInfinity;
                foreach (var position in real.Where(r => MovingLoads.BridgeMovingEnvelope.IsPosition(r.Key, prefix)))
                {
                    var positionForces = reactions(actions[position.Key], position);
                    if (positionForces.Length != 1 || !positionForces[0].IsValid)
                        throw new ArgumentException($"Action {position.Key}: expected one finite linear-static reaction for case {position.SourceCase}. Missing/multiple steps are not zero reactions.");
                    largest = Math.Max(largest, positionForces[0] * upward);
                }
                if (double.IsNegativeInfinity(largest)) throw new ArgumentException("Envelope action without positions: " + record.Key);
                reaction += envelopeFactor * largest;
                continue;
            }
            var action = actions[record.Key];
            if (record.SourceCase != action.Case || record.Behavior != action.Behavior || record.Strain != action.Strain)
                throw new ArgumentException("Changed phase definition: rebuild the bridge model before reading reactions.");
            if (record.ForceCase == null) continue; // Explicit primary-only eigenstrain has no global FEM reaction.
            if (!factors.TryGetValue(record.ForceCase, out double factor) || factor == 0) continue;
            if (!double.IsFinite(factor)) throw new ArgumentException("Non-finite combination coefficient.");
            if (action.BeamIds.Length != 0) throw new ArgumentException("Global support reactions cannot be apportioned to a subset of beam IDs.");
            var forces = reactions(action, record);
            if (forces.Length != 1 || !forces[0].IsValid)
                throw new ArgumentException($"Action {action.Key}: expected one finite linear-static reaction for case {action.Case}. Missing/multiple steps are not zero reactions.");
            reaction += factor * (forces[0] * upward);
        }
        if (!double.IsFinite(reaction)) throw new ArgumentException("Non-finite combined reaction.");
        return reaction / 1000;
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
