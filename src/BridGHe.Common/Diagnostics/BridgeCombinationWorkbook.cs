using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace BridGHe.Common.Diagnostics;

public sealed record BridgeCombinationWorkbookResult(string[][] Table, string[] Warnings);

/// <summary>Small OOXML interchange for the bridge combination table; does not run Excel formulas.</summary>
public static class BridgeCombinationWorkbook
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const long MaxPartSize = 32 * 1024 * 1024;
    private const int MaxCells = 1000000;

    public static string FilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Supply an .xlsx file path.");
        return Path.GetFullPath(path);
    }

    public static void Write(string path, BridgeCombinationCatalog catalog, int blankRows = 20, bool overwrite = false, string[][] table = null)
    {
        path = FilePath(path);
        if (blankRows < 1 || blankRows > 10000) throw new ArgumentException("Rows must be between 1 and 10000.");
        if (File.Exists(path) && !overwrite) throw new IOException("File already exists. Read it, choose a new path, or explicitly enable Overwrite.");
        var data = table == null ? new[] { new[] { "Combination" }.Concat(catalog.ActionIds).ToArray() } : catalog.ValidateTable(table);
        if ((long)Math.Max(blankRows + 1, data.Length) * (catalog.ActionIds.Length + 1) > MaxCells || data.Length > 10001)
            throw new ArgumentException("Too many combination cells. Reduce Rows or split the workbook (maximum 10000 combinations / 1000000 cells).");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = Path.Combine(Path.GetDirectoryName(path), ".bridghe-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
                var types = new XElement(ct + "Types",
                    new XElement(ct + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                    new XElement(ct + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")));
                foreach (var part in new[] { ("workbook.xml", "sheet.main"), ("styles.xml", "styles"), ("worksheets/sheet1.xml", "worksheet"), ("worksheets/sheet2.xml", "worksheet") })
                    types.Add(new XElement(ct + "Override", new XAttribute("PartName", "/xl/" + part.Item1), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml." + part.Item2 + "+xml")));
                Save(zip, "[Content_Types].xml", types);
                Save(zip, "_rels/.rels", new XElement(P + "Relationships", Relationship("rId1", "officeDocument", "xl/workbook.xml")));
                Save(zip, "xl/workbook.xml", new XElement(S + "workbook", new XAttribute(XNamespace.Xmlns + "r", R),
                    new XElement(S + "sheets", Sheet("Combinations", 1), Sheet("Actions", 2)), new XElement(S + "calcPr", new XAttribute("calcId", "0"), new XAttribute("fullCalcOnLoad", "1"))));
                Save(zip, "xl/_rels/workbook.xml.rels", new XElement(P + "Relationships",
                    Relationship("rId1", "worksheet", "worksheets/sheet1.xml"), Relationship("rId2", "worksheet", "worksheets/sheet2.xml"), Relationship("rId3", "styles", "styles.xml")));
                Save(zip, "xl/styles.xml", Styles());
                Save(zip, "xl/worksheets/sheet1.xml", Worksheet(data, Math.Max(blankRows + 1, data.Length), true));
                Save(zip, "xl/worksheets/sheet2.xml", Worksheet(catalog.ReferenceRows, catalog.ReferenceRows.Length, false));
            }
            // New files are renamed atomically. Explicit overwrites hold an exclusive handle and a
            // recovery copy; avoid replacing an open directory entry underneath Windows file filters.
            try { Publish(temporary, path, overwrite); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { throw new IOException("Cannot write the workbook. Close Excel and check the path/permissions. " + e.Message, e); }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Publish(string temporary, string path, bool overwrite)
    {
        if (!overwrite || !File.Exists(path)) { File.Move(temporary, path); return; }
        using var target = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        string recovery = temporary + ".recovery";
        try { using var backup = File.Create(recovery); target.CopyTo(backup); backup.Flush(true); }
        catch { if (File.Exists(recovery)) File.Delete(recovery); throw; }
        bool safeToDelete = false;
        try
        {
            try
            {
                using var source = File.OpenRead(temporary);
                target.Position = 0; source.CopyTo(target); target.SetLength(source.Length); target.Flush(true);
                safeToDelete = true;
            }
            catch
            {
                using var backup = File.OpenRead(recovery);
                target.Position = 0; backup.CopyTo(target); target.SetLength(backup.Length); target.Flush(true);
                safeToDelete = true;
                throw;
            }
        }
        catch (Exception e) when (!safeToDelete)
        { throw new IOException("Could not restore the workbook after a failed write. Recover the original from: " + recovery, e); }
        finally { if (safeToDelete) File.Delete(recovery); }
    }

    public static BridgeCombinationWorkbookResult Read(string path, BridgeCombinationCatalog catalog)
    {
        using var zip = ZipFile.OpenRead(FilePath(path));
        if (zip.Entries.Count > 2048 || zip.Entries.Sum(e => e.Length) > 128 * 1024 * 1024 || zip.Entries.Any(e => e.Length > MaxPartSize))
            throw new InvalidDataException("Workbook exceeds the supported size limits.");
        if (zip.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
            throw new InvalidDataException("Duplicate workbook parts.");
        var book = Load(zip, "xl/workbook.xml");
        var rels = Load(zip, "xl/_rels/workbook.xml.rels");
        var shared = zip.GetEntry("xl/sharedStrings.xml") == null ? Array.Empty<string>() : Load(zip, "xl/sharedStrings.xml")
            .Root.Elements(S + "si").Select(Text).ToArray();
        Dictionary<(int Row, int Column), Cell> ReadSheet(string name)
        {
            var sheets = book.Descendants(S + "sheet").Where(s => (string)s.Attribute("name") == name).ToArray();
            if (sheets.Length != 1) throw new InvalidDataException($"Missing or duplicate sheet '{name}'.");
            var id = (string)sheets[0].Attribute(R + "id");
            var links = rels.Root.Elements(P + "Relationship").Where(r => (string)r.Attribute("Id") == id).ToArray();
            if (links.Length != 1 || (string)links[0].Attribute("TargetMode") == "External" || (string)links[0].Attribute("Type") != R.NamespaceName + "/worksheet")
                throw new InvalidDataException("Invalid worksheet relationship.");
            string target = (string)links[0].Attribute("Target");
            if (string.IsNullOrEmpty(target) || target.Contains('\\') || target.Contains(':')) throw new InvalidDataException("Invalid worksheet path.");
            var uri = new Uri(new Uri("https://workbook.invalid/xl/workbook.xml"), target);
            if (uri.Host != "workbook.invalid" || !uri.AbsolutePath.StartsWith("/xl/", StringComparison.Ordinal)) throw new InvalidDataException("Invalid worksheet path.");
            var xml = Load(zip, Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')));
            var cells = new Dictionary<(int, int), Cell>();
            int count = 0;
            foreach (var c in xml.Root.Element(S + "sheetData")?.Elements(S + "row").SelectMany(row => row.Elements(S + "c")) ?? Enumerable.Empty<XElement>())
            {
                if (++count > MaxCells) throw new InvalidDataException("Worksheet contains too many cells.");
                var address = Address((string)c.Attribute("r"));
                string type = (string)c.Attribute("t");
                string value = (string)c.Element(S + "v") ?? "";
                if (type == "s")
                {
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) || index < 0 || index >= shared.Length) throw new InvalidDataException("Invalid shared string index.");
                    value = shared[index];
                }
                else if (type == "inlineStr") value = Text(c.Element(S + "is"));
                bool formula = c.Element(S + "f") != null;
                if (value.Length == 0 && !formula) continue;
                if (!cells.TryAdd(address, new Cell(value, formula, type == "e" || type == "b" || type == "d"))) throw new InvalidDataException("Duplicate cell address.");
            }
            return cells;
        }
        var reference = ReadSheet("Actions");
        for (int i = 0; i < catalog.ReferenceRows.Length; i++)
        for (int j = 0; j < catalog.ReferenceRows[i].Length; j++)
        {
            reference.Remove((i + 1, j + 1), out var cell);
            if ((cell?.Value ?? "") != catalog.ReferenceRows[i][j] || cell?.Formula == true || cell?.InvalidType == true)
                throw new ArgumentException($"Actions reference changed at {Column(j + 1)}{i + 1}: current phases/source cases differ from the workbook, or its Actions sheet was edited. Regenerate into a new file.");
        }
        if (reference.Count != 0) throw new ArgumentException("Actions sheet contains unexpected data. Regenerate the template.");
        var input = ReadSheet("Combinations");
        if (input.Count == 0 || input.Keys.Any(k => k.Column > catalog.ActionIds.Length + 1 || k.Row > 10001))
            throw new ArgumentException("Combinations has unexpected columns, too many rows, or no header.");
        var table = new List<string[]>(); bool formulas = false;
        foreach (int row in input.Keys.Select(k => k.Row).Append(1).Distinct().OrderBy(n => n))
        {
            var values = new string[catalog.ActionIds.Length + 1];
            for (int col = 1; col <= values.Length; col++)
            {
                input.TryGetValue((row, col), out var cell);
                if (cell?.InvalidType == true || cell?.Formula == true && (row == 1 || col == 1 || string.IsNullOrWhiteSpace(cell.Value)))
                    throw new ArgumentException($"Combinations!{Column(col)}{row}: invalid cell or formula without a numeric cached result. Use literal names; recalculate and save numeric formulas in Excel.");
                formulas |= cell?.Formula == true;
                values[col - 1] = cell?.Value ?? "";
            }
            table.Add(values);
        }
        var warnings = catalog.Warnings.ToList();
        if (formulas) warnings.Add("Excel formulas were read from their saved numeric results. BridGHe does not recalculate Excel: recalculate and save the workbook before each Read.");
        return new BridgeCombinationWorkbookResult(catalog.ValidateTable(table), warnings.ToArray());
    }

    private sealed record Cell(string Value, bool Formula, bool InvalidType);
    private static string Text(XElement element) => string.Concat(element?.Descendants(S + "t").Where(t => t.Parent?.Name != S + "rPh").Select(t => t.Value) ?? Enumerable.Empty<string>());
    private static (int Row, int Column) Address(string address)
    {
        if (string.IsNullOrEmpty(address)) throw new InvalidDataException("Missing cell address.");
        int i = 0, col = 0;
        while (i < address.Length && address[i] is >= 'A' and <= 'Z')
        {
            col = col * 26 + address[i++] - 'A' + 1;
            if (col > 16384) throw new InvalidDataException("Invalid column address.");
        }
        if (col == 0 || !int.TryParse(address.AsSpan(i), NumberStyles.None, CultureInfo.InvariantCulture, out int row) || row < 1 || row > 1048576) throw new InvalidDataException("Invalid cell address.");
        return (row, col);
    }
    private static string Column(int column)
    {
        string result = "";
        while (column > 0) { column--; result = (char)('A' + column % 26) + result; column /= 26; }
        return result;
    }
    private static XElement Sheet(string name, int id) => new(S + "sheet", new XAttribute("name", name), new XAttribute("sheetId", id), new XAttribute(R + "id", "rId" + id));
    private static XElement Relationship(string id, string kind, string target) => new(P + "Relationship", new XAttribute("Id", id), new XAttribute("Type", R.NamespaceName + "/" + kind), new XAttribute("Target", target));
    private static void Save(ZipArchive zip, string path, XElement xml)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        new XDocument(new XDeclaration("1.0", "utf-8", "yes"), xml).Save(stream);
    }
    private static XDocument Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Missing workbook part: " + path);
        if (entry.Length > MaxPartSize) throw new InvalidDataException("Workbook part exceeds size limit.");
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxPartSize });
        return XDocument.Load(reader);
    }
    private static XElement Worksheet(string[][] data, int rowCount, bool editable)
    {
        int columns = data[0].Length;
        var sheetData = new XElement(S + "sheetData");
        for (int i = 0; i < rowCount; i++)
        {
            var row = new XElement(S + "row", new XAttribute("r", i + 1), new XAttribute("ht", i == 0 ? 30 : 23), new XAttribute("customHeight", 1));
            for (int j = 0; j < columns; j++)
            {
                string value = i < data.Length ? data[i][j] : "";
                var cell = new XElement(S + "c", new XAttribute("r", Column(j + 1) + (i + 1)), new XAttribute("s", i == 0 ? 1 : editable ? j == 0 ? 2 : 3 : 4));
                if (editable && i > 0 && j > 0 && value.Length > 0) cell.Add(new XElement(S + "v", value));
                else cell.Add(new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), value)));
                row.Add(cell);
            }
            sheetData.Add(row);
        }
        var widths = new double[] { 24, 16, 25, 23, 12, 30, 26, 18, 18, 30, 15 };
        var result = new XElement(S + "worksheet", new XElement(S + "dimension", new XAttribute("ref", "A1:" + Column(columns) + rowCount)),
            new XElement(S + "sheetViews", new XElement(S + "sheetView", new XAttribute("workbookViewId", 0), new XAttribute("showGridLines", 0),
                new XElement(S + "pane", new XAttribute("xSplit", 1), new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "B2"), new XAttribute("activePane", "bottomRight"), new XAttribute("state", "frozen")))),
            new XElement(S + "cols", Enumerable.Range(0, columns).Select(j => new XElement(S + "col", new XAttribute("min", j + 1), new XAttribute("max", j + 1),
                new XAttribute("width", editable ? j == 0 ? 30 : Math.Clamp(data[0][j].Length + 3, 16, 38) : widths[j]), new XAttribute("customWidth", 1)))), sheetData,
            new XElement(S + "autoFilter", new XAttribute("ref", "A1:" + Column(columns) + rowCount)));
        return result;
    }
    private static XElement Styles()
    {
        XElement Font(string color, bool bold = false) => new(S + "font", bold ? new XElement(S + "b") : null, new XElement(S + "sz", new XAttribute("val", 11)), new XElement(S + "color", new XAttribute("rgb", color)), new XElement(S + "name", new XAttribute("val", "Calibri")));
        XElement Fill(string color) => new(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "solid"), new XElement(S + "fgColor", new XAttribute("rgb", color)), new XElement(S + "bgColor", new XAttribute("indexed", 64))));
        XElement Format(int font, int fill, bool numeric = false) => new(S + "xf", new XAttribute("fontId", font), new XAttribute("fillId", fill), new XAttribute("borderId", fill >= 3 ? 1 : 0), new XAttribute("numFmtId", 0), new XAttribute("xfId", 0), new XAttribute("applyAlignment", 1), new XElement(S + "alignment", new XAttribute("vertical", "center"), new XAttribute("horizontal", numeric ? "right" : "left")));
        return new XElement(S + "styleSheet",
            new XElement(S + "fonts", new XAttribute("count", 3), Font("FF203745"), Font("FFFFFFFF", true), Font("FF1D4E89")),
            new XElement(S + "fills", new XAttribute("count", 5), new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "none"))), new XElement(S + "fill", new XElement(S + "patternFill", new XAttribute("patternType", "gray125"))), Fill("FF17364B"), Fill("FFFFF5DC"), Fill("FFF0F4F6")),
            new XElement(S + "borders", new XAttribute("count", 2), new XElement(S + "border", new XElement(S + "left"), new XElement(S + "right"), new XElement(S + "top"), new XElement(S + "bottom"), new XElement(S + "diagonal")),
                new XElement(S + "border", new XElement(S + "left"), new XElement(S + "right"), new XElement(S + "top"), new XElement(S + "bottom", new XAttribute("style", "hair"), new XElement(S + "color", new XAttribute("rgb", "FFD9DFE2"))), new XElement(S + "diagonal"))),
            new XElement(S + "cellStyleXfs", new XAttribute("count", 1), new XElement(S + "xf", new XAttribute("numFmtId", 0), new XAttribute("fontId", 0), new XAttribute("fillId", 0), new XAttribute("borderId", 0))),
            new XElement(S + "cellXfs", new XAttribute("count", 5), Format(0, 0), Format(1, 2), Format(2, 3), Format(2, 3, true), Format(0, 4)),
            new XElement(S + "cellStyles", new XAttribute("count", 1), new XElement(S + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", 0), new XAttribute("builtinId", 0))));
    }
}
