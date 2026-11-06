#if GPC_PILOT
using System;
using System.Linq;
using GPC.Checkers.CompositeBridge;
using GPC.Model.Materials;
using GPC.Model.Sections;
using GPC.Model.Sections.Rebar;
using GPC.Utilities.Maths;

/// <summary>
/// P2U (F6, Smoke tests on GPC): a composite girder section built directly in GPC, the Checker contract
/// <see cref="BridgeCompositeSectionDefinition"/> that <c>GH_CompositeBeamProperty</c> carries (chain E) and that Composite Shrinkage Actions,
/// Composite Section Geometric Property, Self Tension, Composite Beam, Super Element Check and the preview read, without any FEM model:
/// <list type="bullet">
/// <item>steel I <see cref="SectionH"/> (bottom flange B1/T1, top flange B2/T2, web, height) named as the section (as the GH type names it)
/// or as the caller says (the former Super Element Check named it by the beam);</item>
/// <item>materials EN 1992 from the numbers of the material parameters: concrete from fck (parabola-rectangle), structural steel and rebar
/// from E, fy, fu with an ultimate strain of 0.1 (elastic-perfectly plastic);</item>
/// <item>three bars per row of a third of the area of the row at the pitch of a third of the slab width, the rows as given
/// (<see cref="BridgeCompositeRebarRows"/>, for the one-bar analysis section);</item>
/// <item>the analysis steel: the GPC elastic material of the steel parameter (<c>GpcMaterials.Elastic</c> with E, ν, G, Use Poisson,
/// density, α, the rule of the legacy reader for a material without analysis data), or a given material;</item>
/// <item>the vertical offset, the modular ratios and the Consider Concrete / Consider Rebar choices.</item>
/// </list>
/// The rules of the private <c>GpcCompositeSections.Create</c> of BridGHe (constant section, <see cref="Create(Numbers, string, bool)"/>) and of
/// the interpolation of two composite properties (<see cref="Interpolated"/>). <c>gpc-composite-sections</c> ties them to the production
/// construction through the R6.0 capture (every captured definition rebuilt from its numbers, bit for bit) and, while the FEM properties
/// exist, to the definitions of the FEM properties of the same numbers (<c>GpcCompositeSectionChecks.Fem.cs</c>).
/// </summary>
internal static class GpcCompositeDefinitions
{
    internal const int BarsPerRow = 3;

    /// <summary>The numbers of a composite section in N-mm-MPa (the inputs of Composite Beam Property and of its material parameters).</summary>
    internal sealed record Numbers
    {
        public string Name { get; init; } = "Composite";
        public double Height { get; init; }
        public double Web { get; init; }
        public double TopWidth { get; init; }
        public double TopThickness { get; init; }
        public double BottomWidth { get; init; }
        public double BottomThickness { get; init; }
        public double SlabWidth { get; init; }
        public double SlabHeight { get; init; }
        public double TopArea { get; init; }
        public double TopCover { get; init; }
        public double BottomArea { get; init; }
        public double BottomCover { get; init; }
        public double VerticalOffset { get; init; }
        public string Concrete { get; init; } = "C30";
        public double Fck { get; init; } = 30;
        /// <summary>The modulus of the concrete material parameter: not in the definition (EN 1992 from fck); the FEM parity only.</summary>
        public double ConcreteE { get; init; }
        public string Steel { get; init; } = "S355";
        public double SteelE { get; init; } = 210000;
        public double SteelFy { get; init; } = 355;
        public double SteelFu { get; init; } = 510;
        public double SteelPoisson { get; init; } = 0.3;
        public double SteelShearModulus { get; init; }
        public bool SteelUsePoisson { get; init; } = true;
        public double SteelDensity { get; init; }
        public double SteelThermalExpansion { get; init; }
        /// <summary>The analysis steel as given (a captured definition), instead of the elastic material of the numbers above.</summary>
        public Material AnalysisSteel { get; init; }
        public string Rebar { get; init; } = "B450";
        public double RebarE { get; init; } = 210000;
        public double RebarFy { get; init; } = 450;
        public double RebarFu { get; init; } = 540;
        /// <summary>The ultimate strain of the rebar material parameter (null: none given). Not in the definition (bars at 0.1); the former
        /// analysis section of the composite property used it.</summary>
        public double? RebarUltimateStrain { get; init; }
        public double NLongTerm { get; init; }
        public double NShortTerm { get; init; }
        public double NShrinkage { get; init; }
        public bool ConsiderConcrete { get; init; } = true;
        public bool ConsiderRebar { get; init; }
    }

