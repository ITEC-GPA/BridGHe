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

        /// <summary>R6.5 P1 lane 2a: what the geometric property components (Section Geometric Property, Beam Geometric Property) read from the
        /// former section value of a section component: name, area, centroid, Ixx, Iyy, principal angle, I11, I22, W11, W22, ρ11, ρ22, J.</summary>
        public sealed record FormerValues(string Name, double Area, double XG, double YG, double Ixx, double Iyy, double AngleX1Rad, double I11, double I22,
            double W11, double W22, double Rho11, double Rho22, double J);

        /// <summary>
        /// R6.5 P1 lane 2a: the values of <see cref="FormerValues"/> of a section given by its recipe alone (no FEM value), as the former value
        /// made by the section component held them (<c>SectionModel.CalculateSection</c>, the same expressions): the circles by their formulas
        /// (area, I11 = I22, W11 = W22; the rest 0), a shape section by <see cref="Of"/> of its shapes (no J: the former components computed
        /// none), Refused, because the recipe does not hold
        /// them: the section moduli of a generic section (inputs W11/W22 of Generic Section, not part of the recipe), the shapes of a Brep
        /// section, the values of a native Straus7 section (Straus7 computes them) and a composite section (its composite property, lane 2b).
        /// </summary>
        public static FormerValues Former(Legacy.LegacySectionRecipe r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r.NativeKey != null) throw new NotSupportedException("GpcSectionValuesNative: the values of a native Straus7 section are computed by Straus7, not held by its recipe.");
            string name = string.IsNullOrEmpty(r.Name) ? null : r.Name;
            double d = r.D, t = r.T;
            switch (r.Kind)
            {
                case BridgeSectionKind.SolidCircle:
                {
                    double area = Math.Pow(d / 2, 2) * Math.PI, i11 = Math.PI * Math.Pow(d, 4) / 64, w11 = i11 / (d * 0.5);
                    return new FormerValues(name, area, 0, 0, 0, 0, 0, i11, i11, w11, w11, 0, 0, r.J);
                }
                case BridgeSectionKind.HollowCircle:
                {
                    double area = Math.Pow(d / 2, 2) * Math.PI - Math.Pow(d / 2 - t, 2) * Math.PI, i11 = Math.PI * (Math.Pow(d, 4) - Math.Pow(d - t * 2, 4)) / 64, w11 = i11 / (d * 0.5);
                    return new FormerValues(name, area, 0, 0, 0, 0, 0, i11, i11, w11, w11, 0, 0, r.J);
                }
                case BridgeSectionKind.Composite:
                    throw new NotSupportedException("GpcSectionValuesComposite: a composite section has its values in its composite property (lane 2b).");
                case BridgeSectionKind.Generic:
                    throw new NotSupportedException("GpcSectionModuliNotInRecipe: the section moduli W11 and W22 of a Generic section are not part of its GPC recipe.");
                case BridgeSectionKind.GenericShapes:
                    throw new NotSupportedException("GpcSectionShapesNotInRecipe: the shapes of a Brep section are not part of its GPC recipe.");
                default:
                {
                    var v = Of(StandardShapes(r.Kind, r.D, r.B, r.B1, r.B2, r.T1, r.T2, r.T3, 0), false);
                    return new FormerValues(name, v.Area, v.XG, v.YG, v.Ixx, v.Iyy, v.Angle, v.I11, v.I22, v.W11, v.W22, v.Rho11, v.Rho22, r.J);
                }
            }
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
