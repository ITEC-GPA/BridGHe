using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.MovingLoads;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using static ExampleReferenceLoads;

/// <summary>
/// Example 99 REV005 from REV004: the loads of the four models follow the company reference script (GH04).
/// G1 (phase 10, steel): self-weight of the steel (Dead, × 1.15 in the combinations) and casting of the slab per girder (half the deck each,
/// the girder model has no slab strips). G2 (phase 20, n∞): pavement, curbs, safety barriers, parapets and finishes on the slab strips of the
/// G2 model, in the FEM combination G2_PERMANENTI as in REV004. RITIRO (phase 30) as REV004. Q (phase 40, n0): LM1 lane loads on the strips,
/// wind on the girders, braking, centrifugal force and temperature; the LM1 tandem of lane 1 moves on its own path (phase 45).
/// </summary>
internal static class ExampleReferenceLoads99
{
    internal static string Folder => Path.Combine(ExamplePaths.Root, "99-Ponte completo GPC");
    internal static string Source => Path.Combine(Folder, "REV004", "flusso-acciaio-misto_REV004.ghx");
    internal static string Target => Path.Combine(Folder, "REV005", "flusso-acciaio-misto_REV005.ghx");
    internal static string Workbook => Path.Combine(Folder, "REV005", "Combinazioni_REV005.xlsx");
    internal static readonly string[] ExcelIds = { "G1_ACCIAIO", "G1", "G2", "RITIRO", "Q", "WIND", "FRENATURA", "CENTRIFUGA", "TEMPERATURA" };
    internal static readonly string[] G2Cases = { "G2_PAV", "G2_CORD", "G2_GR", "G2_PAR", "G2_FIN" };

    private static GH_Component Id(GH_Document doc, string guid) =>
        doc.FindObject(new Guid(guid), true) as GH_Component ?? throw new InvalidOperationException("Missing component " + guid);
    private static IGH_Param IdParam(GH_Document doc, string guid) =>
        doc.FindObject(new Guid(guid), true) as IGH_Param ?? throw new InvalidOperationException("Missing parameter " + guid);

    // REV004 objects (instance GUIDs of the saved user definition).
    private const string G1Load = "7366b76a-6b68-4819-8867-c47e68bc48bb", G1Straus = "2c52577a-da63-4e52-a6c9-57c0f9a8a0b7", G1Cases = "8fcdb6e8-6454-496e-87d6-e5fed82fefa9",
        G1Phase = "aee86ebf-20ce-4ae6-8fc7-294acf5ffbaa", G2Build = "a2b57f7a-764c-44e1-b68e-6b71ee7b8b84", G2Straus = "8187f478-4f1f-4b1d-91ef-51b19851099e",
        G2Load1 = "ab711c5b-b140-4e40-93a4-5a19b3d3ffd1", G2Load3 = "b507014d-dff3-42c0-a670-42090d6ec294", G2Combination = "0144c7e6-33d2-424c-bbad-19fd88d600b8",
        G2Coefficients = "100291f4-537e-442e-952b-724a2a80ac83", G2Strips = "16b2c5e3-f7fe-401c-86e6-0bdb58a87e4a",
        QBuild = "f57d490b-f486-4a2f-8764-2b5d9f3c9c42", QStraus = "0f593523-bfa4-4c07-b68d-fda28f451c7b", QCases = "3bbd0af2-8d11-450d-91f7-bad09c022798",
        QPhase = "36f18358-3141-4ba4-b908-dd34a3482148", QTraffic = "269800c2-cd9b-4770-a437-0fc7a06d297f", QWind = "795a6449-b687-44ff-b0e2-a7cfb69026af",
        QTrafficCase = "8dac2c9c-6022-4b9f-980b-17103b052462", QWindCase = "7c1dd262-3cdb-40d9-8355-57aa1b4f2cad", QStrips = "c51a03ba-27b2-4d89-9017-44b6908b096a",
        QResults = "70ab4d31", Others = "e3638efa-0be6-4fd7-b5d8-3cb34a3ba283", Grillage = "55c65698-a941-4227-9608-46b1b12f7c9c",
        Merge = "8394121a-76b9-41a6-988a-a4599c7dbf73", ExcelCreate = "404be398-1fb8-4f7e-9ac6-8364dc315e6e", ExcelRead = "cd8f3cb4-0d78-494f-992d-367f7816d8e4",
        Thickness = "3d534111", SideWidth = "5784144e", Offsets = "2f499dc1", GirderHeight = "9c8132a5";

    private static IGH_Param Prefix(GH_Document doc, string prefix) => doc.Objects.OfType<IGH_Param>().Single(p => p.InstanceGuid.ToString().StartsWith(prefix, StringComparison.Ordinal));

    private sealed class Strips { internal IGH_Param Zero, One, Zeros, Global, Schema, DirX, DirY, DirZ, Dead, N, Right, Left, Curb, Widths; internal double[] Offsets; internal double CurbValue; }

    internal static void Generate()
    {
        Require(!File.Exists(Target), "REV005 already exists; use --check (a revision is never overwritten).");
        byte[] original = File.ReadAllBytes(Source);
        Directory.CreateDirectory(Path.GetDirectoryName(Target));
        foreach (var file in new[] { "Template_BridGHe.docx" })
        {
            string from = Path.Combine(Folder, "REV004", file), to = Path.Combine(Folder, "REV005", file);
            if (!File.Exists(to)) File.Copy(from, to); else Require(File.ReadAllBytes(from).SequenceEqual(File.ReadAllBytes(to)), file + " differs from REV004");
        }
        Require(!File.Exists(Workbook), "Combinazioni_REV005.xlsx already exists");
        using var doc = GrasshopperBridgeExample.Read(Source); doc.FilePath = Target;
        var cv = new Canvas(doc);
        Prepare(doc);
        var s = Shared(doc, cv);
        var replaced = new HashSet<string>(StringComparer.Ordinal) { G2Load1, "b54e94c9-1b23-4905-8c75-2230d54ba87a", G2Load3, "1ff8aad4-6122-41ab-b0a6-f3a8e61b1c15",
            "b606251e-464c-4fef-8337-46eecb4b49f0", "02c08bf0-98a4-4e28-baf7-6d07ca016a22", "0f58c1fe-43b2-4905-a03f-c28fe9053da6", "6507f127-6a61-44f1-8490-15d687ebb1db",
            "0e7d007d-4ed7-4bd9-96d2-17f0e94ef815", QTraffic, QWind };
        Steel(doc, cv, s);
        LongTerm(doc, cv, s);
        ShortTerm(doc, cv, s);
        Traffic(doc, cv, s);
        cv.ExposeDefaults();
        var removed = RemoveDetached(doc, replaced);
        var restore = Neutralize(doc, "generate-99");
        BridgeSegmentExcelTests.Solve(doc, true);
        WriteWorkbook(doc);
        Errors(doc);
        GH_Document.EnableSolutions = false; restore();
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis")) BridgeMovingRevisionExample.Clear(c);
        ExampleTidy.ArrangeModels(doc); ExampleTidy.Straighten(doc);
        GH_Document.EnableSolutions = false; GrasshopperBridgeExample.Write(doc, Target);
        var report = Check(Target);
        Require(original.SequenceEqual(File.ReadAllBytes(Source)), "REV004 changed.");
        File.WriteAllText(Path.Combine(Folder, "REV005", "validation-reference-loads.json"), JsonSerializer.Serialize(new {
            Mode = "offline: reference-script phase loads, no Straus analysis", Generator = "--example-reference-loads 99",
            Source = "REV004/" + Path.GetFileName(Source), SourceSHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)),
            Definition = Path.GetFileName(Target), DefinitionSHA256 = Sha(Target), WorkbookSHA256 = Sha(Workbook), Removed = removed, Checks = report,
            NativeSolverInvoked = false }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine("REV005 GENERATED: " + Target);
    }

