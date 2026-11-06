#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using R = Rhino.Geometry;

/// <summary>
/// F6 R6.4 (1), regression <c>gpc-result-selection</c> (not native): the result catalogue of the analysis from the GPC input
/// (<see cref="GpcResultSelection"/>) against the former catalogue of the FEM model (<see cref="StrausResultSelection"/>), for the three
/// solvers, on the N-mm corpus of <c>--import-dump</c> (examples 01–10 in memory, the 77 captured inputs, the node, beam and plate data) and
/// synthetic models (combinations, stages, names sorted by culture, repeated and empty names, a name shared by a case and a combination, the
/// generated cases of an earlier moving analysis): the same entries (increment and name, in order) or the same error; the selection of all
/// the names, of a missing name and of none: the same entries or the same message. The input fingerprint of Moving Static Analysis
/// (<see cref="GpcResultSelection.InputHash"/>): the same for two conversions of a model and after the BGHGPC1 round trip of its GPC input,
/// different when a load, the tolerance or a moving path changes. R6.4 (3): the super elements of Super Element Filter from the bridge data
/// of the GPC input against the FEM attributes, on the same models. Declared by name: <see cref="Declared"/>.
/// </summary>
internal static class GpcResultSelectionChecks
{
    private const string Captures = @"C:\Users\g.pacini\Documents\Repository\gpc-f6-claude\P2B\results\capture-1";

    /// <summary>The declared differences, by the label of the synthetic model: the GPC input refuses a model the former catalogue refused
    /// with another message (the analysis of that model failed before R6.4 too, with the conversion message after Run).</summary>
    private static readonly Dictionary<string, string> Declared = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Two combinations with one name: the GPC combinations are unique by name, the conversion refuses the second (Build Model refuses
        // it since R6.3); the former catalogue refused the name as ambiguous.
        ["synthetic repeated combination"] = "conversion",
        // A combination with an empty (blank) name: a GPC combination refuses it («Combination name cannot be empty»); the former catalogue
        // refused the name as empty.
        ["synthetic empty combination name"] = "conversion",
    };

    private static string Text(IEnumerable<StrausResultSelection.Entry> entries) => string.Join("\n", entries.Select(e => e.Increment + " " + e.Case.Name));
    private static string Text(IEnumerable<GpcResultSelection.Entry> entries) => string.Join("\n", entries.Select(e => e.Increment + " " + e.Name));

