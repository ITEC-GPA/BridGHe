using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public enum BeamMomentSign { Positive, Negative, Mixed, Zero, Missing }

/// <summary>Classification of sampled FEM moments, not an automatic code cracking assessment.</summary>
public sealed record BeamMomentState(BeamModel Source, BeamMomentSign Sign, double Minimum, double Maximum, string Case)
{
    public override string ToString() => $"{Source.BeamId} | {Sign} | {Minimum:G6} .. {Maximum:G6} N mm | {Case}";
}

public static class BeamMomentClassification
{
    public static BeamMomentState[] Classify(IReadOnlyList<BeamModel> beams, string loadCase, double tolerance = 1000, int component = 1, int sign = 1)
    {
        if (beams == null || beams.Any(b => b == null || string.IsNullOrWhiteSpace(b.BeamId)) || beams.Select(b => b.BeamId).Distinct(StringComparer.Ordinal).Count() != beams.Count)
            throw new ArgumentException("Provide non-null beams with unique IDs.");
        if (string.IsNullOrWhiteSpace(loadCase) || !double.IsFinite(tolerance) || tolerance < 0 || component is < 0 or > 1 || sign is not (1 or -1))
            throw new ArgumentException("Specify one case name, finite non-negative tolerance [N mm], component 0=BM1 or 1=BM2 and sign +1/-1.");
        return beams.Select(b =>
        {
            var samples = b.Results.OfType<BeamForceResultModel>().Where(r => r.LoadCase?.Name == loadCase).ToArray();
            if (samples.Length == 0) return new BeamMomentState(b, BeamMomentSign.Missing, double.NaN, double.NaN, loadCase);
            if (samples.Select(s => (s.StepNum, s.StepType)).Distinct().Count() != 1) throw new ArgumentException($"Beam {b.BeamId}: select a single result step.");
            var stations = samples.Select(s => s.NormalizedLength ? s.Station * b.Length : s.Station).OrderBy(s => s).ToArray();
            double eps = Math.Max(1e-6, b.Length * 1e-9);
            if (!(b.Length > 0) || stations.Any(s => !double.IsFinite(s) || s < -eps || s > b.Length + eps) ||
                Math.Abs(stations[0]) > eps || Math.Abs(stations[^1] - b.Length) > eps || stations.Zip(stations.Skip(1)).Any(p => p.Second - p.First <= eps))
                throw new ArgumentException($"Beam {b.BeamId}: distinct FEM stations covering both ends are required.");
            var moments = samples.Select(s => sign * (component == 1 ? s.My : s.Mx)).ToArray();
            if (moments.Any(v => !double.IsFinite(v))) throw new ArgumentException($"Beam {b.BeamId}: non-finite moment.");
            double min = moments.Min(), max = moments.Max();
            var state = min < -tolerance ? (max > tolerance ? BeamMomentSign.Mixed : BeamMomentSign.Negative) :
                max > tolerance ? BeamMomentSign.Positive : BeamMomentSign.Zero;
            return new BeamMomentState(b, state, min, max, loadCase);
        }).ToArray();
    }
}
