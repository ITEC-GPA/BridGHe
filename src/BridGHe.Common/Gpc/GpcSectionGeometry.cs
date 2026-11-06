using System;
using BridGHe.Common.Geometry;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// R6.2 part C (P2N): the geometry of the sections of the former section components on GPC.Geometry, a copy line by line of
    /// <c>SectionModel.GetShapes</c> and of the shape branch of <c>SectionModel.CalculateSection</c> (the FEM model goes in R6.5): the same
    /// polygons, the same calls, the same numbers. The GPC builders of the sections take the principal angle of a recipe from it (the
    /// former value carried the computed <c>AngleX1</c>, which the reader reads); after R6.5 it gives the geometric values of the section
    /// components.
    /// </summary>
    public static class GpcSectionGeometry
    {
        /// <summary>The values of <c>Shapes.GetGeometricalDatas</c> (section units).</summary>
        public sealed record Values(double XG, double YG, double Area, double Ixx, double Iyy, double Ixy, double I11, double I22, double Angle,
            double Wxx, double Wyy, double W11, double W22, double Rho11, double Rho22);

        /// <summary>The shapes of a standard section (former <c>GetShapes</c>, no mirror), moved to the minimum.</summary>
        public static Shapes StandardShapes(BridgeSectionKind kind, double d, double b, double b1, double b2, double t1, double t2, double t3, double l)
        {
            GPC.Geometry.Polygon2d border = new GPC.Geometry.Polygon2d();
            GPC.Geometry.Polygon2d[] holes = null;

            double d1 = -(b1 - Math.Max(b1, b2)) / 2;
            double d2 = -(b2 - Math.Max(b1, b2)) / 2;
            switch (kind)
            {
                case BridgeSectionKind.SolidCircle:
                    break;
                case BridgeSectionKind.HollowCircle:
                    break;
                case BridgeSectionKind.SolidRectangle:
                    border.Add(0, 0);
                    border.Add(0, d);
                    border.Add(b, d);
                    border.Add(b, 0);
                    break;
                case BridgeSectionKind.HollowRectangle:
                    border.Add(0, 0);
                    border.Add(0, d);
                    border.Add(b, d);
                    border.Add(b, 0);
                    holes = new[] { new GPC.Geometry.Polygon2d {
                        { t2, t1 },
                        { t2, d - t1 },
                        { b - t2, d - t1 },
                        { b - t2, t1 }}};
                    break;
                case BridgeSectionKind.I:
                case BridgeSectionKind.Composite:
                    border.Add(d1, 0);
                    border.Add(d1, t1);
                    border.Add((Math.Max(b1, b2) - t3) / 2, t1);
                    border.Add((Math.Max(b1, b2) - t3) / 2, d - t2);
                    border.Add(d2, d - t2);
                    border.Add(d2, d);
                    border.Add(d2 + b2, d);
                    border.Add(d2 + b2, d - t2);
                    border.Add((Math.Max(b1, b2) - t3) / 2 + t3, d - t2);
                    border.Add((Math.Max(b1, b2) - t3) / 2 + t3, t1);
                    border.Add(d1 + b1, t1);
                    border.Add(d1 + b1, 0);
                    break;
                case BridgeSectionKind.T:
                    border.Add((b - t2) / 2, 0);
                    border.Add((b - t2) / 2, d - t1);
                    if (l > 0 && t3 > 0)
                    {
                        border.Add(t3, d - t1);
                        border.Add(t3, d - t1 - l);
                        border.Add(0, d - t1 - l);
                    }
                    else
                    {
                        border.Add(0, d - t1);
                    }
                    border.Add(0, d);
                    border.Add(b, d);
                    if (l > 0 && t3 > 0)
                    {
                        border.Add(b, d - t1 - l);
                        border.Add(b - t3, d - t1 - l);
                        border.Add(b - t3, d - t1);
                    }
                    else
                    {
                        border.Add(b, d - t1);
                    }
                    border.Add((b - t2) / 2 + t2, d - t1);
                    border.Add((b - t2) / 2 + t2, 0);
                    break;
                case BridgeSectionKind.C:
                    border.Add(0, 0);
                    border.Add(0, d);
                    border.Add(b, d);
                    if (l > 0 && t3 > 0)
                    {
                        border.Add(b, d - t1 - l);
                        border.Add(b - t3, d - t1 - l);
                        border.Add(b - t3, d - t1);
                    }
                    else
                    {
                        border.Add(b, d - t1);
                    }
                    border.Add(t2, d - t1);
                    border.Add(t2, t1);
                    if (l > 0 && t3 > 0)
                    {
                        border.Add(b - t3, t1);
                        border.Add(b - t3, t1 + l);
                        border.Add(b, t1 + l);
                    }
                    else
                    {
                        border.Add(b, t1);
                    }
                    border.Add(b, 0);
                    break;
                case BridgeSectionKind.Omega:
                    border.Add(0, l);
                    border.Add(0, d + l);
                    if (l > 0 && t3 > 0)
                    {
                        border.Add(b - t3, d + l);
                        border.Add(b - t3, d + l * 2);
                        border.Add(b, d + l * 2);
                    }
                    else
                    {
                        border.Add(b, d + l);
                    }
                    border.Add(b, d + l - t1);
                    border.Add(t2, d + l - t1);
                    border.Add(t2, l + t1);
                    border.Add(b, l + t1);
                    if (l > 0 && t3 > 0)
                    {
                        border.Add(b, 0);
                        border.Add(b - t3, 0);
                        border.Add(b - t3, l);
                    }
                    else
                    {
                        border.Add(b, l);
                    }
                    border.Add(b, 0);
                    break;
                case BridgeSectionKind.Angle:
                    border.Add(0, 0);
                    border.Add(0, d);
                    border.Add(t2, d);
                    border.Add(t2, t1);
                    border.Add(b, t1);
                    border.Add(b, 0);
                    break;
                case BridgeSectionKind.Z:
                    border.Add(b2 - t3, 0);
                    border.Add(b2 - t3, d - t2);
                    border.Add(0, d - t2);
                    border.Add(0, d);
                    border.Add(b2, d);
                    border.Add(b2, t1);
                    border.Add(b2 - t3 + b1, t1);
                    border.Add(b2 - t3 + b1, 0);
                    break;
                default: throw new NotSupportedException("GpcSectionGeometryNotAStandardSection: " + kind);
            }

            GPC.Geometry.Shape2d shape = new GPC.Geometry.Shape2d(border, holes);

            shape.MoveToMin();
            return new Shapes(new[] { shape });
        }

        /// <summary>The values of shapes as the former <c>CalculateSection</c> computes them (moved to the minimum; generic shapes always
        /// with the major axes and the angle brought into (−0.01, π]).</summary>
        public static Values Of(Shapes shapes, bool genericShapes)
        {
            shapes.MoveToMin();
            shapes.GetGeometricalDatas(genericShapes, 0.000000001, out double xG, out double yG, out double area, out double ixx, out double iyy,
                out double ixy, out double i11, out double i22, out double angle, out double wxx, out double wyy, out double w11, out double w22, out double rho11, out double rho22);
            if (genericShapes && angle < -0.01) angle += Math.PI;
            return new Values(xG, yG, area, ixx, iyy, ixy, i11, i22, angle, wxx, wyy, w11, w22, rho11, rho22);
        }
    }
}
