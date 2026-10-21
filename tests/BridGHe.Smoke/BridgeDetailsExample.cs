using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BridGHe.Common.Checks;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>Executable tutorial: common gPC definitions drive both layout and local checks.</summary>
internal static class BridgeDetailsExample
{
    private const string Id = "47-pioli-irrigidimenti-verifiche";
    private static int checks;
    private static void Require(bool condition, string name)
    { if (!condition) throw new Exception("Detail tutorial: " + name); checks++; }
    private static void Near(double actual, double expected, string name) =>
        Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= 1e-9 * Math.Max(1, Math.Abs(expected)), name);
    private static object[] Output(GH_Component c, int port = 0) => c.Params.Output[port].VolatileData.AllData(true).Select(g => g.ScriptVariable()).ToArray();
    private static GH_Component Find(GH_Document d, string nick) => d.Objects.OfType<GH_Component>().Single(c => c.NickName == nick);
    private static BridgeStressResult Result(GH_Document d, string nick) => (BridgeStressResult)Output(Find(d, nick)).Single();
    private static void Solve(GH_Document doc)
    {
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        var errors = doc.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => c.NickName + ": " + e)).ToArray();
        Require(errors.Length == 0, string.Join("; ", errors));
    }
    internal static void Run(bool generate = false)
    {
        checks = 0;
        var plugin = BridgeMergeTests.Initialize();
        var math = Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\MathComponents.gha");
        var server = Grasshopper.Instances.ComponentServer;
        if (!server.ObjectProxies.Any(p => p.Desc.Name == "List Item"))
            typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(Assembly), typeof(string) }, null).Invoke(server, new object[] { math, math.Location });
        using var doc = new GH_Document();
        GH_Component Add(string type, string nick, float x, float y)
        {
            var c = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == type));
            c.NickName = nick; c.CreateAttributes(); c.Attributes.Pivot = new PointF(x, y); doc.AddObject(c, false); return c;
        }
        GH_Panel Panel(string name, string text, float x, float y, int w = 300, int h = 100)
        {
            var p = new GH_Panel { NickName = name, UserText = text }; p.CreateAttributes();
            p.Attributes.Pivot = new PointF(x, y); p.Attributes.Bounds = new RectangleF(x, y, w, h); doc.AddObject(p, false); return p;
        }
        void Wire(GH_Component a, int output, GH_Component b, int input) => b.Params.Input[input].AddSource(a.Params.Output[output]);
        void Value(GH_Component c, string name, object v)
        {
            var input = c.Params.Input.Single(p => p.Name == name);
            if (input is Param_Number n) { n.PersistentData.Clear(); n.SetPersistentData(Convert.ToDouble(v)); }
            else if (input is Param_Integer i) { i.PersistentData.Clear(); i.SetPersistentData(Convert.ToInt32(v)); }
            else if (input is Param_Boolean b) { b.PersistentData.Clear(); b.SetPersistentData((bool)v); }
            else if (input is Param_String s) { s.PersistentData.Clear(); s.SetPersistentData((string)v); }
            else throw new ArgumentException(name);
        }
        void Input(GH_Component c, string name, float x, float y, params object[] values)
        {
            var p = Panel(name, string.Join("\n", values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture))), x - 160, y, 210, 85);
            p.Properties.Multiline = false; c.Params.Input.Single(p => p.Name == name).AddSource(p);
        }
        void Readout(GH_Component c, int port, string title, float x, float y, int width = 330, int height = 130)
        { var p = Panel(title, "", x, y, width, height); p.AddSource(c.Params.Output[port]); }
        void Group(string title, Color color, params IGH_DocumentObject[] objects)
        {
            var g = new GH_Group { NickName = title, Colour = Color.FromArgb(45, color) };
            g.CreateAttributes(); foreach (var o in objects) g.AddObject(o.InstanceGuid); doc.AddObject(g, false);
        }
        Panel("COME LEGGERE", "47 | PIOLI E IRRIGIDIMENTI — GEOMETRIA E VERIFICHE\nLeggere da sinistra a destra. La geometria è un ponte a 3 travi e 3 campate; il dettaglio riguarda G2 tra 26 e 34 m, attorno alla pila a 30 m.\nLe azioni locali sono un benchmark di taglio assegnato, non risultati Straus: M=N=T=0; 3 incrementi + ritiro. Non è la verifica completa di questo ponte.\nA: passo 200 mm. B: passo 400 mm. C: irrigidimento intermedio assottigliato a 8 mm. D: reazione d'appoggio aumentata.\nAprire README.md per procedura, unità, risultati attesi e collegamento al modello FEM. Le definizioni gPC sono condivise tra layout e verificatore.", 0, -650, 2100, 230);

        var bridge = Add("SimpleBridgeSolidComponent", "Ponte 30+40+30 m", 450, 0);
        var alignment = new Param_Curve { NickName = "Tracciato 100 m — quota estradosso" }; alignment.CreateAttributes(); alignment.Attributes.Pivot = new PointF(20, 0);
        alignment.PersistentData.Append(new GH_Curve(new LineCurve(Point3d.Origin, new Point3d(100000, 0, 0)))); doc.AddObject(alignment, false); bridge.Params.Input[0].AddSource(alignment);
        Input(bridge, "Support Stations", 0, 120, 0, 30000, 70000, 100000);
        Input(bridge, "Girder Offsets", 0, 230, -4000, 0, 4000);
        bridge.Hidden = true;
        Panel("PREVIEW", "Attivare la preview di Simple Bridge per vedere tutto il ponte. DetailLayout mostra solo il tratto studiato.\nLa trave ha asse a -1500 mm; Steel Top Offset=1250 porta l'ala superiore a -250 mm. I pioli terminano a -70 mm.\nNessuna rigidezza FEM viene aggiunta dalle mesh degli irrigidimenti.", 0, 390, 600, 170);

        var section = Add("BridgeGeometryComponent", "Sezione H + soletta", 1000, 0);
        Input(section, "SlabWidth", 710, -230, 2200);
        Input(section, "WebHeight", 710, -120, 1330);
        Panel("SEZIONE", "Soletta efficace 2200 x 250 mm.\nAnima 1330 x 16 mm.\nAla superiore 450 x 30; inferiore 600 x 40 mm.\nDue file longitudinali di armatura Ø16/150, asse a 40 mm dalle facce.\nLarghezza efficace assegnata: non deriva dalla larghezza del ponte.", 710, 250, 440, 150);
        var studs = Add("BridgeStudsComponent", "Pioli A — passo 200", 1000, 650);
        Input(studs, "LongitudinalPitch", 710, 580, 200);
        Input(studs, "CountPerRow", 710, 690, 2);
        Input(studs, "Diameter", 710, 800, 22);
        var intermediate = Add("BridgeIntermediateStiffenerComponent", "Intermedio 200x20", 1000, 1180);
        Value(intermediate, "LeftPanel", 2000d); Value(intermediate, "RightPanel", 2000d);
        Input(intermediate, "Width", 710, 1010, 200);
        Input(intermediate, "Thickness", 710, 1120, 20);
        Input(intermediate, "ExternalCompressionKN", 710, 1230, 100);
        var support = Add("BridgeSupportStiffenerComponent", "Appoggio 200x25 R900", 1000, 1750);
        Value(support, "Width", 200d); Value(support, "Thickness", 25d); Value(support, "LeftPanel", 2000d); Value(support, "RightPanel", 2000d); Value(support, "WeldThroat", 10d);
        Input(support, "ReactionKN", 710, 1590, 900);
        Input(support, "FootprintLength", 710, 1700, 400);
        Input(support, "FootprintWidth", 710, 1810, 600);
        Panel("PANNELLI E REAZIONE", "Intermedi bilaterali simmetrici; aL=aR=2000 mm; N aggiuntivo=100 kN.\nAppoggio INTERNO, R=900 kN compressiva, eccentricità nulle.\nR non viene ricavata dal taglio di sezione: è un input indipendente, già combinato. Gole: 6 mm intermedi, 10 mm appoggio.\nR e N aggiuntivo sono costanti nelle fasi di questo esempio.", 710, 2200, 520, 180);
        var layout = Add("BridgeDetailLayoutComponent", "Layout G2 26-34 m", 1600, 0);
        Wire(bridge, 14, layout, 0); Wire(section, 0, layout, 1); Value(layout, "Start", 26000d); Value(layout, "End", 34000d);
        Input(layout, "Girders", 1280, -260, 2);
        Input(layout, "Stiffener Stations", 1280, -140, 28000, 32000);
        Wire(studs, 0, layout, 6); Wire(intermediate, 0, layout, 7); Wire(support, 0, layout, 8);
        Readout(layout, 6, "80 pioli e 6 piatti", 1850, -120, 510, 150);
        Group("01 DEFINIZIONI CONDIVISE E PREVIEW", Color.Teal, bridge, section, studs, intermediate, support, layout);

        var checker = Add("BridgeSectionCheckComponent", "A — sezione con dettagli", 2570, 600);
        Wire(section, 0, checker, 0);
        // Materials are serialized typed objects, visibly labelled, rather than hidden inside the checker.
        void Material(string label, BridGHe.Common.Models.MaterialModel material, int port, float y)
        {
            var param = new Param_GenericObject { NickName = label }; param.CreateAttributes(); param.Attributes.Pivot = new PointF(2150, y);
            var type = plugin.GetTypes().Single(t => t.Name == "GH_Material"); param.PersistentData.Append((IGH_Goo)Activator.CreateInstance(type, material));
            doc.AddObject(param, false); checker.Params.Input[port].AddSource(param);
        }
        Material("C30 — fck 30 MPa", new("C30") { Name = "C30", SpecificCompressiveStrength = 30 }, 1, 250);
        Material("S355 — E210000 fy355 fu510 MPa", new("S355") { Name = "S355", Modulus = 210000, MinimumYieldStress = 355, MinimumTensileStress = 510 }, 2, 310);
        Material("B450 — E200000 fy450 fu540 MPa", new("B450") { Name = "B450", Modulus = 200000, MinimumYieldStress = 450, MinimumTensileStress = 540 }, 3, 370);
        var rebar = Add("BridgeRebarRowComponent", "Due file Ø16/150", 2150, 500); Wire(rebar, 0, checker, 5); Wire(rebar, 0, checker, 6);
        var options = Add("BridgeAnalysisOptionsComponent", "SLU — coefficienti espliciti", 2150, 830);
        Value(options, "LimitState", 0); Value(options, "GammaM0", 1d); Value(options, "GammaM1", 1.1); Value(options, "GammaM2", 1.25);
        Wire(options, 0, checker, 8);
        foreach (var c in new[] { studs, intermediate, support }) Wire(c, 0, checker, 7);
        Panel("STATO LIMITE", "LimitState=0 (Ultimate). Azioni già combinate, nessun fattore applicato agli incrementi dal componente.\nStandard=Eurocode4 è il selettore gPC; i coefficienti sono dati dimostrativi espliciti, non una scelta normativa automatica.\nGammaM0=1; M1=1.1; M2=1.25; GammaV pioli=1.25. Gli altri valori sono leggibili negli input.", 2440, 950, 500, 170);
        var phases = new GH_Component[4];
        string[] names = { "01 Acciaio V300", "02 Misto lento V200", "03 Misto istantaneo V800", "04 Ritiro -200 microstrain" };
        for (int i = 0; i < 4; i++)
        {
            phases[i] = Add("BridgePhaseComponent", names[i], 1570, 650 + i * 470);
            Value(phases[i], "Name", names[i]); Value(phases[i], "Kind", i == 0 ? 0 : i == 3 ? 3 : 1);
            Input(phases[i], i == 3 ? "ShrinkageMicrostrain" : "ShearKN", 1280, 610 + i * 470, new[] { 300d, 200, 800, -200 }[i]);
            if (i == 1 || i == 3) Value(phases[i], "Phi", 2d);
            Wire(phases[i], 0, checker, 4);
        }
        Panel("INCREMENTI LOCALI", "Bridge Section Increment usa kN/kNm e MICROSTRAIN: -200, non -0.0002.\nOrdine dei fili = ordine cronologico. M=N=T=0 in questo benchmark. I tagli sono incrementali: V finale=1300 kN.\nL'acciaio contribuisce al taglio dell'anima; il flusso dei pioli nasce dai soli incrementi misti. Il ritiro uniforme non genera qui flusso aggiuntivo.", 1280, 2420, 750, 150);
        var selector = (GH_Component)server.EmitObject(server.ObjectProxies.First(p => p.Desc.Name == "List Item").Guid);
        selector.NickName = "Risultato finale — indice 3"; selector.CreateAttributes(); selector.Attributes.Pivot = new PointF(2940, 600); doc.AddObject(selector, false);
        ((Param_Integer)selector.Params.Input[1]).PersistentData.Clear();
        ((Param_Integer)selector.Params.Input[1]).SetPersistentData(3); Wire(checker, 0, selector, 0);
        Readout(checker, 1, "Ordine risultati", 2440, 50, 450, 150);
        var studResults = Add("BridgeStudResultsComponent", "A — risultati pioli", 3320, 200); Wire(selector, 0, studResults, 0);
        for (int i = 0; i < 5; i++) Readout(studResults, i, studResults.Params.Output[i].Name, 3650 + (i % 3) * 350, 40 + (i / 3) * 190);
        Readout(studResults, 5, "Fasi del flusso", 3650, 440); Readout(studResults, 6, "dq per fase N/mm", 4000, 440);
        var verification = Add("BridgeVerificationResultsComponent", "A — tutti i controlli", 3320, 800); Wire(selector, 0, verification, 0);
        for (int i = 0; i < 7; i++) Readout(verification, i, verification.Params.Output[i].Name, 3650 + i * 330, 740, i is 0 or 6 ? 320 : 300, 400);
        Readout(checker, 2, "Ambito e controlli da completare", 2120, 1250, 780, 400);
        Group("02 CALCOLO GPC E LETTURA DELLE COLONNE", Color.SteelBlue, checker, rebar, options, selector, studResults, verification);

        var wide = Add("BridgeStudsComponent", "B — pioli passo 400", 3300, 1470); Value(wide, "LongitudinalPitch", 400d);
        Input(wide, "LongitudinalPitch", 3000, 1750, 400);
        var weak = Add("BridgeIntermediateStiffenerComponent", "C — intermedio 200x8", 3300, 2140);
        Value(weak, "LeftPanel", 2000d); Value(weak, "RightPanel", 2000d); Value(weak, "ExternalCompressionKN", 100d);
        Input(weak, "Thickness", 3000, 2360, 8);
        var highReaction = Add("BridgeSupportStiffenerComponent", "D — appoggio R3000", 3300, 2800);
        Value(highReaction, "Width", 200d); Value(highReaction, "LeftPanel", 2000d); Value(highReaction, "RightPanel", 2000d); Value(highReaction, "WeldThroat", 10d);
        Input(highReaction, "ReactionKN", 3000, 3120, 3000);
        var variants = new[] { wide, weak, highReaction };
        for (int i = 0; i < variants.Length; i++)
        {
            string label = new[] { "B — passo 400", "C — piatto 8 mm", "D — reazione 3000" }[i]; float y = 1500 + i * 670;
            var c = Add("BridgeDetailCheckComponent", label, 3860, y); Wire(selector, 0, c, 0); Wire(variants[i], 0, c, i + 1);
            Readout(c, 1, "Check — " + label, 4200, y - 120, 390, 430);
            Readout(c, 5, "Rapporto — " + label, 4620, y - 120, 260, 430);
            Readout(c, 6, "Esito e note — " + label, 4910, y - 120, 610, 430);
            Group("03 CONFRONTO " + label, Color.DarkOrange, variants[i], c);
        }
        Panel("COME CONFRONTARE", "B: cambia soltanto il passo. q resta uguale, PEd raddoppia e qRd dimezza.\nC: cambia soltanto lo spessore intermedio. Cercare piatti entro classe 3 e rigidezza torsionale: gPC esclude il beneficio dei pannelli se l'irrigidimento non è idoneo.\nD: cambia soltanto la reazione assegnata. Le verifiche di appoggio peggiorano; il flusso dei pioli resta uguale.\nUna riga Non verificato è un esito strutturale, non un errore rosso del componente. NaN/Da verificare non significa verifica soddisfatta.\nI confronti B/C/D non cambiano la preview: il layout resta collegato alle definizioni A.", 4200, 3450, 1320, 220);

        Solve(doc); Validate(doc);
        string folder = generate ? ExamplePaths.Case(Id) : Path.GetFullPath("artifacts/bridge-details-example"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Id + ".ghx"); GrasshopperBridgeExample.Write(doc, path);
        using var reopened = GrasshopperBridgeExample.Read(path); Solve(reopened); Validate(reopened);
        Require(doc.Objects.Count == reopened.Objects.Count, "All objects survived reopening");
        object Snapshot(string nick)
        {
            var result = Result(reopened, nick); var s = result.Stage.Studs;
            return new { scenario = nick, shearKN = result.Stage.Shear.V, flowNPerMm = s.Flow, forcePerStudKN = s.ForcePerStud,
                resistancePerStudKN = s.ResistancePerStud, resistancePerLengthNPerMm = s.ResistancePerLength,
                ratio = s.ForcePerStud / s.ResistancePerStud, usesStiffeners = result.Stage.Shear.UsesStiffeners,
                flowContributions = s.Contributions, checks = BridgeGpc.Checks(result.Stage), details = result.Stage.Shear.Details, warnings = result.Stage.Warnings };
        }
        File.WriteAllText(Path.Combine(folder, "validation.json"), JsonSerializer.Serialize(new {
            example = Id, solved = true, reopened = true, solverInvoked = false, checks, objects = doc.Objects.Count,
            layout = new { studs = Output(Find(reopened, "Layout G2 26-34 m"), 0).Length, stiffenerPlates = Output(Find(reopened, "Layout G2 26-34 m"), 2).Length },
            scenarios = new[] { Snapshot("Risultato finale — indice 3"), Snapshot("B — passo 400"), Snapshot("C — piatto 8 mm"), Snapshot("D — reazione 3000") }
        }, new JsonSerializerOptions { WriteIndented = true }));
        if (generate)
        {
            string qa = Path.GetFullPath("artifacts/bridge-details-example"); Directory.CreateDirectory(qa);
            using var canvas = new Grasshopper.GUI.Canvas.GH_Canvas { Width = 6200, Height = 4700, Document = reopened };
            canvas.Viewport.Zoom = 1; canvas.Viewport.Target = new System.Drawing.Point(450, 800);
            using var preview = canvas.GetCanvasScreenBuffer(Grasshopper.GUI.Canvas.GH_CanvasMode.Export); preview.Save(Path.Combine(qa, "canvas.png"));
            canvas.Document = null;
        }
        Console.WriteLine($"DETAIL EXAMPLE PASS: {checks} checks, {doc.Objects.Count} GH objects, solve/save/reopen; no Straus solver invoked.");
    }
    private static void Validate(GH_Document doc)
    {
        var a = Result(doc, "Risultato finale — indice 3"); var b = Result(doc, "B — passo 400"); var c = Result(doc, "C — piatto 8 mm"); var d = Result(doc, "D — reazione 3000");
        var s = a.Stage.Studs;
        Near(a.Stage.Shear.V, 1300, "Cumulative shear"); Near(s.Contributions.First().Flow, 0, "No connection flow from steel-only increment");
        Near(s.Contributions.Last().Flow, 0, "Uniform shrinkage has no longitudinal flow in this test");
        Near(s.Flow, s.Contributions.Sum(x => x.Flow + x.AdditionalFlow), "Flow contribution balance");
        Near(s.ForcePerStud, Math.Abs(s.Flow) * 200 / 2 / 1000, "q times pitch / studs, N to kN");
        // Independent steel-governed resistance from the documented GPC formula, not a second library call.
        double steelResistance = .8 * 450 * Math.PI * 22 * 22 / 4 / 1.25 / 1000;
        Near(s.ResistancePerStud, steelResistance, "Steel-governed stud resistance");
        Near(s.ResistancePerLength, 2 * s.ResistancePerStud * 1000 / 200, "Resistance per unit length");
        Near(b.Stage.Studs.Flow, s.Flow, "Pitch does not change elastic flow");
        Near(b.Stage.Studs.ForcePerStud, 2 * s.ForcePerStud, "Double pitch doubles force per stud");
        Near(b.Stage.Studs.ResistancePerLength, s.ResistancePerLength / 2, "Double pitch halves connection resistance per length");
        Require(s.ForcePerStud < s.ResistancePerStud && b.Stage.Studs.ForcePerStud > b.Stage.Studs.ResistancePerStud, "Pitch comparison crosses the stud resistance limit");
        Require(BridgeGpc.Checks(a.Stage).Where(x => x.Name.StartsWith("Intermedio ·") || x.Name.StartsWith("Appoggio ·")).All(x => x.Ratio <= 1), "Base stiffener checks are available and within limits");
        Require(a.Stage.Shear.UsesStiffeners, "Base intermediate stiffener qualifies for panel benefit");
        Require(!c.Stage.Shear.UsesStiffeners, "Unqualified thin stiffener loses panel benefit");
        Require(BridgeGpc.Checks(c.Stage).Any(x => x.Name == "Intermedio · piatti entro classe 3" && x.Ratio > 1), "Thin stiffener is explicitly reported as failing");
        foreach (var variant in new[] { b, c, d })
            Require(variant.Stage.Points.Select(p => p.Stress).SequenceEqual(a.Stage.Points.Select(p => p.Stress)), "Local detail substitution preserves section normal stresses");
        Near(d.Stage.Studs.Flow, s.Flow, "Bearing reaction independent of connection flow");
        double Compression(BridgeStressResult r) => r.Stage.Shear.Details.Single(x => x.Name == "Appoggio · Nst").Value;
        Near(Compression(d) - Compression(a), 2100, "Reaction increment appears in bearing stiffener compression, kN");
        Require(BridgeGpc.Checks(d.Stage).Any(x => x.Name == "Appoggio · saldature alle flange" && x.Ratio > 1)
            && BridgeGpc.Checks(d.Stage).Any(x => x.Name == "Appoggio · contatto piatti/flangia" && x.Ratio > 1), "High reaction exposes both weld and contact failures");
        var layout = Find(doc, "Layout G2 26-34 m");
        Require(Output(layout, 0).Length == 80 && Output(layout, 2).Length == 6, "40 rows x2 studs, two intermediate pairs and one support pair");
        Require(Output(layout, 2).Cast<Mesh>().All(m => m.IsValid && m.IsClosed), "Stiffener preview consists of valid closed meshes");
        Require(Output(layout, 0).Cast<Line>().All(l => Math.Abs(l.From.Z + 250) < 1e-8 && Math.Abs(l.To.Z + 70) < 1e-8), "Stud datum and cover");
        var definitions = layout.Params.Output[3].VolatileData.AllData(true).Select(g => g.GetType().GetProperty("Value").GetValue(g)).OfType<BridgeStiffener>().ToArray();
        Require(definitions.Length == 3 && definitions.All(x => x switch {
            BridgeIntermediateStiffener i => i.LeftPanel == 2000 && i.RightPanel == 2000,
            BridgeSupportStiffener r => r.LeftPanel == 2000 && r.RightPanel == 2000,
            _ => false }), "Adjacent panel lengths in preview and checks agree");
        var expected = HBridgeSection.Calculate(a.Analysis.Input);
        Near(expected.Stages.Last().Studs.Flow, s.Flow, "Component agrees with direct GPC calculation after archive");
    }
}
