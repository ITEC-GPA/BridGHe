using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public sealed record BridgeAlignmentInputsResult(Curve Alignment, double Length, double[] Supports, double[] Spans,
    double StartOverhang, double EndOverhang, double[] Joints, Point3d[] SupportPoints, string[] Information);

/// <summary>Normalize distances along an open curve; derive exact inputs for Bridge Parametric Layout.</summary>
public static class BridgeAlignmentInputs
{
    public static BridgeAlignmentInputsResult Create(Curve alignment, IEnumerable<double> supports = null, int spanCount = 3,
        bool endSupports = true, IEnumerable<double> joints = null, double tolerance = 1)
    {
        if (alignment == null || !alignment.IsValid || alignment.IsClosed)
            throw new ArgumentException("Supply one valid, open alignment curve in mm.");
        double length = alignment.GetLength();
        if (!double.IsFinite(length) || length <= 0 || !double.IsFinite(tolerance) || tolerance < 0 || tolerance >= length / 2)
            throw new ArgumentException("Curve length must be positive; station tolerance must be non-negative and smaller than half the curve length.");
        var info = new List<string> { "Stations are 3D arc lengths in mm from the curve start, not curve parameters or horizontal projections. Curve direction defines station zero." };
        double[] Normalize(IEnumerable<double> source, string kind, bool ends)
        {
            var raw = (source ?? Array.Empty<double>()).ToArray();
            if (raw.Length > 10000 || raw.Any(s => !double.IsFinite(s) || s < -tolerance || s > length + tolerance))
                throw new ArgumentException($"{kind}: use finite stations between 0 and {length:G12} mm (maximum 10000 entries).");
            var snapped = raw.Select(s => Math.Abs(s) <= tolerance ? 0 : Math.Abs(s - length) <= tolerance ? length : s).ToList();
            int adjusted = raw.Zip(snapped, (a, b) => a != b).Count(b => b);
            if (ends) { snapped.Add(0); snapped.Add(length); }
            var ordered = new List<double>();
            foreach (double s in snapped.OrderBy(s => s)) if (ordered.Count == 0 || s - ordered[^1] > tolerance) ordered.Add(s);
            int merged = snapped.Count - ordered.Count;
            if (adjusted > 0) info.Add($"{kind}: {adjusted} station(s) snapped to a curve end within tolerance {tolerance:G6} mm.");
            if (merged > 0) info.Add($"{kind}: repeated stations within {tolerance:G6} mm merged, retaining the lower station. End supports may already be in the input.");
            return ordered.ToArray();
        }
        var explicitSupports = supports?.ToArray() ?? Array.Empty<double>();
        double[] stations;
        if (explicitSupports.Length == 0)
        {
            if (spanCount is < 1 or > 100) throw new ArgumentException("With no support stations, Span Count must be 1..100.");
            stations = Enumerable.Range(0, spanCount + 1).Select(i => i == spanCount ? length : length * i / spanCount).ToArray();
            info.Add($"No support stations supplied: {spanCount} equal spans over the whole curve; both ends are supports.");
        }
        else stations = Normalize(explicitSupports, "Support Stations", endSupports);
        if (stations.Length < 2 || stations.Length > 101)
            throw new ArgumentException("Provide at least two distinct support stations (or enable End Supports), and at most 100 spans.");
        var spans = stations.Zip(stations.Skip(1), (a, b) => b - a).ToArray();
        var jointStations = Normalize(joints, "Joint Stations", false).Where(s => s > 0 && s < length).ToArray();
        var points = new List<Point3d>();
        foreach (double s in stations)
        {
            if (s == 0) points.Add(alignment.PointAtStart);
            else if (s == length) points.Add(alignment.PointAtEnd);
            else if (alignment.LengthParameter(s, out double t)) points.Add(alignment.PointAt(t));
            else throw new ArgumentException($"Cannot locate station {s:G12} on the alignment.");
        }
        info.Add($"{stations.Length} supports, {spans.Length} spans. Curve length {length:G12} mm; overhangs {stations[0]:G12} / {length - stations[^1]:G12} mm.");
        info.Add("Fabrication joints and FEM mesh remain separate. Changing fabrication ranges requires updating the Excel section table.");
        return new(alignment.DuplicateCurve(), length, stations, spans, stations[0], length - stations[^1], jointStations, points.ToArray(), info.ToArray());
    }
}