    private static void Prepare(GH_Document doc)
    {
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis"))
        {
            BridgeMovingRevisionExample.Clear(c);
            foreach (var p in In(c, "Keep Running").Sources.OfType<GH_BooleanToggle>()) p.Value = false;
        }
        foreach (var p in doc.Objects.OfType<GH_Panel>()) { p.UserText = p.UserText.Replace("REV004", "REV005"); if (p.NickName.Contains("REV004")) p.NickName = p.Name = p.NickName.Replace("REV004", "REV005"); }
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.NickName.Contains("REV004"))) c.NickName = c.NickName.Replace("REV004", "REV005");
    }

    private static Strips Shared(GH_Document doc, Canvas cv)
    {
        var grillage = Id(doc, Grillage);
        var offsets = Prefix(doc, Offsets); var side = Prefix(doc, SideWidth);
        var offsetValues = ((GH_Panel)offsets).UserText.Split('\n').Select(t => double.Parse(t.Trim(), CultureInfo.InvariantCulture)).ToArray();
        double w = Number(side);
        var s = new Strips { Offsets = new[] { -w }.Concat(offsetValues).Append(w).ToArray(), CurbValue = 700 };
        cv.At(grillage);
        s.One = cv.Text("Costante 1 — fasce e frazioni", "1"); s.Zero = cv.Text("Costante 0 — fasce o frazioni scariche", "0");
        s.N = cv.Length("Numero travi = offset trasversali", offsets);
        var interior = cv.Sub("Fasce interne per stazione = travi − 1", s.N, s.One);
        s.Zeros = cv.Duplicate("Zeri delle fasce interne", s.Zero, interior, cv.Toggle("Duplicazione in ordine", true));
        s.Right = cv.Sum("Sbalzo destro [mm] = semilarghezza + offset della prima trave", side, cv.Item("Offset della prima trave [mm]", offsets, s.Zero, cv.Toggle("Offset prima trave | Wrap", false)));
        s.Left = cv.Sub("Sbalzo sinistro [mm] = semilarghezza − offset dell'ultima trave", side, cv.Item("Offset dell'ultima trave [mm]", offsets, cv.Text("Indice ultima trave", "-1"), cv.Toggle("Offset ultima trave | Wrap", true)));
        var widths = grillage.Params.Output.Single(p => p.Name == "Deck Strip Widths");
        s.Widths = widths;
        s.Schema = cv.Text("Schema carico distribuito: 0 = uniforme su [a, 1 − b]", "0");
        s.Global = cv.Control("Sistema di riferimento dei carichi — Global", (IGH_Param)Activator.CreateInstance(In(Id(doc, G1Load), "Coordinate System").GetType()));
        s.Global.GetType().GetMethod("SetGlobal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(s.Global, null);
        s.DirX = cv.Text("Direzione globale X: 0", "0"); s.DirY = cv.Text("Direzione globale Y: 1", "1"); s.DirZ = cv.Text("Direzione globale Z: 2", "2");
        return s;
    }

    /// <summary>Strip trees of one model: one branch per station, segments from right to left; the tributary widths of the mesh
    /// (one per strip, paths {station;strip}) flattened and partitioned the same way.</summary>
    private static (IGH_Param Beams, IGH_Param Widths, IGH_Param EdgeWidths) StripTrees(Canvas cv, Strips s, IGH_Param strips, string model, bool edgeLoads = true)
    {
        var perStation = cv.Sum(model + " | fasce per stazione = travi + 1", s.N, s.One);
        var beams = cv.Partition(model + " | fasce soletta per stazione — da destra a sinistra", strips, perStation);
        var widths = cv.Partition(model + " | larghezze tributarie delle fasce per stazione [mm]", s.Widths, perStation);
        ((GH_Component)widths.Attributes.GetTopLevel.DocObject).Params.Input[0].DataMapping = GH_DataMapping.Flatten;
        if (!edgeLoads) return (beams, widths, null);
        var edges = cv.Merge(model + " | maschera sbalzi [1, 0…, 1]", s.One, s.Zeros, s.One);
        var edgeWidths = cv.Mul(model + " | larghezze tributarie, solo sbalzi [mm]", widths, edges);
        return (beams, widths, edgeWidths);
    }

    /// <summary>Phase 10: casting per girder by the reference rule (slab weight over the deck width, n girders), Dead as G1_ACCIAIO.</summary>
    private static void Steel(GH_Document doc, Canvas cv, Strips s)
    {
        var load = Id(doc, G1Load); var straus = Id(doc, G1Straus); var cases = Id(doc, G1Cases); var phase = Id(doc, G1Phase);
        cv.At(load);
        var gamma = cv.Slider("G1 getto — peso specifico del cls [kN/m³]", 25, 0, 30, 2);
        var width = cv.Mul("G1 getto — larghezza dell'impalcato [mm] = 2 × semilarghezza", Prefix(doc, SideWidth), cv.Text("G1 getto — due semilarghezze", "2"));
        var perGirder = cv.Div("G1 getto — carico per trave [N/mm] (metà impalcato per trave)", cv.Mul("G1 getto — γ t B [kN/m³ × mm²]", cv.Mul("G1 getto — γ t", gamma, Prefix(doc, Thickness)), width), s.N);
        Wire(In(load, "PA"), cv.Mul("G1 getto — PA [N/mm] verso il basso", perGirder, cv.Text("G1 getto — conversione kN/m³ × mm² → N/mm, verso il basso", "-0.000001")));
        var dead = cv.Text("G1_ACCIAIO | caso FEM: Dead (peso proprio)", "Dead");
        var action = cv.Bridge("BridgeActionComponent", "G1_ACCIAIO — peso proprio dell'acciaio");
        Wire(In(action, "Action ID"), cv.Text("G1_ACCIAIO | identificativo azione", "G1_ACCIAIO")); Wire(In(action, "Model"), Out(straus, "Model"));
        Wire(In(action, "Case"), dead); Wire(In(action, "Behavior"), cv.Text("G1_ACCIAIO | comportamento: 0 = solo acciaio", "0"));
        var actions = In(phase, "Actions"); Wire(actions, new[] { Out(action, "Action") }.Concat(actions.Sources).ToArray());
        In(straus, "Analysis Results").AddSource(dead); In(cases, "Required Cases").AddSource(dead);
        s.Dead = dead;
        var note = (GH_Panel)cv.Text("NOTA G1 — script di riferimento aziendale",
            "Fase solo acciaio. G1_ACCIAIO = peso proprio delle carpenterie (caso Dead); il fattore 1,15 delle connessioni è il suo coefficiente nell'Excel delle combinazioni.\n"
            + "G1 = getto della soletta: 25 kN/m³ × 300 mm × 5000 mm / 2 travi = 18,75 N/mm per trave. Il modello G1 non ha fasce di soletta: per due travi e sbalzi uguali la regola delle fasce dello script dà metà impalcato per trave.");
        note.Properties.Multiline = true;
    }

    /// <summary>Phase 20: the five G2 cases on the strips of the G2 model, summed in G2_PERMANENTI.</summary>
    private static GH_Component[] LongTerm(GH_Document doc, Canvas cv, Strips s)
    {
        var build = Id(doc, G2Build); var straus = Id(doc, G2Straus); var first = Id(doc, G2Load1); var last = Id(doc, G2Load3);
        var girders = In(first, "Beam").Sources.Single(); var elements = In(build, "Elements"); var strips = Out(Id(doc, G2Strips), "Beams");
        cv.At(first);
        var (beams, widths, edgeWidths) = StripTrees(cv, s, strips, "G2");
        var curb = cv.Slider("G2 cordoli — larghezza di ciascun cordolo dal bordo [mm]", 700, 0, 1000);
        var rR = cv.Div("G2 | cordolo / sbalzo destro", curb, s.Right); var rL = cv.Div("G2 | cordolo / sbalzo sinistro", curb, s.Left);
        var cR = cv.Sub("G2 | 1 − cordolo / sbalzo destro", s.One, rR); var cL = cv.Sub("G2 | 1 − cordolo / sbalzo sinistro", s.One, rL);
        var edgeL = cv.Text("G2 | bordo sinistro sulla fascia: a = 0,999999 (il componente non accetta a = 1)", "0.999999");
        var toPressure = cv.Text("G2 | conversione kN/m³ × mm → N/mm², verso −Z", "-0.000001"); var down = cv.Text("G2 | conversione kN/m → N/mm, verso −Z", "-1");
        var pPav = cv.Mul("G2 pavimentazione — pressione [N/mm²]", cv.Mul("G2 pavimentazione — γ × h", cv.Slider("G2 pavimentazione — peso specifico [kN/m³]", 25, 0, 30, 2),
            cv.Slider("G2 pavimentazione — spessore medio [mm]", 150, 0, 400)), toPressure);
        var pCord = cv.Mul("G2 cordoli — pressione [N/mm²]", cv.Mul("G2 cordoli — γ × h", cv.Slider("G2 cordoli — peso specifico [kN/m³]", 25, 0, 30, 2),
            cv.Slider("G2 cordoli — altezza al netto della soletta [mm]", 200, 0, 600)), toPressure);
        var qGr = cv.Mul("G2 guard-rail — carico per lato [N/mm]", cv.Slider("G2 guard-rail — carico di ciascuno [kN/m]", 1.5, 0, 10, 2), down);
        var qPar = cv.Mul("G2 parapetto — carico per lato [N/mm]", cv.Slider("G2 parapetto — carico di ciascuno [kN/m]", 1, 0, 10, 2), down);
        var qFin = cv.Mul("G2 finiture — carico per bordo [N/mm]", cv.Slider("G2 finiture (velette, impianti) — carico per bordo [kN/m]", 1.5, 0, 10, 2), down);
        GH_Component Case(string name)
        {
            var lc = cv.Bridge("LoadCaseComponent", "G2 | caso " + name); Wire(In(lc, "Name"), cv.Text("G2 | nome caso " + name, name)); return lc;
        }
        var cases = G2Cases.Select(Case).ToArray();
        var d1 = Distributed(cv, "G2_PAV pavimentazione — fasce fra i cordoli", beams, cases[0], s.Schema, s.Global, s.DirZ, cv.Mul("G2_PAV q per fascia [N/mm]", widths, pPav),
            new[] { cv.Merge("G2_PAV a/L per fascia [destra → sinistra]", rR, s.Zeros, s.Zero) }, new[] { cv.Merge("G2_PAV b/L per fascia", s.Zero, s.Zeros, rL) });
        var d2 = Distributed(cv, "G2_CORD cordoli — sbalzi", Out(d1, "Beam"), cases[1], s.Schema, s.Global, s.DirZ, cv.Mul("G2_CORD q per fascia [N/mm]", edgeWidths, pCord),
            new[] { cv.Merge("G2_CORD a/L per fascia [destra → sinistra]", s.Zero, s.Zeros, cL) }, new[] { cv.Merge("G2_CORD b/L per fascia", cR, s.Zeros, s.Zero) });
        var p1 = Point(cv, "G2_GR guard-rail — bordo interno dei cordoli", Out(d2, "Beam"), cases[2], s.Global, s.Zero, cv.Mul("G2_GR forza per fascia [N]", edgeWidths, qGr),
            new[] { cv.Merge("G2_GR a/L per fascia [destra → sinistra]", rR, s.Zeros, cL) });
        var edgeA = cv.Merge("G2 bordi a/L per fascia [destra → sinistra]", s.Zero, s.Zeros, edgeL);
        var p2 = Point(cv, "G2_PAR parapetti — bordi", Out(p1, "Beam"), cases[3], s.Global, s.Zero, cv.Mul("G2_PAR forza per fascia [N]", edgeWidths, qPar), new[] { edgeA });
        var p3 = Point(cv, "G2_FIN finiture — bordi", Out(p2, "Beam"), cases[4], s.Global, s.Zero, cv.Mul("G2_FIN forza per fascia [N]", edgeWidths, qFin), new[] { edgeA });
        Replace(elements, Out(last, "Beam"), girders); Replace(elements, strips, Out(p3, "Beam")); elements.DataMapping = GH_DataMapping.Flatten;
        Redirect(doc, Out(last, "Beam"), girders);
        var combination = Id(doc, G2Combination);
        var merged = cv.Merge("G2 | casi della combinazione G2_PERMANENTI", cases.Select(c => Out(c, "LoadCase")).ToArray());
        Wire(In(combination, "LoadCases"), merged);
        Wire(Id(doc, G2Coefficients).Params.Input[1], cv.Length("G2 | numero dei casi G2", merged));
        var note = (GH_Panel)cv.Text("NOTA G2 — script di riferimento aziendale",
            "Lunga durata (n∞): G2 come lo script di riferimento, sulle fasce del modello G2 (pressione × larghezza tributaria), sommati nella combinazione FEM G2_PERMANENTI dell'azione G2.\n"
            + "G2_PAV 25 kN/m³ × 150 mm fra i cordoli; G2_CORD 25 kN/m³ × 200 mm su 700 mm per lato; G2_GR 1,5 kN/m per lato al bordo interno dei cordoli; G2_PAR 1,0 e G2_FIN 1,5 kN/m per bordo.");
        note.Properties.Multiline = true;
        return cases;
    }

    /// <summary>Phase 40: lane loads on the strips, wind on the girders (same case WIND_1), braking, centrifugal force and temperature.</summary>
    private static void ShortTerm(GH_Document doc, Canvas cv, Strips s)
    {
        var build = Id(doc, QBuild); var straus = Id(doc, QStraus); var cases = Id(doc, QCases); var phase = Id(doc, QPhase);
        var traffic = Id(doc, QTraffic); var windLoad = Id(doc, QWind); var trafficCase = Id(doc, QTrafficCase); var windCase = Id(doc, QWindCase);
        var girders = In(traffic, "Beam").Sources.Single(); var elements = In(build, "Elements"); var strips = Out(Id(doc, QStrips), "Beams");
        var others = Out(Id(doc, Others), "Others");
        cv.At(traffic);
        var (beams, widths, _) = StripTrees(cv, s, strips, "Q", false);
        const double lane = 3000; const string generated = "calcolato dal generatore per corsia 1 di 3 m al cordolo sinistro; aggiornare se cambia la sezione trasversale";
        double yLeft = s.Offsets[^1] - s.CurbValue, yRight = s.Offsets[0] + s.CurbValue;
        var b1 = Band(s.Offsets, yLeft - lane, yLeft); var br = Band(s.Offsets, yRight, yLeft - lane);
        var toPressure = cv.Text("Q | conversione kN/m² → N/mm², verso −Z", "-0.001");
        var q1 = cv.Slider("Q1 schema 1 — carico distribuito corsia 1 q1k [kN/m²]", 9, 0, 20, 2);
        var qr = cv.Slider("Q1 schema 1 — carico distribuito area rimanente [kN/m²]", 2.5, 0, 20, 2);
        var u1 = Distributed(cv, "Q_TRAFFICO schema 1 — area rimanente", beams, trafficCase, s.Schema, s.Global, s.DirZ,
            cv.Mul("Q1 rimanente q per fascia [N/mm]", cv.Mul("Q1 rimanente larghezze caricate [mm]", widths, cv.List("Q1 rimanente maschera per fascia [destra → sinistra] — " + generated, br.Mask)),
                cv.Mul("Q1 rimanente pressione [N/mm²]", qr, toPressure)),
            new[] { cv.List("Q1 rimanente a/L per fascia — " + generated, br.A) }, new[] { cv.List("Q1 rimanente b/L per fascia — " + generated, br.B) });
        var u2 = Distributed(cv, "Q_TRAFFICO schema 1 — corsia 1", Out(u1, "Beam"), trafficCase, s.Schema, s.Global, s.DirZ,
            cv.Mul("Q1 corsia 1 q per fascia [N/mm]", cv.Mul("Q1 corsia 1 larghezze caricate [mm]", widths, cv.List("Q1 corsia 1 maschera per fascia [destra → sinistra] — " + generated, b1.Mask)),
                cv.Mul("Q1 corsia 1 pressione [N/mm²]", q1, toPressure)),
            new[] { cv.List("Q1 corsia 1 a/L per fascia — " + generated, b1.A) }, new[] { cv.List("Q1 corsia 1 b/L per fascia — " + generated, b1.B) });
        GH_Component NewCase(string name)
        {
            var lc = cv.Bridge("LoadCaseComponent", "Q | caso " + name); Wire(In(lc, "Name"), cv.Text("Q | nome caso " + name, name)); return lc;
        }
        var frCase = NewCase("Q3_FRENATURA"); var centrCase = NewCase("Q4_CENTRIFUGA"); var tempCase = NewCase("Q7_TEMPERATURA");
        var strain = cv.Mul("Q7 deformazione impressa α ΔT", cv.Text("Q7 coefficiente di dilatazione α [1/°C] — acciaio e cls come lo script di riferimento", "0.000012"),
            cv.Slider("Q7 variazione termica uniforme ΔT [°C]", 35, -60, 60));
        var strainType = cv.Text("Q7 tipo di precarico: 1 = deformazione", "1");
        var ts = Strain(cv, "Q7_TEMPERATURA — fasce di soletta", Out(u2, "Beam"), tempCase, strainType, strain);
        Replace(elements, strips, Out(ts, "Beam"));
        cv.At(windLoad);
        var length = cv.Slider("Q3 frenatura — lunghezza caricata L [mm] (tracciato 12 + 12 m)", 24000, 0, 200000);
        var lengthM = cv.Mul("Q3 lunghezza caricata L [m]", length, cv.Text("Q3 conversione mm → m", "0.001"));
        var axle = cv.Slider("Q3 frenatura — carico asse del tandem corsia 1 Q1k [kN]", 300, 0, 600);
        var firstTerm = cv.Mul("Q3 0,6 × 2 Q1k [kN]", axle, cv.Text("Q3 0,6 × 2 assi (α = 1)", "1.2"));
        var second = cv.Mul("Q3 0,10 q1k w1 L [kN]", cv.Mul("Q3 q1k w1 L", cv.Mul("Q3 q1k w1 [kN/m]", q1, cv.Text("Q3 larghezza corsia 1 w1 [m]", "3")), lengthM), cv.Text("Q3 coefficiente 0,10", "0.1"));
        var braking = cv.Min("Q3 frenatura entro 180–900 kN [kN]", cv.Max("Q3 frenatura ≥ 180 kN", cv.Sum("Q3 frenatura prima dei limiti [kN]", firstTerm, second), cv.Text("Q3 limite inferiore [kN]", "180")),
            cv.Text("Q3 limite superiore [kN]", "900"));
        var qx = cv.Div("Q3 frenatura per trave [N/mm]", cv.Div("Q3 frenatura per unità di lunghezza [N/mm]", cv.Mul("Q3 frenatura [N]", braking, cv.Text("Q3 conversione kN → N", "1000")), length), s.N);
        var height = cv.Sum("Q5 altezza esposta [mm]", cv.Sum("Q5 veicolo + trave [mm]", cv.Slider("Q5 vento — altezza del veicolo [mm]", 3000, 0, 5000), Prefix(doc, GirderHeight)), Prefix(doc, Thickness));
        var w = cv.Div("Q5 vento per trave [N/mm]", cv.Mul("Q5 vento totale [N/mm]", cv.Mul("Q5 p × h", cv.Slider("Q5 vento — pressione [kN/m²]", 2, 0, 5, 2), height),
            cv.Text("Q5 conversione kN/m² × mm → N/mm", "0.001")), s.N);
        var zeroA = new[] { s.Zero };
        var g1 = Distributed(cv, "Q3_FRENATURA — travi, X globale", girders, frCase, s.Schema, s.Global, s.DirX, qx, zeroA, zeroA);
        var g2 = Distributed(cv, "Q4_CENTRIFUGA — travi, Y globale", Out(g1, "Beam"), centrCase, s.Schema, s.Global, s.DirY, cv.Slider("Q4 centrifuga — su ciascuna trave [kN/m = N/mm]", 0.01, 0, 10, 3), zeroA, zeroA);
        var g3 = Distributed(cv, "WIND_1 vento trasversale — travi, Y globale negativo", Out(g2, "Beam"), windCase, s.Schema, s.Global, s.DirY, cv.Neg("Q5 vento trasversale per trave [N/mm]", w), zeroA, zeroA);
        var g4 = Distributed(cv, "WIND_1 vento longitudinale — travi, X globale", Out(g3, "Beam"), windCase, s.Schema, s.Global, s.DirX,
            cv.Mul("Q5 vento longitudinale per trave [N/mm]", w, cv.Slider("Q5 vento — quota longitudinale", 0.25, 0, 1, 2)), zeroA, zeroA);
        var g5 = Distributed(cv, "WIND_1 vento verticale — travi, Z globale negativo", Out(g4, "Beam"), windCase, s.Schema, s.Global, s.DirZ,
            cv.Neg("Q5 vento verticale per trave [N/mm]", cv.Mul("Q5 vento verticale per trave, modulo [N/mm]", w, cv.Slider("Q5 vento — quota verticale", 0.25, 0, 1, 2))), zeroA, zeroA);
        var tg = Strain(cv, "Q7_TEMPERATURA — travi principali", Out(g5, "Beam"), tempCase, strainType, strain);
        var to = Strain(cv, "Q7_TEMPERATURA — traversi", others, tempCase, strainType, strain);
        Replace(elements, Out(windLoad, "Beam"), Out(tg, "Beam")); Replace(elements, others, Out(to, "Beam")); elements.DataMapping = GH_DataMapping.Flatten;
        Redirect(doc, Out(windLoad, "Beam"), Out(tg, "Beam")); Redirect(doc, Out(traffic, "Beam"), Out(tg, "Beam"));
        // New actions of phase 40 (short term n0).
        var actions = In(phase, "Actions");
        foreach (var (id, lc) in new[] { ("FRENATURA", frCase), ("CENTRIFUGA", centrCase), ("TEMPERATURA", tempCase) })
        {
            var action = cv.Bridge("BridgeActionComponent", id + " dal modello Q");
            Wire(In(action, "Action ID"), cv.Text(id + " | identificativo azione", id)); Wire(In(action, "Model"), Out(straus, "Model"));
            Wire(In(action, "Case"), Out(lc, "LoadCase")); Wire(In(action, "Behavior"), cv.Text(id + " | comportamento: 2 = breve termine n0", "2"));
            actions.AddSource(Out(action, "Action"));
            In(cases, "Required Cases").AddSource(Out(lc, "LoadCase"));
        }
        ((GH_Panel)Prefix(doc, QResults)).UserText = "Q_TRAFFICO\nWIND_1\nQ3_FRENATURA\nQ4_CENTRIFUGA\nQ7_TEMPERATURA";
        var note = (GH_Panel)cv.Text("NOTA Q — script di riferimento aziendale",
            "Breve durata (n0). Q_TRAFFICO: distribuiti dello schema 1 sulle fasce, corsia 1 di 3 m al cordolo sinistro 9 kN/m², area rimanente 2,5 kN/m²; il tandem 2 × 300 kN della corsia 1 è nell'analisi mobile (fase 45).\n"
            + "WIND_1: 2 kN/m² su veicolo 3 m + trave + soletta, ripartito sulle due travi: Y, 0,25 lungo X e 0,25 verso il basso. Q3 frenatura entro 180–900 kN lungo X sulle travi; Q4 centrifuga 0,01 kN/m per trave;\n"
            + "Q7 temperatura uniforme +35 °C come deformazione impressa α ΔT su travi, traversi e fasce. Nessun gradiente termico: lo script di riferimento definisce il caso senza carichi.");
        note.Properties.Multiline = true;
    }

    /// <summary>LM1 tandem of lane 1 on a moving path on the Q model; its positions form phase 45 and the moving combinations.</summary>
    private static void Traffic(GH_Document doc, Canvas cv, Strips s)
    {
        var build = Id(doc, QBuild); var merge = Id(doc, Merge); var read = Id(doc, ExcelRead);
        cv.At(Id(doc, QStraus));
        var wheel = cv.Slider("TRAFFICO | Forza verticale per ruota, tandem corsia 1 [N] — assi 2 × 300 kN", 150000, 0, 300000);
        var spacing = cv.Slider("TRAFFICO | Interasse longitudinale dei due assi [mm]", 1200, 100, 15000);
        var halfGauge = cv.Slider("TRAFFICO | Semicarreggiata ruote [mm] — distanza totale = 2 volte", 1000, 100, 2000);
        var rear = cv.Neg("TRAFFICO | Coordinata asse posteriore", spacing); var right = cv.Neg("TRAFFICO | Coordinata ruota destra", halfGauge);
        var force = cv.Vector("TRAFFICO | Vettore forza per ruota [N]", cv.Text("TRAFFICO | Fx per ruota [N]", "0"), cv.Text("TRAFFICO | Fy per ruota [N]", "0"), cv.Neg("TRAFFICO | Forza verso Z globale negativo", wheel));
        var front = cv.Text("TRAFFICO | Asse anteriore X nel veicolo [mm]", "0"); var axes = cv.Text("TRAFFICO | Assi delle forze: 1 = globali", "1");
        var points = new List<IGH_Param>();
        foreach (int a in new[] { 0, 1 })
        foreach (int side in new[] { 0, 1 })
        {
            var p = cv.Bridge("MovingPointComponent", $"TRAFFICO | Asse {a + 1}, ruota {(side == 0 ? "sinistra" : "destra")}");
            Wire(In(p, "X"), a == 0 ? front : rear); Wire(In(p, "Y"), side == 0 ? halfGauge : right); Wire(In(p, "Force"), force); Wire(In(p, "Axes"), axes);
            points.Add(p.Params.Output[0]);
        }
        var vehicle = cv.Bridge("MovingVehicleComponent", "TRAFFICO | Tandem dello schema 1 — corsia 1, 4 ruote");
        Wire(In(vehicle, "Name"), cv.Text("TRAFFICO | Nome veicolo", "SCHEMA1_TANDEM_CORSIA_1")); Wire(In(vehicle, "Loads"), points.ToArray()); In(vehicle, "Loads").DataMapping = GH_DataMapping.Flatten;
        Wire(In(vehicle, "Lanes"), cv.Text("TRAFFICO | Corsia del template", "1"));
        var template = cv.Bridge("MovingTemplateComponent", "TRAFFICO | Template della singola corsia");
        Wire(In(template, "Vehicles"), vehicle.Params.Output[0]); Wire(In(template, "Name"), cv.Text("TRAFFICO | Nome template", "REV005_SCHEMA1_TANDEM"));
        var path = cv.Bridge("BridgeStraightMovingPathComponent", "TRAFFICO | Percorso alla quota delle aste di soletta");
        Wire(In(path, "Grillage Geometry"), Id(doc, Grillage).Params.Output.Single(p => p.Name == "Grillage Geometry")); Wire(In(path, "Template"), template.Params.Output[0]);
        Wire(In(path, "Case"), cv.Text("TRAFFICO | Caso sorgente del percorso", "Q_MOV_N0"));
        Wire(In(path, "Lane offset"), cv.Sub("TRAFFICO | Asse della corsia 1 a sinistra del tracciato [mm]", Prefix(doc, SideWidth),
            cv.Slider("TRAFFICO | Distanza dell'asse della corsia 1 dal bordo sinistro [mm] — cordolo 700 + metà corsia 1500", 2200, 0, 5000)));
        Wire(In(path, "Divisions"), cv.Slider("TRAFFICO | Divisioni del percorso — affinare per convergenza", 24, 5, 200));
        var attach = cv.Bridge("AttachMovingPathsComponent", "TRAFFICO | Stesso FEM Q con percorso mobile");
        Wire(In(attach, "Model"), Out(build, "Model")); Wire(In(attach, "Paths"), Out(path, "Path"));
        var moving = cv.Bridge("MovingStaticAnalysisComponent", "TRAFFICO | ANALISI MOBILE — sole posizioni, senza peso proprio");
        Wire(In(moving, "Model"), Out(attach, "Model"));
        Wire(In(moving, "Output path"), cv.Text("TRAFFICO | File di analisi mobile", @"C:\Users\g.pacini\Documents\Repository\BridGHe\example\99-Ponte completo GPC\REV005\calcoli\Q_MOBILE.st7"));
        Wire(In(moving, "Run"), cv.Toggle("TRAFFICO | Analisi automatica — lasciare False, usare pulsante Run", false));
        var combinations = cv.Bridge("BridgeMovingCombinationsComponent", "Tabella combinazioni mobili — una posizione alla volta");
        Wire(In(combinations, "Base table"), Out(read, "Table")); Wire(In(combinations, "Moving cases"), Out(moving, "Cases"));
        Wire(In(combinations, "Base combination"), cv.Text("TRAFFICO | Combinazione base cui aggiungere una posizione", "DEMO_TOTALE"));
        Wire(In(combinations, "Traffic factor"), cv.Slider("TRAFFICO | Coefficiente esplicito del tandem — non normativo", 1, 0, 3, 3));
        Wire(In(combinations, "Isolated positions"), cv.Toggle("TRAFFICO | Aggiungere combinazioni di sola posizione mobile", false));
        var action = cv.Bridge("BridgeActionComponent", "TRAFFICO | Azioni del tandem — una per posizione");
        Wire(In(action, "Action ID"), Out(combinations, "Action IDs")); Wire(In(action, "Model"), Out(moving, "Model")); Wire(In(action, "Case"), Out(combinations, "Cases"));
        Wire(In(action, "Behavior"), cv.Text("TRAFFICO | Risposta: 2 = istantanea n0", "2"));
        var phase = cv.Bridge("BridgeConstructionPhaseComponent", "TRAFFICO | Fase 45 — tandem mobile n0");
        Wire(In(phase, "Number"), cv.Slider("TRAFFICO | Numero ordine fase", 45, 0, 100)); Wire(In(phase, "Name"), cv.Text("TRAFFICO | Nome fase", "Q_MOV tandem schema 1"));
        Wire(In(phase, "Section State"), cv.Text("TRAFFICO | Stato sezione: 1 = misto", "1")); Wire(In(phase, "Actions"), Out(action, "Action")); In(phase, "Actions").DataMapping = GH_DataMapping.Flatten;
        In(merge, "Models").AddSource(Out(moving, "Model")); In(merge, "Models").DataMapping = GH_DataMapping.Flatten;
        In(merge, "Phases").AddSource(Out(phase, "Phase"));
        Wire(In(merge, "Combinations"), Out(combinations, "Table"));
        var note = (GH_Panel)cv.Text("IPOTESI TRAFFICO REV005",
            "Tandem dello schema 1 come lo script di riferimento: corsia 1 assi 2 × 300 kN (4 ruote da 150 kN), interasse 1200 mm, carreggiata 2000 mm, forze globali −Z.\n"
            + "Asse della corsia 1 a 2200 mm dal bordo sinistro (cordolo 700 mm + metà corsia). La carreggiata di 3,6 m ha una sola corsia convenzionale; i distribuiti sono nel caso statico Q_TRAFFICO.\n"
            + "Le posizioni sono la fase 45 e le combinazioni mobili, ciascuna con DEMO_TOTALE + UNA posizione. L'Excel delle combinazioni contiene solo le azioni delle fasi 10–40.");
        note.Properties.Multiline = true;
    }

    /// <summary>The combination workbook of REV005: the same rows as REV004, the new actions with the coefficients of their group.</summary>
    private static void WriteWorkbook(GH_Document doc)
    {
        // Offline the analyses have no output: in memory only, the readers of every analysis read its built (not analysed) model, whose
        // GPC input gives the same source model and cases to the catalogue; the wires are restored before the definition is saved.
        var restore = new List<Action>();
        foreach (var a in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis").ToArray())
        {
            var analysed = Out(a, "Model"); var built = In(a, "Model").Sources.Single();
            foreach (var r in analysed.Recipients.ToArray()) { Replace(r, analysed, built); var reader = r; restore.Add(() => Replace(reader, built, analysed)); }
        }
        BridgeSegmentExcelTests.Solve(doc, true);
        var excel = Id(doc, ExcelCreate);
        var inputs = In(excel, "Models").VolatileData.AllData(true).Select(g => (BridGHe.Common.Gpc.GpcModels.Conversion)g.GetType().GetProperty("GpcInput").GetValue(g)).ToArray();
        var phases = In(excel, "Phases").VolatileData.AllData(true).Select(g => (BridGHe.Common.Checks.BridgePhaseDefinition)g.GetType().GetProperty("Value").GetValue(g)).ToArray();
        Console.WriteLine($"REFERENCE LOADS: workbook catalogue from {inputs.Length} built models and {phases.Length} phases");
        var catalog = BridGHe.Common.Diagnostics.BridgeCombinationCatalog.FromGpc(inputs, phases);
        foreach (var undo in restore) undo();
        Require(catalog.ActionIds.SequenceEqual(ExcelIds), "workbook actions: " + string.Join(", ", catalog.ActionIds));
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string[] Row(string name, double g1, double g2, double ritiro, double q, double wind, double traffic, double temperature) =>
            new[] { name, F(g1 * 1.15), F(g1), F(g2), F(ritiro), F(q), F(wind), F(traffic), F(traffic), F(temperature) };
        var table = new[] {
            new[] { "Combination" }.Concat(ExcelIds).ToArray(),
            Row("DEMO_GETTO", 1, 0, 0, 0, 0, 0, 0),
            Row("DEMO_PERMANENTI", 1, 1, 0, 0, 0, 0, 0),
            Row("DEMO_RITIRO", 1, 1, 1, 0, 0, 0, 0),
            Row("DEMO_TOTALE", 1, 1, 1, 1, 1, 1, 1),
            Row("DEMO_Q_ISOLATA", 0, 0, 0, 1, 0, 1, 0),
            Row("DEMO_AMPLIFICATA", 1.5, 1.5, 1, 1.5, 1.5, 1.5, 1.5) };
        BridGHe.Common.Diagnostics.BridgeCombinationWorkbook.Write(Workbook, catalog, 20, false, table);
        Console.WriteLine("REFERENCE LOADS: workbook " + Workbook + " with " + string.Join(", ", catalog.ActionIds));
    }

    private static string[] Errors(GH_Document doc)
    {
        var all = doc.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => (o, e))).ToArray();
        foreach (var (o, e) in all) Console.WriteLine($"REFERENCE LOADS: offline error {o.Name} | {o.NickName}: {e}");
        return all.Select(x => x.o.Name + " | " + x.o.NickName + ": " + x.e).ToArray();
    }

    internal static List<object> Check(string path)
    {
        var report = new List<object>();
        using var doc = GrasshopperBridgeExample.Read(path); doc.FilePath = path;
        True(report, "riapertura: nessuna analisi automatica", doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis")
            .SelectMany(c => c.Params.Input.Where(p => p.Name is "Keep Running" or "Run")).SelectMany(p => p.Sources).OfType<GH_BooleanToggle>().All(t => !t.Value));
        True(report, "file Straus in REV005\\calcoli", doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis")
            .SelectMany(c => c.Params.Input.Where(p => p.Name is "Output Path" or "Output path")).SelectMany(p => p.Sources).OfType<GH_Panel>().All(p => p.UserText.Contains(@"REV005\calcoli\")));
        int backward = BackwardWires(doc);
        report.Add(new { Check = "collegamenti da destra a sinistra", Count = backward, Pass = backward == 0 }); Require(backward == 0, "right-to-left wires");
        Neutralize(doc, "check-99");
        BridgeSegmentExcelTests.Solve(doc, true);
        BridgeMovingRevisionExample.Press(Id(doc, ExcelRead)); BridgeSegmentExcelTests.Solve(doc, true);
        var errors = Errors(doc);
        report.Add(new { Check = "errori offline (senza analisi Straus)", Errors = errors, Pass = true });
        var table = Id(doc, ExcelRead).Params.Output.Single(p => p.Name == "Table").VolatileData;
        if (table.PathCount > 0)
            True(report, "Excel REV005 letto dal componente: intestazione = azioni delle fasi 10–40, 6 combinazioni", table.PathCount == 7
                && table.get_Branch(0).Cast<object>().Select(g => ((IGH_Goo)g).ScriptVariable()).Cast<string>().Skip(1).SequenceEqual(ExcelIds));
        else
        {
            // Offline the reader may wait for analysed models: the workbook is read as a file (its cell texts).
            using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(Path.GetDirectoryName(path), "Combinazioni_REV005.xlsx"));
            string Sheet(string name) { using var r = new StreamReader(zip.GetEntry(name).Open()); return r.ReadToEnd(); }
            string combinations = Sheet("xl/worksheets/sheet1.xml"), actions = Sheet("xl/worksheets/sheet2.xml");
            True(report, "Excel REV005 (letto come file): nove azioni e sei combinazioni nel foglio Combinations, azioni nel foglio Actions",
                ExcelIds.All(id => combinations.Contains(">" + id + "<") && actions.Contains(">" + id + "<"))
                && new[] { "DEMO_GETTO", "DEMO_PERMANENTI", "DEMO_RITIRO", "DEMO_TOTALE", "DEMO_Q_ISOLATA", "DEMO_AMPLIFICATA" }.All(r => combinations.Contains(">" + r + "<")));
        }

        double N(string prefix) => Number(Prefix(doc, prefix));
        double side = N(SideWidth), thickness = N(Thickness), width = 2 * side;
        var offsets = ((GH_Panel)Prefix(doc, Offsets)).UserText.Split('\n').Select(t => double.Parse(t.Trim(), CultureInfo.InvariantCulture)).ToArray();
        int n = offsets.Length;
        // The strips of the G2 and Q models in station order, segments from right to left.
        foreach (var model in new[] { "G2", "Q" })
        {
            var tree = doc.Objects.OfType<GH_Component>().Single(c => c.NickName == model + " | fasce soletta per stazione — da destra a sinistra").Params.Output[0].VolatileData;
            bool ordered = Enumerable.Range(0, tree.PathCount).All(i =>
            {
                var branch = tree.get_Branch(i).Cast<object>().Select(g => Prop(Prop(g, "Gpc"), "Beam")).ToArray();
                if (branch.Length != n + 1) return false;
                return branch.Select((b, j) => ((GPC.Model.Elements.BeamElement)b)).Select((b, j) =>
                    Math.Abs(Math.Min(b.StartPoint.Y, b.EndPoint.Y) - offsets.Prepend(-side).ElementAt(j)) < 1 && Math.Abs(Math.Max(b.StartPoint.Y, b.EndPoint.Y) - offsets.Append(side).ElementAt(j)) < 1
                    && b.StartPoint.Y < b.EndPoint.Y).All(x => x);
            });
            True(report, $"{model}: fasce per stazione = {n + 1}, da destra a sinistra (Y crescente, nodo iniziale a destra)", ordered);
        }
        var stations = doc.Objects.OfType<GH_Component>().Single(c => c.NickName == "G2 | fasce soletta per stazione — da destra a sinistra").Params.Output[0].VolatileData.PathCount;
        var stripWidths = Values(Id(doc, Grillage).Params.Output.Single(p => p.Name == "Deck Strip Widths")).Select(Convert.ToDouble).ToArray();
        True(report, $"una larghezza tributaria per fascia ({stations} stazioni × {n + 1})", stripWidths.Length == stations * (n + 1));
        double length = stripWidths.Sum() / (n + 1);
        Near(report, "lunghezza caricata della frenatura = lunghezza del ponte dalle larghezze tributarie [mm]",
            Number(doc.Objects.OfType<GH_NumberSlider>().Single(x => x.NickName == "Q3 frenatura — lunghezza caricata L [mm] (tracciato 12 + 12 m)")), length);
        Totals Of(string nick) => LoadTotals(Goos(Out(doc.Objects.OfType<GH_Component>().Single(c => c.NickName == nick), "Beam")));
        var g1 = LoadTotals(Goos(Out(Id(doc, G1Load), "Beam")));
        Near(report, "G1 getto Fz totale [N] = −γ t B L", g1.Of("G1_GETTO")[2], -25e-6 * thickness * width * length);
        var g2 = Of("G2_FIN finiture — bordi");
        double curb = Number(doc.Objects.OfType<GH_NumberSlider>().Single(x => x.NickName == "G2 cordoli — larghezza di ciascun cordolo dal bordo [mm]"));
        double S(string nick) => Number(doc.Objects.OfType<GH_NumberSlider>().Single(x => x.NickName == nick));
        Near(report, "G2_PAV Fz [N]", g2.Of("G2_PAV")[2], -S("G2 pavimentazione — peso specifico [kN/m³]") * S("G2 pavimentazione — spessore medio [mm]") * 1e-6 * (width - 2 * curb) * length);
        Near(report, "G2_CORD Fz [N]", g2.Of("G2_CORD")[2], -S("G2 cordoli — peso specifico [kN/m³]") * S("G2 cordoli — altezza al netto della soletta [mm]") * 1e-6 * 2 * curb * length);
        Near(report, "G2_GR Fz [N]", g2.Of("G2_GR")[2], -2 * S("G2 guard-rail — carico di ciascuno [kN/m]") * length);
        Near(report, "G2_PAR Fz [N]", g2.Of("G2_PAR")[2], -2 * S("G2 parapetto — carico di ciascuno [kN/m]") * length);
        Near(report, "G2_FIN Fz [N]", g2.Of("G2_FIN")[2], -2 * S("G2 finiture (velette, impianti) — carico per bordo [kN/m]") * length);
        var qs = Of("Q7_TEMPERATURA — fasce di soletta"); var qg = Of("Q7_TEMPERATURA — travi principali"); var qo = Of("Q7_TEMPERATURA — traversi");
        double q1 = S("Q1 schema 1 — carico distribuito corsia 1 q1k [kN/m²]") * 1e-3, qr = S("Q1 schema 1 — carico distribuito area rimanente [kN/m²]") * 1e-3;
        Near(report, "Q_TRAFFICO distribuiti Fz [N] = −(q1k 3 m + qrk (B − 2c − 3 m)) L", qs.Of("Q_TRAFFICO")[2], -(q1 * 3000 + qr * (width - 2 * curb - 3000)) * length, 1e-6); // fractions written with 6 decimals
        double l = S("Q3 frenatura — lunghezza caricata L [mm] (tracciato 12 + 12 m)");
        double qlk = Math.Min(900, Math.Max(180, 0.6 * 2 * S("Q3 frenatura — carico asse del tandem corsia 1 Q1k [kN]") + 0.1 * q1 * 1e3 * 3 * l / 1000)) * 1000;
        Near(report, "Q3_FRENATURA Fx [N] = Q1k,frenatura × (lunghezza delle travi / L)", qg.Of("Q3_FRENATURA")[0], qlk * length / l);
        Near(report, "Q4_CENTRIFUGA Fy [N] = n q L", qg.Of("Q4_CENTRIFUGA")[1], n * S("Q4 centrifuga — su ciascuna trave [kN/m = N/mm]") * length);
        double wind = S("Q5 vento — pressione [kN/m²]") * (S("Q5 vento — altezza del veicolo [mm]") + N(GirderHeight) + thickness) * 1e-3;
        var v = qg.Of("WIND_1");
        Near(report, "WIND_1 Fy [N] = −p H L", v[1], -wind * length);
        Near(report, "WIND_1 Fx [N] = 0,25 p H L", v[0], S("Q5 vento — quota longitudinale") * wind * length);
        Near(report, "WIND_1 Fz [N] = −0,25 p H L", v[2], -S("Q5 vento — quota verticale") * wind * length);
        True(report, "Q7_TEMPERATURA su fasce, travi e traversi", qs.Strains.GetValueOrDefault("Q7_TEMPERATURA") > 0 && qg.Strains.GetValueOrDefault("Q7_TEMPERATURA") > 0 && qo.Strains.GetValueOrDefault("Q7_TEMPERATURA") > 0);
        var movingPath = Values(Out(doc.Objects.OfType<GH_Component>().Single(c => c.Name == "Bridge Straight Moving Path"), "Path")).OfType<MovingPath>().Single();
        var wheels = movingPath.Template.Vehicles.Single().Loads.Cast<MovingPointForce>().ToArray();
        True(report, "tandem schema 1 corsia 1: 4 ruote da 150 kN, forze globali −Z", wheels.Length == 4 && wheels.All(x => x.Force[2] == -150000 && x.Axis == 1));
        Near(report, "asse della corsia 1 a sinistra del tracciato [mm]",
            Number(doc.Objects.OfType<GH_Component>().Single(c => c.NickName == "TRAFFICO | Asse della corsia 1 a sinistra del tracciato [mm]").Params.Output[0]), side - 2200);
        True(report, "fasi: 10 con G1_ACCIAIO e G1, 40 con Q, WIND, FRENATURA, CENTRIFUGA, TEMPERATURA, 45 con il tandem",
            In(Id(doc, G1Phase), "Actions").SourceCount == 2 && In(Id(doc, QPhase), "Actions").SourceCount == 5
            && In(Id(doc, Merge), "Phases").SourceCount == 5);
        True(report, "combinazione FEM G2_PERMANENTI: cinque casi G2, coefficienti 1",
            Values(In(Id(doc, G2Combination), "Coefficients")).Select(Convert.ToDouble).SequenceEqual(new[] { 1d, 1, 1, 1, 1 }));
        return report;
    }

    private static object Prop(object o, string name)
    {
        if (o == null) return null; var t = o.GetType();
        return t.GetProperty(name) is System.Reflection.PropertyInfo p ? p.GetValue(o) : t.GetField(name)?.GetValue(o);
    }
}
