using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

/// <summary>Arc-length based bridge geometry in mm, independent of Grasshopper and Straus.</summary>
public static class SimpleBridgeBuilder
{
    public const double JoinTolerance = 1e-5; // mm; numerical coincidence, not a meshing/engineering tolerance.
    private const int MaximumStations = 20000;

    public static SimpleBridgeGeometry Build(SimpleBridgeInput input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        using var curve = input.Alignment?.DuplicateCurve();
        return new Builder(input with { Alignment = curve }).Build();
    }

    private sealed class Builder
    {
        private readonly SimpleBridgeInput p;
        private readonly Curve alignment;
        private readonly double length;
        private readonly double[] levels, widths, skews, slopeStations, slopes;
        private readonly Dictionary<double, Plane> frames = new();
        private readonly SimpleBridgeGeometry output = new();
        private readonly Dictionary<(long, long, long), List<int>> nodeBuckets = new();
        private readonly HashSet<(int, int)> edges = new();
        private readonly Dictionary<(BridgeMemberFamily, int, int), int> counters = new();
        private double[][] joints, supports;

        internal Builder(SimpleBridgeInput input)
        {
            p = input ?? throw new ArgumentNullException(nameof(input));
            if (p.Alignment == null || !p.Alignment.IsValid || p.Alignment.IsClosed)
                throw new ArgumentException("Alignment must be a valid open 3D curve, in mm.");
            alignment = p.Alignment;
            length = alignment.GetLength();
            Positive(length, "Alignment length");
            if (alignment.GetNextDiscontinuity(Continuity.G1_continuous, alignment.Domain.T0, alignment.Domain.T1, out double kink)
                && kink < alignment.Domain.T1 - 1e-12)
                throw new ArgumentException("Alignment must have a continuous tangent; replace polyline corners with tangent curves.");
            if (p.GirderOffsets == null || p.GirderOffsets.Length < 2 || p.GirderOffsets.Length > 32 ||
                p.GirderOffsets.Any(v => !double.IsFinite(v)) || p.GirderOffsets.Zip(p.GirderOffsets.Skip(1), (a, b) => b - a).Any(d => d <= JoinTolerance))
                throw new ArgumentException("Provide 2..32 strictly increasing Girder Offsets (positive towards local left).");
            Stations(p.Supports, "Support stations", 2);
            levels = Expand(p.GirderLevels, p.GirderOffsets.Length, "Girder levels");
            widths = Expand(p.DeckWidths, 2, "Deck widths");
            foreach (double w in widths) Positive(w, "Deck widths");
            Positive(p.DeckThickness, "Deck thickness");
            if (p.GirderOffsets[0] < -widths[1] || p.GirderOffsets[^1] > widths[0])
                throw new ArgumentException("Girder axes must lie within the left/right deck widths.");
            if (levels.Any(v => v >= -p.DeckThickness))
                throw new ArgumentException("Girder levels must be below the deck soffit (negative normal offsets from the top reference).");
            Positive(p.MaximumElementLength, "Maximum element length"); Positive(p.ChordTolerance, "Chord tolerance");
            skews = Expand(p.SupportSkewDegrees, p.Supports.Length, "Support skew");
            if (skews.Any(v => Math.Abs(v) >= 75)) throw new ArgumentException("Absolute support skew must be less than 75 degrees.");
            slopes = p.CrossfallPercent ?? throw new ArgumentException("Provide crossfall.");
            if (slopes.Length == 0 || slopes.Any(v => !double.IsFinite(v) || Math.Abs(v) > 30))
                throw new ArgumentException("Crossfall must be finite and within +/-30 percent.");
            slopeStations = p.CrossfallStations ?? Array.Empty<double>();
            if (slopes.Length > 1)
            {
                Stations(slopeStations, "Crossfall stations", 2);
                if (slopes.Length != slopeStations.Length || slopeStations[0] > JoinTolerance || slopeStations[^1] < length - JoinTolerance)
                    throw new ArgumentException("Variable crossfall requires matching stations covering the full alignment.");
            }
            else if (slopeStations.Length > 0) throw new ArgumentException("Constant crossfall requires one value without station pairs.");
            if (p.Segments == null || p.Diaphragms == null) throw new ArgumentException("Null segment or diaphragm definition.");
            if (!double.IsFinite(p.Segments.MaximumLength) || p.Segments.MaximumLength < 0 || p.Segments.Joints == null)
                throw new ArgumentException("Segment maximum length must be zero (one segment) or positive.");
            foreach (var branch in p.Segments.Joints)
            {
                if (branch.Key < 0 || branch.Key > levels.Length) throw new ArgumentException("Joint branch must be 0 (common) or a girder number 1..N.");
                Stations(branch.Value, "Joint stations", 0);
            }
            Stations(p.Diaphragms.Stations, "Diaphragm stations", 0);
            if (!double.IsFinite(p.Diaphragms.MaximumSpacing) || p.Diaphragms.MaximumSpacing < 0)
                throw new ArgumentException("Diaphragm spacing must be zero (explicit/supports only) or positive.");
            if (p.Truss)
            {
                Positive(p.Diaphragms.TrussHeight, "Truss height");
                if (p.Diaphragms.PanelsPerBay < 1 || p.Diaphragms.PanelsPerBay > 100 || !Enum.IsDefined(p.Diaphragms.Pattern))
                    throw new ArgumentException("Truss requires 1..100 panels per bay and a valid X/V/Warren pattern.");
                if (levels.Max() + p.Diaphragms.TrussHeight / 2 >= -p.DeckThickness)
                    throw new ArgumentException("Truss top chord reaches the slab; lower girder levels or reduce truss height.");
            }
        }

