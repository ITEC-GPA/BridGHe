using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public sealed record BridgeSpanAlternative(double[] Stations, Point3d[] Points, double[] Lengths, double Score);
public sealed record BridgeSpanAdvice(BridgeSpanAlternative[] Alternatives, int CandidateCount, string[] Diagnostics);

/// <summary>Exact k-shortest paths on a sampled station grid; geometric feasibility, no strength design.</summary>
public static class BridgeSpanAdvisor
{
    public static BridgeSpanAdvice Suggest(Curve alignment, IReadOnlyList<Curve> forbidden,
        double minimum, double maximum, double target, double grid, double clearance = 0,
        int alternatives = 3, double pierPenalty = 1, double endRatio = 1,
        IReadOnlyList<double> required = null)
    {
        if (alignment == null || !alignment.IsValid || alignment.IsClosed)
            throw new ArgumentException("Provide a valid open alignment in mm.");
        double length = alignment.GetLength();
        if (!double.IsFinite(length) || length <= 0 || new[] { minimum, maximum, target, grid }.Any(v => !double.IsFinite(v) || v <= 0)
            || minimum > maximum || target < minimum || target > maximum)
            throw new ArgumentException("Require positive grid and 0 < minimum <= target <= maximum, in mm.");
        if (!double.IsFinite(clearance) || clearance < 0 || !double.IsFinite(pierPenalty) || pierPenalty < 0 ||
            !double.IsFinite(endRatio) || endRatio <= 0 || endRatio > 1 || alternatives < 1 || alternatives > 10)
            throw new ArgumentException("Require clearance/penalty >= 0, 0 < end ratio <= 1, and 1..10 alternatives.");
        if (length / grid > 3000) throw new ArgumentException("At most 3000 grid intervals; increase the candidate grid spacing.");
        required ??= Array.Empty<double>(); forbidden ??= Array.Empty<Curve>();
        if (required.Any(s => !double.IsFinite(s) || s < 0 || s > length))
            throw new ArgumentException("Required support stations must lie on the alignment.");
        var regions = new List<Curve>();
        try
        {
            foreach (var curve in forbidden)
            {
                if (curve == null || !curve.IsValid || !curve.IsClosed || !curve.TryGetPlane(out var plane, 1e-6) ||
                    Math.Abs(plane.Normal.Z) < 1 - 1e-9)
                    throw new ArgumentException("Forbidden regions must be closed horizontal planar curves. Their XY footprint excludes support reference points.");
                var projected = curve.DuplicateCurve();
                projected.Transform(Transform.PlanarProjection(Plane.WorldXY)); regions.Add(projected);
                using var intersections = Rhino.Geometry.Intersect.Intersection.CurveSelf(projected, 1e-6);
                if (intersections != null && intersections.Count > 0)
                    throw new ArgumentException("Forbidden region boundaries must not self-intersect.");
            }
            Point3d Point(double station)
            {
                if (!alignment.LengthParameter(station, out double t)) throw new ArgumentException("Cannot evaluate alignment station.");
                return alignment.PointAt(t);
            }
            bool Allowed(double station)
            {
                var p = Point(station); p.Z = 0;
                foreach (var region in regions)
                {
                    var containment = region.Contains(p, Plane.WorldXY, 1e-6);
                    if (containment != PointContainment.Outside) return false;
                    if (!region.ClosestPoint(p, out double t)) throw new ArgumentException("Cannot evaluate forbidden region distance.");
                    if (p.DistanceTo(region.PointAt(t)) <= clearance + 1e-6) return false;
                }
                return true;
            }
            var mandatory = required.Append(0).Append(length).Distinct().OrderBy(s => s).ToArray();
            foreach (double s in mandatory)
                if (!Allowed(s)) throw new ArgumentException($"Required support at {s:G9} mm is inside a forbidden region or its clearance. Extend/trim the alignment or revise the constraint.");
            var stations = Enumerable.Range(0, (int)Math.Floor(length / grid) + 1).Select(i => i * grid)
                .Concat(mandatory).Distinct().OrderBy(s => s).Where(Allowed).ToArray();
            var paths = new List<(double Score, int[] Nodes)>[stations.Length];
            paths[0] = new() { (0, new[] { 0 }) };
            for (int j = 1; j < stations.Length; j++)
            {
                var choices = new List<(double Score, int[] Nodes)>();
                double previousRequired = mandatory.Last(s => s < stations[j]);
                for (int i = j - 1; i >= 0; i--)
                {
                    double span = stations[j] - stations[i];
                    if (span > maximum + 1e-6 || stations[i] < previousRequired - 1e-6) break;
                    if (span < minimum - 1e-6 || paths[i] == null) continue;
                    double desired = target * (i == 0 || j == stations.Length - 1 ? endRatio : 1);
                    double cost = Math.Pow((span - desired) / target, 2) + (j == stations.Length - 1 ? 0 : pierPenalty);
                    foreach (var path in paths[i])
                        choices.Add((path.Score + cost, path.Nodes.Append(j).ToArray()));
                }
                paths[j] = choices.OrderBy(p => p.Score).ThenBy(p => p.Nodes.Length)
                    .ThenBy(p => string.Join(",", p.Nodes.Select(n => n.ToString("D4"))), StringComparer.Ordinal)
                    .Take(alternatives).ToList();
            }
            var result = paths[^1].Select(p =>
            {
                var s = p.Nodes.Select(n => stations[n]).ToArray();
                return new BridgeSpanAlternative(s, s.Select(Point).ToArray(), s.Zip(s.Skip(1), (a, b) => b - a).ToArray(), p.Score);
            }).ToArray();
            return new(result, stations.Length, new[]
            {
                result.Length == 0 ? "No feasible layout on this candidate grid. Refine the grid or revise span bounds; this is not a proof of continuous infeasibility." :
                    $"{result.Length} layouts, ordered by geometric cost; lengths are arc lengths along the reference alignment.",
                "Cost = sum(((span - targetSpan)/target)^2) + pierPenalty * internalSupports; targetSpan = target * endRatio for end spans.",
                "Forbidden regions apply in XY to support reference points; clearance must account for pier/foundation footprint when required.",
                "No loads, soil, pier heights, structural resistance, deflection or construction checks enter this geometric advice."
            });
        }
        finally { foreach (var region in regions) region.Dispose(); }
    }
}
