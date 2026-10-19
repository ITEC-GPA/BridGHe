using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Common.Diagnostics;

public sealed record ComparisonRow(string Key, double Reference, double Candidate, double Difference, double RelativeDifference, bool WithinTolerance);

public static class BridgeResultComparison
{
    public static ComparisonRow[] Compare(IReadOnlyList<string> referenceKeys, IReadOnlyList<double> reference,
        IReadOnlyList<string> candidateKeys, IReadOnlyList<double> candidate, double absoluteTolerance, double relativeTolerance)
    {
        void Validate(IReadOnlyList<string> keys, IReadOnlyList<double> values)
        {
            if (keys == null || values == null || keys.Count == 0 || keys.Count != values.Count || keys.Any(string.IsNullOrWhiteSpace) ||
                keys.Distinct(StringComparer.Ordinal).Count() != keys.Count || values.Any(v => !double.IsFinite(v)))
                throw new ArgumentException("Supply non-empty unique keys with one finite value each.");
        }
        Validate(referenceKeys, reference); Validate(candidateKeys, candidate);
        if (!double.IsFinite(absoluteTolerance) || !double.IsFinite(relativeTolerance) || absoluteTolerance < 0 || relativeTolerance < 0)
            throw new ArgumentException("Comparison tolerances must be finite and non-negative.");
        if (referenceKeys.Except(candidateKeys).Any() || candidateKeys.Except(referenceKeys).Any()) throw new ArgumentException("Reference and candidate key sets differ; compare the same physical stations/quantities.");
        var lookup = candidateKeys.Select((k, i) => (k, v: candidate[i])).ToDictionary(x => x.k, x => x.v, StringComparer.Ordinal);
        return referenceKeys.Select((key, i) =>
        {
            double r = reference[i], c = lookup[key], d = c - r;
            if (!double.IsFinite(d)) throw new ArgumentException("Comparison difference overflow.");
            return new ComparisonRow(key, r, c, d, r == 0 ? (d == 0 ? 0 : double.NaN) : d / Math.Abs(r), Math.Abs(d) <= absoluteTolerance + relativeTolerance * Math.Abs(r));
        }).ToArray();
    }
}
