using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public enum BeamMomentSign { Positive, Negative, Mixed, Zero, Missing }

/// <summary>Classification of sampled FEM moments, not an automatic code cracking assessment.</summary>
public sealed record BeamMomentState(BeamMomentSign Sign, double Minimum, double Maximum, string Case)
{
    /// <summary>Chain E (d): the analysed GPC beam the state was classified from (its analysed model with the results catalog), when the
    /// beam had it (Decompose Model of an analysed model); the reference of Bridge State Override on GPC. Default otherwise.</summary>
    public (BridGHe.Common.Gpc.GpcResultCatalog Catalog, GPC.Model.Models.Model Model, GPC.Model.Elements.BeamElement Beam) Analysed { get; init; }

    /// <summary>R6.5 P1 lane 1b: the BridGHe ID of the beam (of its GPC draft; P2CE phase 2b: no FEM beam).</summary>
    public string BeamId { get; init; }

    public override string ToString() => $"{BeamId} | {Sign} | {Minimum:G6} .. {Maximum:G6} N mm | {Case}";
}

public static class BeamMomentClassification
{


    /// <summary>P2K (P1): the beams by their IDs and analysed GPC beams alone (the path of Beam Moment Dispatch): no FEM beam read. P2CE
    /// (13/11/2026, phase 1): on GPC alone, without the FEM beams of the overload below (a beam without its analysed GPC beam is Missing, as
    /// before); the same rules and messages.</summary>
    public static BeamMomentState[] Classify(IReadOnlyList<string> ids, IReadOnlyList<(Gpc.GpcResultCatalog Catalog, GPC.Model.Models.Model Model, GPC.Model.Elements.BeamElement Beam)> analysed,
        string loadCase, double tolerance = 1000, int component = 1, int sign = 1)
    {
        Validate(ids, ids?.Count ?? -1, analysed, loadCase, tolerance, component, sign);
        return ids.Select((id, i) =>
        {
            var gpc = analysed?[i] ?? default;
            if (gpc.Catalog == null || gpc.Beam == null) return new BeamMomentState(BeamMomentSign.Missing, double.NaN, double.NaN, loadCase) { BeamId = id };
            return FromGpc(id, gpc, loadCase, tolerance, component, sign);
        }).ToArray();
    }

    /// <summary>The arguments of a classification (the messages of the former checks).</summary>
    private static void Validate(IReadOnlyList<string> ids, int beams, IReadOnlyList<(Gpc.GpcResultCatalog Catalog, GPC.Model.Models.Model Model, GPC.Model.Elements.BeamElement Beam)> analysed,
        string loadCase, double tolerance, int component, int sign)
    {
        if (ids == null || beams < 0 || beams != ids.Count || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new ArgumentException("Provide non-null beams with unique IDs.");
        if (string.IsNullOrWhiteSpace(loadCase) || !double.IsFinite(tolerance) || tolerance < 0 || component is < 0 or > 1 || sign is not (1 or -1))
            throw new ArgumentException("Specify one case name, finite non-negative tolerance [N mm], component 0=BM1 or 1=BM2 and sign +1/-1.");
        if (analysed != null && analysed.Count != beams) throw new ArgumentException("One analysed GPC beam (or none) per beam.");
    }

    /// <summary>The state of a beam from the rows of the Results components of its analysed GPC beam (the case given).</summary>
    private static BeamMomentState FromGpc(string id, (Gpc.GpcResultCatalog Catalog, GPC.Model.Models.Model Model, GPC.Model.Elements.BeamElement Beam) gpc,
        string loadCase, double tolerance, int component, int sign)
    {
        // The rows of the case (the former written-back objects all had step 0 and no step type: one step).
        var rows = Gpc.GpcResultRows.Beam(gpc.Catalog, gpc.Model, gpc.Beam, loadCase).ToArray();
        if (rows.Length == 0) return new BeamMomentState(BeamMomentSign.Missing, double.NaN, double.NaN, loadCase) { Analysed = gpc, BeamId = id };
        double length = gpc.Catalog.Units.FromMm(gpc.Beam.Length);
        var stations = rows.Select(r => r.Station).OrderBy(s => s).ToArray();
        var moments = rows.Select(r => sign * (component == 1 ? r.Bending2 : r.Bending1)).ToArray();
        return State(id, gpc, length, stations, moments, loadCase, tolerance);
    }

    /// <summary>The sign of the sampled moments of a beam (the stations covering both ends, finite values) and its extrema.</summary>
    private static BeamMomentState State(string id, (Gpc.GpcResultCatalog Catalog, GPC.Model.Models.Model Model, GPC.Model.Elements.BeamElement Beam) gpc,
        double length, double[] stations, double[] moments, string loadCase, double tolerance)
    {
        double eps = Math.Max(1e-6, length * 1e-9);
        if (!(length > 0) || stations.Any(s => !double.IsFinite(s) || s < -eps || s > length + eps) ||
            Math.Abs(stations[0]) > eps || Math.Abs(stations[^1] - length) > eps || stations.Zip(stations.Skip(1)).Any(p => p.Second - p.First <= eps))
            throw new ArgumentException($"Beam {id}: distinct FEM stations covering both ends are required.");
        if (moments.Any(v => !double.IsFinite(v))) throw new ArgumentException($"Beam {id}: non-finite moment.");
        double min = moments.Min(), max = moments.Max();
        var state = min < -tolerance ? (max > tolerance ? BeamMomentSign.Mixed : BeamMomentSign.Negative) :
            max > tolerance ? BeamMomentSign.Positive : BeamMomentSign.Zero;
        return new BeamMomentState(state, min, max, loadCase) { Analysed = gpc.Catalog != null && gpc.Beam != null ? gpc : default, BeamId = id };
    }

    /// <summary>R6.5 P1 lane 1b: the same with the BridGHe ID of every beam given (<paramref name="ids"/>), for beams given by their GPC
    /// drafts alone: such a beam has no FEM value (null in <paramref name="beams"/>, the state has no <see cref="BeamMomentState.Source"/>) and
    /// is classified from its analysed GPC beam, or is Missing (no FEM results to read). Same rules and messages.</summary>
}