        internal SimpleBridgeGeometry Build()
        {
            joints = new double[levels.Length][]; supports = new double[levels.Length][];
            var stationSet = new List<double> { 0, length };
            for (int g = 0; g < levels.Length; g++)
            {
                var explicitJoints = p.Segments.Joints.GetValueOrDefault(0, Array.Empty<double>())
                    .Concat(p.Segments.Joints.GetValueOrDefault(g + 1, Array.Empty<double>())).ToArray();
                joints[g] = Canonical(new[] { 0d, length }.Concat(explicitJoints.Length > 0 ? explicitJoints :
                    Divide(0, length, p.Segments.MaximumLength)));
                for (int j = 0; j + 1 < joints[g].Length; j++)
                    output.Segments.Add(new(g + 1, j + 1, joints[g][j], joints[g][j + 1]));
                foreach (var (s, j) in joints[g].Skip(1).SkipLast(1).Select((s, j) => (s, j)))
                    output.Joints.Add(new(g + 1, j + 1, s, Girder(g, s)));
                supports[g] = p.Supports.Select((s, i) => Intersect(g, s, skews[i])).ToArray();
                if (supports[g].Zip(supports[g].Skip(1), (a, b) => b - a).Any(d => d <= JoinTolerance))
                    throw new ArgumentException("Skew support lines overlap or reverse order on a girder.");
                for (int i = 0; i < supports[g].Length; i++)
                    output.Supports.Add(new(i + 1, g + 1, supports[g][i], Girder(g, supports[g][i])));
                stationSet.AddRange(joints[g]); stationSet.AddRange(supports[g]);
            }
            var diaphragmStations = p.Diaphragms.Stations.ToList();
            if (diaphragmStations.Count == 0 && p.Diaphragms.MaximumSpacing > 0)
            {
                var limits = Canonical(new[] { 0d, length }.Concat(p.Supports));
                for (int i = 0; i + 1 < limits.Length; i++)
                    diaphragmStations.AddRange(Divide(limits[i], limits[i + 1], p.Diaphragms.MaximumSpacing)
                        .Where(s => p.Diaphragms.AtSupports || !p.Supports.Any(v => Math.Abs(v - s) <= JoinTolerance)));
            }
            if (p.Diaphragms.AtSupports) diaphragmStations.AddRange(p.Supports);
            var diaphragms = Canonical(diaphragmStations);
            var attachments = diaphragms.Select(s =>
            {
                int support = Array.FindIndex(p.Supports, x => Math.Abs(x - s) <= JoinTolerance);
                return Enumerable.Range(0, levels.Length).Select(g => Intersect(g, s, support < 0 ? 0 : skews[support])).ToArray();
            }).ToArray();
            stationSet.AddRange(attachments.SelectMany(s => s));
            stationSet.AddRange(slopeStations);
            // Preserve NURBS span boundaries before adaptive chord checks.
            for (int i = 1; i < alignment.SpanCount; i++)
                stationSet.Add(alignment.GetLength(new Interval(alignment.Domain.T0, alignment.SpanDomain(i).T0)));
            var required = Canonical(stationSet);
            var meshStations = new List<double> { required[0] };
            for (int i = 0; i + 1 < required.Length; i++) Refine(required[i], required[i + 1], meshStations, 0);
            output.Stations.AddRange(meshStations);
            foreach (double s in meshStations) output.Frames.Add(Frame(s));
            for (int g = 0; g < levels.Length; g++)
                for (int i = 0; i + 1 < meshStations.Count; i++)
                {
                    double a = meshStations[i], b = meshStations[i + 1], middle = (a + b) / 2;
                    Add(BridgeMemberFamily.Girder, g + 1, Interval(joints[g], middle), Interval(supports[g], middle),
                        Girder(g, a), Girder(g, b), a, b);
                }
            for (int d = 0; d < diaphragms.Length; d++) Diaphragm(d + 1, diaphragms[d], attachments[d]);
            Deck(meshStations);
            output.Diagnostics.Add($"Geometry only, mm: {p.Supports.Length - 1} spans, {levels.Length} girders, {diaphragms.Length} diaphragm lines, {output.Segments.Count} girder segments.");
            output.Diagnostics.Add($"{output.Members.Count} axes, {output.Nodes.Count} nodes. No properties, loads, restraints, releases or rigid links assigned.");
            output.Diagnostics.Add("Girder levels are reference axes, not section centroids. Posts in truss diaphragms are geometric members, not implicit rigid connections.");
            if (p.Supports[0] > JoinTolerance || p.Supports[^1] < length - JoinTolerance)
                output.Diagnostics.Add("Alignment extends outside the support stations: overhangs have Span=0 or Span=number of supports.");
            if (p.Truss && p.Diaphragms.Pattern == BridgeTrussPattern.X && !p.Diaphragms.ConnectCrossings)
                output.Diagnostics.Add("X diagonal crossings are not nodes. Do not remesh/intersect them when assigning beam properties unless a connection is intended.");
            return output;
        }

