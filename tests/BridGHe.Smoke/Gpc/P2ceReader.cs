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
        Inventory(failures, out _);
        // P2CE phase 2b (7): the parities with the CLR reader and the FEM types (the «LoadCases» lists, the constant item, the global frame) are
        // retired by name with the FEM types (P2rRetiredP2ce); the by-value reader of «LoadCases» stays covered by the 14 checks of P2P-01.
        // P2CE phase 2b (6): the casts of Beam Section are on GPC (GpcSectionCasts); their parity with the former FEM cast (36 casts, recipe and
        // text, equal at 2519e4a) is retired by name with the FEM cast (P2rRetiredP2ce); the casts with explicit expected values stay.
        int casts = SectionCasts(failures);
        Console.WriteLine($"P2CE READER SECTION CASTS: {casts} casts of a text or a curve to a Beam Section with their expected kind, dimensions and text");
        int stage = StageChecks(failures);
        int results = P2cgChecks.ResultsTexts(failures); // P2CG (13/11/2026, 2B-01)
        Console.WriteLine($"P2CG RESULTS TEXT: {results} checks of the Node, Beam and Plate Results data (empty, analysed, copied) on their GPC element");
        if (failures.Count > 0) throw new Exception("P2CE READER FAIL: " + string.Join(" | ", failures.Take(20)));
        Console.WriteLine($"P2CE READER PASS: inventory above; {casts} section casts; {stage} checks of P2P-01 (Staged Construction and Load Combinations); {results} checks of the Results texts.");
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
        if (root.Is("BeamPropertyModel")) CorpusProperties.Add(bytes);
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




    // -----------------------------------------------------------------------------------------------------------------------------------
    // (2b) Parity of the «Design» item (phase 2b, coordinator decision 1) and of the constant «LoadCases» item (decision 2)

    private static readonly List<byte[]> CorpusProperties = new();

    /// <summary>The GPC datum of a property as the captures compare it: its text without GUIDs (the identities of the two paths differ, as for
    /// every datum read by value since R6.1).</summary>
    private static string Datum(LegacyBeamProperty p) => p == null ? "null" : GpcDraftCapture.WithoutGuids(GpcDraftCapture.Text(p));


    // -----------------------------------------------------------------------------------------------------------------------------------
    // (2c) The casts of the Beam Section type (phase 2b): on GPC (GpcSectionCasts) = the former FEM cast (GH_BeamSection.CastFrom on its FEM
    // section, the recipe by its conversion) while the FEM section exists; retired by name with the FEM types.


    /// <summary>The casts of the Beam Section type (a text, a curve) with explicit expected values: the kind, the dimensions, the mirror and the text
    /// the former FEM section showed, refused texts, a circle, a closed polyline, an arc refused and a line accepted without a section.</summary>
    private static int SectionCasts(List<string> failures)
    {
        var gooType = R2aDesignFixtures.Plugin.GetTypes().Single(t => t.Name == "GH_BeamSection");
        int n = 0;
        void One(string label, object source, bool cast, string kind, string text)
        {
            n++;
            var goo = (IGH_Goo)Activator.CreateInstance(gooType);
            bool done = goo.CastFrom(source);
            var r = gooType.GetProperty("Gpc").GetValue(goo) as LegacySectionRecipe;
            string got = r == null ? "none" : r.Kind.ToString();
            if (done != cast || got != kind || (text != null && goo.ToString() != text))
                failures.Add("section cast " + label + ": " + done + " " + got + " «" + goo.ToString().Replace((char)10, '|') + "»");
        }
        One("CHS", new GH_String("CHS200x10"), true, "HollowCircle", "HollowCircle: 200x10");
        One("RHS", new GH_String("RHS200x100x6x8"), true, "HollowRectangle", "HollowRectangle: 200x100x6x8");
        One("SHS", new GH_String("SHS150x8"), true, "HollowRectangle", "HollowRectangle: 150x150x8x8");
        One("I", new GH_String("I500x200x220x16x18x10"), true, "I", "ISection: 500x10x200x16x220x18x0");
        One("2xL", new GH_String("2xL100x80x8x10x12"), true, "Angle", "DoubleAngle: 100x80x8x10x12" + (char)10 + "Mirror: Left, Gap: 12");
        One("SC", new GH_String("SC 250"), true, "SolidCircle", "SolidCircle: 250");
        One("refused IPE", new GH_String("IPE300"), false, "none", null);
        One("refused RHS of two", new GH_String("RHS200x100"), false, "none", null);
        One("circle", new GH_Curve(new Rhino.Geometry.ArcCurve(new Rhino.Geometry.Circle(150))), true, "SolidCircle", "SolidCircle: 300");
        One("closed polyline", new GH_Curve(new Rhino.Geometry.PolylineCurve(new[] { new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(300, 0, 0),
            new Rhino.Geometry.Point3d(300, 500, 0), new Rhino.Geometry.Point3d(0, 0, 0) })), true, "GenericShapes", "GenericShapes 1 shapes");
        One("arc", new GH_Curve(new Rhino.Geometry.ArcCurve(new Rhino.Geometry.Arc(new Rhino.Geometry.Circle(100), Math.PI / 2))), false, "none", null);
        One("line", new GH_Curve(new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(100, 0, 0))), true, "none", "Null Section");
        return n;
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

    private static string Records(IEnumerable<LegacyCaseData> cases) => string.Join("|", cases.Select(c => Convert.ToHexString(GpcDraftArchive.Write(c))));

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
        // P2CG: the hang was the Grasshopper assert «object expired during a solution» (SetPersistentData); the path inside the solution of the
        // stage is checked by DialogAndDeferred (CE-03).
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
        // P2CG (13/11/2026, review of ChatGPT task 51, CE-02): also a count smaller than the records (before: a partial catalogue, read as known,
        // that removed the factor of Q, or both factors with a count of 0), a count far larger than the records (before: an array of that size made
        // before the records were looked for), and a gap in the indices; the catalogue stays unknown (the form is not given one).
        foreach (var (label, damage) in new (string, Action<GH_IO.Serialization.GH_IWriter>)[]
        {
            ("version 2", w => Replace(w, "Version", 2)),
            ("truncated", w => Replace(w, "Count", 3)),
            ("CE-02 count 1 for 2 records", w => Replace(w, "Count", 1)),
            ("CE-02 count 0 for 2 records", w => Replace(w, "Count", 0)),
            ("CE-02 count int.MaxValue for 2 records", w => Replace(w, "Count", int.MaxValue)),
            ("CE-02 records 0 and 2 for a count of 2", w =>
            {
                var chunk = (GH_IO.Serialization.GH_IWriter)((GH_IO.Serialization.GH_IReader)w).FindChunk("LoadCaseRecords");
                var second = ((GH_IO.Serialization.GH_IReader)chunk).GetByteArray("Case", 1);
                chunk.RemoveItem("Case", 1); chunk.SetByteArray("Case", 2, second);
            }),
        })
        {
            var damaged = new Rig(new[] { inc }, group, Opened(saved, damage));
            var damagedFactors = damaged.Solve(out errors);
            bool ce02 = label.StartsWith("CE-02", StringComparison.Ordinal);
            Check(errors.Any(e => e.Contains("saved load case records")) && damaged.Persistent().SequenceEqual(new[] { Inc }) && damagedFactors.SequenceEqual(new[] { Inc })
                && (!ce02 || !HasCatalog(damaged.Param)),
                label + ": " + string.Join(" / ", errors) + "; " + string.Join(" / ", damaged.Persistent()) + "; " + string.Join(" / ", damagedFactors));
        }
        Console.WriteLine($"P2CE P2P-01: {checks} checks; the factors kept with an unknown catalogue ({Inc}), saved and reopened with the catalogue ({Inc}; {Odd}), removed by an empty one, a damaged chunk reported "
            + "(P2CG CE-02: also a count other than the records).");
        int before = checks;
        DialogAndDeferred(Check, records, inc, odd, group, setLoadCases, Inc, Odd);
        Console.WriteLine($"P2CG P2P-01: {checks - before} checks; the form refused on an unknown catalogue (CE-01), the catalogue given to the form by Build Model after the stage solved, "
            + "the removal expired after the solution with the requests merged (CE-03), the whole document saved and reopened with Build Model locked (CE-04).");
        return checks;
    }

    private static bool HasCatalog(IGH_Param param) => (bool)param.GetType().GetProperty("HasCaseCatalog").GetValue(param);

    /// <summary>P2CG (13/11/2026, review of ChatGPT task 51: CE-01, CE-03, CE-04; Codex task 82 read as a proposal).
    /// <para>CE-01: the form of a standalone Load Combinations parameter with an unknown catalogue is refused and changes nothing (its row writer,
    /// <c>LoadCombinationRows.Make</c>, and the refusal of the double click, <c>Refusal</c>); a known empty catalogue still works; the catalogue reaches
    /// the parameter when the stage gives it, also when Build Model gives it after the stage solved.</para>
    /// <para>CE-03: a removal found inside the solution of the stage expires the parameters after it, with one schedule for the requests of a
    /// solution; the next solution, whatever starts it, runs the callback (in this host there is no Grasshopper editor, so the 1 ms timer never
    /// starts one: these checks do not cover the timer, which needs Rhino; F7). No nested solution, and no further schedule after one more solve.</para>
    /// <para>CE-04: the whole document (the standalone parameter with 1.35 G + 0.7 Q and 0 G − 0.5 Q, a group, the stage, Build Model locked, the
    /// wires) written to a GH_Archive, read into a new document and solved: the factors are the original ones, exactly.</para></summary>
    private static void DialogAndDeferred(Action<bool, string> Check, LegacyCaseData[] records, IGH_Goo inc, IGH_Goo odd, IGH_Goo group, System.Reflection.MethodInfo setLoadCases,
        string Inc, string Odd)
    {
        var plugin = R2aDesignFixtures.Plugin;
        // CE-01: the row writer of the OK of the form and the refusal of the double click.
        var rowType = plugin.GetType("BridGHe.Grasshopper.Forms.LoadCombinationRows", true);
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var make = rowType.GetMethod("Make", flags);
        string Refusal(IReadOnlyList<LegacyCaseData> catalogue) => (string)rowType.GetMethod("Refusal", flags).Invoke(null, new object[] { catalogue });
        (IEnumerable<IGH_Goo> Made, string Error) Made(IReadOnlyList<LegacyCaseData> catalogue, string[] columns, params (string, IReadOnlyList<object>)[] rows)
        {
            var r = make.Invoke(null, new object[] { rows, columns, catalogue });
            return (((System.Collections.IEnumerable)r.GetType().GetField("Item1").GetValue(r))?.Cast<IGH_Goo>(), (string)r.GetType().GetField("Item2").GetValue(r));
        }
        string unknown = Refusal(null);
        Check(unknown != null && unknown.Contains("run Build Model") && Refusal(Array.Empty<LegacyCaseData>()) == null && Refusal(records) == null,
            "CE-01: the form is refused with an unknown catalogue only: «" + unknown + "»");
        var refused = Made(null, Array.Empty<string>(), ("INC", Array.Empty<object>()));
        Check(refused.Made == null && refused.Error == unknown, "CE-01: OK of the form with an unknown catalogue makes no combination: " + refused.Error);
        var accepted = Made(records, new[] { "G", "Q" }, ("INC", new object[] { 1.35, 0.7 }), ("ODD", new object[] { 0.0, -0.5 }));
        Check(accepted.Error == null && accepted.Made.Select(x => Factors(R2aDesignFixtures.Datum<LegacyCombinationDraft>(x))).SequenceEqual(new[] { Inc, Odd }),
            "CE-01: OK of the form with the G/Q catalogue: " + accepted.Error + " " + string.Join(" / ", accepted.Made?.Select(x => Factors(R2aDesignFixtures.Datum<LegacyCombinationDraft>(x))) ?? new string[0]));
        var emptyMade = Made(Array.Empty<LegacyCaseData>(), Array.Empty<string>(), ("INC", Array.Empty<object>()));
        Check(emptyMade.Error == null && emptyMade.Made.Select(x => Factors(R2aDesignFixtures.Datum<LegacyCombinationDraft>(x))).SequenceEqual(new[] { "INC: " }),
            "CE-01: a known empty catalogue still makes the combinations (as before)");

        // CE-01: the catalogue of the parameter: unknown when the stage has none (the old development file), known when the stage gives it, also when
        // Build Model gives it after the stage solved (SetLoadCases from the solution of Build Model): at once, without a change of the factors and
        // without a solution; the next solve of the stage applies it (G/Q: nothing to change).
        var late = new Rig(new[] { inc, odd }, group, null);
        late.Solve(out _);
        Check(!HasCatalog(late.Param) && late.Persistent().SequenceEqual(new[] { Inc, Odd }), "CE-01: a stage without a catalogue gives none to the parameter (the form refused)");
        int starts = 0;
        late.Doc.SolutionStart += (s, e) => starts++;
        setLoadCases.Invoke(late.Stage, new object[] { records });
        Check(HasCatalog(late.Param) && late.Persistent().SequenceEqual(new[] { Inc, Odd }) && starts == 0 && late.Doc.ScheduleDelay < 0,
            "CE-01: Build Model gives the catalogue after the stage solved: the form has it at once, the factors unchanged, no solution started or asked for");
        late.Stage.ExpireSolution(false); // the next solve of the stage (an edit upstream of it; no solution is asked for by the catalogue)
        var lateFactors = late.Solve(out var errors);
        Check(lateFactors.SequenceEqual(new[] { Inc, Odd }) && late.Persistent().SequenceEqual(new[] { Inc, Odd }) && errors.Length == 0 && starts == 1,
            "CE-01: the next solve of the stage with the catalogue keeps 1.35/0.7 and 0/-0.5: " + string.Join(" / ", lateFactors));

        // The update of the parameter reports a change (G/Q unchanged: none; G/Q -> G/W: the G factors exact, W with 1; again: none).
        var update = late.Param.GetType().GetMethod("UpdateLoadCases");
        Check(!(bool)update.Invoke(late.Param, new object[] { records }), "CE-03: the same catalogue changes no combination");
        var w = BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("W", false);
        var gw = new[] { records[0], w };
        var deferred = plugin.GetType("BridGHe.Grasshopper.Helpers.DeferredExpiry", true);
        int Schedules() => (int)deferred.GetProperty("Schedules", flags).GetValue(null);
        int Waiting(GH_Document d) => (int)deferred.GetMethod("Waiting", flags).Invoke(null, new object[] { d });
        int s0 = Schedules();
        Check((bool)update.Invoke(late.Param, new object[] { gw }) && late.Persistent().SequenceEqual(new[] { "INC: G 1.35, W 1", "ODD: G 0, W 1" }) && starts == 1
            && Waiting(late.Doc) == 1 && Schedules() == s0 + 1,
            "CE-03: G/Q -> G/W: the G factors exact, W added with 1, the persistent data changed, the parameter expired later (one request, no solution now): "
            + string.Join(" / ", late.Persistent()));
        Check(!(bool)update.Invoke(late.Param, new object[] { gw }) && Waiting(late.Doc) == 1 && Schedules() == s0 + 1, "CE-03: the same catalogue again: no change, no new request");
        late.Doc.Dispose();

        // CE-03: the formerly unsafe path: a known empty catalogue applied inside the solution of the stage removes the factors of two standalone
        // parameters; no nested solution (depth 1), one schedule for both; the next solution runs the callback: both parameters solved again on their
        // changed data, and nothing more is asked for (a second solve starts nothing new).
        var inside = new Rig(new[] { inc }, group, s => setLoadCases.Invoke(s, new object[] { Array.Empty<LegacyCaseData>() }));
        var second = (IGH_Param)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "LoadCombinationsParam"));
        second.CreateAttributes(); inside.Doc.AddObject(second, false);
        var secondData = second.GetType().GetProperty("PersistentData").GetValue(second);
        secondData.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 1).Invoke(secondData, new object[] { odd.Duplicate() });
        inside.Stage.Params.Input[2].AddSource(second);
        int depth = 0, insideStarts = 0;
        inside.Doc.SolutionStart += (s, e) => { insideStarts++; depth = Math.Max(depth, inside.Doc.SolutionDepth); };
        s0 = Schedules();
        var insideFactors = inside.Solve(out errors);
        string SecondPersistent() => string.Join(" / ", ((IEnumerable<IGH_Goo>)secondData.GetType().GetMethod("AllData").Invoke(secondData, new object[] { true }))
            .Select(g => Factors(R2aDesignFixtures.Datum<LegacyCombinationDraft>(g))));
        Check(inside.Persistent().SequenceEqual(new[] { "INC: " }) && SecondPersistent() == "ODD: " && depth == 1 && insideStarts == 1
            && Schedules() == s0 + 1 && Waiting(inside.Doc) == 2 && HasCatalog(inside.Param) && HasCatalog(second),
            $"CE-03: an empty catalogue inside the solve of the stage: factors removed in both parameters, depth {depth}, {insideStarts} solution(s), "
            + $"{Schedules() - s0} schedule(s) for {Waiting(inside.Doc)} request(s): " + string.Join(" / ", inside.Persistent()) + "; " + SecondPersistent());
        bool notYet = inside.Param.Phase == GH_SolutionPhase.Computed && second.Phase == GH_SolutionPhase.Computed; // not expired from inside the solution
        insideFactors = inside.Solve(out errors);
        Check(notYet && Waiting(inside.Doc) == 0 && insideStarts == 2 && Schedules() == s0 + 1 && insideFactors.SequenceEqual(new[] { "INC: ", "ODD: " }) && errors.Length == 0,
            "CE-03: the next solution expires and solves both parameters (the stage with them): " + string.Join(" / ", insideFactors));
        inside.Solve(out _);
        Check(Waiting(inside.Doc) == 0 && Schedules() == s0 + 1 && inside.Doc.ScheduleDelay < 0, "CE-03: converged: one more solve asks for nothing");
        inside.Doc.Dispose();

        // CE-04: the whole document saved and reopened, Build Model locked (it gives no catalogue), the stage without one and with one.
        foreach (bool known in new[] { false, true })
        {
            string label = "CE-04 (" + (known ? "the stage with the G/Q catalogue" : "no catalogue saved") + ")";
            using var doc = new GH_Document();
            var param = (IGH_Param)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "LoadCombinationsParam"));
            param.CreateAttributes(); doc.AddObject(param, false);
            var data = param.GetType().GetProperty("PersistentData").GetValue(param);
            foreach (var c in new[] { inc, odd }) data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 1).Invoke(data, new object[] { c.Duplicate() });
            var groupComponent = BridgeSegmentExcelTests.Add(doc, "GroupComponent");
            Set(groupComponent.Params.Input[0], "Deck");
            var stage = BridgeSegmentExcelTests.Add(doc, "StagedConstructionComponent");
            Set(stage.Params.Input[0], "S1"); Set(stage.Params.Input[3], false); Set(stage.Params.Input[4], false);
            stage.Params.Input[1].RemoveAllSources(); stage.Params.Input[1].AddSource(groupComponent.Params.Output[0]);
            stage.Params.Input[2].RemoveAllSources(); stage.Params.Input[2].AddSource(param);
            var build = BridgeSegmentExcelTests.Add(doc, "BuildModelComponent");
            build.Params.Input[3].AddSource(param); build.Params.Input[4].AddSource(stage.Params.Output[0]);
            build.Locked = true;
            if (known) setLoadCases.Invoke(stage, new object[] { records });
            BridgeSegmentExcelTests.Solve(doc, true);
            var archive = new GH_IO.Serialization.GH_Archive();
            if (!archive.AppendObject(doc, "Definition")) { Check(false, label + ": the document is not written"); continue; }
            var reread = new GH_IO.Serialization.GH_Archive();
            using var reopened = new GH_Document();
            if (!reread.Deserialize_Binary(archive.Serialize_Binary()) || !reread.ExtractObject(reopened, "Definition")) { Check(false, label + ": the document is not read"); continue; }
            var p = reopened.Objects.OfType<IGH_Param>().Single(o => o.GetType().Name == "LoadCombinationsParam");
            var s = reopened.Objects.OfType<GH_Component>().Single(o => o.GetType().Name == "StagedConstructionComponent");
            var b = reopened.Objects.OfType<GH_Component>().Single(o => o.GetType().Name == "BuildModelComponent");
            var g = reopened.Objects.OfType<GH_Component>().Single(o => o.GetType().Name == "GroupComponent");
            var pData = p.GetType().GetProperty("PersistentData").GetValue(p);
            string[] Read() => ((IEnumerable<IGH_Goo>)pData.GetType().GetMethod("AllData").Invoke(pData, new object[] { true })).Select(x => Factors(R2aDesignFixtures.Datum<LegacyCombinationDraft>(x))).ToArray();
            Check(Read().SequenceEqual(new[] { Inc, Odd }) && !HasCatalog(p) && b.Locked && s.Params.Input[2].Sources.SequenceEqual(new[] { p }) && s.Params.Input[1].Sources.Single() == g.Params.Output[0]
                && b.Params.Input[3].Sources.SequenceEqual(new[] { p }) && b.Params.Input[4].Sources.Single() == s.Params.Output[0],
                label + ": read back, before the solve: the factors of the archive, no catalogue in the parameter, Build Model locked, the wires: " + string.Join(" / ", Read()));
            BridgeSegmentExcelTests.Solve(reopened, true);
            var increments = R2aDesignFixtures.Datum<LegacyStageDraft>(s.Params.Output[0].VolatileData.AllData(true).FirstOrDefault())?.Increments.Select(Factors).ToArray() ?? new[] { "no stage" };
            var catalogue = (IReadOnlyList<LegacyCaseData>)p.GetType().GetProperty("CaseRecordList").GetValue(p);
            Check(Read().SequenceEqual(new[] { Inc, Odd }) && increments.SequenceEqual(new[] { Inc, Odd }) && s.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0 && HasCatalog(p) == known
                && (known ? Records(catalogue) == Records(records) && Refusal(catalogue) == null : Refusal(catalogue) == unknown),
                label + ": after the solve: the parameter and the stage keep 1.35/0.7 and 0/-0.5, the form " + (known ? "has the catalogue" : "refused") + ": "
                + string.Join(" / ", increments) + "; " + string.Join(" / ", Read()));
        }
    }
}
