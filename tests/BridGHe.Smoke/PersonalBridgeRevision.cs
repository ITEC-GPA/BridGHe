using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Reports;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>Extend the user's saved graph, retaining original object identities and controls.</summary>
internal sealed partial class PersonalBridgeRevision
{
    private readonly GH_Document doc;
    private readonly Assembly plugin;
    private readonly string root;
    private string folder, revision;
    private GH_Component creep, supportSelect, supportCheck, intermediateCheck, report;
    private GH_Component[] phases, analyses;
    private readonly List<GH_Component> addedPlots = new();
    private const string BuildId = "8394121a-76b9-41a6-988a-a4599c7dbf73";
    private const string CheckId = "a8d8af21-d392-4f24-af39-fe1ff1e631fe";
    private const string CompositeId = "dce4b0f7-6d1e-4f80-a956-5cc695dd73cf";
    private const string DetailsId = "c2e20391-89e6-4f46-ab4c-9f06e9dd1040";
    private const string PropertyId = "4b953bca-d42c-44d0-bcac-e51f2e21c1fd";
    private const string ExcelId = "cd8f3cb4-0d78-494f-992d-367f7816d8e4";
    internal PersonalBridgeRevision(GH_Document doc, Assembly plugin, string root) { this.doc = doc; this.plugin = plugin; this.root = root; }
    private T Obj<T>(string id) where T : class => doc.Objects.Single(o => o.InstanceGuid == Guid.Parse(id)) as T ?? throw new Exception("Wrong object type: " + id);
    private GH_Component C(string id) => Obj<GH_Component>(id);
    private IGH_Param P(string id) => Obj<IGH_Param>(id);
    private GH_Component Add(string type, float x, float y, string nick = null)
    {
        var c = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == type));
        c.CreateAttributes(); c.Attributes.Pivot = new PointF(x, y); if (nick != null) c.NickName = nick; doc.AddObject(c, false); return c;
    }
    private GH_Panel Note(string name, string text, float x, float y, int width = 900, int height = 150)
    {
        var p = new GH_Panel { NickName = name, UserText = text }; p.CreateAttributes(); p.Properties.Multiline = false;
        p.Attributes.Pivot = new PointF(x, y); p.Attributes.Bounds = new RectangleF(x, y, width, height); doc.AddObject(p, false); return p;
    }
    private static void Wire(GH_Component c, int port, params IGH_Param[] sources) { c.Params.Input[port].RemoveAllSources(); foreach (var s in sources) c.Params.Input[port].AddSource(s); c.Params.Input[port].ExpireSolution(false); c.ExpireSolution(false); }
    private static void Number(GH_Component c, int port, params double[] values)
    { Wire(c, port); var p = (Param_Number)c.Params.Input[port]; p.PersistentData.Clear(); foreach (double v in values) p.PersistentData.Append(new GH_Number(v)); }
    private static void Text(GH_Component c, int port, params string[] values)
    {
        Wire(c, port);
        if (c.Params.Input[port] is Param_String p) { p.PersistentData.Clear(); foreach (string v in values) p.PersistentData.Append(new GH_String(v)); }
        else { var g = (Param_GenericObject)c.Params.Input[port]; g.PersistentData.Clear(); foreach (string v in values) g.PersistentData.Append(new GH_String(v)); }
    }
    private static void Integer(GH_Component c, int port, int value) { Wire(c, port); var p = (Param_Integer)c.Params.Input[port]; p.PersistentData.Clear(); p.SetPersistentData(value); }
    private static void Boolean(GH_Component c, int port, bool value) { Wire(c, port); var p = (Param_Boolean)c.Params.Input[port]; p.Invert = false; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Boolean(value)); p.ExpireSolution(false); c.ExpireSolution(false); }
    private static void Run(GH_Component c) { c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true); c.ExpireSolution(false); }
    private void Solve(bool strict = false)
    {
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        if (!strict) return;
        var errors = doc.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => c.Name + ": " + e)).ToArray();
        if (errors.Length > 0) throw new Exception(string.Join("\n", errors));
    }
    private object Value(GH_Component c, int output = 0) => c.Params.Output[output].VolatileData.AllData(true).Single().ScriptVariable();
    private GH_Component Multiply(IGH_Param a, IGH_Param b, float x, float y)
    {
        var server = Grasshopper.Instances.ComponentServer;
        var candidates = server.ObjectProxies.Where(p => p.Desc.Name == "Multiplication").Select(p => server.EmitObject(p.Guid)).OfType<GH_Component>().ToArray();
        var c = candidates.First(c => c.GetType().Name == "Component_VariableMultiplication");
        c.CreateAttributes(); c.Attributes.Pivot = new PointF(x, y); doc.AddObject(c, false); Wire(c, 0, a); Wire(c, 1, b); return c;
    }
    private Param_Point Points(string name, IEnumerable<Point3d> points, float x, float y)
    {
        var p = new Param_Point { NickName = name, Hidden = true }; p.CreateAttributes(); p.Attributes.Pivot = new PointF(x, y);
        foreach (var point in points) p.PersistentData.Append(new GH_Point(point)); doc.AddObject(p, false); return p;
    }
    private void ReplaceConsumers(IGH_Param old, IGH_Param replacement)
    {
        foreach (var p in doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input).Where(p => p.Sources.Contains(old)).ToArray())
        { var sources = p.Sources.Select(s => s == old ? replacement : s).ToArray(); p.RemoveAllSources(); foreach (var s in sources) p.AddSource(s); }
    }

    internal int Build(string mode)
    {
        revision = mode.ToUpperInvariant(); folder = Path.Combine(root, revision); Directory.CreateDirectory(folder); Directory.CreateDirectory(Path.Combine(folder, "calcoli"));
        string original = Path.Combine(root, "flusso-acciaio-misto_REV001.ghx"); var originalHash = SHA256.HashData(File.ReadAllBytes(original));
        doc.FilePath = Path.Combine(folder, "flusso-acciaio-misto_" + revision + ".ghx");
        RefreshCompositePropertyPortOrder();
        foreach (var toggle in doc.Objects.OfType<GH_BooleanToggle>()) toggle.Value = false;
        foreach (var plot in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name.Contains("PlotComponent")))
            foreach (var p in plot.Params.Input.OfType<Param_String>().Where(p => p.Name is "File" or "Output Path" or "File Path")) { p.RemoveAllSources(); p.PersistentData.Clear(); p.ExpireSolution(false); }
        ConfigureSectionAndShrinkage(); ConfigurePhases();
        if (mode is "rev003" or "rev004") ConfigureTwoGirders();
        if (mode == "rev004") ConfigureDeckGrillage();
        analyses = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "StrausR3AnalysisComponent").ToArray();
        Wire(C(BuildId),0,analyses.Select(a=>a.Params.Output[0]).ToArray());
        foreach(var excel in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeCombinationExcelComponent")) Wire(excel,0,analyses.Select(a=>a.Params.Output[0]).ToArray());
        foreach(var c in doc.Objects.OfType<GH_Component>()) foreach(var p in c.Params.Input.Where(p=>p.Access==GH_ParamAccess.list && (p.Name is "Models" or "Phases" or "Actions"))) p.DataMapping=GH_DataMapping.Flatten;
        foreach (var a in analyses)
        {
            string key = a.InstanceGuid == Guid.Parse("2c52577a-da63-4e52-a6c9-57c0f9a8a0b7") ? "G1" :
                a.InstanceGuid == Guid.Parse("8187f478-4f1f-4b1d-91ef-51b19851099e") ? "G2" :
                a.InstanceGuid == Guid.Parse("0f593523-bfa4-4c07-b68d-fda28f451c7b") ? "Q" : "RITIRO";
            Text(a, 3, Path.Combine(folder, "calcoli", key + ".st7")); Boolean(a, 4, false);
            if (key == "Q") Text(a, 2, "Q_TRAFFICO", "WIND_1");
        }
        Solve();
        foreach (var a in analyses) { Console.WriteLine("EXAMPLE 99 " + revision + ": Straus " + a.Params.Input[3].VolatileData.AllData(true).First()); Run(a); Solve();
            if (a.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0) throw new Exception(string.Join(";", a.RuntimeMessages(GH_RuntimeMessageLevel.Error))); }
        ValidateReactions(); WriteCombinations();
        if (revision == "REV004") ValidateDeckPhaseModels();
        Run(C(ExcelId)); Solve(); Run(C(BuildId)); Solve(); Run(C(CheckId)); Solve();
        if(secondCheck!=null) { Run(secondCheck); Solve(); }
        foreach (var c in new[] { C(ExcelId), C(BuildId), C(CheckId) })
            if(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0) throw new Exception(c.Name+": "+string.Join(';',c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        AddDetailsAndReport(); if(secondCheck!=null) AddSecondGirderDetails();
        foreach (var plot in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name.Contains("PlotComponent")))
        { int live=plot.Params.Input.FindIndex(p=>p.Name=="Keep Running"); if(live>=0) Boolean(plot,live,true); }
        SetPlotTitles();
        Solve(true);
        Boolean(report, 10, true); Run(report); Solve(true); Boolean(report, 10, false);
        var results = doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="SuperElementCheckComponent").SelectMany(c=>c.Params.Output[0].VolatileData.AllData(true)).Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
        if (results.Length == 0 || supportCheck.Params.Output[0].VolatileDataCount == 0 || intermediateCheck.Params.Output[0].VolatileDataCount == 0)
            throw new Exception("Missing final or local verification results.");
        if (!File.Exists(Path.Combine(folder, "Report_" + revision + ".docx"))) throw new Exception("Missing report.");
        var model = (ModelModel)Value(C(BuildId));
        File.WriteAllBytes(Path.Combine(folder, "calcoli", "combined.bgmodel"), ModelArchive.Write(model));
        foreach (var a in analyses) { var m = (ModelModel)Value(a); File.WriteAllBytes(Path.Combine(folder, "calcoli", m.Name + "-results.bgmodel"), ModelArchive.Write(m)); }
        if (revision == "REV004") PersonalBridgeExplicitInputs.Apply(doc);
        GrasshopperBridgeExample.Write(doc, doc.FilePath);
        var validation = new { revision, originalSha256 = Convert.ToHexString(originalHash), model.Name, beams = model.Beams.Count, nodes = model.Nodes.Count,
            sectionResults = results.Length, supportResultsForFirstGirder = supportCheck.Params.Output[0].VolatileDataCount,
            intermediateResultsForFirstGirder = intermediateCheck.Params.Output[0].VolatileDataCount,
            shrinkage = Value(creep, 3), allAnalysesExecuted = true,
            warnings = doc.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Select(w => c.Name + ": " + w)).Distinct().ToArray() };
        File.WriteAllText(Path.Combine(folder, "validation.json"), JsonSerializer.Serialize(validation, new JsonSerializerOptions { WriteIndented = true }));
        if (!originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(original)))) throw new Exception("Original definition was changed.");
        Console.WriteLine($"EXAMPLE 99 {revision} PASS: {results.Length} section results, {model.Beams.Count} beams; native analyses, checks and Word report."); return 0;
    }

    private void ConfigureSectionAndShrinkage()
    {
        var slab = C("d6a953c7-f8aa-4a49-9196-0d234fad8ad0");
        var height = P("3d534111-49fb-4797-8f1a-b033d8cba0eb"); var width = P("5784144e-44fd-4116-ba2b-4d7300ebdce9");
        Wire(slab, 0, height); Wire(slab, 1, width);
        var steel = C("1777690f-b3ea-4641-83df-186914c57f9f");
        Wire(steel, 0, P("ce2e4616-7c81-414d-86e5-9b348f79a164")); Wire(steel, 1, P("b0cd125d-8e2d-4f62-b010-03e12a589a57"));
        Wire(steel, 3, P("85ee4070-bc94-4653-bed1-8231d6745ba5")); Wire(steel, 4, P("96145bf0-1878-40ad-b3f7-0760a2b58be4"));
        Text(C(PropertyId), 0, "H1800 600x30 800x40 anima15 + soletta 2500x300");
        creep = Add("CreepAndShrinkageConcreteComponent", 1650, 900, "Ritiro da RH geometria ed età");
        Number(creep, 0, 32); Number(creep, 3, 70); Number(creep, 5, 28); Number(creep, 6, 365); Number(creep, 8, 7); Number(creep, 9, 7);
        var area = Multiply(width, height, 1100, 760); Wire(creep, 2, area.Params.Output[0]);
        var two = Note("Due facce esposte", "2", 650, 880, 180, 70); var perimeter = Multiply(width, two, 1100, 900); Wire(creep, 4, perimeter.Params.Output[0]);
        ReplaceConsumers(P("43162c3a-c71d-44dd-86ca-80fbb517c5f2"), creep.Params.Output[0]);
        ReplaceConsumers(P("3bc55761-2999-42a0-8102-1ce2c2d13482"), creep.Params.Output[1]);
        ReplaceConsumers(P("7126ed9a-0f54-4270-abe6-863d3a0a7675"), creep.Params.Output[1]);
        ReplaceConsumers(P("3acd322a-6764-4cd9-afe3-6fa5800e7549"), creep.Params.Output[3]);
        Note("Ipotesi temporali", "C32/40; RH70%; cemento N; 365 giorni. Fine cura e attivazione del misto a 7 giorni.\nAc e perimetro da larghezza/spessore; due facce esposte, bordi trascurati.\nLa stessa epsilon incrementale alimenta FEM e autotensioni. n ritiro = n a lungo termine in questo esempio.", 1800, 820, 1000, 160);
        var legacy = C("893ac784-5170-474a-9765-5f7ecfaae7fd"); doc.RemoveObject(legacy, false);
    }

    private void ConfigurePhases()
    {
        var g1 = C("dfb91ca2-1309-4e76-a6f9-7fac12cc0097"); var g2 = C("94e10a9a-d58a-4629-a9a3-cad822ca5d8c"); var q = C("ab58e104-471d-466e-b3cb-cc7569106113");
        Wire(g1, 1, C("2c52577a-da63-4e52-a6c9-57c0f9a8a0b7").Params.Output[0]);
        Wire(g2, 1, C("8187f478-4f1f-4b1d-91ef-51b19851099e").Params.Output[0]);
        Wire(q, 1, C("0f593523-bfa4-4c07-b68d-fda28f451c7b").Params.Output[0]);
        var shrink = C("3307b7eb-cdd6-4f3d-b04c-ca3dcfc6d7d5"); Text(shrink, 0, "RITIRO");
        Wire(shrink, 1, C("2596c64c-16e2-4176-ae57-929880f64ce2").Params.Output[0]); Text(shrink, 2, "RITIRO_FEM");
        Wire(shrink, 3, creep.Params.Output[3]); Boolean(shrink, 5, false);
        phases = new[] { C("aee86ebf-20ce-4ae6-8fc7-294acf5ffbaa"), C("e8ac2ccc-19ea-457f-a00e-a50896eb8c35"), C("7a004b6b-2ab4-48c1-823e-1c0881d5d615"), C("36f18358-3141-4ba4-b908-dd34a3482148") };
        for (int i = 0; i < phases.Length; i++) Integer(phases[i], 0, (i + 1) * 10);
        Text(phases[2], 1, "Ritiro primario e secondario");
        var wind = Add("BridgeActionComponent", 8150, 2230, "WIND dal modello Q"); Text(wind, 0, "WIND"); Wire(wind, 1, q.Params.Input[1].Sources.ToArray()); Text(wind, 2, "WIND_1"); Integer(wind, 3, 2);
        Wire(phases[3], 3, q.Params.Output[0], wind.Params.Output[0]);
        foreach (var c in doc.Objects.OfType<GH_Component>().ToArray())
        for (int i = 0; i < c.Params.Input.Count; i++)
            if (c.Params.Input[i].Name == "Phases") Wire(c, i, phases.Select(p => p.Params.Output[0]).ToArray());
        doc.RemoveObject(C("6af3d887-d02d-457b-843f-1abb9550cd5b"), false); doc.RemoveObject(C("0fa76105-d7ae-4ab6-a508-0ebb84c6acfe"), false);
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeCombinationExcelComponent"))
        { Text(c, 2, "Combinazioni_" + revision + ".xlsx"); Boolean(c, 5, false); }
        Obj<GH_Panel>("ee602c87-4c69-4f87-89f3-2f84ddb81307").UserText = "Quattro fasi 10/20/30/40. Azioni G1, G2, RITIRO, Q e WIND.\nIl ritiro primario e secondario hanno un unico coefficiente. Nessun prefisso dipende dall'ordine dei modelli.\nExcel in lettura: Run; Build BridGHe Model: Run; Super Element Check: Run; Bridge Report: Generate.";
        Obj<GH_Panel>("00fa8e63-874a-402e-aa95-b93465707856").UserText = revision + " | Estensione del file personale 99. Analisi Straus memorizzate.\nDopo modifiche: Run sulle 4 analisi; Run Excel in lettura; Run Build; Run Super Element Check; Generate Report.\nGeometria, coefficienti e ipotesi sono modificabili. Questa è una prova del flusso, non una combinazione normativa automatica.";
        Obj<GH_Panel>("fce15a6a-f213-4fc2-bbdf-8f98834e547e").UserText = "Modello lineare a fasi incrementali, cls non fessurato.\nCombinazioni DEMO per esercitare il flusso; nessuna conformità normativa implicita.\nVerifiche locali di sezione, pioli e irrigiditori. Restano esclusi fatica, instabilità globale, sisma e progetto trasversale della soletta.";
    }

    private void WriteCombinations()
    {
        var model = (ModelModel)Value(analyses[0]); var definitions = phases.Select(p => (BridgePhaseDefinition)Value(p)).ToArray();
        string[][] table = {
            new[] { "Combination", "G1", "G2", "RITIRO", "Q", "WIND" },
            new[] { "DEMO_GETTO", "1", "0", "0", "0", "0" },
            new[] { "DEMO_PERMANENTI", "1", "1", "0", "0", "0" },
            new[] { "DEMO_RITIRO", "1", "1", "1", "0", "0" },
            new[] { "DEMO_TOTALE", "1", "1", "1", "1", "0" },
            new[] { "DEMO_Q_ISOLATA", "0", "0", "0", "1", "0" },
            new[] { "DEMO_AMPLIFICATA", "1.5", "1.5", "1", "1.5", "0" } };
        BridgeCombinationWorkbook.Write(Path.Combine(folder, "Combinazioni_" + revision + ".xlsx"), new BridgeCombinationCatalog(analyses.Select(a=>(ModelModel)Value(a)).ToArray(), definitions), table.Length - 1, true, table);
        File.WriteAllText(Path.Combine(folder, "combinazioni.json"), JsonSerializer.Serialize(table, new JsonSerializerOptions { WriteIndented = true }));
        Note("Limite verificatore N Mx Vy", "WIND è calcolato in Straus ma il suo coefficiente è ZERO nelle combinazioni di verifica.\nIl verificatore GPC adottato rifiuta correttamente flessione deviata/taglio trasversale/torsione.\nIl vento richiede un percorso di verifica dedicato; non è coperto da questo report.",9000,4400,1300,180);
    }

    private void ValidateReactions()
    {
        var rows=new List<object>();int girders=revision is "REV003" or "REV004"?2:1;
        foreach(var analysis in analyses)
        {
            var model=(ModelModel)Value(analysis);
            var reactions=model.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Where(r=>r.LoadCase.Name==model.Name).ToArray();
            double expected=model.Name switch { "G1_GETTO" or "Q_TRAFFICO"=>240000*girders,"G2_PERMANENTI"=>312000*girders,"RITIRO_FEM"=>0,_=>throw new Exception("Unexpected source") };
            double actual=reactions.Sum(r=>r.Force.Z);
            if(reactions.Length==0||Math.Abs(expected-actual)>Math.Max(.01,Math.Abs(expected)*1e-8))throw new Exception($"Vertical equilibrium {model.Name}: {actual} N instead of {expected} N.");
            rows.Add(new { model.Name, expectedVerticalN=expected, actualVerticalN=actual, residualN=actual-expected });
        }
        File.WriteAllText(Path.Combine(folder,"equilibrium.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("EXAMPLE 99 EQUILIBRIUM PASS: 4 source models, including zero resultant shrinkage.");
    }

    private void AddDetailsAndReport()
    {
        var build = C(BuildId); var check = C(CheckId); var detail = C(DetailsId); var composite = C(CompositeId);
        Wire(detail, 2); Wire(detail, 3); detail.NickName = "Pioli sulle azioni di fase";
        var model = (ModelModel)Value(build);
        var checkedIds = check.Params.Output[0].VolatileData.AllData(true).Select(g => ((BridgeStressResult)g.ScriptVariable()).Source.BeamId).ToHashSet();
        var ends = model.Beams.Where(b => checkedIds.Contains(b.BeamId)).SelectMany(b => new[] { b.PointFrom, b.PointTo }).Distinct().ToArray();
        var supports = ends.Where(p => Math.Abs(p.X) < 1e-6 || Math.Abs(p.X - 12000) < 1e-6 || Math.Abs(p.X - 24000) < 1e-6).ToArray();
        var intermediate = ends.Except(supports).ToArray();
        supportSelect = Add("BridgeDetailStationsComponent", 14300, 2100, "Appoggi e reazioni reali");
        Wire(supportSelect, 0, build.Params.Output[0]); Wire(supportSelect, 1, detail.Params.Output[0]);
        Wire(supportSelect, 2, Points("Appoggi sugli assi FEM", supports, 13800, 2250)); Wire(supportSelect, 3, phases.Select(p => p.Params.Output[0]).ToArray());
        var support = C("bc9ea8ec-36bf-4344-acd9-f50456e1b9cd"); support.NickName = "Appoggio R FEM per combinazione";
        support.Attributes.Pivot = new PointF(14800, 2150); Wire(support, 9, supportSelect.Params.Output[1]); Wire(support, 6, supportSelect.Params.Output[3]);
        supportCheck = Add("BridgeDetailCheckComponent", 15400, 2100, "Verifica agli appoggi"); Wire(supportCheck, 0, supportSelect.Params.Output[0]); Wire(supportCheck, 3, support.Params.Output[0]);
        var stationSelect = Add("BridgeDetailStationsComponent", 14300, 1550, "Sezioni irrigiditori intermedi"); Wire(stationSelect, 0, build.Params.Output[0]); Wire(stationSelect, 1, detail.Params.Output[0]);
        Wire(stationSelect, 2, Points("Intermedi passo 2 m", intermediate, 13800, 1700));
        intermediateCheck = Add("BridgeDetailCheckComponent", 15400, 1550, "Verifica intermedi"); Wire(intermediateCheck, 0, stationSelect.Params.Output[0]);
        Wire(intermediateCheck, 2, C("17d36e61-ca92-4706-b6aa-12efb6b88ded").Params.Output[0]);
        var summary = C("d3cfa47d-ae7a-4248-907c-ce45a479572b"); Wire(summary, 0, detail.Params.Output[0], intermediateCheck.Params.Output[0], supportCheck.Params.Output[0]); Wire(summary, 1);
        Obj<GH_Panel>("fcae3ba6-ee7f-475f-88cd-13b30043ef9f").UserText = "Pioli verificati per sezione/combinazione. Intermedi solo ai punti assegnati; appoggi a x=0/12/24 m.\nLe reazioni sono combinate dalle stesse fonti e dagli stessi coefficienti delle fasi. Sollevamento mantenuto con segno.\nIl modello deve essere ricalcolato prima di aggiornare le verifiche. Opzione Ultimate esplicita; coefficienti DEMO dichiarati nell'Excel.";
        foreach (var (type, result, y, locations) in new[] {
            ("BridgeStudPlotComponent", detail, 2800f, Array.Empty<double>()),
            ("BridgeIntermediateStiffenerPlotComponent", intermediateCheck, 3300f, intermediate.Select(p => p.X / 1000).Distinct().OrderBy(x => x).ToArray()),
            ("BridgeSupportStiffenerPlotComponent", supportCheck, 3800f, new[] { 0d, 12, 24 }) })
        {
            var plot = Add(type, 16000, y); Wire(plot, 0, build.Params.Output[0]); Wire(plot, 1, composite.Params.Output[0]); Wire(plot, 2, result.Params.Output[0]);
            if (type == "BridgeStudPlotComponent") Integer(plot, 3, 5);
            else { Number(plot, 10, locations); var list = new GH_ValueList(); list.CreateAttributes(); list.Attributes.Pivot = new PointF(15600, y + 150); doc.AddObject(list, false); Wire(plot, 3, list); }
            var preview = Add("BridgePlotPreviewComponent", 16700, y); Wire(preview, 0, plot.Params.Output[0]); addedPlots.Add(plot);
        }
        report = Add("BridgeReportComponent", 18000, 2100, "Report completo " + revision);
        Wire(report, 0, detail.Params.Output[0], intermediateCheck.Params.Output[0], supportCheck.Params.Output[0]);
        File.Copy(BridgeReportTests.Template, Path.Combine(folder, "Template_BridGHe.docx"), true);
        Text(report, 1, "Template_BridGHe.docx"); Text(report, 2, "Report_" + revision + ".docx");
        Wire(report, 4, build.Params.Output[0]); Text(report, 5, "Verifiche del flusso ponte misto " + revision);
        Text(report, 6, "Esempio 99 | 12 + 12 m | combinazioni DEMO | RH70% cemento N 7-365 giorni"); Text(report, 7, "BridGHe"); Text(report, 8, revision);
        Wire(report, 9, new[] { C("58c312f7-3e3d-4a53-b764-56245251f7d7").Params.Output[0], C("f7160f75-761f-45f8-87bf-e20a1621711b").Params.Output[0] }.Concat(addedPlots.Select(p => p.Params.Output[0])).ToArray());
        var choices = new GH_ValueList { NickName = "Capitoli" }; choices.CreateAttributes(); choices.Attributes.Pivot = new PointF(17500, 2450); doc.AddObject(choices, false); Wire(report, 3, choices);
        foreach(int i in new[]{0,1,3,4})choices.ListItems[i].Selected=false; choices.ExpireSolution(false); // Compact review: summary WR, phases, plots. Detailed chapters remain selectable; all inputs/results are embedded.
        Note("Sequenza finale", "1 Run Excel in lettura. 2 Run Build. 3 Run Super Element Check. 4 Generate Report.\nPioli, intermedi e appoggi hanno rami distinti per conservare posizioni e reazioni.\nPer i grafici selezionare una verifica o l'inviluppo WR; le anteprime sono sul canvas.", 17500, 1750, 1300, 190);
    }

    internal int Replay(bool updateReport)
    {
        folder=Path.GetDirectoryName(doc.FilePath);revision=Path.GetFileName(folder);
        if(updateReport) RefreshCompositePropertyPortOrder();
        analyses=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="StrausR3AnalysisComponent").ToArray();
        var protectedFiles=Directory.GetFiles(Path.Combine(folder,"calcoli")).Where(p=>Path.GetExtension(p).Equals(".st7",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(p).Equals(".LSA",StringComparison.OrdinalIgnoreCase)).ToDictionary(p=>p,p=>SHA256.HashData(File.ReadAllBytes(p)));
        Solve();Run(C(ExcelId));Solve();Run(C(BuildId));Solve();
        var checks=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="SuperElementCheckComponent").ToArray();
        foreach(var c in checks){Run(c);Solve();} Solve(true);
        ValidateReactions();
        if (revision == "REV004") ValidateDeckPhaseModels();
        report=doc.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BridgeReportComponent");
        if(updateReport)
        {
            SetPlotTitles();Solve(true);
            var choices=report.Params.Input[3].Sources.OfType<GH_ValueList>().Single();foreach(int i in new[]{0,1,3,4})choices.ListItems[i].Selected=false;choices.ExpireSolution(false);
            Boolean(report,10,true);Run(report);Solve(true);Boolean(report,10,false);GrasshopperBridgeExample.Write(doc,doc.FilePath);
        }
        if(protectedFiles.Any(f=>!f.Value.SequenceEqual(SHA256.HashData(File.ReadAllBytes(f.Key)))))throw new Exception("Reopening unexpectedly changed a native solver file.");
        int total=checks.Sum(c=>c.Params.Output[0].VolatileDataCount);
        if(total<300||checks.Any(c=>c.Params.Output[0].VolatileDataCount==0))throw new Exception("Missing reopened results.");
        var plots=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name.EndsWith("PlotComponent")).ToArray();
        if(plots.Any(c=>c.Params.Output[0].VolatileDataCount==0))throw new Exception("Missing plot after reopen.");
        foreach(var plot in plots.Where(c=>c.GetType().Name=="BridgeResultPlotComponent"))
        {
            var check=plot.NickName.EndsWith(" G2",StringComparison.Ordinal)?checks.Single(c=>c.NickName=="Verifiche G2"):C(CheckId);
            if(!plot.Params.Input[2].Sources.SequenceEqual(new[]{check.Params.Output[0]}) || plot.Params.Input[2].VolatileDataCount!=check.Params.Output[0].VolatileDataCount)
                throw new Exception("Stress plot does not cover all section results of its girder: " + plot.NickName);
        }
        using(var zip=ZipFile.OpenRead(Path.Combine(folder,"Report_"+revision+".docx")))
        using(var data=JsonDocument.Parse(zip.GetEntry("bridghe/report.json").Open()))
        {
            var saved=data.RootElement.GetProperty("Data");var metadata=saved.GetProperty("Metadata").Deserialize<BridgeReportMetadata>();
            var tree=report.Params.Input[0].VolatileData;
            var entries=tree.Paths.SelectMany(path=>tree.get_Branch(path).Cast<IGH_Goo>().Select((g,i)=>new BridgeReportEntry((BridgeStressResult)g.ScriptVariable(),path+"["+i+"]")));
            string current=BridgeReportData.Create(entries,metadata,(ModelModel)Value(C(BuildId))).DataHash;
            if(current!=saved.GetProperty("DataHash").GetString())throw new Exception("Reopened results differ from the report numerical archive.");
        }
        File.WriteAllText(Path.Combine(folder,"replay-validation.json"),JsonSerializer.Serialize(new { revision, totalSectionResults=total, checkedGirders=checks.Length, plots=plots.Length, nativeSolverInvoked=false, nativeFilesUnchanged=true, reportNumericalHashMatches=true, reportRegenerated=updateReport},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"EXAMPLE 99 {revision} REPLAY PASS: {total} section results, {plots.Length} plots, native files unchanged.");return 0;
    }

    private void SetPlotTitles()
    {
        foreach(var plot in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name.EndsWith("PlotComponent")))
        {
            string girder=plot.NickName.EndsWith(" G2",StringComparison.Ordinal)?"Trave 2":"Trave 1";
            string title=plot.GetType().Name switch { "BridgeForcePlotComponent"=>"Momento Mx", "BridgeResultPlotComponent"=>"Tensioni acciaio sigma 5", "BridgeStudPlotComponent"=>"Pioli Ed e Rd", "BridgeIntermediateStiffenerPlotComponent"=>"Irrigiditori intermedi WR", "BridgeSupportStiffenerPlotComponent"=>"Irrigiditori agli appoggi WR", _=>plot.Name };
            if(plot.GetType().Name=="BridgeResultPlotComponent" && girder=="Trave 2")
                Wire(plot,2,doc.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="SuperElementCheckComponent"&&c.NickName=="Verifiche G2").Params.Output[0]);
            Text(plot,plot.Params.Input.FindIndex(p=>p.Name=="Graphic Title"),girder+" - "+title);
        }
    }

    private void RefreshCompositePropertyPortOrder()
    {
        // The original saves n ritiro after the booleans. Reuse the actual parameter
        // objects (GUID, persistent value and wires) in the current displayed order.
        foreach(var component in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BeamPropertyCompositeComponent"))
        {
            var fresh=(GH_Component)Activator.CreateInstance(component.GetType());
            var parameters=component.Params.Input.ToDictionary(p=>p.Name,StringComparer.Ordinal);
            for(int i=0;i<fresh.Params.Input.Count;i++) component.Params.Input[i]=parameters[fresh.Params.Input[i].Name];
            component.Params.OnParametersChanged();
            foreach(var parameter in component.Params.Input) parameter.ExpireSolution(false);
            component.ExpireSolution(false);
        }
    }

}
