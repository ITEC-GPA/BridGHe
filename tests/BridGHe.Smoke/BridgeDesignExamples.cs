using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>Executable GH documents: test the public ports, persistence and live data flow without a solver.</summary>
internal static class BridgeDesignExamples
{
    private static Assembly plugin;
    private static bool generate;
    private static GH_Component Add(GH_Document doc, string type, float x, float y)
    {
        var c = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == type));
        c.CreateAttributes(); c.Attributes.Pivot = new PointF(x, y); doc.AddObject(c, false); return c;
    }
    private static IGH_Goo Goo(string typeName, object value)
    {
        var type = plugin.GetTypes().Single(t => t.Name == typeName);
        if (typeName == "GH_Beam") return (IGH_Goo)Activator.CreateInstance(type, new object[] { value, null });
        if (typeName != "GH_Model") return (IGH_Goo)Activator.CreateInstance(type, new[] { value });
        var goo = (IGH_Goo)Activator.CreateInstance(type); type.GetProperty("Value").SetValue(goo, value); return goo;
    }
    private static GH_Panel Panel(GH_Document doc, string name, string text, float x, float y, int width = 300, int height = 115)
    {
        var p = new GH_Panel { NickName = name, UserText = text }; p.CreateAttributes(); p.Properties.Multiline = false;
        p.Attributes.Pivot = new PointF(x, y); p.Attributes.Bounds = new RectangleF(x, y, width, height);
        doc.AddObject(p, false); return p;
    }
    private static void Input(GH_Document doc, GH_Component c, int port, params object[] values)
    {
        var point = c.Attributes.Pivot;
        var text = string.Join("\n", values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)));
        c.Params.Input[port].AddSource(Panel(doc, c.Params.Input[port].Name, text, point.X - 350, point.Y + port * 130));
    }
    private static void Data(GH_Document doc, GH_Component c, int port, string name, params IGH_Goo[] values)
    {
        var p = new Param_GenericObject { NickName = name }; p.CreateAttributes();
        p.Attributes.Pivot = new PointF(c.Attributes.Pivot.X - 200, c.Attributes.Pivot.Y - 65 - port * 40);
        foreach (var v in values) p.PersistentData.Append(v); doc.AddObject(p, false); c.Params.Input[port].AddSource(p);
    }
    private static void Int(GH_Component c, int port, params int[] values)
    { var p = (Param_Integer)c.Params.Input[port]; p.PersistentData.Clear(); p.SetPersistentData(values); }
    private static void Number(GH_Component c, int port, params double[] values)
    { var p = (Param_Number)c.Params.Input[port]; p.PersistentData.Clear(); p.SetPersistentData(values); }
    private static void Text(GH_Component c, int port, params string[] values)
    { var p = (Param_String)c.Params.Input[port]; p.PersistentData.Clear(); p.SetPersistentData(values); }
    private static void Wire(GH_Component from, int output, GH_Component to, int input) => to.Params.Input[input].AddSource(from.Params.Output[output]);
    private static GH_Component Find(GH_Document d, string type) => d.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == type);
    private static object[] Output(GH_Component c, int port) => c.Params.Output[port].VolatileData.AllData(true).Select(g => g.ScriptVariable()).ToArray();
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE DESIGN GH: " + text); }
    private static void Info(GH_Document doc, GH_Component source, int port, float x, float y)
    { var p = Panel(doc, source.Params.Output[port].Name, "", x, y, 520, 220); p.AddSource(source.Params.Output[port]); }
    private static void Header(GH_Document doc, string title, string text) =>
        Panel(doc, title, title + "\n" + text + "\nUnità N, mm, MPa. Nessuna analisi Straus avviata. Procedura e controlli nel README della cartella.",
            0, -450, 1700, 170);
    private static GH_Component Geometry(GH_Document doc, bool curved = false, float x = 420, float y = 50)
    {
        var c = Add(doc, "SimpleBridgeSolidComponent", x, y);
        var input = SimpleBridgeTests.Input(curved: curved);
        var alignment = new Param_Curve { NickName = curved ? "Tracciato R=250 m" : "Tracciato 100 m" }; alignment.CreateAttributes();
        alignment.Attributes.Pivot = new PointF(x - 180, y - 200); alignment.PersistentData.Append(new GH_Curve(input.Alignment));
        doc.AddObject(alignment, false); c.Params.Input[0].AddSource(alignment);
        Input(doc, c, 1, 0, 30000, 70000, 100000); Input(doc, c, 2, -4000, 0, 4000);
        var joints = Add(doc, "BridgeSegmentsComponent", x, y + 600);
        ((Param_Number)joints.Params.Input[0]).PersistentData.AppendRange(
            new[] { 12000d, 24000, 36000, 48000, 60000, 72000, 84000 }.Select(n => new GH_Number(n)), new Grasshopper.Kernel.Data.GH_Path(0));
        Wire(joints, 0, c, 7); c.Hidden = true; return c;
    }
    private static BeamPropertyModel Steel(string id = "Steel H")
    {
        var p = BridgeAssignmentTests.Property(id);
        p.Material.Kind = MaterialModel.MaterialKind.Steel; p.Material.Name = "S355"; p.Material.MinimumYieldStress = 355;
        p.Section = new SectionModel { SectionType = SectionModel.SectionTypes.I, B1 = 600, B2 = 600, D = 2500, T1 = 30, T2 = 30, T3 = 16 };
        return p;
    }
    private static GH_Component Assignment(GH_Document doc, GH_Component bg, int port, float x = 1100, float y = 50)
    {
        var c = Add(doc, "BridgeMemberAssignmentComponent", x, y); Wire(bg, port, c, 0);
        Data(doc, c, 1, "Proprietà acciaio N-mm — sostituibile", Goo("GH_BeamProperty", Steel())); return c;
    }
    private static GH_Component Deck(GH_Document doc, GH_Component bg, int port, float x, float y)
    {
        var c = Add(doc, "BridgeDeckMeshComponent", x, y); Wire(bg, port, c, 0);
        Data(doc, c, 1, "Shell C, t=250 mm, E=34000 MPa", Goo("GH_PlateProperty", BridgeMeshTests.DeckProperty()));
        Number(c, 2, 5000); Number(c, 3, 3000); Input(doc, c, 4, 30000, 70000); return c;
    }
    private static void Solve(GH_Document d)
    {
        GH_Document.EnableSolutions = true; d.Enabled = true; d.NewSolution(false, GH_SolutionMode.Silent);
        var errors = d.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => c.Name + ": " + e)).ToArray();
        if (errors.Length > 0)
            foreach (var c in d.Objects.OfType<GH_Component>())
                Console.WriteLine(c.Name + " outputs: " + string.Join("; ", c.Params.Output.Select(o => $"{o.Name}={o.VolatileDataCount}/{o.VolatileData.PathCount} paths")));
        Require(errors.Length == 0, string.Join("\n", errors));
    }
    private static void Save(GH_Document doc, string id, Action<GH_Document> validate)
    {
        Solve(doc); validate(doc);
        string folder = generate ? ExamplePaths.Case(id) : Path.GetFullPath(Path.Combine("artifacts", "bridge-design-tests", id));
        Directory.CreateDirectory(folder); string path = Path.Combine(folder, id + ".ghx"); GrasshopperBridgeExample.Write(doc, path);
        using (var reopened = GrasshopperBridgeExample.Read(path))
        {
            Require(reopened.Objects.Count == doc.Objects.Count, "Object count on reopen");
            Solve(reopened); validate(reopened);
        }
        File.WriteAllText(Path.Combine(folder, "validation.json"), JsonSerializer.Serialize(new
        {
            example = id, solved = true, reopened = true, solverInvoked = false, objects = doc.Objects.Count,
            components = doc.Objects.OfType<GH_Component>().Select(c => c.GetType().Name).Distinct().ToArray(),
            warnings = doc.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Select(e => c.Name + ": " + e)).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("BRIDGE DESIGN EXAMPLE PASS: " + id);
    }
    internal static void Run(bool writeExamples = false)
    {
        plugin = BridgeMergeTests.Initialize(); generate = writeExamples;
        Advisor(); Schedule(); Assign(); Bearings(); Connections(); Bracing(); DeckExample(); Construction(); Details(); Deflection(); Fatigue(); CheckMap(); Complete();
        Console.WriteLine("BRIDGE DESIGN EXAMPLES PASS: 13 GHX definitions solved and reopened; 12 new components, integrated bridge and extended audit; no solver invoked.");
    }
    private static void Advisor()
    {
        using var doc = new GH_Document();
        Header(doc, "33 | CONSIGLIATORE CAMPATE", "Tracciato 100 m; fascia vietata x=40..60 m. Modificare griglia, luce obiettivo e penalità delle pile.\nLe alternative sono ottimi geometrici sulla griglia scelta; non sono predimensionamenti resistenti.");
        var c = Add(doc, "BridgeSpanAdvisorComponent", 450, 40);
        ((Param_Curve)c.Params.Input[0]).PersistentData.Append(new GH_Curve(new LineCurve(Point3d.Origin, new Point3d(100000, 0, 0))));
        ((Param_Curve)c.Params.Input[1]).PersistentData.Append(new GH_Curve(new Rectangle3d(Plane.WorldXY, new Interval(40000, 60000), new Interval(-10000, 10000)).ToNurbsCurve()));
        Input(doc, c, 2, 20000); Input(doc, c, 3, 45000); Input(doc, c, 4, 35000); Input(doc, c, 6, 2000); Number(c, 8, .1);
        Info(doc, c, 0, 850, 40); Info(doc, c, 2, 850, 300); Info(doc, c, 4, 850, 560);
        Save(doc, "33-consigliatore-campate", d =>
        {
            var a = Find(d, "BridgeSpanAdvisorComponent");
            Require(a.Params.Output[0].VolatileData.PathCount == 3 && a.ClippingBox.IsValid, "Three ranked layouts and preview");
            Require(Output(a, 0).Cast<double>().All(s => s <= 38000 || s >= 62000), "Clearance to forbidden region");
        });
    }
    private static void Schedule()
    {
        using var doc = new GH_Document(); Header(doc, "34 | SEZIONI PER PROGRESSIVA", "Trave centrale G2: cambio sezione fra 12.345 e 45 m, rastremazione verticale 1→1.2.\nLa regola divide le aste alle sue estremità e conserva l'identità del concio.");
        var bg = Geometry(doc); var rule = Add(doc, "BridgeSectionScheduleComponent", 1050, 500);
        Data(doc, rule, 0, "Proprietà rastremata", Goo("GH_BeamProperty", Steel("Tapered H")));
        Input(doc, rule, 1, 12345); Input(doc, rule, 2, 45000); Int(rule, 4, 2);
        ((Param_Vector)rule.Params.Input[7]).PersistentData.Clear(); ((Param_Vector)rule.Params.Input[7]).SetPersistentData(new Vector3d(1, 1.2, 0));
        var c = Assignment(doc, bg, 14, 1500); Wire(rule, 0, c, 3); Info(doc, c, 3, 1850, 50);
        Save(doc, "34-sezioni-per-progressiva", d =>
        {
            var beams = Output(Find(d, "BridgeMemberAssignmentComponent"), 0).Cast<BeamModel>().ToArray();
            Require(beams.Any(b => b.BeamProperty.Id == "Tapered H") && beams.Any(b => Math.Abs(b.PointFrom.X - 12345) < 1e-6), "Live rule and taper persist");
        });
    }
    private static void Assign()
    {
        using var doc = new GH_Document(); Header(doc, "35 | ASSEGNAZIONE MEMBRI", "Ponte curvo a tre travi, conci indipendenti dalle campate.\nGli ID FEM sono numerici; Physical IDs e gruppi conservano famiglia/trave/concio. Le proprietà sono dimostrative.");
        var bg = Geometry(doc, true); var c = Assignment(doc, bg, 14); Info(doc, c, 1, 1500, 20); Info(doc, c, 3, 1500, 300);
        Save(doc, "35-assegnazione-membri", d => Require(Output(Find(d, "BridgeMemberAssignmentComponent"), 0).Length > 200 &&
            Output(Find(d, "BridgeMemberAssignmentComponent"), 2).Length == 0, "Complete curved assignment"));
    }
    private static void Bearings()
    {
        using var doc = new GH_Document(); Header(doc, "36 | APPOGGI", "Dodici appoggi: tre travi per quattro allineamenti. Centro prima spalla fisso, guide longitudinali al centro delle altre linee.\nGli appoggi esterni sono multidirezionali; le rotazioni sono libere. La preview mostra gli assi effettivi dei vincoli.");
        var bg = Geometry(doc, true); var c = Add(doc, "BridgeBearingsComponent", 1100, 50); Wire(bg, 14, c, 0);
        Int(c, 3, 2, 0, 2, 2, 1, 2, 2, 1, 2, 2, 1, 2); Info(doc, c, 2, 1500, 30);
        Save(doc, "36-appoggi-orientati", d =>
        {
            var a = Find(d, "BridgeBearingsComponent");
            Require(Output(a, 0).Length == 12 && a.ClippingBox.IsValid, "Bearing count and preview");
            Require(Output(a, 0).Cast<NodeModel>().All(n => n.Attributes.OfType<NodeCoordinateAttributeModel>().Count() == 1), "Actual local frames survive archive");
        });
    }
    private static void Connections()
    {
        using var doc = new GH_Document(); Header(doc, "37 | CONNESSIONI", "Tre rappresentazioni distinte: link rigido Straus, asta deformabile con proprietà, offset locale della sezione.\nI link non sono travi con rigidezza artificiosamente grande. Le linee vanno collegate ai vertici FEM previsti.");
        var lines = new[] { new Line(new Point3d(0, 0, -1500), new Point3d(0, 0, -125)), new Line(new Point3d(5000, 0, -1500), new Point3d(5000, 0, -125)) };
        for (int i = 0; i < 3; i++)
        {
            var c = Add(doc, "BridgeConnectionsComponent", 400 + 650 * i, 100); c.NickName = $"Mode {i}"; Int(c, 0, i);
            if (i < 2) ((Param_Line)c.Params.Input[1]).PersistentData.AppendRange(lines.Select(l => new GH_Line(l)));
            if (i == 1) Data(doc, c, 2, "Asta elastica — proprietà esplicita", Goo("GH_BeamProperty", Steel()));
            if (i == 2)
            {
                Data(doc, c, 3, "Trave da spostare", Goo("GH_Beam", new BeamModel(lines[0].From, lines[1].From, Steel()) { BeamId = "1" }));
                ((Param_Vector)c.Params.Input[4]).PersistentData.Clear(); ((Param_Vector)c.Params.Input[4]).SetPersistentData(new Vector3d(0, 100, 0));
            }
            Info(doc, c, 1, 150 + 650 * i, 450);
        }
        Save(doc, "37-connessioni-offset", d =>
        {
            var all = d.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeConnectionsComponent").ToArray();
            Require(all.Sum(c => Output(c, 0).Length) == 7, "Two links/four nodes, two flexible beams, one offset beam");
        });
    }
    private static void Bracing()
    {
        using var doc = new GH_Document(); Header(doc, "38 | CONTROVENTI LONGITUDINALI", "Controventi X fra 0, 12.345 e 30 m, nelle due maglie trasversali. Le aste delle travi vengono divise agli attacchi.\nPattern 0/1/2: X, V, Warren. Cambiare Normal Offset genera i montanti di collegamento.");
        var bg = Geometry(doc); var c = Add(doc, "BridgeLongitudinalBracingComponent", 1100, 50); Wire(bg, 14, c, 0); Input(doc, c, 1, 0, 12345, 30000);
        var assigned = Assignment(doc, c, 0, 1750); Info(doc, c, 2, 1100, 550); Info(doc, assigned, 3, 2000, 50);
        Save(doc, "38-controventi-longitudinali", d =>
        {
            var c = Find(d, "BridgeLongitudinalBracingComponent"); var g = (SimpleBridgeGeometry)Output(c, 0).Single();
            Require(g.Members.Count(m => m.Family == BridgeMemberFamily.LongitudinalBracing) == 8 && Output(c, 1).Length == 8, "Actual X bracing topology");
        });
    }
    private static void DeckExample()
    {
        using var doc = new GH_Document(); Header(doc, "39 | MESH SOLETTA", "Ponte curvo con plate triangolari sulla superficie media e tre zone di getto.\nUsare BG in uscita per assegnare le travi. Connection Lines definisce collegamenti da creare esplicitamente con Bridge Connections.");
        var bg = Geometry(doc, true); var c = Deck(doc, bg, 14, 1100, 50); var assigned = Assignment(doc, c, 0, 1800);
        Info(doc, c, 5, 1450, 500);
        Save(doc, "39-soletta-getti", d =>
        {
            var deck = Find(d, "BridgeDeckMeshComponent"); var plates = Output(deck, 2).Cast<PlateModel>().ToArray();
            Require(plates.Length > 300 && plates.SelectMany(p => p.Groups).Select(g => g.FullName).Distinct().Count() == 3,
                $"Three live pour groups: plates={plates.Length}, groups={string.Join(",", plates.SelectMany(p => p.Groups).Select(g => g.FullName).Distinct())}");
            Require(Output(Find(d, "BridgeMemberAssignmentComponent"), 2).Length == 0, "Deck-split geometry assigned");
        });
    }
    private static GH_Component Phase(GH_Document doc, int number, string name, double day, float x, float y)
    {
        var c = Add(doc, "BridgeConstructionPhaseComponent", x, y); Int(c, 0, number); Text(c, 1, name); Number(c, 4, day); return c;
    }
    private static void Construction()
    {
        using var doc = new GH_Document(); Header(doc, "40 | SEQUENZA COSTRUTTIVA", "Tre snapshot indipendenti: primo concio G2, trave G2 completa, struttura intera.\nFasi 10/20/30 ai giorni 0/7/28. Nessun trasferimento automatico di deformazioni o storia dal modello precedente.");
        var bg = Geometry(doc); var assign = Assignment(doc, bg, 14); var build = Add(doc, "BuildModelComponent", 1550, 50); Wire(assign, 0, build, 0);
        Text(build, 1, "Sequenza geometrica"); Int(build, 2, (int)ModelModel.ModelUnits.Nmm);
        for (int i = 0; i < 3; i++)
        {
            var p = Phase(doc, 10 * (i + 1), new[] { "Primo concio G2", "Trave G2", "Struttura completa" }[i], new[] { 0d, 7, 28 }[i], 1900, i * 400);
            var c = Add(doc, "BridgeConstructionSequenceComponent", 2350, i * 400); Wire(build, 0, c, 0); Wire(p, 0, c, 1);
            if (i == 0) Text(c, 3, "Girder:2:1"); if (i == 1) Text(c, 2, "Girder\\Group2");
            Info(doc, c, 5, 2750, i * 400);
        }
        Save(doc, "40-sequenza-costruttiva", d =>
        {
            var snapshots = d.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeConstructionSequenceComponent")
                .Select(c => (ModelModel)Output(c, 0).Single()).OrderBy(m => m.Beams.Count).ToArray();
            Require(snapshots.Length == 3 && snapshots[0].Beams.Count < snapshots[1].Beams.Count && snapshots[1].Beams.Count < snapshots[2].Beams.Count, "Three independent activation snapshots");
        });
    }
    private static void Details()
    {
        using var doc = new GH_Document(); Header(doc, "41 | PIOLI E IRRIGIDIMENTI", "Trave G2, intervallo [0,10 m): 50 file da due pioli e tre coppie di irrigidimenti.\nGeometria e quantità da classi gPC. Sono dettagli di officina, non elementi FEM o verifiche resistenti.");
        var bg = Geometry(doc); var c = Add(doc, "BridgeDetailLayoutComponent", 1150, 50); Wire(bg, 14, c, 0);
        Data(doc, c, 1, "H gPC 1330/16, ali 450/30 e 600/40", Goo("BridgeDetailGoo", BridgeDetailLayoutTests.Section()));
        Data(doc, c, 6, "gPC pioli Ø22 H180, passo200", Goo("BridgeDetailGoo", BridgeDetailLayoutTests.Studs()));
        Data(doc, c, 7, "gPC irrigidimenti 200x20", Goo("BridgeDetailGoo", BridgeDetailLayoutTests.Stiffener()));
        Int(c, 2, 2); Input(doc, c, 3, 0); Input(doc, c, 4, 10000); Info(doc, c, 6, 1550, 30);
        Save(doc, "41-pioli-irrigidimenti", d =>
        {
            var a = Find(d, "BridgeDetailLayoutComponent"); Require(Output(a, 0).Length == 100 && Output(a, 2).Length == 6, "Expected gPC detail quantities");
        });
    }
    private static void Deflection()
    {
        using var doc = new GH_Document(); Header(doc, "42 | FRECCE E CONTROFRECCIA", "DATI SINTETICI: parabola con freccia relativa 20 mm e cedimenti u=-5-0.0002s.\nA s=15 m: assoluta -28 mm, relativa -20 mm, controfreccia +20 mm. Display Scale cambia solo le curve.");
        var g = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()); var c = Add(doc, "BridgeDeflectionCamberComponent", 450, 50);
        Data(doc, c, 0, "Modello con spostamenti sintetici", Goo("GH_Model", BridgeDeflectionTests.AnalyticModel(g)));
        Data(doc, c, 1, "Geometria dei campioni", Goo("BridgeGeometryGoo", g)); Input(doc, c, 3, "G1"); Input(doc, c, 9, 100);
        Info(doc, c, 2, 900, 20); Info(doc, c, 3, 900, 300);
        Save(doc, "42-frecce-controfreccia", d =>
        {
            var a = Find(d, "BridgeDeflectionCamberComponent"); int mid = Array.IndexOf(Output(a, 0).Cast<double>().ToArray(), 15000d);
            Require(mid >= 0 && Math.Abs((double)Output(a, 3)[mid] - 20) < 1e-9, "Analytical relative camber survives model archive");
        });
    }
    private static void Fatigue()
    {
        using var doc = new GH_Document(); Header(doc, "43 | STORIE PER FATICA", "Storia ordinata dimostrativa 0,10,2,8,0: cicli di ampiezza 10 e 6.\nA sinistra valori diretti in MPa; a destra stessa storia da campioni FEM SINTETICI N. Nessuna curva S-N o danno automatico.");
        var direct = Add(doc, "BridgeFatigueHistoryComponent", 450, 50); Input(doc, direct, 0, 0, 10, 2, 8, 0); Info(doc, direct, 5, 850, 50);
        var fem = Add(doc, "BridgeFatigueHistoryComponent", 1500, 50);
        Data(doc, fem, 1, "Transito sintetico, non Straus", Goo("GH_Model", BridgeFatigueTests.HistoryModel()));
        Text(fem, 2, "1"); Number(fem, 3, 500); Int(fem, 5, 0); Input(doc, fem, 4, "Q0", "Q1", "Q2", "Q3", "Q4"); Info(doc, fem, 1, 1900, 50);
        Save(doc, "43-storie-fatica", d =>
        {
            var cs = d.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeFatigueHistoryComponent").ToArray();
            Require(cs.All(c => Math.Abs((double)Output(c, 5).Single() - Math.Pow(1216, 1d / 3)) < 1e-12), "Direct and extracted histories give hand-counted equivalent range");
        });
    }
    private static void CheckMap()
    {
        using var doc = new GH_Document(); Header(doc, "44 | MAPPA VERIFICHE", "Controlli locali gPC su una sezione di esempio, con azioni assegnate direttamente: nessuna soluzione FEM globale.\nSolo l'asta 1 riceve controlli. Tutte le altre restano grigie: non sono verificate.");
        var bg = Geometry(doc); var assigned = Assignment(doc, bg, 14);
        var build = Add(doc, "BuildModelComponent", 1500, 50); Wire(assigned, 0, build, 0); Int(build, 2, (int)ModelModel.ModelUnits.Nmm);
        var section = Add(doc, "BridgeGeometryComponent", 400, 1100); var studs = Add(doc, "BridgeStudsComponent", 400, 1500);
        var check = Add(doc, "BridgeSectionCheckComponent", 1100, 1100); Wire(section, 0, check, 0); Wire(studs, 0, check, 7);
        Data(doc, check, 1, "C30", Goo("GH_Material", new MaterialModel("C30") { Name = "C30", Kind = MaterialModel.MaterialKind.Concrete, SpecificCompressiveStrength = 30 }));
        Data(doc, check, 2, "S355", Goo("GH_Material", new MaterialModel("S355") { Name = "S355", Kind = MaterialModel.MaterialKind.Steel, Modulus = 210000, MinimumYieldStress = 355, MinimumTensileStress = 510 }));
        Data(doc, check, 4, "Incrementi locali dimostrativi",
            Goo("BridgeDetailGoo", new BridgePhase { Name = "Acciaio", Kind = BridgePhaseKind.SteelOnly, MomentKNm = 800, ShearKN = 300 }),
            Goo("BridgeDetailGoo", new BridgePhase { Name = "Misto", MomentKNm = 200, ShearKN = 100, Phi = 2 }));
        var map = Add(doc, "BridgeCheckMapComponent", 2100, 50); Wire(build, 0, map, 0); Wire(check, 0, map, 1);
        build.Hidden = true; assigned.Hidden = true;
        Text(map, 2, "1", "1"); Number(map, 3, 0, 0); Text(map, 4, "Demo-G1", "Demo-G2");
        Info(doc, map, 5, 2500, 50); Info(doc, map, 6, 2500, 400);
        Save(doc, "44-mappa-verifiche", d =>
        {
            var a = Find(d, "BridgeCheckMapComponent");
            Require(Output(a, 0).Length == Output(Find(d, "BridgeMemberAssignmentComponent"), 0).Length && a.ClippingBox.IsValid,
                $"Map geometry and preview: axes={Output(a, 0).Length}, checkResults={Output(Find(d, "BridgeSectionCheckComponent"), 0).Length}");
            Require(Output(a, 3).Cast<double>().Skip(1).All(double.IsNaN), "Unmapped beams stay unknown");
        });
    }
    private static void Complete()
    {
        using var doc = new GH_Document(); Header(doc, "45 | PONTE COMPLETO: GEOMETRIA → MODELLO → FASI", "Ponte curvo 30+40+30 m, tre travi, conci da 12 m, controventi Warren, soletta shell in tre getti, link rigidi e appoggi orientati.\nTre snapshot: acciaio, primo getto, soletta completa. Modello senza carichi: proprietà e schema da dimensionare. Non è una verifica del ponte.");
        var bg = Geometry(doc, true);
        var brace = Add(doc, "BridgeLongitudinalBracingComponent", 1000, 50); Wire(bg, 14, brace, 0); Int(brace, 3, 2);
        Input(doc, brace, 1, 0, 10000, 20000, 30000, 40000, 50000, 60000, 70000, 80000, 90000, 100000);
        var deck = Deck(doc, brace, 0, 1650, 50); var assigned = Assignment(doc, deck, 0, 2300);
        var bearings = Add(doc, "BridgeBearingsComponent", 2300, 650); Wire(deck, 0, bearings, 0);
        Int(bearings, 3, 2, 0, 2, 2, 1, 2, 2, 1, 2, 2, 1, 2);
        var links = Add(doc, "BridgeConnectionsComponent", 2300, 1200); Wire(deck, 3, links, 1);
        var build = Add(doc, "BuildModelComponent", 3000, 50);
        build.Params.Input[0].DataMapping = GH_DataMapping.Flatten;
        foreach (var pair in new[] { (assigned, 0), (deck, 2), (bearings, 0), (links, 0) }) Wire(pair.Item1, pair.Item2, build, 0);
        Int(build, 2, (int)ModelModel.ModelUnits.Nmm); Text(build, 1, "Ponte curvo acciaio-shell 30+40+30");
        var audit = Add(doc, "BridgeModelAuditComponent", 3550, 50); Wire(build, 0, audit, 0); Wire(deck, 0, audit, 2);
        audit.Params.Input[3].DataMapping = GH_DataMapping.Flatten;
        foreach (var source in new[] { brace, deck, assigned, bearings, links, audit }) source.Hidden = true;
        Info(doc, audit, 4, 4000, 50);
        for (int i = 0; i < 3; i++)
        {
            var phase = Phase(doc, (i + 1) * 10, new[] { "Acciaio", "Primo getto attivo", "Soletta completa" }[i],
                new[] { 0d, 7, 28 }[i], 3000, 600 + i * 450); Int(phase, 2, i == 0 ? 0 : 1);
            var c = Add(doc, "BridgeConstructionSequenceComponent", 3550, 600 + i * 450); Wire(build, 0, c, 0); Wire(phase, 0, c, 1);
            c.Hidden = true;
            if (i == 0) Text(c, 2, "Girder", "SolidDiaphragm", "LongitudinalBracing");
            if (i == 1) Text(c, 2, "Girder", "SolidDiaphragm", "LongitudinalBracing", "Deck\\Pour1");
            Wire(c, 0, audit, 3); Info(doc, c, 5, 4000, 600 + i * 450);
        }
        Save(doc, "45-ponte-completo-fasi", d =>
        {
            var model = (ModelModel)Output(Find(d, "BuildModelComponent"), 0).Single();
            Require(model.Beams.Count == Output(Find(d, "BridgeMemberAssignmentComponent"), 0).Length &&
                model.Plates.Count == Output(Find(d, "BridgeDeckMeshComponent"), 2).Length && model.Plates.Count > 0, "Complete beam/shell model without lost members");
            Require(model.Nodes.Any(n => n.Attributes.OfType<RigidConnectionAttributeModel>().Any()), "Native rigid link definitions assembled");
            var diagnostics = BridGHe.Common.Diagnostics.BridgeModelAudit.Inspect(model, 1e-5);
            Require(!diagnostics.Any(i => i.Severity == "Error" || i.Code == "DISCONNECTED"), string.Join("; ", diagnostics.Select(i => i.ToString())));
            var phases = d.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeConstructionSequenceComponent")
                .Select(c => (ModelModel)Output(c, 0).Single()).OrderBy(m => m.Plates.Count).ToArray();
            Require(phases[0].Plates.Count == 0 && phases[1].Plates.Count > 0 && phases[2].Plates.Count == model.Plates.Count, "Steel/first pour/full deck snapshots");
            Require(phases.All(m => m.Elements.All(e => e.Results.Count == 0)), "Unanalysed snapshots contain no invented results");
        });
    }
}
