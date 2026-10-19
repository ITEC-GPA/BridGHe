using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static class BridgeUtilityExamples
{
    private static Assembly plugin;
    private static bool writeExamples;
    private static readonly List<object> reports = new();
    private static GH_Component Add(GH_Document doc, string type, float x, float y)
    {
        var c = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == type));
        c.CreateAttributes(); c.Attributes.Pivot = new PointF(x, y); doc.AddObject(c, false); return c;
    }
    private static IGH_Goo Goo(string name, object value)
    {
        var type = plugin.GetTypes().Single(t => t.Name == name);
        if (name != "GH_Model") return (IGH_Goo)Activator.CreateInstance(type, new[] { value });
        var goo = (IGH_Goo)Activator.CreateInstance(type); type.GetProperty("Value").SetValue(goo, value); return goo;
    }
    private static void Generic(GH_Component c, int port, params IGH_Goo[] values)
    { var p = (Param_GenericObject)c.Params.Input[port]; p.PersistentData.Clear(); foreach (var v in values) p.PersistentData.Append(v); }
    private static void Text(GH_Component c, int port, params string[] values) { var p = (Param_String)c.Params.Input[port]; p.PersistentData.Clear(); p.SetPersistentData(values); }
    private static void Numbers(GH_Component c, int port, params double[] values) { var p = (Param_Number)c.Params.Input[port]; p.PersistentData.Clear(); p.SetPersistentData(values); }
    private static void Wire(GH_Component from, int output, GH_Component to, int input) => to.Params.Input[input].AddSource(from.Params.Output[output]);
    private static void Header(GH_Document doc, string text)
    {
        var panel = new GH_Panel { UserText = text }; panel.CreateAttributes(); panel.Attributes.Pivot = new PointF(0, -200);
        panel.Attributes.Bounds = new RectangleF(0, -200, 1080, 140); doc.AddObject(panel, false);
    }
    private static GH_Panel Panel(GH_Document doc, GH_Component source, int output, float x, float y)
    {
        var p = new GH_Panel(); p.CreateAttributes(); p.Attributes.Pivot = new PointF(x, y); p.Attributes.Bounds = new RectangleF(x, y, 230, 230);
        doc.AddObject(p, false); p.AddSource(source.Params.Output[output]); return p;
    }
    private static void Solve(GH_Document doc)
    {
        foreach (var c in doc.Objects.OfType<GH_Component>())
            if (c.GetType().Name == "BuildBridgeModel") c.GetType().GetField("_run", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c, true);
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        var errors = doc.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => o.Name + ": " + e)).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join("; ", errors));
    }
    private static GH_Component Find(GH_Document doc, string type) => doc.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == type);
    private static object[] Output(GH_Component c, int port) => c.Params.Output[port].VolatileData.AllData(true).Select(g => g.ScriptVariable()).ToArray();
    private static void Save(GH_Document doc, string id, Action<GH_Document> validate)
    {
        Solve(doc); validate(doc);
        var folder = writeExamples ? ExamplePaths.Case(id) : System.IO.Path.GetFullPath(System.IO.Path.Combine("artifacts", "utility-component-tests", id));
        Directory.CreateDirectory(folder); var path = System.IO.Path.Combine(folder, id + ".ghx");
        GrasshopperBridgeExample.Write(doc, path);
        using var copy = GrasshopperBridgeExample.Read(path); Solve(copy); validate(copy);
        if (copy.Objects.Count != doc.Objects.Count) throw new InvalidOperationException("Lost objects on reopen.");
        var types = doc.Objects.OfType<GH_Component>().Select(c => c.GetType().Name).Where(n => n.StartsWith("Bridge")).Distinct().ToArray();
        var report = new { example = id, passed = true, reopened = true, objects = doc.Objects.Count, components = types, solverInvoked = false };
        File.WriteAllText(System.IO.Path.Combine(folder, "validation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); reports.Add(report);
        Console.WriteLine("UTILITY EXAMPLE PASS: " + id);
    }
    internal static void Run(bool generate = false)
    {
        writeExamples = generate; plugin = BridgeMergeTests.Initialize(); reports.Clear();
        AuditPhase(); Envelopes(); Phases(); Checks(); Comparison(); Lanes();
        var reportFolder = writeExamples ? ExamplePaths.Validation : System.IO.Path.GetFullPath("artifacts/utility-component-tests");
        Directory.CreateDirectory(reportFolder);
        File.WriteAllText(System.IO.Path.Combine(reportFolder, "utilities.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("UTILITY EXAMPLES PASS: 6 GHX definitions solved and reopened; 12 new component types exercised; no Straus solver invoked.");
    }
    private static void AuditPhase()
    {
        using var doc = new GH_Document(); Header(doc, "23 | AUDIT E COPIA DI FASE\nDati dimostrativi: due travi parallele, volutamente disconnesse. Il warning DISCONNECTED è atteso.\nPhase Model modifica solo B0 e invalida i risultati; non esegue Straus. Consultare README.md.");
        var model = BridgeUtilityTests.Source(); var audit = Add(doc, "BridgeModelAuditComponent", 100, 60); Generic(audit, 0, Goo("GH_Model", model));
        var phase = Add(doc, "BridgePhaseModelComponent", 480, 60); Wire(audit, 5, phase, 0); Text(phase, 1, "Risposta omogeneizzata da analizzare"); Text(phase, 2, "B0");
        Generic(phase, 3, Goo("GH_BeamProperty", BridgeGrillageExample.LongProperty(new(), 1)));
        Panel(doc, audit, 4, 150, 370); var audit2 = Add(doc, "BridgeModelAuditComponent", 850, 60); Wire(phase, 0, audit2, 0); Panel(doc, audit2, 4, 850, 370);
        Save(doc, "23-audit-modello-fase", d =>
        {
            var m = (ModelModel)Output(Find(d, "BridgePhaseModelComponent"), 0).Single();
            BridgeUtilityTests.Assert(m.Elements.All(e => e.Results.Count == 0) && m.Beams.Count == 2, "Phase GH output retained old results.");
        });
    }
    private static void Envelopes()
    {
        using var doc = new GH_Document(); Header(doc, "24 | INVILUPPI CONCOMITANTI\nCampioni numerici controllati, non una soluzione FEM: G e Q con passo mobile 2. Unità N mm.\nM3 minimo Q conserva N=-50, V2=13, V3=-17, T=19, M2=-23 dello stesso campione. Reazione Fz=-120; spostamento Uz=-10.");
        var model = Goo("GH_Model", BridgeUtilityTests.Source());
        string[] types = { "BridgeResultCasesComponent", "BridgeBeamEnvelopeComponent", "BridgeBearingEnvelopeComponent", "BridgeDisplacementEnvelopeComponent" };
        for (int i = 0; i < types.Length; i++) { var c = Add(doc, types[i], 80 + i * 340, 80); Generic(c, 0, model); Panel(doc, c, i == 0 ? 0 : 2, 50 + i * 340, 460); }
        Save(doc, "24-inviluppi-concomitanti", d =>
        {
            BridgeUtilityTests.Assert(Output(Find(d, types[1]), 2).Cast<double>().All(v => v < 0), "GH beam signed envelope failed.");
            BridgeUtilityTests.Assert((double)Output(Find(d, types[2]), 2).Single() == -120 && (double)Output(Find(d, types[3]), 2).Single() == -10, "GH nodal envelope failed.");
            BridgeUtilityTests.Assert(Output(Find(d, types[0]), 0).Cast<string>().SequenceEqual(new[] { "G", "Q" }), "Result catalogue failed.");
        });
    }
    private static void Phases()
    {
        using var doc = new GH_Document(); Header(doc, "25 | FASI E COMBINAZIONI PER ACTION ID\nDue fasi dimostrative: G1 acciaio e CS ritiro primario. I dati FEM G sono campioni controllati.\nAction Combinations riceve colonne CS,G1 e produce il formato Build. SLS: 0.7,1. Primary Only esclude effetti di vincolo del ritiro.");
        var model = BridgeWorkflowTests.Source("Steel samples", 100);
        var a = Add(doc, "BridgeActionComponent", 80, 60); Text(a, 0, "G1"); Generic(a, 1, Goo("GH_Model", model)); Generic(a, 2, new GH_String("G"));
        var r = Add(doc, "BridgeShrinkageActionComponent", 80, 390); Text(r, 0, "CS"); Numbers(r, 3, -200e-6);
        ((Param_Boolean)r.Params.Input[5]).PersistentData.Clear(); ((Param_Boolean)r.Params.Input[5]).SetPersistentData(true);
        var p1 = Add(doc, "BridgeConstructionPhaseComponent", 400, 60); ((Param_Integer)p1.Params.Input[0]).SetPersistentData(10); Text(p1, 1, "Acciaio");
        ((Param_Integer)p1.Params.Input[2]).PersistentData.Clear(); ((Param_Integer)p1.Params.Input[2]).SetPersistentData(0); Wire(a, 0, p1, 3);
        var p2 = Add(doc, "BridgeConstructionPhaseComponent", 400, 390); ((Param_Integer)p2.Params.Input[0]).SetPersistentData(20); Text(p2, 1, "Ritiro"); Wire(r, 0, p2, 3);
        var order = Add(doc, "BridgePhaseTableComponent", 750, 60); Wire(p2, 0, order, 0); Wire(p1, 0, order, 0);
        var combo = Add(doc, "BridgeActionCombinationsComponent", 1090, 60); Wire(order, 0, combo, 0); Text(combo, 1, "CS", "G1"); Text(combo, 2, "SLS");
        var factors = (Param_Number)combo.Params.Input[3]; factors.PersistentData.AppendRange(new[] { new GH_Number(.7), new GH_Number(1) }, new GH_Path(0));
        var build = Add(doc, "BuildBridgeModel", 1420, 60); Generic(build, 0, Goo("GH_Model", model)); Wire(order, 0, build, 1); Wire(combo, 0, build, 2);
        Panel(doc, combo, 0, 1050, 440); Panel(doc, build, 1, 1420, 440);
        Save(doc, "25-fasi-combinazioni", d =>
        {
            var m = (ModelModel)Output(Find(d, "BuildBridgeModel"), 0).Single();
            BridgeUtilityTests.Assert(m.BridgeWorkflow.Phases.Select(p => p.Order).SequenceEqual(new[] { 10, 20 }) && m.LoadCombinations.Count == 1, "GH combination workflow failed.");
        });
    }
    private static void Checks()
    {
        using var doc = new GH_Document(); Header(doc, "26 | RIEPILOGO DELLE VERIFICHE GPC\nSezione H mista di esempio; quattro incrementi assegnati in kN/kNm, senza analisi FEM globale.\nIl riepilogo conserva stato e note GPC. Un rapporto assente è NaN, non zero. Il massimo non è una verifica complessiva del ponte.");
        var geometry = Add(doc, "BridgeGeometryComponent", 50, 0); var studs = Add(doc, "BridgeStudsComponent", 50, 500);
        var checker = Add(doc, "BridgeSectionCheckComponent", 510, 100); Wire(geometry, 0, checker, 0); Wire(studs, 0, checker, 7);
        Generic(checker, 1, Goo("GH_Material", new MaterialModel("C30") { Name = "C30", SpecificCompressiveStrength = 30 }));
        Generic(checker, 2, Goo("GH_Material", new MaterialModel("S355") { Name = "S355", Modulus = 210000, MinimumYieldStress = 355, MinimumTensileStress = 510 }));
        var phases = new[] { new BridgePhase { Name = "Steel", Kind = BridgePhaseKind.SteelOnly, MomentKNm = 800, ShearKN = 300 },
            new BridgePhase { Name = "Long", MomentKNm = 200, ShearKN = 100, Phi = 2 }, new BridgePhase { Name = "Instant", MomentKNm = 400, ShearKN = -50 },
            new BridgePhase { Name = "Shrinkage", Kind = BridgePhaseKind.Shrinkage, ShrinkageMicrostrain = -200, Phi = 2 } };
        Generic(checker, 4, phases.Select(p => Goo("BridgeDetailGoo", p)).ToArray());
        var summary = Add(doc, "BridgeCheckSummaryComponent", 950, 100); Wire(checker, 0, summary, 0); Text(summary, 1, "G2/x=15000/SLS", "G2/x=15000/SLS", "G2/x=15000/SLS", "G2/x=15000/SLS");
        Panel(doc, summary, 9, 1330, 70); Panel(doc, summary, 4, 1330, 400);
        Save(doc, "26-riepilogo-verifiche-gpc", d =>
        {
            var results = Output(Find(d, "BridgeSectionCheckComponent"), 0).Cast<BridgeStressResult>().ToArray();
            var expected = results.SelectMany(r => BridgeGpc.Checks(r.Stage)).Select(c => c.Ratio ?? double.NaN).ToArray();
            var actual = Output(Find(d, "BridgeCheckSummaryComponent"), 3).Cast<double>().ToArray();
            BridgeUtilityTests.Assert(expected.Length > 0 && actual.SequenceEqual(expected), "GPC summary changed or dropped ratios.");
        });
    }
    private static void Comparison()
    {
        using var doc = new GH_Document(); Header(doc, "27 | RITIRO INCREMENTALE E CONFRONTO NUMERICO\nRitiro cumulativo assegnato: -100, -200, -250 microstrain ai giorni 7,28,100. Inserimento in forma adimensionale.\nConfronto dimostrativo su due campioni con ordine invertito: unità e punti fisici uguali. Non costituisce uno studio di convergenza FEM.");
        var inc = Add(doc, "BridgeShrinkageIncrementsComponent", 100, 80); Numbers(inc, 0, 7, 28, 100); Numbers(inc, 1, -100e-6, -200e-6, -250e-6); Panel(doc, inc, 0, 100, 350);
        var compare = Add(doc, "BridgeResultComparisonComponent", 560, 80); Text(compare, 0, "G2/x=15m/Uz", "G2/x=30m/Uz"); Numbers(compare, 1, -10, -20);
        Text(compare, 2, "G2/x=30m/Uz", "G2/x=15m/Uz"); Numbers(compare, 3, -20.1, -10.02); Panel(doc, compare, 2, 650, 400);
        Save(doc, "27-ritiro-confronti", d =>
        {
            BridgeUtilityTests.Assert(Math.Abs(Output(Find(d, "BridgeShrinkageIncrementsComponent"), 0).Cast<double>().Sum() + 250e-6) < 1e-16, "GH shrinkage increments failed.");
            BridgeUtilityTests.Assert((bool)Output(Find(d, "BridgeResultComparisonComponent"), 0).Single(), "GH keyed comparison failed.");
        });
    }
    private static void Lanes()
    {
        using var doc = new GH_Document(); Header(doc, "28 | TRE PERCORSI MOBILI PARALLELI\nPrototipo rettilineo lungo 20 m. Offsets -3000,0,+3000 mm, tre casi distinti. Veicolo dimostrativo da 100 kN.\nCollegare Paths ad Attach Moving Paths del modello, poi Moving Static Analysis. Il file prepara i percorsi, non avvia Straus.");
        var lanes = Add(doc, "BridgeMovingLanesComponent", 150, 80); Generic(lanes, 0, Goo("MovingGoo", BridgeUtilityTests.Path())); Numbers(lanes, 1, -3000, 0, 3000); Text(lanes, 2, "L1", "L2", "L3"); Panel(doc, lanes, 0, 600, 80);
        Save(doc, "28-corsie-mobili", d =>
        {
            var data = Find(d, "BridgeMovingLanesComponent").Params.Output[0].VolatileData.AllData(true).ToArray();
            BridgeUtilityTests.Assert(data.Length == 3 && data.All(g => ((IGH_PreviewData)g).ClippingBox.IsValid), "Moving-lane archive or preview failed.");
        });
    }
}
