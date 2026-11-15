using System;
using GPC.Geometry;

namespace BridGHe.Common.Gpc
{
    /// <summary>BridGHe beam orientation in GPC terms. The BridGHe angle (degrees) rotates the default frame about the beam
    /// axis; the default frame has axis 1 = Z × axis 3 (horizontal) and axis 2 = axis 3 × axis 1, or, for vertical beams,
    /// axis 2 = axis 3 × Y. The section frame is the principal frame rotated back by the principal angle of the section.
    /// The former exporter wrote the same orientation as the Straus7 reference angle 90° + angle.</summary>
    public static class GpcBeamAxes
    {
        public static bool IsVertical(Point3d from, Point3d to)
        {
            var v = Unit(Delta(from, to));
            return Vector3d.ZAxis.CrossProduct(v).Length < 1.0E-12;
        }

        /// <summary>Principal axes (V1, V2) and beam axis V3, origin at the start point.</summary>
        public static CoordinateSystem Principal(Point3d from, Point3d to, double angleDeg) => Frame(from, to, angleDeg / 180 * Math.PI);

        /// <summary>Section (geometric) axes: the principal axes rotated by −principalAngle about V3.</summary>
        public static CoordinateSystem Section(Point3d from, Point3d to, double angleDeg, double principalAngleRad) =>
            Frame(from, to, angleDeg / 180 * Math.PI - principalAngleRad);

        private static CoordinateSystem Frame(Point3d from, Point3d to, double r)
        {
            var axes3 = Unit(Delta(from, to));
            Vector3d axes1, axes2;
            if (Vector3d.ZAxis.CrossProduct(axes3).Length >= 1.0E-12)
            {
                axes1 = Unit(Vector3d.ZAxis.CrossProduct(axes3));
                axes2 = Unit(axes3.CrossProduct(axes1));
            }
            else
            {
                axes2 = Unit(axes3.CrossProduct(Vector3d.YAxis));
                axes1 = Unit(axes2.CrossProduct(axes3));
            }
            if (r != 0)
            {
                double c = Math.Cos(r), s = Math.Sin(r);
                var a1 = axes1 * c + axes2 * s; var a2 = axes2 * c - axes1 * s;
                axes1 = a1; axes2 = a2;
            }
            return new CoordinateSystem(new Point3d(from.X, from.Y, from.Z), axes1, axes2);
        }

        internal static Vector3d Delta(Point3d from, Point3d to) => new Vector3d(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        internal static Vector3d Unit(Vector3d v)
        {
            double l = v.Length;
            if (!(l > 0) || double.IsInfinity(l)) throw new ArgumentException("DegenerateBeamAxis");
            return new Vector3d(v.X / l, v.Y / l, v.Z / l);
        }
    }
}
