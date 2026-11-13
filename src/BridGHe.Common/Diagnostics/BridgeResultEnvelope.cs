using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using GPC.Model.Models;

namespace BridGHe.Common.Diagnostics;

/// <param name="Source">The stored result of the governing value: a GPC result row (<see cref="GpcResultRows.BeamRow"/> or
/// <see cref="GpcResultRows.NodeRow"/>) for an analysed GPC model, the former result object for a FEM model.</param>
public sealed record EnvelopeRow(string Element, double Station, string Case, double Step, string StepType,
    double Value, double[] Components, object Source, string Coverage);

/// <summary>Envelopes stored results only. Never combines independent component maxima or interpolates steps.</summary>
public static class BridgeResultEnvelope
{

    /// <summary>One stored result as the envelope reads it: element ID, station, case identity, the six components and its source.</summary>
    private readonly record struct Sample(string Id, double X, string Case, double Step, string StepType, double[] V, object Source);


    /// <summary>F6: the envelope of an analysed GPC model (<see cref="GpcResultCatalog"/>), from the rows the Results components show,
    /// in the definition units: the same rows, in the same order, as the former result objects the analysis wrote from them. Every stored
    /// result is a case of the catalog, step 0, no step type (as the former result objects).</summary>
    public static EnvelopeRow[] Evaluate(Model analysed, int kind, IReadOnlyList<string> ids, IReadOnlyList<string> cases, int component, int mode)
    {
        ArgumentNullException.ThrowIfNull(analysed);
        Arguments(kind, component, mode);
        var catalog = GpcResultCatalog.Of(analysed) ?? throw new ArgumentException("The model has no stored results of a BridGHe analysis.");
        var beams = analysed.BeamElements.Values.ToArray(); var nodes = analysed.NodesElements.Values.ToArray();
        var availableIds = (kind == 0 ? beams.Select(b => b.Name) : nodes.Select(n => n.Name)).ToArray();
        var selectedIds = SelectIds(ids, availableIds);
        string Label(int entry) => catalog.Entries[entry].Label;
        IEnumerable<Sample> input = kind == 0
            ? beams.Where(b => selectedIds.Contains(b.Name)).SelectMany(b => GpcResultRows.Beam(catalog, analysed, b)
                .Select(r => new Sample(b.Name, r.Station, Label(r.Entry), 0, "", Shown(r.Axial, r.Shear1, r.Shear2, r.Torque, r.Bending1, r.Bending2), r)))
            : nodes.Where(n => selectedIds.Contains(n.Name)).SelectMany(n => GpcResultRows.Node(catalog, analysed, n)
                .Where(r => (kind == 1 ? r.Reaction : r.Displacement) != null)
                .Select(r => new Sample(n.Name, 0d, Label(r.Entry), 0, "", Shown(kind == 1 ? r.Reaction : r.Displacement), r)));
        var source = input.ToArray();
        if (source.Length == 0) throw new ArgumentException("No stored results of the requested type for the selected elements.");
        var lengths = kind == 0 ? beams.ToDictionary(b => b.Name, b => catalog.Units.FromMm(b.Length), StringComparer.Ordinal) : null;
        return Envelope(source, selectedIds, lengths, cases, component, mode);
    }

    private static double[] Shown(params double[] values) => values.Select(GpcResultRows.Shown).ToArray();

    private static void Arguments(int kind, int component, int mode)
    {
        if (kind is < 0 or > 2 || component is < 0 or > 5 || mode is < 0 or > 2)
            throw new ArgumentException("Kind 0 beam/1 reaction/2 displacement; component 0..5; mode 0 minimum/1 maximum/2 absolute.");
    }

    private static HashSet<string> SelectIds(IReadOnlyList<string> ids, string[] availableIds)
    {
        if (availableIds.Any(string.IsNullOrWhiteSpace) || availableIds.Distinct().Count() != availableIds.Length)
            throw new ArgumentException("Element IDs must be non-empty and unique.");
        return Select(ids, availableIds, "element");
    }

    private static EnvelopeRow[] Envelope(Sample[] source, HashSet<string> selectedIds, Dictionary<string, double> lengths,
        IReadOnlyList<string> cases, int component, int mode)
    {
        if (lengths != null && source.Any(r => !double.IsFinite(r.X) || r.X < 0 || r.X > lengths[r.Id] + 1e-9 * Math.Max(1, lengths[r.Id])))
            throw new ArgumentException("A beam result station lies outside the element length.");
        if (source.Any(r => string.IsNullOrWhiteSpace(r.Case))) throw new ArgumentException("Stored result has no case identity.");
        var selectedCases = Select(cases, source.Select(r => r.Case).Distinct().ToArray(), "case");
        var filtered = source.Where(r => selectedCases.Contains(r.Case)).ToArray();
        var missingIds = selectedIds.Except(filtered.Select(r => r.Id)).ToArray();
        if (missingIds.Length > 0) throw new ArgumentException("No selected-case results for elements: " + string.Join(", ", missingIds));
        var rows = new List<EnvelopeRow>();
        foreach (var group in filtered.GroupBy(r => (r.Id, r.X)).OrderBy(g => g.Key.Id, StringComparer.Ordinal).ThenBy(g => g.Key.X))
        {
            if (!double.IsFinite(group.Key.X) || group.Key.X < 0) throw new ArgumentException("Invalid result station.");
            var data = group.ToArray();
            if (data.Any(r => !double.IsFinite(r.Step) || r.V.Any(v => !double.IsFinite(v)))) throw new ArgumentException("Non-finite stored result; envelope aborted.");
            if (data.GroupBy(r => (r.Case, r.Step, r.StepType)).Any(g => g.Count() > 1))
                throw new ArgumentException($"Duplicate result identity at {group.Key.Id}, station {group.Key.X}.");
            var ordered = data.OrderBy(r => r.Case, StringComparer.Ordinal).ThenBy(r => r.Step).ThenBy(r => r.StepType, StringComparer.Ordinal);
            var governing = mode == 0 ? ordered.OrderBy(r => r.V[component]).First() :
                ordered.OrderByDescending(r => mode == 2 ? Math.Abs(r.V[component]) : r.V[component]).First();
            string[] absent = selectedCases.Except(data.Select(r => r.Case)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            rows.Add(new(group.Key.Id, group.Key.X, governing.Case, governing.Step, governing.StepType,
                governing.V[component], governing.V, governing.Source, absent.Length == 0 ? "Complete cases" : "Missing cases at this station: " + string.Join(", ", absent)));
        }
        return rows.ToArray();
    }

    private static HashSet<string> Select(IReadOnlyList<string> requested, string[] available, string label)
    {
        if (requested == null || requested.Count == 0) return available.ToHashSet(StringComparer.Ordinal);
        if (requested.Any(string.IsNullOrWhiteSpace) || requested.Distinct().Count() != requested.Count) throw new ArgumentException($"Invalid or duplicate {label} selection.");
        var missing = requested.Except(available).ToArray();
        if (missing.Length > 0) throw new ArgumentException($"Unknown {label}: " + string.Join(", ", missing));
        return requested.ToHashSet(StringComparer.Ordinal);
    }
}