    /// <summary>The section of <c>BridgeGrillageExample.Composite</c> («H + soletta tributaria»: H 1600 × 600 × 40, web 16, slab 250 × 2500
    /// without bars (covers 40 and 50, the defaults of the slab), C30, S355, B450, n0 = Es/Ec, n∞ = Es/Ec·(1 + φ), n,sh = n∞), from the
    /// parameters of the example.</summary>
    internal static Numbers Grillage(BridgeGrillageExample.Parameters p)
    {
        double n0 = BridgeGrillageExample.Es / BridgeGrillageExample.Ec, nInf = BridgeGrillageExample.Es / BridgeGrillageExample.Ec * (1 + p.PhiLong);
        return new Numbers
        {
            Name = "H + soletta tributaria", Height = p.SteelHeight, Web = p.WebThickness, TopWidth = p.FlangeWidth, TopThickness = p.FlangeThickness,
            BottomWidth = p.FlangeWidth, BottomThickness = p.FlangeThickness, SlabWidth = p.GirderSlabWidth, SlabHeight = p.SlabThickness,
            TopArea = 0, TopCover = 40, BottomArea = 0, BottomCover = 50, SteelE = BridgeGrillageExample.Es, SteelPoisson = BridgeGrillageExample.NuSteel,
            RebarE = BridgeGrillageExample.Es, NLongTerm = nInf, NShortTerm = n0, NShrinkage = nInf, ConsiderConcrete = true, ConsiderRebar = false,
        };
    }

    /// <summary>The analysis steel of the numbers (the elastic GPC material of the steel parameter, or the one given).</summary>
    internal static Material AnalysisSteel(Numbers x) => x.AnalysisSteel ?? BridGHe.Common.Gpc.GpcMaterials.Elastic(x.Steel, x.SteelE, x.SteelPoisson, x.SteelShearModulus,
        x.SteelUsePoisson, x.SteelDensity, x.SteelThermalExpansion, 0, 0, 0, 0, null);

    /// <summary>The definition of a constant section from its numbers, the steel H named as the section.</summary>
    internal static BridgeCompositeSectionDefinition Create(Numbers x) => Create(x, x?.Name);