    private static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException) { return "ERROR " + ex.GetType().Name + ": " + ex.Message; }
    }

    internal static void Regression()
    {
        BridgeMergeTests.Initialize();
        int models = 0, catalogues = 0, errors = 0, selections = 0, hashes = 0, declared = 0, superElements = 0; var seen = new HashSet<string>(StringComparer.Ordinal);
        void Fail(string label, string what) => throw new Exception("GPC RESULT SELECTION FAIL (" + label + "): " + what);
        void Check(string label, ModelModel fem, double tolerance)
        {
            models++;
            GpcModels.Conversion conversion = null; string refused = null;
            try { conversion = GpcModels.Convert(fem, tolerance); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException) { refused = ex.Message; }
            if (conversion == null)
            {
                if (!Declared.TryGetValue(label, out var kind) || kind != "conversion") Fail(label, "the GPC input refused: " + refused);
                bool femRefused = Enumerable.Range(0, 3).Any(s => Try(() => Text(StrausResultSelection.Catalog(fem, s))).StartsWith("ERROR", StringComparison.Ordinal));
                if (!femRefused) Fail(label, "the GPC input refused, the former catalogue did not: " + refused);
                Console.WriteLine($"GPC RESULT SELECTION DECLARED ({label}): the GPC input refused ('{refused}'), the former catalogue refused the names");
                declared++; seen.Add(label); return;
            }
            for (int solver = 0; solver < 3; solver++)
            {
                string x = Try(() => Text(StrausResultSelection.Catalog(fem, solver)));
                string y = Try(() => Text(GpcResultSelection.Catalog(conversion.Model, solver)));
                if (x != y) Fail(label, $"solver {solver}: former\n{x}\nGPC\n{y}");
                catalogues++; if (x.StartsWith("ERROR", StringComparison.Ordinal)) { errors++; continue; }
                var names = StrausResultSelection.Catalog(fem, solver).Select(e => e.Case.Name).ToArray();
                foreach (var requested in new[] { names, names.Take(1).Concat(new[] { "missing result" }).ToArray(), Array.Empty<string>(), names.Reverse().Take(2).ToArray() })
                {
                    string a = Try(() => Text(StrausResultSelection.Select(fem, solver, requested)));
                    string b = Try(() => Text(GpcResultSelection.Select(conversion.Model, solver, requested)));
                    if (a != b) Fail(label, $"solver {solver}, selection '{string.Join(", ", requested)}': former\n{a}\nGPC\n{b}");
                    selections++;
                }
            }
            // The input fingerprint: the same for a second conversion and after the BGHGPC1 round trip of the GPC input.
            string hash = GpcResultSelection.InputHash(conversion);
            if (!hash.StartsWith("GPC:", StringComparison.Ordinal)) Fail(label, "input fingerprint without the GPC prefix: " + hash);
            if (GpcResultSelection.InputHash(GpcModels.Convert(fem, tolerance)) != hash) Fail(label, "the input fingerprint of a second conversion differs");
            var (data, records) = GpcDraftArchive.Write(conversion);
            if (GpcResultSelection.InputHash(GpcDraftArchive.ReadConversion(data, records)) != hash) Fail(label, "the input fingerprint after the BGHGPC1 round trip differs");
            hashes++;
            // R6.4 (3): the super elements of Super Element Filter from the bridge data of the GPC input (names in the order of the beams, the
            // super element of every beam) against the FEM attributes (names of every element, the first attribute of every beam).
            {
                var beams = conversion.Model.BeamElements.Values.OrderBy(b => b.Id).ToArray();
                var of = beams.Select(b => conversion.Bridge.Beam(b.Guid)?.SuperElement).ToArray();
                string g = string.Join("\n", of.Where(s => s != null).Distinct(StringComparer.Ordinal)) + "\n--\n" + string.Join("\n", of.Select(s => s ?? "-"));
                string f = string.Join("\n", BridGHe.Common.Helpers.ModelHelper.GetSuperElements(fem).Select(a => a.Value)) + "\n--\n"
                    + string.Join("\n", fem.Beams.Select(b => SuperElementAttributeModel.GetSuperElement(b) ?? "-"));
                if (g != f) Fail(label, "super elements: former\n" + f + "\nGPC\n" + g);
                if (of.Any(s => s != null)) superElements++;
            }
            if (models % 20 == 0 || label.StartsWith("synthetic", StringComparison.Ordinal)) Console.WriteLine($"GPC RESULT SELECTION PASS ({label}): catalogues of the three solvers equal, input fingerprint stable");
        }

        foreach (var example in NativeExamples.Create()) Check("memory-" + example.Id, example.Model, 1e-6);
        Check("nodes", ImportDump.NodeData(ModelModel.ModelUnits.Nmm), 1e-3);
        Check("nodes-labelled-rigid", ImportDump.NodeData(ModelModel.ModelUnits.Nmm, true), 1e-3);
        Check("beams", ImportDump.BeamData(ModelModel.ModelUnits.Nmm, null), 1e-3);
        Check("plates", ImportDump.PlateData(ModelModel.ModelUnits.Nmm), 1e-3);
        if (Directory.Exists(Captures))
            foreach (var file in Directory.GetFiles(Captures, "*.bgmodel").OrderBy(f => f, StringComparer.Ordinal))
            {
                string info = Path.ChangeExtension(file, ".txt");
                double tolerance = File.Exists(info) ? double.Parse(File.ReadAllLines(info)[1], System.Globalization.CultureInfo.InvariantCulture) : 1e-6;
                Check("capture-" + Path.GetFileNameWithoutExtension(file), BridGHe.Common.Helpers.ModelArchive.Read<ModelModel>(File.ReadAllBytes(file)), tolerance);
            }
        else throw new Exception("GPC RESULT SELECTION FAIL: the captured inputs are missing: " + Captures);

        // Synthetic models on the beam data: cases of node loads with names sorted by culture, combinations, stages.
        ModelModel Synthetic(Action<ModelModel> change)
        {
            var m = ImportDump.BeamData(ModelModel.ModelUnits.Nmm, null);
            change(m);
            return m;
        }
        LoadCombinationModel Combination(string name, params (LoadCaseModel Case, double Factor)[] factors)
        {
            var c = new LoadCombinationModel(name); foreach (var (lc, f) in factors) c.Values.Add(lc, f); return c;
        }
        var q = new LoadCaseModel("", "Q"); var cases = new[] { "b", "A", "a", "Z1", "é", "e", "Q 2", "q" }.Select(n => new LoadCaseModel("", n)).ToArray();
        void Loads(ModelModel m) { int k = 0; foreach (var c in cases) m.Nodes[k++ % m.Nodes.Count].Loads.Add(new NodeForceLoadModel(c, new R.Vector3d(0, 0, -1 - k))); }
        Check("synthetic cases sorted", Synthetic(m => { Loads(m); m.LoadCases.Add(new LoadCaseModel("", "registered only")); }), 1e-3);
        Check("synthetic combinations", Synthetic(m => { Loads(m); m.AddLoadCombinations(new[] { Combination("C2", (cases[0], 1.0), (q, 1.5)), Combination("C1", (cases[1], 2.0)) }); }), 1e-3);
        Check("synthetic stages", Synthetic(m =>
        {
            Loads(m); var c = Combination("C", (q, 1.0)); m.AddLoadCombinations(new[] { c, Combination("D", (cases[2], 1.0)) });
            foreach (string name in new[] { "Phase 1", "Phase 2" }) { var stage = new StagedConstructionModel(name); stage.AddLoadCombinations(new[] { c, Combination("E", (cases[3], 1.0)) }); m.AddStagedConstructions(new[] { stage }); }
        }), 1e-3);
        Check("synthetic repeated stage", Synthetic(m =>
        {
            var c = Combination("C", (q, 1.0));
            foreach (string name in new[] { "Phase", "Phase" }) { var stage = new StagedConstructionModel(name); stage.AddLoadCombinations(new[] { c }); m.AddStagedConstructions(new[] { stage }); }
        }), 1e-3);
        Check("synthetic case and combination named alike", Synthetic(m => { Loads(m); m.AddLoadCombinations(new[] { Combination("A", (q, 1.0)) }); }), 1e-3);
        Check("synthetic empty combination name", Synthetic(m => m.AddLoadCombinations(new[] { Combination(" ", (q, 1.0)) })), 1e-3);
        Check("synthetic repeated combination", Synthetic(m => m.AddLoadCombinations(new[] { Combination("C", (q, 1.0)), Combination("C", (q, 2.0)) })), 1e-3);
        var moving = NativeExamples.Create().Single(e => e.Id == "04-moving-load").Model;
        {
            // An earlier moving analysis: its generated cases are registered cases of the FEM model, left out by both catalogues.
            var analysed = BridGHe.Common.Helpers.ModelArchive.Clone(moving);
            analysed.GeneratedMovingCases.Clear(); analysed.GeneratedMovingCases[1] = "Generated 1"; analysed.GeneratedMovingCases[2] = "Generated 2";
            foreach (var g in analysed.GeneratedMovingCases) analysed.LoadCases.Add(new LoadCaseModel(g.Key.ToString(), g.Value));
            Check("synthetic generated moving cases", analysed, 1e-6);
        }
        foreach (var label in Declared.Keys) if (!seen.Contains(label)) throw new Exception("GPC RESULT SELECTION FAIL: declared difference not met: " + label);

        // The input fingerprint changes with what the analysis reads: a load, the tolerance, a moving path.
        {
            var a = Synthetic(Loads); string h = GpcResultSelection.InputHash(GpcModels.Convert(a, 1e-3));
            var b = Synthetic(Loads); ((NodeForceLoadModel)b.Nodes[0].Loads[0]).Value = new R.Vector3d(0, 0, -100);
            if (GpcResultSelection.InputHash(GpcModels.Convert(b, 1e-3)) == h) throw new Exception("GPC RESULT SELECTION FAIL: the input fingerprint does not see a load");
            if (GpcResultSelection.InputHash(GpcModels.Convert(a, 2e-3)) == h) throw new Exception("GPC RESULT SELECTION FAIL: the input fingerprint does not see the tolerance");
            string m0 = GpcResultSelection.InputHash(GpcModels.Convert(moving, 1e-6));
            var other = BridGHe.Common.Helpers.ModelArchive.Clone(moving); other.MovingPaths[0].LoadCaseName = other.MovingPaths[0].LoadCaseName + " 2";
            if (GpcResultSelection.InputHash(GpcModels.Convert(other, 1e-6)) == m0) throw new Exception("GPC RESULT SELECTION FAIL: the input fingerprint does not see a moving path");
            Console.WriteLine("GPC RESULT SELECTION: the input fingerprint changes with a load, the tolerance and a moving path");
        }
        Console.WriteLine($"GPC RESULT SELECTION PASS: {models} models, {catalogues} catalogues equal ({errors} the same error), {selections} selections equal, {hashes} input fingerprints stable, super elements equal ({superElements} models with super elements), {declared} declared ({string.Join("; ", Declared.Keys)})");
    }
}
#endif
