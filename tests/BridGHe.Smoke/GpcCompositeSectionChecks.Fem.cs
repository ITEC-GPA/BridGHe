using System;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using GPC.Model.Sections;
using Numbers = GpcCompositeDefinitions.Numbers;

/// <summary>
/// FEM path (removed with the FeMM models, R6.5): the parts of <c>gpc-composite-sections</c> and of the native gates of 50 and 99 that
/// read FEM composite properties. Deleting this file removes them and nothing else: the call of <see cref="FemParity"/> is a partial method.
/// <list type="bullet">
/// <item>parity: the definitions built from the numbers (<see cref="GpcCompositeDefinitions"/>) equal the definitions of the FEM properties
/// of the same numbers (constant, with the steel H of the beam or of the property, and interpolated), and the former rules on the numbers
/// (<c>CompositeAnalysisPropertyChecks</c>) give the values of the FEM properties (<c>CompareFem</c> on the same variants);</item>
/// <item>the FEM constant-section rule (material instances) and the former definitions of composite beams from the FEM properties;</item>
/// <item>the composite property GH type on a FEM property (<c>GH_CompositeBeamProperty.Definition</c>, until the part C of R6.2);</item>
/// <item>the preview of a composite beam from its definitions = from its FEM properties (the preview class is FEM until R6.5);</item>
/// <item>(P2R: the native check of 50 and 99, <c>RequireSuperElementCheck</c>, is on GPC data alone in <c>GpcCompositeSectionChecks.Gpc.cs</c>.)</item>
/// </list>
/// </summary>
internal static partial class GpcCompositeSectionChecks
{
    /// <summary>The FEM composite property of the numbers (the material parameters with only the values the numbers give).</summary>
    private static CompositeBeamPropertyModel FemProperty(Numbers x)
    {
        var steel = new SectionModel { Name = x.Name + " I", SectionType = SectionModel.SectionTypes.I, D = x.Height, B1 = x.BottomWidth, T1 = x.BottomThickness, B2 = x.TopWidth, T2 = x.TopThickness, T3 = x.Web };
        return new CompositeBeamPropertyModel(x.Name)
        {
            Name = x.Name, SteelSection = steel, ConcreteSection = new SectionConcreteSlabModel(x.SlabHeight, x.SlabWidth, x.TopArea, x.TopCover, x.BottomArea, x.BottomCover),
            SteelMaterial = new MaterialModel(x.Steel) { Name = x.Steel, Modulus = x.SteelE, MinimumYieldStress = x.SteelFy, MinimumTensileStress = x.SteelFu, PoissonRatio = x.SteelPoisson,
                ShearModulus = x.SteelShearModulus, UsePoisson = x.SteelUsePoisson, Density = x.SteelDensity, ThermalExpansion = x.SteelThermalExpansion },
            ConcreteMaterial = new MaterialModel(x.Concrete) { Name = x.Concrete, Modulus = x.ConcreteE, SpecificCompressiveStrength = x.Fck },
            RebarMaterial = new MaterialModel(x.Rebar) { Name = x.Rebar, Modulus = x.RebarE, MinimumYieldStress = x.RebarFy, MinimumTensileStress = x.RebarFu, ReinforcementUltimateStrain = x.RebarUltimateStrain },
            NInfinite = x.NLongTerm, NInstant = x.NShortTerm, NShrinkage = x.NShrinkage, VerticalOffset = x.VerticalOffset, ConsiderConcrete = x.ConsiderConcrete, ConsiderRebar = x.ConsiderRebar,
        };
    }

