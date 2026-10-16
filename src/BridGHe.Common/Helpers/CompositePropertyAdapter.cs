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
    }
}
