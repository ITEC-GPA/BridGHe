using System;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// P2CE (13/11/2026, phase 2b): the casts of the Beam Section type on GPC data: a text (CHS, SHS, RHS, I, C, L, R, T, SC, 2xC, 2xL with their
    /// dimensions) and a curve (a circle, a closed planar polyline), with the rules of the former FEM cast (<c>GH_BeamSection.CastFrom</c>: the
    /// same prefixes in the same order, the same parsing and the same refusals), the recipe made by the section builders (the recipe the former
    /// cast gave through the conversion of its FEM section, checked equal by p2ce-reader while the FEM section existed).
    /// </summary>
    internal static class GpcSectionCasts
    {
        private const int MirrorLeft = 2; // the former MirrorTypes.Left

        private static double[] Values(string text, bool lower) =>
            (lower ? text.ToLower() : text).Split(new char[] { 'x' }).Select(s => Convert.ToDouble(s)).ToArray();

        /// <summary>The cast of a text: false when the former cast refused it (recipe null); true with the recipe otherwise.</summary>
        internal static bool Text(string text, out LegacySectionRecipe recipe)
        {
            recipe = null;
            if (text == null) return false;
            try
            {
                if (text.StartsWith("CHS")) { var v = Values(text.Substring(3), true); recipe = GpcSectionBuilders.Standard(BridgeSectionKind.HollowCircle, v[0], t: v[1]); return true; }
                if (text.StartsWith("SHS")) { var v = Values(text.Substring(3), true); recipe = GpcSectionBuilders.Standard(BridgeSectionKind.HollowRectangle, v[0], b: v[0], t1: v[1], t2: v[1]); return true; }
                if (text.StartsWith("RHS"))
                {
                    var v = Values(text.Substring(3), true);
                    if (v.Length != 3 && v.Length != 4) return false;
                    recipe = GpcSectionBuilders.Standard(BridgeSectionKind.HollowRectangle, v[0], b: v[1], t1: v[2], t2: v.Length == 4 ? v[3] : v[2]); return true;
                }
                if (text.StartsWith("I")) { var v = Values(text.Substring(1), true); recipe = GpcSectionBuilders.Standard(BridgeSectionKind.I, v[0], b1: v[1], b2: v[2], t1: v[3], t2: v[4], t3: v[5]); return true; }
                if (text.StartsWith("C")) { var v = Values(text.Substring(1), true); recipe = GpcSectionBuilders.Standard(BridgeSectionKind.C, v[0], b: v[1], t1: v[2], t2: v[3]); return true; }
                if (text.StartsWith("L")) { var v = Values(text.Substring(1), true); recipe = GpcSectionBuilders.Standard(BridgeSectionKind.Angle, v[0], b: v[1], t1: v[2], t2: v[3]); return true; }
                if (text.StartsWith("R")) { var v = Values(text.ToLower().Substring(1), false); recipe = GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, v[0], b: v[1]); return true; }
                if (text.StartsWith("T")) { var v = Values(text.Substring(1), false); recipe = GpcSectionBuilders.Standard(BridgeSectionKind.T, v[0], b: v[1], t1: v[2], t2: v[3]); return true; }
                if (text.StartsWith("SC")) { recipe = GpcSectionBuilders.Standard(BridgeSectionKind.SolidCircle, Convert.ToInt32(text.Substring(2).Trim())); return true; }
                if (text.StartsWith("2x"))
                {
                    string sec = text.Substring(2);
                    if (sec.StartsWith("C")) { var v = Values(sec.Substring(1), true); recipe = GpcSectionBuilders.Standard(BridgeSectionKind.C, v[0], b: v[1], t1: v[2], t2: v[3], mirror: MirrorLeft, gapA: v[4]); return true; }
                    if (sec.StartsWith("L")) { var v = Values(sec.Substring(1), true); recipe = GpcSectionBuilders.Standard(BridgeSectionKind.Angle, v[0], b: v[1], t1: v[2], t2: v[3], mirror: MirrorLeft, gapA: v[4]); return true; }
                }
                return false;
            }
            catch (Exception) { recipe = null; return false; }
        }

        /// <summary>The cast of a curve: a circle gives a solid circle; a closed planar polyline of three or more points, moved to the world XY
        /// plane by the plane of its first three points, gives a section of generic shapes; another curve that is not an arc is accepted with
        /// no section (as the former cast: true, an invalid section); an arc that is not a circle is refused.</summary>
        internal static bool Curve(Curve curve, out LegacySectionRecipe recipe)
        {
            recipe = null;
            if (curve is ArcCurve circle)
            {
                if (!circle.IsCircle()) return false;
                recipe = GpcSectionBuilders.Standard(BridgeSectionKind.SolidCircle, circle.Radius * 2);
                return true;
            }
            if (curve.IsPolyline() && curve.IsClosed && curve.IsPlanar() && curve.TryGetPolyline(out Polyline pline) && pline.Count > 2)
            {
                var pl = new Plane(pline[0], pline[1], pline[2]);
                pline.Transform(Transform.PlaneToPlane(pl, Plane.WorldXY));
                var fill = new GPC.Geometry.Polygon2d();
                foreach (Point3d p in pline) fill.Add(p.X, p.Y);
                recipe = GpcSectionBuilders.Shapes(new[] { new GPC.Geometry.Shape2d(fill, null) });
            }
            return true;
        }
    }
}
