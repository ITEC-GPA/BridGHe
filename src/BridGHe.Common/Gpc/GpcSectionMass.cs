using System;
using GPC.Model.ElementProperties;
using GPC.Model.Sections;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// Chain E (d): the mass per length of a beam property in the GPC transformers that keep it (Bridge State Override, Preserve Mass).
    /// <para><b>Rule of 0.4.0 (coordinator, 5/11): the self-weight stays bit for bit.</b> The FEM path took density × the area of the BridGHe
    /// section (<c>SectionModel.SectionArea</c>: for the standard shapes the polygon of <c>SectionModel.GetShapes</c> integrated by
    /// <see cref="Geometry.Shapes.GetGeometricalDatas"/>, for the circles the closed forms of <c>SectionModel.CalculateSection</c>; for the explicit
    /// and native sections the area as given). The GPC sections of the same dimensions compute their own area, which may differ in the last
    /// bits; so the GPC path uses the same area with the same formula and the same order of operations (<see cref="LegacyArea"/>), never
    /// <see cref="ISectionShape.Area"/> of a standard GPC section. 0.5.0 can revisit this (one area for analysis and mass), with the measure of
    /// the change of the self-weight.</para>
    /// </summary>
    public static class GpcSectionMass
    {
        /// <summary>The mass per length of <paramref name="property"/> as the FEM path computed it: density × <see cref="LegacyArea"/>.</summary>
        public static double MassPerLength(ElasticBeamProperty property)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            return property.Material.Density * LegacyArea(property.Section);
        }

        /// <summary>
        /// The area of <paramref name="section"/> as BridGHe's FEM section computed it (definition units), for the five standard shapes the
        /// legacy reader maps (<see cref="GpcSections.Standard"/>: I, solid and hollow rectangle, solid and hollow circle), a copy of
        /// <c>SectionModel.GetShapes</c>/<c>CalculateSection</c> (no mirror: the reader refuses mirrored sections); any other section
        /// (explicit, native) its area as given. <c>gpc-state-override</c> compares it bit for bit with <c>SectionModel.SectionArea</c>.
        /// </summary>
        public static double LegacyArea(ISectionShape section)
        {
            switch (section)
            {
                case SectionH h: return PolygonArea(I(h.LenghtBottomFlange, h.LenghtTopFlange, h.Height, h.ThicknessBottomFlange, h.ThicknessTopFlange, h.ThicknessWeb), null);
                case SectionRHS r:
                    {
                        double d = r.Height, b = r.Base, t1 = r.ThicknessTop, t2 = r.ThicknessWebLeft;
                        return PolygonArea(Rectangle(d, b), new[] { new GPC.Geometry.Polygon2d { { t2, t1 }, { t2, d - t1 }, { b - t2, d - t1 }, { b - t2, t1 } } });
                    }
                case SectionRectangular r: return PolygonArea(Rectangle(r.Height, r.Width), null);
                case SectionCHS c: { double d = c.Diameter, t = c.Thickness; return Math.Pow(d / 2, 2) * Math.PI - Math.Pow(d / 2 - t, 2) * Math.PI; }
                case SectionCircular c: { double d = c.Diameter; return Math.Pow(d / 2, 2) * Math.PI; }
                case null: throw new ArgumentNullException(nameof(section));
                default: return section.Area;
            }
        }

        // SectionModel.GetShapes, SectionTypes.SolidRectangle (also the border of HollowRectangle).
        private static GPC.Geometry.Polygon2d Rectangle(double d, double b)
        {
            var border = new GPC.Geometry.Polygon2d();
            border.Add(0, 0); border.Add(0, d); border.Add(b, d); border.Add(b, 0);
            return border;
        }

        // SectionModel.GetShapes, SectionTypes.I: B1/T1 bottom flange, B2/T2 top flange, T3 web.
        private static GPC.Geometry.Polygon2d I(double b1, double b2, double d, double t1, double t2, double t3)
        {
            var border = new GPC.Geometry.Polygon2d();
            double d1 = -(b1 - Math.Max(b1, b2)) / 2;
            double d2 = -(b2 - Math.Max(b1, b2)) / 2;
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
            return border;
        }

        // SectionModel.GetShapes (shape moved to its minimum) and CalculateSection (shapes moved to their minimum, not always major axes,
        // tolerance 1e-9): the area.
        private static double PolygonArea(GPC.Geometry.Polygon2d border, GPC.Geometry.Polygon2d[] holes)
        {
            var shape = new GPC.Geometry.Shape2d(border, holes);
            shape.MoveToMin();
            var shapes = new Geometry.Shapes(new[] { shape });
            shapes.MoveToMin();
            shapes.GetGeometricalDatas(false, 0.000000001, out _, out _, out double area, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
            return area;
        }
    }
}
