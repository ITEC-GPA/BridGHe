#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;

/// <summary>
/// R6.6 lane R2b, regression <c>gpc-result-selection</c> (not native), on GPC data alone: the result catalogue of the analysis from the GPC
/// input (<see cref="GpcResultSelection"/>) against its contract written here (the former parity with the catalogue of the FEM model,
/// <c>StrausResultSelection</c>, is retired):
/// <list type="bullet">
/// <item>solver 0: «Dead» first, then the other cases by the comparison of the current culture, then the combinations in their order, increment 0;</item>
/// <item>solver 1: the combinations in their order, increments 1, 2, …;</item>
/// <item>solver 2: «combination [stage]» for every load increment of every stage (stages in their order), increments numbered across the stages;</item>
/// <item>an empty or repeated name: the ambiguity message; a selection of all the names: the catalogue; of a missing name: the missing message with
/// the name; of none: the empty-selection message; of some names: those entries in catalogue order.</item>
/// </list>
/// On examples 01–10 built in GPC (<see cref="GpcNativeExamples"/>), the 0.3.0 corpus read by value (<see cref="R3LegacyCorpus"/>: the 77
/// captures, the 10 captured examples, the N-mm node, labelled node, beam and plate data) and synthetic models of Build Model on GPC, with
/// literal expected catalogues (names sorted by culture, combinations, stages, a name shared by a case and a combination, an empty and a
/// repeated combination name, a repeated stage). The input fingerprint of Moving Static Analysis (<see cref="GpcResultSelection.InputHash"/>):
/// «GPC:», the same for two conversions and after the BGHGPC1 round trip, different when a load, the tolerance or a moving path changes. The
/// super elements of Super Element Filter from the bridge data of the GPC input against the super-element attributes of the 0.3.0 archive.
/// </summary>
internal static class GpcResultSelectionChecks
{
    private static string Text(IEnumerable<GpcResultSelection.Entry> entries) => string.Join("\n", entries.Select(e => e.Increment + " " + e.Name));

