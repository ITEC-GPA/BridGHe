using System;
using GPC.Geometry;
using GPC.Model.Sections;
using GPC.Model.Sections.Concrete;

namespace BridGHe.Common.Helpers
{
    /// <summary>Adapts the component inclusion flags to the current gPC API.</summary>
    public static class CompositePropertyAdapter
    {
        // The caller passes a freshly constructed section; filtering never modifies the GH input.
        public static (double areaH, double SxH, double SyH, Point2d centroidH, double JxxH, double JyyH, double JxyH, double JpH, double J11H, double J22H, double angleX)
            Calculate(ReinforcedConcreteSection section, double phi, bool concrete, bool rebars, bool steel)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (!double.IsFinite(phi) || phi <= -1) throw new ArgumentOutOfRangeException(nameof(phi));
            if (!rebars) section.ClearRebars();
            if (!steel) section.SteelSections.Clear();
            if (concrete) return section.GetHomogeneizedMechanicalProperties(phi);

            // Raw section moments obey Q(phi) = Q(concrete net) + (1+phi) Q(steel at phi=0).
            // Differencing two calls removes the concrete, including displaced concrete,
            // while retaining gPC's material ratios, placements and local inertias.
            var p0 = section.GetHomogeneizedMechanicalProperties(0);
            var p1 = section.GetHomogeneizedMechanicalProperties(1);
            double factor = 1 + phi;
            double area = factor * (p1.areaH - p0.areaH);
            if (!(area > 0) || !double.IsFinite(area))
                throw new ArgumentException("La sezione selezionata non contiene area resistente positiva.");
            double sx = factor * (p1.SxH - p0.SxH);
            double sy = factor * (p1.SyH - p0.SyH);
            var centroid = new Point2d(sy / area, sx / area);
            double ix = factor * (p1.JxxH + p1.areaH * p1.centroidH.Y * p1.centroidH.Y - p0.JxxH - p0.areaH * p0.centroidH.Y * p0.centroidH.Y) - area * centroid.Y * centroid.Y;
            double iy = factor * (p1.JyyH + p1.areaH * p1.centroidH.X * p1.centroidH.X - p0.JyyH - p0.areaH * p0.centroidH.X * p0.centroidH.X) - area * centroid.X * centroid.X;
            double ixy = factor * (p1.JxyH + p1.areaH * p1.centroidH.X * p1.centroidH.Y - p0.JxyH - p0.areaH * p0.centroidH.X * p0.centroidH.Y) - area * centroid.X * centroid.Y;
            double radius = Math.Sqrt((ix - iy) * (ix - iy) / 4 + ixy * ixy);
            double i1 = (ix + iy) / 2 + radius;
            double i2 = (ix + iy) / 2 - radius;
            double angle = Math.Abs(ixy) <= 1e-10 * (Math.Abs(ix) + Math.Abs(iy))
                ? (ix >= iy ? 0 : -Math.PI / 2) : -0.5 * Math.Atan2(2 * ixy, ix - iy);
            return (area, sx, sy, centroid, ix, iy, ixy, ix + iy, i1, i2, angle);
        }

        /// <summary>
        /// The inertias of the generic homogenised analysis property of a composite girder (written with principal angle 0) from the values of
        /// <see cref="Calculate"/>: axis 1 horizontal and axis 2 vertical, as on every BridGHe beam (user decision of 12/11/2026, option B), so
        /// I11 is the principal value whose axis is nearest the horizontal X axis of the section (the vertical bending) and I22 the other one.
        /// <para>Before 0.4.0 the property wrote the sorted principal values (<paramref name="j11"/> ≥ <paramref name="j22"/>) as I11/I22: when
        /// the slab makes Iyy &gt; Ixx the maximum is about Y and the lateral inertia became the vertical bending stiffness (the side girders of
        /// examples 16–18: +18 % long term, +173 % short term). Straus7 keeps I11 &lt; I22 as given and bends each plane with the inertia of its
        /// own axis (native probe of lane INR, 12/11). The rejected option A (the principal values with an angle of −90°) would rotate the
        /// result axes of those girders only.</para>
        /// <para>The axis of <paramref name="j11"/> within 45° of X: the same doubles as before. Beyond it the two principal values are
        /// exchanged; with X and Y principal (angle ±90°, a symmetric section) exactly <paramref name="jxx"/> and <paramref name="jyy"/>.
        /// A small product of inertia is still ignored (angle 0, as before). The shear areas of the property are already those of axis 1
        /// horizontal (<c>GpcCompositeSections.HomogenisedShearAreas</c>, angle 0), so area 1 and I22 now belong to the same plane.</para>
        /// </summary>
        /// <param name="j11">The maximum principal value (J11H)</param>
        /// <param name="j22">The minimum principal value (J22H)</param>
        /// <param name="jxx">The moment about X (JxxH)</param>
        /// <param name="jyy">The moment about Y (JyyH)</param>
        /// <param name="angleX">The angle of the axis of <paramref name="j11"/> from X (rad)</param>
        /// <returns>I11 (about the axis nearest X) and I22, in the scale of the values given</returns>
        public static (double I11, double I22) SectionAxisInertias(double j11, double j22, double jxx, double jyy, double angleX)
        {
            if (!(Math.Abs(angleX) > Math.PI / 4)) return (j11, j22);
            return Math.Abs(angleX) == Math.PI / 2 ? (jxx, jyy) : (j22, j11);
        }
    }
}