        private static void Positive(double value, string name)
        { if (!double.IsFinite(value) || value <= JoinTolerance) throw new ArgumentException(name + " must be finite and positive."); }

        private static double[] Expand(double[] values, int count, string name)
        {
            if (values == null || (values.Length != 1 && values.Length != count) || values.Any(v => !double.IsFinite(v)))
                throw new ArgumentException($"{name}: provide one value or {count} finite values.");
            return values.Length == 1 ? Enumerable.Repeat(values[0], count).ToArray() : values.ToArray();
        }

        private void Stations(double[] values, string name, int minimum)
        {
            if (values == null || values.Length < minimum || values.Length > MaximumStations ||
                values.Any(v => !double.IsFinite(v) || v < 0 || v > length + JoinTolerance) ||
                values.Zip(values.Skip(1), (a, b) => b - a).Any(d => d <= JoinTolerance))
                throw new ArgumentException($"{name}: ordered unique distances within 0..{length:G9} mm required (minimum {minimum}).");
        }

        private double[] Canonical(IEnumerable<double> values)
        {
            var result = new List<double>();
            foreach (double s in values.OrderBy(x => x))
            {
                double v = Math.Clamp(s, 0, length);
                if (result.Count == 0 || v - result[^1] > JoinTolerance) result.Add(v);
            }
            if (result.Count > MaximumStations) throw new ArgumentException("Too many stations; increase spacing or simplify the alignment.");
            return result.ToArray();
        }

