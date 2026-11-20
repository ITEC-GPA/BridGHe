using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Helpers;
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
    /// <item><see cref="BarsPerRow"/> bars per row, each of a third of the area of the row, at the pitch of a third of the slab width; since
    /// 0.5.0 a slab given by its bars (Concrete Slab Section with pitch and diameters) gives its own count, W/P rounded, with bars of about the
    /// real diameter (the area of a row divided by the count);</item>
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
        public const int BarsPerRow = Checks.ConcreteSlabSection.AreaBarsPerRow;



        /// <summary>BGHGPC1 (ii): the definition of a former composite property by value (a BGHA1 payload of a Composite Beam Property
        /// parameter, read as data), with the rule of <see cref="Definition(CompositeBeamPropertyModel, string)"/> and the name of the property
        /// as the name of the steel (as <c>GH_CompositeBeam</c>); n,sh absent or NaN: n∞, as the former property.</summary>
        public static BridgeCompositeSectionDefinition Definition(Legacy.LegacyValue p)
        {
            if (p == null || p.IsNull || !p.Is("CompositeBeamPropertyModel")) throw new ArgumentException("LegacyCompositeBeamPropertyModelExpected");
            var s = p.Field("_steelSection"); var c = p.Field("_concreteSection");
            var concrete = p.Field("_concreteMaterial"); var steel = p.Field("_steelMaterial"); var rebar = p.Field("_rebarMaterial");
            string name = p.String("Name");
            double nInfinite = p.Double("NInfinite"), nShrinkage = p.Has("NShrinkage") ? p.Double("NShrinkage") : double.NaN;
            if (double.IsNaN(nShrinkage)) nShrinkage = nInfinite;
            return Create(name, name, s.Double("D"), s.Double("T3"), s.Double("B2"), s.Double("T2"), s.Double("B1"), s.Double("T1"), c.Double("_width"), c.Double("_height"),
                c.Double("_topRebarArea"), c.Double("_topCover"), c.Double("_bottomRebarArea"), c.Double("_bottomCover"), p.Double("_verticalOffset"),
                concrete.String("Name"), concrete.Double("SpecificCompressiveStrength"), steel.String("Name"), steel.Double("Modulus"), steel.Double("MinimumYieldStress"),
                steel.Double("MinimumTensileStress"), rebar.String("Name"), rebar.Double("Modulus"), rebar.Double("MinimumYieldStress"), rebar.Double("MinimumTensileStress"),
                nInfinite, p.Double("NInstant"), nShrinkage, p.Bool("ConsiderConcrete"), p.Bool("ConsiderRebar"), Legacy.LegacyModelImport.ElasticMaterial(steel));
        }


        /// <summary>
        /// Chain E (c): the constant-section rule of Super Element Check on the definitions a composite beam carries (start and end): the same
        /// steel H, slab, rows of bars, vertical offset and modular ratios, and the same materials by value (name and fck of the concrete; name,
        /// E, fy and fu of the steel and of the bars, none when the section has no bars). The FEM rule (<see cref="IsConstant(CompositeBeamPropertyModel, CompositeBeamPropertyModel)"/>)
        /// compared the material instances: two properties with equal materials in different instances were variable, and their interpolation
        /// gave at every station the values of the constant section (the linear interpolation of equal ends is that value); by value they are
        /// constant (one definition instead of one per station, the same sections).
        /// </summary>
        public static bool IsConstant(BridgeCompositeSectionDefinition start, BridgeCompositeSectionDefinition end)
        {
            if (start == null || end == null) throw new ArgumentNullException(start == null ? nameof(start) : nameof(end));
            if (ReferenceEquals(start, end)) return true;
            SectionH s = SteelH(start), e = SteelH(end); var cs = start.Section; var ce = end.Section; var rs = Rows(start); var re = Rows(end);
            return s.Height == e.Height && s.ThicknessWeb == e.ThicknessWeb && s.LenghtTopFlange == e.LenghtTopFlange && s.ThicknessTopFlange == e.ThicknessTopFlange
                && s.LenghtBottomFlange == e.LenghtBottomFlange && s.ThicknessBottomFlange == e.ThicknessBottomFlange
                && cs.Width == ce.Width && cs.Height == ce.Height
                && rs.TopAreaMm2 == re.TopAreaMm2 && rs.TopCoverMm == re.TopCoverMm && rs.BottomAreaMm2 == re.BottomAreaMm2 && rs.BottomCoverMm == re.BottomCoverMm && rs.BarsPerRow == re.BarsPerRow
                && start.VerticalOffsetMm == end.VerticalOffsetMm && start.NShortTerm == end.NShortTerm && start.NLongTerm == end.NLongTerm && start.NShrinkage == end.NShrinkage
                && SameConcrete(Concrete(start), Concrete(end)) && SameSteel(Steel(start), Steel(end)) && SameSteel(Rebar(start), Rebar(end));
        }

        /// <summary>
        /// Chain E (c): the definition of a variable section at the normalised station <paramref name="station"/> (0 at the start, 1 at the end)
        /// from the definitions a composite beam carries, with the rules of <see cref="Definition(CompositeBeamPropertyModel, CompositeBeamPropertyModel, double, string)"/>:
        /// every dimension, row, ratio and material value interpolated linearly; the names, the choices and the material names of the start;
        /// the steel H named <paramref name="steelName"/>; no analysis steel (a section of the checks only). The values are read back from the
        /// definitions (steel H, slab, <see cref="BridgeCompositeSectionDefinition.RebarRows"/>, fck, E, fy, fu of the EN 1992 materials): the
        /// interpolation of the definitions of two properties equals bit for bit that of the properties (<c>gpc-composite-sections</c>, natives
        /// of 50 and 99). The material of the bars is that of the start; when the start has no bars, that of the end (and the reverse): a
        /// definition carries the material of its bars only (listed difference: a FEM property without bars still interpolated its own rebar
        /// material with that of the other end).
        /// </summary>
        public static BridgeCompositeSectionDefinition Definition(BridgeCompositeSectionDefinition start, BridgeCompositeSectionDefinition end, double station, string steelName)
        {
            if (start == null || end == null) throw new ArgumentNullException(start == null ? nameof(start) : nameof(end));
            double L(double a, double b) => Interpolation.GetLinearInterpolation(0, 1, a, b, station);
            SectionH s = SteelH(start), e = SteelH(end); var cs = start.Section; var ce = end.Section; var rs = Rows(start); var re = Rows(end);
            ConcreteMaterialEN1992 concrete = Concrete(start), concreteEnd = Concrete(end);
            SteelMaterial steel = Steel(start), steelEnd = Steel(end);
            SteelMaterial rebar = Rebar(start) ?? Rebar(end), rebarEnd = Rebar(end) ?? rebar;
            // No bars at either end: no bars at any station (the rebar material is not used); the values of the structural steel keep the
            // constructor of the (unused) rebar material valid.
            string rebarName = rebar?.Name ?? ""; double rebarE = (rebar ?? steel).ElasticModulusTension, rebarEEnd = (rebarEnd ?? steelEnd).ElasticModulusTension;
            double rebarFy = (rebar ?? steel).Fyk, rebarFyEnd = (rebarEnd ?? steelEnd).Fyk, rebarFu = (rebar ?? steel).Fu, rebarFuEnd = (rebarEnd ?? steelEnd).Fu;
            return Create(start.Name, steelName, L(s.Height, e.Height), L(s.ThicknessWeb, e.ThicknessWeb), L(s.LenghtTopFlange, e.LenghtTopFlange), L(s.ThicknessTopFlange, e.ThicknessTopFlange),
                L(s.LenghtBottomFlange, e.LenghtBottomFlange), L(s.ThicknessBottomFlange, e.ThicknessBottomFlange),
                L(cs.Width, ce.Width), L(cs.Height, ce.Height), L(rs.TopAreaMm2, re.TopAreaMm2), L(rs.TopCoverMm, re.TopCoverMm),
                L(rs.BottomAreaMm2, re.BottomAreaMm2), L(rs.BottomCoverMm, re.BottomCoverMm), L(start.VerticalOffsetMm, end.VerticalOffsetMm),
                concrete.Name, L(-concrete.Fck, -concreteEnd.Fck),
                steel.Name, L(steel.ElasticModulusTension, steelEnd.ElasticModulusTension), L(steel.Fyk, steelEnd.Fyk), L(steel.Fu, steelEnd.Fu),
                rebarName, L(rebarE, rebarEEnd), L(rebarFy, rebarFyEnd), L(rebarFu, rebarFuEnd),
                L(start.NLongTerm, end.NLongTerm), L(start.NShortTerm, end.NShortTerm), L(start.NShrinkage, end.NShrinkage), start.ConsiderConcrete, start.ConsiderRebar, null,
                Math.Max(rs.BarsPerRow, re.BarsPerRow));
        }

        /// <summary>Chain E (c): <see cref="Definition(BridgeCompositeSectionDefinition, BridgeCompositeSectionDefinition, double, string)"/> with
        /// the steel H named as that of the start definition (the composite property: <c>GH_CompositeBeamProperty.Definition</c>), the rule of
        /// Super Element Check and Composite Beam Linear Tension Analysis on the definitions. The name is not in the input of the H bridge
        /// engine nor in the results (the former checks named the H by the BridGHe ID of the beam: fingerprints of 50 and 99 unchanged).</summary>
        public static BridgeCompositeSectionDefinition Definition(BridgeCompositeSectionDefinition start, BridgeCompositeSectionDefinition end, double station) =>
            Definition(start, end, station, SteelH(start ?? throw new ArgumentNullException(nameof(start))).Name);

        private static BridgeCompositeRebarRows Rows(BridgeCompositeSectionDefinition definition) =>
            definition.RebarRows ?? throw new InvalidOperationException($"BridgeCompositeSectionWithoutRebarRows: the composite section '{definition.Name}' has no rows of bars (made by GpcCompositeSections)");

        private static ConcreteMaterialEN1992 Concrete(BridgeCompositeSectionDefinition definition) => (ConcreteMaterialEN1992)definition.Section.ConcreteMaterial;

        private static SteelMaterial Steel(BridgeCompositeSectionDefinition definition) => definition.Section.SteelSections[0].Section.SteelMaterial;

        private static SteelMaterial Rebar(BridgeCompositeSectionDefinition definition) => definition.Section.Rebars.FirstOrDefault()?.RebarSection.RebarMaterial as SteelMaterial;

        private static bool SameConcrete(ConcreteMaterialEN1992 a, ConcreteMaterialEN1992 b) => ReferenceEquals(a, b) || a.Name == b.Name && a.Fck == b.Fck;

        private static bool SameSteel(SteelMaterial a, SteelMaterial b) => ReferenceEquals(a, b)
            || a != null && b != null && a.Name == b.Name && a.ElasticModulusTension == b.ElasticModulusTension && a.Fyk == b.Fyk && a.Fu == b.Fu;

        /// <summary>R6.2 part C: also the builder of the Composite Beam Property component (<see cref="Builders.GpcPropertyBuilders.Composite"/>).
        /// <paramref name="barsPerRow"/>: the bars of each row of the section of the checks, each of the area of the row divided by the count, at
        /// the pitch slab width / count (<see cref="BarsPerRow"/> for a slab given by its areas, W/P for a slab given by its bars).</summary>
        internal static BridgeCompositeSectionDefinition Create(string name, string steelName, double h, double webThickness, double topWidth, double topThickness,
            double bottomWidth, double bottomThickness, double slabWidth, double slabHeight, double topArea, double topCover, double bottomArea, double bottomCover,
            double verticalOffset, string concreteName, double fck, string steelMaterialName, double steelE, double steelFy, double steelFu,
            string rebarName, double rebarE, double rebarFy, double rebarFu, double nLongTerm, double nShortTerm, double nShrinkage, bool considerConcrete, bool considerRebar,
            GPC.Model.Materials.Material analysisSteel, int barsPerRow = BarsPerRow)
        {
            if (barsPerRow < 1 || barsPerRow > Checks.ConcreteSlabSection.MaximumBarsPerRow) throw new ArgumentOutOfRangeException(nameof(barsPerRow), barsPerRow, "BridgeCompositeBarsPerRow");
            var sectionH = new SectionH(h, webThickness, topWidth, topThickness, bottomWidth, bottomThickness, steelName);
            var concrete = new ConcreteMaterialEN1992(concreteName, fck, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
            var steel = new SteelMaterialEN1992(steelMaterialName, steelE, steelFy, steelFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
            var rebar = new SteelMaterialEN1992(rebarName, rebarE, rebarFy, rebarFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
            double topDiameter = Math.Sqrt(topArea / barsPerRow * 4 / Math.PI), bottomDiameter = Math.Sqrt(bottomArea / barsPerRow * 4 / Math.PI);
            var top = topDiameter != 0 ? new RebarSectionCircular("", topDiameter, rebar) : null;
            var bottom = bottomDiameter != 0 ? new RebarSectionCircular("", bottomDiameter, rebar) : null;
            var section = Checks.CompositeSectionFactory.Create(slabWidth, slabHeight, concrete, top, slabWidth / barsPerRow, topCover, bottom, slabWidth / barsPerRow,
                sectionH, steel, bottomCover, verticalOffset, name);
            // Chain E step 1 (coordinator, 5/11, option ii): the rows as given, for the one-bar section of the analysis properties.
            var rows = new BridgeCompositeRebarRows(topArea, topCover, bottomArea, bottomCover, barsPerRow);
            // Chain E step 2 (coordinator, 5/11, option b): the steel of the analysis properties (constant sections; an interpolated section is
            // a section of the checks only).
            return analysisSteel == null ? new BridgeCompositeSectionDefinition(name, section, rows, nLongTerm, nShortTerm, nShrinkage, verticalOffset, considerConcrete, considerRebar)
                : new BridgeCompositeSectionDefinition(name, section, rows, analysisSteel, nLongTerm, nShortTerm, nShrinkage, verticalOffset, considerConcrete, considerRebar);
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
        /// n, torsion of the steel alone, shear areas of the steel and the slab, centroid, principal angle 0). Lane INR (12/11): the inertias on
        /// the section axes, I11 about the horizontal axis (<see cref="CompositePropertyAdapter.SectionAxisInertias"/>).
        /// </summary>
        public static HomogenisedAnalysisProperty Homogenised(BridgeCompositeSectionDefinition definition, double n, bool considerConcrete)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!double.IsFinite(n) || n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            var p = AnalysisProperties(definition, n, considerConcrete, definition.ConsiderRebar, true);
            var shear = HomogenisedShearAreas(definition, n, considerConcrete);
            // Lane INR (user decision 12/11, option B), as CreateHomogenizedProperty: I11 about the horizontal axis, I22 about the vertical one.
            var (i11, i22) = CompositePropertyAdapter.SectionAxisInertias(p.J11H, p.J22H, p.JxxH, p.JyyH, p.angleX);
            return new HomogenisedAnalysisProperty(p.areaH / n, i11 / n, i22 / n, SteelTorsionConstant(definition), shear.Area1, shear.Area2, p.centroidH.X, p.centroidH.Y);
        }
    }

    /// <summary>The section values of a generic homogenised analysis property of a composite girder in steel units (mm², mm⁴, mm), principal
    /// angle 0: what <c>CompositeBeamPropertyModel.CreateHomogenizedProperty</c> writes in its generic FEM section. Axis 1 horizontal: I11
    /// about the horizontal axis (vertical bending), I22 about the vertical one, ShearArea1 for the horizontal shear (lane INR, 12/11).</summary>
    public sealed record HomogenisedAnalysisProperty(double Area, double I11, double I22, double J, double ShearArea1, double ShearArea2, double CentroidX, double CentroidY);
}
