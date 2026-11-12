using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using Rhino.Geometry;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// C3 (11/11, gap 2 of lane R2a): the preview geometry of a GPC draft, made from the GPC data alone (display only, never an analysis
    /// input), with the Rhino operations of the FEM preview one for one, so that the GPC-only path draws what the FEM value drew:
    /// <list type="bullet">
    /// <item>the section solids of a beam draft (<see cref="Beam"/>): the outline of its section recipe (standard shapes, Generic as the
    /// equivalent circle, the shapes of a Brep section), turned by the BridGHe angle, moved by the
    /// BridGHe offset, mirrored for a double C or angle, extruded along the beam or swept between the two ends of a taper (the former
    /// <c>BeamModel.CreateShapes</c>);</item>
    /// <item>the solids and the schematic bar lines of a composite beam from its two composite section definitions (<see cref="Composite"/>,
    /// <see cref="CompositeBars"/>: the former <c>CompositeBeamModel.CreateShapes</c> and the bar lines of <c>ModelPreview</c>).</item>
    /// </list>
    /// P11 (P2D, user decision U5 of 12/11): a beam draft on a homogenised composite property (a Generic recipe with
    /// <see cref="LegacySectionRecipe.CompositePreview"/>) draws the steel and the slab of the outline its recipe holds
    /// (<see cref="LegacySectionRecipe.CompositeOutline"/>, the former preview geometry of the property) and the schematic bar lines
    /// (<see cref="Bars"/>), as the FEM beam drew them; nothing when the recipe holds no outline (a recipe written before P2D). A native Straus7
    /// section (library, BXS, BGL, standard key) draws no solid: its contours came from the vendor mesh, which the GPC data do not hold (user
    /// decision U5: not drawn). The drafts are in definition units, as the document.
    /// </summary>
    public static class GpcDraftShapes
    {
        private const double Tolerance = 0.001;

        /// <summary>True for a beam draft on a homogenised composite property (no outline in the GPC data: <see cref="Beam"/> gives none).</summary>
        public static bool HasCompositePreview(LegacyBeamDraft d) => d?.Property?.Section is { } r && r.NativeKey == null && r.Kind == BridgeSectionKind.Generic && r.CompositePreview;

        private static Point3d P(GPC.Geometry.Point3d p) => new Point3d(p.X, p.Y, p.Z);

        /// <summary>The section frame of the FEM preview (<c>BeamModel.GetLocalAxes(false)</c>): the BridGHe axes of the beam turned by
        /// <paramref name="rotation"/> (rad) about the beam axis, from the start point.</summary>
        private static Transform Frame(Point3d from, Point3d to, double rotation)
        {
            Vector3d axes3 = to - from; axes3.Unitize();
            Vector3d axes1, axes2;
            Vector3d v = axes3; v.Unitize();
            if (!(Vector3d.CrossProduct(Vector3d.ZAxis, v).Length < 1.0E-12))
            {
                axes1 = Vector3d.CrossProduct(Vector3d.ZAxis, axes3); axes1.Unitize();
                axes2 = Vector3d.CrossProduct(axes3, axes1); axes2.Unitize();
            }
            else
            {
                axes2 = Vector3d.CrossProduct(axes3, Vector3d.YAxis); axes2.Unitize();
                axes1 = Vector3d.CrossProduct(axes2, axes3); axes1.Unitize();
            }
            var plane = new Plane(from, axes1, axes2);
            plane.Rotate(rotation, plane.Normal);
            return Transform.PlaneToPlane(Plane.WorldXY, plane);
        }

        /// <summary>The area centroid of a closed polygon (x, y; the first point repeated or not).</summary>
        private static (double X, double Y, double A) Centroid(IReadOnlyList<(double x, double y)> p)
        {
            double a = 0, cx = 0, cy = 0; int n = p.Count;
            if (n > 1 && p[0] == p[n - 1]) n--;
            for (int i = 0; i < n; i++)
            {
                var (x0, y0) = p[i]; var (x1, y1) = p[(i + 1) % n];
                double cross = x0 * y1 - x1 * y0;
                a += cross; cx += (x0 + x1) * cross; cy += (y0 + y1) * cross;
            }
            a /= 2;
            return a == 0 ? (0, 0, 0) : (cx / (6 * a), cy / (6 * a), a);
        }

        private static Brep Planar(Curve curve) => Brep.CreatePlanarBreps(curve, Tolerance)?.FirstOrDefault();
        private static Brep Planar(IEnumerable<Curve> curves) => Brep.CreatePlanarBreps(curves, Tolerance)?.FirstOrDefault();
        private static PolylineCurve Polyline(IEnumerable<(double x, double y)> points) => new PolylineCurve(points.Select(p => new Point3d(p.x, p.y, 0)));

        /// <summary>The centroid of the I outline of the FEM preview from the bottom fibre (the steel I of a composite section).</summary>
        private static double ICentroidY(double d, double b1, double t1, double b2, double t2, double t3) =>
            Centroid(IOutline(d, b1, t1, b2, t2, t3, 0)).Y;

        /// <summary>The I outline of the FEM preview with the centroid at <paramref name="cy"/> above the bottom fibre (<c>BeamModel</c>, case I).</summary>
        private static (double x, double y)[] IOutline(double d, double b1, double t1, double b2, double t2, double t3, double cy) => new[]
        {
            (-b1 / 2, -cy), (b1 / 2, -cy), (b1 / 2, -cy + t1), (t3 / 2, -cy + t1), (t3 / 2, -cy + d - t2), (b2 / 2, -cy + d - t2), (b2 / 2, -cy + d),
            (-b2 / 2, -cy + d), (-b2 / 2, -cy + d - t2), (-t3 / 2, -cy + d - t2), (-t3 / 2, -cy + t1), (-b1 / 2, -cy + t1), (-b1 / 2, -cy),
        };

        /// <summary>The planar section surfaces of a recipe, in the section plane with the centroid at the origin, as the FEM preview made them;
        /// null: nothing to draw (no section, a Z or composite kind, a homogenised composite property, a native section without its definition).</summary>
        private static List<Brep> SectionSurfaces(LegacyBeamDraft d, out double principalAngle)
        {
            principalAngle = 0;
            var r = d.Property?.Section;
            if (r == null) return null;
            if (HasCompositePreview(d))
            {
                // P11 (P2D): the steel I and the slab of the outline, as the former preview geometry made them (CompositeSectionPreviewGeometry
                // .Outlines: the I with its centroid at the origin, the slab on its top flange), turned as the Generic section of the property.
                var o = r.CompositeOutline;
                if (o == null) return null;
                principalAngle = r.Angle;
                double cy = ICentroidY(o.D, o.B1, o.T1, o.B2, o.T2, o.T3), top = o.D - cy;
                var parts = new List<Brep>();
                void Part(Brep b) { if (b != null) parts.Add(b); }
                Part(Planar(Polyline(IOutline(o.D, o.B1, o.T1, o.B2, o.T2, o.T3, cy))));
                Part(Planar(Polyline(new[] { (-o.Width / 2, top), (o.Width / 2, top), (o.Width / 2, top + o.Height), (-o.Width / 2, top + o.Height), (-o.Width / 2, top) })));
                return parts;
            }
            var surfaces = new List<Brep>();
            void Add(Brep b) { if (b != null) surfaces.Add(b); }
            // A native Straus7 section (library, BXS, BGL or a standard key): the FEM section held the contours of the vendor mesh, made by
            // Straus7 when the section was computed (StrausSection.Calculate); the GPC data hold the definition without its mesh, which only a
            // native resolution gives: not drawn (open point of C3, with the homogenised composite property).
            if (r.NativeKey != null) return null;
            switch (r.Kind)
            {
                case BridgeSectionKind.SolidCircle:
                    Add(Planar(new ArcCurve(new Circle(r.D / 2)))); break;
                case BridgeSectionKind.HollowCircle:
                    Add(Planar(new Curve[] { new ArcCurve(new Circle(r.D / 2)), new ArcCurve(new Circle(r.D / 2 - r.T)) })); break;
                case BridgeSectionKind.SolidRectangle:
                    Add(Planar(Polyline(new[] { (-r.B / 2, -r.D / 2), (r.B / 2, -r.D / 2), (r.B / 2, r.D / 2), (-r.B / 2, r.D / 2), (-r.B / 2, -r.D / 2) }))); break;
                case BridgeSectionKind.HollowRectangle:
                    Add(Planar(new Curve[]
                    {
                        Polyline(new[] { (-r.B / 2, -r.D / 2), (r.B / 2, -r.D / 2), (r.B / 2, r.D / 2), (-r.B / 2, r.D / 2), (-r.B / 2, -r.D / 2) }),
                        Polyline(new[] { (-r.B / 2 + r.T2, -r.D / 2 + r.T1), (r.B / 2 - r.T2, -r.D / 2 + r.T1), (r.B / 2 - r.T2, r.D / 2 - r.T1), (-r.B / 2 + r.T2, r.D / 2 - r.T1), (-r.B / 2 + r.T2, -r.D / 2 + r.T1) }),
                    }));
                    break;
                case BridgeSectionKind.I:
                    Add(Planar(Polyline(IOutline(r.D, r.B1, r.T1, r.B2, r.T2, r.T3, ICentroidY(r.D, r.B1, r.T1, r.B2, r.T2, r.T3))))); break;
                case BridgeSectionKind.T:
                {
                    // The T of the former components (no lips: L = 0): the web T2 under the flange B × T1, the centroid of that T, the point
                    // list of the FEM preview (its first flange point at B/2 − T3).
                    (double x, double y)[] T(double cy, double t3) => new[]
                    {
                        (-r.T2 / 2, -cy), (r.T2 / 2, -cy), (r.T2 / 2, -cy + r.D - r.T1), (r.B / 2 - t3, -cy + r.D - r.T1), (r.B / 2, -cy + r.D), (-r.B / 2, -cy + r.D),
                        (-r.B / 2, -cy + r.D - r.T1), (-r.T2 / 2, -cy + r.D - r.T1), (-r.T2 / 2, -cy),
                    };
                    var regular = new[] { (-r.T2 / 2, 0d), (r.T2 / 2, 0d), (r.T2 / 2, r.D - r.T1), (r.B / 2, r.D - r.T1), (r.B / 2, r.D), (-r.B / 2, r.D), (-r.B / 2, r.D - r.T1), (-r.T2 / 2, r.D - r.T1) };
                    Add(Planar(Polyline(T(Centroid(regular).Y, r.T3)))); break;
                }
                case BridgeSectionKind.C:
                case BridgeSectionKind.Omega:
                {
                    (double x, double y)[] C(double cx, double cy)
                    {
                        var p = new List<(double x, double y)> { (-cx, -cy) };
                        void Step(double dx, double dy) { var last = p[^1]; p.Add((last.x + dx, last.y + dy)); }
                        Step(0, r.D); Step(r.B, 0); Step(0, -r.T1); Step(-r.B + r.T2, 0); Step(0, -r.D + r.T1 * 2); Step(r.B - r.T2, 0); Step(0, -r.T1);
                        p.Add(p[0]); return p.ToArray();
                    }
                    var c0 = Centroid(C(0, 0)); Add(Planar(Polyline(C(c0.X, c0.Y)))); break;
                }
                case BridgeSectionKind.Angle:
                {
                    (double x, double y)[] L(double cx, double cy)
                    {
                        var p = new List<(double x, double y)> { (-cx, -cy) };
                        void Step(double dx, double dy) { var last = p[^1]; p.Add((last.x + dx, last.y + dy)); }
                        Step(0, r.D); Step(r.T2, 0); Step(0, -r.D + r.T1); Step(r.B - r.T2, 0); Step(0, -r.T1); Step(-r.B, 0);
                        p.Add(p[0]); return p.ToArray();
                    }
                    var c0 = Centroid(L(0, 0)); Add(Planar(Polyline(L(c0.X, c0.Y)))); break;
                }
                case BridgeSectionKind.Generic:
                    // The equivalent circle of the FEM preview; the principal angle of the explicit section.
                    principalAngle = r.Angle;
                    Add(Planar(new ArcCurve(new Circle(Math.Sqrt(r.Area / Math.PI))))); break;
                case BridgeSectionKind.GenericShapes:
                {
                    if (r.Shapes == null || r.Shapes.Count == 0) return null;
                    // The FEM section moved its shapes to the corner of their bounding box and took their centroid and principal angle there
                    // (SectionModel.CalculateSection: MoveToMin, GetGeometricalDatas with the major axes, an angle below −0.01 turned by π), so the
                    // outline was the shapes less their own area centroid, turned by that angle: the same operations on the shapes of the recipe.
                    static IEnumerable<(double x, double y)> Pts(IReadOnlyList<double> xy) { for (int i = 0; i + 1 < xy.Count; i += 2) yield return (xy[i], xy[i + 1]); }
                    static GPC.Geometry.Polygon2d Polygon(IReadOnlyList<double> xy) { var p = new GPC.Geometry.Polygon2d(); foreach (var (x, y) in Pts(xy)) p.Add(x, y); return p; }
                    var moved = new Geometry.Shapes(r.Shapes.Select(s => new GPC.Geometry.Shape2d(Polygon(s.Fill), s.Holes.Count == 0 ? null : s.Holes.Select(Polygon).ToArray())));
                    var min = moved.GetBBox().Min;
                    moved.MoveToMin();
                    moved.GetGeometricalDatas(true, 0.000000001, out double xG, out double yG, out _, out _, out _, out _, out _, out _, out double angle, out _, out _, out _, out _, out _, out _);
                    if (angle < -0.01) angle += Math.PI;
                    double cx = min.X + xG, cy = min.Y + yG;
                    principalAngle = angle;
                    foreach (var s in r.Shapes)
                    {
                        var curves = new List<Curve>();
                        (double, double) Moved((double x, double y) p) => (p.x - cx, p.y - cy);
                        var fill = Pts(s.Fill).Select(Moved).ToList(); fill.Add(fill[0]); curves.Add(Polyline(fill));
                        foreach (var h in s.Holes) { var hole = Pts(h).Select(Moved).ToList(); hole.Add(hole[0]); curves.Add(Polyline(hole)); }
                        Add(Planar(curves));
                    }
                    break;
                }
                default:
                    return null; // Z and Composite: the FEM preview drew nothing.
            }
            return surfaces;
        }

        /// <summary>The section solids of a beam draft (see the class); none when it has nothing to draw.</summary>
        public static Brep[] Beam(LegacyBeamDraft d)
        {
            if (d?.Beam == null) return Array.Empty<Brep>();
            Point3d from = P(d.Beam.StartPoint), to = P(d.Beam.EndPoint);
            if (!from.IsValid || !to.IsValid || from.DistanceTo(to) <= 0) return Array.Empty<Brep>();
            var main = SectionSurfaces(d, out double principalAngle);
            if (main == null || main.Count == 0) return Array.Empty<Brep>();
            var section = d.Property.Section;
            var offset = d.Beam.Assignments?.OffsetI ?? new GPC.Geometry.Vector3d(0, 0, 0);
            double rotation = d.AngleDeg / 180 * Math.PI - principalAngle;
            var t = Frame(from, to, rotation);
            var shapes = new List<Brep>();
            var taper = d.Beam.Assignments?.Physical?.Taper;
            if (taper == null)
            {
                foreach (var surface in main)
                {
                    surface.Translate(offset.X, offset.Y, 0);
                    var surfaces = new List<Brep> { surface };
                    if (section.Mirror != LegacySectionRecipe.NoMirror)
                    {
                        // The FEM preview moved the single section by its centroid and half gap A before mirroring it about the y axis: the
                        // centroid cancels (the outline starts at −centroid), so the single section of the recipe gives the same solids.
                        double cx = SingleCentroidX(section);
                        surface.Translate(cx + section.MirrorGapA / 2, 0, 0);
                        var mirrored = surface.DuplicateBrep();
                        mirrored.Transform(Transform.Mirror(Point3d.Origin, Vector3d.XAxis));
                        surfaces.Add(mirrored);
                    }
                    foreach (var s in surfaces)
                    {
                        s.Transform(t);
                        var shape = s.Faces[0].CreateExtrusion(new LineCurve(from, to), true);
                        if (shape == null) continue;
                        if (shape.SolidOrientation == BrepSolidOrientation.Inward) shape.Flip();
                        shapes.Add(shape);
                    }
                }
            }
            else
            {
                foreach (var surface in main)
                {
                    var fromSurface = surface.DuplicateBrep(); fromSurface.MakeDeformable(); fromSurface.Transform(Transform.Scale(Plane.WorldXY, taper.ScaleI1, taper.ScaleI2, 1));
                    var toSurface = surface.DuplicateBrep(); toSurface.MakeDeformable(); toSurface.Transform(Transform.Scale(Plane.WorldXY, taper.ScaleJ1, taper.ScaleJ2, 1));
                    fromSurface.Translate(offset.X, offset.Y, 0); toSurface.Translate(offset.X, offset.Y, 0);
                    fromSurface.Transform(t); toSurface.Transform(t);
                    var line = new LineCurve(from, to);
                    toSurface.Transform(Transform.Translation(to - from));
                    var parts = new List<Brep>();
                    for (int i = 0; i < fromSurface.Loops.Count; i++)
                    {
                        var sweep = new SweepOneRail { GlobalShapeBlending = true };
                        parts.AddRange(sweep.PerformSweep(line, new Curve[] { fromSurface.Loops[i].To3dCurve(), toSurface.Loops[i].To3dCurve() }));
                    }
                    parts.Add(fromSurface); parts.Add(toSurface);
                    var join = Brep.JoinBreps(parts, 0.001);
                    if (join != null && join.Length > 0) shapes.Add(join[0]);
                }
            }
            return shapes.ToArray();
        }

        /// <summary>P11 (P2D, user decision U5 of 12/11): the schematic bar lines of a beam draft on a homogenised composite property with its
        /// outline: three lines per row given (area &gt; 0) along the beam, at x = (i − 1)·width/3 and at the cover from the top or the bottom
        /// of the slab, moved by the BridGHe offset and turned as the section (the former <c>ModelPreview.SectionReinforcementLines</c> of a beam
        /// with a composite preview); none otherwise.</summary>
        public static Line[] Bars(LegacyBeamDraft d)
        {
            var r = d?.Property?.Section; var o = HasCompositePreview(d) ? r.CompositeOutline : null;
            if (o == null || d.Beam == null) return Array.Empty<Line>();
            Point3d from = P(d.Beam.StartPoint), to = P(d.Beam.EndPoint);
            if (!from.IsValid || !to.IsValid || from.DistanceTo(to) <= 0) return Array.Empty<Line>();
            double cy = ICentroidY(o.D, o.B1, o.T1, o.B2, o.T2, o.T3);
            var offset = d.Beam.Assignments?.OffsetI ?? new GPC.Geometry.Vector3d(0, 0, 0);
            var t = Frame(from, to, d.AngleDeg / 180 * Math.PI - r.Angle); var length = to - from;
            var lines = new List<Line>();
            foreach (bool top in new[] { true, false })
                if ((top ? o.TopArea : o.BottomArea) > 0)
                    for (int i = 0; i < 3; i++)
                    {
                        var a = new Point3d((i - 1) * o.Width / 3, o.D - cy + (top ? o.Height - o.TopCover : o.BottomCover), 0);
                        a += new Vector3d(offset.X, offset.Y, 0);
                        var z = a;
                        a.Transform(t); z.Transform(t); z += length;
                        lines.Add(new Line(a, z));
                    }
            return lines.ToArray();
        }

        /// <summary>The x of the centroid of the single C or angle of a recipe (from its outer web face), as <see cref="SectionSurfaces"/> uses it.</summary>
        private static double SingleCentroidX(LegacySectionRecipe r)
        {
            var p = new List<(double x, double y)> { (0, 0) };
            void Step(double dx, double dy) { var last = p[^1]; p.Add((last.x + dx, last.y + dy)); }
            if (r.Kind == BridgeSectionKind.Angle) { Step(0, r.D); Step(r.T2, 0); Step(0, -r.D + r.T1); Step(r.B - r.T2, 0); Step(0, -r.T1); Step(-r.B, 0); }
            else { Step(0, r.D); Step(r.B, 0); Step(0, -r.T1); Step(-r.B + r.T2, 0); Step(0, -r.D + r.T1 * 2); Step(r.B - r.T2, 0); Step(0, -r.T1); }
            return Centroid(p).X;
        }

        /// <summary>The steel I and the slab of a composite section definition, as the FEM composite beam read them from its property.</summary>
        private readonly record struct CompositeOutline(double D, double B1, double T1, double B2, double T2, double T3, double CentroidY, double Width, double Height,
            double TopArea, double TopCover, double BottomArea, double BottomCover);

        private static CompositeOutline Outline(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition definition)
        {
            var section = definition.Section; var h = (GPC.Model.Sections.SectionH)section.SteelSections[0].Section.SectionShape; var rows = definition.RebarRows;
            double d = h.Height, b1 = h.LenghtBottomFlange, t1 = h.ThicknessBottomFlange, b2 = h.LenghtTopFlange, t2 = h.ThicknessTopFlange, t3 = h.ThicknessWeb;
            return new CompositeOutline(d, b1, t1, b2, t2, t3, ICentroidY(d, b1, t1, b2, t2, t3), section.Width, section.Height,
                rows?.TopAreaMm2 ?? 0, rows?.TopCoverMm ?? 0, rows?.BottomAreaMm2 ?? 0, rows?.BottomCoverMm ?? 0);
        }

        /// <summary>The solids of a composite beam from its beam draft and its two definitions: the steel swept from the I of the start to the I
        /// of the end, then the slab likewise (the former <c>CompositeBeamModel.CreateShapes</c>: steel first, then concrete); none without the
        /// two definitions.</summary>
        public static Brep[] Composite(LegacyBeamDraft beam, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition start, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition end)
        {
            if (beam?.Beam == null || start == null || end == null) return Array.Empty<Brep>();
            Point3d from = P(beam.Beam.StartPoint), to = P(beam.Beam.EndPoint);
            if (!from.IsValid || !to.IsValid || from.DistanceTo(to) <= 0) return Array.Empty<Brep>();
            var a = Outline(start); var b = Outline(end);
            Brep Steel(CompositeOutline o) => Planar(Polyline(IOutline(o.D, o.B1, o.T1, o.B2, o.T2, o.T3, o.CentroidY)));
            Brep Slab(CompositeOutline o)
            {
                var slab = Planar(Polyline(new[] { (-o.Width / 2, -o.Height / 2), (o.Width / 2, -o.Height / 2), (o.Width / 2, o.Height / 2), (-o.Width / 2, o.Height / 2), (-o.Width / 2, -o.Height / 2) }));
                slab?.Translate(0, o.D - o.CentroidY + o.Height / 2, 0);
                return slab;
            }
            var t = Frame(from, to, 0);
            var shapes = new List<Brep>();
            foreach (var (s0, s1) in new[] { (Steel(a), Steel(b)), (Slab(a), Slab(b)) })
            {
                if (s0 == null || s1 == null) continue;
                var fromSurface = s0.DuplicateBrep(); fromSurface.MakeDeformable();
                var toSurface = s1.DuplicateBrep(); toSurface.MakeDeformable();
                fromSurface.Transform(t); toSurface.Transform(t);
                var line = new LineCurve(from, to);
                toSurface.Transform(Transform.Translation(to - from));
                var parts = new List<Brep>();
                for (int i = 0; i < fromSurface.Loops.Count; i++)
                {
                    var sweep = new SweepOneRail { GlobalShapeBlending = true };
                    parts.AddRange(sweep.PerformSweep(line, new Curve[] { fromSurface.Loops[i].To3dCurve(), toSurface.Loops[i].To3dCurve() }));
                }
                parts.Add(fromSurface); parts.Add(toSurface);
                var join = Brep.JoinBreps(parts, 0.001);
                if (join != null && join.Length > 0) shapes.Add(join[0]);
            }
            return shapes.ToArray();
        }

        /// <summary>The schematic bar lines of a composite beam: three lines per row given (area &gt; 0) at the start or the end, from the point of
        /// the start section to that of the end section (the former <c>CompositeSectionPreviewGeometry.RebarPoint</c>: at x = (i − 1)·width/3,
        /// at the cover from the top or the bottom of the slab); none without the two definitions.</summary>
        public static Line[] CompositeBars(LegacyBeamDraft beam, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition start, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition end)
        {
            if (beam?.Beam == null || start == null || end == null) return Array.Empty<Line>();
            Point3d from = P(beam.Beam.StartPoint), to = P(beam.Beam.EndPoint);
            if (!from.IsValid || !to.IsValid || from.DistanceTo(to) <= 0) return Array.Empty<Line>();
            var a = Outline(start); var b = Outline(end);
            static Point3d Bar(CompositeOutline o, bool top, int i) => new Point3d((i - 1) * o.Width / 3, o.D - o.CentroidY + (top ? o.Height - o.TopCover : o.BottomCover), 0);
            static bool Row(CompositeOutline o, bool top) => (top ? o.TopArea : o.BottomArea) > 0;
            var t = Frame(from, to, 0); var length = to - from;
            var lines = new List<Line>();
            foreach (bool top in new[] { true, false })
                if (Row(a, top) || Row(b, top))
                    for (int i = 0; i < 3; i++)
                    {
                        var p0 = Bar(a, top, i); var p1 = Bar(b, top, i);
                        p0.Transform(t); p1.Transform(t); p1 += length;
                        lines.Add(new Line(p0, p1));
                    }
            return lines.ToArray();
        }
    }
}