        private static int Interval(double[] boundaries, double station) => boundaries.Count(s => s < station);
        private static IEnumerable<double> Divide(double a, double b, double maximum)
        {
            int count = maximum == 0 ? 1 : (int)Math.Min(MaximumStations + 1d, Math.Ceiling((b - a) / maximum));
            if (count > MaximumStations) throw new ArgumentException("Requested spacing creates too many stations.");
            for (int i = 0; i <= count; i++) yield return a + (b - a) * i / count;
        }

        private Plane Frame(double s)
        {
            if (frames.TryGetValue(s, out var frame)) return frame;
            if (!alignment.LengthParameter(Math.Clamp(s, 0, length), out double parameter)) throw new ArgumentException("Cannot locate alignment station.");
            var tangent = alignment.TangentAt(parameter); tangent.Unitize();
            var left = Vector3d.CrossProduct(Vector3d.ZAxis, tangent);
            if (left.Length < 1e-4) throw new ArgumentException("Alignment tangent is vertical or undefined; transverse frame is ambiguous.");
            left.Unitize();
            var up = Vector3d.CrossProduct(tangent, left); up.Unitize();
            double slope = slopes[0];
            if (slopes.Length > 1)
            {
                int i = Math.Clamp(Array.FindIndex(slopeStations, x => x >= s) - 1, 0, slopes.Length - 2);
                if (s >= slopeStations[^1]) i = slopes.Length - 2;
                double t = Math.Clamp((s - slopeStations[i]) / (slopeStations[i + 1] - slopeStations[i]), 0, 1);
                slope = (1 - t) * slopes[i] + t * slopes[i + 1];
            }
            double angle = Math.Atan(slope / 100);
            var transverse = Math.Cos(angle) * left + Math.Sin(angle) * up;
            // Plane X = transverse left, Y = deck normal, Z = alignment tangent.
            frame = new Plane(alignment.PointAt(parameter), transverse, Vector3d.CrossProduct(tangent, transverse));
            frames.Add(s, frame);
            return frame;
        }

        private Point3d Girder(int g, double s) => Frame(s).PointAt(p.GirderOffsets[g], levels[g]);
        private Point3d[] Section(double s)
        {
            var f = Frame(s);
            return Enumerable.Range(0, levels.Length).Select(g => Girder(g, s))
                .Concat(new[] { f.PointAt(widths[0], 0), f.PointAt(-widths[1], 0),
                    f.PointAt(widths[0], -p.DeckThickness), f.PointAt(-widths[1], -p.DeckThickness) }).ToArray();
        }

        private void Refine(double a, double b, List<double> stations, int depth)
        {
            var left = Section(a); var right = Section(b);
            bool split = left.Zip(right, (x, y) => x.DistanceTo(y)).Any(d => d > p.MaximumElementLength + JoinTolerance);
            foreach (double t in new[] { .25, .5, .75 })
            {
                var sample = Section(a + t * (b - a));
                for (int i = 0; i < sample.Length; i++)
                    if (sample[i].DistanceTo(new Line(left[i], right[i]).PointAt(t)) > p.ChordTolerance) split = true;
            }
            var tangent = Frame((a + b) / 2).ZAxis;
            if (left.Zip(right, (x, y) => (y - x) * tangent).Any(d => d <= 0))
                throw new ArgumentException("Offset geometry folds or reverses; reduce bridge width/offsets or increase alignment radius.");
            if (split)
            {
                if (depth >= 24) throw new ArgumentException("Discretization cannot satisfy length/chord tolerance.");
                double middle = (a + b) / 2;
                Refine(a, middle, stations, depth + 1); Refine(middle, b, stations, depth + 1);
            }
            else stations.Add(b);
            if (stations.Count > MaximumStations) throw new ArgumentException("Too many geometry stations; relax discretization.");
        }

