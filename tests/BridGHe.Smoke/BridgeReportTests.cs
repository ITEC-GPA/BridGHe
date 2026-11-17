using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Reports;
using GPC.Checkers.CompositeBridge;

internal static class BridgeReportTests
{
    private static int checks;
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    internal static readonly string Template = Path.GetFullPath("example/48-report-word/Template_BridGHe.docx");
    private static void Require(bool ok, string text) { if (!ok) throw new Exception("REPORT: " + text); checks++; }
    private static string Part(string path, string name) { using var zip = ZipFile.OpenRead(path); using var read = new StreamReader(zip.GetEntry(name).Open()); return read.ReadToEnd(); }
    private static string Text(string path) => string.Join(" ", XDocument.Parse(Part(path, "word/document.xml")).Descendants(W + "t").Select(t => t.Value));
    internal static void Run()
    {
        checks = 0; string folder = Path.GetFullPath(Path.Combine("artifacts/report-tests", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        var plain = BridgeDetailPlotTests.Result();
        var stages = plain.Analysis.Stages.ToList();
        stages[^1] = plain.Stage with { Shear = plain.Stage.Shear with { Checks = new() {
            new("Intermedio · A", 8, 10, "MPa", .8), new("Intermedio · B", 0, 1, "—", 2), new("Intermedio · C", 3, 4, "MPa", null, "Controllo mancante") } } };
        var result = new BridgeStressResult(plain.Analysis with { Stages = stages }, stages.Count - 1, new("B1", 200, "C1", "G1"),new[]{BridgeCheckAdapter.PlanarAssumption});
        var data = BridgeReportData.Create(new[] { new BridgeReportEntry(result, "{2;4}[0]"), new BridgeReportEntry(result.WithSource(result.Source with { Combination = "C2" }), "{2;4}[1]") },
            new("Verifiche del ponte", "Progetto <A> & B", "Tecnico", "03"));
        Require(data.Samples.Length == 2 && data.Sections.Length == 1, "Lost samples or duplicate section configurations.");
        Require(data.Rows.Any(r => r.Check.Ratio == 2 && r.Check.Demand == 0 && r.Check.Resistance == 1), "Native qualitative WR recomputed as D/R.");
        int missingPerResult=result.Stage.Points.Count(p=>p.Active&&(!p.Utilization.HasValue||!double.IsFinite(p.Utilization.Value)))
            +BridgeGpc.Checks(result.Stage).Count(c=>!c.Ratio.HasValue||!double.IsFinite(c.Ratio.Value));
        Require(data.Rows.Count(r => r.Check.Active && !r.Check.HasRatio) == 2*missingPerResult, "Missing ratios became zero or disappeared.");
        Require(data.Governing(false).Count(r => r.Check.Name == "Intermedio · B") == 2, "Governing ties or combinations lost.");
        Require(BridgeReportData.ResultIds(new[]{"R00003","R00001","R00002"})=="R00001–R00003" && BridgeReportData.ResultIds(new[]{"R00001","R00003","R00004"})=="R00001, R00003, R00004","Result ID ranges must not invent missing results.");
        Require(data.Samples[0].Source.StationMm == 200 && data.Samples[0].TreePosition == "{2;4}[0]", "Lost local station or tree identity.");
        Require(data.Samples[0].Contributions.Select(c=>(c.N,c.Mx,c.V,c.ShrinkageStrain)).SequenceEqual(result.Stage.Contributions.Select(c=>(c.N,c.Mx,c.V,c.ShrinkageStrain))), "Report altered concomitant actions.");
        Require(data.DataHash == BridgeReportData.Create(new[] { new BridgeReportEntry(result, "{2;4}[0]"), new BridgeReportEntry(result.WithSource(result.Source with { Combination = "C2" }), "{2;4}[1]") },data.Metadata).DataHash,"Numeric hash depends on export time/ID.");
        string full = Path.Combine(folder,"full.docx");
        var exported = BridgeWordReport.Write(Template,full,data,new[]{BridgeReportTopic.Sections,BridgeReportTopic.Checks,BridgeReportTopic.Critical},overwrite:true);
        string text = Text(full);
        Require(text.Contains("Ipotesi del verificatore") && text.Contains(BridgeCheckAdapter.PlanarAssumption) && data.Samples.All(s=>s.Assumptions.Contains(BridgeCheckAdapter.PlanarAssumption)),"Planar force exclusions must be visible and archived in the report, irrespective of selected chapters.");
        Require(text.Contains("non disponibili") && text.Contains("R00001") && text.Contains("R00002"), "Incomplete checks or source IDs absent from document.");
        Require(!text.Contains("{{BG.") && text.Contains("Progetto <A> & B"), "Unreplaced/unsafe XML tokens.");
        Require(!text.Contains("Modello di riferimento") && !text.Contains("Tabulato completo"), "Unselected chapters leaked into document.");
        Require(text.Contains("Riepilogo e copertura") && text.Contains("Non" , StringComparison.OrdinalIgnoreCase), "Mandatory coverage is missing.");
        Require(XDocument.Parse(Part(full,"word/header1.xml")).Descendants(W+"t").Select(t=>t.Value).Aggregate("",(a,b)=>a+b).Contains("Progetto <A> & B"),"Split-run scalar in header not replaced.");
        Require(Part(full,"word/styles.xml")==Part(Template,"word/styles.xml"),"Template styles changed.");
        var bodyElements=XDocument.Parse(Part(full,"word/document.xml")).Root.Element(W+"body").Elements().ToArray();
        Require(!bodyElements.Zip(bodyElements.Skip(1),(a,b)=>a.Name==W+"tbl"&&b.Name==W+"tbl").Any(adjacent=>adjacent),
            "Word would merge consecutive geometry/material tables and repeat the wrong header.");
        Require(XNode.DeepEquals(XDocument.Parse(Part(full,"word/document.xml")).Descendants(W+"sectPr").Last(),XDocument.Parse(Part(Template,"word/document.xml")).Descendants(W+"sectPr").Last()),"Margins or header/footer references changed.");
        using(var archive=JsonDocument.Parse(Part(full,"bridghe/report.json")))
        {
            var saved=archive.RootElement.GetProperty("Data");
            Require(saved.GetProperty("DataHash").GetString()==data.DataHash && saved.GetProperty("Samples").GetArrayLength()==2,"Embedded numerical snapshot missing.");
            Require(saved.GetProperty("Samples")[0].GetProperty("Checks").EnumerateArray().Any(c=>c.GetProperty("Name").GetString()=="Intermedio · C" && c.GetProperty("Ratio").ValueKind==JsonValueKind.Null),"Null ratio corrupted in archive.");
        }
        byte[] before=File.ReadAllBytes(full); MustFail(()=>BridgeWordReport.Write(Template,full,data),"overwrite protection");
        Require(before.SequenceEqual(File.ReadAllBytes(full)),"Existing report changed after rejected overwrite.");
        using(var locked=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.None))
            MustFail(()=>BridgeWordReport.Write(Template,full,data,overwrite:true),"locked destination");
        Require(before.SequenceEqual(File.ReadAllBytes(full)),"Locked destination changed.");
        MustFail(()=>BridgeWordReport.Write(Template,Template,data,overwrite:true),"template protection");
        MustFail(()=>BridgeWordReport.Write(Template,full,data,new[]{(BridgeReportTopic)999},overwrite:true),"invalid topic");
        string basic=Modified("basic",d=> {
            foreach(var p in d.Descendants(W+"p").Where(p=>string.Concat(p.Descendants(W+"t").Select(t=>t.Value)).StartsWith("{{BG.") && !p.Ancestors(W+"tbl").Any()).ToArray())
                if(string.Concat(p.Descendants(W+"t").Select(t=>t.Value)) is not "{{BG.Title}}" and not "{{BG.Project}}")p.Remove();
            d.Root.Element(W+"body").Elements(W+"sectPr").Last().AddBeforeSelf(new XElement(W+"p",new XElement(W+"r",new XElement(W+"t","{{BG.Report}}"))));
        });
        string minimal=Path.Combine(folder,"minimal.docx");BridgeWordReport.Write(basic,minimal,data,Array.Empty<BridgeReportTopic>(),overwrite:true);
        Require(Text(minimal).Contains("Riepilogo e copertura")&&!Text(minimal).Contains("Verifiche governanti per voce"),"Empty selection did not retain summary alone.");
        string wrong=Modified("wrong",d=>d.Descendants(W+"t").First(t=>t.Value=="{{BG.Summary}}").Value="{{BG.Summry}}");
        MustFail(()=>BridgeWordReport.Write(wrong,full,data,overwrite:true),"missing mandatory block");
        wrong=Modified("unknown",d=>d.Descendants(W+"t").First(t=>t.Value=="{{BG.Summary}}").AddAfterSelf(new XElement(W+"t","{{BG.Fake}}")));
        MustFail(()=>BridgeWordReport.Write(wrong,full,data,overwrite:true),"unknown marker");
        string duplicate=Modified("duplicate",d=> {var p=d.Descendants(W+"p").Single(p=>string.Concat(p.Descendants(W+"t").Select(t=>t.Value))=="{{BG.Summary}}");p.AddAfterSelf(new XElement(p));});
        MustFail(()=>BridgeWordReport.Write(duplicate,full,data,overwrite:true),"duplicate block");
        string dotx=Path.Combine(folder,"basic.dotx");File.Copy(basic,dotx,true);
        using(var zip=ZipFile.Open(dotx,ZipArchiveMode.Update))
        {
            var entry=zip.GetEntry("[Content_Types].xml");string value;using(var reader=new StreamReader(entry.Open()))value=reader.ReadToEnd();
            entry.Delete();using var writer=new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open());writer.Write(value.Replace("wordprocessingml.document.main+xml","wordprocessingml.template.main+xml"));
        }
        BridgeWordReport.Write(dotx,minimal,data,Array.Empty<BridgeReportTopic>(),overwrite:true);
        Require(Part(minimal,"[Content_Types].xml").Contains("wordprocessingml.document.main+xml")&&!Part(minimal,"[Content_Types].xml").Contains("wordprocessingml.template.main+xml"),"DOTX not converted to DOCX content type.");
        Require(!Directory.EnumerateFiles(folder,".bridghe-report-*").Any(),"Failed writes leaked temporary files.");
        Divided(folder, result);
        Console.WriteLine($"REPORT PASS: {checks} assertions; native values, missing WR, ties, template styles/headers, chapter selection, split markers, archives, index with attachments and safe file handling.");

        void MustFail(Action action,string label) {try {action();}catch(Exception e) when(e is IOException or ArgumentException or UnauthorizedAccessException){checks++;return;}throw new Exception("REPORT expected failure: "+label);}
        string Modified(string name,Action<XDocument> edit)
        {
            string path=Path.Combine(folder,name+".docx");File.Copy(Template,path,true);
            using var zip=ZipFile.Open(path,ZipArchiveMode.Update);var entry=zip.GetEntry("word/document.xml");XDocument xml;
            using(var s=entry.Open())xml=XDocument.Load(s);edit(xml);entry.Delete();using(var s=zip.CreateEntry("word/document.xml").Open())xml.Save(s);return path;
        }
    }

    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static XElement[] Body(string path) => XDocument.Parse(Part(path, "word/document.xml")).Root.Element(W + "body").Elements().ToArray();
    private static string Style(XElement e) => (string)e.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val");
    private static string Txt(XElement e) => string.Concat(e.Descendants(W + "t").Select(t => t.Value));
    /// <summary>The body by chapter (Heading 1 text → its elements).</summary>
    private static System.Collections.Generic.Dictionary<string, XElement[]> Chapters(string path)
    {
        var result = new System.Collections.Generic.Dictionary<string, XElement[]>(); string key = ""; var list = new System.Collections.Generic.List<XElement>();
        foreach (var e in Body(path)) { if (Style(e) == "Heading1") { result[key] = list.ToArray(); key = Txt(e); list = new(); } else list.Add(e); }
        result[key] = list.ToArray(); return result;
    }
    private static string[][] Rows(XElement table) => table.Elements(W + "tr").Skip(1).Select(tr => tr.Elements(W + "tc").Select(Txt).ToArray()).ToArray();
    /// <summary>The cards or entries of a chapter: Heading 2 text → the texts of its paragraphs and table cells.</summary>
    private static System.Collections.Generic.Dictionary<string, string> Cards(XElement[] chapter)
    {
        var result = new System.Collections.Generic.Dictionary<string, string>(); string key = null; var text = new StringBuilder();
        void Flush() { if (key != null) result.Add(key, text.ToString().Replace("archivio numerico del report", "archivio incorporato")); text.Clear(); }
        foreach (var e in chapter)
        {
            if (Style(e) == "Heading2") { Flush(); key = Txt(e); continue; }
            if (key == null || e.Name == W + "sectPr") continue; // the section properties of the template close the last chapter
            text.Append(e.Name == W + "tbl" ? string.Join("|", e.Descendants(W + "tc").Select(Txt)) : Txt(e)).Append('\n');
        }
        Flush(); return result;
    }
    private static string Sha(string path) => BridgeReportData.Hash(File.ReadAllBytes(path));

