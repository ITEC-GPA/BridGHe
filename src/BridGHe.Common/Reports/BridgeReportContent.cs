using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace BridGHe.Common.Reports;

internal sealed class BridgeReportContent
{
    private static readonly XNamespace W = BridgeWordReport.W;
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private readonly BridgeReportData data;
    private readonly BridgeReportTopic[] selected;
    private readonly IReadOnlyList<BridgeReportFigure> figures;
    private readonly IReadOnlyList<string> warnings;
    private readonly int width;
    private readonly Dictionary<string, byte[]> parts;
    private int pictureId;
    private static string N(double? value) => BridgeReportData.Number(value);

    internal BridgeReportContent(BridgeReportData data, BridgeReportTopic[] selected, IReadOnlyList<BridgeReportFigure> figures,
        IReadOnlyList<string> warnings, int width, Dictionary<string, byte[]> parts)
    { this.data = data; this.selected = selected; this.figures = figures; this.warnings = warnings; this.width = width; this.parts = parts;
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        pictureId = BridgeWordReport.Read(parts, "word/document.xml").Descendants(wp + "docPr").Select(e => (int?)e.Attribute("id") ?? 0).DefaultIfEmpty().Max(); }

    internal IEnumerable<XElement> All() => new[] { "Summary" }.Concat(selected.Select(t => t.ToString())).SelectMany(Chapter);
    internal IEnumerable<XElement> Chapter(string name)
    {
        if (name != "Summary" && !selected.Any(t => t.ToString() == name)) return Array.Empty<XElement>();
        return name switch { "Summary" => Summary(), "Model" => Model(), "Sections" => Sections(), "Phases" => Phases(),
            "Checks" => Checks(), "Critical" => Critical(), "Plots" => Plots(), "Appendix" => Appendix(), _ => throw new ArgumentException("Unknown chapter.") };
    }
    private static XElement P(string text, string style = "Normal") => new(W + "p", new XElement(W + "pPr", new XElement(W + "pStyle", new XAttribute(W + "val", style))),
        new XElement(W + "r", new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text ?? "")));
    private static XElement H(string text, bool second = false) => P(text, second ? "Heading2" : "Heading1");
    private XElement Table(string[] headers, IEnumerable<string[]> rows, params double[] weights)
    {
        if (weights.Length != headers.Length) weights = Enumerable.Repeat(1.0, headers.Length).ToArray();
        var widths = weights.Select(v => (int)(width * v / weights.Sum())).ToArray(); widths[^1] += width - widths.Sum();
        var borders = new XElement(W + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(s =>
            new XElement(W + s, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", "4"), new XAttribute(W + "color", "D9D9D9"))));
        var table = new XElement(W + "tbl", new XElement(W + "tblPr", new XElement(W + "tblStyle", new XAttribute(W + "val", "TableGrid")),
            new XElement(W + "tblW", new XAttribute(W + "w", width), new XAttribute(W + "type", "dxa")),
            new XElement(W + "tblLayout", new XAttribute(W + "type", "fixed")), borders,
            new XElement(W + "tblCellMar", new[] { "top", "left", "bottom", "right" }.Select(s => new XElement(W + s, new XAttribute(W + "w", "90"), new XAttribute(W + "type", "dxa"))))),
            new XElement(W + "tblGrid", widths.Select(w => new XElement(W + "gridCol", new XAttribute(W + "w", w)))));
        int index = 0;
        foreach (var row in new[] { headers }.Concat(rows))
        {
            if (row.Length != headers.Length) throw new ArgumentException("Report table column count mismatch.");
            var tr = new XElement(W + "tr", new XElement(W + "trPr", index == 0 ? new XElement(W + "tblHeader") : null));
            for (int c = 0; c < row.Length; c++)
            {
                var p = P(row[c], "BGTableText");
                p.Element(W + "pPr").Add(new XElement(W + "spacing", new XAttribute(W + "after", "50")),
                    new XElement(W + "jc", new XAttribute(W + "val", c > 0 && weights[c] <= 1.2 ? "center" : "left")));
                if (index == 0)
                {
                    // A repeated header must not be left alone at the foot of a page.
                    p.Element(W + "pPr").Add(new XElement(W + "keepNext"));
                    p.Element(W + "r").AddFirst(new XElement(W + "rPr", new XElement(W + "b")));
                }
                tr.Add(new XElement(W + "tc", new XElement(W + "tcPr", new XElement(W + "tcW", new XAttribute(W + "w", widths[c]), new XAttribute(W + "type", "dxa")),
                    new XElement(W + "vAlign", new XAttribute(W + "val", "center")), new XElement(W + "shd", new XAttribute(W + "fill", index == 0 ? "E3EDF4" : "FFFFFF"))), p));
            }
            table.Add(tr); index++;
        }
        return table;
    }
    private string Location(BridgeReportSample s) => s.Source == null ? "GH " + s.TreePosition + " — provenienza FEM assente"
        : (string.IsNullOrWhiteSpace(s.Source.SuperElement) ? "" : s.Source.SuperElement + " / ") + "Trave " + s.Source.BeamId + ", s=" + N(s.Source.StationMm) + " mm, " + s.Source.Combination;
    private IEnumerable<XElement> Summary()
    {
        var rows = data.Rows.ToArray(); int finite = rows.Count(r => r.Check.HasRatio), failed = rows.Count(r => r.Check.HasRatio && r.Check.Ratio > 1);
        int missing = rows.Count(r => r.Check.Active && !r.Check.HasRatio), inactive = rows.Count(r => !r.Check.Active);
        yield return H("Riepilogo e copertura delle verifiche");
        yield return P($"Risultati forniti: {data.Samples.Length}. Configurazioni di sezione e dettaglio: {data.Sections.Length}. " +
            $"{finite} esiti con WR numerico, di cui {failed} oltre limite; {missing} esiti attivi senza WR disponibile e {inactive} punti non attivi.");
        yield return P(failed > 0 ? "Sono presenti verifiche oltre limite nei risultati forniti."
            : missing > 0 ? "I WR disponibili sono entro limite, ma la copertura degli esiti attivi è incompleta."
            : finite > 0 ? "I WR numerici disponibili sono entro limite. Questo esito riguarda esclusivamente le verifiche presenti nei risultati forniti."
            : "Nessun esito numerico disponibile per formulare un riepilogo dei WR.");
        yield return P("I WR sono quelli nativi GPC. I controlli qualitativi possono usare valori convenzionali; non sono una misura della riserva resistente. " +
            "Verifiche disattivate, assenti o non implementate non sono considerate superate. Un valore mancante non è zero. Le progressive s sono locali all'asta FEM, in mm.");
        yield return Table(new[] { "Famiglia", "WR massimo disponibile", "Senza WR", "Oltre limite" }, rows.GroupBy(r => r.Check.Family).Select(g => new[] {
            g.Key, N(g.Where(r=>r.Check.HasRatio).Select(r=>r.Check.Ratio).DefaultIfEmpty(null).Max()),
            g.Count(r=>r.Check.Active&&!r.Check.HasRatio).ToString(), g.Count(r=>r.Check.HasRatio&&r.Check.Ratio>1).ToString() }), 2.3, 1.2, .9, .9);
        yield return P("Report " + data.Id + " — " + data.Created.ToString("yyyy-MM-dd HH:mm zzz") + ". BridGHe " + data.BridGHeVersion + "; GPC " + data.GpcVersion + ".");
        yield return P("Impronta dati SHA256 " + data.DataHash + ". Archivio numerico incluso nel DOCX: bridghe/report.json.");
        yield return P("Il report documenta i risultati disponibili e le opzioni di calcolo dichiarate; non esegue nuove analisi né completa verifiche mancanti. " +
            "I riferimenti nelle note sono quelli della libreria e non costituiscono un controllo indipendente di applicabilità normativa.");
        if (warnings.Count > 0)
        {
            yield return H("Segnalazioni", true);
            foreach (var warning in warnings.Take(25)) yield return P(warning);
            if (warnings.Count > 25) yield return P($"Segnalazioni aggiuntive nell'archivio numerico incorporato: {warnings.Count - 25}.");
        }
    }
    private IEnumerable<XElement> Model()
    {
        yield return H("Modello di riferimento");
        if (data.Model == null) { yield return P("Modello non fornito. Sono disponibili soltanto le sezioni, le azioni e gli identificativi conservati nei risultati di verifica."); yield break; }
        var m = data.Model;
        yield return P($"{m.Name}. Unità FEM {m.Units}; nodi {m.Nodes}, aste {m.Beams}, piastre {m.Plates}.");
        yield return P("La prima versione del report non ricostruisce vincoli, asse del ponte o schema grafico globale. Inserire queste descrizioni e figure nel template quando necessarie.");
        yield return Table(new[] { "Risultato", "Posizione e combinazione", "Config.", "Fase e stato limite" },
            data.Samples.Select(s => new[] { s.Id, Location(s), s.SectionId, s.Phase + " / " + s.LimitState }), .8, 2.8, .8, 1.5);
    }
    private IEnumerable<XElement> Sections()
    {
        yield return H("Sezioni materiali e dettagli");
        foreach (var s in data.Sections)
        {
            var g = s.Geometry; var m = s.Materials; var o = s.Options;
            yield return H(s.Id + " " + g.SectionType + " " + o.LimitStateName, true);
            yield return P("Le configurazioni distinguono geometria, materiali, opzioni e dettagli; configurazioni diverse della stessa asta restano separate.");
            yield return Table(new[] { "Grandezza", "Valore", "Unità" }, new[] {
                new[]{"Larghezza efficace soletta",N(g.Width),"mm"},new[]{"Spessore soletta",N(g.SlabHeight),"mm"},
                new[]{"Altezza acciaio",N(g.Height),"mm"},new[]{"Numero anime",N(g.WebCount),"—"},new[]{"Spessore reale anima",N(g.PlateThickness),"mm"},
                new[]{"Larghezza totale flange superiori",N(g.TopWidth),"mm"},new[]{"Spessore flange superiori",N(g.TopThickness),"mm"},
                new[]{"Larghezza prima piattabanda inferiore",N(g.Bottom1Width),"mm"},new[]{"Spessore prima piattabanda inferiore",N(g.Bottom1Thickness),"mm"},
                new[]{"Area acciaio",N(g.SteelArea),"mm²"},new[]{"Inerzia acciaio",N(g.SteelInertia),"mm⁴"},new[]{"Numero barre soletta",N(g.Bars.Length),"—"} }, 3, 1.2, .8);
            yield return Table(new[] { "Materiale", "Resistenza caratteristica MPa", "Modulo elastico MPa" }, new[] {
                new[]{"Acciaio " + m.Steel,N(m.Fy),N(m.Ea)},new[]{"Calcestruzzo " + m.Concrete,N(m.Fck),N(m.Ec)},new[]{"Armatura " + m.Rebar,N(m.Fys),N(m.Es)} }, 2.5, 1.3, 1.3);
            yield return P($"Opzioni dichiarate: {o.StandardName}; {o.LimitStateName}; γM0={N(o.GammaM0)}, γM1={N(o.GammaM1)}, γM2={N(o.GammaM2)}, γC={N(o.GammaC)}, γS={N(o.GammaS)}, αcc={N(o.AlphaCC)}. Classe 4: {o.Class4}.");
            foreach (var detail in s.Details.EnumerateObject())
            {
                bool enabled = detail.Value.TryGetProperty("Enabled", out var active) && active.ValueKind == System.Text.Json.JsonValueKind.True;
                string name = detail.Name switch { "Studs" => "Pioli", "Intermediate" => "Irrigiditori intermedi", "Support" => "Irrigiditori di appoggio",
                    "Transverse" => "Armature trasversali", "Fatigue" => "Fatica", "Box" => "Cassoncino", _ => detail.Name };
                string Value(string key) => detail.Value.TryGetProperty(key, out var value) ? value.ValueKind == System.Text.Json.JsonValueKind.Number ? N(value.GetDouble()) : value.ToString() : "n.d.";
                string description = !enabled ? "non attivati" : detail.Name switch {
                    "Studs" => $"diametro {Value("Diameter")} mm; altezza {Value("Height")} mm; {Value("CountPerRow")} pioli/fila; passo longitudinale {Value("LongitudinalPitch")} mm; trasversale {Value("TransversePitch")} mm; fu {Value("Fu")} MPa; γV {Value("GammaV")}",
                    "Intermediate" => $"piatto {Value("Width")} × {Value("Thickness")} mm; pannelli {Value("LeftPanel")} e {Value("RightPanel")} mm; fattore lunghezza {Value("LengthFactor")}; saldature {Value("CheckWelds")}, gola {Value("WeldThroat")} mm",
                    "Support" => $"piatto {Value("Width")} × {Value("Thickness")} mm; R assegnata {Value("ReactionKN")} kN; impronta {Value("FootprintLength")} × {Value("FootprintWidth")} mm; saldature {Value("CheckWelds")}, gola {Value("WeldThroat")} mm",
                    _ => "attivati; parametri completi nell'archivio numerico" };
                yield return P(name + ": " + description + ".");
            }
        }
    }
    private IEnumerable<XElement> Phases()
    {
        yield return H("Fasi azioni e combinazioni");
        if (data.Model?.Workflow is { } workflow)
        {
            yield return P("Metodo di assemblaggio: " + workflow.Method + ". Gli strain sono incrementi adimensionali; i giorni sono metadati di sequenza.");
            foreach (var phase in workflow.Phases.OrderBy(p => p.Order))
            {
                yield return H(phase.Order + " " + phase.Name, true);
                yield return P("Stato sezione " + phase.State + "; giorno " + N(phase.Day) + ".");
                yield return Table(new[] { "Azione", "Modello e caso sorgente", "Comportamento", "Strain" }, phase.Actions.Select(a => new[] {
                    a.Key,a.SourceName + " / " + (a.SourceCase ?? "senza caso FEM"),a.Behavior.ToString(),N(a.Strain)}),1.1,2.8,1.2,.9);
            }
        }
        else yield return P("Sequenza costruttiva globale non disponibile. Le fasi locali effettivamente usate sono conservate per ciascun risultato nell'archivio e nelle schede dei casi governanti.");
        if (data.Model != null)
        {
            yield return Table(new[] { "Combinazione", "Caso", "Coefficiente" }, data.Model.Combinations.SelectMany(c => c.Factors.Length == 0
                ? new[] { new[] { c.Name, "Coefficienti non disponibili", "n.d." } }
                : c.Factors.Select(f => new[] { c.Name, f.Case, N(f.Factor) })), 1.5, 2.8, 1);
            yield return P("I coefficienti sono quelli memorizzati nel modello; non vengono generati o maggiorati dal report. La selezione SLU o SLE del verificatore non certifica i coefficienti della combinazione.");
        }
    }
    private IEnumerable<XElement> Checks()
    {
        yield return H("Verifiche governanti per voce");
        yield return P("Massimi WR disponibili distinti per configurazione, fase, stato limite e voce. Le parità sono mantenute. Le voci senza esiti numerici compaiono separatamente.");
        yield return Table(new[] { "Voce", "Risultati", "WR", "Fase e stato limite" }, data.Governing(true)
            .GroupBy(r => (r.Sample.SectionId, r.Check.Name, r.Check.Unit, r.Check.Ratio, r.Sample.Phase, r.Sample.LimitState)).Select(g => new[] {
                g.Key.SectionId + " / " + g.Key.Name,BridgeReportData.ResultIds(g.Select(r => r.Sample.Id)),N(g.Key.Ratio),g.Key.Phase + " / " + g.Key.LimitState }), 2.6, 1.3, .9, 1.2);
        var missing = data.Rows.Where(r => r.Check.Active && !r.Check.HasRatio).ToArray();
        if (missing.Length > 0)
        {
            yield return H("Esiti non disponibili", true);
            yield return Table(new[] { "Risultati", "Voce", "Motivo GPC" }, missing.GroupBy(r => (r.Sample.SectionId, r.Sample.Phase, r.Sample.LimitState, r.Check.Name, r.Check.NativeStatus, r.Check.Note))
                .Select(g => new[] { BridgeReportData.ResultIds(g.Select(r=>r.Sample.Id)), g.Key.Name, g.Key.NativeStatus + ". " + g.Key.Note }), 1.3, 2.1, 2.5);
        }
    }
    private IEnumerable<XElement> Critical()
    {
        yield return H("Schede dei casi governanti");
        yield return P("Una scheda per ciascun risultato che governa almeno una famiglia, distinguendo configurazione, fase e stato limite. Domande e resistenze sono concomitanti; non si combinano massimi di casi diversi.");
        foreach (var group in data.Governing(false).GroupBy(r => r.Sample.Id))
        {
            var s = group.First().Sample;
            yield return H(s.Id + " " + s.SectionId + " " + s.LimitState, true);
            yield return P(Location(s) + ". Fase " + s.Phase + ". Metodo " + s.Method + ".");
            yield return P("Campo di applicazione dichiarato dal calcolo: " + s.Scope);
            yield return P("Iterazioni " + s.Iterations + "; residuo nativo " + N(s.Residual) + ". Nessun ricalcolo nel report.");
            yield return Table(new[] { "Verifica", "Domanda", "Limite", "Unità", "WR" }, group.Select(r => new[] { r.Check.Name, N(r.Check.Demand), N(r.Check.Resistance), r.Check.Unit, N(r.Check.Ratio) }), 3, .9, .9, .7, .7);
            foreach (var row in group.Where(r => !string.IsNullOrWhiteSpace(r.Check.Note))) yield return P(row.Check.Name + ": " + row.Check.Note);
            yield return Table(new[] { "Contributo", "N kN", "Mx kNm", "V kN", "n", "Strain" }, s.Contributions.Select(c => new[] { c.Name, N(c.N), N(c.Mx), N(c.V), N(c.HomogenizationN), N(c.ShrinkageStrain) }), 2, 1.2, 1.1, 1.1, .5, .8);
            yield return P("Le azioni della tabella sono i contributi effettivi del risultato. Riferimenti di applicazione, opzioni e input locali completi sono nell'archivio incorporato. Le formule non esposte dalla libreria non sono ricostruite dal report.");
        }
    }
    private IEnumerable<XElement> Plots()
    {
        yield return H("Grafici delle verifiche");
        if (figures.Count == 0) { yield return P("Nessun grafico fornito. Collegare le uscite SVG dei plot a Bridge Report."); yield break; }
        yield return P("Grafici forniti dall'utente tramite SVG. La corrispondenza con i risultati del report deve essere controllata: lo SVG non contiene un'identità numerica del modello verificabile dal componente.");
        foreach (var f in figures) { yield return P(f.Title, "Caption"); yield return Picture(f); }
    }
    private IEnumerable<XElement> Appendix()
    {
        yield return H("Tabulato completo delle verifiche");
        foreach (var s in data.Samples)
        {
            yield return H(s.Id + " " + s.Phase + " " + s.LimitState, true); yield return P(Location(s) + ". Configurazione " + s.SectionId + ".");
            yield return Table(new[] { "Voce", "Domanda", "Limite", "Unità", "WR", "Esito" }, s.Checks.Select(c => new[] { c.Name, N(c.Demand), N(c.Resistance), c.Unit, N(c.Ratio), c.Status }), 2.8, .8, .8, .7, .7, 1.2);
        }
    }
    private XElement Picture(BridgeReportFigure f)
    {
        if (f.Png == null || f.Width <= 0 || f.Height <= 0) throw new ArgumentException("Invalid report figure.");
        string key = "bridghe-" + Guid.NewGuid().ToString("N"), relId = "bg" + Guid.NewGuid().ToString("N");
        parts["word/media/" + key + ".png"] = f.Png;
        if (!string.IsNullOrEmpty(f.Svg)) parts["word/media/" + key + ".svg"] = Encoding.UTF8.GetBytes(f.Svg);
        const string relPath = "word/_rels/document.xml.rels";
        var rels = parts.ContainsKey(relPath) ? BridgeWordReport.Read(parts, relPath) : new XDocument(new XElement(Rel + "Relationships"));
        rels.Root.Add(new XElement(Rel + "Relationship", new XAttribute("Id", relId), new XAttribute("Type", R.NamespaceName + "/image"), new XAttribute("Target", "media/" + key + ".png")));
        parts[relPath] = BridgeWordReport.Xml(rels);
        XNamespace wp = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace pic = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        long cx = Math.Min(width * 635L, (long)(5.8 * 914400 * f.Width / f.Height)), cy = cx * f.Height / f.Width;
        var extent = new XElement(a + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy));
        var graphic = new XElement(a + "graphic", new XElement(a + "graphicData", new XAttribute("uri", pic.NamespaceName),
            new XElement(pic + "pic", new XElement(pic + "nvPicPr", new XElement(pic + "cNvPr", new XAttribute("id", "0"), new XAttribute("name", f.Title)), new XElement(pic + "cNvPicPr")),
                new XElement(pic + "blipFill", new XElement(a + "blip", new XAttribute(R + "embed", relId)), new XElement(a + "stretch", new XElement(a + "fillRect"))),
                new XElement(pic + "spPr", new XElement(a + "xfrm", new XElement(a + "off", new XAttribute("x", 0), new XAttribute("y", 0)), extent),
                    new XElement(a + "prstGeom", new XAttribute("prst", "rect"), new XElement(a + "avLst"))))));
        return new XElement(W + "p", new XElement(W + "r", new XElement(W + "drawing", new XElement(wp + "inline",
            new XElement(wp + "extent", new XAttribute("cx", cx), new XAttribute("cy", cy)),
            new XElement(wp + "docPr", new XAttribute("id", ++pictureId), new XAttribute("name", f.Title), new XAttribute("descr", f.Title)), graphic))));
    }
}