    private static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException) { return "ERROR " + ex.Message; }
    }

    /// <summary>The contract of the catalogue on the GPC input (the expected entries, or the ambiguity message).</summary>
    private static string Expected(GPC.Model.Models.Model m, int solver)
    {
        var lines = new List<(int Increment, string Name)>();
        if (solver == 0)
        {
            var cases = m.LoadCases.Values.Select(c => c.Name).ToList();
            var others = cases.Where(c => c != GpcResultSelection.DeadName).ToList();
            others.Sort((a, b) => a.Equals(b) ? 0 : a.CompareTo(b)); // the comparison of the current culture, as the former load cases
            lines.AddRange(cases.Where(c => c == GpcResultSelection.DeadName).Concat(others).Select(n => (0, n)));
            lines.AddRange(m.Combinations.Values.Select(c => (0, c.Name)));
        }
        else if (solver == 1) lines.AddRange(m.Combinations.Values.Select((c, i) => (i + 1, c.Name)));
        else
            foreach (var stage in m.Stages.Values.OrderBy(s => s.Id))
                foreach (var increment in stage.AnalysisState?.Increments ?? Array.Empty<GPC.Model.Stages.StageLoadIncrement>())
                    lines.Add((lines.Count + 1, increment.Name + " [" + stage.Name + "]"));
        if (lines.Any(l => string.IsNullOrWhiteSpace(l.Name)) || lines.GroupBy(l => l.Name, StringComparer.Ordinal).Any(g => g.Count() > 1)) return "ERROR " + GpcResultSelection.AmbiguousMessage;
        return string.Join("\n", lines.Select(l => l.Increment + " " + l.Name));
    }

    internal static void Regression()
    {
        BridgeMergeTests.Initialize();
        int models = 0, catalogues = 0, errors = 0, selections = 0, hashes = 0, superElements = 0, literal = 0;
        void Fail(string label, string what) => throw new Exception("GPC RESULT SELECTION FAIL (" + label + "): " + what);
        void Check(string label, GpcModels.Conversion conversion, Func<GpcModels.Conversion> again, string[] superElementsOfBeams = null, IReadOnlyDictionary<int, string> literalCatalogues = null)
        {
            models++;
            for (int solver = 0; solver < 3; solver++)
            {
                string x = Expected(conversion.Model, solver), y = Try(() => Text(GpcResultSelection.Catalog(conversion.Model, solver)));
                if (x != y) Fail(label, $"solver {solver}: expected\n{x}\nGPC\n{y}");
                if (literalCatalogues != null && literalCatalogues.TryGetValue(solver, out var written))
                {
                    if (written != y) Fail(label, $"solver {solver}: written catalogue\n{written}\nGPC\n{y}");
                    literal++;
                }
                catalogues++; if (x.StartsWith("ERROR", StringComparison.Ordinal)) { errors++; continue; }
                var names = GpcResultSelection.Catalog(conversion.Model, solver).Select(e => e.Name).ToArray();
                string Lines(IEnumerable<string> wanted) { var w = wanted.ToHashSet(StringComparer.Ordinal); return string.Join("\n", x.Split('\n').Where(l => l.Length > 0 && w.Contains(l.Substring(l.IndexOf(' ') + 1)))); }
                var requests = new (string[] Names, string Expected)[]
                {
                    (names, names.Length == 0 ? "ERROR " + GpcResultSelection.EmptySelectionMessage : x),
                    (names.Take(1).Concat(new[] { "missing result" }).ToArray(), "ERROR " + GpcResultSelection.MissingPrefix + "missing result"),
                    (Array.Empty<string>(), "ERROR " + GpcResultSelection.EmptySelectionMessage),
                    (names.Reverse().Take(2).ToArray(), names.Length == 0 ? "ERROR " + GpcResultSelection.EmptySelectionMessage : Lines(names.Reverse().Take(2))),
                };
                foreach (var (requested, expected) in requests)
                {
                    string b = Try(() => Text(GpcResultSelection.Select(conversion.Model, solver, requested)));
                    if (b != expected) Fail(label, $"solver {solver}, selection '{string.Join(", ", requested)}': expected\n{expected}\nGPC\n{b}");
                    selections++;
                }
            }
            // The input fingerprint: «GPC:», the same for a second conversion and after the BGHGPC1 round trip of the GPC input.
            string hash = GpcResultSelection.InputHash(conversion);
            if (!hash.StartsWith("GPC:", StringComparison.Ordinal)) Fail(label, "input fingerprint without the GPC prefix: " + hash);
            if (GpcResultSelection.InputHash(again()) != hash) Fail(label, "the input fingerprint of a second conversion differs");
            var (data, records) = GpcDraftArchive.Write(conversion);
            if (GpcResultSelection.InputHash(GpcDraftArchive.ReadConversion(data, records)) != hash) Fail(label, "the input fingerprint after the BGHGPC1 round trip differs");
            hashes++;
            if (superElementsOfBeams != null)
            {
                // Super Element Filter: the names in the order of the beams, and the super element of every beam, from the bridge data.
                var of = conversion.Model.BeamElements.Values.OrderBy(b => b.Id).Select(b => conversion.Bridge.Beam(b.Guid)?.SuperElement).ToArray();
                string g = string.Join("\n", of.Where(s => s != null).Distinct(StringComparer.Ordinal)) + "\n--\n" + string.Join("\n", of.Select(s => s ?? "-"));
                string f = string.Join("\n", superElementsOfBeams.Where(s => s != null).Distinct(StringComparer.Ordinal)) + "\n--\n" + string.Join("\n", superElementsOfBeams.Select(s => s ?? "-"));
                if (g != f) Fail(label, "super elements: archive\n" + f + "\nGPC\n" + g);
                if (of.Any(s => s != null)) superElements++;
            }
            if (models % 20 == 0 || label.StartsWith("synthetic", StringComparison.Ordinal)) Console.WriteLine($"GPC RESULT SELECTION PASS ({label}): catalogues of the three solvers as the contract, input fingerprint stable");
        }

        // Examples 01–10 built in GPC.
        var examples = GpcNativeExamples.Create().ToArray(); var examplesAgain = GpcNativeExamples.Create().ToArray();
        for (int i = 0; i < examples.Length; i++) { int k = i; Check("example-" + examples[i].Id, examples[i].Input, () => examplesAgain[k].Input); }
        // The 0.3.0 corpus read by value: the conversion of the reader (owner of the content), its super elements against the archive attributes.
        GpcModels.Conversion Read(R3LegacyCorpus.Item item)
        {
            var report = new LegacyImportReport();
            var owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r2b-selection:" + item.Id);
            var model = LegacyModelImport.Import(item.Root, owner, item.Tolerance, report, item.Signatures);
            return GpcModels.Conversion.FromGpc(model, report.Units, report.BeamAngles, item.Tolerance, report.NativeSections, report.Bridge);
        }
        static string[] Archived(R3LegacyCorpus.Item item) => item.Root.Field("Elements").Items.Where(e => e.Is("BeamModel"))
            .Select(b => b.Field("Attributes").Items.FirstOrDefault(a => a.Is("SuperElementAttributeModel"))?.String("Value")).ToArray();
        var corpus = R3LegacyCorpus.Examples().Concat(new[] { "nodes-Nmm", "nodes-labelled-rigid-Nmm", "beams-Nmm", "plates-Nmm" }.Select(R3LegacyCorpus.Fixture)).Concat(R3LegacyCorpus.Captures());
        foreach (var item in corpus) Check(item.Id, Read(item), () => Read(item), Archived(item));

        // Synthetic models of Build Model on GPC: the cantilever of R0 with node loads in the cases, combinations and stages given.
        var cantilever = GpcFixtures.CantileverInputs("result selection");
        string[] caseNames = { "b", "A", "a", "Z1", "é", "e", "Q 2", "q" };
        GpcModels.Conversion Synthetic(string name, bool loads, Func<Dictionary<string, LegacyCaseData>, (List<LegacyCombinationDraft>, List<LegacyStageDraft>)> extra = null, double tolerance = 1e-3)
        {
            var cases = caseNames.Append("Q").ToDictionary(n => n, n => GpcCaseBuilders.Case(n, false), StringComparer.Ordinal);
            var node = cantilever.Nodes[1]; int k = 0;
            if (loads) foreach (var n in caseNames) node = GpcNodeBuilders.WithForce(node, cases[n], 0, 0, -1 - ++k, GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r2b-selection-load:" + n));
            var (combinations, stages) = extra?.Invoke(cases) ?? (new List<LegacyCombinationDraft>(), new List<LegacyStageDraft>());
            var nodes = new[] { cantilever.Nodes[0], node };
            return LegacyBuildModel.Build(name, new BridgeUnits(DefinitionUnits.Nmm), tolerance, nodes.Select(d => (d.Node.Guid, d)).ToList(), cantilever.Beams.Select(d => (d.Beam.Guid, d)).ToList(),
                new List<(Guid, LegacyPlateDraft)>(), combinations, stages);
        }
        LegacyCombinationDraft Combination(Dictionary<string, LegacyCaseData> c, string name, params (string Case, double Factor)[] factors) =>
            GpcCaseBuilders.Combination(name, factors.Select(f => c[f.Case]).ToArray(), factors.Select(f => f.Factor).ToArray());
        // The cases by culture: «Dead» first, then a A b e é q «Q 2» Z1 (the order of the former load cases).
        string sorted = "0 Dead\n0 a\n0 A\n0 b\n0 e\n0 é\n0 q\n0 Q 2\n0 Z1";
        void Synth(string label, bool loads, Func<Dictionary<string, LegacyCaseData>, (List<LegacyCombinationDraft>, List<LegacyStageDraft>)> extra, Dictionary<int, string> written)
        {
            GpcModels.Conversion model;
            try { model = Synthetic(label, loads, extra); }
            catch (ArgumentException e) { if (written == null || !written.TryGetValue(-1, out var refusal) || !e.Message.Contains(refusal)) throw; Console.WriteLine($"GPC RESULT SELECTION ({label}): refused by Build Model: {e.Message}"); literal++; return; }
            Check(label, model, () => Synthetic(label, loads, extra), null, written);
        }
        Synth("synthetic cases sorted", true, null, new Dictionary<int, string> { [0] = sorted, [1] = "", [2] = "" });
        Synth("synthetic combinations", true, c => (new List<LegacyCombinationDraft> { Combination(c, "C2", ("b", 1.0), ("Q", 1.5)), Combination(c, "C1", ("A", 2.0)) }, new List<LegacyStageDraft>()),
            new Dictionary<int, string> { [1] = "1 C2\n2 C1" });
        Synth("synthetic stages", true, c =>
        {
            var cc = Combination(c, "C", ("Q", 1.0));
            var stages = new[] { "Phase 1", "Phase 2" }.Select(n => GpcCaseBuilders.Stage(n, new[] { GpcCaseBuilders.Group("Girder", null) }, new[] { cc, Combination(c, "E", ("Z1", 1.0)) }, false, false)).ToList();
            return (new List<LegacyCombinationDraft> { cc, Combination(c, "D", ("a", 1.0)) }, stages);
        }, new Dictionary<int, string> { [1] = "1 C\n2 D", [2] = "1 C [Phase 1]\n2 E [Phase 1]\n3 C [Phase 2]\n4 E [Phase 2]" });
        Synth("synthetic case and combination named alike", true, c => (new List<LegacyCombinationDraft> { Combination(c, "A", ("Q", 1.0)) }, new List<LegacyStageDraft>()),
            new Dictionary<int, string> { [0] = "ERROR " + GpcResultSelection.AmbiguousMessage, [1] = "1 A" });
        Synth("synthetic repeated stage", false, c =>
        {
            var cc = Combination(c, "C", ("Q", 1.0));
            return (new List<LegacyCombinationDraft> { cc }, new[] { "Phase", "Phase" }.Select(n => GpcCaseBuilders.Stage(n, new[] { GpcCaseBuilders.Group("Girder", null) }, new[] { cc }, false, false)).ToList());
        }, new Dictionary<int, string> { [-1] = "", [2] = "ERROR " + GpcResultSelection.AmbiguousMessage });
        // An empty and a repeated combination name: a GPC combination refuses an empty name, Build Model a repeated one (declared since R6.3).
        bool Refused(Func<object> f) { try { f(); return false; } catch (ArgumentException) { return true; } }
        if (!Refused(() => Synthetic("synthetic empty combination name", false, c => (new List<LegacyCombinationDraft> { Combination(c, " ", ("Q", 1.0)) }, new List<LegacyStageDraft>()))))
            Fail("synthetic empty combination name", "a combination with a blank name accepted");
        if (!Refused(() => Synthetic("synthetic repeated combination", false, c => (new List<LegacyCombinationDraft> { Combination(c, "C", ("Q", 1.0)), Combination(c, "C", ("Q", 2.0)) }, new List<LegacyStageDraft>()))))
            Fail("synthetic repeated combination", "two combinations with one name accepted");
        // The moving example (04): the cases of its paths are registered; generated cases of an earlier analysis are never in the input.
        var moving = examples.Single(e => e.Id == "04-moving-load").Input;
        if (Expected(moving.Model, 0).Contains("Generated")) Fail("04-moving-load", "generated moving cases in the input");

        // The input fingerprint changes with what the analysis reads: a load, the tolerance, a moving path.
        {
            string h = GpcResultSelection.InputHash(Synthetic("fingerprint", true));
            var other = GpcFixtures.CantileverInputs("result selection");
            var loaded = GpcNodeBuilders.WithForce(other.Nodes[1], GpcCaseBuilders.Case("b", false), 0, 0, -100, GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r2b-selection-load:b"));
            var changedLoad = LegacyBuildModel.Build("fingerprint", new BridgeUnits(DefinitionUnits.Nmm), 1e-3, new[] { other.Nodes[0], loaded }.Select(d => (d.Node.Guid, d)).ToList(),
                other.Beams.Select(d => (d.Beam.Guid, d)).ToList(), new List<(Guid, LegacyPlateDraft)>(), new List<LegacyCombinationDraft>(), new List<LegacyStageDraft>());
            if (GpcResultSelection.InputHash(changedLoad) == h) throw new Exception("GPC RESULT SELECTION FAIL: the input fingerprint does not see a load");
            if (GpcResultSelection.InputHash(Synthetic("fingerprint", true, tolerance: 2e-3)) == h) throw new Exception("GPC RESULT SELECTION FAIL: the input fingerprint does not see the tolerance");
            var pathIdentity = GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r2b-selection-moving");
            string m0 = GpcResultSelection.InputHash(GpcModelTransforms.ReplacePaths(moving, new[] { GpcNativeExamples.Path04(6000) }, pathIdentity));
            var renamedPath = GpcNativeExamples.Path04(6000); renamedPath.LoadCaseName += " 2";
            var renamed = GpcModelTransforms.ReplacePaths(moving, new[] { renamedPath }, pathIdentity);
            if (GpcResultSelection.InputHash(renamed) == m0) throw new Exception("GPC RESULT SELECTION FAIL: the input fingerprint does not see a moving path");
            Console.WriteLine("GPC RESULT SELECTION: the input fingerprint changes with a load, the tolerance and a moving path");
        }
        Console.WriteLine($"GPC RESULT SELECTION PASS: {models} models on GPC data alone, {catalogues} catalogues as the contract ({errors} the ambiguity error), {literal} written catalogues or refusals, {selections} selections, {hashes} input fingerprints stable, super elements as the archive ({superElements} models with super elements)");
    }
}
#endif
