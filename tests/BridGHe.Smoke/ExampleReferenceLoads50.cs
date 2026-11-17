using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.MovingLoads;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using static ExampleReferenceLoads;

/// <summary>
/// Example 50 REV004 from the user's REV003: the loads of the four phases follow the company reference script (GH04).
/// Phase 10 steel: self-weight of the steel (Dead, × 1.15 for the connections in the combinations) and casting of the slab on the strips.
/// Phase 20 n∞: G2 pavement, curbs, safety barriers, parapets and finishes on the slab strips. Phase 30 n0: LM1 lane loads on the strips,
/// tandems of lanes 1 and 2 on the moving path, braking, centrifugal force, wind and uniform temperature. Phase 40: shrinkage as in REV003.
/// </summary>
internal static class ExampleReferenceLoads50
{
    internal static string Folder => Path.Combine(ExamplePaths.Root, "50-ponte-quattro-modelli");
    internal static string Source => Path.Combine(Folder, "50-ponte-quattro-modelli_REV003.ghx");
    internal static string Target => Path.Combine(Folder, "50-ponte-quattro-modelli_REV004.ghx");
    private static string Book(string revision) => Path.Combine(Folder, $"Sezioni_conci_{revision}.xlsx");

    internal const double SteelFactor = 1.15;
    internal static readonly string[] Ids = {
        "M01_ACCIAIO_PESO", "M01_ACCIAIO_SOLETTA", "M01_ACCIAIO_TRAVI",
        "M02_NINF_G2_PAV", "M02_NINF_G2_CORD", "M02_NINF_G2_GR", "M02_NINF_G2_PAR", "M02_NINF_G2_FIN",
        "M03_N0_Q1_UDL", "M03_N0_Q3_FR", "M03_N0_Q4_CENTR", "M03_N0_Q5_VENTO", "M03_N0_Q7_TEMP",
        "M04_NSH_RITIRO" };

    /// <summary>Strip trees and constants shared by phases 20 and 30.</summary>
    private sealed class Strips
    {
        internal IGH_Param Beams, Widths, EdgeWidths, Zero, One, Zeros, Ones, Schema, Global, DirX, DirY, DirZ, Girders, Dead;
        internal int N; internal double[] Offsets; internal double Left, Right, Curb;
    }

    internal static void Generate()
    {
        Require(!File.Exists(Target), "REV004 already exists; use --check (a revision is never overwritten).");
        byte[] original = File.ReadAllBytes(Source), originalBook = File.ReadAllBytes(Book("REV003"));
        if (File.Exists(Book("REV004"))) Require(File.ReadAllBytes(Book("REV004")).SequenceEqual(originalBook), "Sezioni_conci_REV004.xlsx differs from REV003");
        else File.WriteAllBytes(Book("REV004"), originalBook);
        using var doc = GrasshopperBridgeExample.Read(Source); doc.FilePath = Target;
        var cv = new Canvas(doc);
        Prepare(doc);
        var s = SharedStrips(doc, cv);
        var replaced = new HashSet<string>(StringComparer.Ordinal);
        Steel(doc, cv);
        LongTerm(doc, cv, s, replaced);
        ShortTerm(doc, cv, s, replaced);
        Traffic(doc, cv, s);
        Combinations(doc);
        cv.ExposeDefaults();
        var removed = RemoveDetached(doc, replaced);
        var restore = Neutralize(doc, "generate-50");
        BridgeSegmentExcelTests.Solve(doc, true);
        foreach (var read in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeSectionReadComponent")) { BridgeSegmentExcelTests.Press(read); read.ExpireSolution(false); }
        BridgeSegmentExcelTests.Solve(doc, true);
        Errors(doc);
        GH_Document.EnableSolutions = false; restore();
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis")) BridgeMovingRevisionExample.Clear(c);
        Example50HorizontalLayout.Apply(doc); ExampleTidy.Straighten(doc);
        GH_Document.EnableSolutions = false; GrasshopperBridgeExample.Write(doc, Target);
        var report = Check(Target);
        Require(original.SequenceEqual(File.ReadAllBytes(Source)) && originalBook.SequenceEqual(File.ReadAllBytes(Book("REV003"))), "REV003 changed.");
        File.WriteAllText(Path.Combine(Folder, "REV004-validation.json"), JsonSerializer.Serialize(new {
            Mode = "offline: reference-script phase loads, no Straus analysis", Generator = "--example-reference-loads 50",
            Source = Path.GetFileName(Source), SourceSHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)),
            Definition = Path.GetFileName(Target), DefinitionSHA256 = Sha(Target), Removed = removed, Checks = report,
            NativeSolverInvoked = false }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine("REV004 GENERATED: " + Target);
    }

    /// <summary>REV004 file names, empty analysis caches, no automatic run.</summary>
    private static void Prepare(GH_Document doc)
    {
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis"))
        {
            BridgeMovingRevisionExample.Clear(c);
            foreach (var p in c.Params.Input.Where(p => p.Name is "Keep Running" or "Run").SelectMany(p => p.Sources).OfType<GH_BooleanToggle>()) p.Value = false;
            foreach (var p in c.Params.Input.Where(p => p.Name is "Output Path" or "Output path").SelectMany(p => p.Sources).OfType<GH_Panel>())
                p.UserText = p.UserText.Replace("REV003", "REV004");
        }
        foreach (var p in doc.Objects.OfType<GH_Panel>())
            p.UserText = p.UserText.Replace("Sezioni_conci_REV003.xlsx", "Sezioni_conci_REV004.xlsx").Replace("Report_50_REV003", "Report_50_REV004");
        foreach (var procedure in doc.Objects.OfType<GH_Panel>().Where(p => p.NickName == "PROCEDURA REV003 E IPOTESI").ToArray())
        {
        procedure.NickName = procedure.Name = "PROCEDURA REV004 E IPOTESI";
        procedure.UserText = procedure.UserText.Replace("REV003", "REV004") + "\nCARICHI REV004: fasi come lo script di riferimento aziendale. Fase 10 acciaio: peso proprio (Dead × 1,15) e getto della soletta. "
            + "Fase 20 n∞: G2 pavimentazione, cordoli, guard-rail, parapetti, finiture. Fase 30 n0: schema 1 (corsie 1 e 2), frenatura, centrifuga, vento, temperatura.";
        }
        foreach (var p in ReportRevision(doc)) p.UserText = "REV004 — carichi delle fasi come lo script di riferimento aziendale";
    }

    private static IEnumerable<GH_Panel> ReportRevision(GH_Document doc) =>
        In(doc.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == "BridgeReportComponent"), "Revision").Sources.OfType<GH_Panel>();

