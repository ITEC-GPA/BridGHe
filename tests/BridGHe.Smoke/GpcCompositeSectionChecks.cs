#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;
using GPC.Checkers.CompositeBridge;
using GPC.Geometry;
using GPC.Model.Materials;
using GPC.Model.Results;
using GPC.Model.Sections;
using GPC.Model.Sections.Concrete;
using GPC.Model.Sections.Rebar;
using GPC.Utilities.Maths;
using Numbers = GpcCompositeDefinitions.Numbers;

/// <summary>
/// F6 (L11): the input of the H bridge engine of Super Element Check from the GPC contract of the composite section
/// (<see cref="BridgeCompositeSectionDefinition.SectionInput(bool, BridgeAnalysisOptions)"/>) against the former construction of the
/// component (kept here as the reference, <see cref="Former"/>) and <see cref="BridgeCheckAdapter.SectionInput"/>: every property of the
/// input (doubles by their bits, materials by their public values), constant and interpolated sections.
/// <para>R6.6 (P2U): without FEM models. The composite properties of the checks are their numbers (<see cref="Numbers"/>); the definitions
/// are built from them with the rules of the production construction (<see cref="GpcCompositeDefinitions"/>), which the R6.0 capture ties
/// to the production code: every captured definition (the payloads of the example definitions read by value and the outputs of Composite
/// Beam Property, 44) is rebuilt from its numbers bit for bit, and the former construction and the adapter give its input of the H bridge
/// engine. While the FEM properties exist, <c>GpcCompositeSectionChecks.Fem.cs</c> keeps the former comparisons with them (the parity, the
/// FEM constant-section rule, the composite property GH type, the preview) and the native check of Super Element Check; that file goes
/// with the FeMM models.</para>
/// </summary>
internal static partial class GpcCompositeSectionChecks
{
    /// <summary>TESTS ONLY: the section of the former Super Element Check from the numbers of its composite properties (constant section:
    /// <paramref name="end"/> null), the construction of the component before L11 kept as the reference.</summary>
    internal static (ReinforcedConcreteSection Section, double[] Ratios) Former(Numbers start, Numbers end, double station, string beamId)
    {
        double L(double a, double b) => Interpolation.GetLinearInterpolation(0, 1, a, b, station);
        double L1(double a, double b) => Interpolation.GetLinearInterpolation(0, 1.0, a, b, station);
        if (end == null)
        {
            var s = start;
            var sectionH = new SectionH(s.Height, s.Web, s.TopWidth, s.TopThickness, s.BottomWidth, s.BottomThickness, beamId);
            var concrete = new ConcreteMaterialEN1992(s.Concrete, s.Fck, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
            var steel = new SteelMaterialEN1992(s.Steel, s.SteelE, s.SteelFy, s.SteelFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
            var rebar = new SteelMaterialEN1992(s.Rebar, s.RebarE, s.RebarFy, s.RebarFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
            int numberRebars = 3; double rebarAreaTop = s.TopArea / numberRebars; double rebarDiameterTop = Math.Sqrt(rebarAreaTop * 4 / Math.PI);
            var top = rebarDiameterTop != 0 ? new RebarSectionCircular("", rebarDiameterTop, rebar) : null;
            double rebarDiameterBottom = Math.Sqrt(s.BottomArea / numberRebars * 4 / Math.PI);
            var bottom = rebarDiameterBottom != 0 ? new RebarSectionCircular("", rebarDiameterBottom, rebar) : null;
            var rc = CompositeSectionFactory.Create(s.SlabWidth, s.SlabHeight, concrete, top, s.SlabWidth / numberRebars, s.TopCover, bottom, s.SlabWidth / numberRebars, sectionH, steel, s.BottomCover, s.VerticalOffset, s.Name);
            return (rc, new[] { s.NLongTerm, s.NShortTerm, s.NShrinkage });
        }
        {
            var s = start; var e = end;
            double width = L(s.SlabWidth, e.SlabWidth), slabHeight = L(s.SlabHeight, e.SlabHeight), cover = L(s.TopCover, e.TopCover), offset = L(s.VerticalOffset, e.VerticalOffset);
            double nInfinite = L(s.NLongTerm, e.NLongTerm), nInstant = L(s.NShortTerm, e.NShortTerm);
            var sectionH = new SectionH(L1(s.Height, e.Height), L1(s.Web, e.Web), L1(s.TopWidth, e.TopWidth), L1(s.TopThickness, e.TopThickness), L1(s.BottomWidth, e.BottomWidth), L1(s.BottomThickness, e.BottomThickness), beamId);
            var concrete = new ConcreteMaterialEN1992(s.Concrete, L1(s.Fck, e.Fck), ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
            var steel = new SteelMaterialEN1992(s.Steel, L1(s.SteelE, e.SteelE), L1(s.SteelFy, e.SteelFy), L1(s.SteelFu, e.SteelFu), 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
            var rebar = new SteelMaterialEN1992(s.Rebar, L1(s.RebarE, e.RebarE), L1(s.RebarFy, e.RebarFy), L1(s.RebarFu, e.RebarFu), 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
            double rebarsArea = L1(s.TopArea, e.TopArea); int numberRebars = 3; double rebarDiameter = Math.Sqrt(rebarsArea / numberRebars * 4 / Math.PI);
            var top = rebarDiameter != 0 ? new RebarSectionCircular("", rebarDiameter, rebar) : null;
            double bottomArea = L(s.BottomArea, e.BottomArea), bottomCover = L(s.BottomCover, e.BottomCover);
            double rebarDiameterBottom = Math.Sqrt(bottomArea / numberRebars * 4 / Math.PI);
            var bottom = rebarDiameterBottom != 0 ? new RebarSectionCircular("", rebarDiameterBottom, rebar) : null;
            var rc = CompositeSectionFactory.Create(width, slabHeight, concrete, top, width / numberRebars, cover, bottom, width / numberRebars, sectionH, steel, bottomCover, offset, s.Name);
            return (rc, new[] { nInfinite, nInstant, L(s.NShrinkage, e.NShrinkage) });
        }
    }

    /// <summary>Every public value reachable from <paramref name="value"/> (doubles by their bits; depth-limited, cycles cut).</summary>
    internal static string Dump(object value)
    {
        var text = new StringBuilder(); var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
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

    /// <summary>The numbers of a composite property of the checks (the inputs of Composite Beam Property): steel I, slab with two rows of
    /// <paramref name="rows"/> mm² at 50 mm covers, S355 (E 210000, fy 355, fu 510; no Poisson's ratio, density or thermal expansion given),
    /// C32/40 (fck 32), B450C (E 200000, fy 450, fu 540), both components considered.</summary>
    private static Numbers Property(string name, double d, double b1, double t1, double b2, double t2, double tw, double slabWidth, double slabHeight, double rows, double nInf, double nInst, double nSh) => new Numbers
    {
        Name = name, Height = d, BottomWidth = b1, BottomThickness = t1, TopWidth = b2, TopThickness = t2, Web = tw,
        SlabWidth = slabWidth, SlabHeight = slabHeight, TopArea = rows, TopCover = 50, BottomArea = rows, BottomCover = 50,
        Steel = "S355", SteelE = 210000, SteelFy = 355, SteelFu = 510, SteelPoisson = 0,
        Concrete = "C32/40", ConcreteE = 33345.7645, Fck = 32, Rebar = "B450C", RebarE = 200000, RebarFy = 450, RebarFu = 540,
        NLongTerm = nInf, NShortTerm = nInst, NShrinkage = nSh, ConsiderConcrete = true, ConsiderRebar = true,
    };

    /// <summary>A slab of the numbers (the arguments in the order of the former slab type: height, width, top area, top cover, bottom area,
    /// bottom cover).</summary>
    private static Numbers WithSlab(Numbers x, double height, double width, double topArea, double topCover, double bottomArea, double bottomCover) =>
        x with { SlabHeight = height, SlabWidth = width, TopArea = topArea, TopCover = topCover, BottomArea = bottomArea, BottomCover = bottomCover };

    private static readonly BridgeAnalysisOptions[] Options =
    {
        new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare, WebBuckling = true, TopFlangeBuckling = true, BottomFlangeBuckling = true },
        new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare, WebBuckling = false, TopFlangeBuckling = false, BottomFlangeBuckling = false },
        new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare, WebBuckling = true, TopFlangeBuckling = false, BottomFlangeBuckling = true },
    };

    /// <summary>The composite properties of the regression: 50 G1-C1, the section of 99 and the end of a variable 99.</summary>
    private static (Numbers G1C1, Numbers P99, Numbers P99End) Sections() => (
        Property("G1-C1", 2500, 600, 30, 600, 30, 16, 3000, 300, 3500, 18.2, 6.3, 16.1),
        Property("Sezione 99", 1800, 800, 40, 600, 30, 15, 2500, 300, 1600 * Math.PI, 17.5, 6.1, 15.8),
        Property("Sezione 99 fine", 2200, 900, 45, 650, 35, 17, 2700, 320, 2000 * Math.PI, 18.5, 6.4, 16.6));

    /// <summary>The variants of the analysis properties: the rows, the choices and the rebar material (B450C of BridGHe: ultimate strain
    /// 0.075, not in the values).</summary>
    private static (string Label, Numbers Numbers)[] AnalysisVariants(Numbers g1c1, Numbers p99, Numbers p99End)
    {
        var b450c = new SteelMaterialEN1992("B450C", 200000, 450, 540, .075, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
        return new[]
        {
            ("50 G1-C1", g1c1), ("99", p99), ("99 end", p99End),
            ("top row only", WithSlab(g1c1, 300, 3000, 3500, 45, 0, 50)),
            ("bottom row only", WithSlab(p99, 250, 2500, 0, 50, 2711.3, 62.5)),
            ("different covers", WithSlab(p99End, 320, 2700, 4021.24, 47.5, 1884.96, 70)),
            ("no bars", WithSlab(g1c1, 300, 3000, 0, 50, 0, 50)),
            ("rebars not considered", g1c1 with { ConsiderRebar = false }),
            ("concrete not considered", p99 with { ConsiderConcrete = false }),
            ("B450C of BridGHe", g1c1 with { Rebar = b450c.Name, RebarE = b450c.ElasticModulusTension, RebarFy = 450, RebarFu = 540, RebarUltimateStrain = b450c.StrainUTension }),
        };
    }

    /// <summary>The cases of the definitions of composite beams: start and end, the former rule (on the FEM properties it compared the material
    /// instances: <c>FemConstant</c> is that rule's answer, checked on the FEM properties in the parity) and the rule by value.</summary>
    private static (string Label, Numbers Start, Numbers End, bool FemConstant, bool GpcConstant)[] BeamCases(Numbers g1c1, Numbers p99, Numbers p99End) => new[]
    {
        ("same property", p99, p99, true, true),
        ("copy sharing the materials", p99, p99 with { }, true, true),
        ("clone with other material instances", p99, p99 with { }, false, true), // listed difference: one definition instead of one per station, the same sections
        ("other steel name", p99, p99 with { Steel = "S355 bis" }, false, false),
        ("variable 99", p99, p99End, false, false),
        ("variable reversed", p99End, p99, false, false),
        ("top row only to both rows", WithSlab(g1c1, 300, 3000, 3500, 45, 0, 50), g1c1, false, false),
        ("different covers", g1c1, WithSlab(g1c1, 300, 3000, 3500, 40, 3500, 60), false, false),
        ("other ratios", g1c1, g1c1 with { NLongTerm = 17.9, NShrinkage = 15.2 }, false, false),
    };

    /// <summary>The comparisons with the FEM properties of the same numbers while they exist (<c>GpcCompositeSectionChecks.Fem.cs</c>); the
    /// call disappears with that file.</summary>
    static partial void FemParity(ref int checks, ref string summary);

    /// <summary>Regression (not native) <c>gpc-composite-sections</c>: the sections of examples 50 (G1-C1) and 99, constant and variable (four
    /// stations), with and without bars, three sets of options: the definition's input = the former construction's adapter input, the
    /// modular ratios equal; the analysis values of the definitions against the former ones of the composite property
    /// (<see cref="CompositeAnalysisPropertyChecks"/>); the definitions of composite beams; every definition of the R6.0 capture.</summary>
    internal static void Regression()
    {
        var (g1c1, p99, p99End) = Sections();
        int compared = 0;
        void Compare(string label, Numbers start, Numbers end, double station)
        {
            var definition = end == null ? GpcCompositeDefinitions.Create(start, "B7") : GpcCompositeDefinitions.Interpolated(start, end, station, "B7");
            var (former, ratios) = Former(start, end, station, "B7");
            compared += CompareInput(label, definition, former, ratios);
        }
        Compare("50 G1-C1", g1c1, null, 0);
        Compare("99", p99, null, 0);
        foreach (double s in new[] { 0, 0.25, 0.5, 1 }) Compare("99 variable " + s.ToString(CultureInfo.InvariantCulture), p99, p99End, s);
        if (compared != 36) throw new Exception($"GPC COMPOSITE SECTION FAIL: {compared} inputs compared instead of 36");
        // Chain E step 1: the analysis properties (one bar per row) from the definition = the former ones of the composite property.
        var analysed = AnalysisVariants(g1c1, p99, p99End);
        int analysis = 0;
        foreach (var (label, p) in analysed) analysis += CompositeAnalysisPropertyChecks.Compare(p, GpcCompositeDefinitions.Create(p, p.Name), label);
        int beamChecks = BeamDefinitions(g1c1, p99, p99End, Options);
        var (captured, capturedInputs, capturedAnalysis) = CapturedDefinitions();
        int parity = 0; string paritySummary = "no FEM parity (the FeMM models are removed)";
        FemParity(ref parity, ref paritySummary);
        Console.WriteLine($"GPC COMPOSITE SECTIONS PASS: {compared} section inputs of the definitions equal bit for bit to the former construction and its adapter (constant and variable sections, with and without bars, 3 option sets); " +
            $"{analysis} analysis values or variants of {analysed.Length} definitions equal bit for bit to the former ones of their composite properties (self tension: {CompositeAnalysisPropertyChecks.EchoedRebarDifferences} results with the echoed rebar material of ultimate strain 0.1 instead of the property one, stresses equal); " +
            $"chain E (c): {beamChecks} checks of the definitions of the composite beams (constant rule by value, interpolation from the definitions, steel H name, Linear Tension) equal to the former path; " +
            $"R6.0 capture: {captured} captured definitions rebuilt from their numbers bit for bit, {capturedInputs} inputs of the H bridge engine equal to the former construction, {capturedAnalysis} analysis values equal to the former ones; {paritySummary}.");
    }

    /// <summary>The input of the H bridge engine of <paramref name="definition"/> against that of the adapter on the former section, for every
    /// option set with and without bars, its section and its ratios: the number of inputs compared.</summary>
    private static int CompareInput(string label, BridgeCompositeSectionDefinition definition, ReinforcedConcreteSection former, double[] ratios)
    {
        if (!new[] { definition.NLongTerm, definition.NShortTerm, definition.NShrinkage }.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(ratios.Select(BitConverter.DoubleToInt64Bits)))
            throw new Exception($"GPC COMPOSITE SECTION FAIL ({label}): modular ratios differ");
        string formerSection = Dump(former), section = Dump(definition.Section);
        if (formerSection != section) throw new Exception($"GPC COMPOSITE SECTION FAIL ({label}): section differs");
        int compared = 0;
        foreach (var o in Options)
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
        return compared;
    }

    /// <summary>The folder of the R6.0 capture: the folder of <c>BRIDGHE_CAPTURE=compare:&lt;folder&gt;</c>, else the frozen capture of P2N.</summary>
    internal static string CaptureFolder()
    {
        string setting = Environment.GetEnvironmentVariable(GpcDraftCapture.Variable);
        return setting != null && setting.StartsWith("compare:", StringComparison.Ordinal) ? setting.Substring("compare:".Length)
            : @"C:\Users\g.pacini\Documents\Repository\gpc-f6-claude\P2B\results\p2n-capture";
    }

    /// <summary>The captured entries of one kind of a source of the R6.0 capture (<c>&lt;source&gt;.bgcap</c>, the format of
    /// <see cref="GpcDraftCapture"/>): key and the data chunk of the final writer.</summary>
    internal static IEnumerable<(string Key, byte[] Data)> Captured(string source, string kind)
    {
        string path = Path.Combine(CaptureFolder(), source + ".bgcap");
        if (!File.Exists(path)) throw new FileNotFoundException("R6.0 capture not found (BRIDGHE_CAPTURE=compare:<folder> or the frozen capture of P2N): " + path);
        using var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        if (!r.ReadBytes(6).SequenceEqual(Encoding.ASCII.GetBytes("BGCAP1"))) throw new InvalidDataException("Not a capture: " + path);
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            string key = r.ReadString(), entryKind = r.ReadString(); r.ReadString();
            int n = r.ReadInt32(); byte[] data = null;
            if (n >= 0) { if (entryKind == kind) data = r.ReadBytes(n); else r.BaseStream.Seek(n, SeekOrigin.Current); }
            n = r.ReadInt32(); if (n >= 0) r.BaseStream.Seek(n, SeekOrigin.Current);
            n = r.ReadInt32(); r.BaseStream.Seek(n, SeekOrigin.Current);
            if (entryKind == kind) yield return (key, data);
        }
    }

    /// <summary>
    /// R6.0 capture: every captured composite definition (the payloads of the example definitions read by value, <c>payloads</c>, and the
    /// definitions of the Composite Beam Property outputs and inputs of every Build Model of the scope of L12, <c>scan</c>: made by the
    /// production construction) rebuilt from its numbers with <see cref="GpcCompositeDefinitions"/> (fingerprint and every value, bit for
    /// bit); the former construction of Super Element Check on its numbers gives its section, its ratios and, through the adapter, its input
    /// of the H bridge engine for every option set with and without bars; and its analysis values equal the former ones of a composite
    /// property of its numbers.
    /// </summary>
    private static (int Definitions, int Inputs, int Analysis) CapturedDefinitions()
    {
        int definitions = 0, inputs = 0, analysis = 0;
        foreach (string source in new[] { "payloads", "scan" })
            foreach (var (key, data) in Captured(source, "composite"))
            {
                string label = source + " " + (key.Length > 90 ? "…" + key.Substring(key.Length - 90) : key);
                var captured = GpcDraftArchive.ReadComposite(data);
                var (numbers, steelName) = GpcCompositeDefinitions.Read(captured);
                GpcCompositeDefinitions.RequireEqual(GpcCompositeDefinitions.Create(numbers, steelName, captured.AnalysisSteel != null), captured, label, "the captured definition");
                var (former, ratios) = Former(numbers, null, 0, steelName);
                inputs += CompareInput(label, captured, former, ratios);
                analysis += CompositeAnalysisPropertyChecks.Compare(numbers, captured, label);
                definitions++;
            }
        if (definitions != 44) throw new Exception($"GPC COMPOSITE SECTION FAIL: {definitions} captured definitions instead of 44 (10 payloads, 34 scan)");
        return (definitions, inputs, analysis);
    }

    /// <summary>The definitions of one composite beam with the former rules of Super Element Check from the numbers of its composite properties:
    /// a constant girder (<paramref name="constant"/>: the former rule) the definition of the start at every station, the steel H named by the
    /// beam; a variable one the interpolation of the two properties at each normalised station.</summary>
    internal static BridgeCompositeSectionDefinition[] FormerDefinitions(Numbers start, Numbers end, double[] stations, string beamId, bool constant)
    {
        var definition = constant ? GpcCompositeDefinitions.Create(start, beamId) : null;
        return stations.Select(s => definition ?? GpcCompositeDefinitions.Interpolated(start, end, s, beamId)).ToArray();
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

    /// <summary>Compares the definitions of one composite beam per station (former rules / definitions), bit for bit: modular ratios, choices,
    /// rows, the section (but the name of the steel H) and the input of the H bridge engine for every option set, with and without bars. The
    /// number of comparisons.</summary>
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

    /// <summary>Chain E (c), not native: the definitions of composite beams (Composite Beam: the definitions of its two composite properties)
    /// against the former path of Super Element Check and Composite Beam Linear Tension Analysis.</summary>
    private static int BeamDefinitions(Numbers g1c1, Numbers p99, Numbers p99End, BridgeAnalysisOptions[] options)
    {
        int checks = 0;
        BridgeCompositeSectionDefinition D(Numbers p) => GpcCompositeDefinitions.Create(p, p.Name);
        var stations = new[] { 0, 0.125, 0.25, 0.5, 0.75, 1 };
        // Constant-section rule: by value on the definitions (the former rule on the material instances: in the FEM parity).
        foreach (var c in BeamCases(g1c1, p99, p99End))
        {
            var (ds, de) = (D(c.Start), D(c.End));
            bool gpc = GpcCompositeSections.IsConstant(ds, de);
            if (gpc != c.GpcConstant) throw new Exception($"GPC COMPOSITE BEAM FAIL ({c.Label}): constant on the definitions {gpc}, expected {c.GpcConstant}");
            var former = FormerDefinitions(c.Start, c.End, stations, "B7", c.FemConstant);
            var now = BeamDefinitions(ds, de, stations);
            checks += CompareBeam(c.Label, former, now, "B7", options) + 1;
            // The interpolation from the definitions with the name of the former checks: the same section, dump for dump.
            if (!gpc)
                foreach (double s in stations)
                {
                    if (Dump(GpcCompositeSections.Definition(ds, de, s, "B7").Section) != Dump(GpcCompositeDefinitions.Interpolated(c.Start, c.End, s, "B7").Section))
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
        return checks;
    }
}
#endif
