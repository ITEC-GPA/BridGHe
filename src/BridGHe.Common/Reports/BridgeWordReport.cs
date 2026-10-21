using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace BridGHe.Common.Reports;

public sealed record BridgeReportExport(string File, string Id, int Results, int Checks, int Missing, string[] Warnings);

/// <summary>Template-based DOCX generation using OOXML only. Does not automate Word or execute template code.</summary>
public static class BridgeWordReport
{
    internal static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly Regex Token = new(@"\{\{BG\.([A-Za-z]+)\}\}", RegexOptions.Compiled);
    private static readonly string[] Scalars = { "Title", "Project", "Author", "Revision", "Date", "ReportId" };
    private static readonly string[] Blocks = Enum.GetNames<BridgeReportTopic>().Prepend("Summary").Prepend("Report").ToArray();

    public static BridgeReportExport Write(string template, string output, BridgeReportData data,
        IEnumerable<BridgeReportTopic> topics = null, IReadOnlyList<BridgeReportFigure> figures = null, bool overwrite = false)
    {
        template = Path.GetFullPath(template); output = Path.GetFullPath(output);
        if (!new[] { ".docx", ".dotx" }.Contains(Path.GetExtension(template).ToLowerInvariant())) throw new ArgumentException("Template must be .docx or .dotx.");
        if (!string.Equals(Path.GetExtension(output), ".docx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Report output must be .docx.");
        if (string.Equals(template, output, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The output must not replace the template.");
        if (File.Exists(output) && !overwrite) throw new IOException("Report already exists. Choose a new file or enable Overwrite.");
        if (data == null) throw new ArgumentNullException(nameof(data));
        var selected = (topics ?? BridgeReportData.DefaultTopics).Distinct().OrderBy(t => t).ToArray();
        if (selected.Any(t => !Enum.IsDefined(t))) throw new ArgumentException("Unknown report topic.");
        figures ??= Array.Empty<BridgeReportFigure>();
        var warnings = data.Warnings.ToList();
        if (selected.Contains(BridgeReportTopic.Plots) && figures.Count == 0) warnings.Add("Capitolo Grafici selezionato, ma nessun SVG fornito.");
        byte[] templateBytes = File.ReadAllBytes(template);
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
        var content = new BridgeReportContent(data, selected, figures, warnings, width, parts);
        var generatedTables = new HashSet<XElement>();
        foreach (var marker in markers)
        {
            var block = (marker.Key == "Report" ? content.All() : content.Chapter(marker.Key)).ToArray();
            foreach (var table in block.Where(e => e.Name == W + "tbl")) generatedTables.Add(table);
            marker.Value.ReplaceWith(block);
        }
        // Word merges consecutive tables, including their repeated header rows and column grids.
        // Separate generated tables without changing the template's own table layout or styles.
        var bodyElements = body.Elements().ToArray();
        for (int i = 1; i < bodyElements.Length; i++)
            if (bodyElements[i].Name == W + "tbl" && bodyElements[i - 1].Name == W + "tbl"
                && (generatedTables.Contains(bodyElements[i]) || generatedTables.Contains(bodyElements[i - 1])))
                bodyElements[i].AddBeforeSelf(new XElement(W + "p", new XElement(W + "pPr",
                    new XElement(W + "spacing", new XAttribute(W + "before", "0"), new XAttribute(W + "after", "0"),
                        new XAttribute(W + "line", "20"), new XAttribute(W + "lineRule", "exact")))));
        var values = new Dictionary<string, string> { ["Title"] = data.Metadata.Title, ["Project"] = data.Metadata.Project,
            ["Author"] = data.Metadata.Author, ["Revision"] = data.Metadata.Revision, ["Date"] = data.Created.ToString("yyyy-MM-dd HH:mm zzz"), ["ReportId"] = data.Id };
        foreach (var item in texts)
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
        EnsureType(types, "/bridghe/report.json", "application/json");
        foreach (var name in parts.Keys.Where(n => n.StartsWith("word/media/bridghe-")))
            EnsureType(types, "/" + name, name.EndsWith(".svg") ? "image/svg+xml" : "image/png");
        parts["[Content_Types].xml"] = Xml(types);
        parts["bridghe/report.json"] = Encoding.UTF8.GetBytes(BridgeReportData.Serialize(new { Schema = 1, TemplateHash = BridgeReportData.Hash(templateBytes),
            Topics = selected.Select(t => t.ToString()), Data = data, ExportWarnings = warnings,
            Figures = figures.Select(f => new { f.Title, Hash = BridgeReportData.Hash(Encoding.UTF8.GetBytes(f.Svg ?? "")) }) }));
        var rels = Read(parts, "_rels/.rels");
        rels.Root.Add(new XElement(Rel + "Relationship", new XAttribute("Id", "bg" + Guid.NewGuid().ToString("N")),
            new XAttribute("Type", "urn:bridghe:report:snapshot"), new XAttribute("Target", "bridghe/report.json")));
        parts["_rels/.rels"] = Xml(rels);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        string temporary = Path.Combine(Path.GetDirectoryName(output), ".bridghe-report-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var result = ZipFile.Open(temporary, ZipArchiveMode.Create))
                foreach (var part in parts) { using var stream = result.CreateEntry(part.Key, CompressionLevel.Optimal).Open(); stream.Write(part.Value); }
            if (File.Exists(output))
            {
                if (!overwrite) throw new IOException("Report already exists.");
                PublishOverwrite(temporary, output);
            }
            else File.Move(temporary, output);
        }
        catch (IOException e) { throw new IOException("Cannot publish the report. Close the destination in Word and check its path. " + e.Message, e); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new(output, data.Id, data.Samples.Length, data.Rows.Count(), data.Rows.Count(r => r.Check.Active && !r.Check.HasRatio), warnings.ToArray());
    }

    private static void PublishOverwrite(string temporary, string output)
    {
        // Match the workbook exporter: an exclusive handle protects open Office documents and a
        // recovery copy protects an existing delivery if a filesystem filter interrupts the write.
        using var target = new FileStream(output, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        string recovery = temporary + ".recovery";
        using (var backup = File.Create(recovery)) { target.CopyTo(backup); backup.Flush(true); }
        bool restoredOrWritten = false;
        try
        {
            try
            {
                using var source = File.OpenRead(temporary); target.Position = 0;
                source.CopyTo(target); target.SetLength(source.Length); target.Flush(true); restoredOrWritten = true;
            }
            catch
            {
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