    private static Strips SharedStrips(GH_Document doc, Canvas cv)
    {
        var grillage = Component(doc, "Bridge Slab Grillage", "Fasce soletta FEM");
        var n = Float(doc, "Numero travi longitudinali");
        var s = new Strips
        {
            N = (int)Number(n), Schema = Float(doc, "Schema carichi di test"), Global = Float(doc, "Sistema di riferimento dei carichi — Global"),
            DirZ = Float(doc, "Direzione carichi di test"), Dead = Float(doc, "CASO PER LA MAPPA — peso proprio"),
            Left = Number(Float(doc, "Sbalzo laterale sinistro soletta [mm]")), Right = Number(Float(doc, "Sbalzo laterale destro soletta [mm]"))
        };
        var explicitOffsets = Float(doc, "Offset trasversali non uniformi [mm] — destra negativi (opzionale)");
        Require(explicitOffsets.SourceCount == 0 && ((explicitOffsets as Param_Number)?.PersistentData.IsEmpty ?? true), "non-uniform girder offsets: the lane patterns assume a regular layout");
        double spacing = Number(Float(doc, "Interasse regolare travi [mm]")), half = (s.N - 1) * spacing / 2;
        s.Offsets = new[] { -half - s.Right }.Concat(Enumerable.Range(0, s.N).Select(i => -half + i * spacing)).Append(half + s.Left).ToArray();
        cv.At(grillage);
        s.One = cv.Text("Costante 1 — fasce e frazioni", "1"); s.Zero = cv.Text("Costante 0 — fasce o frazioni scariche", "0");
        var order = cv.Toggle("Duplicazione in ordine", true);
        var perStation = cv.Sum("Fasce per stazione = travi + 1", n, s.One);
        var interior = cv.Sub("Fasce interne per stazione = travi − 1", n, s.One);
        s.Zeros = cv.Duplicate("Zeri delle fasce interne", s.Zero, interior, order);
        s.Beams = cv.Partition("Fasce soletta per stazione — da destra a sinistra", Out(grillage, "Beams"), perStation);
        s.Widths = cv.Partition("Larghezze tributarie delle fasce per stazione [mm]", Out(grillage, "Strip Widths"), perStation);
        var edges = cv.Merge("Maschera sbalzi [1, 0…, 1]", s.One, s.Zeros, s.One);
        s.EdgeWidths = cv.Mul("Larghezze tributarie, solo sbalzi [mm]", s.Widths, edges);
        s.DirX = cv.Text("Direzione globale X: 0", "0"); s.DirY = cv.Text("Direzione globale Y: 1", "1");
        return s;
    }

    /// <summary>Phase 10: the self-weight of the steel members (Dead) as its own action; the casting of the slab stays on the strips.</summary>
    private static void Steel(GH_Document doc, Canvas cv)
    {
        var phase = Component(doc, "Bridge Phase", "M01_ACCIAIO | fase di test");
        var straus = Component(doc, "Straus R3 Analysis", "M01_ACCIAIO | analisi lineare");
        cv.At(phase);
        var action = cv.Bridge("BridgeActionComponent", "M01_ACCIAIO_PESO | azione — peso proprio dell'acciaio (caso Dead)");
        Wire(In(action, "Action ID"), cv.Text("M01_ACCIAIO_PESO | identificativo azione", "M01_ACCIAIO_PESO"));
        Wire(In(action, "Model"), Out(straus, "Model")); Wire(In(action, "Case"), Float(doc, "CASO PER LA MAPPA — peso proprio"));
        Wire(In(action, "Behavior"), cv.Text("M01_ACCIAIO_PESO | comportamento: 0 = solo acciaio", "0"));
        var actions = In(phase, "Actions"); Wire(actions, new[] { Out(action, "Action") }.Concat(actions.Sources).ToArray());
        var girders = Float(doc, "M01_ACCIAIO | carico verticale travi principali [N/mm = kN/m]");
        girders.NickName = girders.Name = "M01_ACCIAIO | casseri o altri carichi di costruzione sulle travi [N/mm] — 0 nello script di riferimento";
        var note = (GH_Panel)cv.Text("NOTA FASE 10 — script di riferimento aziendale",
            "Fase solo acciaio. M01_ACCIAIO_PESO = peso proprio delle carpenterie (caso Dead); il fattore 1,15 delle connessioni è il suo coefficiente nelle combinazioni.\n"
            + "M01_ACCIAIO_SOLETTA = getto: peso del cls × spessore × larghezza tributaria di ogni fascia, portato dalla sola sezione d'acciaio.\n"
            + "M01_ACCIAIO_TRAVI resta a 0: lo script di riferimento non ha casseri né altri carichi di costruzione.");
        note.Properties.Multiline = true;
    }

