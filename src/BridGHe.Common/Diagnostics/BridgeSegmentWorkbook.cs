using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;

namespace BridGHe.Common.Diagnostics;

public sealed record BridgeSegmentWorkbookResult(string[] Table, string[] Warnings);

/// <summary>Section dimensions keyed by fabrication segment, independent of FEM mesh and material assignment.</summary>
public static class BridgeSegmentWorkbook
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static readonly string[] Headers = BridgeSegmentSections.Header.TrimStart('#', ' ').Split(';').Select(s => s.Trim()).ToArray();
    public static string[] Template(SimpleBridgeGeometry geometry, BeamPropertyModel property = null) => property == null
        ? BridgeSegmentSections.Synchronize(geometry, null).Table
        : BridgeSegmentSections.Create(geometry, property, null).ResolvedTable;

    public static void Write(string path, SimpleBridgeGeometry geometry, BeamPropertyModel property = null, bool overwrite = false, IEnumerable<string> initialTable = null)
    {
        path = BridgeCombinationWorkbook.FilePath(path);
        var table = initialTable == null ? Template(geometry, property) : BridgeSegmentSections.ValidateTable(geometry, initialTable);
        if (table.Length > 100001) throw new ArgumentException("Maximum 100000 fabrication segments per workbook.");
        if (File.Exists(path) && !overwrite) throw new IOException("File already exists. Use Read, choose another filename, or explicitly enable Overwrite.");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temporary = Path.Combine(Path.GetDirectoryName(path), ".bridghe-sections-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var resource = typeof(BridgeSegmentWorkbook).Assembly.GetManifestResourceStream("BridGHe.Common.Resources.BridgeSegmentSections.xlsx")
                ?? throw new InvalidOperationException("Missing embedded section workbook template."))
            using (var target = File.Create(temporary)) resource.CopyTo(target);
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Update))
            {
                const string part = "xl/worksheets/sheet1.xml";
                var entry = zip.GetEntry(part) ?? throw new InvalidDataException("Invalid embedded template.");
                XDocument xml;
                using (var stream = entry.Open()) xml = XDocument.Load(stream);
                var data = xml.Root.Element(S + "sheetData");
                var sample = data.Elements(S + "row").Single(r => (int)r.Attribute("r") == 7);
                var styles = sample.Elements(S + "c").Select(c => (string)c.Attribute("s")).ToArray();
                if (styles.Length != 10) throw new InvalidDataException("Invalid section template columns.");
                data.Elements(S + "row").Where(r => (int)r.Attribute("r") >= 7).Remove();
                XElement Cell(int row, int column, string value, string style = null)
                {
                    var c = new XElement(S + "c", new XAttribute("r", ((char)('A' + column)).ToString() + row));
                    if (style != null) c.SetAttributeValue("s", style);
                    if (column > 0 && double.TryParse(value, NumberStyles.Float, Inv, out double number)) c.Add(new XElement(S + "v", number.ToString("R", Inv)));
                    else c.Add(new XAttribute("t", "inlineStr"), new XElement(S + "is", new XElement(S + "t", value)));
                    return c;
                }
                var summary = data.Elements(S + "row").Single(r => (int)r.Attribute("r") == 3).Elements(S + "c").Single(c => (string)c.Attribute("r") == "A3");
                summary.ReplaceWith(Cell(3, 0, $"{table.Length - 1} conci, {geometry.Segments.Select(s => s.Girder).Distinct().Count()} travi. Progressive e dimensioni in mm.", (string)summary.Attribute("s")));
                for (int i = 1; i < table.Length; i++)
                {
                    var row = new XElement(S + "row", new XAttribute("r", i + 6), new XAttribute("ht", 24), new XAttribute("customHeight", 1));
                    var cells = table[i].Split(';').Select(s => s.Trim()).ToArray();
                    for (int col = 0; col < cells.Length; col++) row.Add(Cell(i + 6, col, cells[col], styles[col]));
                    data.Add(row);
                }
                string range = "A6:J" + (table.Length + 5);
                xml.Root.Element(S + "dimension")?.SetAttributeValue("ref", "A1:J" + (table.Length + 5));
                xml.Root.Element(S + "autoFilter")?.Remove();
                data.AddAfterSelf(new XElement(S + "autoFilter", new XAttribute("ref", range)));
                entry.Delete();
                using var output = zip.CreateEntry(part, CompressionLevel.Optimal).Open(); xml.Save(output);
            }
            BridgeCombinationWorkbook.Publish(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static BridgeSegmentWorkbookResult Read(string path, SimpleBridgeGeometry geometry)
    {
        var cells = BridgeCombinationWorkbook.ReadSheets(path, "Sezioni")["Sezioni"];
        for (int c = 1; c <= 10; c++)
            if (!cells.TryGetValue((6, c), out var header) || header.Value != Headers[c - 1] || header.Formula || header.InvalidType)
                throw new ArgumentException($"Sezioni!{(char)('A' + c - 1)}6: keep the original column heading '{Headers[c - 1]}'.");
        if (cells.Keys.Any(k => k.Row >= 7 && (k.Column > 10 || k.Row > 100006))) throw new ArgumentException("Unexpected data outside the section table.");
        bool formulas = false;
        var lines = new List<string> { BridgeSegmentSections.Header };
        foreach (int r in cells.Keys.Where(k => k.Row >= 7).Select(k => k.Row).Distinct().OrderBy(r => r))
        {
            var row = new string[10];
            for (int c = 1; c <= 10; c++)
            {
                cells.TryGetValue((r, c), out var cell);
                if (cell == null || cell.InvalidType || string.IsNullOrWhiteSpace(cell.Value) ||
                    cell.Formula && (c <= 3 || !double.TryParse(cell.Value, NumberStyles.Float, Inv, out double n) || !double.IsFinite(n)))
                    throw new ArgumentException($"Sezioni!{(char)('A' + c - 1)}{r}: enter a dimension or '*'. Keep ID and stations literal; recalculate and save numeric formulas in Excel.");
                formulas |= cell.Formula;
                row[c - 1] = cell.Value;
            }
            lines.Add(string.Join("; ", row));
        }
        var table = BridgeSegmentSections.ValidateTable(geometry, lines);
        return new(table, formulas ? new[] { "Numeric formulas use their saved Excel results. Recalculate and save Excel before Read; BridGHe does not evaluate Excel formulas." } : Array.Empty<string>());
    }
}
