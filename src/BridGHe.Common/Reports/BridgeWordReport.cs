using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace BridGHe.Common.Reports;

/// <summary>The written report. <see cref="File"/> is the single document or the index; for a divided report (task 195) also the folder of the
/// attachments, the attachments in order and the numerical archive (null for a single document).</summary>
public sealed record BridgeReportExport(string File, string Id, int Results, int Checks, int Missing, string[] Warnings,
    string Folder = null, string[] Attachments = null, string Archive = null);

/// <summary>Template-based DOCX generation using OOXML only. Does not automate Word or execute template code.</summary>
public static class BridgeWordReport
{
    internal static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly Regex Token = new(@"\{\{BG\.([A-Za-z]+)\}\}", RegexOptions.Compiled);
    private static readonly string[] Scalars = { "Title", "Project", "Author", "Revision", "Date", "ReportId" };
    private static readonly string[] Blocks = Enum.GetNames<BridgeReportTopic>().Prepend("Summary").Prepend("Report").ToArray();

    /// <summary>Task 195: the most results of one document. A report with more results is written, with <see cref="BridgeReportLayout.Automatic"/>,
    /// as an index and attachments of at most this many results each (the threshold of task 186, 948 results per combination in example 50).</summary>
    public const int DefaultAttachmentResults = 1000;

