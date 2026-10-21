using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;

namespace BridGHe.Common.Diagnostics;

/// <summary>Stable action names and their meaning, independent of phase GUIDs and FEM sampling.</summary>
public sealed class BridgeCombinationCatalog
{
    private readonly BridgePhaseDefinition[] phases;
    public string[] ActionIds { get; }
    public string[][] ReferenceRows { get; }
    public string[] Warnings { get; }

    public BridgeCombinationCatalog(IReadOnlyList<ModelModel> models, IEnumerable<BridgePhaseDefinition> definitions)
    {
        if (models == null || models.Count == 0 || models.Any(m => m == null))
            throw new ArgumentException("Supply at least one reference Model and valid Phases.");
        if (models.Any(m => m.Units != ModelModel.ModelUnits.Nmm))
            throw new ArgumentException("Build BridGHe Model requires N-mm-MPa models.");
        phases = BridgeWorkflowTools.Ordered(definitions);
        ActionIds = phases.SelectMany(p => p.Actions).Select(a => a.Key).ToArray();
        if (ActionIds.Length == 0 || ActionIds.Length > 512)
            throw new ArgumentException("The workbook requires between 1 and 512 actions.");
        var rows = new List<string[]> { new[] { "Action ID", "Phase number", "Phase", "Section state", "Day", "Source model", "Source case", "Behavior", "Strain [-]", "Beam IDs", "Primary only" } };
        var warnings = new List<string>();
        var sources = new Dictionary<ModelModel, HashSet<string>>(ReferenceEqualityComparer.Instance);
        var resultCases = new Dictionary<ModelModel, HashSet<string>>(ReferenceEqualityComparer.Instance);
        foreach (var phase in phases)
        foreach (var action in phase.Actions)
        {
            if (!Enum.IsDefined(action.Behavior) || action.Strain is double strain && !double.IsFinite(strain))
                throw new ArgumentException($"Action {action.Key}: invalid behavior or non-finite strain.");
            if (action.PrimaryOnly)
            {
                if (!action.Strain.HasValue || action.Source != null || !string.IsNullOrEmpty(action.Case))
                    throw new ArgumentException($"Action {action.Key}: Primary Only requires a strain and no FEM source/case.");
            }
            else
            {
                var source = action.Source;
                if (source == null || string.IsNullOrWhiteSpace(action.Case))
                    throw new ArgumentException($"Action {action.Key}: specify its Source and exact Case, or use Primary Only for an imposed strain.");
                if (source.Units != ModelModel.ModelUnits.Nmm)
                    throw new ArgumentException($"Action {action.Key}: source must use N-mm-MPa.");
                if (!sources.TryGetValue(source, out var cases))
                {
                    var results = source.Elements.SelectMany(e => e.Results).Select(r => r.LoadCase?.Name).Where(n => n != null).ToHashSet(StringComparer.Ordinal);
                    resultCases.Add(source, results);
                    cases = source.LoadCases.Select(c => c.Name).Concat(source.LoadCombinations.Select(c => c.Name))
                        .Concat(source.Elements.SelectMany(e => e.Loads).Select(l => l.LoadCase?.Name)).Concat(results).Where(n => n != null).ToHashSet(StringComparer.Ordinal);
                    sources.Add(source, cases);
                }
                if (!cases.Contains(action.Case)) throw new ArgumentException($"Action {action.Key}: source case '{action.Case}' was not found in '{source.Name}'.");
                if (!resultCases[source].Contains(action.Case)) warnings.Add($"{action.Key}: case is defined but has no FEM results yet; analyse it before Build BridGHe Model.");
            }
            rows.Add(new[] { action.Key, Number(phase.Order), phase.Name, phase.State.ToString(), Number(phase.Day),
                action.Source?.Name ?? "", action.Case ?? "", action.Behavior.ToString(), Number(action.Strain),
                JsonSerializer.Serialize((action.BeamIds ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal)), action.PrimaryOnly ? "Yes" : "No" });
        }
        ReferenceRows = rows.ToArray(); Warnings = warnings.ToArray();
    }

    private static string Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";

    public string[][] ValidateTable(IReadOnlyList<string[]> rows)
    {
        if (rows == null || rows.Count < 2) throw new ArgumentException("No combinations: enter at least one combination name and all its coefficients (explicit 0 for inactive actions).");
        var header = rows[0];
        if (header == null || header.Length != ActionIds.Length + 1 || header[0] != "Combination" ||
            header.Skip(1).Distinct(StringComparer.Ordinal).Count() != ActionIds.Length ||
            !header.Skip(1).ToHashSet(StringComparer.Ordinal).SetEquals(ActionIds))
            throw new ArgumentException("Combinations headers must be 'Combination' followed by every exact Action ID once. Regenerate the template if Phases changed.");
        var names = new List<string>(); var factors = new List<double[]>();
        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row == null || row.Length != header.Length) throw new ArgumentException($"Combination row {i + 1}: incorrect column count.");
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (string.IsNullOrWhiteSpace(row[0])) throw new ArgumentException($"Combination row {i + 1}: missing name.");
            var values = new double[ActionIds.Length];
            for (int j = 0; j < values.Length; j++)
                if (!double.TryParse(row[j + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[j]) || !double.IsFinite(values[j]))
                    throw new ArgumentException($"Combination '{row[0]}', action '{header[j + 1]}': supply a finite numeric coefficient; use explicit 0 for an inactive action.");
            names.Add(row[0]); factors.Add(values);
        }
        return BridgeWorkflowTools.CombinationTable(phases, header.Skip(1).ToArray(), names, factors);
    }
}
