using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static class BridgeCombinationExcelExample
{
    internal static void Run(bool generate = false)
    {
        try { RunCore(generate); }
        catch (Exception e) { Console.Error.WriteLine("FAIL COMBINATION EXCEL EXAMPLE: " + e); throw; }
    }
    private static void RunCore(bool generate)
    {
        const string id = "46-combinazioni-excel";
        string folder = generate ? ExamplePaths.Case(id) : Path.GetFullPath("artifacts/combination-excel-component");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, id + ".ghx"), excel = Path.Combine(folder, "Combinations.xlsx"), template = Path.Combine(folder, "Template.xlsx");
        var plugin = BridgeMergeTests.Initialize();
        var (models, phases) = BridgeCombinationExcelTests.Recipe();
        var catalog = new BridgeCombinationCatalog(models, phases);
        using var doc = new GH_Document(); doc.FilePath = path;
        GH_Component Add(string name, float x, float y)
        {
            var c = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == name));
            c.CreateAttributes(); c.Attributes.Pivot = new PointF(x, y); doc.AddObject(c, false); return c;
        }
        IGH_Goo Model(ModelModel m)
        {
            var type = plugin.GetTypes().Single(t => t.Name == "GH_Model"); var goo = Activator.CreateInstance(type); type.GetProperty("Value").SetValue(goo, m); return (IGH_Goo)goo;
        }
        void Generic(GH_Component c, int port, params IGH_Goo[] values)
        { var p = (Param_GenericObject)c.Params.Input[port]; p.PersistentData.Clear(); foreach (var value in values) p.PersistentData.Append(value); }
        void Text(GH_Component c, int port, string value) { var p = (Param_String)c.Params.Input[port]; p.PersistentData.Clear(); p.SetPersistentData(value); }
        void Integer(GH_Component c, int port, int value) { var p = (Param_Integer)c.Params.Input[port]; p.PersistentData.Clear(); p.SetPersistentData(value); }
        void Number(GH_Component c, int port, double value) { var p = (Param_Number)c.Params.Input[port]; p.PersistentData.Clear(); p.SetPersistentData(value); }
        void Wire(GH_Component a, int output, GH_Component b, int input) => b.Params.Input[input].AddSource(a.Params.Output[output]);
        void Panel(string message, float x, float y, int width = 330, int height = 140)
        {
            var p = new GH_Panel { UserText = message }; p.CreateAttributes(); p.Attributes.Pivot = new PointF(x, y); p.Attributes.Bounds = new RectangleF(x, y, width, height); doc.AddObject(p, false);
        }
        Panel("46 | EXCEL DELLE COMBINAZIONI\nQuattro Action ID, tre fasi, ritiro FEM + strain. I quattro modelli hanno tutti un caso sorgente chiamato G: le intestazioni corrette sono G1, G2, Q, RITIRO.\nCampioni FEM sintetici su due aste, nessun solutore. Coefficienti DEMO senza valore normativo.", 30, -240, 1450, 150);
        var a = new GH_Component[4];
        for (int i = 0; i < a.Length; i++)
        {
            a[i] = Add(i == 3 ? "BridgeShrinkageActionComponent" : "BridgeActionComponent", 80, i * 240);
            Text(a[i], 0, catalog.ActionIds[i]); Generic(a[i], 1, Model(models[i])); Generic(a[i], 2, new GH_String("G"));
            if (i == 3) Number(a[i], 3, -200e-6); else Integer(a[i], 3, i);
        }
        var p = new GH_Component[3];
        for (int i = 0; i < p.Length; i++)
        {
            p[i] = Add("BridgeConstructionPhaseComponent", 450, i * 260);
            Integer(p[i], 0, phases[i].Order); Text(p[i], 1, phases[i].Name); Integer(p[i], 2, (int)phases[i].State); Number(p[i], 4, phases[i].Day.Value);
            Wire(a[i], 0, p[i], 3);
        }
        Wire(a[3], 0, p[2], 3);
        var create = Add("BridgeCombinationExcelComponent", 850, 70); create.NickName = "1 Crea template";
        var read = Add("BridgeCombinationExcelComponent", 850, 450); read.NickName = "2 Leggi Excel";
        foreach (var c in new[] { create, read })
        {
            Generic(c, 0, Model(models[0])); foreach (var phase in p.Reverse()) Wire(phase, 0, c, 1);
        }
        Text(create, 2, "Template.xlsx"); Text(read, 2, "Combinations.xlsx"); Integer(read, 3, 1);
        var build = Add("BuildBridgeModel", 1300, 450); Generic(build, 0, Model(models[0])); foreach (var phase in p) Wire(phase, 0, build, 1); Wire(read, 1, build, 2);
        Panel("1. Crea: scegli un file nuovo e premi Run.\n2. Compila Combinations; conserva Actions.\n3. Leggi: scegli il file compilato e premi Run.\n4. Premi Run su Build BridGHe Model.\nTemplate.xlsx è vuoto; Combinations.xlsx contiene due righe dimostrative già compilate.", 820, 850, 520, 180);
        var table = new GH_Panel(); table.CreateAttributes(); table.Attributes.Pivot = new PointF(1300, 40); table.Attributes.Bounds = new RectangleF(1300, 40, 340, 300); doc.AddObject(table, false); table.AddSource(read.Params.Output[1]);
        // Exercise explicit Create without an existing file, then Read and Build through real GH wires.
        string uniqueTemplate = Path.Combine(folder, "create-" + Guid.NewGuid().ToString("N") + ".xlsx"); Text(create, 2, uniqueTemplate);
        void RunButton(GH_Component c) => c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true);
        void Solve(GH_Document d)
        {
            GH_Document.EnableSolutions = true; d.Enabled = true; d.NewSolution(false, GH_SolutionMode.Silent);
            var errors = d.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => o.Name + ": " + e)).ToArray();
            if (errors.Any()) throw new Exception(string.Join("; ", errors));
        }
        // Build has a required table: isolate it during no-run/Create checks.
        build.Locked = true;
        Solve(doc); BridgeWorkflowTests.Require(!File.Exists(uniqueTemplate), "Excel was written without pressing Run.");
        RunButton(create); create.ExpireSolution(false); Solve(doc);
        BridgeWorkflowTests.Require(File.Exists(uniqueTemplate), "GH Create did not create the template.");
        File.Copy(uniqueTemplate, template, true); File.Delete(uniqueTemplate); Text(create, 2, "Template.xlsx");
        BridgeCombinationWorkbook.Write(excel, catalog, overwrite: true, table: BridgeCombinationExcelTests.Demo);
        build.Locked = false; RunButton(read); RunButton(build); read.ExpireSolution(false); build.ExpireSolution(false); Solve(doc);
        void Validate(GH_Document d)
        {
            var b = d.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == "BuildBridgeModel");
            var model = (ModelModel)b.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
            BridgeWorkflowTests.Require(model.LoadCombinations.Count == 2 && model.LoadCombinations[0].Values.Count(v => v.Key.Name.StartsWith("RITIRO", StringComparison.Ordinal) && v.Value == .7) == 2, "GH Excel > Build lost combination factors.");
        }
        Validate(doc);
        GrasshopperBridgeExample.Write(doc, path);
        var excelBytes = File.ReadAllBytes(excel); var templateBytes = File.ReadAllBytes(template);
        using var reopened = GrasshopperBridgeExample.Read(path); reopened.FilePath = path;
        var copyRead = reopened.Objects.OfType<GH_Component>().Single(c => c.NickName == "2 Leggi Excel");
        var copyBuild = reopened.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == "BuildBridgeModel");
        copyBuild.Locked = true; Solve(reopened);
        BridgeWorkflowTests.Require(!copyRead.Params.Output[1].VolatileData.AllData(true).Any() && File.ReadAllBytes(excel).SequenceEqual(excelBytes) && File.ReadAllBytes(template).SequenceEqual(templateBytes), "Reopening a GH file must not read/write automatically.");
        copyBuild.Locked = false; RunButton(copyRead); RunButton(copyBuild); copyRead.ExpireSolution(false); copyBuild.ExpireSolution(false); Solve(reopened); Validate(reopened);
        // A changed/invalid workbook must not leave a previous table available to the downstream Build.
        var bad = (Param_String)copyRead.Params.Input[2]; bad.PersistentData.Clear(); bad.SetPersistentData("missing.xlsx");
        RunButton(copyRead); copyRead.ExpireSolution(false); reopened.NewSolution(false, GH_SolutionMode.Silent);
        BridgeWorkflowTests.Require(copyRead.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0 && !copyRead.Params.Output[1].VolatileData.AllData(true).Any(), "Failed Read retained a stale combination table.");
        File.WriteAllText(Path.Combine(folder, "validation.json"), JsonSerializer.Serialize(new { example = id, passed = true, reopened = true, objects = doc.Objects.Count, combinations = 2, phases = 3, actions = 4, solverInvoked = false, syntheticFemSamples = true, explicitRunVerified = true, staleOutputCleared = true }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("COMBINATION EXCEL EXAMPLE PASS: create/read/build, relative paths, unified shrinkage, explicit Run and GHX reopen.");
    }
}