    /// <summary>Task 195: the index with attachments against the single document of the same data: the same rows, cards, entries, governing
    /// checks and archive, distributed by combination (equal parts above the limit, small combinations together); links and bookmarks; the
    /// automatic switch at the limit; overwrite, a locked attachment, files of other origin and a later single document.</summary>
    private static void Divided(string root, BridgeStressResult result)
    {
        string folder = Path.Combine(root, "divided"); Directory.CreateDirectory(folder);
        var names = new[] { "C1", "C2", "C3", "C4", "C5" };
        var entries = new System.Collections.Generic.List<BridgeReportEntry>();
        for (int station = 0; station < 5; station++)
            for (int k = 0; k < 3; k++) entries.Add(new(result.WithSource(new("B" + (station + 1), 100 * station, names[k], "G1")), "{0}[" + entries.Count + "]"));
        entries.Add(new(result.WithSource(new("B1", 50, "C4", "G1")), "{1}[0]")); entries.Add(new(result.WithSource(new("B2", 50, "C5", "G1")), "{1}[1]"));
        entries.Add(new(result.WithSource(null), "{2}[0]"));
        var model = new BridgeReportModel("Modello di prova", "Nmm", 12, 6, 0, null, names.Select(n => new BridgeReportCombination(n, new[] { new BridgeReportFactor("G " + n, 1.35) })).ToArray());
        var data = BridgeReportData.Create(entries, new("Ponte di prova", "Progetto", "Tecnico", "02"), new BridgeReportData.ReportModelSource(true, id => 1000, names, model));
        var topics = Enum.GetValues<BridgeReportTopic>();
        string single = Path.Combine(folder, "single.docx"), index = Path.Combine(folder, "Relazione ponte.docx"), attachments = Path.Combine(folder, "Relazione ponte_allegati");
        BridgeWordReport.Write(Template, single, data, topics, overwrite: true, layout: BridgeReportLayout.Single);
        var exported = BridgeWordReport.Write(Template, index, data, topics, attachmentResults: 4);
        // 18 results, limit 4: C1, C2 and C3 (5 each) in two parts of 3 and 2; C4, C5 and the result without provenance together.
        Require(exported.File == index && exported.Folder == attachments && exported.Attachments.Length == 7 && exported.Archive == Path.Combine(attachments, "Relazione ponte_archivio.zip"), "Divided report layout.");
        Require(exported.Attachments.Select(Path.GetFileName).SequenceEqual(Enumerable.Range(1, 7).Select(i => $"Relazione ponte_A{i:00}.docx")) && exported.Attachments.All(File.Exists) && File.Exists(exported.Archive),
            "Attachment files and archive.");
        Require(!Directory.EnumerateFileSystemEntries(folder, ".bridghe-report-*").Any(), "Staging folders left beside the report.");
        // The archive is the snapshot of the single document, byte for byte; the manifest describes every file and result.
        using (var zip = ZipFile.OpenRead(exported.Archive)) using (var s = zip.GetEntry("bridghe/report.json").Open()) using (var m = new MemoryStream())
        { s.CopyTo(m); Require(m.ToArray().SequenceEqual(Encoding.UTF8.GetBytes(Part(single, "bridghe/report.json"))), "Archive differs from the snapshot of the single document."); }
        using (var manifest = JsonDocument.Parse(Part(index, "bridghe/manifest.json")))
        {
            var r = manifest.RootElement;
            Require(r.GetProperty("DataHash").GetString() == data.DataHash && r.GetProperty("Archive").GetProperty("Sha256").GetString() == Sha(exported.Archive), "Manifest archive identity.");
            var listed = r.GetProperty("Attachments").EnumerateArray().ToArray();
            Require(listed.Length == 7 && listed.Select((a, i) => a.GetProperty("Sha256").GetString() == Sha(exported.Attachments[i]) && a.GetProperty("File").GetString() == Path.GetFileName(exported.Attachments[i])).All(x => x), "Manifest attachment hashes.");
            Require(listed.Select(a => a.GetProperty("Results").GetInt32()).SequenceEqual(new[] { 3, 2, 3, 2, 3, 2, 3 }), "Equal parts above the limit, small combinations together.");
            Require(r.GetProperty("Results").EnumerateObject().Count() == data.Samples.Length, "Manifest result map.");
        }
        using (var zip = ZipFile.OpenRead(index)) Require(!Part(index, "[Content_Types].xml").Contains("/bridghe/report.json") && zip.GetEntry("bridghe/report.json") == null, "The index must not embed the whole archive.");
        // Content: the single document distributed, nothing lost or repeated.
        var one = Chapters(single); var idx = Chapters(index);
        var parts = exported.Attachments.Select(Chapters).ToArray();
        string[] modelRows(System.Collections.Generic.Dictionary<string, XElement[]> c) => c.TryGetValue("Modello di riferimento", out var e) ? Rows(e.First(x => x.Name == W + "tbl")).Select(x => string.Join("|", x)).ToArray() : Array.Empty<string>();
        var distributed = parts.SelectMany(modelRows).ToArray();
        Require(distributed.Length == data.Samples.Length && distributed.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(modelRows(one).OrderBy(x => x, StringComparer.Ordinal)), "Model rows lost or repeated in the attachments.");
        Require(!idx["Modello di riferimento"].Any(e => e.Name == W + "tbl"), "The index must not repeat the model table.");
        Require(parts.Select((c, i) => modelRows(c).All(row => row.Contains(exported.Attachments[i].EndsWith("A07.docx") ? "" : names[i / 2]))).All(x => x), "A row in the attachment of another combination.");
        var cards = Cards(one["Schede dei casi governanti"]); var split = parts.SelectMany(c => Cards(c["Schede dei casi governanti"])).ToArray();
        Require(cards.Count > 0 && split.Length == cards.Count && split.All(c => cards.TryGetValue(c.Key, out var v) && v == c.Value), "Cards differ from the single document.");
        var entriesOne = Cards(one["Tabulato completo delle verifiche"]); var entriesSplit = parts.SelectMany(c => Cards(c["Tabulato completo delle verifiche"])).ToArray();
        Require(entriesSplit.Length == data.Samples.Length && entriesSplit.All(c => entriesOne.TryGetValue(c.Key, out var v) && v == c.Value), "Appendix entries differ from the single document.");
        foreach (string chapter in new[] { "Sezioni materiali e dettagli", "Fasi azioni e combinazioni", "Grafici delle verifiche" })
            Require(idx[chapter].Select(Txt).SequenceEqual(one[chapter].Select(Txt)) && parts.All(c => !c.ContainsKey(chapter)), "Global chapter not kept in the index alone: " + chapter);
        var governing = Rows(one["Verifiche governanti per voce"].First(e => e.Name == W + "tbl")); var indexed = Rows(idx["Verifiche governanti per voce"].First(e => e.Name == W + "tbl"));
        Require(governing.Length == indexed.Length && governing.Zip(indexed, (a, b) => a.SequenceEqual(b.Take(4)) && b[4].Length > 0).All(x => x), "Governing checks of the index differ from the single document.");
        var summaryOne = one["Riepilogo e copertura delle verifiche"].Select(Txt).ToArray(); var summaryIndex = idx["Riepilogo e copertura delle verifiche"];
        Require(summaryIndex.Take(summaryOne.Length).Select(Txt).Zip(summaryOne, (a, b) => a == b || a.StartsWith("Impronta dati SHA256 " + data.DataHash) && b.StartsWith("Impronta dati SHA256 " + data.DataHash)
                || a.StartsWith("Segnalazioni aggiuntive nell'archivio numerico") && b.StartsWith("Segnalazioni aggiuntive nell'archivio numerico") && a[^4..] == b[^4..]).All(x => x)
            && Txt(summaryIndex[summaryOne.Length]) == "Allegati" && Rows(summaryIndex.Skip(summaryOne.Length).First(e => e.Name == W + "tbl")).Length == 7, "Summary of the index differs from the single document or lists no attachments.");
        // Links: every hyperlink of the index and of the attachments reaches an existing file and, with a fragment, a bookmark in it.
        foreach (var file in exported.Attachments.Prepend(index))
        {
            var rels = XDocument.Parse(Part(file, "word/_rels/document.xml.rels")).Root.Elements().Where(e => (string)e.Attribute("TargetMode") == "External").ToDictionary(e => (string)e.Attribute("Id"), e => (string)e.Attribute("Target"));
            var used = XDocument.Parse(Part(file, "word/document.xml")).Descendants(W + "hyperlink").Select(h => (string)h.Attribute(R + "id")).ToArray();
            Require(used.Length > 0 && used.All(rels.ContainsKey), "Hyperlink without relationship: " + Path.GetFileName(file));
            foreach (var target in used.Select(u => rels[u]).Distinct())
            {
                var pieces = target.Split('#'); string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file), Uri.UnescapeDataString(pieces[0])));
                Require(File.Exists(path), "Broken link " + target + " in " + Path.GetFileName(file));
                if (pieces.Length > 1) Require(XDocument.Parse(Part(path, "word/document.xml")).Descendants(W + "bookmarkStart").Any(b => (string)b.Attribute(W + "name") == pieces[1]), "Missing bookmark " + target);
            }
            var bookmarks = XDocument.Parse(Part(file, "word/document.xml")).Descendants(W + "bookmarkStart").Select(b => (string)b.Attribute(W + "name")).ToArray();
            Require(bookmarks.Distinct().Count() == bookmarks.Length, "Duplicate bookmarks in " + Path.GetFileName(file));
            Require(Part(file, "word/styles.xml") == Part(Template, "word/styles.xml") && !Txt(XDocument.Parse(Part(file, "word/document.xml")).Root).Contains("{{BG."), "Template styles or markers in " + Path.GetFileName(file));
        }
        Require(exported.Attachments.All(a => XDocument.Parse(Part(a, "word/_rels/document.xml.rels")).Root.Elements().Any(e => (string)e.Attribute("Target") == "../" + Uri.EscapeDataString("Relazione ponte.docx"))), "Attachment without its link to the index.");
        // The automatic switch: up to the limit one document, above it the index.
        string automatic = Path.Combine(folder, "auto.docx");
        Require(BridgeWordReport.Write(Template, automatic, data, topics, attachmentResults: data.Samples.Length).Attachments == null && !Directory.Exists(Path.Combine(folder, "auto_allegati")), "At the limit the report must stay one document.");
        Require(BridgeWordReport.Write(Template, automatic, data, topics, overwrite: true, attachmentResults: data.Samples.Length - 1).Attachments?.Length == 2, "Above the limit the report must be divided.");
        // Overwrite: refused without it; a locked attachment keeps the previous delivery; fewer parts replace the previous ones, other files kept.
        string State() => Sha(index) + string.Concat(Directory.EnumerateFiles(attachments).OrderBy(f => f, StringComparer.Ordinal).Select(f => Path.GetFileName(f) + Sha(f)));
        string before = State();
        MustFail(() => BridgeWordReport.Write(Template, index, data, topics, attachmentResults: 4), "divided overwrite protection");
        using (new FileStream(exported.Attachments[2], FileMode.Open, FileAccess.Read, FileShare.None))
            MustFail(() => BridgeWordReport.Write(Template, index, data, topics, overwrite: true, attachmentResults: 4), "locked attachment");
        Require(State() == before, "The previous delivery changed after a refused generation.");
        // D1 (0.4.2): the write of the index interrupted and its restore failed too (a file system filter, the network): the recovery copy named
        // by the error exists beside the report with the bytes of the previous index, and the attachments of the previous delivery are back.
        byte[] previousIndex = File.ReadAllBytes(index);
        string recoveryMessage = Faulted(new[] { "overwrite-write", "overwrite-restore" }, () => BridgeWordReport.Write(Template, index, data, topics, overwrite: true, attachmentResults: 4));
        string recovery = recoveryMessage == null ? null : Regex.Match(recoveryMessage, @"Recover the original report from: (.+?\.recovery)").Groups[1].Value;
        Require(!string.IsNullOrEmpty(recovery) && Path.GetDirectoryName(recovery) == folder && File.Exists(recovery) && File.ReadAllBytes(recovery).SequenceEqual(previousIndex),
            "D1: the recovery copy named by the error must exist beside the report, with the previous index.");
        File.Copy(recovery, index, true); File.Delete(recovery);
        Require(State() == before, "D1: the previous delivery whole again from the recovery copy.");
        File.WriteAllText(Path.Combine(attachments, "nota.txt"), "nota dell'utente");
        var again = BridgeWordReport.Write(Template, index, data, topics, overwrite: true, attachmentResults: 6);
        Require(again.Attachments.Length == 4 && Directory.EnumerateFiles(attachments).Count() == 5 && !File.Exists(Path.Combine(attachments, "Relazione ponte_A05.docx")), "Stale attachments of the previous generation.");
        var kept = Directory.EnumerateDirectories(folder, "Relazione ponte_allegati_precedente-*").ToArray();
        Require(kept.Length == 1 && File.Exists(Path.Combine(kept[0], "nota.txt")) && again.Warnings.Any(w => w.Contains("non scritti da BridGHe")), "A file of the user in the folder must be kept and said.");
        var later = BridgeWordReport.Write(Template, index, data, topics, overwrite: true, layout: BridgeReportLayout.Single);
        Require(later.Attachments == null && Directory.Exists(attachments) && later.Warnings.Any(w => w.Contains("generazione precedente")), "A later single document must leave and report the folder.");
        Require(!Directory.EnumerateFileSystemEntries(folder, ".bridghe-report-*").Any(), "Temporary files or folders leaked.");

        void MustFail(Action action, string label) { try { action(); } catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { checks++; return; } throw new Exception("REPORT expected failure: " + label); }
    }

    /// <summary>0.4.2: runs <paramref name="action"/> with an IOException injected at every named step of the publication
    /// (<c>BridgeWordReport.FaultInjection</c>, internal, by reflection) and returns the message of the IOException it ends with (null when none).</summary>
    private static string Faulted(string[] steps, Action action)
    {
        var field = typeof(BridgeWordReport).GetField("FaultInjection", BindingFlags.NonPublic | BindingFlags.Static) ?? throw new Exception("REPORT: no fault injection point.");
        field.SetValue(null, (Action<string>)(step => { if (steps.Contains(step)) throw new IOException("injected failure at " + step); }));
        try { action(); return null; }
        catch (IOException e) { checks++; return e.Message; }
        finally { field.SetValue(null, null); }
    }
}
