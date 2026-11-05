#if GPC_PILOT
using System;
using System.Linq;
using GPC.Checkers.CompositeBridge;
using GPC.Model.Materials;
using GPC.Model.Sections;
using GPC.Model.Sections.Rebar;

/// <summary>
/// P2U (F6, Smoke tests on GPC): a composite girder section built directly in GPC, the Checker contract
/// <see cref="BridgeCompositeSectionDefinition"/> that <c>GH_CompositeBeamProperty</c> carries (chain E) and that Composite Shrinkage Actions,
/// Composite Section Geometric Property, Self Tension, Composite Beam, Super Element Check and the preview read, without any FEM model:
/// <list type="bullet">
/// <item>steel I <see cref="SectionH"/> (bottom flange B1/T1, top flange B2/T2, web, height) named as the section (as the GH type names it);</item>
/// <item>materials EN 1992 from the numbers of the material parameters: concrete from fck (parabola-rectangle), structural steel and rebar
/// from E, fy, fu with an ultimate strain of 0.1 (elastic-perfectly plastic);</item>
/// <item>three bars per row of a third of the area of the row at the pitch of a third of the slab width, the rows as given
/// (<see cref="BridgeCompositeRebarRows"/>, for the one-bar analysis section);</item>
/// <item>the analysis steel: the GPC elastic material of the steel parameter (E, ν, density, α), as the legacy rule converts a material that
/// uses Poisson's ratio without analysis data;</item>
/// <item>the modular ratios and the Consider Concrete / Consider Rebar choices.</item>
/// </list>
/// The same rules as <c>GpcCompositeSections.Definition</c> on a composite property of the same numbers: the tests compare the two
/// definitions bit for bit (fingerprint and every public value) while the FEM properties exist.
/// </summary>
internal static class GpcCompositeDefinitions
{
    internal const int BarsPerRow = 3;

    /// <summary>The numbers of a composite section in N-mm-MPa (the inputs of Composite Beam Property).</summary>
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
        public string Concrete { get; init; } = "C30";
        public double Fck { get; init; } = 30;
        public string Steel { get; init; } = "S355";
        public double SteelE { get; init; } = 210000;
        public double SteelFy { get; init; } = 355;
        public double SteelFu { get; init; } = 510;
        public double SteelPoisson { get; init; } = 0.3;
        public double SteelDensity { get; init; }
        public double SteelThermalExpansion { get; init; }
        public string Rebar { get; init; } = "B450";
        public double RebarE { get; init; } = 210000;
        public double RebarFy { get; init; } = 450;
        public double RebarFu { get; init; } = 540;
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

    /// <summary>The definition of a constant section from its numbers.</summary>
    internal static BridgeCompositeSectionDefinition Create(Numbers x)
    {
        if (x == null) throw new ArgumentNullException(nameof(x));
        var h = new SectionH(x.Height, x.Web, x.TopWidth, x.TopThickness, x.BottomWidth, x.BottomThickness, x.Name);
        var concrete = new ConcreteMaterialEN1992(x.Concrete, x.Fck, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
        var steel = new SteelMaterialEN1992(x.Steel, x.SteelE, x.SteelFy, x.SteelFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
        var rebar = new SteelMaterialEN1992(x.Rebar, x.RebarE, x.RebarFy, x.RebarFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
        double top = Math.Sqrt(x.TopArea / BarsPerRow * 4 / Math.PI), bottom = Math.Sqrt(x.BottomArea / BarsPerRow * 4 / Math.PI);
        var section = BridGHe.Common.Checks.CompositeSectionFactory.Create(x.SlabWidth, x.SlabHeight, concrete,
            top != 0 ? new RebarSectionCircular("", top, rebar) : null, x.SlabWidth / BarsPerRow, x.TopCover,
            bottom != 0 ? new RebarSectionCircular("", bottom, rebar) : null, x.SlabWidth / BarsPerRow, h, steel, x.BottomCover, 0, x.Name);
        var rows = new BridgeCompositeRebarRows(x.TopArea, x.TopCover, x.BottomArea, x.BottomCover, BarsPerRow);
        var analysisSteel = new Material(x.Steel, x.SteelE, x.SteelPoisson, x.SteelDensity, x.SteelThermalExpansion);
        return new BridgeCompositeSectionDefinition(x.Name, section, rows, analysisSteel, x.NLongTerm, x.NShortTerm, x.NShrinkage, 0, x.ConsiderConcrete, x.ConsiderRebar);
    }

    /// <summary>Parity (removed with the FeMM models): <paramref name="gpc"/>, built in GPC, equals bit for bit <paramref name="fromFem"/>, the
    /// definition of the FEM property of the same numbers (fingerprint and every public value).</summary>
    internal static void RequireSame(BridgeCompositeSectionDefinition gpc, BridgeCompositeSectionDefinition fromFem, string label)
    {
        if (gpc.Fingerprint() != fromFem.Fingerprint()) throw new Exception($"GPC COMPOSITE DEFINITION ({label}): fingerprint differs from the definition of the FEM property");
        string a = GpcCompositeSectionChecks.Dump(gpc), b = GpcCompositeSectionChecks.Dump(fromFem);
        if (a != b)
        {
            var x = a.Split('\n'); var y = b.Split('\n'); int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            throw new Exception($"GPC COMPOSITE DEFINITION ({label}): " + (line < 0 ? "length differs" : $"GPC '{x[line]}' / FEM '{y[line]}'"));
        }
    }

    /// <summary>The bits of a double, for the exact comparisons.</summary>
    internal static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);
}
#endif
