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
    public static BeamMomentState[] Classify(IReadOnlyList<BeamModel> beams, string loadCase, double tolerance = 1000, int component = 1, int sign = 1) =>
        Classify(beams, null, loadCase, tolerance, component, sign);

    /// <summary>F6 (a)/(b): with <paramref name="analysed"/> (per beam its analysed GPC model with the results catalog and its GPC beam, or
    /// nulls) a beam is classified from the rows of the Results components of its GPC results (the numbers the former result objects were
    /// written from: station in the definition length, BM1/BM2 as shown), otherwise from its FEM results. Same rules and messages.</summary>
    public static BeamMomentState[] Classify(IReadOnlyList<BeamModel> beams, IReadOnlyList<(Gpc.GpcResultCatalog Catalog, GPC.Model.Models.Model Model, GPC.Model.Elements.BeamElement Beam)> analysed,
        string loadCase, double tolerance = 1000, int component = 1, int sign = 1)
    {
        if (beams == null || beams.Any(b => b == null || string.IsNullOrWhiteSpace(b.BeamId)) || beams.Select(b => b.BeamId).Distinct(StringComparer.Ordinal).Count() != beams.Count)
            throw new ArgumentException("Provide non-null beams with unique IDs.");
        if (string.IsNullOrWhiteSpace(loadCase) || !double.IsFinite(tolerance) || tolerance < 0 || component is < 0 or > 1 || sign is not (1 or -1))
            throw new ArgumentException("Specify one case name, finite non-negative tolerance [N mm], component 0=BM1 or 1=BM2 and sign +1/-1.");
        if (analysed != null && analysed.Count != beams.Count) throw new ArgumentException("One analysed GPC beam (or none) per beam.");
        return beams.Select((b, i) =>
        {
            double length; double[] stations, moments;
            var gpc = analysed?[i] ?? default;
            if (gpc.Catalog != null && gpc.Beam != null)
            {
                // The rows of the case (the former written-back objects all had step 0 and no step type: one step).
                var rows = Gpc.GpcResultRows.Beam(gpc.Catalog, gpc.Model, gpc.Beam).Where(r => gpc.Catalog.Entries[r.Entry].Label == loadCase).ToArray();
                if (rows.Length == 0) return new BeamMomentState(b, BeamMomentSign.Missing, double.NaN, double.NaN, loadCase);
                length = gpc.Catalog.Units.FromMm(gpc.Beam.Length);
                stations = rows.Select(r => r.Station).OrderBy(s => s).ToArray();
                moments = rows.Select(r => sign * (component == 1 ? r.Bending2 : r.Bending1)).ToArray();
            }
            else
            {
                var samples = b.Results.OfType<BeamForceResultModel>().Where(r => r.LoadCase?.Name == loadCase).ToArray();
                if (samples.Length == 0) return new BeamMomentState(b, BeamMomentSign.Missing, double.NaN, double.NaN, loadCase);
                if (samples.Select(s => (s.StepNum, s.StepType)).Distinct().Count() != 1) throw new ArgumentException($"Beam {b.BeamId}: select a single result step.");
                length = b.Length;
                stations = samples.Select(s => s.NormalizedLength ? s.Station * b.Length : s.Station).OrderBy(s => s).ToArray();
                moments = samples.Select(s => sign * (component == 1 ? s.My : s.Mx)).ToArray();
            }
            double eps = Math.Max(1e-6, length * 1e-9);
            if (!(length > 0) || stations.Any(s => !double.IsFinite(s) || s < -eps || s > length + eps) ||
                Math.Abs(stations[0]) > eps || Math.Abs(stations[^1] - length) > eps || stations.Zip(stations.Skip(1)).Any(p => p.Second - p.First <= eps))
                throw new ArgumentException($"Beam {b.BeamId}: distinct FEM stations covering both ends are required.");
            if (moments.Any(v => !double.IsFinite(v))) throw new ArgumentException($"Beam {b.BeamId}: non-finite moment.");
            double min = moments.Min(), max = moments.Max();
            var state = min < -tolerance ? (max > tolerance ? BeamMomentSign.Mixed : BeamMomentSign.Negative) :
                max > tolerance ? BeamMomentSign.Positive : BeamMomentSign.Zero;
            return new BeamMomentState(b, state, min, max, loadCase);
        }).ToArray();
    }
}