        private double Intersect(int girder, double station, double skew)
        {
            if (Math.Abs(skew) < 1e-12) return station;
            var f = Frame(station); double angle = skew * Math.PI / 180;
            var normal = Math.Cos(angle) * f.ZAxis - Math.Sin(angle) * f.XAxis;
            double Distance(double s) => (Girder(girder, s) - f.Origin) * normal;
            var roots = new List<double>(); double a = 0, fa = Distance(a);
            for (int i = 1; i <= 256; i++)
            {
                double b = length * i / 256, fb = Distance(b);
                if (Math.Abs(fa) <= JoinTolerance) roots.Add(a);
                if (fa * fb < 0)
                {
                    double low = a, high = b, fl = fa;
                    for (int j = 0; j < 64 && high - low > JoinTolerance / 10; j++)
                    {
                        double mid = (low + high) / 2, fm = Distance(mid);
                        if (fl * fm <= 0) high = mid; else { low = mid; fl = fm; }
                    }
                    roots.Add((low + high) / 2);
                }
                a = b; fa = fb;
            }
            if (Math.Abs(fa) <= JoinTolerance) roots.Add(length);
            var unique = Canonical(roots);
            if (unique.Length != 1)
                throw new ArgumentException($"Skew line at {station:G9} mm must intersect girder {girder + 1} once; found {unique.Length}. Extend the alignment at skew ends or correct the layout.");
            return unique[0];
        }

        private int Node(Point3d point)
        {
            long X(double x) => (long)Math.Floor(x / JoinTolerance);
            var key = (X(point.X), X(point.Y), X(point.Z));
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (nodeBuckets.TryGetValue((key.Item1 + x, key.Item2 + y, key.Item3 + z), out var bucket))
                    foreach (int id in bucket) if (output.Nodes[id - 1].DistanceTo(point) <= JoinTolerance) return id;
            int next = output.Nodes.Count + 1; output.Nodes.Add(point);
            if (!nodeBuckets.TryGetValue(key, out var list)) nodeBuckets.Add(key, list = new());
            list.Add(next); return next;
        }

        private void Add(BridgeMemberFamily family, int group, int segment, int span, Point3d a, Point3d b, double sa, double sb)
        {
            if (!a.IsValid || !b.IsValid || a.DistanceTo(b) <= JoinTolerance) throw new ArgumentException("Degenerate member in bridge layout.");
            int n1 = Node(a), n2 = Node(b);
            if (n1 == n2) throw new ArgumentException("Member collapses within the node coincidence tolerance.");
            if (!edges.Add((Math.Min(n1, n2), Math.Max(n1, n2)))) throw new ArgumentException("Duplicate member in bridge layout.");
            var key = (family, group, segment); int index = counters.GetValueOrDefault(key) + 1; counters[key] = index;
            string id = $"{family}:{group:D2}:{segment:D2}:{index:D4}";
            output.Members.Add(new(id, family, group, segment, span, n1, n2,
                new Line(output.Nodes[n1 - 1], output.Nodes[n2 - 1]), sa, sb));
            if (output.Members.Count > 200000) throw new ArgumentException("Geometry exceeds 200000 members; simplify discretization.");
        }

