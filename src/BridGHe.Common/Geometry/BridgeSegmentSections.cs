using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Geometry;

public sealed record BridgeSegmentSectionResult(BridgeSectionRule[] Rules, string[] Table, string[] ResolvedTable);
public sealed record BridgeSegmentTableUpdate(string[] Table, string[] Changes);

/// <summary>Editable, keyed section dimensions. Mesh refinement never determines section identity.</summary>
public static class BridgeSegmentSections
{
    public const string Header = "# ID; s_inizio_mm; s_fine_mm; b_inf_mm; b_sup_mm; h_mm; t_inf_mm; t_sup_mm; tw_mm; r_mm";
    public const double StationTolerance = 1e-5; // mm; consistent with bridge joint canonicalization.
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private sealed record Row(string Id, double Start, double End, double?[] Dimensions);
    public static string Id(BridgeSegmentRange segment) => $"G{segment.Girder}-C{segment.Segment}";
    private static string Number(double value) => value.ToString("0.######", Inv);

    public static BridgeSegmentRange[] Segments(SimpleBridgeGeometry geometry)
    {
        BridgeGeometryTools.Validate(geometry);
        var result = geometry.Segments.OrderBy(s => s.Girder).ThenBy(s => s.Segment).ToArray();
        if (result.Length == 0 || result.Select(Id).Distinct().Count() != result.Length ||
            result.Any(s => s.Girder < 1 || s.Segment < 1 || !double.IsFinite(s.Start) || !double.IsFinite(s.End) || s.Start < 0 || s.End <= s.Start))
            throw new ArgumentException("Bridge Geometry requires distinct, valid girder fabrication segments.");
        return result;
    }

