using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Checks;

namespace BridGHe.Common.Diagnostics;

/// <summary>Stable action names and their meaning, independent of phase GUIDs and FEM sampling.</summary>
public sealed class BridgeCombinationCatalog
{
    private readonly BridgePhaseDefinition[] phases;
    public string[] ActionIds { get; }
    public string[][] ReferenceRows { get; }
    public string[] Warnings { get; }

    /// <summary>The source of an action as the catalogue reads it: its name, whether it is N-mm, every case name it knows and the names of its
    /// stored results.</summary>
    private sealed record Source(string Name, bool Nmm, HashSet<string> Cases, HashSet<string> Results);


    /// <summary>
    /// F6 R6.4 (3): the catalogue on the GPC data: the units of the reference models from their GPC inputs, and every action read from its
    /// analysed GPC source (<see cref="BridgeActionDefinition.GpcSource"/>): its name, the units of its results catalogue, the names of its
    /// load cases, combinations and stored results (the catalogue labels). The same rows, warnings and messages as the FEM catalogue (the
    /// GPC source knows every case of the FEM source: the reader registers the cases of the element loads, combinations, stages and moving
    /// paths).
    /// </summary>
    public static BridgeCombinationCatalog FromGpc(IReadOnlyList<Gpc.GpcModels.Conversion> references, IEnumerable<BridgePhaseDefinition> definitions) =>
        new BridgeCombinationCatalog(references == null ? null : references.Select(r => r == null ? (bool?)null : r.Report.Units.Units == Gpc.DefinitionUnits.Nmm).ToArray(),
            definitions, a => (object)a.GpcSource ?? a.GpcInput, GpcSource); // L3b: a source that was not analysed: its GPC input (GPC-only path)


    private static Source GpcSource(object value)
    {
        // L3b: a source given by its GPC input alone, not analysed: its cases and no stored result (the FEM warning «no FEM results yet»).
        if (value is Gpc.GpcModels.Conversion input)
            return new Source(input.Model.Name, input.Report.Units.Units == Gpc.DefinitionUnits.Nmm,
                input.Model.LoadCases.Values.Select(c => c.Name).Concat(input.Model.Combinations.Values.Select(c => c.Name)).Where(n => n != null).ToHashSet(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        var source = (GPC.Model.Models.Model)value;
        var catalog = Gpc.GpcResultCatalog.Of(source) ?? throw new ArgumentException($"The source '{source.Name}' has no analysed GPC model (analysed before F6); recalculate the analysis.");
        var results = catalog.Entries.Select(e => e.Label).Where(n => n != null).ToHashSet(StringComparer.Ordinal);
        var cases = source.LoadCases.Values.Select(c => c.Name).Concat(source.Combinations.Values.Select(c => c.Name)).Concat(results).Where(n => n != null).ToHashSet(StringComparer.Ordinal);
        return new Source(source.Name, catalog.Units.Units == Gpc.DefinitionUnits.Nmm, cases, results);
    }

    private BridgeCombinationCatalog(IReadOnlyList<bool?> referencesNmm, IEnumerable<BridgePhaseDefinition> definitions, Func<BridgeActionDefinition, object> sourceOf, Func<object, Source> read)
    {
        if (referencesNmm == null || referencesNmm.Count == 0 || referencesNmm.Any(m => m == null))
            throw new ArgumentException("Supply at least one reference Model and valid Phases.");
        if (referencesNmm.Any(m => m != true))
            throw new ArgumentException("Build BridGHe Model requires N-mm-MPa models.");
        phases = BridgeWorkflowTools.Ordered(definitions);
        ActionIds = phases.SelectMany(p => p.Actions).Select(a => a.Key).ToArray();
        if (ActionIds.Length == 0 || ActionIds.Length > 512)
            throw new ArgumentException("The workbook requires between 1 and 512 actions.");
        var rows = new List<string[]> { new[] { "Action ID", "Phase number", "Phase", "Section state", "Day", "Source model", "Source case", "Behavior", "Strain [-]", "Beam IDs", "Primary only" } };
        var warnings = new List<string>();
        var sources = new Dictionary<object, Source>(ReferenceEqualityComparer.Instance);
        foreach (var phase in phases)
        foreach (var action in phase.Actions)
        {
            if (!Enum.IsDefined(action.Behavior) || action.Strain is double strain && !double.IsFinite(strain))
                throw new ArgumentException($"Action {action.Key}: invalid behavior or non-finite strain.");
            var value = sourceOf(action);
            Source source = null;
            if (action.PrimaryOnly)
            {
                if (!action.Strain.HasValue || value != null || !string.IsNullOrEmpty(action.Case))
                    throw new ArgumentException($"Action {action.Key}: Primary Only requires a strain and no FEM source/case.");
            }
            else
            {
                if (value == null || string.IsNullOrWhiteSpace(action.Case))
                    throw new ArgumentException($"Action {action.Key}: specify its Source and exact Case, or use Primary Only for an imposed strain.");
                if (!sources.TryGetValue(value, out source)) sources.Add(value, source = read(value));
                if (!source.Nmm)
                    throw new ArgumentException($"Action {action.Key}: source must use N-mm-MPa.");
                if (!source.Cases.Contains(action.Case)) throw new ArgumentException($"Action {action.Key}: source case '{action.Case}' was not found in '{source.Name}'.");
                if (!source.Results.Contains(action.Case)) warnings.Add($"{action.Key}: case is defined but has no FEM results yet; analyse it before Build BridGHe Model.");
            }
            rows.Add(new[] { action.Key, Number(phase.Order), phase.Name, phase.State.ToString(), Number(phase.Day),
                source?.Name ?? (value == null ? "" : read(value).Name), action.Case ?? "", action.Behavior.ToString(), Number(action.Strain),
                JsonSerializer.Serialize((action.BeamIds ?? Array.Empty<string>()).OrderBy(x => x, StringComparer.Ordinal)), action.PrimaryOnly ? "Yes" : "No" });
        }
        ReferenceRows = rows.ToArray(); Warnings = warnings.ToArray();
    }

    /// <summary>R6.4: the canonical text of the catalogue (actions, rows, warnings), for the comparison of the GPC and the FEM catalogues.</summary>
    public string Text() => string.Join("|", ActionIds) + "\n" + string.Join("\n", ReferenceRows.Select(r => string.Join("|", r))) + "\n--\n" + string.Join("\n", Warnings);

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
