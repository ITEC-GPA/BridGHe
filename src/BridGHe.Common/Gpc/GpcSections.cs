using System;
using GPC.Geometry;
using GPC.Model.Sections;

namespace BridGHe.Common.Gpc
{
    /// <summary>BridGHe section families (former SectionModel.SectionTypes, same names and order).</summary>
    public enum BridgeSectionKind
    {
        Generic, SolidCircle, HollowCircle, SolidRectangle, HollowRectangle, I, T, C, Angle, Omega, Z, GenericShapes, Composite
    }

    /// <summary>GPC sections from BridGHe dimensions, in N-mm. Native Straus7 dimension order (review C3, L2 question 4):
    /// I section B1/T1 bottom flange, B2/T2 top flange, D depth, T3 web, as the former exporter sent them to Straus7.</summary>
    public static class GpcSections
    {
        public static ISectionShape Standard(BridgeSectionKind kind, string name, double d, double b, double b1, double b2,
            double t, double t1, double t2, double t3)
        {
            switch (kind)
            {
                case BridgeSectionKind.SolidCircle: return new SectionCircular(d, name ?? "");
                case BridgeSectionKind.HollowCircle: return new SectionCHS(d, t, name ?? "");
                case BridgeSectionKind.SolidRectangle: return new SectionRectangular(d, b, name ?? "");
                // Straus7 bsSquareHollow {B, D, -, T1 flanges, T2 webs}.
                case BridgeSectionKind.HollowRectangle: return new SectionRHS(d, b, t1, t1, t2, t2, name ?? "");
                // Straus7 bsISection {B1 bottom, B2 top, D, T1 bottom, T2 top, T3 web}.
                case BridgeSectionKind.I: return new SectionH(d, t3, b2, t2, b1, t1, name ?? "");
                default: throw new NotSupportedException("BridgeSectionKindNotMappedYet: " + kind);
            }
        }

        /// <summary>Explicit properties without outline (former Generic and Composite types). The shear-centre offsets are
        /// principal-axis offsets from the centroid, as the former exporter wrote them (ipSL1/ipSL2).</summary>
        public static Section Explicit(string name, double area, double i11, double i22, double j, double centroidX, double centroidY,
            double shearOffset1, double shearOffset2, double angleRad)
        {
            double c = Math.Cos(angleRad), s = Math.Sin(angleRad);
            // Inverse of the writer rotation: offsets (o1, o2) in principal axes = R(-angle) (dx, dy).
            double dx = c * shearOffset1 - s * shearOffset2, dy = s * shearOffset1 + c * shearOffset2;
            return new Section(area, i11, i22, j, 0, new Point2d(centroidX, centroidY), new Point3d(centroidX + dx, centroidY + dy, 0), angleRad, name ?? "");
        }
    }
}
