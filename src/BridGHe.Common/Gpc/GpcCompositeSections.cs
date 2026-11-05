using System;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using GPC.Model.Materials;
using GPC.Model.Sections;
using GPC.Model.Sections.Rebar;
using GPC.Utilities.Maths;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// F6 (L11): the composite girder section of a BridGHe composite property as the GPC contract of the Checker
    /// (<see cref="BridgeCompositeSectionDefinition"/>), with the rules of Super Element Check, so that the checks take the input of the H
    /// bridge engine from <see cref="BridgeCompositeSectionDefinition.SectionInput(bool, BridgeAnalysisOptions)"/> instead of rebuilding the
    /// section (L11 proved it equal bit for bit to <c>BridgeCheckAdapter.SectionInput</c> on the same section):
    /// <list type="bullet">
    /// <item>steel I <see cref="SectionH"/> from B1/T1 (bottom flange), B2/T2 (top flange), D, T3 (web), named by the caller (Super Element Check:
    /// the BridGHe ID of the beam);</item>
    /// <item>materials EN 1992: concrete from fck (parabola-rectangle), structural steel and rebar from E, fy, fu with an ultimate strain of 0.1
    /// (elastic-perfectly plastic);</item>
    /// <item><see cref="BarsPerRow"/> bars per row, each of a third of the area of the row, at the pitch of a third of the slab width;</item>
    /// <item>the modular ratios n∞, n0 and n,sh (n∞ when the property has none) and the Consider Concrete / Consider Rebar choices of the
    /// property (of the start property for a variable section).</item>
    /// </list>
    /// A variable section (start and end properties that differ) is interpolated linearly at the normalised station.
    /// <para>The section of the homogenised FEM properties (<c>CompositeBeamPropertyModel.GetReinforcedConcreteSection</c>) has one bar per row
    /// of the whole area: same area and centroid of the bars, a different own inertia of the bars; it stays as it is until chain E.</para>
    /// </summary>
    public static class GpcCompositeSections
    {
        public const int BarsPerRow = 3;

        /// <summary>True when Super Element Check treats the girder as of constant section (same dimensions, ratios and material instances).</summary>
        public static bool IsConstant(CompositeBeamPropertyModel start, CompositeBeamPropertyModel end)
        {
            if (start == null || end == null) throw new ArgumentNullException(start == null ? nameof(start) : nameof(end));
            SectionModel s = start.SteelSection, e = end.SteelSection; var cs = start.ConcreteSection; var ce = end.ConcreteSection;
            return s.B1 == e.B1 && s.B2 == e.B2 && s.D == e.D && s.T1 == e.T1 && s.T2 == e.T2 && s.T3 == e.T3
                && cs.Height == ce.Height && cs.Width == ce.Width && cs.TopRebarArea == ce.TopRebarArea && cs.TopCover == ce.TopCover
                && cs.BottomRebarArea == ce.BottomRebarArea && cs.BottomCover == ce.BottomCover
                && start.VerticalOffset == end.VerticalOffset && start.NInstant == end.NInstant && start.NInfinite == end.NInfinite && start.NShrinkage == end.NShrinkage
                && start.ConcreteMaterial == end.ConcreteMaterial && start.SteelMaterial == end.SteelMaterial && start.RebarMaterial == end.RebarMaterial;
        }

        /// <summary>The definition of a constant section.</summary>
        public static BridgeCompositeSectionDefinition Definition(CompositeBeamPropertyModel property, string steelName)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            var s = property.SteelSection; var c = property.ConcreteSection;
            return Create(property.Name, steelName, s.D, s.T3, s.B2, s.T2, s.B1, s.T1, c.Width, c.Height, c.TopRebarArea, c.TopCover, c.BottomRebarArea, c.BottomCover,
                property.VerticalOffset, property.ConcreteMaterial.Name, property.ConcreteMaterial.SpecificCompressiveStrength,
                property.SteelMaterial.Name, property.SteelMaterial.Modulus, property.SteelMaterial.MinimumYieldStress, property.SteelMaterial.MinimumTensileStress,
                property.RebarMaterial.Name, property.RebarMaterial.Modulus, property.RebarMaterial.MinimumYieldStress, property.RebarMaterial.MinimumTensileStress,
                property.NInfinite, property.NInstant, property.NShrinkage, property.ConsiderConcrete, property.ConsiderRebar);
        }

        /// <summary>The definition of a variable section at the normalised station <paramref name="station"/> (0 at the start, 1 at the end):
        /// every dimension, ratio and material value interpolated linearly, the names, the choices and the material names of the start.</summary>
        public static BridgeCompositeSectionDefinition Definition(CompositeBeamPropertyModel start, CompositeBeamPropertyModel end, double station, string steelName)
        {
            if (start == null || end == null) throw new ArgumentNullException(start == null ? nameof(start) : nameof(end));
            double L(double a, double b) => Interpolation.GetLinearInterpolation(0, 1, a, b, station);
            double Lm(double a, double b) => Interpolation.GetLinearInterpolation(0, 1.0, a, b, station);
            SectionModel s = start.SteelSection, e = end.SteelSection; var cs = start.ConcreteSection; var ce = end.ConcreteSection;
            MaterialModel concrete = start.ConcreteMaterial, steel = start.SteelMaterial, rebar = start.RebarMaterial;
            return Create(start.Name, steelName, Lm(s.D, e.D), Lm(s.T3, e.T3), Lm(s.B2, e.B2), Lm(s.T2, e.T2), Lm(s.B1, e.B1), Lm(s.T1, e.T1),
                L(cs.Width, ce.Width), L(cs.Height, ce.Height), Lm(cs.TopRebarArea, ce.TopRebarArea), L(cs.TopCover, ce.TopCover),
                L(cs.BottomRebarArea, ce.BottomRebarArea), L(cs.BottomCover, ce.BottomCover), L(start.VerticalOffset, end.VerticalOffset),
                concrete.Name, Lm(concrete.SpecificCompressiveStrength, end.ConcreteMaterial.SpecificCompressiveStrength),
                steel.Name, Lm(steel.Modulus, end.SteelMaterial.Modulus), Lm(steel.MinimumYieldStress, end.SteelMaterial.MinimumYieldStress), Lm(steel.MinimumTensileStress, end.SteelMaterial.MinimumTensileStress),
                rebar.Name, Lm(rebar.Modulus, end.RebarMaterial.Modulus), Lm(rebar.MinimumYieldStress, end.RebarMaterial.MinimumYieldStress), Lm(rebar.MinimumTensileStress, end.RebarMaterial.MinimumTensileStress),
                L(start.NInfinite, end.NInfinite), L(start.NInstant, end.NInstant), L(start.NShrinkage, end.NShrinkage), start.ConsiderConcrete, start.ConsiderRebar);
        }

        private static BridgeCompositeSectionDefinition Create(string name, string steelName, double h, double webThickness, double topWidth, double topThickness,
            double bottomWidth, double bottomThickness, double slabWidth, double slabHeight, double topArea, double topCover, double bottomArea, double bottomCover,
            double verticalOffset, string concreteName, double fck, string steelMaterialName, double steelE, double steelFy, double steelFu,
            string rebarName, double rebarE, double rebarFy, double rebarFu, double nLongTerm, double nShortTerm, double nShrinkage, bool considerConcrete, bool considerRebar)
        {
            var sectionH = new SectionH(h, webThickness, topWidth, topThickness, bottomWidth, bottomThickness, steelName);
            var concrete = new ConcreteMaterialEN1992(concreteName, fck, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
            var steel = new SteelMaterialEN1992(steelMaterialName, steelE, steelFy, steelFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
            var rebar = new SteelMaterialEN1992(rebarName, rebarE, rebarFy, rebarFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
            double topDiameter = Math.Sqrt(topArea / BarsPerRow * 4 / Math.PI), bottomDiameter = Math.Sqrt(bottomArea / BarsPerRow * 4 / Math.PI);
            var top = topDiameter != 0 ? new RebarSectionCircular("", topDiameter, rebar) : null;
            var bottom = bottomDiameter != 0 ? new RebarSectionCircular("", bottomDiameter, rebar) : null;
            var section = Checks.CompositeSectionFactory.Create(slabWidth, slabHeight, concrete, top, slabWidth / BarsPerRow, topCover, bottom, slabWidth / BarsPerRow,
                sectionH, steel, bottomCover, verticalOffset, name);
            return new BridgeCompositeSectionDefinition(name, section, nLongTerm, nShortTerm, nShrinkage, verticalOffset, considerConcrete, considerRebar);
        }
    }
}
