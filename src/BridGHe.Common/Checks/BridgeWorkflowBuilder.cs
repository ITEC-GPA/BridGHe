using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Checks;

public sealed record BridgeWorkflowBuildResult(ModelModel Model, string[] Summary, string[] Warnings);

/// <summary>Joins incremental FEM results by beam identity, preserving FEM samples and resampling different station grids.</summary>
public static class BridgeWorkflowBuilder
{
    public static BridgeWorkflowBuildResult Build(IReadOnlyList<ModelModel> models, IEnumerable<BridgePhaseDefinition> definitions,
        IReadOnlyList<string[]> combinations, BridgeWorkflowMethod method, double tolerance)
    {
        if (!Enum.IsDefined(method)) throw new ArgumentException("Unknown workflow method.");
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentException("Invalid geometry tolerance.");
        var phases = definitions?.OrderBy(p => p?.Order).ToArray() ?? throw new ArgumentException("Provide phases.");
        if (phases.Length == 0 || phases.Any(p => p == null)) throw new ArgumentException("Provide non-null phases.");
        if (phases.Select(p => p.Order).Distinct().Count() != phases.Length) throw new ArgumentException("Phase numbers must be unique.");
        if (phases.Any(p => p.Id == Guid.Empty) || phases.Select(p => p.Id).Distinct().Count() != phases.Length)
            throw new ArgumentException("Phase identities must be non-empty and unique; do not connect the same phase twice.");
        var actions = new List<(BridgePhaseDefinition Phase, BridgeActionDefinition Action)>();
        bool composite = false; double previousDay = 0;
        foreach (var phase in phases)
        {
            if (phase.Order < 0 || string.IsNullOrWhiteSpace(phase.Name) || !Enum.IsDefined(phase.State))
                throw new ArgumentException($"Phase {phase.Order}: invalid number, name or section state.");
            if (phase.Day is double day)
            {
                if (!double.IsFinite(day) || day < previousDay) throw new ArgumentException($"Phase {phase.Order}: days must be finite, non-negative and chronological.");
                previousDay = day;
            }
            if (phase.State == BridgeSectionState.Steel && composite) throw new ArgumentException($"Phase {phase.Order}: steel-only cannot follow activation of the composite section.");
            if (phase.State == BridgeSectionState.ConcreteExcluded && (!composite || method == BridgeWorkflowMethod.Compatible))
                throw new ArgumentException($"Phase {phase.Order}: concrete exclusion requires LinearHistory and prior concrete activation.");
            composite |= phase.State == BridgeSectionState.Composite;
            foreach (var action in phase.Actions ?? throw new ArgumentException($"Phase {phase.Order}: null action list."))
            {
                if (action == null || string.IsNullOrWhiteSpace(action.Key) || action.Key != action.Key.Trim() || !Enum.IsDefined(action.Behavior))
                    throw new ArgumentException($"Phase {phase.Order}: invalid action identifier or behavior.");
                string label = $"Phase {phase.Order}, action {action.Key}";
                bool shrinkage = action.Behavior == BridgeActionBehavior.Shrinkage;
                if ((phase.State == BridgeSectionState.Steel) != (action.Behavior == BridgeActionBehavior.Steel))
                    throw new ArgumentException($"{label}: action behavior does not match the active section.");
                if (shrinkage && (phase.State != BridgeSectionState.Composite || action.Strain == null))
                    throw new ArgumentException($"{label}: shrinkage requires active concrete and an explicit strain increment.");
                if (action.Strain.HasValue && (phase.State != BridgeSectionState.Composite || action.Behavior is BridgeActionBehavior.Steel or BridgeActionBehavior.LongTerm || !double.IsFinite(action.Strain.Value) || action.Strain > 0))
                    throw new ArgumentException($"{label}: concrete shrinkage is a finite, non-positive increment [strain].");
                if (string.IsNullOrWhiteSpace(action.Case))
                {
                    if (!action.Strain.HasValue || !action.PrimaryOnly) throw new ArgumentException($"{label}: supply the analysed FEM case, or explicitly select Primary Only for shrinkage.");
                }
                else if (action.Source == null || action.PrimaryOnly)
                    throw new ArgumentException($"{label}: a FEM case needs its source model and cannot be Primary Only.");
                if (action.BeamIds == null || action.BeamIds.Any(string.IsNullOrWhiteSpace) || action.BeamIds.Distinct(StringComparer.Ordinal).Count() != action.BeamIds.Length)
                    throw new ArgumentException($"{label}: beam IDs must be non-empty and unique.");
                actions.Add((phase, action));
            }
        }
        if (actions.Count == 0 || actions.Select(a => a.Action.Key).Distinct(StringComparer.Ordinal).Count() != actions.Count)
            throw new ArgumentException("Action identifiers must be non-empty and unique across all phases.");
        var sources = new List<ModelModel>(models ?? Array.Empty<ModelModel>());
        if (sources.Any(m => m == null)) throw new ArgumentException("Null reference model.");
        foreach (var a in actions.Select(a => a.Action))
            if (a.Source != null && !sources.Any(m => ReferenceEquals(m, a.Source))) sources.Add(a.Source);
        if (sources.Count == 0) throw new ArgumentException("A reference model is required, including for strain-only workflows.");
        if (sources[0].Units != ModelModel.ModelUnits.Nmm) throw new ArgumentException("Bridge checks require N-mm-MPa models.");
        var aligned = BridgeMergeValidation.Align(sources, tolerance);
        var beamIds = sources[0].Beams.Select(b => b.BeamId).ToHashSet(StringComparer.Ordinal);
        foreach (var (_, a) in actions)
        {
            var missing = a.BeamIds.Where(id => !beamIds.Contains(id)).ToArray();
            if (missing.Length > 0) throw new ArgumentException($"Action {a.Key}: unknown beam IDs {string.Join(", ", missing)}.");
        }
        // Prevent double application of the same source case to overlapping beam selections.
        for (int i = 0; i < actions.Count; i++) for (int j = i + 1; j < actions.Count; j++)
        {
            var a = actions[i].Action; var b = actions[j].Action;
            if (a.Source != null && ReferenceEquals(a.Source, b.Source) && a.Case == b.Case && !string.IsNullOrWhiteSpace(a.Case)
                && (a.BeamIds.Length == 0 || b.BeamIds.Length == 0 || a.BeamIds.Intersect(b.BeamIds).Any()))
                throw new ArgumentException($"Actions {a.Key} and {b.Key} reuse the same FEM increment on overlapping beams.");
        }
        var model = new ModelModel(sources[0]);
        model.Name = "BridGHe phase model";
        model.LoadCases.Clear(); model.LoadCombinations.Clear(); model.StagedConstruction.Clear(); model.MovingPaths.Clear(); model.GeneratedMovingCases.Clear();
        foreach (var element in model.Elements) { element.Results.Clear(); element.Loads.Clear(); }
        var records = new Dictionary<string, BridgeWorkflowAction>(StringComparer.Ordinal);
        var concentratedLoads = new Dictionary<(string Action, string Beam), double[]>();
        var warnings = new List<string>();
        foreach (var (phase, action) in actions)
        {
            bool hasForce = !string.IsNullOrWhiteSpace(action.Case);
            var record = new BridgeWorkflowAction { Key = action.Key, SourceName = action.Source?.Name, SourceCase = action.Case,
                Behavior = action.Behavior, ForceCase = hasForce ? action.Key + " [FEM]" : null,
                StrainCase = action.Strain.HasValue ? action.Key + " [strain]" : null,
                Strain = action.Strain, BeamIds = action.BeamIds.ToArray() };
            records.Add(action.Key, record);
            LoadCaseModel Case(string name, LoadCaseModel.Stage.BridgePhases kind)
            {
                var lc = new LoadCaseModel(name, name);
                lc.Stages.Add(new LoadCaseModel.Stage($"{phase.Order} · {phase.Name}") { BridgePhase = kind });
                model.LoadCases.Add(lc); return lc;
            }
            var forceCase = hasForce ? Case(record.ForceCase, action.Behavior switch {
                BridgeActionBehavior.Steel => LoadCaseModel.Stage.BridgePhases.OnlySteel,
                BridgeActionBehavior.LongTerm => LoadCaseModel.Stage.BridgePhases.NInfinite,
                BridgeActionBehavior.ShortTerm => LoadCaseModel.Stage.BridgePhases.NInstant,
                _ => LoadCaseModel.Stage.BridgePhases.NShrinkage }) : null;
            var strainCase = action.Strain.HasValue ? Case(record.StrainCase, action.Behavior == BridgeActionBehavior.ShortTerm
                ? LoadCaseModel.Stage.BridgePhases.StrainInstant : LoadCaseModel.Stage.BridgePhases.StrainInfinite) : null;
            int sourceIndex = hasForce ? sources.FindIndex(m => ReferenceEquals(m, action.Source)) : -1;
            for (int i = 0; i < model.Beams.Count; i++)
            {
                var beam = model.Beams[i];
                bool selected = action.BeamIds.Length == 0 || action.BeamIds.Contains(beam.BeamId, StringComparer.Ordinal);
                if (strainCase != null && selected) beam.Loads.Add(new CompositeBeamSlabStrainModel(strainCase, new() { Strain = action.Strain.Value }));
                if (forceCase == null || !selected) continue;
                var input = aligned[sourceIndex][i];
                var results = input.Results.OfType<BeamForceResultModel>().Where(r => r.LoadCase?.Name == action.Case).ToArray();
                if (results.Length == 0) throw new ArgumentException($"Phase {phase.Order}, action {action.Key}, beam {beam.BeamId}: no FEM results for '{action.Case}'.");
                if (results.Select(r => (r.StepNum, r.StepType)).Distinct().Count() != 1)
                    throw new ArgumentException($"Action {action.Key}, beam {beam.BeamId}: select one FEM step before merging.");
                var sourceCombination = action.Source.LoadCombinations.SingleOrDefault(c => c.Name == action.Case);
                var contributingCases = sourceCombination == null ? new[] { action.Case }.ToHashSet(StringComparer.Ordinal) :
                    sourceCombination.Values.Where(v => v.Value != 0).Select(v => v.Key.Name).ToHashSet(StringComparer.Ordinal);
                concentratedLoads[(action.Key, beam.BeamId)] = input.Loads.OfType<BeamPointLoadModel>()
                    .Where(l => l.LoadCase != null && contributingCases.Contains(l.LoadCase.Name) && l.Value.P.SquareLength > 0)
                    .Select(l => l.Value.A * input.Length).ToArray();
                foreach (var force in results)
                {
                    if (new[] { force.AxialForce, force.Vx, force.Vy, force.Torque, force.Mx, force.My }.Any(v => !double.IsFinite(v)))
                        throw new ArgumentException($"Action {action.Key}, beam {beam.BeamId}: non-finite FEM forces.");
                    beam.Results.Add(new BeamForceResultModel(forceCase, force.NormalizedLength ? force.Station * input.Length : force.Station, 0, "", false)
                    { AxialForce = force.AxialForce, ShearForce = force.ShearForce, Torque = force.Torque, BendingMoment = force.BendingMoment });
                }
            }
            if (action.PrimaryOnly) warnings.Add($"{action.Key}: primary shrinkage only; global restraint effects explicitly excluded.");
        }
        // Different analyses can add different internal extrema. Retain their union and
        // resample each action from its own original grid, never from another action.
        int interpolatedSamples = 0;
        foreach (var beam in model.Beams)
        {
            var forces = beam.Results.OfType<BeamForceResultModel>().ToArray();
            var stations = BridgeMergeValidation.CanonicalStations(forces.Select(r => r.Station), beam.Length);
            if (stations.Length == 0) stations = new[] { 0d, beam.Length };
            beam.Results.Clear();
            foreach (var record in records.Values.Where(r => r.ForceCase != null))
            {
                var samples = forces.Where(r => r.LoadCase.Name == record.ForceCase).ToArray();
                bool selected = record.BeamIds.Length == 0 || record.BeamIds.Contains(beam.BeamId, StringComparer.Ordinal);
                var lc = model.LoadCases.Single(c => c.Name == record.ForceCase);
                if (selected)
                {
                    var resampled = BridgeForceResampling.Resample(samples, stations, beam.Length,
                        $"Action {record.Key}, beam {beam.BeamId}", concentratedLoads[(record.Key, beam.BeamId)], out int added);
                    foreach (var sample in resampled) beam.Results.Add(sample);
                    interpolatedSamples += added;
                }
                else foreach (double station in stations) beam.Results.Add(new BeamForceResultModel(lc, station, 0, "", false));
            }
            if (forces.Length == 0 && records.Values.All(r => r.ForceCase == null))
            {
                // Existing check components derive their sampling grid from results, even for a pure eigenstrain model.
                var grid = model.LoadCases.First(c => c.Name == records.Values.First().StrainCase);
                foreach (var station in stations) beam.Results.Add(new BeamForceResultModel(grid, station, 0, "", false));
            }
        }
        if (interpolatedSamples > 0)
            warnings.Add($"Station alignment: {interpolatedSamples} interpolated FEM samples; original values retained. N/V/T linear, moments quadratic using shear and both endpoint moments. Refine output stations for non-polynomial diagrams.");
        model.BridgeWorkflow = new BridgeWorkflow { Method = method, Phases = phases.Select(p => new BridgeWorkflowPhase {
            Id = p.Id, Order = p.Order, Name = p.Name, State = p.State, Day = p.Day,
            Actions = p.Actions.Select(a => records[a.Key]).ToArray() }).ToArray() };
        BuildCombinations(model, records, combinations);
        foreach (var record in records.Values)
            if (!model.LoadCombinations.Any(c => c.Values.Any(v => (v.Key.Name == record.ForceCase || v.Key.Name == record.StrainCase) && v.Value != 0)))
                warnings.Add($"Action {record.Key} is not active in any combination.");
        if (phases.Any(p => p.Day.HasValue)) warnings.Add("Days describe the sequence; no age-dependent creep integration is performed.");
        if (method == BridgeWorkflowMethod.Compatible) warnings.Add("Compatible: existing category-based section calculation; numbered phases are retained for inspection. Select LinearHistory for sequential section activation.");
        else warnings.Add("LinearHistory: gPC section N–Mx history; global construction stages must already be analysed in Straus. Shear, studs and stiffeners are not verified by this history method.");
        var summary = model.BridgeWorkflow.Phases.SelectMany(p => new[] { $"{p.Order} | {p.Name} | {p.State}" + (p.Day.HasValue ? $" | day {p.Day:G6}" : "") }
            .Concat(p.Actions.Select(a => $"  {a.Key} | {a.Behavior} | {a.SourceName ?? "local strain"} / {a.SourceCase ?? "—"}" + (a.Strain.HasValue ? $" | Δεcs={a.Strain.Value:G6}" : "")))).ToArray();
        return new(model, summary, warnings.ToArray());
    }

