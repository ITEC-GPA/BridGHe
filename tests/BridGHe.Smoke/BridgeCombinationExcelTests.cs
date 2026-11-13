using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

internal static class BridgeCombinationExcelTests
{
    internal static void ReadExternal(string path)
    {
        var (references, phases) = RecipeGpc();
        var result = BridgeCombinationWorkbook.Read(path, BridgeCombinationCatalog.FromGpc(references.Select(r => R2bFixtures.Input(r.Input)).ToArray(), phases));
        Assert(result.Table.SelectMany(r => r).SequenceEqual(Demo.SelectMany(r => r)), "native Excel round trip changed the combination table");
        Console.WriteLine("PASS NATIVE EXCEL ROUND TRIP");
    }
    private static BridgePhaseDefinition[] Phases(ModelModel steel, ModelModel longTerm, ModelModel traffic, ModelModel shrinkage,
        GPC.Model.Models.Model gSteel, GPC.Model.Models.Model gLongTerm, GPC.Model.Models.Model gTraffic, GPC.Model.Models.Model gShrinkage) => new[] {
            new BridgePhaseDefinition { Order = 10, Name = "Getto su acciaio", State = BridgeSectionState.Steel, Day = 0,
                Actions = new[] { new BridgeActionDefinition { Key = "G1", Source = steel, GpcSource = gSteel, Case = "G", Behavior = BridgeActionBehavior.Steel } } },
            new BridgePhaseDefinition { Order = 20, Name = "Permanenti portati", State = BridgeSectionState.Composite, Day = 28,
                Actions = new[] { new BridgeActionDefinition { Key = "G2", Source = longTerm, GpcSource = gLongTerm, Case = "G", Behavior = BridgeActionBehavior.LongTerm } } },
            new BridgePhaseDefinition { Order = 30, Name = "Esercizio e ritiro", State = BridgeSectionState.Composite, Day = 365,
                Actions = new[] { new BridgeActionDefinition { Key = "Q", Source = traffic, GpcSource = gTraffic, Case = "G", Behavior = BridgeActionBehavior.ShortTerm },
                    new BridgeActionDefinition { Key = "RITIRO", Source = shrinkage, GpcSource = gShrinkage, Case = "G", Behavior = BridgeActionBehavior.Shrinkage, Strain = -200e-6 } } }
        };
    /// <summary>R6.6 R2b: <see cref="Recipe"/> with the four sources built in GPC only (<see cref="GpcWorkflowSources"/>, the same forces): the
    /// GPC input and the analysed copy of every source, and the phases with the actions on their analysed GPC source (no FEM source).</summary>
    internal static ((GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed)[] References, BridgePhaseDefinition[] Phases) RecipeGpc()
    {
        var s = new[] { ("Steel", 100d), ("Composite long term", 30d), ("Composite short term", 50d), ("Shrinkage secondary", 20d) }.Select(p => GpcWorkflowSources.Pair(p.Item1, p.Item2)).ToArray();
        return (s, Phases(null, null, null, null, s[0].Analysed, s[1].Analysed, s[2].Analysed, s[3].Analysed));
    }
    internal static string[][] Demo => new[] { new[] { "Combination", "G1", "G2", "Q", "RITIRO" },
        new[] { "DEMO_01", "1", "1", "1", "0.7" }, new[] { "DEMO_02", "1", "0.8", "0", "1" } };
    private static void Assert(bool condition, string message) => BridgeWorkflowTests.Require(condition, "Excel combinations: " + message);
    private static void Reject(Action action, string contains)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or IOException && e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new Exception("Expected Excel validation: " + contains);
    }
    // Mutate OOXML fixtures to exercise real Excel encodings and malformed user edits.
    private static void Edit(string path, string entry, Action<XDocument> edit)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var old = zip.GetEntry(entry); XDocument xml;
        using (var stream = old.Open()) xml = XDocument.Load(stream);
        edit(xml); old.Delete(); using var output = zip.CreateEntry(entry).Open(); xml.Save(output);
    }
    internal static void Run()
    {
        Console.WriteLine("EXCEL TEST: catalog and file protection");
        BridgePreAnalysisActionTests.Run();
        // R6.6 R2b: the catalogue on the GPC inputs of the references and the analysed GPC sources of the actions (no FEM model).
        var (references, phases) = RecipeGpc();
        var inputs = references.Select(r => R2bFixtures.Input(r.Input)).ToArray();
        var original = R2bFixtures.ResultTokens(references[0].Analysed);
        BridgeCombinationCatalog Catalog(System.Collections.Generic.IEnumerable<BridgePhaseDefinition> p, bool first = false) => BridgeCombinationCatalog.FromGpc(first ? inputs.Take(1).ToArray() : inputs, p);
        var catalog = Catalog(phases.Reverse(), true);
        Assert(catalog.ActionIds.SequenceEqual(new[] { "G1", "G2", "Q", "RITIRO" }), "headers must be Action IDs, not source case G repeated four times");
        var folder = Path.GetFullPath("artifacts/combination-excel-tests/run-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "template.xlsx");
        BridgeCombinationWorkbook.Write(path, catalog);
        Reject(() => BridgeCombinationWorkbook.Read(path, catalog), "combinations");
        var before = File.ReadAllBytes(path);
        Reject(() => BridgeCombinationWorkbook.Write(path, catalog), "already exists");
        Assert(before.SequenceEqual(File.ReadAllBytes(path)), "overwrite protection changed the workbook");
        Reject(() => BridgeCombinationWorkbook.Write(path, catalog, 0, true), "Rows");
        Reject(() => BridgeCombinationWorkbook.Write(Path.ChangeExtension(path, ".csv"), catalog), ".xlsx");
        var previousTemporaryFiles = Directory.GetFiles(folder, ".bridghe-*.tmp").ToHashSet(StringComparer.OrdinalIgnoreCase);
        using (var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            bool rejected = false;
            try { BridgeCombinationWorkbook.Write(path, catalog, overwrite: true); } catch (IOException) { rejected = true; }
            Assert(rejected, "a locked workbook was replaced");
        }
        Assert(before.SequenceEqual(File.ReadAllBytes(path)), "failed writes must preserve the existing workbook");
        Assert(!Directory.GetFiles(folder, ".bridghe-*.tmp").Except(previousTemporaryFiles, StringComparer.OrdinalIgnoreCase).Any(), "failed writes must remove their own temporary file");
        var filled = Path.Combine(folder, "filled.xlsx");
        BridgeCombinationWorkbook.Write(filled, catalog, table: Demo);
        // Independent fixtures keep one invalid edit from affecting the next case.
        int resetCount = 0;
        void Reset() { path = Path.Combine(folder, "case-" + ++resetCount + ".xlsx"); File.Copy(filled, path); }
        Reset(); var read = BridgeCombinationWorkbook.Read(path, catalog);
        Console.WriteLine("EXCEL TEST: numeric round trip and workflow assembly");
        Assert(read.Table.SelectMany(r => r).SequenceEqual(Demo.SelectMany(r => r)), "numeric round trip");
        {
            // The assembly on Build BridGHe Model on GPC: the combinations of the GPC bridge model from the Excel table equal those of the
            // direct table and the explicit factors of the demo table; the unified shrinkage scales the FEM and strain cases together.
            var gpcDirect = GpcBridgeMerge.Build(references, phases, Demo, "excel direct");
            var gpcExcel = GpcBridgeMerge.Build(references, phases, read.Table, "excel table");
            static System.Collections.Generic.KeyValuePair<GPC.Model.LoadCases.LoadCaseBase, double>[] Factors(BridGHe.Common.Gpc.GpcBridgeBuild b, string name) =>
                b.Bridge.Model.Combinations[name].GetLoadCaseCoefficientsPair();
            Assert(gpcExcel.Bridge.Model.Combinations.Keys.SequenceEqual(new[] { "DEMO_01", "DEMO_02" }), "GPC: combinations of the bridge model");
            foreach (var name in gpcExcel.Bridge.Model.Combinations.Keys)
            {
                var expected = Factors(gpcDirect, name);
                Assert(Factors(gpcExcel, name).All(v => expected.Single(e => e.Key.Name == v.Key.Name).Value == v.Value), "GPC: Excel changed an assembled coefficient");
            }
            double Factor(string combination, string caseName) => Factors(gpcExcel, combination).Where(v => v.Key.Name == caseName).Select(v => v.Value).DefaultIfEmpty(0).Single();
            Assert(Factor("DEMO_01", "G1 [FEM]") == 1 && Factor("DEMO_01", "G2 [FEM]") == 1 && Factor("DEMO_01", "Q [FEM]") == 1 && Factor("DEMO_01", "RITIRO [FEM]") == .7 && Factor("DEMO_01", "RITIRO [strain]") == .7
                && Factor("DEMO_02", "G1 [FEM]") == 1 && Factor("DEMO_02", "G2 [FEM]") == .8 && Factor("DEMO_02", "Q [FEM]") == 0 && Factor("DEMO_02", "RITIRO [FEM]") == 1 && Factor("DEMO_02", "RITIRO [strain]") == 1,
                "GPC: the factors of the demo table");
            Assert(Factors(gpcExcel, "DEMO_01").Where(v => v.Key.Name.StartsWith("RITIRO", StringComparison.Ordinal)).Count(v => v.Value == .7) == 2, "GPC: unified shrinkage must scale FEM and strain together");
        }
        Assert(R2bFixtures.ResultTokens(references[0].Analysed).SequenceEqual(original), "source model changed");
        var regeneratedGuids = Catalog(phases.Select(p => p with { Id = Guid.NewGuid() }));
        Console.WriteLine("EXCEL TEST: source/action metadata validation");
        BridgeCombinationWorkbook.Read(path, regeneratedGuids);
        var changed = Catalog(phases.Select(p => p.Order == 30 ? p with { Actions = p.Actions.Select(a => a.Key == "RITIRO" ? a with { Strain = -250e-6 } : a).ToArray() } : p));
        Reject(() => BridgeCombinationWorkbook.Read(path, changed), "Actions reference changed");
        var unknown = phases.Select(p => p.Order == 10 ? p with { Actions = p.Actions.Select(a => a with { Case = "typo" }).ToArray() } : p);
        Reject(() => Catalog(unknown), "not found");
        // A defined but unanalysed source: its GPC input alone (no analysed model).
        var preAnalysis = new[] { phases[0] with { Actions = new[] { phases[0].Actions[0] with { GpcSource = null, GpcInput = inputs[0] } } } };
        Assert(BridgeCombinationCatalog.FromGpc(inputs.Take(1).ToArray(), preAnalysis).Warnings.Length == 1, "defined but unanalysed cases should allow a template with a warning");
        var primary = phases[2] with { Actions = new[] { phases[2].Actions[1] with { GpcSource = null, Case = null, PrimaryOnly = true } } };
        Assert(Catalog(new[] { primary }).ActionIds.Single() == "RITIRO", "primary-only strain must not require FEM");
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Console.WriteLine("EXCEL TEST: edited workbook validation");
        XElement Cell(XDocument x, string address) => x.Descendants(s + "c").Single(c => (string)c.Attribute("r") == address);
        void Value(string cell, string value) => Edit(path, "xl/worksheets/sheet1.xml", x => { var c = Cell(x, cell); c.SetAttributeValue("t", null); c.RemoveNodes(); c.Add(new XElement(s + "v", value)); });
        Value("B2", "NaN"); Reject(() => BridgeCombinationWorkbook.Read(path, catalog), "finite numeric");
        Reset(); Value("B2", ""); Reject(() => BridgeCombinationWorkbook.Read(path, catalog), "explicit 0");
        Reset(); Value("A2", ""); Reject(() => BridgeCombinationWorkbook.Read(path, catalog), "missing name");
        Reset(); Value("B1", "g1"); Reject(() => BridgeCombinationWorkbook.Read(path, catalog), "headers");
        Reset(); Value("B1", "G2"); Reject(() => BridgeCombinationWorkbook.Read(path, catalog), "headers");
        Reset(); Value("A3", "DEMO_01"); Reject(() => BridgeCombinationWorkbook.Read(path, catalog), "unique combination");
        Reset(); Edit(path, "xl/worksheets/sheet1.xml", x => {
            foreach (var row in x.Descendants(s + "row")) foreach (var c in row.Elements(s + "c")) {
                string a = (string)c.Attribute("r"); if (a[0] == 'B') c.SetAttributeValue("r", "D" + a[1..]); else if (a[0] == 'D') c.SetAttributeValue("r", "B" + a[1..]);
            }
        });
        Assert(BridgeCombinationWorkbook.Read(path, catalog).Table.SelectMany(r => r).SequenceEqual(Demo.SelectMany(r => r)), "column reorder changed action associations");
        Reset(); Edit(path, "xl/worksheets/sheet1.xml", x => Cell(x, "E2").AddFirst(new XElement(s + "f", "0.5+0.2")));
        Assert(BridgeCombinationWorkbook.Read(path, catalog).Warnings.Any(w => w.Contains("saved numeric")), "cached formula values need an explicit diagnostic");
        Value("E2", ""); Edit(path, "xl/worksheets/sheet1.xml", x => Cell(x, "E2").AddFirst(new XElement(s + "f", "0.5+0.2")));
        Reject(() => BridgeCombinationWorkbook.Read(path, catalog), "cached result");
        Reset(); Edit(path, "xl/worksheets/sheet1.xml", x => { Cell(x, "B2").SetAttributeValue("t", "e"); Cell(x, "B2").Element(s + "v").Value = "#REF!"; });
        Reject(() => BridgeCombinationWorkbook.Read(path, catalog), "invalid cell");
        Reset();
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        using (var stream = zip.CreateEntry("xl/sharedStrings.xml").Open())
            new XDocument(new XElement(s + "sst", new XElement(s + "si", new XElement(s + "r", new XElement(s + "t", "DEMO_")), new XElement(s + "r", new XElement(s + "t", "01"))))).Save(stream);
        Edit(path, "xl/worksheets/sheet1.xml", x => { var c = Cell(x, "A2"); c.RemoveNodes(); c.SetAttributeValue("t", "s"); c.Add(new XElement(s + "v", 0)); });
        Assert(BridgeCombinationWorkbook.Read(path, catalog).Table[1][0] == "DEMO_01", "Excel shared/rich strings");
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT"); Assert(BridgeCombinationWorkbook.Read(path, catalog).Table[1][4] == "0.7", "numeric XLSX values must be culture independent"); }
        finally { CultureInfo.CurrentCulture = culture; }
        Reset();
        var literalPhases = new[] { phases[0] with { Actions = new[] { phases[0].Actions[0] with { Key = "=literal & <action>" } } } };
        var literal = Catalog(literalPhases);
        var literalFile = Path.Combine(folder, "literal.xlsx");
        BridgeCombinationWorkbook.Write(literalFile, literal, overwrite: true, table: new[] { new[] { "Combination", literal.ActionIds[0] }, new[] { "=literal name", "-0.5" } });
        Assert(BridgeCombinationWorkbook.Read(literalFile, literal).Table[1][0] == "=literal name", "names starting with = must be literal, not formulas");
        // A single explicit successful overwrite, separate from malformed-read fixtures.
        BridgeCombinationWorkbook.Write(literalFile, literal, overwrite: true, table: new[] { new[] { "Combination", literal.ActionIds[0] }, new[] { "REPLACED", "0" } });
        Assert(BridgeCombinationWorkbook.Read(literalFile, literal).Table[1][0] == "REPLACED", "explicit overwrite failed");
        Console.WriteLine("PASS Excel: exact action names, metadata drift, blank/invalid/duplicate inputs, locked file, no overwrite, numeric/formula/shared strings, column reorder, source immutability and FEM+strain factors");
    }
}
