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
using GPC.Model.Results;
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
        int beamChecks = BeamDefinitions(g1c1, p99, p99End, options);
        Console.WriteLine($"GPC COMPOSITE SECTIONS PASS: {compared} section inputs of the definitions equal bit for bit to the former construction and its adapter (constant and variable sections, with and without bars, 3 option sets); " +
            $"{analysis} analysis values or variants of {analysed.Count + 1} definitions equal bit for bit to their FEM properties (self tension: {CompositeAnalysisPropertyChecks.EchoedRebarDifferences} results with the echoed rebar material of ultimate strain 0.1 instead of the property one, stresses equal); " +
            $"chain E (c): {beamChecks} checks of the definitions of the composite beams (constant rule by value, interpolation from the definitions, steel H name, Linear Tension, preview) equal to the former FEM path.");
    }

    /// <summary>The definitions a composite beam would give with the former rules of Super Element Check (from its FEM properties: constant
    /// rule on the material instances, interpolation of the properties, steel H named by the beam) at the normalised stations.</summary>
    internal static BridgeCompositeSectionDefinition[] FormerBeamDefinitions(CompositeBeamPropertyModel start, CompositeBeamPropertyModel end, double[] stations, string beamId)
    {
        var constant = GpcCompositeSections.IsConstant(start, end) ? GpcCompositeSections.Definition(start, beamId) : null;
        return stations.Select(s => constant ?? GpcCompositeSections.Definition(start, end, s, beamId)).ToArray();
    }

    /// <summary>The same from the definitions the composite beam carries (chain E (c), the rules of Super Element Check now).</summary>
    internal static BridgeCompositeSectionDefinition[] BeamDefinitions(BridgeCompositeSectionDefinition start, BridgeCompositeSectionDefinition end, double[] stations)
    {
        var constant = GpcCompositeSections.IsConstant(start, end) ? start : null;
        return stations.Select(s => constant ?? GpcCompositeSections.Definition(start, end, s)).ToArray();
    }

    /// <summary>Lines of the dumps of two sections that differ, when they differ only by the name of the steel H (the former checks named it by
    /// the beam, the definitions by the composite property); an exception for any other difference.</summary>
    internal static int SteelNameLines(string label, ReinforcedConcreteSection former, ReinforcedConcreteSection gpc, string formerName, string gpcName)
    {
        var x = Dump(former).Split('\n'); var y = Dump(gpc).Split('\n');
        if (x.Length != y.Length) throw new Exception($"GPC COMPOSITE BEAM FAIL ({label}): section dumps of different length");
        int differences = 0;
        for (int i = 0; i < x.Length; i++)
            if (x[i] != y[i])
            {
                if (x[i].Replace("'" + formerName + "'", "'#'") != y[i].Replace("'" + gpcName + "'", "'#'"))
                    throw new Exception($"GPC COMPOSITE BEAM FAIL ({label}): the sections differ beyond the name of the steel H: former '{x[i]}' / GPC '{y[i]}'");
                differences++;
            }
        return differences;
    }

    /// <summary>Compares the definitions of one composite beam per station (former FEM rules / definitions), bit for bit: modular ratios,
    /// choices, rows, the section (but the name of the steel H) and the input of the H bridge engine for every option set, with and without
    /// bars. The number of comparisons.</summary>
    internal static int CompareBeam(string label, BridgeCompositeSectionDefinition[] former, BridgeCompositeSectionDefinition[] gpc, string formerName, BridgeAnalysisOptions[] options)
    {
        if (former.Length != gpc.Length) throw new Exception($"GPC COMPOSITE BEAM FAIL ({label}): {former.Length} / {gpc.Length} stations");
        static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");
        int compared = 0;
        for (int k = 0; k < former.Length; k++)
        {
            BridgeCompositeSectionDefinition a = former[k], b = gpc[k];
            string Values(BridgeCompositeSectionDefinition d) => string.Join(" ", new[] { d.NLongTerm, d.NShortTerm, d.NShrinkage, d.VerticalOffsetMm }.Select(B))
                + $" {d.ConsiderConcrete} {d.ConsiderRebar} '{d.Name}' " + (d.RebarRows == null ? "-" : string.Join(" ", new[] { d.RebarRows.TopAreaMm2, d.RebarRows.TopCoverMm, d.RebarRows.BottomAreaMm2, d.RebarRows.BottomCoverMm }.Select(B)) + " " + d.RebarRows.BarsPerRow);
            if (Values(a) != Values(b)) throw new Exception($"GPC COMPOSITE BEAM FAIL ({label}, station {k}): ratios, choices or rows: former '{Values(a)}' / GPC '{Values(b)}'");
            string gpcName = ((SectionH)b.Section.SteelSections[0].Section.SectionShape).Name;
            SteelNameLines($"{label}, station {k}", a.Section, b.Section, formerName, gpcName);
            foreach (var o in options)
                foreach (bool rebars in new[] { true, false })
                {
                    string x = Dump(a.SectionInput(rebars, o)), y = Dump(b.SectionInput(rebars, o));
                    if (x != y) throw new Exception($"GPC COMPOSITE BEAM FAIL ({label}, station {k}, rebars {rebars}): the input of the H bridge engine differs");
                    compared++;
                }
        }
        return compared;
    }

    /// <summary>
    /// Chain E (c), native (50, 99): for every composite beam of a solved Super Element Check, the definitions the component builds now (from
    /// the definitions the beam carries: <c>GH_CompositeBeam.StartDefinition</c>/<c>EndDefinition</c>) against those of the former FEM path
    /// (from the FEM properties of the beam, steel H named by the beam), at the stations of the check (the merged samples of the GPC bridge
    /// model), with the options of the component: ratios, choices, rows, section (but the steel H name) and input of the H bridge engine with
    /// and without bars, bit for bit. The fingerprints of the runs show that the name of the steel H does not reach the verdicts.
    /// </summary>
    internal static void RequireSuperElementCheck(Grasshopper.Kernel.GH_Component check, string label)
    {
        void Fail(string what) => throw new Exception("GPC COMPOSITE BEAM FAIL (" + label + " " + check.NickName + "): " + what);
        var goo = check.Params.Input[0].VolatileData.AllData(true).Single();
        var bridge = (GpcBridge)goo.GetType().GetProperty("GpcBridge").GetValue(goo) ?? throw new Exception("GPC COMPOSITE BEAM FAIL (" + label + "): no GPC bridge model");
        bool Flag(int input) => check.Params.Input[input].VolatileData.AllData(true).OfType<Grasshopper.Kernel.Types.GH_Boolean>().Single().Value;
        var options = new[] { new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare, WebBuckling = Flag(2), TopFlangeBuckling = Flag(3), BottomFlangeBuckling = Flag(4) } };
        int beams = 0, stations = 0, constant = 0, constantByValue = 0, shared = 0, compared = 0, nameLines = 0;
        foreach (var g in check.Params.Input[1].VolatileData.AllData(true))
        {
            var fem = (CompositeBeamModel)((Grasshopper.Kernel.Types.IGH_Goo)g).ScriptVariable();
            var start = (BridgeCompositeSectionDefinition)g.GetType().GetProperty("StartDefinition").GetValue(g);
            var end = (BridgeCompositeSectionDefinition)g.GetType().GetProperty("EndDefinition").GetValue(g);
            if (start == null || end == null) Fail($"beam {fem.BeamId} without its definitions");
            var samples = bridge.SamplesOf(fem.BeamId) ?? throw new Exception($"GPC COMPOSITE BEAM FAIL ({label}): beam {fem.BeamId} not in the GPC bridge model");
            var at = samples.Select(s => Math.Clamp(s.Sample.Station / fem.Length, 0, 1)).ToHashSet().OrderBy(v => v).ToArray();
            bool femConstant = GpcCompositeSections.IsConstant(fem.StartBeamProperty, fem.EndBeamProperty), gpcConstant = GpcCompositeSections.IsConstant(start, end);
            if (femConstant && !gpcConstant) Fail($"beam {fem.BeamId}: constant on the FEM properties, variable on the definitions");
            if (femConstant) constant++; else if (gpcConstant) constantByValue++;
            if (ReferenceEquals(start, end)) shared++;
            var former = FormerBeamDefinitions(fem.StartBeamProperty, fem.EndBeamProperty, at, fem.BeamId);
            var now = BeamDefinitions(start, end, at);
            compared += CompareBeam($"{label} {fem.BeamId}", former, now, fem.BeamId, options);
            nameLines += SteelNameLines($"{label} {fem.BeamId}", former[0].Section, now[0].Section, fem.BeamId, ((SectionH)now[0].Section.SteelSections[0].Section.SectionShape).Name);
            beams++; stations += at.Length;
        }
        if (beams == 0) Fail("no composite beams");
        Console.WriteLine($"GPC COMPOSITE BEAM PASS ({label} {check.NickName}): {beams} composite beams, {stations} stations; constant {constant} (FEM rule), {constantByValue} more by value, {shared} with one definition at both ends; " +
            $"{compared} inputs of the H bridge engine from the definitions of the beams equal bit for bit to the former FEM path (ratios, choices, rows and sections too; {nameLines} section values differ only by the name of the steel H).");
    }

    /// <summary>Chain E (c), not native: the definitions of composite beams (Composite Beam: the definitions of its two composite properties)
    /// against the former FEM path of Super Element Check, Composite Beam Linear Tension Analysis and the preview.</summary>
    private static int BeamDefinitions(CompositeBeamPropertyModel g1c1, CompositeBeamPropertyModel p99, CompositeBeamPropertyModel p99End, BridgeAnalysisOptions[] options)
    {
        int checks = 0;
        BridgeCompositeSectionDefinition D(CompositeBeamPropertyModel p) => GpcCompositeSections.Definition(p, p.Name);
        CompositeBeamPropertyModel Variant(CompositeBeamPropertyModel p, Action<CompositeBeamPropertyModel> change) { var copy = ModelArchive.Clone(p); change(copy); return copy; }
        var stations = new[] { 0, 0.125, 0.25, 0.5, 0.75, 1 };
        // Constant-section rule: by value on the definitions; the FEM rule on the material instances.
        var sameInstances = new CompositeBeamPropertyModel(p99); var cloned = ModelArchive.Clone(p99);
        var renamed = Variant(p99, x => x.SteelMaterial.Name = "S355 bis");
        var cases = new (string Label, CompositeBeamPropertyModel Start, CompositeBeamPropertyModel End, bool FemConstant, bool GpcConstant)[]
        {
            ("same property", p99, p99, true, true),
            ("copy sharing the materials", p99, sameInstances, true, true),
            ("clone with other material instances", p99, cloned, false, true), // listed difference: one definition instead of one per station, the same sections
            ("other steel name", p99, renamed, false, false),
            ("variable 99", p99, p99End, false, false),
            ("variable reversed", p99End, p99, false, false),
            ("top row only to both rows", Variant(g1c1, x => x.ConcreteSection = new SectionConcreteSlabModel(300, 3000, 3500, 45, 0, 50)), g1c1, false, false),
            ("different covers", g1c1, Variant(g1c1, x => x.ConcreteSection = new SectionConcreteSlabModel(300, 3000, 3500, 40, 3500, 60)), false, false),
            ("other ratios", g1c1, Variant(g1c1, x => { x.NInfinite = 17.9; x.NShrinkage = 15.2; }), false, false),
        };
        foreach (var c in cases)
        {
            bool fem = GpcCompositeSections.IsConstant(c.Start, c.End);
            var (ds, de) = (D(c.Start), ReferenceEquals(c.Start, c.End) ? D(c.Start) : D(c.End));
            bool gpc = GpcCompositeSections.IsConstant(ds, de);
            if (fem != c.FemConstant || gpc != c.GpcConstant) throw new Exception($"GPC COMPOSITE BEAM FAIL ({c.Label}): constant FEM {fem} / definitions {gpc}, expected {c.FemConstant} / {c.GpcConstant}");
            var former = FormerBeamDefinitions(c.Start, c.End, stations, "B7");
            var now = BeamDefinitions(ds, de, stations);
            checks += CompareBeam(c.Label, former, now, "B7", options) + 1;
            // The interpolation from the definitions with the name of the former checks: the same section, dump for dump.
            if (!gpc)
                foreach (double s in stations)
                {
                    if (Dump(GpcCompositeSections.Definition(ds, de, s, "B7").Section) != Dump(GpcCompositeSections.Definition(c.Start, c.End, s, "B7").Section))
                        throw new Exception($"GPC COMPOSITE BEAM FAIL ({c.Label}, station {s}): the interpolation of the definitions differs from that of the properties");
                    checks++;
                }
        }
        // Composite Beam Linear Tension Analysis (constant beams): the stresses of the adapter on the section of the start definition = on
        // the former section of the component (steel H named by the beam), every value of the three results by its bits.
        foreach (var p in new[] { g1c1, p99 })
        {
            var (formerSection, _) = Former(p, null, 0, "B7");
            var section = D(p).Section;
            foreach (bool concrete in new[] { true, false })
            {
                string Run(ReinforcedConcreteSection rc)
                {
                    var cs = new CoordinateSystem(rc.GetHomogenizedCentroid(out double _, out double _), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0));
                    var steel = rc.SteelSections[0].Section.SteelMaterial; var cm = rc.ConcreteMaterial;
                    double phiInf = BridGHe.Common.Checks.BridgeCreepCoefficient.FromModularRatio(18, steel.ElasticModulusTension, cm.ElasticModulusCompression), phiInst = BridGHe.Common.Checks.BridgeCreepCoefficient.FromModularRatio(7, steel.ElasticModulusTension, cm.ElasticModulusCompression);
                    var o = options[0];
                    var adapter = new BridgeCheckAdapter(rc, cs, o);
                    bool ok = adapter.GetLinearAnalysisResultByStages(out var r1, out var r2, out var r3, out double tau, phiInf, phiInst,
                        new ResultBeamForces(-1.5e5, 2e4, -3e5, 1e6, -2.5e9, 3e7, cs), new ResultBeamForces(2e5, -1e4, 1.2e5, -2e6, 1.8e9, -4e7, cs),
                        new ResultBeamForces(-3e5, 5e3, 2.2e5, 3e6, 3.1e9, 1e7, cs), concrete);
                    return ok + "\n" + Dump(r1) + Dump(r2) + Dump(r3) + BitConverter.DoubleToInt64Bits(tau).ToString("X16");
                }
                if (Run(formerSection) != Run(section)) throw new Exception($"GPC COMPOSITE BEAM FAIL (Linear Tension {p.Name}, concrete {concrete}): results differ");
                checks++;
            }
        }
        // The preview of a composite beam from its definitions = from its FEM properties: outlines and bar lines, by their bits.
        static string Preview(CompositeSectionPreviewGeometry g) => string.Join(";", g.Outlines().SelectMany(c => c.ToPolyline()).Select(q => BitConverter.DoubleToInt64Bits(q.X).ToString("X16") + "," + BitConverter.DoubleToInt64Bits(q.Y).ToString("X16")))
            + "|" + string.Join(";", new[] { true, false }.SelectMany(top => Enumerable.Range(0, 3).Select(i => g.RebarPoint(top, i))).Select(q => BitConverter.DoubleToInt64Bits(q.X).ToString("X16") + "," + BitConverter.DoubleToInt64Bits(q.Y).ToString("X16")))
            + "|" + g.HasRow(true) + g.HasRow(false);
        foreach (var p in new[] { g1c1, p99, p99End, Variant(g1c1, x => x.ConcreteSection = new SectionConcreteSlabModel(300, 3000, 0, 45, 2000, 0)) })
        {
            if (Preview(new CompositeSectionPreviewGeometry(p)) != Preview(new CompositeSectionPreviewGeometry(D(p))))
                throw new Exception($"GPC COMPOSITE BEAM FAIL (preview {p.Name}): the preview from the definition differs from the one of the property");
            checks++;
        }
        return checks;
    }
}
#endif
