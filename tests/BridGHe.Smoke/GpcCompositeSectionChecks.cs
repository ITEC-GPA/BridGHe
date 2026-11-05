#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using GPC.Geometry;
using GPC.Model.Materials;
using GPC.Model.Sections;
using GPC.Model.Sections.Concrete;
using GPC.Model.Sections.Rebar;
using GPC.Utilities.Maths;

/// <summary>
/// F6 (L11): the input of the H bridge engine of Super Element Check from the GPC contract of the composite section
/// (<see cref="GpcCompositeSections"/> → <see cref="BridgeCompositeSectionDefinition.SectionInput(bool, BridgeAnalysisOptions)"/>) against the
/// former construction of the component (kept here as the reference, <see cref="Former"/>) and <see cref="BridgeCheckAdapter.SectionInput"/>:
/// every property of the input (doubles by their bits, materials by their public values), constant and interpolated sections.
/// </summary>
internal static class GpcCompositeSectionChecks
{
    /// <summary>TESTS ONLY: the section of the former Super Element Check (constant section: <paramref name="end"/> null).</summary>
    private static (ReinforcedConcreteSection Section, double[] Ratios) Former(CompositeBeamPropertyModel start, CompositeBeamPropertyModel end, double station, string beamId)
    {
        double L(double a, double b) => Interpolation.GetLinearInterpolation(0, 1, a, b, station);
        double L1(double a, double b) => Interpolation.GetLinearInterpolation(0, 1.0, a, b, station);
        if (end == null)
        {
            var s = start.SteelSection; var c = start.ConcreteSection; var cm = start.ConcreteMaterial; var sm = start.SteelMaterial; var rm = start.RebarMaterial;
            var sectionH = new SectionH(s.D, s.T3, s.B2, s.T2, s.B1, s.T1, beamId);
            var concrete = new ConcreteMaterialEN1992(cm.Name, cm.SpecificCompressiveStrength, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
            var steel = new SteelMaterialEN1992(sm.Name, sm.Modulus, sm.MinimumYieldStress, sm.MinimumTensileStress, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
            var rebar = new SteelMaterialEN1992(rm.Name, rm.Modulus, rm.MinimumYieldStress, rm.MinimumTensileStress, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
            int numberRebars = 3; double rebarAreaTop = c.TopRebarArea / numberRebars; double rebarDiameterTop = Math.Sqrt(rebarAreaTop * 4 / Math.PI);
            var top = rebarDiameterTop != 0 ? new RebarSectionCircular("", rebarDiameterTop, rebar) : null;
            double rebarDiameterBottom = Math.Sqrt(c.BottomRebarArea / numberRebars * 4 / Math.PI);
            var bottom = rebarDiameterBottom != 0 ? new RebarSectionCircular("", rebarDiameterBottom, rebar) : null;
            var rc = CompositeSectionFactory.Create(c.Width, c.Height, concrete, top, c.Width / numberRebars, c.TopCover, bottom, c.Width / numberRebars, sectionH, steel, c.BottomCover, start.VerticalOffset, start.Name);
            return (rc, new[] { start.NInfinite, start.NInstant, start.NShrinkage });
        }
        {
            var s = start.SteelSection; var e = end.SteelSection; var cs = start.ConcreteSection; var ce = end.ConcreteSection;
            double width = L(cs.Width, ce.Width), slabHeight = L(cs.Height, ce.Height), cover = L(cs.TopCover, ce.TopCover), offset = L(start.VerticalOffset, end.VerticalOffset);
            double nInfinite = L(start.NInfinite, end.NInfinite), nInstant = L(start.NInstant, end.NInstant);
            var sectionH = new SectionH(L1(s.D, e.D), L1(s.T3, e.T3), L1(s.B2, e.B2), L1(s.T2, e.T2), L1(s.B1, e.B1), L1(s.T1, e.T1), beamId);
            var concrete = new ConcreteMaterialEN1992(start.ConcreteMaterial.Name, L1(start.ConcreteMaterial.SpecificCompressiveStrength, end.ConcreteMaterial.SpecificCompressiveStrength), ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
            var steel = new SteelMaterialEN1992(start.SteelMaterial.Name, L1(start.SteelMaterial.Modulus, end.SteelMaterial.Modulus), L1(start.SteelMaterial.MinimumYieldStress, end.SteelMaterial.MinimumYieldStress),
                L1(start.SteelMaterial.MinimumTensileStress, end.SteelMaterial.MinimumTensileStress), 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
            var rebar = new SteelMaterialEN1992(start.RebarMaterial.Name, L1(start.RebarMaterial.Modulus, end.RebarMaterial.Modulus), L1(start.RebarMaterial.MinimumYieldStress, end.RebarMaterial.MinimumYieldStress),
                L1(start.RebarMaterial.MinimumTensileStress, end.RebarMaterial.MinimumTensileStress), 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
            double rebarsArea = L1(cs.TopRebarArea, ce.TopRebarArea); int numberRebars = 3; double rebarDiameter = Math.Sqrt(rebarsArea / numberRebars * 4 / Math.PI);
            var top = rebarDiameter != 0 ? new RebarSectionCircular("", rebarDiameter, rebar) : null;
            double bottomArea = L(cs.BottomRebarArea, ce.BottomRebarArea), bottomCover = L(cs.BottomCover, ce.BottomCover);
            double rebarDiameterBottom = Math.Sqrt(bottomArea / numberRebars * 4 / Math.PI);
            var bottom = rebarDiameterBottom != 0 ? new RebarSectionCircular("", rebarDiameterBottom, rebar) : null;
            var rc = CompositeSectionFactory.Create(width, slabHeight, concrete, top, width / numberRebars, cover, bottom, width / numberRebars, sectionH, steel, bottomCover, offset, start.Name);
            return (rc, new[] { nInfinite, nInstant, L(start.NShrinkage, end.NShrinkage) });
        }
    }

    /// <summary>Every public value reachable from <paramref name="value"/> (doubles by their bits; depth-limited, cycles cut).</summary>
    internal static string Dump(object value)
    {
        var text = new System.Text.StringBuilder(); var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object v, string path, int depth)
        {
            switch (v)
            {
                case null: text.Append(path).Append("=null\n"); return;
                case double d: text.Append(path).Append('=').Append(BitConverter.DoubleToInt64Bits(d).ToString("X16")).Append('\n'); return;
                case float f: text.Append(path).Append('=').Append(BitConverter.SingleToInt32Bits(f).ToString("X8")).Append('\n'); return;
                case string s: text.Append(path).Append("='").Append(s).Append("'\n"); return;
                case Guid: text.Append(path).Append("=<guid>\n"); return; // identities of the GPC values (random per construction)
                case Enum or bool or int or long: text.Append(path).Append('=').Append(Convert.ToString(v, CultureInfo.InvariantCulture)).Append('\n'); return;
            }
            if (depth > 5 || !seen.Add(v)) { text.Append(path).Append("=<").Append(v.GetType().Name).Append(">\n"); return; }
            if (v is System.Collections.IEnumerable items) { int i = 0; foreach (var item in items) Walk(item, path + "[" + i++ + "]", depth + 1); return; }
            foreach (var p in v.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                object x; try { x = p.GetValue(v); } catch (TargetInvocationException) { continue; }
                Walk(x, path + "." + p.Name, depth + 1);
            }
        }
        Walk(value, "", 0);
        return text.ToString();
    }

    /// <summary>Regression (not native) <c>gpc-composite-sections</c>: the sections of examples 50 (G1-C1) and 99, constant and variable (four
    /// stations), with and without bars, three sets of options: the definition's input = the former construction's adapter input, the
    /// modular ratios equal, and the definition of the composite property type (GH_CompositeBeamProperty.Definition) the constant one.</summary>
    internal static void Regression()
    {
        MaterialModel Material(string name, double e, double fy, double fu, double fck) =>
            new MaterialModel(name) { Name = name, Modulus = e, MinimumYieldStress = fy, MinimumTensileStress = fu, SpecificCompressiveStrength = fck };
        CompositeBeamPropertyModel Property(string name, double d, double b1, double t1, double b2, double t2, double tw, double slabWidth, double slabHeight, double rows, double nInf, double nInst, double nSh)
        {
            var steel = new SectionModel { Name = name + " I", SectionType = SectionModel.SectionTypes.I, D = d, B1 = b1, T1 = t1, B2 = b2, T2 = t2, T3 = tw };
            return new CompositeBeamPropertyModel(name)
            {
                Name = name, SteelSection = steel, ConcreteSection = new SectionConcreteSlabModel(slabHeight, slabWidth, rows, 50, rows, 50),
                SteelMaterial = Material("S355", 210000, 355, 510, 0), ConcreteMaterial = Material("C32/40", 33345.7645, 0, 0, 32), RebarMaterial = Material("B450C", 200000, 450, 540, 0),
                NInfinite = nInf, NInstant = nInst, NShrinkage = nSh, ConsiderConcrete = true, ConsiderRebar = true,
            };
        }
        var g1c1 = Property("G1-C1", 2500, 600, 30, 600, 30, 16, 3000, 300, 3500, 18.2, 6.3, 16.1);
        var p99 = Property("Sezione 99", 1800, 800, 40, 600, 30, 15, 2500, 300, 1600 * Math.PI, 17.5, 6.1, 15.8);
        var p99End = Property("Sezione 99 fine", 2200, 900, 45, 650, 35, 17, 2700, 320, 2000 * Math.PI, 18.5, 6.4, 16.6);
        var options = new[]
        {
            new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare, WebBuckling = true, TopFlangeBuckling = true, BottomFlangeBuckling = true },
            new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare, WebBuckling = false, TopFlangeBuckling = false, BottomFlangeBuckling = false },
            new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare, WebBuckling = true, TopFlangeBuckling = false, BottomFlangeBuckling = true },
        };
        int compared = 0;
        void Compare(string label, CompositeBeamPropertyModel start, CompositeBeamPropertyModel end, double station)
        {
            var definition = end == null ? GpcCompositeSections.Definition(start, "B7") : GpcCompositeSections.Definition(start, end, station, "B7");
            var (former, ratios) = Former(start, end, station, "B7");
            if (!new[] { definition.NLongTerm, definition.NShortTerm, definition.NShrinkage }.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(ratios.Select(BitConverter.DoubleToInt64Bits)))
                throw new Exception($"GPC COMPOSITE SECTION FAIL ({label}): modular ratios differ");
            string formerSection = Dump(former), section = Dump(definition.Section);
            if (formerSection != section) throw new Exception($"GPC COMPOSITE SECTION FAIL ({label}): section differs");
            foreach (var o in options)
                foreach (bool rebars in new[] { true, false })
                {
                    var adapter = new BridgeCheckAdapter(former, new CoordinateSystem(former.Centroid, new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0)), o).SectionInput(rebars);
                    var input = definition.SectionInput(rebars, o);
                    string a = Dump(adapter), b = Dump(input);
                    if (a != b)
                    {
                        var x = a.Split('\n'); var y = b.Split('\n'); int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
                        throw new Exception($"GPC COMPOSITE SECTION FAIL ({label}, rebars {rebars}): section input differs: " + (line < 0 ? "length" : $"former '{x[line]}' / GPC '{y[line]}'"));
                    }
                    compared++;
                }
        }
        Compare("50 G1-C1", g1c1, null, 0);
        Compare("99", p99, null, 0);
        if (!GpcCompositeSections.IsConstant(p99, new CompositeBeamPropertyModel(p99)) || GpcCompositeSections.IsConstant(p99, p99End))
            throw new Exception("GPC COMPOSITE SECTION FAIL: constant-section rule");
        foreach (double s in new[] { 0, 0.25, 0.5, 1 }) Compare("99 variable " + s.ToString(CultureInfo.InvariantCulture), p99, p99End, s);
        // The contract of the composite property type: the constant definition of the property (steel H named by the property).
        var plugin = BridgeMergeTests.Initialize();
        var goo = Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "GH_CompositeBeamProperty"), new object[] { g1c1 });
        var typed = (BridgeCompositeSectionDefinition)goo.GetType().GetProperty("Definition").GetValue(goo);
        if (Dump(typed.SectionInput(true, options[0])) != Dump(GpcCompositeSections.Definition(g1c1, "any").SectionInput(true, options[0])) || typed.Name != g1c1.Name)
            throw new Exception("GPC COMPOSITE SECTION FAIL: GH_CompositeBeamProperty.Definition");
        if (compared != 36) throw new Exception($"GPC COMPOSITE SECTION FAIL: {compared} inputs compared instead of 36");
        // Chain E step 1: the analysis properties (one bar per row) from the definition = those of the FEM property, on the same properties
        // and on variants of the rows, of the choices and of the rebar material (B450C of BridGHe: ultimate strain 0.075, not in the values).
        CompositeBeamPropertyModel Variant(CompositeBeamPropertyModel p, Action<CompositeBeamPropertyModel> change) { var copy = ModelArchive.Clone(p); change(copy); return copy; }
        var analysed = new List<(string, CompositeBeamPropertyModel)>
        {
            ("50 G1-C1", g1c1), ("99", p99), ("99 end", p99End),
            ("top row only", Variant(g1c1, x => x.ConcreteSection = new SectionConcreteSlabModel(300, 3000, 3500, 45, 0, 50))),
            ("bottom row only", Variant(p99, x => x.ConcreteSection = new SectionConcreteSlabModel(250, 2500, 0, 50, 2711.3, 62.5))),
            ("different covers", Variant(p99End, x => x.ConcreteSection = new SectionConcreteSlabModel(320, 2700, 4021.24, 47.5, 1884.96, 70))),
            ("no bars", Variant(g1c1, x => x.ConcreteSection = new SectionConcreteSlabModel(300, 3000, 0, 50, 0, 50))),
            ("rebars not considered", Variant(g1c1, x => x.ConsiderRebar = false)),
            ("concrete not considered", Variant(p99, x => x.ConsiderConcrete = false)),
            ("B450C of BridGHe", Variant(g1c1, x => x.RebarMaterial = ReinforcementMaterials.B450C())),
        };
        int analysis = 0;
        foreach (var (label, p) in analysed) analysis += CompositeAnalysisPropertyChecks.Compare(p, GpcCompositeSections.Definition(p, p.Name), label);
        // The definition of the GH type carries the rows.
        if (typed.RebarRows == null || typed.RebarRows.BarsPerRow != GpcCompositeSections.BarsPerRow || BitConverter.DoubleToInt64Bits(typed.RebarRows.TopAreaMm2) != BitConverter.DoubleToInt64Bits(g1c1.ConcreteSection.TopRebarArea))
            throw new Exception("GPC COMPOSITE SECTION FAIL: GH_CompositeBeamProperty.Definition without the rows of the property");
        analysis += CompositeAnalysisPropertyChecks.Compare(g1c1, typed, "GH type");
        Console.WriteLine($"GPC COMPOSITE SECTIONS PASS: {compared} section inputs of the definitions equal bit for bit to the former construction and its adapter (constant and variable sections, with and without bars, 3 option sets); " +
            $"{analysis} analysis values or variants of {analysed.Count + 1} definitions equal bit for bit to their FEM properties (self tension: {CompositeAnalysisPropertyChecks.EchoedRebarDifferences} results with the echoed rebar material of ultimate strain 0.1 instead of the property one, stresses equal).");
    }
}
#endif
