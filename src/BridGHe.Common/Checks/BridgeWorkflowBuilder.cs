using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Checks;

/// <param name="Samples">F6 (a)/(b): the merged beam actions as numbers, per beam of <see cref="Model"/> (its order), in the order they are
/// stored on the beam: case name and sample (station mm, BridGHe convention). The same numbers as the result objects of the merged model;
/// the GPC bridge model takes its dataset from them.</param>
public sealed record BridgeWorkflowBuildResult(ModelModel Model, string[] Summary, string[] Warnings, IReadOnlyList<(string Case, BeamForceSample Sample)>[] Samples = null);

/// <summary>Joins incremental FEM results by beam identity, preserving FEM samples and resampling different station grids.
/// <para>F6 chain C: the validation of the recipe, the action records, the combination table, the summary and the warnings are shared
/// with the GPC bridge model (<see cref="Gpc.GpcBridgeBuilder"/>), which merges the same actions from the analysed GPC models.</para></summary>
public static class BridgeWorkflowBuilder
{
    public static BridgeWorkflowBuildResult Build(IReadOnlyList<ModelModel> models, IEnumerable<BridgePhaseDefinition> definitions,
        IReadOnlyList<string[]> combinations, BridgeWorkflowMethod method, double tolerance, bool allowAdditionalMembers = false)
    {
        var (phases, actions) = Validate(definitions, method, tolerance);
        var sources = new List<ModelModel>(models ?? Array.Empty<ModelModel>());
        if (sources.Any(m => m == null)) throw new ArgumentException("Null reference model.");
        foreach (var a in actions.Select(a => a.Action))
            if (a.Source != null && !sources.Any(m => ReferenceEquals(m, a.Source))) sources.Add(a.Source);
        if (sources.Count == 0) throw new ArgumentException("A reference model is required, including for strain-only workflows.");
        if (sources[0].Units != ModelModel.ModelUnits.Nmm) throw new ArgumentException("Bridge checks require N-mm-MPa models.");
        var aligned = BridgeMergeValidation.Align(sources, tolerance, allowAdditionalMembers);
        RequireKnownBeams(actions, sources[0].Beams.Select(b => b.BeamId));
        RejectReusedIncrements(actions, a => a.Source);
        var model = new ModelModel(sources[0]);
        model.Name = MergedModelName;
        model.LoadCases.Clear(); model.LoadCombinations.Clear(); model.StagedConstruction.Clear(); model.MovingPaths.Clear(); model.GeneratedMovingCases.Clear();
        foreach (var element in model.Elements) { element.Results.Clear(); element.Loads.Clear(); }
        var records = new Dictionary<string, BridgeWorkflowAction>(StringComparer.Ordinal);
        var concentratedLoads = new Dictionary<(string Action, string Beam), double[]>();
        var warnings = new List<string>();
        if (allowAdditionalMembers)
            foreach (var source in sources.Where(s => s.Beams.Count > sources[0].Beams.Count))
                warnings.Add(AdditionalMembersWarning(source.Name, source.Beams.Count - sources[0].Beams.Count));
        foreach (var (phase, action) in actions)
        {
            bool hasForce = !string.IsNullOrWhiteSpace(action.Case);
            var record = Record(phase, action, action.Source?.Name, LocalSectionStates(phase, action));
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
            // F6 (a)/(b): the action from the rows of its analysed GPC model (the numbers the analysis wrote into the FEM results), when the
            // Bridge Action component attached it; the FEM results otherwise.
            var gpc = hasForce && action.GpcSource != null ? GpcActionSource.Of(action.Source, action.GpcSource) : null;
            for (int i = 0; i < model.Beams.Count; i++)
            {
                var beam = model.Beams[i];
                bool selected = action.BeamIds.Length == 0 || action.BeamIds.Contains(beam.BeamId, StringComparer.Ordinal);
                if (strainCase != null && selected) beam.Loads.Add(new CompositeBeamSlabStrainModel(strainCase, new() { Strain = action.Strain.Value }));
                if (forceCase == null || !selected) continue;
                var input = aligned[sourceIndex][i];
                BeamForceSample[] results;
                if (gpc != null) { results = gpc.Forces(input, action.Case); concentratedLoads[(action.Key, beam.BeamId)] = gpc.ConcentratedLoads(input, action.Case); }
                else
                {
                    var stored = input.Results.OfType<BeamForceResultModel>().Where(r => r.LoadCase?.Name == action.Case).ToArray();
                    if (stored.Select(r => (r.StepNum, r.StepType)).Distinct().Count() > 1)
                        throw new ArgumentException($"Action {action.Key}, beam {beam.BeamId}: select one FEM step before merging.");
                    results = stored.Select(r => new BeamForceSample(r.NormalizedLength ? r.Station * input.Length : r.Station, r.AxialForce, r.Vx, r.Vy, r.Torque, r.Mx, r.My)).ToArray();
                    var sourceCombination = action.Source.LoadCombinations.SingleOrDefault(c => c.Name == action.Case);
                    var contributingCases = sourceCombination == null ? new[] { action.Case }.ToHashSet(StringComparer.Ordinal) :
                        sourceCombination.Values.Where(v => v.Value != 0).Select(v => v.Key.Name).ToHashSet(StringComparer.Ordinal);
                    concentratedLoads[(action.Key, beam.BeamId)] = input.Loads.OfType<BeamPointLoadModel>()
                        .Where(l => l.LoadCase != null && contributingCases.Contains(l.LoadCase.Name) && l.Value.P.SquareLength > 0)
                        .Select(l => l.Value.A * input.Length).ToArray();
                }
                RequireForces(phase, action, beam.BeamId, results);
                foreach (var force in results)
                    beam.Results.Add(new BeamForceResultModel(forceCase, force.Station, 0, "", false)
                    { AxialForce = force.Axial, Vx = force.Shear1, Vy = force.Shear2, Torque = force.Torque, Mx = force.Bending1, My = force.Bending2 });
            }
            if (action.PrimaryOnly) warnings.Add(PrimaryOnlyWarning(action));
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
        if (interpolatedSamples > 0) warnings.Add(InterpolationWarning(interpolatedSamples));
        model.BridgeWorkflow = Workflow(phases, method, records);
        var table = Combinations(records, combinations);
        foreach (var (name, factors) in table)
        {
            var combo = new LoadCombinationModel(name);
            foreach (var (caseName, factor) in factors) combo.Values.Add(model.LoadCases.Single(c => c.Name == caseName), factor);
            model.LoadCombinations.Add(combo);
        }
        var summary = Finish(phases, method, records, table, model.BridgeWorkflow, warnings);
        // The merged actions as numbers (the GPC bridge model takes its dataset from them): the same values as the result objects.
        var merged = model.Beams.Select(b => (IReadOnlyList<(string, BeamForceSample)>)b.Results.OfType<BeamForceResultModel>()
            .Select(r => (r.LoadCase.Name, new BeamForceSample(r.Station, r.AxialForce, r.Vx, r.Vy, r.Torque, r.Mx, r.My))).ToArray()).ToArray();
        return new(model, summary, warnings.ToArray(), merged);
    }

    /// <summary>The name of the merged (bridge) model.</summary>
    public const string MergedModelName = "BridGHe phase model";

    /// <summary>The phases ordered by number and their actions in order, validated (phase numbers, identities, days, section states,
    /// behaviours, strains, cases, beam selections, unique action keys). Shared by the FEM merge and the GPC bridge model; with
    /// <paramref name="gpcSources"/> (the GPC bridge model, finding 2 of P2T) an action with a case needs its FEM source or its analysed GPC source.</summary>
    internal static (BridgePhaseDefinition[] Phases, List<(BridgePhaseDefinition Phase, BridgeActionDefinition Action)> Actions) Validate(
        IEnumerable<BridgePhaseDefinition> definitions, BridgeWorkflowMethod method, double tolerance, bool gpcSources = false)
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
                else if ((gpcSources ? action.Source == null && action.GpcSource == null : action.Source == null) || action.PrimaryOnly)
                    throw new ArgumentException($"{label}: a FEM case needs its source model and cannot be Primary Only.");
                if (action.BeamIds == null || action.BeamIds.Any(string.IsNullOrWhiteSpace) || action.BeamIds.Distinct(StringComparer.Ordinal).Count() != action.BeamIds.Length)
                    throw new ArgumentException($"{label}: beam IDs must be non-empty and unique.");
                actions.Add((phase, action));
            }
        }
        if (actions.Count == 0 || actions.Select(a => a.Action.Key).Distinct(StringComparer.Ordinal).Count() != actions.Count)
            throw new ArgumentException("Action identifiers must be non-empty and unique across all phases.");
        return (phases, actions);
    }

    internal static void RequireKnownBeams(IEnumerable<(BridgePhaseDefinition Phase, BridgeActionDefinition Action)> actions, IEnumerable<string> referenceBeamIds)
    {
        var beamIds = referenceBeamIds.ToHashSet(StringComparer.Ordinal);
        foreach (var (_, a) in actions)
        {
            var missing = a.BeamIds.Where(id => !beamIds.Contains(id)).ToArray();
            if (missing.Length > 0) throw new ArgumentException($"Action {a.Key}: unknown beam IDs {string.Join(", ", missing)}.");
        }
    }

    /// <summary>Prevents double application of the same source case to overlapping beam selections (the source by reference).</summary>
    internal static void RejectReusedIncrements(IReadOnlyList<(BridgePhaseDefinition Phase, BridgeActionDefinition Action)> actions, Func<BridgeActionDefinition, object> source)
    {
        for (int i = 0; i < actions.Count; i++) for (int j = i + 1; j < actions.Count; j++)
        {
            var a = actions[i].Action; var b = actions[j].Action;
            if (source(a) != null && ReferenceEquals(source(a), source(b)) && a.Case == b.Case && !string.IsNullOrWhiteSpace(a.Case)
                && (a.BeamIds.Length == 0 || b.BeamIds.Length == 0 || a.BeamIds.Intersect(b.BeamIds).Any()))
                throw new ArgumentException($"Actions {a.Key} and {b.Key} reuse the same FEM increment on overlapping beams.");
        }
    }

    /// <summary>The per-beam local section states of an action, read from the FEM source beams (Bridge State Override): whether any is
    /// assigned and the beams whose concrete is excluded. F6: until the GPC section-state attribute (lane L11) the GPC bridge model reads
    /// them here as well.</summary>
    public static (bool HasLocalSectionStates, string[] ConcreteExcludedBeamIds) LocalSectionStates(BridgePhaseDefinition phase, BridgeActionDefinition action)
    {
        var assigned = (action.Source?.Beams ?? new List<BeamModel>()).Where(b => action.BeamIds.Length == 0 || action.BeamIds.Contains(b.BeamId, StringComparer.Ordinal))
            .SelectMany(b => b.Attributes.OfType<BridgeSectionStateAttributeModel>().Select(a => (b.BeamId, a.Value, b.BeamProperty))).ToArray();
        if (assigned.Select(a => a.BeamId).Distinct(StringComparer.Ordinal).Count() != assigned.Length ||
            assigned.Any(a => a.Value.Behavior != action.Behavior || phase.State != BridgeSectionState.Composite))
            throw new ArgumentException($"Action {action.Key}: local FEM section state does not match the mechanical phase. Do not reuse it for shrinkage.");
        if (assigned.Any(a => a.Value.PropertySignature != BridgeSectionAssignment.Signature(a.BeamProperty)))
            throw new ArgumentException($"Action {action.Key}: FEM properties changed after the local section-state assignment. Reapply Bridge State Override and recalculate.");
        return (assigned.Length > 0, assigned.Where(a => a.Value.ConcreteExcluded).Select(a => a.BeamId).ToArray());
    }

    /// <summary>The record of an action in the merged recipe: its merged cases "key [FEM]" and "key [strain]".</summary>
    internal static BridgeWorkflowAction Record(BridgePhaseDefinition phase, BridgeActionDefinition action, string sourceName,
        (bool HasLocalSectionStates, string[] ConcreteExcludedBeamIds) states)
    {
        bool hasForce = !string.IsNullOrWhiteSpace(action.Case);
        return new BridgeWorkflowAction { Key = action.Key, SourceName = sourceName, SourceCase = action.Case,
            Behavior = action.Behavior, ForceCase = hasForce ? action.Key + " [FEM]" : null,
            StrainCase = action.Strain.HasValue ? action.Key + " [strain]" : null,
            Strain = action.Strain, BeamIds = action.BeamIds.ToArray(), HasLocalSectionStates = states.HasLocalSectionStates,
            ConcreteExcludedBeamIds = states.ConcreteExcludedBeamIds };
    }

    internal static void RequireForces(BridgePhaseDefinition phase, BridgeActionDefinition action, string beamId, BeamForceSample[] results)
    {
        if (results.Length == 0) throw new ArgumentException($"Phase {phase.Order}, action {action.Key}, beam {beamId}: no FEM results for '{action.Case}'.");
        foreach (var force in results)
            if (force.Values.Any(v => !double.IsFinite(v)))
                throw new ArgumentException($"Action {action.Key}, beam {beamId}: non-finite FEM forces.");
    }

    internal static string AdditionalMembersWarning(string sourceName, int additional) =>
        $"{sourceName}: {additional} additional phase members retained in the FEM source only. Checks use the first reference model's beams; global reactions must come from the full source models.";
    internal static string PrimaryOnlyWarning(BridgeActionDefinition action) => $"{action.Key}: primary shrinkage only; global restraint effects explicitly excluded.";
    internal static string InterpolationWarning(int interpolatedSamples) =>
        $"Station alignment: {interpolatedSamples} interpolated FEM samples; original values retained. N/V/T linear, moments quadratic using shear and both endpoint moments. Refine output stations for non-polynomial diagrams.";

    internal static BridgeWorkflow Workflow(BridgePhaseDefinition[] phases, BridgeWorkflowMethod method, IReadOnlyDictionary<string, BridgeWorkflowAction> records) =>
        new BridgeWorkflow { Method = method, Phases = phases.Select(p => new BridgeWorkflowPhase {
            Id = p.Id, Order = p.Order, Name = p.Name, State = p.State, Day = p.Day,
            Actions = p.Actions.Select(a => records[a.Key]).ToArray() }).ToArray() };

    /// <summary>The combination table (header: label + action keys; rows: name + coefficients) as combinations of the merged cases: for
    /// every row, its name and, for every action of the header in order, its force case then its strain case with the coefficient.</summary>
    internal static List<(string Name, List<(string Case, double Factor)> Factors)> Combinations(IReadOnlyDictionary<string, BridgeWorkflowAction> actions, IReadOnlyList<string[]> rows)
    {
        if (rows == null || rows.Count < 2 || rows[0].Length < 2) throw new ArgumentException("Provide a combination table: first cell is a label, following headers are action identifiers; at least one data row is required.");
        var keys = rows[0].Skip(1).ToArray();
        if (keys.Any(k => k == null || !actions.ContainsKey(k)) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("Combination headers must be unique action identifiers, without model-order prefixes or internal FEM/strain suffixes.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        var table = new List<(string, List<(string, double)>)>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Length != keys.Length + 1 || string.IsNullOrWhiteSpace(row[0]) || !names.Add(row[0])) throw new ArgumentException("Invalid or duplicate combination row/name.");
            var factors = new List<(string, double)>();
            for (int i = 0; i < keys.Length; i++)
            {
                if ((!double.TryParse(row[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double factor)
                    && !double.TryParse(row[i + 1], NumberStyles.Float, CultureInfo.CurrentCulture, out factor)) || !double.IsFinite(factor))
                    throw new ArgumentException($"Combination {row[0]}, action {keys[i]}: invalid coefficient '{row[i + 1]}'.");
                var action = actions[keys[i]];
                foreach (var name in new[] { action.ForceCase, action.StrainCase }.Where(n => n != null)) factors.Add((name, factor));
            }
            table.Add((row[0], factors));
        }
        return table;
    }

    /// <summary>The warnings that follow the combinations (inactive actions, days, local states, method) and the phase/action summary.</summary>
    internal static string[] Finish(BridgePhaseDefinition[] phases, BridgeWorkflowMethod method, IReadOnlyDictionary<string, BridgeWorkflowAction> records,
        IReadOnlyList<(string Name, List<(string Case, double Factor)> Factors)> combinations, BridgeWorkflow workflow, List<string> warnings)
    {
        foreach (var record in records.Values)
            if (!combinations.Any(c => c.Factors.Any(v => (v.Case == record.ForceCase || v.Case == record.StrainCase) && v.Factor != 0)))
                warnings.Add($"Action {record.Key} is not active in any combination.");
        if (phases.Any(p => p.Day.HasValue)) warnings.Add("Days describe the sequence; no age-dependent creep integration is performed.");
        if (records.Values.Any(a => a.HasLocalSectionStates)) warnings.Add("Per-beam mechanical section states retained. Each action is checked using its own gross centroid and concrete inclusion. Shrinkage remains uncracked if its source has no state override. Moment-sign classification is a preliminary stiffness assumption, not a code cracking assessment.");
        if (method == BridgeWorkflowMethod.Compatible) warnings.Add("Compatible: existing category-based section calculation; numbered phases are retained for inspection. Select LinearHistory for sequential section activation.");
        else warnings.Add("LinearHistory: gPC section N–Mx history; global construction stages must already be analysed in Straus. Shear, studs and stiffeners are not verified by this history method.");
        return workflow.Phases.SelectMany(p => new[] { $"{p.Order} | {p.Name} | {p.State}" + (p.Day.HasValue ? $" | day {p.Day:G6}" : "") }
            .Concat(p.Actions.Select(a => $"  {a.Key} | {a.Behavior} | {a.SourceName ?? "local strain"} / {a.SourceCase ?? "—"}" + (a.Strain.HasValue ? $" | Δεcs={a.Strain.Value:G6}" : "")))).ToArray();
    }
}
