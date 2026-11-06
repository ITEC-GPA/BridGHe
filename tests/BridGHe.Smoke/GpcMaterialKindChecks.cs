#if GPC_PILOT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using GPC.Model.Materials;
using Grasshopper.Kernel.Types;

/// <summary>
/// Regression <c>gpc-material-kinds</c> (M1, coordinator 6/11; §1bis, row <c>GH_Material</c>): the GPC material by kind of every former
/// material (<see cref="GpcMaterials.FromFemm"/>, the datum <c>GH_Material.Gpc</c>) against today's builders, bit for bit:
/// <list type="bullet">
/// <item>the type by kind (steel and rebar <see cref="SteelMaterialEN1992"/> structural or rebar, concrete <see cref="ConcreteMaterialEN1992"/>,
/// aluminium <see cref="AluminiumMaterialEN1999"/>, undefined the generic material), with the fallbacks counted;</item>
/// <item>the analysis material (<see cref="GpcMaterials.Analysis"/>) equal to the material of the beam properties of the reader
/// (<see cref="LegacyModelImport.ElasticMaterial"/>, <see cref="GpcModels.Material"/>);</item>
/// <item>the check materials (<see cref="GpcMaterials.StructuralSteel"/>, <see cref="GpcMaterials.Reinforcement"/>,
/// <see cref="GpcMaterials.Concrete"/>) equal to the ones the builders make from the FEM value (<c>BridgeGpc.Materials</c>,
/// <c>CompositeBeamPropertyModel.GetReinforcedConcreteSection</c>), the scalars of <c>GpcCompositeSections</c> equal to the FEM fields, and
/// the steel of <see cref="ReinforcementMaterials.B450C"/> equal to the typed rebar but for the declarations;</item>
/// <item>the values at the ports carried (declared strengths, alloy and form of aluminium, reinforcement ultimate strain).</item>
/// </list>
/// Corpus: the six material components (every grade, type, kind and unit system of the inputs) and every material of the
/// <c>--import-dump</c> corpus (77 captured inputs, the captured examples, the examples 01–10 in memory, node, beam and plate data in every
/// unit system), found by walking the FEM values (beam, plate and composite properties).
/// </summary>
internal static class GpcMaterialKindChecks
{
    private const string Results = @"C:\Users\g.pacini\Documents\Repository\gpc-f6-claude\P2B\results";

    private static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");
    private static string B(double? v) => v.HasValue ? B(v.Value) : "null";

    /// <summary>Every value of a GPC material, doubles as bits (the identity GUID aside: a new one per instance).</summary>
    internal static string Dump(Material m)
    {
        if (m == null) return "null";
        var s = new System.Text.StringBuilder();
        s.Append(m.GetType().Name).Append('|').Append(m.Name ?? "<null>");
        foreach (var v in new[] { m.ElasticModulusCompression, m.ElasticModulusTension, m.StrainYCompression, m.StrainUCompression, m.StrainYTension, m.StrainUTension,
            m.StressYCompression, m.StressUCompression, m.StressYTension, m.StressUTension, m.Ni, m.AlfaThermalExpansion, m.Density }) s.Append('|').Append(B(v));
        s.Append("|ro=").Append(m.IsReadOnly).Append("|std=").Append(m.AccordingToStandard);
        s.Append("|tc=").Append(Table(m.StressStrainTableCompression)).Append("|tt=").Append(Table(m.StressStrainTableTension));
        if (m is SteelMaterial st) s.Append($"|steel {st.SteelType} {st.StressStrainCurve} {B(st.Fyk)} {B(st.Fu)}");
        if (m is ConcreteMaterialEuropeanCommon c) s.Append($"|concrete {B(c.Fck)} {c.CementType} {c.ParabolaRectangleMethod}");
        if (m is AluminiumMaterial a) s.Append($"|aluminium {B(a.Fo)} {B(a.Fu)} {a.AluminiumType} {B(a.ThicknessMax)}");
        var d = m.AnalysisData;
        s.Append("|analysis=").Append(d == null ? "null" : $"{B(d.BeamShearModulus)} {B(d.ViscousDampingDensity)} {B(d.ModalDampingRatio)} {B(d.ThermalConductivity)} {B(d.SpecificHeat)} [{(d.StressStrainPairs == null ? "null" : string.Join(",", d.StressStrainPairs.Select(B)))}] {d.NonlinearResponse} {d.Hardening}");
        var x = m.Declarations;
        s.Append("|declared=").Append(x == null ? "null" : $"{B(x.ExpectedYieldStress)} {B(x.ExpectedTensileStrength)} {B(x.ExpectedCompressiveStrength)} {B(x.CompressiveYieldStrength)} {B(x.TensileYieldStrength)} {B(x.TensileUltimateStrength)} {B(x.ShearUltimateStrength)} {x.Alloy ?? "null"} {x.AluminiumForm}");
        s.Append("|ortho=").Append(m.Orthotropy == null ? "null" : "set");
        return s.ToString();
    }