    private static void BuildCombinations(ModelModel model, IReadOnlyDictionary<string, BridgeWorkflowAction> actions, IReadOnlyList<string[]> rows)
    {
        if (rows == null || rows.Count < 2 || rows[0].Length < 2) throw new ArgumentException("Provide a combination table: first cell is a label, following headers are action identifiers; at least one data row is required.");
        var keys = rows[0].Skip(1).ToArray();
        if (keys.Any(k => k == null || !actions.ContainsKey(k)) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("Combination headers must be unique action identifiers, without model-order prefixes or internal FEM/strain suffixes.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows.Skip(1))
        {
            if (row.Length != keys.Length + 1 || string.IsNullOrWhiteSpace(row[0]) || !names.Add(row[0])) throw new ArgumentException("Invalid or duplicate combination row/name.");
            var combo = new LoadCombinationModel(row[0]);
            for (int i = 0; i < keys.Length; i++)
            {
                if ((!double.TryParse(row[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double factor)
                    && !double.TryParse(row[i + 1], NumberStyles.Float, CultureInfo.CurrentCulture, out factor)) || !double.IsFinite(factor))
                    throw new ArgumentException($"Combination {row[0]}, action {keys[i]}: invalid coefficient '{row[i + 1]}'.");
                var action = actions[keys[i]];
                foreach (var name in new[] { action.ForceCase, action.StrainCase }.Where(n => n != null))
                    combo.Values.Add(model.LoadCases.Single(c => c.Name == name), factor);
            }
            model.LoadCombinations.Add(combo);
        }
    }
}
