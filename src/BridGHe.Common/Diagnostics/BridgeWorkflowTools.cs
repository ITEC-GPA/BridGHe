using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Checks;

namespace BridGHe.Common.Diagnostics;

public static class BridgeWorkflowTools
{
    public static BridgePhaseDefinition[] Ordered(IEnumerable<BridgePhaseDefinition> input)
    {
        var phases = input?.ToArray() ?? throw new ArgumentNullException(nameof(input));
        if (phases.Length == 0 || phases.Any(p => p == null || p.Id == Guid.Empty || p.Order < 0 || string.IsNullOrWhiteSpace(p.Name) || !Enum.IsDefined(p.State) || p.Actions == null))
            throw new ArgumentException("Supply valid numbered Bridge Phase objects.");
        if (phases.Select(p => p.Order).Distinct().Count() != phases.Length || phases.Select(p => p.Id).Distinct().Count() != phases.Length)
            throw new ArgumentException("Phase numbers and identities must be unique.");
        var actions = phases.SelectMany(p => p.Actions).ToArray();
        if (actions.Any(a => a == null || string.IsNullOrWhiteSpace(a.Key)) || actions.Select(a => a.Key).Distinct(StringComparer.Ordinal).Count() != actions.Length)
            throw new ArgumentException("Action IDs must be non-empty and unique across phases.");
        var ordered = phases.OrderBy(p => p.Order).ToArray(); double lastDay = 0;
        foreach (var phase in ordered)
            if (phase.Day is double day)
            {
                if (!double.IsFinite(day) || day < lastDay) throw new ArgumentException("Phase days must be finite, non-negative and non-decreasing in phase order.");
                lastDay = day;
            }
        return ordered;
    }
    public static string[][] CombinationTable(IEnumerable<BridgePhaseDefinition> phases, IReadOnlyList<string> headers, IReadOnlyList<string> names, IReadOnlyList<double[]> factors)
    {
        var keys = Ordered(phases).SelectMany(p => p.Actions).Select(a => a.Key).ToArray();
        if (headers == null || headers.Count == 0 || headers.Any(string.IsNullOrWhiteSpace) || headers.Distinct(StringComparer.Ordinal).Count() != headers.Count)
            throw new ArgumentException("Combination headers must be explicit unique Action IDs.");
        if (headers.Except(keys).Any()) throw new ArgumentException("Unknown Action ID in combination headers: " + string.Join(", ", headers.Except(keys)));
        if (names == null || factors == null || names.Count == 0 || names.Count != factors.Count || names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            throw new ArgumentException("Supply unique combination names, one per coefficient branch.");
        if (factors.Any(row => row == null || row.Length != headers.Count || row.Any(v => !double.IsFinite(v))))
            throw new ArgumentException("Every coefficient branch must match the Action ID count and contain finite values.");
        var result = new List<string[]> { new[] { "Combination" }.Concat(keys).ToArray() };
        var index = headers.Select((key, i) => (key, i)).ToDictionary(x => x.key, x => x.i, StringComparer.Ordinal);
        for (int i = 0; i < names.Count; i++) result.Add(new[] { names[i] }.Concat(keys.Select(k => (index.TryGetValue(k, out int j) ? factors[i][j] : 0).ToString("R", CultureInfo.InvariantCulture))).ToArray());
        return result.ToArray();
    }
    public static double[] ShrinkageIncrements(IReadOnlyList<double> days, IReadOnlyList<double> cumulative, double initial)
    {
        if (days == null || cumulative == null || days.Count == 0 || days.Count != cumulative.Count || !double.IsFinite(initial) || initial > 0)
            throw new ArgumentException("Supply paired days/cumulative strains and a non-positive initial strain.");
        double lastDay = -1, last = initial; var increments = new double[days.Count];
        for (int i = 0; i < days.Count; i++)
        {
            if (!double.IsFinite(days[i]) || days[i] < 0 || days[i] <= lastDay || !double.IsFinite(cumulative[i]) || cumulative[i] > last)
                throw new ArgumentException("Days must increase strictly; cumulative shrinkage must be finite, non-positive and non-increasing.");
            increments[i] = cumulative[i] - last; last = cumulative[i]; lastDay = days[i];
        }
        return increments;
    }
}