    /// <summary>Phase 20: the five G2 cases of the reference script on the slab strips, n∞.</summary>
    private static void LongTerm(GH_Document doc, Canvas cv, Strips s, HashSet<string> replaced)
    {
        const string m = "M02_NINF";
        var build = Component(doc, "Build Model", m); var straus = Component(doc, "Straus R3 Analysis", m + " | analisi lineare");
        var cases = Component(doc, "Bridge Analysis Cases", m + " | casi richiesti dalle azioni"); var phase = Component(doc, "Bridge Phase", m + " | fase di test");
        var oldSlab = Component(doc, "Beam Distributed Load", m + " | carico soletta"); var oldGirder = Component(doc, "Beam Distributed Load", m + " | carico travi");
        var girders = In(oldGirder, "Beam").Sources.Single(); var elements = In(build, "Elements");
        cv.At(oldSlab);
        var curb = cv.Slider(m + " | G2 cordoli — larghezza di ciascun cordolo dal bordo [mm]", 700, 0, 1500);
        s.Curb = 700;
        var rightC = Float(doc, "Sbalzo laterale destro soletta [mm]"); var leftC = Float(doc, "Sbalzo laterale sinistro soletta [mm]");
        var rR = cv.Div(m + " | cordolo / sbalzo destro", curb, rightC); var rL = cv.Div(m + " | cordolo / sbalzo sinistro", curb, leftC);
        var cR = cv.Sub(m + " | 1 − cordolo / sbalzo destro", s.One, rR); var cL = cv.Sub(m + " | 1 − cordolo / sbalzo sinistro", s.One, rL);
        var toPressure = cv.Text(m + " | conversione kN/m³ × mm → N/mm², verso −Z", "-0.000001");
        var down = cv.Text(m + " | conversione kN/m → N/mm, verso −Z", "-1");
        var pPav = cv.Mul(m + " | G2 pavimentazione — pressione [N/mm²]", cv.Mul(m + " | G2 pavimentazione — γ × h",
            cv.Slider(m + " | G2 pavimentazione — peso specifico [kN/m³]", 25, 0, 30, 2), cv.Slider(m + " | G2 pavimentazione — spessore medio [mm]", 150, 0, 400)), toPressure);
        var pCord = cv.Mul(m + " | G2 cordoli — pressione [N/mm²]", cv.Mul(m + " | G2 cordoli — γ × h",
            cv.Slider(m + " | G2 cordoli — peso specifico [kN/m³]", 25, 0, 30, 2), cv.Slider(m + " | G2 cordoli — altezza al netto della soletta [mm]", 200, 0, 600)), toPressure);
        var qGr = cv.Mul(m + " | G2 guard-rail — carico per lato [N/mm]", cv.Slider(m + " | G2 guard-rail — carico di ciascuno [kN/m]", 1.5, 0, 10, 2), down);
        var qPar = cv.Mul(m + " | G2 parapetto — carico per lato [N/mm]", cv.Slider(m + " | G2 parapetto — carico di ciascuno [kN/m]", 1, 0, 10, 2), down);
        var qFin = cv.Mul(m + " | G2 finiture — carico per bordo [N/mm]", cv.Slider(m + " | G2 finiture (velette, impianti) — carico per bordo [kN/m]", 1.5, 0, 10, 2), down);
        var pav = CaseAndAction(cv, m, "M02_NINF_G2_PAV", "G2_PAV pavimentazione", straus, 1);
        var cord = CaseAndAction(cv, m, "M02_NINF_G2_CORD", "G2_CORD cordoli", straus, 1);
        var gr = CaseAndAction(cv, m, "M02_NINF_G2_GR", "G2_GR guard-rail", straus, 1);
        var par = CaseAndAction(cv, m, "M02_NINF_G2_PAR", "G2_PAR parapetti", straus, 1);
        var fin = CaseAndAction(cv, m, "M02_NINF_G2_FIN", "G2_FIN finiture", straus, 1);
        var d1 = Distributed(cv, m + " | G2_PAV pavimentazione — fasce fra i cordoli", s.Beams, pav.Case, s.Schema, s.Global, s.DirZ,
            cv.Mul(m + " | G2_PAV q per fascia [N/mm]", s.Widths, pPav),
            new[] { cv.Merge(m + " | G2_PAV a/L per fascia [destra → sinistra]", rR, s.Zeros, s.Zero) }, new[] { cv.Merge(m + " | G2_PAV b/L per fascia", s.Zero, s.Zeros, rL) });
        var d2 = Distributed(cv, m + " | G2_CORD cordoli — sbalzi", Out(d1, "Beam"), cord.Case, s.Schema, s.Global, s.DirZ,
            cv.Mul(m + " | G2_CORD q per fascia [N/mm]", s.EdgeWidths, pCord),
            new[] { cv.Merge(m + " | G2_CORD a/L per fascia [destra → sinistra]", s.Zero, s.Zeros, cL) }, new[] { cv.Merge(m + " | G2_CORD b/L per fascia", cR, s.Zeros, s.Zero) });
        // The line loads act on a narrow band of each cantilever strip: Build BridGHe Model refuses a concentrated force inside a strip
        // (it interpolates across the load), so the force F = q × width is spread uniformly over the band.
        var band = cv.Slider(m + " | G2 carichi lineari — larghezza della fascia di applicazione [mm]", 100, 10, 500);
        var half = cv.Mul(m + " | G2 metà della fascia di applicazione [mm]", band, cv.Text(m + " | costante 0,5", "0.5"));
        var inner = cv.Sub(m + " | G2 cordolo − metà fascia [mm]", curb, half); var outer = cv.Sum(m + " | G2 cordolo + metà fascia [mm]", curb, half);
        var grA = cv.Merge(m + " | G2_GR a/L per fascia [destra → sinistra]", cv.Div(m + " | G2_GR destro a/L", inner, rightC), s.Zeros,
            cv.Sub(m + " | G2_GR sinistro a/L", s.One, cv.Div(m + " | G2_GR (cordolo + metà fascia) / sbalzo sinistro", outer, leftC)));
        var grB = cv.Merge(m + " | G2_GR b/L per fascia", cv.Sub(m + " | G2_GR destro b/L", s.One, cv.Div(m + " | G2_GR (cordolo + metà fascia) / sbalzo destro", outer, rightC)),
            s.Zeros, cv.Div(m + " | G2_GR sinistro b/L", inner, leftC));
        var edgeA = cv.Merge(m + " | G2 bordi a/L per fascia [destra → sinistra]", s.Zero, s.Zeros, cv.Sub(m + " | G2 bordo sinistro a/L", s.One, cv.Div(m + " | G2 fascia / sbalzo sinistro", band, leftC)));
        var edgeB = cv.Merge(m + " | G2 bordi b/L per fascia", cv.Sub(m + " | G2 bordo destro b/L", s.One, cv.Div(m + " | G2 fascia / sbalzo destro", band, rightC)), s.Zeros, s.Zero);
        IGH_Param Spread(string label, IGH_Param q) => cv.Div(m + $" | {label} q sulla fascia di applicazione [N/mm]", cv.Mul(m + $" | {label} forza per fascia [N]", s.EdgeWidths, q), band);
        var p1 = Distributed(cv, m + " | G2_GR guard-rail — bordo interno dei cordoli", Out(d2, "Beam"), gr.Case, s.Schema, s.Global, s.DirZ, Spread("G2_GR", qGr), new[] { grA }, new[] { grB });
        var p2 = Distributed(cv, m + " | G2_PAR parapetti — bordi", Out(p1, "Beam"), par.Case, s.Schema, s.Global, s.DirZ, Spread("G2_PAR", qPar), new[] { edgeA }, new[] { edgeB });
        var p3 = Distributed(cv, m + " | G2_FIN finiture — bordi", Out(p2, "Beam"), fin.Case, s.Schema, s.Global, s.DirZ, Spread("G2_FIN", qFin), new[] { edgeA }, new[] { edgeB });
        Replace(elements, Out(oldSlab, "Beam"), Out(p3, "Beam")); Replace(elements, Out(oldGirder, "Beam"), girders);
        Redirect(doc, Out(oldSlab, "Beam"), Out(p3, "Beam")); Redirect(doc, Out(oldGirder, "Beam"), girders);
        elements.DataMapping = GH_DataMapping.Flatten;
        var all = new[] { pav, cord, gr, par, fin };
        Wire(In(phase, "Actions"), all.Select(x => Out(x.Action, "Action")).ToArray());
        Wire(In(cases, "Required Cases"), all.Select(x => Out(x.Case, "LoadCase")).Append(s.Dead).ToArray());
        Float(doc, m + " | casi restituiti").Let(p => ((GH_Panel)p).UserText = string.Join("\n", Ids.Where(i => i.StartsWith(m, StringComparison.Ordinal)).Append("Dead")));
        var note = (GH_Panel)cv.Text("NOTA FASE 20 — script di riferimento aziendale",
            "Lunga durata (n∞): carichi permanenti portati G2 come lo script di riferimento, sulle fasce di soletta (pressione × larghezza tributaria).\n"
            + "G2_PAV 25 kN/m³ × 150 mm fra i cordoli; G2_CORD 25 kN/m³ × 200 mm su 700 mm per lato; G2_GR 1,5 kN/m per lato al bordo interno dei cordoli;\n"
            + "G2_PAR 1,0 kN/m e G2_FIN 1,5 kN/m per bordo. Le frazioni a/L, b/L sugli sbalzi derivano da cordolo e sbalzi; le fasce interne sono 0.\n"
            + "I carichi lineari stanno su una fascia di 100 mm (centrata sul bordo interno del cordolo, o adiacente al bordo): l'assemblaggio non accetta forze concentrate dentro le aste.");
        note.Properties.Multiline = true;
        foreach (var n in new[] { m + " | carico soletta", m + " | carico travi", m + " | caso SOLETTA", m + " | caso TRAVI", m + "_SOLETTA | azione", m + "_TRAVI | azione",
            m + " | q = pressione × fascia tributaria", m + " | pressione in N/mm²" }) replaced.Add(n);
    }

