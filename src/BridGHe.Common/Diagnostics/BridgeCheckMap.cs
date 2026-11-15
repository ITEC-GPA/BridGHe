using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Diagnostics;

public sealed record BridgeMappedCheck(string BeamId, double Station, string Phase, string Case, string Check, double? Ratio, string Status, string Note);
public sealed record BridgeMappedMember(string BeamId, Line Axis, double Maximum, int Unavailable, Color Color, string Label, string Governing);
/// <summary>R6.5 P1 lane 3a: a beam as the map reads it: its BridGHe ID, its axis and its length in model length units.</summary>
public sealed record BridgeMapBeam(string BeamId, Line Axis, double Length);

public static class BridgeCheckMap
{

    /// <summary>R6.5 P1 lane 3a: the map on the beams of a model as the map reads them (the FEM beams, or those of a GPC model in model
    /// length units), in model order.</summary>
    public static BridgeMappedMember[] Evaluate(IReadOnlyList<BridgeMapBeam> modelBeams, IReadOnlyList<BridgeMappedCheck> checks)
    {
        ArgumentNullException.ThrowIfNull(modelBeams); ArgumentNullException.ThrowIfNull(checks);
        if (modelBeams.Select(b => b.BeamId).Distinct().Count() != modelBeams.Count)
            throw new ArgumentException("Check map requires unique beam IDs.");
        var beams = modelBeams.ToDictionary(b => b.BeamId, StringComparer.Ordinal);
        foreach (var c in checks)
        {
            if (c == null || !beams.TryGetValue(c.BeamId, out var beam) || !double.IsFinite(c.Station) ||
                c.Station < 0 || c.Station > beam.Length + 1e-6 || string.IsNullOrWhiteSpace(c.Check) ||
                c.Ratio is double ratio && ratio < 0)
                throw new ArgumentException("Invalid check mapping: verify beam ID, station, check label and nonnegative ratio.");
        }
        var grouped = checks.ToLookup(c => c.BeamId, StringComparer.Ordinal);
        return modelBeams.Select(b =>
        {
            var rows = grouped[b.BeamId].ToArray();
            int unavailable = rows.Count(c => c.Ratio == null || double.IsNaN(c.Ratio.Value));
            var known = rows.Where(c => c.Ratio.HasValue && !double.IsNaN(c.Ratio.Value))
                .OrderByDescending(c => c.Ratio).ThenBy(c => c.Case, StringComparer.Ordinal).ThenBy(c => c.Station).ToArray();
            double max = known.Length == 0 ? double.NaN : known[0].Ratio.Value;
            Color color = known.Length == 0 ? Color.SlateGray : max > 1 ? Color.Crimson :
                unavailable > 0 ? Color.DarkOrange : Color.FromArgb((int)(35 + 175 * Math.Clamp(max, 0, 1)), (int)(165 - 45 * Math.Clamp(max, 0, 1)), 70);
            string governing = known.Length == 0 ? "" : string.Join(" | ", known.Where(c => c.Ratio == max)
                .Select(c => $"{c.Check}; {c.Phase}; {c.Case}; x={c.Station:G8}").Distinct());
            string label = rows.Length == 0 ? $"{b.BeamId}: no supplied checks" :
                $"{b.BeamId}: max={max:G4}; {unavailable} unavailable / {rows.Length} supplied checks";
            return new BridgeMappedMember(b.BeamId, b.Axis, max, unavailable, color, label, governing);
        }).ToArray();
    }
}
