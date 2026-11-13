using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// P2CE phase 2a (13/11/2026): the 0.3.0 reader without the FEM CLR types.
/// <para>(1) Inventory: the items that the archive of the CLR types (<c>ModelArchive</c>) still read until phase 2a (the «LoadCases» item of
/// Staged Construction, «Definition», «Design», «BridgeGeometry»), the actions with their FEM source and the composite previews of the sections,
/// in the example files and in the frozen fixtures: counted, with the FEM types inside each item.</para>
/// <para>(2) Parity while both readers exist: the cases of a «LoadCases» item read by value (<c>GpcLegacyPayloads.Cases</c>, the reader of
/// Staged Construction from phase 2a) equal, record by record, the cases of the CLR reading converted as before (<c>GpcModels.CaseDraft</c>),
/// for every item of the corpus and for the lists of the corpus cases as 0.3.0 wrote them. Retired by name in phase 2b with the CLR reader.</para>
/// <para>(3) P2P-01 (review of ChatGPT task 45): the factors of a standalone Load Combinations parameter wired to a stage, with an oracle on the
/// original factors: after Read before the solve; an old development file (LoadCases = [], Build Model not run); save and reopen with the
/// catalogue (also 0 and a negative factor); an authoritative empty catalogue; a damaged chunk.</para>
/// </summary>
internal static class P2ceReader
{
    private static readonly string[] Items = { "LoadCases", "Definition", "Design", "BridgeGeometry" };

    internal static void Regression()
    {
        var failures = new List<string>();
        Inventory(failures, out var corpusCases);
        int lists = Parity(failures, corpusCases);
        Console.WriteLine($"P2CE READER PARITY: {lists} lists of cases compared ({failures.Count} failures so far)");
        int stage = StageChecks(failures);
        if (failures.Count > 0) throw new Exception("P2CE READER FAIL: " + string.Join(" | ", failures.Take(20)));
        Console.WriteLine($"P2CE READER PASS: inventory above; {lists} lists of cases equal by value and by the CLR reader; {stage} checks of P2P-01 (Staged Construction and Load Combinations).");
    }

    // -----------------------------------------------------------------------------------------------------------------------------------
    // (1) Inventory

    private sealed class Tally
    {
        public readonly SortedDictionary<string, int> Counts = new(StringComparer.Ordinal);
        public void Add(string key, int n = 1) { Counts.TryGetValue(key, out int c); Counts[key] = c + n; }
        public string Text => Counts.Count == 0 ? "none" : string.Join(", ", Counts.Select(c => c.Key + " " + c.Value));
    }

    /// <summary>The types of a value graph (references resolved by the reader: visited once).</summary>
    private static void Walk(LegacyValue v, Action<LegacyValue> visit, HashSet<LegacyValue> seen)
    {
        if (v == null || v.IsNull || !seen.Add(v)) return;
        visit(v);
        foreach (var f in v.Fields.Values) Walk(f, visit, seen);
        foreach (var i in v.Items) Walk(i, visit, seen);
    }

    private static void Payload(string where, string item, byte[] bytes, Tally tally, List<string> failures, List<byte[]> cases)
    {
        LegacyValue root;
        try { root = LegacyArchive.Read(bytes); }
        catch (Exception x) { failures.Add(where + ": not readable by value: " + x.Message); return; }
        bool watched = Items.Contains(item);
        if (watched) tally.Add("item " + item + " (" + root.ShortType + ")");
        if (root.Is("LoadCaseModel")) cases?.Add(bytes);
        Walk(root, v =>
        {
            if (v.Type != null && v.Type.Contains("BridGHe.Common.Models.") && watched && !ReferenceEquals(v, root)) tally.Add("FEM type in " + item + ": " + v.ShortType);
            if (v.Is("BridgeActionDefinition")) tally.Add("BridgeActionDefinition" + (v.Has("Source") ? " with a FEM source" : " without source"));
            if (v.Is("SectionModel") && v.Has("CompositePreview")) tally.Add("SectionModel with a composite preview");
            if (v.Is("BridgeSectionRule") || v.Is("BridgeFamilyProperty")) tally.Add(v.ShortType + (v.Has("Property") ? " with a FEM property" : " without FEM property"));
        }, new HashSet<LegacyValue>(ReferenceEqualityComparer.Instance));
        if (item == "LoadCases" && root.IsCollection) tally.Add("LoadCases with " + (root.Items.Count == 0 ? "no case" : "cases"));
    }