    /// <summary>Phase 30: lane loads on the strips, braking, centrifugal force, wind and temperature, n0.</summary>
    private static void ShortTerm(GH_Document doc, Canvas cv, Strips s, HashSet<string> replaced)
    {
        const string m = "M03_N0";
        var build = Component(doc, "Build Model", m); var straus = Component(doc, "Straus R3 Analysis", m + " | analisi lineare");
        var cases = Component(doc, "Bridge Analysis Cases", m + " | casi richiesti dalle azioni"); var phase = Component(doc, "Bridge Phase", m + " | fase di test");
        var oldSlab = Component(doc, "Beam Distributed Load", m + " | carico soletta"); var oldGirder = Component(doc, "Beam Distributed Load", m + " | carico travi");
        var state = Component(doc, "Bridge State Override", m + " | proprietà per stato di ogni asta");
        var girders = In(oldGirder, "Beam").Sources.Single(); var others = Out(state, "Other Steel"); var elements = In(build, "Elements");
        Require(elements.Sources.Contains(others), "M03 Build Model without the other steel members");
        var n = Float(doc, "Numero travi longitudinali");
        cv.At(oldSlab);
        // LM1 lane loads: lane 1 of 3 m next to the left curb (the moving tandem uses the same lane axis), 2.5 kN/m² on the rest.
        const double lane = 3000;
        double yLeft = s.Offsets[^1] - s.Curb, yRight = s.Offsets[0] + s.Curb;
        var b1 = Band(s.Offsets, yLeft - lane, yLeft); var br = Band(s.Offsets, yRight, yLeft - lane);
        var toPressure = cv.Text(m + " | conversione kN/m² → N/mm², verso −Z", "-0.001");
        var q1 = cv.Slider(m + " | Q1 schema 1 — carico distribuito corsia 1 q1k [kN/m²]", 9, 0, 20, 2);
        var qr = cv.Slider(m + " | Q1 schema 1 — carico distribuito altre corsie e area rimanente [kN/m²]", 2.5, 0, 20, 2);
        var udl = CaseAndAction(cv, m, "M03_N0_Q1_UDL", "Q1_UDL schema 1 distribuito", straus, 2);
        const string generated = "calcolato dal generatore per corsia 1 di 3 m al cordolo sinistro; aggiornare se cambia la sezione trasversale";
        var u1 = Distributed(cv, m + " | Q1_UDL — corsia 2 e area rimanente", s.Beams, udl.Case, s.Schema, s.Global, s.DirZ,
            cv.Mul(m + " | Q1_UDL rimanente q per fascia [N/mm]", cv.Mul(m + " | Q1_UDL rimanente larghezze caricate [mm]", s.Widths, cv.List(m + " | Q1_UDL rimanente maschera per fascia [destra → sinistra] — " + generated, br.Mask)),
                cv.Mul(m + " | Q1_UDL rimanente pressione [N/mm²]", qr, toPressure)),
            new[] { cv.List(m + " | Q1_UDL rimanente a/L per fascia — " + generated, br.A) }, new[] { cv.List(m + " | Q1_UDL rimanente b/L per fascia — " + generated, br.B) });
        var u2 = Distributed(cv, m + " | Q1_UDL — corsia 1", Out(u1, "Beam"), udl.Case, s.Schema, s.Global, s.DirZ,
            cv.Mul(m + " | Q1_UDL corsia 1 q per fascia [N/mm]", cv.Mul(m + " | Q1_UDL corsia 1 larghezze caricate [mm]", s.Widths, cv.List(m + " | Q1_UDL corsia 1 maschera per fascia [destra → sinistra] — " + generated, b1.Mask)),
                cv.Mul(m + " | Q1_UDL corsia 1 pressione [N/mm²]", q1, toPressure)),
            new[] { cv.List(m + " | Q1_UDL corsia 1 a/L per fascia — " + generated, b1.A) }, new[] { cv.List(m + " | Q1_UDL corsia 1 b/L per fascia — " + generated, b1.B) });
        // Uniform temperature as an imposed strain α ΔT on all steel members and strips (the reference script loads girders, cross-girders and slab).
        var temp = CaseAndAction(cv, m, "M03_N0_Q7_TEMP", "Q7_TEMP temperatura uniforme", straus, 2);
        var strain = cv.Mul(m + " | Q7 deformazione impressa α ΔT", cv.Text(m + " | Q7 coefficiente di dilatazione α [1/°C] — acciaio e cls come lo script di riferimento", "0.000012"),
            cv.Slider(m + " | Q7 variazione termica uniforme ΔT [°C]", 35, -60, 60));
        var strainType = cv.Text(m + " | Q7 tipo di precarico: 1 = deformazione", "1");
        var ts = Strain(cv, m + " | Q7_TEMP — fasce di soletta", Out(u2, "Beam"), temp.Case, strainType, strain);
        Replace(elements, Out(oldSlab, "Beam"), Out(ts, "Beam")); Redirect(doc, Out(oldSlab, "Beam"), Out(ts, "Beam"));
        // Girder loads: braking, centrifugal force, wind.
        cv.At(oldGirder);
        var length = Out(doc.Objects.OfType<GH_Component>().Single(c => c.Name == "Bridge Span Stations"), "Alignment Length");
        var axle = cv.Slider(m + " | Q3 frenatura — carico asse del tandem corsia 1 Q1k [kN]", 300, 0, 600);
        var lengthM = cv.Mul(m + " | Q3 lunghezza caricata L [m]", length, cv.Text(m + " | Q3 conversione mm → m", "0.001"));
        var first = cv.Mul(m + " | Q3 0,6 × 2 Q1k [kN]", axle, cv.Text(m + " | Q3 0,6 × 2 assi (α = 1)", "1.2"));
        var second = cv.Mul(m + " | Q3 0,10 q1k w1 L [kN]", cv.Mul(m + " | Q3 q1k w1 L", cv.Mul(m + " | Q3 q1k w1 [kN/m]", q1, cv.Text(m + " | Q3 larghezza corsia 1 w1 [m]", "3")), lengthM),
            cv.Text(m + " | Q3 coefficiente 0,10", "0.1"));
        var braking = cv.Min(m + " | Q3 frenatura entro 180–900 kN [kN]", cv.Max(m + " | Q3 frenatura ≥ 180 kN", cv.Sum(m + " | Q3 frenatura prima dei limiti [kN]", first, second),
            cv.Text(m + " | Q3 limite inferiore [kN]", "180")), cv.Text(m + " | Q3 limite superiore [kN]", "900"));
        var qx = cv.Div(m + " | Q3 frenatura per trave [N/mm]", cv.Div(m + " | Q3 frenatura per unità di lunghezza [N/mm]", cv.Mul(m + " | Q3 frenatura [N]", braking, cv.Text(m + " | Q3 conversione kN → N", "1000")), length), n);
        var fr = CaseAndAction(cv, m, "M03_N0_Q3_FR", "Q3_FR frenatura", straus, 2);
        var centr = CaseAndAction(cv, m, "M03_N0_Q4_CENTR", "Q4_CENTR centrifuga", straus, 2);
        var wind = CaseAndAction(cv, m, "M03_N0_Q5_VENTO", "Q5_VENTO vento", straus, 2);
        var height = cv.Sum(m + " | Q5 altezza esposta [mm]", cv.Sum(m + " | Q5 veicolo + trave [mm]", cv.Slider(m + " | Q5 vento — altezza del veicolo [mm]", 3000, 0, 5000),
            Float(doc, "Sezione travi principali | Altezza totale [mm]")), Float(doc, "Spessore soletta [mm]"));
        var w = cv.Div(m + " | Q5 vento per trave [N/mm]", cv.Mul(m + " | Q5 vento totale [N/mm]", cv.Mul(m + " | Q5 p × h", cv.Slider(m + " | Q5 vento — pressione [kN/m²]", 2, 0, 5, 2), height),
            cv.Text(m + " | Q5 conversione kN/m² × mm → N/mm", "0.001")), n);
        var zeroA = new[] { s.Zero };
        var g1 = Distributed(cv, m + " | Q3_FR frenatura — travi, X globale", girders, fr.Case, s.Schema, s.Global, s.DirX, qx, zeroA, zeroA);
        var g2 = Distributed(cv, m + " | Q4_CENTR centrifuga — travi, Y globale", Out(g1, "Beam"), centr.Case, s.Schema, s.Global, s.DirY,
            cv.Slider(m + " | Q4 centrifuga — su ciascuna trave [kN/m = N/mm]", 0.01, 0, 10, 3), zeroA, zeroA);
        var g3 = Distributed(cv, m + " | Q5_VENTO trasversale — travi, Y globale negativo", Out(g2, "Beam"), wind.Case, s.Schema, s.Global, s.DirY, cv.Neg(m + " | Q5 vento trasversale per trave [N/mm]", w), zeroA, zeroA);
        var g4 = Distributed(cv, m + " | Q5_VENTO longitudinale — travi, X globale", Out(g3, "Beam"), wind.Case, s.Schema, s.Global, s.DirX,
            cv.Mul(m + " | Q5 vento longitudinale per trave [N/mm]", w, cv.Slider(m + " | Q5 vento — quota longitudinale", 0.25, 0, 1, 2)), zeroA, zeroA);
        var g5 = Distributed(cv, m + " | Q5_VENTO verticale — travi, Z globale negativo", Out(g4, "Beam"), wind.Case, s.Schema, s.Global, s.DirZ,
            cv.Neg(m + " | Q5 vento verticale per trave [N/mm]", cv.Mul(m + " | Q5 vento verticale per trave, modulo [N/mm]", w, cv.Slider(m + " | Q5 vento — quota verticale", 0.25, 0, 1, 2))), zeroA, zeroA);
        var tg = Strain(cv, m + " | Q7_TEMP — travi principali", Out(g5, "Beam"), temp.Case, strainType, strain);
        var to = Strain(cv, m + " | Q7_TEMP — traversi e controventi", others, temp.Case, strainType, strain);
        Replace(elements, Out(oldGirder, "Beam"), Out(tg, "Beam")); Replace(elements, others, Out(to, "Beam")); Redirect(doc, Out(oldGirder, "Beam"), Out(tg, "Beam"));
        elements.DataMapping = GH_DataMapping.Flatten;
        var all = new[] { udl, fr, centr, wind, temp };
        var actions = In(phase, "Actions");
        var moving = actions.Sources.Where(p => p.Attributes.GetTopLevel.DocObject is GH_Component c && c.NickName.StartsWith(m + " | Azioni di traffico", StringComparison.Ordinal)).ToArray();
        Require(moving.Length == 1, "moving traffic actions of phase 30");
        Wire(actions, all.Select(x => Out(x.Action, "Action")).Concat(moving).ToArray());
        Wire(In(cases, "Required Cases"), all.Select(x => Out(x.Case, "LoadCase")).Append(s.Dead).ToArray());
        ((GH_Panel)Float(doc, m + " | casi restituiti")).UserText = string.Join("\n", Ids.Where(i => i.StartsWith(m, StringComparison.Ordinal)).Append("Dead"));
        var note = (GH_Panel)cv.Text("NOTA FASE 30 — script di riferimento aziendale",
            "Breve durata (n0). Q1 schema 1: corsia 1 di 3 m al cordolo sinistro, 9 kN/m² e tandem 2 × 300 kN; corsia 2 tandem 2 × 200 kN; 2,5 kN/m² sul resto della carreggiata.\n"
            + "I carichi distribuiti stanno sulle fasce su tutta la lunghezza; i tandem si muovono nell'analisi mobile. Q3 frenatura 0,6·2·Q1k + 0,10·q1k·w1·L entro 180–900 kN, ripartita sulle travi lungo X.\n"
            + "Q4 centrifuga 0,01 kN/m per trave (valore dello script di riferimento). Q5 vento 2 kN/m² su veicolo + trave + soletta, ripartito sulle travi: Y, 0,25 lungo X e 0,25 verso il basso.\n"
            + "Q7 temperatura uniforme +35 °C come deformazione impressa α ΔT, α = 1,2e-5 per acciaio e cls. Nessun gradiente termico: lo script di riferimento definisce il caso senza carichi.");
        note.Properties.Multiline = true;
        foreach (var x in new[] { m + " | carico soletta", m + " | carico travi", m + " | caso SOLETTA", m + " | caso TRAVI", m + "_SOLETTA | azione", m + "_TRAVI | azione",
            m + " | q = pressione × fascia tributaria", m + " | pressione in N/mm²" }) replaced.Add(x);
    }

