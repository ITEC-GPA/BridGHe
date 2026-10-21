using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

/// <summary>Portable replay of archived Straus results: no new global solve or fabricated FEM forces.</summary>
internal static class BridgeDetailsModelExample
{
    private static readonly string Folder = ExamplePaths.Case("47-pioli-irrigidimenti-verifiche");
    private static readonly string FileName = Path.Combine(Folder, "47-integrazione-modello.ghx");
    private static GH_Component Find(GH_Document d, string type) => d.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == type);
    private static void RunButton(GH_Component c) { c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true); c.ExpireSolution(false); }
    private static void Solve(GH_Document d)
    {
        GH_Document.EnableSolutions = true; d.Enabled = true; d.NewSolution(false, GH_SolutionMode.Silent);
        var errors = d.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => c.Name + ": " + e)).ToArray();
        if (errors.Length > 0) throw new Exception(string.Join("; ", errors));
    }
    private static void Calculate(GH_Document d)
    { RunButton(Find(d, "BuildBridgeModel")); Solve(d); RunButton(Find(d, "SuperElementCheckComponent")); Solve(d); }
    private static int Validate(GH_Document d)
    {
        var source = Find(d, "SuperElementCheckComponent").Params.Output[0].VolatileData;
        var details = Find(d, "BridgeDetailCheckComponent").Params.Output[0].VolatileData;
        BridgeWorkflowTests.Require(source.DataCount > 2 && source.DataCount == details.DataCount, "Lost station/combination results between super-element and detail checker.");
        BridgeWorkflowTests.Require(source.Paths.SequenceEqual(details.Paths), "Station tree paths changed in detail checking.");
        var summary = Find(d, "BridgeCheckSummaryComponent");
        BridgeWorkflowTests.Require(summary.Params.Input[1].Optional && summary.Params.Input[1].VolatileDataCount == 0
            && summary.Params.Output[0].VolatileDataCount > source.DataCount && summary.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count == 0,
            "Summary must generate station labels with only Results connected.");
        var a = source.AllData(true).Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
        var b = details.AllData(true).Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
        var combinedModel = (ModelModel)Find(d, "BuildBridgeModel").Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        var beam = combinedModel.Beams.Single();
        var stations = beam.Results.OfType<BeamForceResultModel>().Select(r => r.NormalizedLength ? r.Station * beam.Length : r.Station).Distinct().OrderBy(x => x).ToArray();
        var expectedSources = stations.SelectMany(s => combinedModel.LoadCombinations.Select(combo => new BridgeResultSource(beam.BeamId, s, combo.Name, "G2"))).ToArray();
        BridgeWorkflowTests.Require(a.Select(r => r.Source).SequenceEqual(expectedSources), "Super-element result provenance does not match the beam, local stations and combinations.");
        var expectedLabels = b.SelectMany(r => BridgeGpc.Checks(r.Stage).Select(_ => r.Source.Label));
        BridgeWorkflowTests.Require(summary.Params.Output[0].VolatileData.AllData(true).Select(g => (string)g.ScriptVariable()).SequenceEqual(expectedLabels), "Automatic summary labels lost the source identity.");
        for (int i = 0; i < a.Length; i++)
        {
            BridgeWorkflowTests.Require(a[i].Source == b[i].Source, "Detail checking lost result provenance.");
            BridgeWorkflowTests.Require(a[i].Analysis.Input.Options.LimitState == BridgeLimitState.Rare && b[i].Analysis.Input.Options.LimitState == BridgeLimitState.Ultimate, "Explicit limit state override not applied.");
            BridgeWorkflowTests.Require(a[i].Analysis.Input.Phases.SequenceEqual(b[i].Analysis.Input.Phases), "Detail checking changed already combined FEM actions or strain.");
            var s = b[i].Stage.Studs;
            BridgeWorkflowTests.Require(s.Enabled && s.ResistancePerStud > 0 && Math.Abs(s.ForcePerStud - Math.Abs(s.Flow) * .1) < 1e-8, "Incorrect connected stud force.");
            BridgeWorkflowTests.Require(BridgeGpc.Checks(b[i].Stage).Any(c => c.Name.StartsWith("Intermedio")) && BridgeGpc.Checks(b[i].Stage).Any(c => c.Name.StartsWith("Appoggio")), "Missing stiffener checks.");
        }
        return a.Length;
    }
    internal static void Replay()
    {
        BridgeMergeTests.Initialize(); using var d = GrasshopperBridgeExample.Read(FileName); Calculate(d);
        Console.WriteLine($"MODEL DETAIL EXAMPLE PASS: {Validate(d)} station/combination results; original phase actions and tree paths retained; no solver invoked.");
    }
    internal static void Generate()
    {
        var plugin = BridgeMergeTests.Initialize();
        string root = ExamplePaths.Calculations("19-flusso-acciaio-misto");
        string[] names = { "G1_GETTO", "G2_PERMANENTI", "Q_TRAFFICO", "RITIRO_FEM" }, keys = { "G1", "G2", "Q", "RITIRO" };
        var paths = names.Select(n => Path.Combine(root, "REV003-" + n + "-results.bgmodel")).ToArray();
        var hashes = paths.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
        var models = paths.Select(p => ModelArchive.Read<ModelModel>(File.ReadAllBytes(p))).Select(m => {
            // Beam 36 is the central girder segment immediately before the internal support, x=27.5..30 m.
            var part = new ModelModel(m.Units) { Name = m.Name + " / estratto beam 36" };
            part.AddElements(new[] { m.Beams.Single(b => b.BeamId == "36") });
            part.LoadCases.AddRange(part.Beams.SelectMany(b => b.Results).Select(r => r.LoadCase).OfType<LoadCaseModel>().DistinctBy(c => c.Name)); return part;
        }).ToArray();
        using var original = GrasshopperBridgeExample.Read(Path.Combine(ExamplePaths.Case("19-flusso-acciaio-misto"), "19-flusso-acciaio-misto_REV003.ghx"));
        Solve(original);
        var property = original.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BeamPropertyCompositeComponent")
            .SelectMany(c => c.Params.Output[0].VolatileData.AllData(true)).Select(g => (CompositeBeamPropertyModel)g.ScriptVariable()).Single(p => p.SteelSection.D == 2000);
        using var d = new GH_Document();
        GH_Component Add(string type, string nick, float x, float y)
        { var c = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == type)); c.NickName = nick; c.CreateAttributes(); c.Attributes.Pivot = new PointF(x, y); d.AddObject(c, false); return c; }
        void Wire(GH_Component a, int o, GH_Component b, int i) => b.Params.Input[i].AddSource(a.Params.Output[o]);
        void Set(GH_Component c, int i, object v)
        {
            if (c.Params.Input[i] is Param_Integer n) { n.PersistentData.Clear(); n.SetPersistentData(Convert.ToInt32(v)); }
            else if (c.Params.Input[i] is Param_Number q) { q.PersistentData.Clear(); q.SetPersistentData(Convert.ToDouble(v)); }
            else if (c.Params.Input[i] is Param_String s) { s.PersistentData.Clear(); s.SetPersistentData((string)v); }
            else throw new ArgumentException("Unexpected input type.");
        }
        IGH_Goo Goo(string type, object value)
        { var g = (IGH_Goo)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == type)); g.GetType().GetProperty("Value").SetValue(g, value); return g; }
        Param_GenericObject Data(string title, IGH_Goo goo, float x, float y)
        { var p = new Param_GenericObject { NickName = title }; p.CreateAttributes(); p.Attributes.Pivot = new PointF(x, y); p.PersistentData.Append(goo); d.AddObject(p, false); return p; }
        GH_Panel Panel(string title, string text, float x, float y, int w, int h)
        { var p = new GH_Panel { NickName = title, UserText = text }; p.CreateAttributes(); p.Attributes.Pivot = new PointF(x, y); p.Attributes.Bounds = new RectangleF(x, y, w, h); d.AddObject(p, false); return p; }
        Panel("47 | DAL MODELLO AI DETTAGLI", "Risultati Straus già calcolati dell'esempio 19 REV003; estratto G2, beam 36, x=27.5..30 m.\n1. Run su Build BridGHe Model. 2. Run su Super Element Check. I dettagli si aggiornano a valle. Nessuna licenza richiesta per questa riproduzione.\nLa tabella DEMO usa fattori unitari. Ultimate nel Detail Check serve a mostrare i controlli; NON rende questi carichi una combinazione normativa SLU.\nPer il tuo modello sostituisci Models/Phases/tabella e proprietà mista. R=900 kN è assegnata per l'esempio, non estratta automaticamente dai risultati nodali.", 0, -480, 2450, 190);
        var build = Add("BuildBridgeModel", "1 Run — Cumulative", 1300, 350);
        for (int i = 0; i < 4; i++)
        {
            var data = Data(names[i] + " — risultati nativi archiviati", Goo("GH_Model", models[i]), 0, i * 260);
            if (i == 0) build.Params.Input[0].AddSource(data);
            var action = Add(i == 3 ? "BridgeShrinkageActionComponent" : "BridgeActionComponent", keys[i], 450, i * 260);
            Set(action, 0, keys[i]); action.Params.Input[1].AddSource(data);
            ((Param_GenericObject)action.Params.Input[2]).PersistentData.Append(new GH_String(models[i].LoadCases.Single().Name));
            Set(action, 3, i == 3 ? -200e-6 : i);
            var phase = Add("BridgeConstructionPhaseComponent", "Fase " + (i + 1), 850, i * 260);
            Set(phase, 0, 10 * (i + 1)); Set(phase, 1, keys[i]); Set(phase, 2, i == 0 ? 0 : 1); Wire(action, 0, phase, 3); Wire(phase, 0, build, 1);
        }
        var table = new Param_GenericObject { NickName = "DEMO — fattori 1, nessuna selezione normativa" }; table.CreateAttributes(); table.Attributes.Pivot = new PointF(950, 1150); d.AddObject(table, false);
        foreach (var s in new[] { "", "G1", "G2", "Q", "RITIRO" }) table.PersistentData.Append(new GH_String(s), new GH_Path(0));
        foreach (var s in new[] { "DEMO", "1", "1", "1", "1" }) table.PersistentData.Append(new GH_String(s), new GH_Path(1)); build.Params.Input[2].AddSource(table);
        var filter = Add("SuperElementFilterComponent", "Seleziona G2", 1740, 120); Set(filter, 1, "G2"); Wire(build, 0, filter, 0);
        var composite = Add("CompositeBeamComponent", "Proprietà fisica mista", 2150, 120); Wire(filter, 0, composite, 0);
        composite.Params.Input[1].AddSource(Data("Spina H2000 + soletta 3000x250; n0=7 nInf=18 nR=12", Goo("GH_CompositeBeamProperty", property), 1740, 480));
        var check = Add("SuperElementCheckComponent", "2 Run — verifica superelemento", 2540, 350); Wire(build, 0, check, 0); Wire(composite, 0, check, 1);
        var detail = Add("BridgeDetailCheckComponent", "3 Dettagli sulle stesse azioni", 3000, 350); Wire(check, 0, detail, 0);
        var studs = Add("BridgeStudsComponent", "Pioli 2x22 passo200", 2530, 900); Wire(studs, 0, detail, 1);
        var intermediate = Add("BridgeIntermediateStiffenerComponent", "Intermedi 200x20 pannelli2m", 2530, 1360); Set(intermediate, 6, 2000); Wire(intermediate, 0, detail, 2);
        var support = Add("BridgeSupportStiffenerComponent", "Appoggio R900kN assegnato", 2990, 1360); Set(support, 2, 200); Set(support, 7, 2000); Set(support, 8, 2000); Set(support, 9, 900); Set(support, 19, 10); Wire(support, 0, detail, 3);
        var options = Add("BridgeAnalysisOptionsComponent", "Ultimate SOLO dimostrazione", 3420, 1360); Set(options, 1, 0); Wire(options, 0, detail, 4);
        var extract = Add("BridgeStudResultsComponent", "Pioli per stazione e combo", 3450, 100); Wire(detail, 0, extract, 0);
        var sum = Add("BridgeCheckSummaryComponent", "Tutti gli esiti locali", 3450, 650); Wire(detail, 0, sum, 0);
        foreach (var (o, title, y) in new[] { (0, "Flusso N/mm", 0), (1, "Forza per piolo kN", 250), (4, "Rapporto piolo", 500) })
            Panel(title, "", 3870, y, 370, 200).AddSource(extract.Params.Output[o]);
        Panel("Controlli", "", 4330, 0, 490, 720).AddSource(detail.Params.Output[1]);
        Panel("Rapporti — stesso ordine", "", 4850, 0, 360, 720).AddSource(detail.Params.Output[5]);
        Panel("PERCORSO E LIMITI", "Tension Check contiene BridgeStressResult, non soltanto tensioni. Conservare l'albero {indice beam;indice stazione}; gli item sono le combinazioni.\nDetails di Layout è una lista di definizioni geometriche: non collegarla indiscriminatamente a Results. Suddividere le zone e associare il dettaglio alla sua stazione.\nCalculation=1 LinearHistory NON è supportato da Detail Check. L'opzione Ultimate non genera coefficienti né altera le azioni FEM.\nPer appoggi reali usare la reazione compressiva corretta, con unità e associazione di combinazione coerenti. R=900 in questo file è dimostrativa.", 3870, 1000, 1300, 220);
        Calculate(d);
        Panel("Riepilogo — controllo governante per stazione", "", 4330, 800, 880, 140).AddSource(sum.Params.Output[9]); Solve(d);
        int count = Validate(d); Directory.CreateDirectory(Folder); GrasshopperBridgeExample.Write(d, FileName);
        using var reopened = GrasshopperBridgeExample.Read(FileName); Calculate(reopened); BridgeWorkflowTests.Require(Validate(reopened) == count, "Replay changed station count.");
        BridgeWorkflowTests.Require(paths.Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).SequenceEqual(hashes), "Source native result archives were modified.");
        File.WriteAllText(Path.Combine(Folder, "validation-modello.json"), JsonSerializer.Serialize(new {
            solved = true, reopened = true, solverInvoked = false, source = "Example 19 REV003 archived Straus results", beam = "36", stationCombinationResults = count,
            sourceHashes = paths.Select((p, i) => new { file = Path.GetFileName(p), sha256 = hashes[i] }),
            warnings = d.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Select(e => c.Name + ": " + e))
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"MODEL DETAIL EXAMPLE PASS: {count} native station/combination samples, save/reopen, actions and paths preserved, source hashes unchanged; no new Straus analysis.");
    }
}
