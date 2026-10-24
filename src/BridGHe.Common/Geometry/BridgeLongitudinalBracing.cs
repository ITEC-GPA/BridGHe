using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public static class BridgeLongitudinalBracing
{
    public static SimpleBridgeGeometry Add(SimpleBridgeGeometry source, IReadOnlyList<double> stations,
        IReadOnlyList<int> bays, BridgeTrussPattern pattern, double normalOffset, bool connectX, IReadOnlyList<int> omittedPanels = null)
    {
        BridgeGeometryTools.Validate(source);
        double start=source.Stations[0], end=source.Stations[^1], tolerance=SimpleBridgeBuilder.JoinTolerance;
        if (stations == null || stations.Count < 2 || stations.Count > 1001 || stations.Any(s => !double.IsFinite(s) || s < start-tolerance || s > end+tolerance))
            throw new ArgumentException("Provide 2..1001 increasing bracing panel stations within Bridge Geometry, in mm.");
        // The builder merges coincident end stations. Use that same tolerance when a
        // separately computed alignment length lies just outside the canonical grid.
        stations=stations.Select(s=>Math.Clamp(s,start,end)).ToArray();
        if(stations.Zip(stations.Skip(1)).Any(p=>p.Second-p.First<=tolerance))
            throw new ArgumentException("Provide 2..1001 increasing bracing panel stations within Bridge Geometry, in mm.");
        if (!Enum.IsDefined(pattern) || !double.IsFinite(normalOffset)) throw new ArgumentException("Invalid bracing pattern/normal offset.");
        int girders = source.Segments.Max(s => s.Girder);
        bays = bays == null || bays.Count == 0 ? Enumerable.Range(1, girders - 1).ToArray() : bays;
        if (bays.Any(b => b < 1 || b >= girders) || bays.Distinct().Count() != bays.Count)
            throw new ArgumentException("Bay IDs must be distinct, 1..number of girders-1.");
        omittedPanels ??= Array.Empty<int>();
        if (omittedPanels.Any(p => p < 1 || p >= stations.Count)) throw new ArgumentException("Invalid omitted panel number.");
        using var editor = new BridgeGeometryEditor(source);
        int groupBase = source.Members.Where(m => m.Family is BridgeMemberFamily.LongitudinalBracing or BridgeMemberFamily.BracingPost)
            .Select(m => m.Group).DefaultIfEmpty(0).Max();
        var attachments = new Dictionary<(int Girder, int Station), Point3d>();
        Point3d Attach(int girder, int station)
        {
            if (attachments.TryGetValue((girder, station), out var point)) return point;
            var basePoint = BridgeGeometryTools.GirderPoint(source, girder, stations[station]);
            editor.SplitAt(basePoint);
            point = basePoint + normalOffset * BridgeGeometryTools.FrameAt(source, stations[station]).YAxis;
            if (Math.Abs(normalOffset) > SimpleBridgeBuilder.JoinTolerance)
                editor.Add($"BracingPost:{groupBase + girder}:{station + 1}", BridgeMemberFamily.BracingPost, groupBase + girder, station + 1, basePoint, point, stations[station], stations[station]);
            attachments.Add((girder, station), point); return point;
        }
        foreach (int bay in bays)
            for (int i = 0; i < stations.Count - 1; i++)
            {
                if (omittedPanels.Contains(i + 1)) continue;
                var a = Attach(bay, i); var b = Attach(bay + 1, i);
                var c = Attach(bay, i + 1); var d = Attach(bay + 1, i + 1);
                int member = 0;
                void Add(Point3d p, Point3d q, double s0, double s1) => editor.Add(
                    $"LongBracing:{groupBase + bay}:{i + 1}:{++member}", BridgeMemberFamily.LongitudinalBracing, groupBase + bay, i + 1, p, q, s0, s1);
                if (pattern == BridgeTrussPattern.X)
                {
                    if (connectX)
                    {
                        var ad = new Line(a, d); var bc = new Line(b, c);
                        if (!Rhino.Geometry.Intersect.Intersection.LineLine(ad, bc, out double u, out double v,
                            SimpleBridgeBuilder.JoinTolerance, true) || ad.PointAt(u).DistanceTo(bc.PointAt(v)) > SimpleBridgeBuilder.JoinTolerance ||
                            u <= 0 || u >= 1 || v <= 0 || v >= 1)
                            throw new ArgumentException("X diagonals do not intersect in 3D; disable Connect X or revise bracing geometry.");
                        var x = ad.PointAt(u); double s = stations[i] + u * (stations[i + 1] - stations[i]);
                        Add(a, x, stations[i], s); Add(x, d, s, stations[i + 1]);
                        Add(b, x, stations[i], s); Add(x, c, s, stations[i + 1]);
                    }
                    else { Add(a, d, stations[i], stations[i + 1]); Add(b, c, stations[i], stations[i + 1]); }
                }
                else if (pattern == BridgeTrussPattern.Warren)
                    Add(i % 2 == 0 ? a : b, i % 2 == 0 ? d : c, stations[i], stations[i + 1]);
                else
                {
                    var middle = new Line(c, d).PointAt(.5);
                    editor.SplitAt(middle);
                    Add(c, middle, stations[i + 1], stations[i + 1]); Add(middle, d, stations[i + 1], stations[i + 1]);
                    Add(a, middle, stations[i], stations[i + 1]); Add(b, middle, stations[i], stations[i + 1]);
                }
            }
        editor.Geometry.Diagnostics.Add("Longitudinal bracing: family 6 diagonals/closure members; family 7 physical offset posts. Assign their actual mechanical properties explicitly.");
        return editor.Geometry;
    }
}