    static partial void FemParity(ref int checks, ref string summary)
    {
        var (g1c1, p99, p99End) = Sections();
        int definitions = 0, analysis = 0, rules = 0, typeChecks = 0, previews = 0;
        // The definitions of the numbers = the definitions of the FEM properties of the same numbers.
        foreach (var (label, n) in AnalysisVariants(g1c1, p99, p99End))
            foreach (string steelName in new[] { "B7", n.Name })
            { GpcCompositeDefinitions.RequireSame(GpcCompositeDefinitions.Create(n, steelName), GpcCompositeSections.Definition(FemProperty(n), steelName), label + " " + steelName); definitions++; }
        foreach (var (start, end) in new[] { (p99, p99End), (p99End, p99), (g1c1, g1c1 with { NLongTerm = 17.9, NShrinkage = 15.2 }) })
            foreach (double s in new[] { 0, 0.125, 0.25, 0.5, 0.75, 1 })
            {
                GpcCompositeDefinitions.RequireEqual(GpcCompositeDefinitions.Interpolated(start, end, s, "B7"), GpcCompositeSections.Definition(FemProperty(start), FemProperty(end), s, "B7"),
                    $"{start.Name} to {end.Name} at {s.ToString(CultureInfo.InvariantCulture)}", "the interpolation of the FEM properties");
                definitions++;
            }
        // The former rules on the numbers give the values of the FEM properties: the same variants compared with the FEM properties.
        foreach (var (label, n) in AnalysisVariants(g1c1, p99, p99End))
        {
            var fem = FemProperty(n);
            analysis += CompositeAnalysisPropertyChecks.CompareFem(fem, GpcCompositeSections.Definition(fem, fem.Name), label);
        }
        // The constant-section rule of the FEM properties (material instances) and the former definitions of composite beams from them.
        var femP99 = FemProperty(p99);
        if (!GpcCompositeSections.IsConstant(femP99, new CompositeBeamPropertyModel(femP99)) || GpcCompositeSections.IsConstant(femP99, FemProperty(p99End)))
            throw new Exception("GPC COMPOSITE SECTION FAIL: constant-section rule");
        rules++;
        var stations = new[] { 0, 0.125, 0.25, 0.5, 0.75, 1 };
        foreach (var c in BeamCases(g1c1, p99, p99End))
        {
            var start = FemProperty(c.Start);
            CompositeBeamPropertyModel end = c.Label switch
            {
                "same property" => start,
                "copy sharing the materials" => new CompositeBeamPropertyModel(start),
                "clone with other material instances" => ModelArchive.Clone(start),
                "other steel name" => Renamed(start, c.End.Steel),
                _ => FemProperty(c.End),
            };
            bool fem = GpcCompositeSections.IsConstant(start, end);
            if (fem != c.FemConstant) throw new Exception($"GPC COMPOSITE BEAM FAIL ({c.Label}): constant FEM {fem}, expected {c.FemConstant}");
            rules += CompareBeam(c.Label + " (FEM)", FormerBeamDefinitions(start, end, stations, "B7"), FormerDefinitions(c.Start, c.End, stations, "B7", c.FemConstant), "B7", Options) + 1;
        }
        // The contract of the composite property type on a FEM property: the constant definition of the property (steel H named by the property).
        var plugin = BridgeMergeTests.Initialize();
        var femG1c1 = FemProperty(g1c1);
        var goo = Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "GH_CompositeBeamProperty"), new object[] { femG1c1 });
        var typed = (BridgeCompositeSectionDefinition)goo.GetType().GetProperty("Definition").GetValue(goo);
        if (Dump(typed.SectionInput(true, Options[0])) != Dump(GpcCompositeDefinitions.Create(g1c1, "any").SectionInput(true, Options[0])) || typed.Name != g1c1.Name)
            throw new Exception("GPC COMPOSITE SECTION FAIL: GH_CompositeBeamProperty.Definition");
        if (typed.RebarRows == null || typed.RebarRows.BarsPerRow != GpcCompositeSections.BarsPerRow || BitConverter.DoubleToInt64Bits(typed.RebarRows.TopAreaMm2) != BitConverter.DoubleToInt64Bits(g1c1.TopArea))
            throw new Exception("GPC COMPOSITE SECTION FAIL: GH_CompositeBeamProperty.Definition without the rows of the property");
        GpcCompositeDefinitions.RequireSame(GpcCompositeDefinitions.Create(g1c1), typed, "GH type");
        typeChecks += CompositeAnalysisPropertyChecks.CompareFem(femG1c1, typed, "GH type") + CompositeAnalysisPropertyChecks.Compare(g1c1, typed, "GH type") + 3;
        // The preview of a composite beam from its definitions = from its FEM properties: outlines and bar lines, by their bits.
        static string Preview(CompositeSectionPreviewGeometry g) => string.Join(";", g.Outlines().SelectMany(c => c.ToPolyline()).Select(q => BitConverter.DoubleToInt64Bits(q.X).ToString("X16") + "," + BitConverter.DoubleToInt64Bits(q.Y).ToString("X16")))
            + "|" + string.Join(";", new[] { true, false }.SelectMany(top => Enumerable.Range(0, 3).Select(i => g.RebarPoint(top, i))).Select(q => BitConverter.DoubleToInt64Bits(q.X).ToString("X16") + "," + BitConverter.DoubleToInt64Bits(q.Y).ToString("X16")))
            + "|" + g.HasRow(true) + g.HasRow(false);
        foreach (var p in new[] { g1c1, p99, p99End, WithSlab(g1c1, 300, 3000, 0, 45, 2000, 0) })
        {
            if (Preview(new CompositeSectionPreviewGeometry(FemProperty(p))) != Preview(new CompositeSectionPreviewGeometry(GpcCompositeDefinitions.Create(p, p.Name))))
                throw new Exception($"GPC COMPOSITE BEAM FAIL (preview {p.Name}): the preview from the definition differs from the one of the property");
            previews++;
        }
        checks += definitions + analysis + rules + typeChecks + previews;
        summary = $"FEM parity (until R6.5): {definitions} definitions equal to those of the FEM properties of the same numbers, {analysis} analysis values of the FEM properties, " +
            $"{rules} checks of the former beam definitions from the FEM properties (constant rule on the instances), {typeChecks} of the composite property GH type on a FEM property, {previews} previews";
    }

    private static CompositeBeamPropertyModel Renamed(CompositeBeamPropertyModel p, string steelName) { var copy = ModelArchive.Clone(p); copy.SteelMaterial.Name = steelName; return copy; }

    /// <summary>The definitions a composite beam would give with the former rules of Super Element Check (from its FEM properties: constant
    /// rule on the material instances, interpolation of the properties, steel H named by the beam) at the normalised stations.</summary>
    internal static BridgeCompositeSectionDefinition[] FormerBeamDefinitions(CompositeBeamPropertyModel start, CompositeBeamPropertyModel end, double[] stations, string beamId)
    {
        var constant = GpcCompositeSections.IsConstant(start, end) ? GpcCompositeSections.Definition(start, beamId) : null;
        return stations.Select(s => constant ?? GpcCompositeSections.Definition(start, end, s, beamId)).ToArray();
    }

}