    /// <summary>LM1 tandems of lanes 1 and 2 on the existing moving path; lane 1 axis at 2200 mm from the left deck edge.</summary>
    private static void Traffic(GH_Document doc, Canvas cv, Strips s)
    {
        var vehicle = doc.Objects.OfType<GH_Component>().Single(c => c.Name == "Moving Vehicle");
        var points = doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Moving Point Force").ToArray();
        Require(points.Length == 4, "four wheels in REV003");
        // The inputs of the REV003 wheels are read from their wires (some controls share a nickname).
        IGH_Param Source(GH_Component c, string input) => In(c, input).Sources.Single();
        var firstLeft = points.Single(p => p.NickName == "M03_N0 | Asse 1, ruota sinistra");
        var secondLeft = points.Single(p => p.NickName == "M03_N0 | Asse 2, ruota sinistra");
        var halfGauge = Source(firstLeft, "Y"); var front = Source(firstLeft, "X"); var rear = Source(secondLeft, "X"); var axes = Source(firstLeft, "Axes");
        var vector = (GH_Component)Source(firstLeft, "Force").Attributes.GetTopLevel.DocObject;
        var fx = vector.Params.Input[0].Sources.Single(); var fy = vector.Params.Input[1].Sources.Single();
        var wheel = ((GH_Component)vector.Params.Input[2].Sources.Single().Attributes.GetTopLevel.DocObject).Params.Input[0].Sources.Single();
        wheel.NickName = wheel.Name = "TRAFFICO | Forza verticale per ruota, tandem corsia 1 [N] — assi 2 × 300 kN";
        cv.At(vehicle);
        var wheel2 = cv.Slider("TRAFFICO | Forza verticale per ruota, tandem corsia 2 [N] — assi 2 × 200 kN", 100000, 0, 300000);
        var lane2 = cv.Slider("TRAFFICO | Distanza dell'asse della corsia 2 dall'asse della corsia 1, verso destra [mm]", 3000, 0, 6000);
        var y2 = cv.Neg("M03_N0 | Corsia 2 — asse nel veicolo [mm]", lane2);
        var left2 = cv.Sum("M03_N0 | Corsia 2 — Y ruota sinistra [mm]", y2, halfGauge); var right2 = cv.Sub("M03_N0 | Corsia 2 — Y ruota destra [mm]", y2, halfGauge);
        var force2 = cv.Vector("M03_N0 | Corsia 2 — vettore forza per ruota [N]", fx, fy,
            cv.Neg("M03_N0 | Corsia 2 — forza verso Z globale negativo", wheel2));
        var loads = In(vehicle, "Loads");
        foreach (int a in new[] { 0, 1 })
        foreach (int side in new[] { 0, 1 })
        {
            var p = cv.Bridge("MovingPointComponent", $"M03_N0 | Corsia 2, asse {a + 1}, ruota {(side == 0 ? "sinistra" : "destra")}");
            Wire(In(p, "X"), a == 0 ? front : rear); Wire(In(p, "Y"), side == 0 ? left2 : right2); Wire(In(p, "Force"), force2); Wire(In(p, "Axes"), axes);
            loads.AddSource(p.Params.Output[0]);
        }
        vehicle.NickName = "M03_N0 | Veicolo schema 1 — tandem delle corsie 1 e 2, 8 ruote";
        var name = In(vehicle, "Name").Sources.Single();
        name.NickName = name.Name = "TRAFFICO | Nome veicolo — tandem dello schema 1, corsie 1 e 2"; ((GH_Panel)name).UserText = "SCHEMA1_TANDEM_CORSIE_1_2";
        var path = doc.Objects.OfType<GH_Component>().Single(c => c.Name == "Bridge Straight Moving Path");
        var deck = doc.Objects.OfType<GH_Component>().Single(c => c.Name == "Bridge Deck Widths");
        cv.At(path);
        var leftWidth = cv.Item("TRAFFICO | Semilarghezza sinistra dell'impalcato [mm]", Out(deck, "Side Widths"), s.Zero, cv.Toggle("TRAFFICO | Semilarghezza sinistra | Wrap", false));
        var offset = cv.Sub("M03_N0 | Asse della corsia 1 a sinistra del tracciato [mm]", leftWidth,
            cv.Slider("TRAFFICO | Distanza dell'asse della corsia 1 dal bordo sinistro [mm] — cordolo 700 + metà corsia 1500", 2200, 0, 10000));
        Wire(In(path, "Lane offset"), offset);
        foreach (var note in doc.Objects.OfType<GH_Panel>().Where(p => p.NickName == "IPOTESI TRAFFICO REV003").ToArray())
        {
        note.NickName = note.Name = "IPOTESI TRAFFICO REV004";
        note.UserText = "Tandem dello schema 1 come lo script di riferimento: corsia 1 assi 2 × 300 kN (4 ruote da 150 kN), corsia 2 assi 2 × 200 kN (4 ruote da 100 kN) a 3000 mm verso destra; "
            + "interasse 1200 mm, carreggiata 2000 mm, forze globali −Z, posizioni statiche alternative.\nAsse della corsia 1 a 2200 mm dal bordo sinistro (cordolo 700 mm + metà corsia). "
            + "I distribuiti dello schema 1 sono nel caso statico M03_N0_Q1_UDL, presente in ogni combinazione con una posizione.\nProprietà n0 dalla mappa Dead: nessun aggiornamento di fessurazione al passaggio del veicolo. "
            + "UNA posizione per combinazione; coefficienti dimostrativi, nessuna combinazione normativa automatica.";
        }
    }