        private void Diaphragm(int number, double station, double[] attach)
        {
            var centers = Enumerable.Range(0, levels.Length).Select(g => Girder(g, attach[g])).ToArray();
            if (!p.Truss)
            {
                for (int g = 0; g + 1 < levels.Length; g++)
                    Add(BridgeMemberFamily.SolidDiaphragm, number, g + 1, 0, centers[g], centers[g + 1], attach[g], attach[g + 1]);
                return;
            }
            var halfHeight = Frame(station).YAxis * (p.Diaphragms.TrussHeight / 2);
            for (int g = 0; g < centers.Length; g++)
            {
                Add(BridgeMemberFamily.Post, number, g + 1, 0, centers[g] - halfHeight, centers[g], attach[g], attach[g]);
                Add(BridgeMemberFamily.Post, number, g + 1, 0, centers[g], centers[g] + halfHeight, attach[g], attach[g]);
            }
            for (int g = 0; g + 1 < centers.Length; g++)
            {
                int count = p.Diaphragms.PanelsPerBay;
                var axis = new Line(centers[g], centers[g + 1]);
                for (int i = 0; i < count; i++)
                {
                    var a = axis.PointAt((double)i / count); var b = axis.PointAt((double)(i + 1) / count);
                    var ta = a + halfHeight; var tb = b + halfHeight;
                    var ba = a - halfHeight; var bb = b - halfHeight;
                    void Member(BridgeMemberFamily family, Point3d start, Point3d end) => Add(family, number, g + 1, 0, start, end, station, station);
                    Member(BridgeMemberFamily.TopChord, ta, tb);
                    if (p.Diaphragms.Pattern == BridgeTrussPattern.V)
                    {
                        var mid = new Line(ba, bb).PointAt(.5);
                        Member(BridgeMemberFamily.BottomChord, ba, mid); Member(BridgeMemberFamily.BottomChord, mid, bb);
                        Member(BridgeMemberFamily.Diagonal, ta, mid); Member(BridgeMemberFamily.Diagonal, tb, mid);
                    }
                    else
                    {
                        Member(BridgeMemberFamily.BottomChord, ba, bb);
                        if (p.Diaphragms.Pattern == BridgeTrussPattern.X && p.Diaphragms.ConnectCrossings)
                        {
                            var mid = new Line(ta, bb).PointAt(.5);
                            Member(BridgeMemberFamily.Diagonal, ta, mid); Member(BridgeMemberFamily.Diagonal, mid, bb);
                            Member(BridgeMemberFamily.Diagonal, ba, mid); Member(BridgeMemberFamily.Diagonal, mid, tb);
                        }
                        else
                        {
                            Member(BridgeMemberFamily.Diagonal, i % 2 == 0 ? ba : ta, i % 2 == 0 ? tb : bb);
                            if (p.Diaphragms.Pattern == BridgeTrussPattern.X) Member(BridgeMemberFamily.Diagonal, i % 2 == 0 ? ta : ba, i % 2 == 0 ? bb : tb);
                        }
                    }
                    if (i > 0 && p.Diaphragms.Pattern != BridgeTrussPattern.Warren) Member(BridgeMemberFamily.Post, ba, ta);
                }
            }
        }

        private void Deck(List<double> stations)
        {
            var mesh = new Mesh();
            mesh.Vertices.UseDoublePrecisionVertices = true;
            var left = new Polyline(); var right = new Polyline();
            foreach (double s in stations)
            {
                var f = Frame(s);
                var l = f.PointAt(widths[0], 0); var r = f.PointAt(-widths[1], 0);
                left.Add(l); right.Add(r);
                foreach (var v in new[] { l, r, f.PointAt(-widths[1], -p.DeckThickness), f.PointAt(widths[0], -p.DeckThickness) }) mesh.Vertices.Add(v);
            }
            for (int i = 0; i + 1 < stations.Count; i++)
                for (int side = 0; side < 4; side++)
                    mesh.Faces.AddFace(4 * i + side, 4 * i + (side + 1) % 4, 4 * (i + 1) + (side + 1) % 4, 4 * (i + 1) + side);
            mesh.Faces.AddFace(3, 2, 1, 0);
            int last = mesh.Vertices.Count - 4; mesh.Faces.AddFace(last, last + 1, last + 2, last + 3);
            mesh.Normals.ComputeNormals(); mesh.Compact();
            if (!mesh.IsValid || !mesh.IsClosed) throw new ArgumentException("Cannot form a closed deck mesh.");
            output.Deck = mesh; output.LeftEdge = left; output.RightEdge = right; output.DeckThickness = p.DeckThickness;
        }
    }
}
