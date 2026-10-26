using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Geometry;
using Rhino.Geometry;

namespace BridGHe.Common.MovingLoads;

public sealed record BridgeMovingCombinationTable(string[][] Rows, string[] ActionIds, string[] Cases);

/// <summary>Explicit alternative traffic positions; no normative factors and no envelope of non-concomitant forces.</summary>
public static class BridgeMovingWorkflow
{
    public static BridgeMovingCombinationTable Combinations(IReadOnlyList<string[]> table, IReadOnlyList<string> cases,
        string baseCombination, double factor, bool isolated, string prefix)
    {
        if (table == null || table.Count < 2 || table[0] == null || table[0].Length < 2)
            throw new ArgumentException("Provide a header and at least one base combination.");
        int width = table[0].Length;
        if (table.Any(r => r == null || r.Length != width) || table[0].Skip(1).Any(string.IsNullOrWhiteSpace)
            || table[0].Skip(1).Distinct(StringComparer.Ordinal).Count() != width - 1)
            throw new ArgumentException("Invalid combination dimensions or duplicate/empty Action IDs.");
        foreach (var row in table.Skip(1))
            if (string.IsNullOrWhiteSpace(row[0]) || row.Skip(1).Any(v => !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n)))
                throw new ArgumentException("Base combination names and finite numeric coefficients are required.");
        if (table.Skip(1).Select(r => r[0]).Distinct(StringComparer.Ordinal).Count() != table.Count - 1)
            throw new ArgumentException("Duplicate base combination names.");
        var basis = table.Skip(1).SingleOrDefault(r => r[0] == baseCombination)
            ?? throw new ArgumentException("Base combination not found: " + baseCombination);
        if (cases == null || cases.Count == 0 || cases.Any(string.IsNullOrWhiteSpace) || cases.Distinct(StringComparer.Ordinal).Count() != cases.Count)
            throw new ArgumentException("Provide distinct cases from Moving Static Analysis.");
        if (!double.IsFinite(factor) || factor < 0 || string.IsNullOrWhiteSpace(prefix) || prefix != prefix.Trim())
            throw new ArgumentException("A finite non-negative traffic factor and a trimmed Action prefix are required.");
        var ids = Enumerable.Range(1, cases.Count).Select(i => $"{prefix}_{i:D4}").ToArray();
        if (ids.Intersect(table[0].Skip(1), StringComparer.Ordinal).Any()) throw new ArgumentException("Moving Action IDs collide with the base table.");
        var rows = new List<string[]> { table[0].Concat(ids).ToArray() };
        rows.AddRange(table.Skip(1).Select(r => r.Concat(Enumerable.Repeat("0", cases.Count)).ToArray()));
        for (int i = 0; i < cases.Count; i++)
        {
            var row = basis.Concat(Enumerable.Repeat("0", cases.Count)).ToArray();
            row[0] = baseCombination + " + " + ids[i]; row[width + i] = factor.ToString("G17", CultureInfo.InvariantCulture); rows.Add(row);
            if (isolated)
            {
                var only = Enumerable.Repeat("0", width + cases.Count).ToArray();
                only[0] = "TEST_" + ids[i]; only[width + i] = row[width + i]; rows.Add(only);
            }
        }
        if (rows.Skip(1).Select(r => r[0]).Distinct(StringComparer.Ordinal).Count() != rows.Count - 1)
            throw new ArgumentException("Generated combination names collide with existing names; change the Action prefix.");
        return new(rows.ToArray(), ids, cases.ToArray());
    }

    /// <summary>Path on the actual transverse slab axes. Rejects curvature/warping instead of substituting a chord.</summary>
    public static MovingPath StraightPath(SimpleBridgeGeometry geometry, MovingTemplate template, string name, double offset, int divisions)
    {
        BridgeGeometryTools.Validate(geometry); ArgumentNullException.ThrowIfNull(template); template.Validate();
        if (!double.IsFinite(offset) || divisions < 1 || divisions > 2000) throw new ArgumentException("Finite offset and 1..2000 path divisions required.");
        var frame = geometry.Frames[0]; var last = geometry.Frames[^1];
        const double tolerance = .01; // mm, geometric coplanarity only
        if (geometry.Frames.Any(f => (f.XAxis - frame.XAxis).Length > 1e-8 || (f.YAxis - frame.YAxis).Length > 1e-8
            || Math.Abs((f.Origin - frame.Origin) * frame.XAxis) > tolerance || Math.Abs((f.Origin - frame.Origin) * frame.YAxis) > tolerance))
            throw new ArgumentException("A straight alignment with constant crossfall is required. Define curved paths explicitly with Moving Load Path.");
        var deck = geometry.Members.Where(m => m.Family == BridgeMemberFamily.DeckTransverse).ToArray();
        if (deck.Length == 0) throw new ArgumentException("Connect Grillage Geometry containing transverse slab members.");
        double level = (deck[0].Axis.From - frame.Origin) * frame.YAxis;
        if (deck.SelectMany(m => new[] { m.Axis.From, m.Axis.To }).Any(p => Math.Abs((p - frame.Origin) * frame.YAxis - level) > tolerance))
            throw new ArgumentException("Slab beam axes are not coplanar.");
        if (template.LaneFactors.Length != 1 || !string.IsNullOrEmpty(template.LibraryName))
            throw new ArgumentException("This helper requires an explicit one-lane template so its footprint can be checked against the slab.");
        var footprint = template.Vehicles.SelectMany(v => v.Loads).SelectMany(l => l switch {
            MovingPointForce p => new[] { p.Y }, MovingDistributedForce d => new[] { d.Y1, d.Y2 },
            _ => throw new ArgumentException("Use structural point/distributed forces for this slab path.") }).ToArray();
        foreach (var station in deck.GroupBy(m => m.StationFrom))
        {
            var y = station.SelectMany(m => new[] { m.Axis.From, m.Axis.To }).Select(p => (p - frame.Origin) * frame.XAxis).ToArray();
            if (footprint.Any(v => offset + v < y.Min() - tolerance || offset + v > y.Max() + tolerance))
                throw new ArgumentException("The vehicle footprint extends beyond the slab. Reduce lane offset or vehicle width.");
        }
        var start = frame.PointAt(offset, level); var end = last.PointAt(offset, level); var lateral = start + 1000 * frame.XAxis;
        var path = new MovingPath { Template = (MovingTemplate)template.Clone(), LoadCaseName = name, Shape = 0, Target = 2,
            Divisions = divisions, ConvertToStaticCases = true,
            Coordinates = new[] { start.X, start.Y, start.Z, end.X, end.Y, end.Z, lateral.X, lateral.Y, lateral.Z } };
        path.Validate(); return path;
    }
}
