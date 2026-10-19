using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;

namespace BridGHe.Common.Diagnostics;

public sealed record EnvelopeRow(string Element, double Station, string Case, double Step, string StepType,
    double Value, double[] Components, ResultModel Source, string Coverage);

/// <summary>Envelopes stored results only. Never combines independent component maxima or interpolates steps.</summary>
public static class BridgeResultEnvelope
{
    public static double[] Values(ResultModel r) => r switch
    {
        BeamForceResultModel b => new[] { b.AxialForce, b.Vx, b.Vy, b.Torque, b.Mx, b.My },
        NodeReactResultModel n => new[] { n.Force.X, n.Force.Y, n.Force.Z, n.Moment.X, n.Moment.Y, n.Moment.Z },
        NodeDisplResultModel n => new[] { n.Displacement.X, n.Displacement.Y, n.Displacement.Z, n.Rotation.X, n.Rotation.Y, n.Rotation.Z },
        _ => throw new ArgumentException("Unsupported result type.")
    };
    public static EnvelopeRow[] Evaluate(ModelModel model, int kind, IReadOnlyList<string> ids, IReadOnlyList<string> cases, int component, int mode)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (kind is < 0 or > 2 || component is < 0 or > 5 || mode is < 0 or > 2)
            throw new ArgumentException("Kind 0 beam/1 reaction/2 displacement; component 0..5; mode 0 minimum/1 maximum/2 absolute.");
        var availableIds = (kind == 0 ? model.Beams.Select(b => b.BeamId) : model.Nodes.Select(n => n.NodeId)).ToArray();
        if (availableIds.Any(string.IsNullOrWhiteSpace) || availableIds.Distinct().Count() != availableIds.Length)
            throw new ArgumentException("Element IDs must be non-empty and unique.");
        var selectedIds = Select(ids, availableIds, "element");
        IEnumerable<(string Id, double X, ResultModel Result)> input = kind == 0
            ? model.Beams.Where(b => selectedIds.Contains(b.BeamId)).SelectMany(b => b.Results.OfType<BeamForceResultModel>()
                .Select(r => (b.BeamId, r.NormalizedLength ? r.Station * b.Length : r.Station, (ResultModel)r)))
            : model.Nodes.Where(n => selectedIds.Contains(n.NodeId)).SelectMany(n => n.Results
                .Where(r => kind == 1 ? r is NodeReactResultModel : r is NodeDisplResultModel).Select(r => (n.NodeId, 0d, r)));
        var source = input.ToArray();
        if (source.Length == 0) throw new ArgumentException("No stored results of the requested type for the selected elements.");
        if (kind == 0)
        {
            var lengths = model.Beams.ToDictionary(b => b.BeamId, b => b.Length, StringComparer.Ordinal);
            if (source.Any(r => !double.IsFinite(r.X) || r.X < 0 || r.X > lengths[r.Id] + 1e-9 * Math.Max(1, lengths[r.Id])))
                throw new ArgumentException("A beam result station lies outside the element length.");
        }
        if (source.Any(r => r.Result.LoadCase == null || string.IsNullOrWhiteSpace(r.Result.LoadCase.Name))) throw new ArgumentException("Stored result has no case identity.");
        var selectedCases = Select(cases, source.Select(r => r.Result.LoadCase.Name).Distinct().ToArray(), "case");
        var filtered = source.Where(r => selectedCases.Contains(r.Result.LoadCase.Name)).ToArray();
        var missingIds = selectedIds.Except(filtered.Select(r => r.Id)).ToArray();
        if (missingIds.Length > 0) throw new ArgumentException("No selected-case results for elements: " + string.Join(", ", missingIds));
        var rows = new List<EnvelopeRow>();
        foreach (var group in filtered.GroupBy(r => (r.Id, r.X)).OrderBy(g => g.Key.Id, StringComparer.Ordinal).ThenBy(g => g.Key.X))
        {
            if (!double.IsFinite(group.Key.X) || group.Key.X < 0) throw new ArgumentException("Invalid result station.");
            var data = group.Select(r => (r.Result, V: Values(r.Result))).ToArray();
            if (data.Any(r => !double.IsFinite(r.Result.StepNum) || r.V.Any(v => !double.IsFinite(v)))) throw new ArgumentException("Non-finite stored result; envelope aborted.");
            if (data.GroupBy(r => (r.Result.LoadCase.Name, r.Result.StepNum, r.Result.StepType)).Any(g => g.Count() > 1))
                throw new ArgumentException($"Duplicate result identity at {group.Key.Id}, station {group.Key.X}.");
            var ordered = data.OrderBy(r => r.Result.LoadCase.Name, StringComparer.Ordinal).ThenBy(r => r.Result.StepNum).ThenBy(r => r.Result.StepType, StringComparer.Ordinal);
            var governing = mode == 0 ? ordered.OrderBy(r => r.V[component]).First() :
                ordered.OrderByDescending(r => mode == 2 ? Math.Abs(r.V[component]) : r.V[component]).First();
            string[] absent = selectedCases.Except(data.Select(r => r.Result.LoadCase.Name)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            rows.Add(new(group.Key.Id, group.Key.X, governing.Result.LoadCase.Name, governing.Result.StepNum, governing.Result.StepType,
                governing.V[component], governing.V, governing.Result, absent.Length == 0 ? "Complete cases" : "Missing cases at this station: " + string.Join(", ", absent)));
        }
        return rows.ToArray();
    }
    private static HashSet<string> Select(IReadOnlyList<string> requested, string[] available, string label)
    {
        if (requested == null || requested.Count == 0) return available.ToHashSet(StringComparer.Ordinal);
        if (requested.Any(string.IsNullOrWhiteSpace) || requested.Distinct().Count() != requested.Count) throw new ArgumentException($"Invalid or duplicate {label} selection.");
        var missing = requested.Except(available).ToArray();
        if (missing.Length > 0) throw new ArgumentException($"Unknown {label}: " + string.Join(", ", missing));
        return requested.ToHashSet(StringComparer.Ordinal);
    }
}