    /// <summary>The definition of a constant section from its numbers, the steel H named <paramref name="steelName"/>, with the analysis
    /// steel (a constant section) or without it.</summary>
    internal static BridgeCompositeSectionDefinition Create(Numbers x, string steelName, bool analysisSteel = true)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        return Build(x.Name, steelName, x.Height, x.Web, x.TopWidth, x.TopThickness, x.BottomWidth, x.BottomThickness, x.SlabWidth, x.SlabHeight,
            x.TopArea, x.TopCover, x.BottomArea, x.BottomCover, x.VerticalOffset, x.Concrete, x.Fck, x.Steel, x.SteelE, x.SteelFy, x.SteelFu,
            x.Rebar, x.RebarE, x.RebarFy, x.RebarFu, x.NLongTerm, x.NShortTerm, x.NShrinkage, x.ConsiderConcrete, x.ConsiderRebar,
            analysisSteel ? AnalysisSteel(x) : null);
    }

    /// <summary>The definition of a variable section at the normalised station <paramref name="station"/> from the numbers of its two ends, with
    /// the rule of the interpolation of two composite properties (<c>GpcCompositeSections.Definition(start, end, station, steelName)</c> on the
    /// FEM properties): the steel H, the top bars, fck and the E, fy, fu of the steel and of the bars interpolated with
    /// <c>GetLinearInterpolation(0, 1.0, …)</c>, the slab, covers, bottom bars, offset and ratios with <c>GetLinearInterpolation(0, 1, …)</c>; the
    /// names, the choices and the material names of the start; no analysis steel (a section of the checks only).</summary>
    internal static BridgeCompositeSectionDefinition Interpolated(Numbers s, Numbers e, double station, string steelName)
    {
        if (s == null || e == null) throw new ArgumentNullException(s == null ? nameof(s) : nameof(e));
        double L(double a, double b) => Interpolation.GetLinearInterpolation(0, 1, a, b, station);
        double Lm(double a, double b) => Interpolation.GetLinearInterpolation(0, 1.0, a, b, station);
        return Build(s.Name, steelName, Lm(s.Height, e.Height), Lm(s.Web, e.Web), Lm(s.TopWidth, e.TopWidth), Lm(s.TopThickness, e.TopThickness),
            Lm(s.BottomWidth, e.BottomWidth), Lm(s.BottomThickness, e.BottomThickness), L(s.SlabWidth, e.SlabWidth), L(s.SlabHeight, e.SlabHeight),
            Lm(s.TopArea, e.TopArea), L(s.TopCover, e.TopCover), L(s.BottomArea, e.BottomArea), L(s.BottomCover, e.BottomCover), L(s.VerticalOffset, e.VerticalOffset),
            s.Concrete, Lm(s.Fck, e.Fck), s.Steel, Lm(s.SteelE, e.SteelE), Lm(s.SteelFy, e.SteelFy), Lm(s.SteelFu, e.SteelFu),
            s.Rebar, Lm(s.RebarE, e.RebarE), Lm(s.RebarFy, e.RebarFy), Lm(s.RebarFu, e.RebarFu),
            L(s.NLongTerm, e.NLongTerm), L(s.NShortTerm, e.NShortTerm), L(s.NShrinkage, e.NShrinkage), s.ConsiderConcrete, s.ConsiderRebar, null);
    }

    private static BridgeCompositeSectionDefinition Build(string name, string steelName, double h, double web, double topWidth, double topThickness,
        double bottomWidth, double bottomThickness, double slabWidth, double slabHeight, double topArea, double topCover, double bottomArea, double bottomCover,
        double verticalOffset, string concreteName, double fck, string steelMaterial, double steelE, double steelFy, double steelFu,
        string rebarName, double rebarE, double rebarFy, double rebarFu, double nLongTerm, double nShortTerm, double nShrinkage, bool considerConcrete, bool considerRebar,
        Material analysisSteel)
    {
        var sectionH = new SectionH(h, web, topWidth, topThickness, bottomWidth, bottomThickness, steelName);
        var concrete = new ConcreteMaterialEN1992(concreteName, fck, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
        var steel = new SteelMaterialEN1992(steelMaterial, steelE, steelFy, steelFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
        var rebar = new SteelMaterialEN1992(rebarName, rebarE, rebarFy, rebarFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
        double top = Math.Sqrt(topArea / BarsPerRow * 4 / Math.PI), bottom = Math.Sqrt(bottomArea / BarsPerRow * 4 / Math.PI);
        var section = BridGHe.Common.Checks.CompositeSectionFactory.Create(slabWidth, slabHeight, concrete,
            top != 0 ? new RebarSectionCircular("", top, rebar) : null, slabWidth / BarsPerRow, topCover,
            bottom != 0 ? new RebarSectionCircular("", bottom, rebar) : null, slabWidth / BarsPerRow, sectionH, steel, bottomCover, verticalOffset, name);
        var rows = new BridgeCompositeRebarRows(topArea, topCover, bottomArea, bottomCover, BarsPerRow);
        return analysisSteel == null ? new BridgeCompositeSectionDefinition(name, section, rows, nLongTerm, nShortTerm, nShrinkage, verticalOffset, considerConcrete, considerRebar)
            : new BridgeCompositeSectionDefinition(name, section, rows, analysisSteel, nLongTerm, nShortTerm, nShrinkage, verticalOffset, considerConcrete, considerRebar);
    }

    /// <summary>The numbers of a definition (a captured one, for instance) and the name of its steel H: the steel H, the slab, the rows, the
    /// offset, the EN 1992 materials (fck, E, fy, fu; the bars of the first rebar, the structural steel when the section has no bars: the
    /// rebar material is not used then), the analysis steel as it is, the ratios and the choices.</summary>
    internal static (Numbers Numbers, string SteelName) Read(BridgeCompositeSectionDefinition d)
    {
        if (d?.RebarRows == null) throw new ArgumentException("A definition with its rows of bars is required.");
        var section = d.Section; var h = (SectionH)section.SteelSections[0].Section.SectionShape; var steel = section.SteelSections[0].Section.SteelMaterial;
        var concrete = (ConcreteMaterialEN1992)section.ConcreteMaterial; var rebar = section.Rebars.FirstOrDefault()?.RebarSection.RebarMaterial as SteelMaterial ?? steel;
        var rows = d.RebarRows;
        if (rows.BarsPerRow != BarsPerRow) throw new ArgumentException($"{rows.BarsPerRow} bars per row instead of {BarsPerRow}.");
        var analysis = d.AnalysisSteel;
        return (new Numbers
        {
            Name = d.Name, Height = h.Height, Web = h.ThicknessWeb, TopWidth = h.LenghtTopFlange, TopThickness = h.ThicknessTopFlange,
            BottomWidth = h.LenghtBottomFlange, BottomThickness = h.ThicknessBottomFlange, SlabWidth = section.Width, SlabHeight = section.Height,
            TopArea = rows.TopAreaMm2, TopCover = rows.TopCoverMm, BottomArea = rows.BottomAreaMm2, BottomCover = rows.BottomCoverMm, VerticalOffset = d.VerticalOffsetMm,
            Concrete = concrete.Name, Fck = -concrete.Fck, Steel = steel.Name, SteelE = steel.ElasticModulusTension, SteelFy = steel.Fyk, SteelFu = steel.Fu,
            SteelPoisson = analysis?.Ni ?? 0, SteelDensity = analysis?.Density ?? 0, SteelThermalExpansion = analysis?.AlfaThermalExpansion ?? 0, AnalysisSteel = analysis,
            Rebar = rebar.Name, RebarE = rebar.ElasticModulusTension, RebarFy = rebar.Fyk, RebarFu = rebar.Fu,
            NLongTerm = d.NLongTerm, NShortTerm = d.NShortTerm, NShrinkage = d.NShrinkage, ConsiderConcrete = d.ConsiderConcrete, ConsiderRebar = d.ConsiderRebar,
        }, h.Name);
    }

    /// <summary><paramref name="actual"/> equals bit for bit <paramref name="reference"/>: fingerprint and every public value.</summary>
    internal static void RequireEqual(BridgeCompositeSectionDefinition actual, BridgeCompositeSectionDefinition reference, string label, string what)
    {
        if (actual.Fingerprint() != reference.Fingerprint()) throw new Exception($"GPC COMPOSITE DEFINITION ({label}): fingerprint differs from {what}");
        string a = GpcCompositeSectionChecks.Dump(actual), b = GpcCompositeSectionChecks.Dump(reference);
        if (a != b)
        {
            var x = a.Split('\n'); var y = b.Split('\n'); int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            throw new Exception($"GPC COMPOSITE DEFINITION ({label}): " + (line < 0 ? "length differs from " + what : $"'{x[line]}' / {what} '{y[line]}'"));
        }
    }

    /// <summary>Parity (removed with the FeMM models): <paramref name="gpc"/>, built in GPC, equals bit for bit <paramref name="fromFem"/>, the
    /// definition of the FEM property of the same numbers (fingerprint and every public value).</summary>
    internal static void RequireSame(BridgeCompositeSectionDefinition gpc, BridgeCompositeSectionDefinition fromFem, string label) =>
        RequireEqual(gpc, fromFem, label, "the definition of the FEM property");

    /// <summary>The bits of a double, for the exact comparisons.</summary>
    internal static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);
}
#endif