    private static void Combinations(GH_Document doc)
    {
        var table = TablePanel(doc);
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string Row(string name, Func<string, double> coefficient) => name + ";" + string.Join(";", Ids.Select(i => F(coefficient(i))));
        double G1(string i) => i == "M01_ACCIAIO_PESO" ? SteelFactor : 1;
        table.UserText = string.Join("\n", new[] {
            "Combinazione;" + string.Join(";", Ids),
            Row("TEST_M01_ACCIAIO", i => i.StartsWith("M01", StringComparison.Ordinal) ? G1(i) : 0),
            Row("TEST_M02_NINF", i => i.StartsWith("M02", StringComparison.Ordinal) ? 1 : 0),
            Row("TEST_M03_N0", i => i.StartsWith("M03", StringComparison.Ordinal) ? 1 : 0),
            Row("TEST_M04_NSH", i => i.StartsWith("M04", StringComparison.Ordinal) ? 1 : 0),
            Row("DEMO_CUMULATIVA", G1) });
    }

    /// <summary>The errors of an offline solve: only the cases of the girders of M02/M03, which wait for the Dead map of M01.</summary>
    private static string[] Errors(GH_Document doc)
    {
        var all = doc.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => (o, e))).ToArray();
        foreach (var (o, e) in all) Console.WriteLine($"REFERENCE LOADS: offline error {o.NickName}: {e}");
        var unexpected = all.Where(x => !(x.o is GH_Component c && c.Name == "Bridge Analysis Cases" && (c.NickName.StartsWith("M02_NINF") || c.NickName.StartsWith("M03_N0"))
            && x.e.StartsWith("Risultati assenti per il solutore selezionato:", StringComparison.Ordinal))).Select(x => x.o.NickName + ": " + x.e).ToArray();
        Require(unexpected.Length == 0, "unexpected errors:\n" + string.Join("\n", unexpected));
        return all.Select(x => x.o.NickName + ": " + x.e).ToArray();
    }

    /// <summary>Reopens the saved REV004 and checks it offline: values, trees, applied load resultants, traffic template, phases, combinations, layout.</summary>
    internal static List<object> Check(string path)
    {
        var report = new List<object>();
        using var doc = GrasshopperBridgeExample.Read(path); doc.FilePath = path;
        True(report, "riapertura: nessuna analisi automatica", doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis")
            .SelectMany(c => c.Params.Input.Where(p => p.Name is "Keep Running" or "Run")).SelectMany(p => p.Sources).OfType<GH_BooleanToggle>().All(t => !t.Value));
        True(report, "file Straus in calcoli/REV004", doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis")
            .SelectMany(c => c.Params.Input.Where(p => p.Name is "Output Path" or "Output path")).SelectMany(p => p.Sources).OfType<GH_Panel>().All(p => p.UserText.Contains(@"calcoli\REV004\")));
        int backward = BackwardWires(doc);
        report.Add(new { Check = "collegamenti da destra a sinistra", Count = backward, Pass = backward == 0 }); Require(backward == 0, "right-to-left wires");
        Neutralize(doc, "check-50");
        BridgeSegmentExcelTests.Solve(doc, true);
        foreach (var read in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeSectionReadComponent")) { BridgeSegmentExcelTests.Press(read); read.ExpireSolution(false); }
        BridgeSegmentExcelTests.Solve(doc, true);
        var errors = Errors(doc);
        report.Add(new { Check = "errori offline attesi (travi M02/M03 in attesa della mappa Dead di M01)", Errors = errors, Pass = true });
        // Offline the girders of M03 wait for the Dead map of M01: for the load resultants only, the girder chain of M03 reads in memory the
        // girders and the other steel members of M01 (same beams and lengths, steel properties). The saved file is not changed.
        var steel = Component(doc, "Bridge Segment Override", "M01_ACCIAIO | proprietà longitudinali");
        Wire(In(Component(doc, "Beam Distributed Load", "M03_N0 | Q3_FR frenatura — travi, X globale"), "Beam"), Out(steel, "Girders"));
        Wire(In(Component(doc, "Beam PreLoad", "M03_N0 | Q7_TEMP — traversi e controventi"), "Beam"), Out(steel, "Other Steel"));
        BridgeSegmentExcelTests.Solve(doc, true);
        report.Add(new { Check = "risultanti delle travi M03 lette sulle travi di M01, collegate in memoria", Pass = true });

        double N(string nick) => Number(Float(doc, nick));
        int n = (int)N("Numero travi longitudinali");
        var grillage = Component(doc, "Bridge Slab Grillage", "Fasce soletta FEM");
        var ids = Values(Out(grillage, "Member IDs")).Cast<string>().ToArray(); var widths = Values(Out(grillage, "Strip Widths")).Select(Convert.ToDouble).ToArray();
        True(report, $"fasce in ordine stazione → segmento ({ids.Length} fasce, {n + 1} per stazione)", ids.Length % (n + 1) == 0 && ids.Select((id, k) => id.EndsWith($":{k % (n + 1) + 1:D2}", StringComparison.Ordinal)).All(x => x));
        var partition = doc.Objects.OfType<GH_Component>().Single(c => c.NickName == "Fasce soletta per stazione — da destra a sinistra");
        True(report, "albero delle fasce: un ramo per stazione", partition.Params.Output[0].VolatileData.PathCount == ids.Length / (n + 1)
            && Enumerable.Range(0, partition.Params.Output[0].VolatileData.PathCount).All(b => partition.Params.Output[0].VolatileData.get_Branch(b).Count == n + 1));
        double length = widths.Sum() / (n + 1), alignment = Values(Out(doc.Objects.OfType<GH_Component>().Single(c => c.Name == "Bridge Span Stations"), "Alignment Length")).Select(Convert.ToDouble).Single();
        Near(report, "somma delle larghezze tributarie = lunghezza del tracciato [mm]", length, alignment, 1e-6);
        double spacing = N("Interasse regolare travi [mm]"), left = N("Sbalzo laterale sinistro soletta [mm]"), right = N("Sbalzo laterale destro soletta [mm]");
        double width = (n - 1) * spacing + left + right, curb = N("M02_NINF | G2 cordoli — larghezza di ciascun cordolo dal bordo [mm]");

        Totals Of(string nick) => LoadTotals(Goos(Out(doc.Objects.OfType<GH_Component>().Single(c => c.NickName == nick), "Beam")));
        var m01 = Of("M01_ACCIAIO | carico soletta"); var m02 = Of("M02_NINF | G2_FIN finiture — bordi");
        var m03s = Of("M03_N0 | Q7_TEMP — fasce di soletta"); var m03g = Of("M03_N0 | Q7_TEMP — travi principali"); var m03o = Of("M03_N0 | Q7_TEMP — traversi e controventi");
        Near(report, "M01 getto soletta Fz [N] = −γ t B L", m01.Of("M01_ACCIAIO_SOLETTA")[2], -25e-6 * N("Spessore soletta [mm]") * width * alignment);
        double gPav = N("M02_NINF | G2 pavimentazione — peso specifico [kN/m³]") * N("M02_NINF | G2 pavimentazione — spessore medio [mm]") * 1e-6;
        double gCord = N("M02_NINF | G2 cordoli — peso specifico [kN/m³]") * N("M02_NINF | G2 cordoli — altezza al netto della soletta [mm]") * 1e-6;
        {
            // Diagnosis of the strip patterns (printed): the inputs a, b, PA of the pavement load and the pieces on the first strips.
            var pav = doc.Objects.OfType<GH_Component>().Single(c => c.NickName == "M02_NINF | G2_PAV pavimentazione — fasce fra i cordoli");
            foreach (var input in new[] { "a", "b", "PA" })
            {
                var data = In(pav, input).VolatileData;
                Console.WriteLine($"DIAG G2_PAV {input}: {data.PathCount} rami; primo {string.Join(" ", data.get_Branch(0).Cast<IGH_Goo>().Select(g => g.ScriptVariable()))}; secondo {(data.PathCount > 1 ? string.Join(" ", data.get_Branch(1).Cast<IGH_Goo>().Select(g => g.ScriptVariable())) : "-")}");
            }
            var outputs = Out(pav, "Beam").VolatileData; Console.WriteLine($"DIAG G2_PAV Beam: {outputs.PathCount} rami, {outputs.DataCount} fasce");
            foreach (var goo in Goos(Out(pav, "Beam")).Skip(4).Take(4))
            {
                var beam = (GPC.Model.Elements.BeamElement)goo.GetType().GetProperty("Gpc").GetValue(goo).GetType().GetProperty("Beam").GetValue(goo.GetType().GetProperty("Gpc").GetValue(goo));
                Console.WriteLine($"DIAG strip {beam.Name}: L={beam.Length:0.#} start=({beam.StartPoint.X:0},{beam.StartPoint.Y:0}) end=({beam.EndPoint.X:0},{beam.EndPoint.Y:0}) loads="
                    + string.Join(" | ", beam.Assignments.Loads.Select(l => $"{l.StartIntensity?.LoadCaseName ?? l.Concentrated?.LoadCaseName} [{l.Start:0.###},{l.End:0.###}] {l.StartIntensity?.GetGlobalForces()[2]:0.####}")));
            }
        }
        Near(report, "M02 G2_PAV Fz [N] = −γ h (B − 2c) L", m02.Of("M02_NINF_G2_PAV")[2], -gPav * (width - 2 * curb) * alignment);
        Near(report, "M02 G2_CORD Fz [N] = −γ h 2c L", m02.Of("M02_NINF_G2_CORD")[2], -gCord * 2 * curb * alignment);
        Near(report, "M02 G2_GR Fz [N] = −2 q L", m02.Of("M02_NINF_G2_GR")[2], -2 * N("M02_NINF | G2 guard-rail — carico di ciascuno [kN/m]") * alignment);
        Near(report, "M02 G2_PAR Fz [N] = −2 q L", m02.Of("M02_NINF_G2_PAR")[2], -2 * N("M02_NINF | G2 parapetto — carico di ciascuno [kN/m]") * alignment);
        Near(report, "M02 G2_FIN Fz [N] = −2 q L", m02.Of("M02_NINF_G2_FIN")[2], -2 * N("M02_NINF | G2 finiture (velette, impianti) — carico per bordo [kN/m]") * alignment);
        Near(report, "M02 G2: nessuna forza orizzontale", m02.Force.Values.Sum(f => Math.Abs(f[0]) + Math.Abs(f[1])), 0);
        // Moment of G2_GR about the alignment: the two barriers are symmetric for a symmetric deck.
        double q1 = N("M03_N0 | Q1 schema 1 — carico distribuito corsia 1 q1k [kN/m²]") * 1e-3, qr = N("M03_N0 | Q1 schema 1 — carico distribuito altre corsie e area rimanente [kN/m²]") * 1e-3;
        Near(report, "M03 Q1_UDL Fz [N] = −(q1k 3 m + qrk (B − 2c − 3 m)) L", m03s.Of("M03_N0_Q1_UDL")[2], -(q1 * 3000 + qr * (width - 2 * curb - 3000)) * alignment, 1e-6); // fractions written with 6 decimals
        double qlk = Math.Min(900, Math.Max(180, 0.6 * 2 * N("M03_N0 | Q3 frenatura — carico asse del tandem corsia 1 Q1k [kN]") + 0.1 * q1 * 1e3 * 3 * alignment / 1000)) * 1000;
        Near(report, "M03 Q3_FR Fx [N] = Q1k,frenatura (180–900 kN)", m03g.Of("M03_N0_Q3_FR")[0], qlk);
        Near(report, "M03 Q4_CENTR Fy [N] = n q L", m03g.Of("M03_N0_Q4_CENTR")[1], n * N("M03_N0 | Q4 centrifuga — su ciascuna trave [kN/m = N/mm]") * alignment);
        double wind = N("M03_N0 | Q5 vento — pressione [kN/m²]") * (N("M03_N0 | Q5 vento — altezza del veicolo [mm]") + N("Sezione travi principali | Altezza totale [mm]") + N("Spessore soletta [mm]")) * 1e-3;
        var v = m03g.Of("M03_N0_Q5_VENTO");
        Near(report, "M03 Q5_VENTO Fy [N] = −p H L", v[1], -wind * alignment);
        Near(report, "M03 Q5_VENTO Fx [N] = 0,25 p H L", v[0], N("M03_N0 | Q5 vento — quota longitudinale") * wind * alignment);
        Near(report, "M03 Q5_VENTO Fz [N] = −0,25 p H L", v[2], -N("M03_N0 | Q5 vento — quota verticale") * wind * alignment);
        int strips = ids.Length, girders = Values(Out(steel, "Girders")).Length;
        True(report, $"M03 Q7_TEMP: deformazione impressa su {strips} fasce, {girders} travi e tutte le altre aste d'acciaio",
            m03s.Strains.GetValueOrDefault("M03_N0_Q7_TEMP") == strips && m03g.Strains.GetValueOrDefault("M03_N0_Q7_TEMP") == girders && m03o.Strains.GetValueOrDefault("M03_N0_Q7_TEMP") > 0);
        // Traffic template.
        var movingPath = Values(Out(doc.Objects.OfType<GH_Component>().Single(c => c.Name == "Bridge Straight Moving Path"), "Path")).OfType<MovingPath>().Single();
        var wheels = movingPath.Template.Vehicles.Single().Loads.Cast<MovingPointForce>().ToArray();
        True(report, "tandem schema 1: 4 ruote da 150 kN e 4 da 100 kN, forze globali −Z", wheels.Length == 8 && wheels.Count(l => l.Force[2] == -150000) == 4 && wheels.Count(l => l.Force[2] == -100000) == 4 && wheels.All(l => l.Axis == 1));
        True(report, "corsia 2 a 3000 mm verso destra della corsia 1", wheels.Where(l => l.Force[2] == -100000).Select(l => l.Y).OrderBy(y => y).SequenceEqual(new[] { -4000d, -4000, -2000, -2000 }));
        double lane1 = (n - 1) * spacing / 2 + left - N("TRAFFICO | Distanza dell'asse della corsia 1 dal bordo sinistro [mm] — cordolo 700 + metà corsia 1500");
        Near(report, "asse della corsia 1 a sinistra del tracciato [mm]",
            Number(doc.Objects.OfType<GH_Component>().Single(c => c.NickName == "M03_N0 | Asse della corsia 1 a sinistra del tracciato [mm]").Params.Output[0]), lane1);
        True(report, "percorso mobile: 30 divisioni, casi statici", movingPath.Divisions == 30 && movingPath.ConvertToStaticCases && movingPath.Target == 2);
        // Phases and combinations.
        var phaseIds = doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Bridge Action").Select(c => Values(In(c, "Action ID")).Cast<string>().ToArray()).SelectMany(x => x).Where(x => !x.StartsWith("Q_MOV")).OrderBy(x => x).ToArray();
        True(report, "azioni statiche delle fasi = colonne della tabella", phaseIds.SequenceEqual(Ids.Where(i => i != "M04_NSH_RITIRO").OrderBy(x => x)));
        var header = TablePanel(doc).UserText.Replace("\r", "").Split('\n')[0].Split(';');
        True(report, "intestazione delle combinazioni", header.Skip(1).SequenceEqual(Ids));
        foreach (var (phase, count) in new[] { ("M01_ACCIAIO | fase di test", 3), ("M02_NINF | fase di test", 5), ("M03_N0 | fase di test", 6) })
            True(report, $"{phase}: {count} ingressi di azioni", In(Component(doc, "Bridge Phase", phase), "Actions").SourceCount == count);
        return report;
    }

    private static T Let<T>(this T value, Action<T> action) { action(value); return value; }

    /// <summary>The panel of the combination table: the text source of the Text Split read by the moving combinations.</summary>
    private static GH_Panel TablePanel(GH_Document doc)
    {
        var combinations = doc.Objects.OfType<GH_Component>().Single(c => c.Name == "Bridge Moving Combinations");
        var split = (GH_Component)In(combinations, "Base table").Sources.Single().Attributes.GetTopLevel.DocObject;
        return split.Params.Input[0].Sources.OfType<GH_Panel>().Single();
    }
}