    public static BridgeReportExport Write(string template, string output, BridgeReportData data,
        IEnumerable<BridgeReportTopic> topics = null, IReadOnlyList<BridgeReportFigure> figures = null, bool overwrite = false,
        BridgeReportLayout layout = BridgeReportLayout.Automatic, int attachmentResults = DefaultAttachmentResults)
    {
        template = Path.GetFullPath(template); output = Path.GetFullPath(output);
        if (!new[] { ".docx", ".dotx" }.Contains(Path.GetExtension(template).ToLowerInvariant())) throw new ArgumentException("Template must be .docx or .dotx.");
        if (!string.Equals(Path.GetExtension(output), ".docx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Report output must be .docx.");
        if (string.Equals(template, output, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The output must not replace the template.");
        if (File.Exists(output) && !overwrite) throw new IOException("Report already exists. Choose a new file or enable Overwrite.");
        if (data == null) throw new ArgumentNullException(nameof(data));
        if (!Enum.IsDefined(layout)) throw new ArgumentException("Unknown report layout.");
        var selected = (topics ?? BridgeReportData.DefaultTopics).Distinct().OrderBy(t => t).ToArray();
        if (selected.Any(t => !Enum.IsDefined(t))) throw new ArgumentException("Unknown report topic.");
        figures ??= Array.Empty<BridgeReportFigure>();
        var warnings = data.Warnings.ToList();
        if (selected.Contains(BridgeReportTopic.Plots) && figures.Count == 0) warnings.Add("Capitolo Grafici selezionato, ma nessun SVG fornito.");
        bool divided = layout == BridgeReportLayout.IndexWithAttachments || layout == BridgeReportLayout.Automatic && data.Samples.Length > attachmentResults;
        byte[] templateBytes = File.ReadAllBytes(template);
        if (divided) return WriteDivided(template, output, data, selected, figures, warnings, overwrite, templateBytes, BridgeReportSplit.Plan(data, output, attachmentResults));
        // A folder of attachments left by a divided report of the same name is not part of this document: kept, and said.
        string stale = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "_allegati");
        if (Directory.Exists(stale)) warnings.Add("La cartella " + Path.GetFileName(stale) + " accanto al report appartiene a una generazione precedente divisa in allegati: non fa parte di questo documento ed è stata lasciata invariata.");
        var package = Open(templateBytes, selected);
        var content = new BridgeReportContent(data, selected, figures, warnings, package.Width, package.Parts);
        Compose(package, content);
        Finish(package, Values(data, data.Metadata.Title), content, SnapshotPart, "urn:bridghe:report:snapshot");
        // P2K (P6): the snapshot is written straight into its ZIP entry (the same UTF-8 bytes, without the string of the whole JSON:
        // OutOfMemoryException on example 50); its place in the order of the parts is kept by an empty slot.
        var snapshot = Snapshot(templateBytes, selected, data, warnings, figures);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        string temporary = Path.Combine(Path.GetDirectoryName(output), ".bridghe-report-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Save(package, temporary, SnapshotPart, stream => BridgeReportData.Serialize(stream, snapshot));
            if (File.Exists(output))
            {
                if (!overwrite) throw new IOException("Report already exists.");
                PublishOverwrite(temporary, output, temporary + ".recovery");
            }
            else File.Move(temporary, output);
        }
        catch (IOException e) { throw new IOException("Cannot publish the report. Close the destination in Word and check its path. " + e.Message, e); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new(output, data.Id, data.Samples.Length, data.Rows.Count(), data.Rows.Count(r => r.Check.Active && !r.Check.HasRatio), warnings.ToArray());
    }

    /// <summary>The numerical archive of a report, the same object for the single document (embedded) and the divided report (archive file).</summary>
    private static object Snapshot(byte[] templateBytes, BridgeReportTopic[] selected, BridgeReportData data, List<string> warnings, IReadOnlyList<BridgeReportFigure> figures) =>
        new { Schema = 1, TemplateHash = BridgeReportData.Hash(templateBytes),
            Topics = selected.Select(t => t.ToString()), Data = data, ExportWarnings = warnings,
            Figures = figures.Select(f => new { f.Title, Hash = BridgeReportData.Hash(Encoding.UTF8.GetBytes(f.Svg ?? "")) }) };

    private static Dictionary<string, string> Values(BridgeReportData data, string title) => new() { ["Title"] = title, ["Project"] = data.Metadata.Project,
        ["Author"] = data.Metadata.Author, ["Revision"] = data.Metadata.Revision, ["Date"] = data.Created.ToString("yyyy-MM-dd HH:mm zzz"), ["ReportId"] = data.Id };

    /// <summary>One DOCX in the making: the parts of the template, its body and markers, the texts with scalar markers, the text width.</summary>
    private sealed class Package
    {
        internal Dictionary<string, byte[]> Parts;
        internal XDocument Document;
        internal XElement Body;
        internal Dictionary<string, XElement> Markers;
        internal Dictionary<string, XDocument> Texts;
        internal int Width;
    }

    /// <summary>Reads and validates the template for the selected chapters (every document of a report is made from the same template bytes).</summary>
    private static Package Open(byte[] templateBytes, BridgeReportTopic[] selected)
    {
        using var input = new MemoryStream(templateBytes);
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        if (zip.Entries.Count > 10000 || zip.Entries.Sum(e => e.Length) > 200_000_000 || zip.Entries.Any(e => e.Length > 40_000_000))
            throw new ArgumentException("Template exceeds the supported package size.");
        if (zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
            throw new ArgumentException("Duplicate parts in the Word template.");
        var parts = zip.Entries.ToDictionary(e => e.FullName, e => { using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
        if (parts.Keys.Any(k => k.Contains("vbaProject", StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Macro-enabled templates are not supported.");
        XDocument document = Read(parts, "word/document.xml");
        var body = document.Root?.Element(W + "body") ?? throw new ArgumentException("Invalid Word document body.");
        var markerParagraphs = body.Descendants(W + "p").Where(p => Token.Matches(Text(p)).Any(m => Blocks.Contains(m.Groups[1].Value))).ToArray();
        var markers = new Dictionary<string, XElement>();
        foreach (var p in markerParagraphs)
        {
            string value = Text(p).Trim(); var m = Token.Match(value);
            if (p.Parent != body || m.Value != value || !Blocks.Contains(m.Groups[1].Value)
                || p.Descendants(W + "drawing").Any() || p.Element(W + "pPr")?.Element(W + "sectPr") != null)
                throw new ArgumentException("Place each report block on its own paragraph in the document body, outside tables and section breaks.");
            if (!markers.TryAdd(m.Groups[1].Value, p)) throw new ArgumentException("Duplicate template block: " + m.Value);
        }
        if (markers.ContainsKey("Report"))
        {
            if (markers.Count != 1) throw new ArgumentException("Use either {{BG.Report}} or individual chapter blocks, not both.");
        }
        else
        {
            foreach (string required in selected.Select(t => t.ToString()).Prepend("Summary"))
                if (!markers.ContainsKey(required)) throw new ArgumentException("Missing template block {{BG." + required + "}}. Add it or deselect that topic.");
        }
        // Validate all tokens before any substitutions. A misspelled token must never silently disappear.
        var texts = new Dictionary<string, XDocument> { ["word/document.xml"] = document };
        foreach (var name in parts.Keys.Where(n => n.StartsWith("word/header", StringComparison.Ordinal) || n.StartsWith("word/footer", StringComparison.Ordinal)).Where(n => n.EndsWith(".xml")))
            texts.Add(name, Read(parts, name));
        foreach (var item in texts)
            foreach (var p in item.Value.Descendants(W + "p"))
            {
                string text = Text(p);
                foreach (Match m in Token.Matches(text))
                    if (!Scalars.Contains(m.Groups[1].Value) && !(item.Key == "word/document.xml" && markers.Values.Contains(p)))
                        throw new ArgumentException("Unsupported template marker: " + m.Value);
                if (Token.Replace(text, "").Contains("{{BG.", StringComparison.Ordinal)) throw new ArgumentException("Malformed BridGHe template marker.");
            }
        int width = body.Descendants(W + "sectPr").Select(s =>
        {
            int page = (int?)s.Element(W + "pgSz")?.Attribute(W + "w") ?? 12240;
            var margins = s.Element(W + "pgMar");
            return page - ((int?)margins?.Attribute(W + "left") ?? 1440) - ((int?)margins?.Attribute(W + "right") ?? 1440);
        }).DefaultIfEmpty(9360).Min();
        if (width < 4000) throw new ArgumentException("Template text area is too narrow for report tables (minimum 4000 twips).");
        return new Package { Parts = parts, Document = document, Body = body, Markers = markers, Texts = texts, Width = width };
    }

    /// <summary>Replaces the block markers with the chapters of <paramref name="content"/>.</summary>
    private static void Compose(Package package, BridgeReportContent content)
    {
        var generatedTables = new HashSet<XElement>();
        foreach (var marker in package.Markers)
        {
            var block = (marker.Key == "Report" ? content.All() : content.Chapter(marker.Key)).ToArray();
            foreach (var table in block.Where(e => e.Name == W + "tbl")) generatedTables.Add(table);
            marker.Value.ReplaceWith(block);
        }
        // Word merges consecutive tables, including their repeated header rows and column grids.
        // Separate generated tables without changing the template's own table layout or styles.
        var bodyElements = package.Body.Elements().ToArray();
        for (int i = 1; i < bodyElements.Length; i++)
            if (bodyElements[i].Name == W + "tbl" && bodyElements[i - 1].Name == W + "tbl"
                && (generatedTables.Contains(bodyElements[i]) || generatedTables.Contains(bodyElements[i - 1])))
                bodyElements[i].AddBeforeSelf(new XElement(W + "p", new XElement(W + "pPr",
                    new XElement(W + "spacing", new XAttribute(W + "before", "0"), new XAttribute(W + "after", "0"),
                        new XAttribute(W + "line", "20"), new XAttribute(W + "lineRule", "exact")))));
    }

    /// <summary>Scalars, hyperlinks of the content, content types and the package relationship of the BridGHe part <paramref name="dataPart"/>
    /// (written by <see cref="Save"/>; its slot keeps its place after the template parts and the media).</summary>
    private static void Finish(Package package, Dictionary<string, string> values, BridgeReportContent content, string dataPart, string relationship)
    {
        var parts = package.Parts;
        if (content.Links.Count > 0)
        {
            const string relPath = "word/_rels/document.xml.rels";
            var links = parts.ContainsKey(relPath) ? Read(parts, relPath) : new XDocument(new XElement(Rel + "Relationships"));
            foreach (var link in content.Links)
                links.Root.Add(new XElement(Rel + "Relationship", new XAttribute("Id", link.Key), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink"),
                    new XAttribute("Target", link.Value), new XAttribute("TargetMode", "External")));
            parts[relPath] = Xml(links);
        }
        foreach (var item in package.Texts)
        {
            foreach (var p in item.Value.Descendants(W + "p")) ReplaceScalars(p, values);
            parts[item.Key] = Xml(item.Value);
        }
        // DOTX becomes a DOCX; all other template parts, styles, logos, margins and headers survive unchanged.
        var types = Read(parts, "[Content_Types].xml");
        var mainType = types.Root.Elements(Ct + "Override").SingleOrDefault(e => (string)e.Attribute("PartName") == "/word/document.xml")
            ?? throw new ArgumentException("Template main content type is missing.");
        if (((string)mainType.Attribute("ContentType")).Contains("macroEnabled", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Macro-enabled templates are not supported.");
        mainType.SetAttributeValue("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");
        EnsureType(types, "/" + dataPart, "application/json");
        foreach (var name in parts.Keys.Where(n => n.StartsWith("word/media/bridghe-")))
            EnsureType(types, "/" + name, name.EndsWith(".svg") ? "image/svg+xml" : "image/png");
        parts["[Content_Types].xml"] = Xml(types);
        parts[dataPart] = null;
        var rels = Read(parts, "_rels/.rels");
        rels.Root.Add(new XElement(Rel + "Relationship", new XAttribute("Id", "bg" + Guid.NewGuid().ToString("N")),
            new XAttribute("Type", relationship), new XAttribute("Target", dataPart)));
        parts["_rels/.rels"] = Xml(rels);
    }

    private static void Save(Package package, string path, string dataPart, Action<Stream> writeData)
    {
        using var result = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var part in package.Parts)
        {
            using var stream = result.CreateEntry(part.Key, CompressionLevel.Optimal).Open();
            if (part.Value == null && part.Key == dataPart) writeData(stream); else stream.Write(part.Value);
        }
    }

    // ------------------------------------------------------------------------------------------------ task 195: index and attachments

    private const string ManifestPart = "bridghe/manifest.json", AttachmentPart = "bridghe/attachment.json";

    /// <summary>Task 195 (variant C of task 186, user decision of 15/11/2026): the index (<paramref name="output"/>: summary, sections, phases,
    /// governing checks and plots of all the results, the list of the attachments and the manifest) and, in the folder «name_allegati» beside
    /// it, the attachments (rows of the model table, cards and full appendix of their results) and one numerical archive «name_archivio.zip»
    /// with the bridghe/report.json of the single document. Written in a staging folder, published by renames; Overwrite replaces the
    /// attachments of the previous generation only (files of other origin are kept and said).</summary>
    private static BridgeReportExport WriteDivided(string template, string output, BridgeReportData data, BridgeReportTopic[] selected,
        IReadOnlyList<BridgeReportFigure> figures, List<string> warnings, bool overwrite, byte[] templateBytes, BridgeReportSplit plan)
    {
        string directory = Path.GetDirectoryName(output), folder = Path.Combine(directory, plan.Folder);
        if (template.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The template must not be in the folder of the attachments.");
        if (File.Exists(folder)) throw new IOException("A file has the name of the folder of the attachments: " + folder + ".");
        if (!overwrite && Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new IOException("The folder of the attachments already exists: " + folder + ". Choose a new file or enable Overwrite.");
        Open(templateBytes, selected); // the template refused before anything is written
        if (overwrite) Writable(output, folder);
        Directory.CreateDirectory(directory);
        string staging = Path.Combine(directory, ".bridghe-report-" + Guid.NewGuid().ToString("N") + ".tmp");
        string stagedFolder = Path.Combine(staging, plan.Folder), stagedIndex = Path.Combine(staging, plan.IndexFile);
        Directory.CreateDirectory(stagedFolder);
        try
        {
            // The archive (the JSON of the whole report, about a minute for example 50) is written while the attachments are made.
            var snapshot = Snapshot(templateBytes, selected, data, warnings, figures);
            string archive = Path.Combine(stagedFolder, plan.Archive);
            var archiving = Task.Run(() =>
            {
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                using (var stream = zip.CreateEntry(SnapshotPart, CompressionLevel.Optimal).Open())
                    BridgeReportData.Serialize(stream, snapshot);
                return (Bytes: new FileInfo(archive).Length, Sha256: FileHash(archive));
            });
            var attachments = new List<object>();
            try
            {
                foreach (var part in plan.Parts)
                {
                    var package = Open(templateBytes, selected);
                    var content = new BridgeReportContent(data, selected, Array.Empty<BridgeReportFigure>(), warnings, package.Width, package.Parts, plan, part);
                    Compose(package, content);
                    Finish(package, Values(data, data.Metadata.Title + " — Allegato " + part.Code), content, AttachmentPart, "urn:bridghe:report:attachment");
                    var description = new { Schema = 1, Kind = "BridGHe report attachment", ReportId = data.Id, data.DataHash, Attachment = part.Code, Of = plan.Parts.Count,
                        part.Combinations, part.Piece, part.Pieces, Index = plan.IndexFile, plan.Archive, Results = part.Samples.Select(s => s.Id) };
                    string file = Path.Combine(stagedFolder, part.File);
                    Save(package, file, AttachmentPart, stream => BridgeReportData.Serialize(stream, description));
                    attachments.Add(new { part.Code, part.File, part.Combinations, part.Piece, part.Pieces, Results = part.Samples.Length,
                        First = part.Samples[0].Id, Last = part.Samples[^1].Id, Cards = plan.Cards.Count(c => plan.PartOf(c.Sample.Id).Number == part.Number),
                        Bytes = new FileInfo(file).Length, Sha256 = FileHash(file) });
                }
            }
            catch { try { archiving.Wait(); } catch (AggregateException) { } throw; } // the archive task never outlives the staging folder
            var archived = archiving.GetAwaiter().GetResult();
            var index = Open(templateBytes, selected);
            var indexContent = new BridgeReportContent(data, selected, figures, warnings, index.Width, index.Parts, plan);
            Compose(index, indexContent);
            Finish(index, Values(data, data.Metadata.Title), indexContent, ManifestPart, "urn:bridghe:report:manifest");
            var manifest = new { Schema = 1, Kind = "BridGHe report index", ReportId = data.Id, data.Created, data.DataHash, TemplateHash = BridgeReportData.Hash(templateBytes),
                Topics = selected.Select(t => t.ToString()), plan.Folder, Archive = new { File = plan.Archive, Entry = SnapshotPart, archived.Bytes, archived.Sha256 },
                AttachmentResults = plan.Parts.Max(p => p.Samples.Length), Attachments = attachments,
                Results = plan.Parts.SelectMany(p => p.Samples.Select(s => (s.Id, p.Code))).ToDictionary(x => x.Id, x => x.Code) };
            Save(index, stagedIndex, ManifestPart, stream => BridgeReportData.Serialize(stream, manifest));
            // 0.4.2 (D1 of the final review of 0.4.1): the recovery copy of a replaced index stays beside the report, outside the staging
            // folder that is removed below, so that the path given by a failed restore still exists.
            PublishDivided(stagedFolder, stagedIndex, output, folder, plan, overwrite, warnings, staging + ".recovery");
        }
        catch (Exception e)
        {
            // D2: the removal of the staging folder never replaces the error of the generation; when it fails, the error names the folder left.
            var left = RemoveStaging(staging);
            string leftText = left == null ? "" : " The temporary folder " + staging + " could not be deleted (" + left.Message + "): delete it by hand.";
            if (e is IOException) throw new IOException("Cannot publish the report. Close the index and the attachments in Word and check the path. " + e.Message + leftText, e);
            throw;
        }
        var leftover = RemoveStaging(staging);
        if (leftover != null) warnings.Add("La cartella temporanea " + Path.GetFileName(staging) + " accanto al report non è stata cancellata (" + leftover.Message + "): il report è completo; cancellarla a mano.");
        return new(output, data.Id, data.Samples.Length, data.Rows.Count(), data.Rows.Count(r => r.Check.Active && !r.Check.HasRatio), warnings.ToArray(),
            folder, plan.Parts.Select(p => Path.Combine(folder, p.File)).ToArray(), Path.Combine(folder, plan.Archive));
    }

    /// <summary>D2 (0.4.2): the renames and removals of the folders of a divided report are tried <see cref="FolderAttempts"/> times, waiting
    /// <see cref="FolderRetryMilliseconds"/> between the attempts: just after 36 attachments and an archive of 135 MB are written, an antivirus,
    /// the indexer or a synchronised folder may still hold one of the files.</summary>
    internal const int FolderAttempts = 5;
    internal const int FolderRetryMilliseconds = 300;

    private static void MoveFolder(string from, string to)
    {
        for (int attempt = 1; ; attempt++)
            try { Fault("move-folder"); Directory.Move(from, to); return; }
            catch (Exception e) when ((e is IOException || e is UnauthorizedAccessException) && attempt < FolderAttempts && Directory.Exists(from) && !Directory.Exists(to) && !File.Exists(to))
            { System.Threading.Thread.Sleep(FolderRetryMilliseconds); }
    }

    /// <summary>Removes the staging folder (with retries); the error when it stays, never thrown.</summary>
    private static Exception RemoveStaging(string staging)
    {
        for (int attempt = 1; ; attempt++)
            try { Fault("remove-staging"); if (Directory.Exists(staging)) Directory.Delete(staging, true); return null; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                if (attempt >= FolderAttempts) return e;
                System.Threading.Thread.Sleep(FolderRetryMilliseconds);
            }
    }

    private static string FileHash(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s)); }

    /// <summary>Before the generation: the index and the files of the folder of a previous generation can be replaced (not open in Word).</summary>
    private static void Writable(string output, string folder)
    {
        var files = (Directory.Exists(folder) ? Directory.EnumerateFiles(folder) : Enumerable.Empty<string>()).Prepend(output).Where(File.Exists);
        foreach (var file in files)
            try { using var _ = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { throw new IOException("Close " + Path.GetFileName(file) + " in Word before Generate: " + e.Message, e); }
    }

    /// <summary>The staged attachments become the folder, then the staged index the report; on a failure the previous files are put back.</summary>
    private static void PublishDivided(string stagedFolder, string stagedIndex, string output, string folder, BridgeReportSplit plan, bool overwrite, List<string> warnings,
        string recovery)
    {
        if (File.Exists(output) && !overwrite) throw new IOException("Report already exists.");
        string previous = null;
        if (Directory.Exists(folder))
        {
            previous = Path.Combine(Path.GetDirectoryName(output), ".bridghe-report-" + Guid.NewGuid().ToString("N") + ".old");
            try { MoveFolder(folder, previous); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { throw new IOException("The folder of the attachments is in use: " + e.Message, e); }
        }
        try { MoveFolder(stagedFolder, folder); }
        catch { if (previous != null) MoveFolder(previous, folder); throw; }
        try
        {
            if (File.Exists(output)) PublishOverwrite(stagedIndex, output, recovery);
            else File.Move(stagedIndex, output);
        }
        catch
        {
            MoveFolder(folder, stagedFolder);
            if (previous != null) MoveFolder(previous, folder);
            throw;
        }
        if (previous == null) return;
        // The previous generation: its attachments and archive are replaced; anything else stays, in a folder beside, and is said.
        // D2: the report is published at this point; a file of the previous generation still held makes a warning, not an error.
        try
        {
            var ours = new Regex("^" + Regex.Escape(plan.Stem) + @"_(A\d+\.docx|archivio\.zip)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            foreach (var file in Directory.EnumerateFiles(previous).Where(f => ours.IsMatch(Path.GetFileName(f))).ToArray()) File.Delete(file);
            if (!Directory.EnumerateFileSystemEntries(previous).Any()) { Directory.Delete(previous); return; }
            string kept = folder + "_precedente-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            MoveFolder(previous, kept);
            warnings.Add("Nella cartella degli allegati c'erano file non scritti da BridGHe: spostati in " + Path.GetFileName(kept) + ".");
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            warnings.Add("La generazione precedente degli allegati è rimasta nella cartella " + Path.GetFileName(previous) + " accanto al report (" + e.Message + "): il report nuovo è completo; controllarla e cancellarla a mano.");
        }
    }

    /// <summary>Tests only (BridGHe.Smoke, by reflection): called at the named steps of the publication of a report («overwrite-write»,
    /// «overwrite-restore», «move-folder», «remove-staging»), to inject the failures of a file system filter, of an antivirus or of the
    /// network. Null in the product.</summary>
    internal static Action<string> FaultInjection = null;
    private static void Fault(string step) => FaultInjection?.Invoke(step);

    /// <summary>Replaces <paramref name="output"/> with <paramref name="temporary"/> in place. <paramref name="recovery"/>, the copy of the
    /// original kept until the write or the restore succeeds, must be beside the report: never in a folder removed after a failure (D1).</summary>
    private static void PublishOverwrite(string temporary, string output, string recovery)
    {
        // Match the workbook exporter: an exclusive handle protects open Office documents and a
        // recovery copy protects an existing delivery if a filesystem filter interrupts the write.
        using var target = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using (var backup = File.Create(recovery)) { target.CopyTo(backup); backup.Flush(true); }
        bool restoredOrWritten = false;
        try
        {
            try
            {
                using var source = File.OpenRead(temporary); target.Position = 0;
                source.CopyTo(target); Fault("overwrite-write"); target.SetLength(source.Length); target.Flush(true); restoredOrWritten = true;
            }
            catch
            {
                Fault("overwrite-restore");
                using var backup = File.OpenRead(recovery); target.Position = 0;
                backup.CopyTo(target); target.SetLength(backup.Length); target.Flush(true); restoredOrWritten = true;
                throw;
            }
        }
        catch (Exception e) when (!restoredOrWritten) { throw new IOException("Recover the original report from: " + recovery, e); }
        finally { if (restoredOrWritten) File.Delete(recovery); }
    }

    internal static XDocument Read(Dictionary<string, byte[]> parts, string name)
    {
        if (!parts.TryGetValue(name, out var bytes)) throw new ArgumentException("Missing Word package part: " + name);
        using var s = new MemoryStream(bytes);
        using var reader = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 40_000_000 });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
    internal static byte[] Xml(XDocument document) { using var s = new MemoryStream(); document.Save(s, SaveOptions.DisableFormatting); return s.ToArray(); }
    /// <summary>P2K (P6): the ZIP entry of the JSON snapshot of the report.</summary>
    private const string SnapshotPart = "bridghe/report.json";

    private static string Text(XElement p) => string.Concat(p.Descendants(W + "t").Select(t => t.Value));
    private static void EnsureType(XDocument types, string part, string type)
    {
        var value = types.Root.Elements(Ct + "Override").SingleOrDefault(e => (string)e.Attribute("PartName") == part);
        if (value == null) types.Root.Add(new XElement(Ct + "Override", new XAttribute("PartName", part), new XAttribute("ContentType", type)));
        else value.SetAttributeValue("ContentType", type);
    }
    private static void ReplaceScalars(XElement p, Dictionary<string, string> values)
    {
        // Word may split a token across runs after spellchecking/formatting. Replace by character spans,
        // keeping the formatting of the first run and all text outside the token.
        foreach (Match match in Token.Matches(Text(p)).Cast<Match>().Reverse())
        {
            if (!values.TryGetValue(match.Groups[1].Value, out var replacement)) continue;
            int offset = 0, start = match.Index, end = start + match.Length;
            foreach (var node in p.Descendants(W + "t"))
            {
                string old = node.Value; int next = offset + old.Length;
                if (offset < end && next > start)
                {
                    string before = old[..Math.Max(0, start - offset)];
                    string after = old[Math.Min(old.Length, end - offset)..];
                    node.Value = before + (start >= offset ? replacement ?? "" : "") + after;
                    node.SetAttributeValue(XNamespace.Xml + "space", "preserve");
                }
                offset = next;
            }
        }
    }
}