    private static double ParseNumber(string text, string context)
    {
        // Semicolon or tab separates columns. A decimal comma is accepted, thousands separators are not.
        if (!double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, Inv, out double value) || !double.IsFinite(value))
            throw new ArgumentException($"{context}: expected a finite number in mm, found '{text}'.");
        return value;
    }

    private static Dictionary<string, Row> Parse(IEnumerable<string> table)
    {
        var result = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
        int lineNumber = 0;
        foreach (string line in (table ?? Array.Empty<string>()).SelectMany(s => (s ?? "").Replace("\r", "").Split('\n')))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            string[] cells = line.Split(line.Contains(';') ? ';' : '\t').Select(s => s.Trim()).ToArray();
            if (cells.Length != 10) throw new ArgumentException($"Section table, line {lineNumber}: expected 10 columns separated by ';' or tabs. {Header}");
            string id = cells[0].ToUpperInvariant();
            if (!Regex.IsMatch(id, @"^G[1-9]\d*-C[1-9]\d*$")) throw new ArgumentException($"Section table, line {lineNumber}: invalid ID '{id}'; use G1-C1, G1-C2, G2-C1, etc.");
            double start = ParseNumber(cells[1], id + " start"), end = ParseNumber(cells[2], id + " end");
            if (start < 0 || end <= start) throw new ArgumentException($"{id}: require 0 <= start < end.");
            var dims = cells.Skip(3).Select((s, i) => s == "*" ? (double?)null : ParseNumber(s, id + " column " + (i + 4))).ToArray();
            if (dims.Select((v, i) => v.HasValue && (i == 6 ? v < 0 : v <= 0)).Any(b => b))
                throw new ArgumentException($"{id}: section dimensions must be positive; radius may be zero. '*' inherits the base section.");
            if (!result.TryAdd(id, new Row(id, start, end, dims))) throw new ArgumentException($"Duplicate section row: {id}.");
        }
        return result;
    }

    private static bool SameRange(Row row, BridgeSegmentRange s) =>
        Math.Abs(row.Start - s.Start) <= StationTolerance && Math.Abs(row.End - s.End) <= StationTolerance;
    private static string Line(BridgeSegmentRange s, IEnumerable<double?> dimensions) =>
        string.Join("; ", new[] { Id(s), Number(s.Start), Number(s.End) }.Concat(dimensions.Select(v => v.HasValue ? v.Value.ToString("G", Inv) : "*")));

    /// <summary>Explicit refresh preserves only rows whose key AND physical range still match. No file writes.</summary>
    public static BridgeSegmentTableUpdate Synchronize(SimpleBridgeGeometry geometry, IEnumerable<string> table)
    {
        var segments = Segments(geometry); var old = Parse(table); var changes = new List<string>();
        var lines = new List<string> { Header };
        foreach (var s in segments)
        {
            bool exists = old.TryGetValue(Id(s), out var row);
            bool keep = exists && SameRange(row, s);
            lines.Add(Line(s, keep ? row.Dimensions : new double?[7]));
            if (!keep) changes.Add($"{Id(s)}: {(exists ? "range changed; reset to base (*)" : "new segment; base (*)")}.");
        }
        var keys = segments.Select(Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        changes.AddRange(old.Keys.Where(id => !keys.Contains(id)).Select(id => $"{id}: removed from current geometry."));
        return new(lines.ToArray(), changes.ToArray());
    }

    public static BridgeSegmentSectionResult Create(SimpleBridgeGeometry geometry, BeamPropertyModel baseProperty, IEnumerable<string> table)
    {
        var segments = Segments(geometry);
        if (baseProperty?.IsValid != true || baseProperty.SectionType != SectionModel.SectionTypes.I ||
            baseProperty.Section.Mirror != SectionModel.MirrorTypes.None || baseProperty.Section.NativeSection != null ||
            baseProperty.PropertyType != BeamPropertyType.Beam || baseProperty.Material?.Kind != MaterialModel.MaterialKind.Steel)
            throw new ArgumentException("Base Property must be an unmirrored I Section with Steel Material and Beam formulation.");
        var rows = MatchRows(segments, table, true);
        var rules = new List<BridgeSectionRule>(); var normalized = new List<string> { Header }; var resolved = new List<string> { Header };
        double[] defaults = { baseProperty.B1, baseProperty.B2, baseProperty.D, baseProperty.T1, baseProperty.T2, baseProperty.T3, baseProperty.R };
        foreach (var s in segments)
        {
            var row = rows[Id(s)];
            double[] v = row.Dimensions.Select((value, i) => value ?? defaults[i]).ToArray();
            if (v.Any(value => !double.IsFinite(value)) || v.Take(6).Any(value => value <= 0) || v[6] < 0 ||
                v[2] <= v[3] + v[4] || v[5] > Math.Min(v[0], v[1]) ||
                2 * v[6] > Math.Min(v[2] - v[3] - v[4], Math.Min(v[0], v[1]) - v[5]))
                throw new ArgumentException($"{Id(s)}: invalid I section. Check positive dimensions, clear web height, web/flange widths and fillet clearance.");
            var section = new SectionModel { Id = Id(s), Name = "Sezione " + Id(s), SectionType = SectionModel.SectionTypes.I,
                B1 = v[0], B2 = v[1], D = v[2], T1 = v[3], T2 = v[4], T3 = v[5], R = v[6],
                IncludeTorsionalStiffness = baseProperty.IncludeTorsionalStiffness };
            section.CalculateSection(); // The same GPC-backed path as the existing I Section component.
            if (!double.IsFinite(section.SectionArea) || section.SectionArea <= 0 || !double.IsFinite(section.Ixx) || section.Ixx <= 0 ||
                !double.IsFinite(section.Iyy) || section.Iyy <= 0) throw new ArgumentException($"{Id(s)}: invalid calculated section properties.");
            var property = ModelArchive.Clone(baseProperty);
            property.Id = "Concio " + Id(s); property.Name = property.Id; property.Section = section;
            var rule = new BridgeSectionRule { Family = 0, Group = s.Girder, Segment = s.Segment, Start = s.Start, End = s.End, Property = property };
            rule.Validate(); rules.Add(rule);
            normalized.Add(Line(s, row.Dimensions)); resolved.Add(Line(s, v.Select(value => (double?)value)));
        }
        return new(rules.ToArray(), normalized.ToArray(), resolved.ToArray());
    }

    /// <summary>Validate an explicit table without requiring materials or constructing FEM sections.</summary>
    public static string[] ValidateTable(SimpleBridgeGeometry geometry, IEnumerable<string> table)
    {
        var segments = Segments(geometry);
        var rows = MatchRows(segments, table, false);
        return new[] { Header }.Concat(segments.Select(s => Line(s, rows[Id(s)].Dimensions))).ToArray();
    }

    private static Dictionary<string, Row> MatchRows(BridgeSegmentRange[] segments, IEnumerable<string> table, bool allowEmpty)
    {
        var rows = Parse(table);
        if (allowEmpty && rows.Count == 0) rows = Parse(segments.Select(s => Line(s, new double?[7])));
        var keys = segments.Select(Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = keys.Except(rows.Keys, StringComparer.OrdinalIgnoreCase).ToArray();
        var extra = rows.Keys.Except(keys, StringComparer.OrdinalIgnoreCase).ToArray();
        if (missing.Length > 0 || extra.Length > 0)
            throw new ArgumentException($"Section table differs from geometry. Missing: {string.Join(", ", missing)}. Obsolete: {string.Join(", ", extra)}. Use 'Aggiorna tabella conci' on the component.");
        foreach (var s in segments)
        {
            var row = rows[Id(s)];
            if (!SameRange(row, s)) throw new ArgumentException($"{Id(s)}: fabrication range changed. Use 'Aggiorna tabella conci'; section overrides on changed ranges are reset explicitly.");
        }
        return rows;
    }
}