    private static string Table(StressStrainTable t)
    {
        if (t == null) return "null";
        try { return string.Join(",", t.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0)
            .Select(p => p.Name + "=" + Text(p.GetValue(t)))); }
        catch (TargetInvocationException) { return "unreadable"; }
        static string Text(object v) => v switch { null => "null", double d => B(d), IEnumerable e when !(e is string) => "[" + string.Join(",", e.Cast<object>().Select(Text)) + "]", _ => v.ToString() };
    }

    /// <summary>R6.2 part C, the declared difference of the Aluminium component (user decision of 6/11): the GPC material of the
    /// component has fu = 262 MPa where the conversion of its FEM value (0.3.0) has 165.5, every other value equal, the declared shear
    /// ultimate strength 165.5 on both. True when the two materials differ by that only (every value of <see cref="Dump"/>).</summary>
    internal static bool DeclaredAluminium(Material gpc, Material fem)
    {
        if (!(gpc is AluminiumMaterial) || !(fem is AluminiumMaterial)) return false;
        string old = B(165.5), fixedFu = B(262), oldC = B(-165.5), fixedC = B(-262);
        string a = Dump(gpc), f = Dump(fem);
        if (!a.Contains("|aluminium ") || !f.Contains(old)) return false;
        // The declared shear ultimate strength (7th declared value) is 165.5 on both: masked before 165.5 becomes 262 in the conversion.
        static string MaskShear(string d)
        {
            int i = d.IndexOf("|declared=", StringComparison.Ordinal); if (i < 0) return d;
            int end = d.IndexOf('|', i + 1); if (end < 0) end = d.Length;
            var parts = d.Substring(i + 10, end - i - 10).Split(' ');
            if (parts.Length > 6) parts[6] = "SHEAR"; else return d;
            return d.Substring(0, i + 10) + string.Join(" ", parts) + d.Substring(end);
        }
        // fu and the compressive ultimate stress (−fu) of the GPC aluminium, in the values and in the tables.
        return MaskShear(a) == MaskShear(f).Replace(old, fixedFu).Replace(oldC, fixedC);
    }

    private static string Without(string dump, string part) => string.Join("|", dump.Split('|').Where(p => !p.StartsWith(part + "=", StringComparison.Ordinal)));

    /// <summary>The checks of one material and their counts (shared by the regression and by <c>--material-scan</c>).</summary>
    private sealed class Tally
    {
        internal readonly List<string> failures = new List<string>();
        internal readonly Dictionary<string, int> types = new Dictionary<string, int>(); internal int fallbacks, samples, analysis, steelViews, rebarViews, concreteViews, compositeRoles, crossKind, refusedSteel, refusedConcrete;
        internal void Require(bool condition, string what) { if (!condition && failures.Count < 40) failures.Add(what); }

        internal void Check(string label, MaterialModel m, string role)
        {
            samples++;
            var typed = GpcMaterials.FromFemm(m);
            string kind = m.Kind.ToString();
            types.TryGetValue(kind + " -> " + typed.GetType().Name, out int n); types[kind + " -> " + typed.GetType().Name] = n + 1;
            bool expected = m.Kind switch
            {
                MaterialModel.MaterialKind.Steel => typed is SteelMaterialEN1992 s && s.SteelType == SteelMaterial.SteelTypes.Structural,
                MaterialModel.MaterialKind.Rebar => typed is SteelMaterialEN1992 r && r.SteelType == SteelMaterial.SteelTypes.Rebar,
                MaterialModel.MaterialKind.Concrete => typed is ConcreteMaterialEN1992,
                MaterialModel.MaterialKind.Aluminium => typed is AluminiumMaterialEN1999,
                _ => typed.GetType() == typeof(Material),
            };
            if (!expected) { fallbacks++; Require(typed.GetType() == typeof(Material), $"{label}: kind {kind} gave {typed.GetType().Name}"); }

            // The reader of the BGHA1 payloads gives the same material (the archive of the value, read as data).
            Require(Dump(LegacyModelImport.Material(LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(m)))) == Dump(typed), $"{label}: reader by value");

            // Analysis: the material of the beam properties of the reader.
            string a = Dump(GpcMaterials.Analysis(typed)), e = Dump(GpcModels.Material(m));
            Require(a == e, $"{label}: analysis material\n  GPC  {a}\n  FEM  {e}"); analysis++;
            Require(GPC.Model.Core.ModelValues.Fingerprint(new object[] { GpcMaterials.Analysis(typed) }) == GPC.Model.Core.ModelValues.Fingerprint(new object[] { GpcModels.Material(m) }),
                $"{label}: analysis fingerprint");

            // The analysis values of the FEM value are on the typed material too.
            Require(B(typed.Ni) == B(m.PoissonRatio) && B(typed.Density) == B(m.Density) && B(typed.AlfaThermalExpansion) == B(m.ThermalExpansion)
                && B(typed.ElasticModulusCompression) == B(m.Modulus), $"{label}: analysis values on the typed material");

            // The check materials: the views on the GPC material against the expressions of the former builders on the FEM value (an error on
            // both sides counts as equal when it is the same error). A steel view applies to steels and to the generic material (a former
            // material without a kind: the example generators internalise steels and concretes so), a concrete view to concretes and to the
            // generic material; another kind in a check role (the former builders read zeros) is counted, not compared.
            static string Try(Func<Material> f) { try { return Dump(f()); } catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { return "ERROR " + x.GetType().Name; } }
            bool generic = typed.GetType() == typeof(Material);
            bool steelData = typed is SteelMaterial || generic && (typed.StressYTension != 0 || typed.StressUTension != 0), concreteData = typed is ConcreteMaterialEuropeanCommon || generic && typed.StressYCompression != 0;
            // A generic material without the strengths of a role (an analysis material) is refused by the view of that role (the former builders gave zeros).
            if (generic && !steelData) { refusedSteel++; Require(Try(() => GpcMaterials.StructuralSteel(typed)).StartsWith("ERROR") && Try(() => GpcMaterials.Reinforcement(typed)).StartsWith("ERROR"), $"{label}: steel views refuse a material without steel strengths"); }
            if (generic && !concreteData) { refusedConcrete++; Require(Try(() => GpcMaterials.Concrete(typed)).StartsWith("ERROR"), $"{label}: concrete view refuses a material without fck"); }
            if (steelData)
            {
                // Structural steel of the checks (BridgeGpc.Materials and CompositeBeamPropertyModel, the same expression).
                Require(Try(() => GpcMaterials.StructuralSteel(typed)) == Try(() => new SteelMaterialEN1992(m.Name, m.Modulus, m.MinimumYieldStress, m.MinimumTensileStress, .1,
                    SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural)), $"{label}: structural steel of the checks"); steelViews++;
                // Bars of the checks (BridgeGpc.Materials with a rebar, CompositeBeamPropertyModel).
                Require(Try(() => GpcMaterials.Reinforcement(typed)) == Try(() => new SteelMaterialEN1992(m.Name, m.Modulus, m.MinimumYieldStress, m.MinimumTensileStress,
                    m.ReinforcementUltimateStrain ?? .1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar)), $"{label}: bars of the checks"); rebarViews++;
                // Scalars of GpcCompositeSections (steel and bars).
                var v = GpcMaterials.SteelValues(typed);
                Require(v.Name == m.Name && B(v.E) == B(m.Modulus) && B(v.Fy) == B(m.MinimumYieldStress) && B(v.Fu) == B(m.MinimumTensileStress) && B(v.StrainU) == B(m.ReinforcementUltimateStrain ?? .1),
                    $"{label}: steel values of the composite sections: {v} / {m.Name} {m.Modulus:R} {m.MinimumYieldStress:R} {m.MinimumTensileStress:R} {m.ReinforcementUltimateStrain:R}");
            }
            else if (role == "steel" || role == "rebar") crossKind++;
            if (concreteData)
            {
                Require(Try(() => GpcMaterials.Concrete(typed)) == Try(() => new ConcreteMaterialEN1992(m.Name, m.SpecificCompressiveStrength, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle)),
                    $"{label}: concrete of the checks"); concreteViews++;
                var v = GpcMaterials.ConcreteValues(typed);
                Require(v.Name == m.Name && B(v.Fck) == B(m.SpecificCompressiveStrength), $"{label}: concrete values of the composite sections: {v} / {m.Name} {m.SpecificCompressiveStrength:R}");
            }
            else if (role == "concrete") crossKind++;
            // The declared values (every kind).
            var x = typed.Declarations;
            Require(B(x?.ExpectedYieldStress ?? 0) == B(m.ExpectedYieldStress) && B(x?.ExpectedTensileStrength ?? 0) == B(m.ExpectedTensileStress) && B(x?.ExpectedCompressiveStrength ?? 0) == B(m.ExpectedCompressiveStrength)
                && B(x?.CompressiveYieldStrength ?? 0) == B(m.CompressiveYieldStrength) && B(x?.TensileYieldStrength ?? 0) == B(m.TensileYieldStrength)
                && B(x?.TensileUltimateStrength ?? 0) == B(m.TensileUltimateStrength) && B(x?.ShearUltimateStrength ?? 0) == B(m.ShearUltimateStrength) && x?.Alloy == m.Alloy
                && (m.Kind != MaterialModel.MaterialKind.Aluminium || x != null && (int)x.AluminiumForm == (int)m.AlumiumType + 1), $"{label}: declared values carried");
            if (typed is AluminiumMaterial alu) Require(B(alu.Fo) == B(m.TensileYieldStrength) && B(alu.Fu) == B(m.TensileUltimateStrength), $"{label}: aluminium strengths");
            if (role != null) compositeRoles++;
        }

        internal string Kinds => string.Join(", ", types.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => t.Key + " " + t.Value));

        internal string Summary => $"type by kind ({fallbacks} generic fallbacks), analysis material = the reader's {analysis}, structural steel {steelViews}, " +
            $"bars {rebarViews}, concrete {concreteViews} of the checks = the former builders bit for bit (generic materials without the strengths of a role refused: steel {refusedSteel}, concrete {refusedConcrete}); {compositeRoles} composite-property roles, {crossKind} of another kind";
    }

    internal static void Regression()
    {
        var tally = new Tally(); var failures = tally.failures;
        void Require(bool condition, string what) => tally.Require(condition, what);
        void Check(string label, MaterialModel m, string role) => tally.Check(label, m, role);

        // The six material components (headless, every grade and input).
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        using var document = new Grasshopper.Kernel.GH_Document { Enabled = true };
        Grasshopper.Kernel.GH_Document.EnableSolutions = true;
        int componentSamples = 0, attached = 0, aluminiumDeclared = 0;
        void Component(string type, params (int input, Func<IGH_Goo> value)[][] runs)
        {
            var component = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(assembly.GetTypes().Single(t => t.Name == type));
            component.CreateAttributes(); document.AddObject(component, false);
            foreach (var run in runs)
            {
                foreach (var (input, value) in run)
                {
                    var p = (Grasshopper.Kernel.IGH_Param)component.Params.Input[input];
                    var data = p.GetType().GetProperty("PersistentData").GetValue(p);
                    data.GetType().GetMethod("Clear").Invoke(data, null);
                    var v = value();
                    if (v != null) data.GetType().GetMethods().First(mm => mm.Name == "Append" && mm.GetParameters().Length == 1).Invoke(data, new object[] { v });
                    p.ExpireSolution(false);
                }
                document.NewSolution(false, Grasshopper.Kernel.GH_SolutionMode.Silent);
                var goos = component.Params.Output[0].VolatileData.AllData(true).ToList();
                string label = type + "[" + string.Join(",", run.Select(r => r.value()?.ToString() ?? "-")) + "]";
                if (goos.Count != 1) { Require(false, label + ": no output (" + string.Join("; ", component.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)) + ")"); continue; }
                var goo = goos[0];
                var m = (MaterialModel)goo.GetType().GetProperty("Value").GetValue(goo);
                var gpc = (Material)goo.GetType().GetProperty("Gpc").GetValue(goo);
                if ((bool)goo.GetType().GetProperty("HasAttachedGpc").GetValue(goo)) attached++;
                // R6.2 part C: the Aluminium component gives fu = 262 on GPC (user decision of 6/11): the declared difference, by name.
                if (type == "MaterialAluminiumComponent") { Require(DeclaredAluminium(gpc, GpcMaterials.FromFemm(m)), label + ": GH_Material.Gpc = FromFemm of its value but fu 165.5 -> 262 (declared)"); aluminiumDeclared++; }
                else Require(Dump(gpc) == Dump(GpcMaterials.FromFemm(m)), label + ": GH_Material.Gpc = FromFemm of its value");
                var copy = (IGH_Goo)goo.Duplicate();
                Require(ReferenceEquals(copy.GetType().GetProperty("Gpc").GetValue(copy), gpc), label + ": a copy shares the GPC material");
                Check(label, m, null); componentSamples++;
            }
        }
        (int, Func<IGH_Goo>) S(int i, string s) => (i, () => new GH_String(s));
        (int, Func<IGH_Goo>) I(int i, int v) => (i, () => new GH_Integer(v));
        (int, Func<IGH_Goo>) D(int i, double v) => (i, () => new GH_Number(v));
        (int, Func<IGH_Goo>) T(int i, bool v) => (i, () => new GH_Boolean(v));
        Component("MaterialSteelComponent", Enumerable.Range(0, 4).Select(g => new[] { S(0, "S"), I(1, g) }).ToArray());
        Component("MaterialConcreteComponent", Enumerable.Range(0, 16).Select(g => new[] { S(0, "C"), I(1, g) }).ToArray());
        Component("MaterialAluminiumComponent", Enumerable.Range(0, 3).Select(t => new[] { S(0, "Al"), I(1, t), S(2, "6061-T6") }).ToArray());
        Component("MaterialRebarComponent", new[] { S(0, "B450C") }, new[] { S(0, " Barre ") });
        var custom = new List<(int, Func<IGH_Goo>)[]>();
        foreach (int kind in new[] { -1, 0, 1, 2 })
            foreach (int units in Enum.GetValues(typeof(ModelModel.ModelUnits)).Cast<int>())
                foreach (bool usePoisson in new[] { true, false })
                    custom.Add(new[] { S(0, "Custom"), I(1, kind), I(2, units), D(3, 205000), D(4, .29), D(5, 1.15e-5), D(6, 79000), T(7, usePoisson), D(8, 7.8e-9), D(9, 355), D(10, 510) });
        Component("MaterialCustomComponent", custom.ToArray());
        Component("MaterialTableComponent",
            // The component reads the stresses only without thermal expansion and density (existing behaviour: nested reads).
            new[] { S(0, "Table"), T(1, true), D(2, .3), D(3, 80000), (4, () => null), (5, () => null), (6, () => new GH_Number(100)), (7, () => new GH_Number(.0005)) },
            new[] { S(0, "Table"), T(1, false), D(2, .25), D(3, 81000), (4, () => null), (5, () => null), (6, () => new GH_Number(200)), (7, () => new GH_Number(.001)) });

        // Every material of the --import-dump corpus.
        int corpusModels = 0, corpusSamples = 0;
        void Model(string id, ModelModel fem)
        {
            corpusModels++;
            foreach (var (m, role, path) in Materials(fem)) { Check(id + ":" + path, m, role); corpusSamples++; }
        }
        foreach (var file in Directory.GetFiles(Path.Combine(Results, "capture-1"), "*.bgmodel").OrderBy(f => f, StringComparer.Ordinal))
            Model("capture-" + Path.GetFileNameWithoutExtension(file), BridGHe.Common.Helpers.ModelArchive.Read<ModelModel>(File.ReadAllBytes(file)));
        foreach (var file in Directory.GetFiles(Path.Combine(Results, "examples-1"), "*.bgmodel", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            Model("examples-" + Path.GetFileName(Path.GetDirectoryName(file)) + "-" + Path.GetFileNameWithoutExtension(file), BridGHe.Common.Helpers.ModelArchive.Read<ModelModel>(File.ReadAllBytes(file)));
        foreach (var example in NativeExamples.Create()) Model("memory-" + example.Id, example.Model);
        foreach (ModelModel.ModelUnits units in Enum.GetValues(typeof(ModelModel.ModelUnits)))
        {
            Model("nodes-" + units, ImportDump.NodeData(units)); Model("beams-" + units, ImportDump.BeamData(units, null)); Model("plates-" + units, ImportDump.PlateData(units));
        }

        // The steel of ReinforcementMaterials.B450C (the GPC object it builds) is the typed rebar but for the declarations.
        foreach (var name in new[] { "B450C", " Barre " })
        {
            var b450c = ReinforcementMaterials.B450C(name);
            var steel = new SteelMaterialEN1992(name.Trim(), 200000, 450, 540, .075, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
            Require(Without(Dump(GpcMaterials.FromFemm(b450c)), "declared") == Without(Dump(steel), "declared"), "B450C " + name + ": the steel of ReinforcementMaterials");
        }

        Console.WriteLine("Material kinds: " + tally.Kinds);
        if (failures.Count > 0) throw new Exception("GPC MATERIAL KINDS FAIL:\n" + string.Join("\n", failures));
        if (componentSamples < 59 || corpusModels < 90) throw new Exception($"GPC MATERIAL KINDS FAIL: only {componentSamples} component outputs and {corpusModels} models");
        Console.WriteLine($"GPC MATERIAL KINDS PASS: {tally.samples} materials ({componentSamples} from the six components, {corpusSamples} from {corpusModels} models of the import-dump corpus; " +
            $"{attached} attached by a GPC builder; {aluminiumDeclared} Aluminium outputs with the declared difference fu 165.5 -> 262), {tally.Summary}.");
    }

    /// <summary>
    /// <c>--material-scan [files]</c> (M1): the checks of <see cref="Regression"/> on every former material of the definitions of the scope of
    /// L12 (<see cref="BuildModelScan.ScopeFiles"/>): each definition read (never saved), the components upstream of Build Model and of every
    /// component with a material or composite property input solved (the rest locked, no analysis run, nothing written), then every datum of
    /// every parameter, persistent (internalised) and volatile, walked for former materials (Material parameters, beam, plate and composite
    /// properties). Exit 1 on a difference.
    /// </summary>
    /// <summary>
    /// R6.2 part C (P2N, 6/11), with <c>BRIDGHE_FLOAT_TOKENS=1</c>: the census of the float tokens of the FEM values of the scan (the finding A
    /// of P2U on the values the components compute, beside its measurement on the payloads of the files): every distinct FEM value of the
    /// BridGHe types in the parameters, written as the GH types write it for the reader (BGHA1), its doubles that the binary writer stores as
    /// float tokens, and those whose text (the float's shortest string) reads to another double: the values <see cref="LegacyArchive"/>
    /// read differently before its fix. By element and text, with the files.
    /// </summary>
    internal sealed class FloatTokenCensus
    {
        private readonly HashSet<object> seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        private readonly SortedDictionary<string, (int data, SortedSet<string> files)> wrong = new SortedDictionary<string, (int, SortedSet<string>)>(StringComparer.Ordinal);
        private int data, withFloats, tokens, wrongTokens, dataWithWrong;
        private static readonly MethodInfo Write = typeof(BridGHe.Common.Helpers.ModelArchive).GetMethod("Write");
        internal static FloatTokenCensus Open() => Environment.GetEnvironmentVariable("BRIDGHE_FLOAT_TOKENS") == "1" ? new FloatTokenCensus() : null;

        internal void Add(string file, object value)
        {
            if (!seen.Add(value)) return;
            byte[] bytes;
            try { bytes = (byte[])Write.MakeGenericMethod(value.GetType()).Invoke(null, new[] { value }); }
            catch (TargetInvocationException) { return; }
            data++;
            var quotas = new System.Xml.XmlDictionaryReaderQuotas { MaxArrayLength = 100000000, MaxStringContentLength = 64000000, MaxDepth = 512, MaxBytesPerRead = 65536, MaxNameTableCharCount = 64000000 };
            using var r = System.Xml.XmlDictionaryReader.CreateBinaryReader(bytes, 5, bytes.Length - 5, quotas);
            string element = ""; int here = 0, hereWrong = 0; var keys = new HashSet<string>(StringComparer.Ordinal);
            while (r.Read())
            {
                if (r.NodeType == System.Xml.XmlNodeType.Element) element = r.LocalName;
                else if (r.NodeType == System.Xml.XmlNodeType.Text && r.ValueType == typeof(float))
                {
                    here++; string text = r.Value; double exact = System.Xml.XmlConvert.ToSingle(text);
                    if (!System.Xml.XmlConvert.ToDouble(text).Equals(exact)) { hereWrong++; keys.Add($"{value.GetType().Name} {element} {text} -> {exact.ToString("R", CultureInfo.InvariantCulture)}"); }
                }
            }
            tokens += here; wrongTokens += hereWrong; if (here > 0) withFloats++; if (hereWrong > 0) dataWithWrong++;
            foreach (var k in keys) { wrong.TryGetValue(k, out var w); w.files ??= new SortedSet<string>(StringComparer.Ordinal); w.files.Add(file); wrong[k] = (w.data + 1, w.files); }
        }

        internal void Print()
        {
            Console.WriteLine($"FLOAT TOKENS scan: {data} distinct FEM values written for the reader, {withFloats} with float tokens ({tokens} tokens), {dataWithWrong} with {wrongTokens} tokens whose text reads to another double before the fix of LegacyArchive:");
            foreach (var w in wrong) Console.WriteLine($"  {w.Key}: in {w.Value.data} values, files {string.Join(", ", w.Value.files)}");
        }
    }

    internal static int Scan(string[] args)
    {
        BridgeExampleMigration.Initialize();
        var census = FloatTokenCensus.Open();
        var files = args.SkipWhile(a => a != "--material-scan").Skip(1).Where(a => !a.StartsWith("--")).Select(a => Path.GetFullPath(Path.Combine(ExamplePaths.Root, a))).ToList();
        if (files.Count == 0) files = BuildModelScan.ScopeFiles();
        var tally = new Tally(); int failed = 0, internalised = 0, volatileData = 0;
        // R6.0: the capture of the scan (source "scan"): the drafts of the Build Model inputs by location (file, Build Model, input, path,
        // item), the Build Model outputs by Build Model, and the materials (GPC materials of the GH_Material data, check materials of the
        // properties) and the other data of the GH types by content within the file (a set: independent of how the values are shared).
        var capture = GpcDraftCapture.Open("scan");
        var drafts = new GpcDraftArchiveChecks.Tally { Capture = capture }; var materials = new HashSet<Material>(ReferenceEqualityComparer.Instance);
        foreach (var file in files)
        {
            string label = Path.GetRelativePath(ExamplePaths.Root, file);
            try
            {
                using var doc = GrasshopperBridgeExample.Read(file); doc.FilePath = file;
                static bool Consumer(Grasshopper.Kernel.IGH_Component c) => c.GetType().Name == "BuildModelComponent"
                    || c.Params.Input.Any(p => p.Type.Name == "GH_Material" || p.Type.Name == "GH_CompositeBeamProperty" || p.Type.Name == "GH_BeamProperty" || p.Type.Name == "GH_PlateProperty");
                var upstream = new HashSet<Grasshopper.Kernel.IGH_DocumentObject>();
                void Up(Grasshopper.Kernel.IGH_DocumentObject o)
                {
                    if (!upstream.Add(o)) return;
                    var parameters = o is Grasshopper.Kernel.IGH_Component c ? c.Params.Input : o is Grasshopper.Kernel.IGH_Param single ? new List<Grasshopper.Kernel.IGH_Param> { single } : new List<Grasshopper.Kernel.IGH_Param>();
                    foreach (var p in parameters) foreach (var s in p.Sources) Up(s.Attributes.GetTopLevel.DocObject);
                }
                foreach (var c in doc.Objects.OfType<Grasshopper.Kernel.IGH_Component>().Where(Consumer).ToArray()) Up(c);
                // No analysis runs (the Run button is never pressed: the definitions may name outputs in the main checkout).
                foreach (var o in doc.Objects.OfType<Grasshopper.Kernel.IGH_ActiveObject>())
                    if (!upstream.Contains(o) || o.GetType().Name == "StrausR3AnalysisComponent" || o.GetType().Name == "MovingStaticAnalysisComponent") o.Locked = true;
                BridgeSegmentExcelTests.Solve(doc, true);
                int before = tally.samples;
                var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
                var byContent = new HashSet<string>(StringComparer.Ordinal);
                void Content(string kind, object datum)
                {
                    if (capture == null) return;
                    string key = label + "|" + kind + "|" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(GpcDraftCapture.Text(datum))));
                    if (byContent.Add(key)) capture.Record(key, datum);
                }
                foreach (var p in doc.Objects.SelectMany(o => o is Grasshopper.Kernel.IGH_Component c ? c.Params.Input.Concat(c.Params.Output) : o is Grasshopper.Kernel.IGH_Param q ? new[] { q } : Enumerable.Empty<Grasshopper.Kernel.IGH_Param>()))
                {
                    var persistent = p.GetType().GetProperty("PersistentData")?.GetValue(p) as Grasshopper.Kernel.Data.IGH_Structure;
                    foreach (var (data, isPersistent) in new[] { (persistent, true), (p.VolatileData, false) })
                    {
                        if (data == null) continue;
                        foreach (var goo in data.AllData(true))
                        {
                            var value = goo?.GetType().GetProperty("Value")?.GetValue(goo);
                            if (value == null || value.GetType().Namespace?.StartsWith("BridGHe.Common", StringComparison.Ordinal) != true) continue;
                            census?.Add(label, value);
                            foreach (var (m, role, path) in Materials(value))
                            {
                                if (!seen.Add(m)) continue;
                                if (isPersistent) internalised++; else volatileData++;
                                tally.Check($"{label}:{p.Attributes?.GetTopLevel?.DocObject?.NickName}/{p.NickName}{(isPersistent ? " (internalised)" : "")}{path}", m, role);
                                Content("check-material", GpcMaterials.FromFemm(m));
                            }
                        }
                    }
                }
                // BGHGPC1 (i): the drafts of the Build Model inputs (and the GPC material of every Material datum) through the final writer and reader.
                foreach (var build in doc.Objects.OfType<Grasshopper.Kernel.IGH_Component>().Where(c => c.GetType().Name == "BuildModelComponent"))
                    for (int pi = 0; pi < build.Params.Input.Count; pi++)
                    {
                        var p = build.Params.Input[pi]; var data = p.VolatileData;
                        for (int bi = 0; bi < data.PathCount; bi++)
                        {
                            var branch = data.get_Branch(bi);
                            for (int i = 0; i < branch.Count; i++)
                            {
                                var goo = branch[i];
                                var gpc = goo?.GetType().GetProperty("Gpc")?.GetValue(goo);
                                if (gpc is LegacyNodeDraft || gpc is LegacyBeamDraft || gpc is LegacyPlateDraft)
                                    drafts.Check($"{label}:{build.NickName}/{p.NickName}", gpc, $"{label}|{build.InstanceGuid}|in{pi}|{data.get_Path(bi)}|{i}");
                            }
                        }
                    }
                foreach (var build in doc.Objects.OfType<Grasshopper.Kernel.IGH_Component>().Where(c => c.GetType().Name == "BuildModelComponent"))
                    foreach (var goo in build.Params.Output[0].VolatileData.AllData(true))
                        if (goo?.GetType().GetProperty("GpcInput")?.GetValue(goo) is GpcModels.Conversion conversion)
                            drafts.Check($"{label}:{build.NickName}/model", conversion, $"{label}|{build.InstanceGuid}|model");
                foreach (var p in doc.Objects.SelectMany(o => o is Grasshopper.Kernel.IGH_Component c ? c.Params.Input.Concat(c.Params.Output) : o is Grasshopper.Kernel.IGH_Param q ? new[] { q } : Enumerable.Empty<Grasshopper.Kernel.IGH_Param>()))
                    foreach (var goo in p.VolatileData.AllData(true))
                        if (goo?.GetType().Name == "GH_Material" && goo.GetType().GetProperty("Gpc")?.GetValue(goo) is Material material)
                        {
                            if (materials.Add(material)) { var keep = drafts.Capture; drafts.Capture = null; drafts.Check($"{label}:{p.NickName}", material); drafts.Capture = keep; }
                            Content("material", material);
                        }
                // The other GH types of the definition (cases, combinations, stages, beam, plate and composite properties, sections), every datum
                // as its GH type would carry it, through the final writer and reader.
                var others = new HashSet<object>(ReferenceEqualityComparer.Instance);
                foreach (var p in doc.Objects.SelectMany(o => o is Grasshopper.Kernel.IGH_Component c ? c.Params.Input.Concat(c.Params.Output) : o is Grasshopper.Kernel.IGH_Param q ? new[] { q } : Enumerable.Empty<Grasshopper.Kernel.IGH_Param>()))
                    foreach (var goo in p.VolatileData.AllData(true))
                    {
                        var value = goo?.GetType().GetProperty("Value")?.GetValue(goo);
                        if (!(value is LoadCaseModel || value is LoadCombinationModel || value is StagedConstructionModel || value is BeamPropertyBaseModel || value is PlatePropertyModel || value is SectionModel) || !others.Add(value)) continue;
                        object draft;
                        try { draft = GpcDraftArchiveChecks.DraftOf(value); }
                        catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException) { drafts.Count(value.GetType().Name + " refused by the reader"); Content(value.GetType().Name, new GpcDraftCapture.Refused(value.GetType().Name, x.Message)); continue; }
                        if (draft != null) { var keep = drafts.Capture; drafts.Capture = null; drafts.Check($"{label}:{p.NickName}", draft); drafts.Capture = keep; Content(draft.GetType().Name, draft); }
                    }
                Console.WriteLine($"MATERIAL SCAN {label}: {tally.samples - before} materials");
            }
            catch (Exception e) { failed++; Console.WriteLine($"MATERIAL SCAN {label}: FAILED {e.GetType().Name}: {e.Message}"); }
        }
        Console.WriteLine("Material kinds: " + tally.Kinds);
        foreach (var f in tally.failures) Console.WriteLine("  " + f);
        Console.WriteLine($"MATERIAL SCAN TOTAL {files.Count} files, {failed} failed, {tally.samples} materials ({internalised} internalised, {volatileData} computed), {tally.failures.Count} differences: {tally.Summary}");
        foreach (var d in drafts.Failures) Console.WriteLine("  " + d);
        Console.WriteLine($"DRAFT SCAN TOTAL (BGHGPC1 (i), the drafts of the Build Model inputs and the GPC materials): {drafts.Summary}; {drafts.Failures.Count} differences");
        census?.Print();
        capture?.Dispose();
        return failed == 0 && tally.failures.Count == 0 && drafts.Failures.Count == 0 && !GpcDraftCapture.Failed ? 0 : 1;
    }

    /// <summary>Every former material reachable from a FEM model (beam, plate and composite properties), distinct instances, with the role of
    /// a composite property (concrete, steel, rebar) or null.</summary>
    internal static IEnumerable<(MaterialModel material, string role, string path)> Materials(object root)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var found = new List<(MaterialModel, string, string)>();
        void Visit(object o, string field, string path)
        {
            if (o == null || o is string || o.GetType().IsPrimitive || o.GetType().IsEnum || !seen.Add(o)) return;
            if (o is MaterialModel m)
            {
                string role = field == "_concreteMaterial" ? "concrete" : field == "_steelMaterial" ? "steel" : field == "_rebarMaterial" ? "rebar" : null;
                found.Add((m, role, path)); return;
            }
            var t = o.GetType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                Visit(t.GetProperty("Key").GetValue(o), field, path + ".Key"); Visit(t.GetProperty("Value").GetValue(o), field, path + ".Value"); return;
            }
            if (o is IEnumerable e) { var items = new List<object>(); try { foreach (var item in e) items.Add(item); } catch (Exception) { return; } for (int i = 0; i < items.Count; i++) Visit(items[i], field, path.Length > 200 ? path : path + "[" + i + "]"); return; }
            if (t.Namespace == null || !t.Namespace.StartsWith("BridGHe.Common", StringComparison.Ordinal)) return;
            for (var c = t; c != null && c != typeof(object); c = c.BaseType)
                foreach (var f in c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (!f.FieldType.IsPrimitive && f.FieldType != typeof(string)) Visit(f.GetValue(o), f.Name, path.Length > 200 ? path : path + "." + f.Name);
        }
        Visit(root, null, "");
        return found;
    }
}
#endif