    private static void Inventory(List<string> failures, out List<List<byte[]>> corpusCases)
    {
        corpusCases = new List<List<byte[]>>();
        var files = Directory.GetFiles(ExamplePaths.Root, "*.gh*", SearchOption.AllDirectories).Where(f => f.EndsWith(".gh") || f.EndsWith(".ghx"))
            .OrderBy(f => f, StringComparer.Ordinal).ToArray();
        var examples = new Tally(); int payloads = 0;
        foreach (var file in files)
        {
            var archive = new GH_IO.Serialization.GH_Archive();
            if (!archive.ReadFromFile(file)) { failures.Add(file + ": not readable"); continue; }
            var cases = new List<byte[]>();
            void Chunk(GH_IO.Serialization.GH_IChunk chunk, string path)
            {
                foreach (var item in chunk.Items)
                {
                    if (item.Type != GH_IO.Types.GH_Types.gh_bytearray || !LegacyArchive.IsLegacy(item._bytearray)) continue;
                    payloads++;
                    Payload(Path.GetRelativePath(ExamplePaths.Root, file) + ":" + path + "/" + item.Name, item.Name, item._bytearray, examples, failures, cases);
                }
                foreach (var c in chunk.Chunks) Chunk(c, path + "/" + c.Name);
            }
            Chunk(archive.GetRootNode, "");
            if (cases.Count > 0) corpusCases.Add(cases);
        }
        Console.WriteLine($"P2CE READER INVENTORY examples: {files.Length} files, {payloads} BGHA1 payloads; {examples.Text}");
        string fixtures = Path.Combine(Path.GetDirectoryName(ExamplePaths.Root), "tests", "BridGHe.Smoke", "Fixtures");
        foreach (var name in new[] { "legacy-payloads", "draft-merge", "p2d-payloads", "lane3d-payloads" })
        {
            string folder = Path.Combine(fixtures, name);
            if (!Directory.Exists(folder)) { Console.WriteLine($"P2CE READER INVENTORY fixture {name}: not on this branch"); continue; }
            var tally = new Tally(); int legacy = 0, all = 0;
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                all++;
                var bytes = File.ReadAllBytes(file);
                if (!LegacyArchive.IsLegacy(bytes)) continue;
                legacy++;
                Payload(name + "/" + Path.GetFileName(file), "", bytes, tally, failures, null);
                try { tally.Add("root " + LegacyArchive.Read(bytes).ShortType); } catch { }
            }
            Console.WriteLine($"P2CE READER INVENTORY fixture {name}: {all} files, {legacy} BGHA1 payloads; {tally.Text}");
        }
    }

    // -----------------------------------------------------------------------------------------------------------------------------------
    // (2) Parity of the «LoadCases» item

    private static string Records(IEnumerable<LegacyCaseData> cases) => string.Join("|", cases.Select(c => Convert.ToHexString(GpcDraftArchive.Write(c))));

    /// <summary>The cases of a «LoadCases» item by value and by the CLR reader (the former path of Staged Construction), equal record by record.</summary>
    private static bool Same(byte[] item, List<string> failures, string label)
    {
        var clr = BridGHe.Common.Helpers.ModelArchive.Read<List<BridGHe.Common.Models.LoadCaseModel>>(item).Select(GpcModels.CaseDraft).ToArray();
        var value = GpcLegacyPayloads.Cases(item);
        if (clr.Length != value.Length || Records(clr) != Records(value)) { failures.Add(label + ": the cases by value differ from the CLR reading (" + value.Length + " / " + clr.Length + ")"); return false; }
        return true;
    }

    private static int Parity(List<string> failures, List<List<byte[]>> corpusCases)
    {
        int lists = 0;
        // The lists of the cases of every example file, as 0.3.0 wrote the item (ModelArchive.Write of the list of FEM cases), and each case alone.
        foreach (var cases in corpusCases)
        {
            var femCases = cases.Select(b => BridGHe.Common.Helpers.ModelArchive.Read<BridGHe.Common.Models.LoadCaseModel>(b)).ToList();
            if (Same(BridGHe.Common.Helpers.ModelArchive.Write(femCases), failures, "corpus list of " + femCases.Count + " cases")) lists++;
            foreach (var c in femCases.Take(3))
                if (Same(BridGHe.Common.Helpers.ModelArchive.Write(new List<BridGHe.Common.Models.LoadCaseModel> { c }), failures, "corpus case " + c.Name)) lists++;
        }
        // The empty item of 0.4.0 (an unknown catalogue for the stage).
        if (Same(BridGHe.Common.Helpers.ModelArchive.Write(new List<BridGHe.Common.Models.LoadCaseModel>()), failures, "empty list")) lists++;
        if (corpusCases.Count == 0) failures.Add("parity: no case in the corpus");
        return lists;
    }

    // -----------------------------------------------------------------------------------------------------------------------------------
    // (3) P2P-01

    private static IGH_Goo One(string type, params (int Input, object Value)[] inputs)
    {
        var doc = new GH_Document();
        var c = BridgeSegmentExcelTests.Add(doc, type);
        foreach (var (input, value) in inputs) Set(c.Params.Input[input], value);
        BridgeSegmentExcelTests.Solve(doc, true);
        return c.Params.Output[0].VolatileData.AllData(true).FirstOrDefault()?.Duplicate() ?? throw new Exception("P2CE: " + type + " gives no output");
    }

    private static void Set(IGH_Param p, object value)
    {
        p.RemoveAllSources();
        var data = p.GetType().GetProperty("PersistentData").GetValue(p);
        data.GetType().GetMethod("Clear").Invoke(data, null);
        var append = data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(GH_Path));
        foreach (var v in value is object[] list ? list : new[] { value })
            append.Invoke(data, new object[] { v switch { IGH_Goo g => g, double d => new GH_Number(d), bool b => new GH_Boolean(b), string s => new GH_String(s), _ => new GH_ObjectWrapper(v) }, new GH_Path(0) });
        p.ExpireSolution(false);
    }

    private static string Factors(LegacyCombinationDraft c) => c == null ? "null" : c.Name + ": " + string.Join(", ", c.Factors.Select(f => f.Case.Name + " " + f.Factor.ToString("R")));

    /// <summary>A document with a standalone Load Combinations parameter (its persistent combinations) wired to the LCB port of a stage; the stage
    /// read from <paramref name="saved"/> (a chunk written by a stage) or given the catalogue <paramref name="catalogue"/> (Build Model).</summary>
    private sealed class Rig
    {
        public GH_Document Doc = new GH_Document();
        public IGH_Param Param;
        public GH_Component Stage;
        public Rig(IGH_Goo[] combinations, IGH_Goo group, Action<GH_Component> prepare)
        {
            var plugin = R2aDesignFixtures.Plugin;
            Param = (IGH_Param)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "LoadCombinationsParam"));
            Param.CreateAttributes(); Doc.AddObject(Param, false);
            var data = Param.GetType().GetProperty("PersistentData").GetValue(Param);
            foreach (var c in combinations) data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 1).Invoke(data, new object[] { c.Duplicate() });
            Stage = BridgeSegmentExcelTests.Add(Doc, "StagedConstructionComponent");
            prepare?.Invoke(Stage);
            Set(Stage.Params.Input[0], "S1"); Set(Stage.Params.Input[1], group); Set(Stage.Params.Input[3], false); Set(Stage.Params.Input[4], false);
            Stage.Params.Input[2].RemoveAllSources(); Stage.Params.Input[2].AddSource(Param);
        }
        public string[] Persistent()
        {
            var data = Param.GetType().GetProperty("PersistentData").GetValue(Param);
            return ((IEnumerable<IGH_Goo>)data.GetType().GetMethod("AllData").Invoke(data, new object[] { true })).Select(g => Factors(R2aDesignFixtures.Datum<LegacyCombinationDraft>(g))).ToArray();
        }
        public string[] Solve(out string[] errors)
        {
            BridgeSegmentExcelTests.Solve(Doc, true);
            errors = Stage.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToArray();
            var stage = R2aDesignFixtures.Datum<LegacyStageDraft>(Stage.Params.Output[0].VolatileData.AllData(true).FirstOrDefault());
            return stage == null ? new[] { "no stage" } : stage.Increments.Select(Factors).ToArray();
        }
    }

    private static byte[] Save(GH_Component stage)
    {
        var archive = new GH_IO.Serialization.GH_Archive(); archive.CreateNewRoot(false);
        var chunk = archive.GetRootNode.CreateChunk("Stage");
        if (!stage.Write(chunk)) throw new Exception("P2CE: the stage was not written");
        return archive.Serialize_Binary();
    }

    private static Action<GH_Component> Opened(byte[] saved, Action<GH_IO.Serialization.GH_IWriter> damage = null) => stage =>
    {
        var archive = new GH_IO.Serialization.GH_Archive();
        if (!archive.Deserialize_Binary(saved)) throw new Exception("P2CE: the saved stage is not readable");
        var chunk = archive.GetRootNode.FindChunk("Stage");
        if (damage != null) damage((GH_IO.Serialization.GH_IWriter)chunk);
        if (!stage.Read(chunk)) throw new Exception("P2CE: the stage was not read");
    };

    /// <summary>A damaged records chunk: one of its integers replaced.</summary>
    private static void Replace(GH_IO.Serialization.GH_IWriter stage, string item, int value)
    {
        var chunk = (GH_IO.Serialization.GH_IWriter)((GH_IO.Serialization.GH_IReader)stage).FindChunk("LoadCaseRecords");
        chunk.RemoveItem(item); chunk.SetInt32(item, value);
    }

    private static int StageChecks(List<string> failures)
    {
        int checks = 0;
        void Check(bool ok, string what) { checks++; if (!ok) { Console.WriteLine("P2CE P2P-01 check " + checks + " FAILED: " + what); failures.Add("P2P-01: " + what); } }
        var g = One("LoadCaseComponent", (0, "G"), (1, false));
        var q = One("LoadCaseComponent", (0, "Q"), (1, false));
        var records = new[] { R2aDesignFixtures.Datum<LegacyCaseData>(g), R2aDesignFixtures.Datum<LegacyCaseData>(q) };
        var inc = One("LoadCombinationComponent", (0, "INC"), (1, new object[] { g, q }), (2, new object[] { 1.35, 0.7 }));
        var odd = One("LoadCombinationComponent", (0, "ODD"), (1, new object[] { g, q }), (2, new object[] { 0.0, -0.5 }));
        var group = One("GroupComponent", (0, "Deck"));
        const string Inc = "INC: G 1.35, Q 0.7", Odd = "ODD: G 0, Q -0.5";
        Check(Factors(R2aDesignFixtures.Datum<LegacyCombinationDraft>(inc)) == Inc && Factors(R2aDesignFixtures.Datum<LegacyCombinationDraft>(odd)) == Odd, "the inputs (control)");
        var setLoadCases = R2aDesignFixtures.Plugin.GetTypes().Single(t => t.Name == "StagedConstructionComponent").GetMethod("SetLoadCases");

        // A stage that never had a catalogue (unknown), saved as the development files of 0.4.0 were: LoadCases = [] and no records chunk.
        var unknown = new Rig(new[] { inc }, group, null);
        byte[] oldFile = Save(unknown.Stage);
        var probe = new GH_IO.Serialization.GH_Archive(); probe.Deserialize_Binary(oldFile);
        var oldChunk = probe.GetRootNode.FindChunk("Stage");
        Check(oldChunk.ItemExists("LoadCases") && oldChunk.FindChunk("LoadCaseRecords") == null && GpcLegacyPayloads.Cases(oldChunk.GetByteArray("LoadCases")).Length == 0,
            "an unknown catalogue writes the empty «LoadCases» item and no records chunk");
        // After Read, before the solve: the factors of the parameter are exact.
        var reopened = new Rig(new[] { inc }, group, Opened(oldFile));
        Check(reopened.Persistent().SequenceEqual(new[] { Inc }), "after Read before the solve: " + string.Join(" / ", reopened.Persistent()));
        // Build Model not run: after the stage solves, the parameter and the stage keep 1.35/0.7 (before P2P-01: cleared).
        var stageFactors = reopened.Solve(out var errors);
        Check(reopened.Persistent().SequenceEqual(new[] { Inc }), "old development file, after the solve, PersistentData: " + string.Join(" / ", reopened.Persistent()));
        Check(stageFactors.SequenceEqual(new[] { Inc }) && errors.Length == 0, "old development file, after the solve, the stage: " + string.Join(" / ", stageFactors) + " " + string.Join(" / ", errors));

        // Save and reopen with the G/Q catalogue (also a factor of 0 and a negative finite factor): everything equal, exactly.
        var known = new Rig(new[] { inc, odd }, group, s => setLoadCases.Invoke(s, new object[] { records }));
        var knownFactors = known.Solve(out errors);
        Check(knownFactors.SequenceEqual(new[] { Inc, Odd }) && known.Persistent().SequenceEqual(new[] { Inc, Odd }) && errors.Length == 0,
            "with the catalogue of Build Model: " + string.Join(" / ", knownFactors) + "; " + string.Join(" / ", known.Persistent()));
        byte[] saved = Save(known.Stage);
        probe = new GH_IO.Serialization.GH_Archive(); probe.Deserialize_Binary(saved);
        var recordsChunk = probe.GetRootNode.FindChunk("Stage").FindChunk("LoadCaseRecords");
        Check(recordsChunk != null && recordsChunk.GetInt32("Version") == 1 && recordsChunk.GetInt32("Count") == 2
            && Records(new[] { GpcDraftArchive.ReadCase(recordsChunk.GetByteArray("Case", 0)), GpcDraftArchive.ReadCase(recordsChunk.GetByteArray("Case", 1)) }) == Records(records),
            "the records chunk: version 1, count 2, the two records in order");
        var again = new Rig(new[] { inc, odd }, group, Opened(saved));
        Check(again.Persistent().SequenceEqual(new[] { Inc, Odd }), "reopened, before the solve: " + string.Join(" / ", again.Persistent()));
        var againFactors = again.Solve(out errors);
        Check(againFactors.SequenceEqual(knownFactors) && again.Persistent().SequenceEqual(new[] { Inc, Odd }) && errors.Length == 0,
            "reopened, after the solve: " + string.Join(" / ", againFactors) + "; " + string.Join(" / ", again.Persistent()));

        // An authoritative empty catalogue (Build Model with no case, or a records chunk of count 0) still removes the orphan factors. The update is
        // called outside a solution: a removal expires the solution of the parameter (ExpireSolution(true)), which a document solved by the test
        // host must not run again from inside its own solution (it hung the host of gate p2ce3); the stage is solved first with an unknown
        // catalogue, then given the empty one, saved, and its records chunk of count 0 read back as a known empty catalogue.
        var empty = new Rig(new[] { inc }, group, null);
        empty.Solve(out _);
        empty.Param.GetType().GetMethod("UpdateLoadCases").Invoke(empty.Param, new object[] { Array.Empty<LegacyCaseData>() });
        setLoadCases.Invoke(empty.Stage, new object[] { Array.Empty<LegacyCaseData>() });
        Check(empty.Persistent().SequenceEqual(new[] { "INC: " }), "an empty catalogue removes the orphan factors: " + string.Join(" / ", empty.Persistent()));
        byte[] savedEmpty = Save(empty.Stage);
        var emptyArchive = new GH_IO.Serialization.GH_Archive(); emptyArchive.Deserialize_Binary(savedEmpty);
        var read = R2aDesignFixtures.Plugin.GetTypes().Single(t => t.Name == "StagedConstructionComponent").GetMethod("ReadCaseRecords", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Invoke(null, new object[] { emptyArchive.GetRootNode.FindChunk("Stage") });
        var emptyRecords = (IReadOnlyList<LegacyCaseData>)read.GetType().GetField("Item1").GetValue(read);
        Check(emptyRecords != null && emptyRecords.Count == 0 && read.GetType().GetField("Item2").GetValue(read) == null, "a records chunk of count 0 is read as a known empty catalogue");
        // The unknown catalogue does not call the update at all: the parameter refuses null before any write.
        var unknownAgain = new Rig(new[] { inc }, group, null);
        unknownAgain.Param.GetType().GetMethod("UpdateLoadCases").Invoke(unknownAgain.Param, new object[] { null });
        Check(unknownAgain.Persistent().SequenceEqual(new[] { Inc }), "a null catalogue changes nothing in the parameter: " + string.Join(" / ", unknownAgain.Persistent()));

        // A damaged chunk (another version; truncated): the error, and nothing changes.
        foreach (var (label, damage) in new (string, Action<GH_IO.Serialization.GH_IWriter>)[]
        {
            ("version 2", w => Replace(w, "Version", 2)),
            ("truncated", w => Replace(w, "Count", 3)),
        })
        {
            var damaged = new Rig(new[] { inc }, group, Opened(saved, damage));
            var damagedFactors = damaged.Solve(out errors);
            Check(errors.Any(e => e.Contains("saved load case records")) && damaged.Persistent().SequenceEqual(new[] { Inc }) && damagedFactors.SequenceEqual(new[] { Inc }),
                label + ": " + string.Join(" / ", errors) + "; " + string.Join(" / ", damaged.Persistent()) + "; " + string.Join(" / ", damagedFactors));
        }
        Console.WriteLine($"P2CE P2P-01: {checks} checks; the factors kept with an unknown catalogue ({Inc}), saved and reopened with the catalogue ({Inc}; {Odd}), removed by an empty one, a damaged chunk reported.");
        return checks;
    }
}
