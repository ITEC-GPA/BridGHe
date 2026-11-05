using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Helpers;
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
    /// <para>The section of the homogenised analysis properties (<c>CompositeBeamPropertyModel.GetReinforcedConcreteSection</c>) has one bar per
    /// row of the whole area (decision of 5/11 for 0.4.0: the analysis keeps it; passo 38 of correzioni-P2B.md measured I22 +4 % to +410 %
    /// with three bars): the definition keeps the rows (<see cref="BridgeCompositeRebarRows"/>) and gives that section
    /// (<see cref="BridgeCompositeSectionDefinition.AnalysisSection"/>), from which <see cref="Homogenised"/> and <see cref="AnalysisProperties"/>
    /// give the analysis values.</para>
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
            // Chain E step 1 (coordinator, 5/11, option ii): the rows as given, for the one-bar section of the analysis properties.
            var rows = new BridgeCompositeRebarRows(topArea, topCover, bottomArea, bottomCover, BarsPerRow);
            return new BridgeCompositeSectionDefinition(name, section, rows, nLongTerm, nShortTerm, nShrinkage, verticalOffset, considerConcrete, considerRebar);
        }

        /// <summary>
        /// Chain E step 1: the homogenised mechanical properties of the analysis section of <paramref name="definition"/>
        /// (<see cref="BridgeCompositeSectionDefinition.AnalysisSection"/>: one bar per row with the whole area of the row) with the modular ratio
        /// <paramref name="n"/> and the components chosen, as <see cref="CompositePropertyAdapter.Calculate"/> gives them:
        /// <c>CompositeBeamPropertyModel.GetHomogeneizedMechanicalProperties</c> of the property of the definition bit for bit (checked on the
        /// composite properties of 50 and 99 and in <c>gpc-composite-sections</c>).
        /// </summary>
        public static (double areaH, double SxH, double SyH, GPC.Geometry.Point2d centroidH, double JxxH, double JyyH, double JxyH, double JpH, double J11H, double J22H, double angleX)
            AnalysisProperties(BridgeCompositeSectionDefinition definition, double n, bool considerConcrete, bool considerRebars, bool considerSteelSections)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var section = definition.AnalysisSection();
            double phi = GPC.Model.Sections.Concrete.ReinforcedConcreteSection.CalculateHomogenizedFactorPhi(n, section.SteelSections.First().Section.SteelMaterial, section.ConcreteMaterial);
            return CompositePropertyAdapter.Calculate(section, phi, considerConcrete, considerRebars, considerSteelSections);
        }

        /// <summary>The torsion constant of the steel H alone (<c>CompositeBeamPropertyModel.GetOnlySteelMechanicalProperties().Jt</c>): the
        /// torsion of the homogenised analysis properties. A new <see cref="SectionH"/> with the dimensions of the steel of the definition, as
        /// BridGHe computes it: the H of the definition sits in a rolled <c>SteelSection</c>, which gives its shape the fillet edges, and so the
        /// torsion formula of the rolled sections (a different value).</summary>
        public static double SteelTorsionConstant(BridgeCompositeSectionDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var h = SteelH(definition);
            return new SectionH(h.Height, h.ThicknessWeb, h.LenghtTopFlange, h.ThicknessTopFlange, h.LenghtBottomFlange, h.ThicknessBottomFlange, definition.Name).Jt;
        }

        private static SectionH SteelH(BridgeCompositeSectionDefinition definition) => (SectionH)definition.Section.SteelSections[0].Section.SectionShape;

        private static readonly ConcurrentDictionary<string, SectionShearAreaProperties> shearAreas = new ConcurrentDictionary<string, SectionShearAreaProperties>(StringComparer.Ordinal);

        /// <summary>
        /// The shear areas, in steel units, of the steel I (bottom flange <paramref name="b1"/> × <paramref name="t1"/>, top flange
        /// <paramref name="b2"/> × <paramref name="t2"/>, web <paramref name="tw"/>, height <paramref name="d"/>) with, when
        /// <paramref name="concrete"/> and the slab has a size, the slab on its top flange (centred) at the modular ratio <paramref name="n"/>,
        /// in the principal axes of the homogenised section (axis 1 horizontal): GPCModel SectionShearAreas.ComputeHomogenised at ν = 0 (E13).
        /// The bars are in the area and the inertias, not in the shear areas. Cached per process by geometry and n (one source for the FEM
        /// property and the definition).
        /// </summary>
        public static SectionShearAreaProperties HomogenisedShearAreas(double b1, double b2, double d, double t1, double t2, double tw, double slabWidth, double slabHeight,
            bool concrete, double n, string name)
        {
            if (!double.IsFinite(n) || n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            double w = slabWidth, h = slabHeight; concrete = concrete && w > 0 && h > 0;
            string key = string.Join("|", new[] { b1, b2, d, t1, t2, tw, concrete ? w : 0, concrete ? h : 0, concrete ? n : 0 }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
            return shearAreas.GetOrAdd(key, _ =>
            {
                GPC.Geometry.Shape2d Outline(params (double x, double y)[] v) => new GPC.Geometry.Shape2d(new GPC.Geometry.Polygon2d(v.Select(p => new GPC.Geometry.Point2d(p.x, p.y)).ToArray()));
                // B1/T1 bottom flange, B2/T2 top flange, T3 web; the slab on the top flange, centred (CompositeSectionFactory).
                var parts = new List<(GPC.Geometry.Shape2d, double)>
                {
                    (Outline((-b1 / 2, 0), (b1 / 2, 0), (b1 / 2, t1), (tw / 2, t1), (tw / 2, d - t2), (b2 / 2, d - t2), (b2 / 2, d), (-b2 / 2, d),
                        (-b2 / 2, d - t2), (-tw / 2, d - t2), (-tw / 2, t1), (-b1 / 2, t1)), 1)
                };
                if (concrete) parts.Add((Outline((-w / 2, d), (w / 2, d), (w / 2, d + h), (-w / 2, d + h)), n));
                return SectionShearAreas.ComputeHomogenised(parts, 0, name);
            });
        }

        /// <summary>The shear areas of the homogenised analysis property of <paramref name="definition"/> with or without the slab
        /// (<c>CompositeBeamPropertyModel.HomogenisedShearAreas</c> of its property with Consider Concrete = <paramref name="considerConcrete"/>).</summary>
        public static SectionShearAreaProperties HomogenisedShearAreas(BridgeCompositeSectionDefinition definition, double n, bool considerConcrete)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            var h = SteelH(definition); var section = definition.Section;
            return HomogenisedShearAreas(h.LenghtBottomFlange, h.LenghtTopFlange, h.Height, h.ThicknessBottomFlange, h.ThicknessTopFlange, h.ThicknessWeb,
                section.Width, section.Height, considerConcrete, n, definition.Name);
        }

        /// <summary>
        /// Chain E step 1: the values of the generic homogenised property of the analysis, in steel units, of <paramref name="definition"/> at
        /// the modular ratio <paramref name="n"/>, with the slab (<paramref name="considerConcrete"/>, Bridge State Override: uncracked) or
        /// without it (cracked), the bars as the definition says (Consider Rebar): the section values of
        /// <c>CompositeBeamPropertyModel.CreateHomogenizedProperty</c> bit for bit (area and inertias of the one-bar analysis section divided by
        /// n, torsion of the steel alone, shear areas of the steel and the slab, centroid, principal angle 0).
        /// </summary>
        public static HomogenisedAnalysisProperty Homogenised(BridgeCompositeSectionDefinition definition, double n, bool considerConcrete)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!double.IsFinite(n) || n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            var p = AnalysisProperties(definition, n, considerConcrete, definition.ConsiderRebar, true);
            var shear = HomogenisedShearAreas(definition, n, considerConcrete);
            return new HomogenisedAnalysisProperty(p.areaH / n, p.J11H / n, p.J22H / n, SteelTorsionConstant(definition), shear.Area1, shear.Area2, p.centroidH.X, p.centroidH.Y);
        }
    }

    /// <summary>The section values of a generic homogenised analysis property of a composite girder in steel units (mm², mm⁴, mm), principal
    /// angle 0: what <c>CompositeBeamPropertyModel.CreateHomogenizedProperty</c> writes in its generic FEM section.</summary>
    public sealed record HomogenisedAnalysisProperty(double Area, double I11, double I22, double J, double ShearArea1, double ShearArea2, double CentroidX, double CentroidY);
}
