using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Checks;
using GPC.Converter;
using GPC.Geometry;
using GPC.Model.Analysis;
using GPC.Model.Combinations;
using GPC.Model.Core.Units;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Models;
using GPC.Model.Results.Locations;
using GPC.Model.Results.State;
using GPC.Model.Structure.Assignments;
using D3 = GPC.Checkers.CompositeBridge;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// F6 chain C: the bridge model of Build BridGHe Model on GPC, the input of the GPC workflow executor (D3) of the checks: a GPC model
    /// (N-mm) with the merged actions as one dataset (<see cref="DatasetName"/>) and the D3 recipe <see cref="Workflow"/>. Built by
    /// <see cref="GpcBridgeBuilder"/> from the GPC models only (it replaced the transitional seam on the merged FEM model of F6 C5).
    /// The former <c>SourceName</c> and <c>SourceCase</c> of every action (phase model and case name), which D3 does not keep, are BridGHe
    /// records of the model (<see cref="SourceRecordPrefix"/> + action key).
    /// </summary>
    public sealed class GpcBridge
    {
        public const string DatasetName = "BridGHe bridge model", SourceRecordPrefix = "BridgeWorkflowAction/";

        public Model Model { get; }
        public D3.BridgeWorkflowDefinition Workflow { get; }
        public string Dataset => DatasetName;
        /// <summary>The merged beam actions as numbers, per beam of the bridge model (GPC beam k + 1), in their stored order: case name and
        /// sample (station in the definition length, BridGHe convention), the numbers of the dataset before its mapping to the section axes.</summary>
        public IReadOnlyList<(string Case, BeamForceSample Sample)>[] Samples { get; }
        /// <summary>P2T request 3: the records of the merged actions (cases "key [FEM]"/"key [strain]", source name and case, behaviour, strain,
        /// beams, local section states, concrete-excluded beams), as the former merged FEM model kept them (<c>ModelModel.BridgeWorkflow</c>).</summary>
        public BridgeWorkflow Records { get; }
        /// <summary>P2T request 3: the BridGHe angle (degrees) of every beam of the bridge model (beam k + 1), from its reference model.</summary>
        public IReadOnlyList<double> BeamAngles { get; }
        /// <summary>P2T request 3: the combination table of Build BridGHe Model (name and coefficients of the merged cases, zero coefficients
        /// included, in order), as the merged FEM model kept it; the GPC combinations of <see cref="Model"/> keep only the non-zero ones.</summary>
        public IReadOnlyList<(string Name, IReadOnlyList<(string Case, double Factor)> Factors)> Combinations { get; }
        private readonly Dictionary<string, int> beams;
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Model, GpcBridge> registry = new System.Runtime.CompilerServices.ConditionalWeakTable<Model, GpcBridge>();

        /// <summary>The bridge of a GPC bridge model (the components that only receive its beams, e.g. Bridge Girder Results), or null.</summary>
        public static GpcBridge Of(Model model) => model != null && registry.TryGetValue(model, out var bridge) ? bridge : null;

        internal GpcBridge(Model model, D3.BridgeWorkflowDefinition workflow, IReadOnlyList<(string Case, BeamForceSample Sample)>[] samples, BridgeWorkflow records, IReadOnlyList<double> beamAngles,
            IReadOnlyList<(string Name, IReadOnlyList<(string Case, double Factor)> Factors)> combinations, bool register = true)
        {
            Model = model; Workflow = workflow; Samples = samples; Records = records; BeamAngles = beamAngles; Combinations = combinations; if (register) registry.AddOrUpdate(model, this);
            beams = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var b in model.BeamElements) beams.TryAdd(b.Value.Source.OriginalId, b.Key);
        }

        /// <summary>A variant with another combination table (same model, recipe, samples, records and angles; not registered, so
        /// <see cref="Of"/> still gives this one): what-if checks on other coefficients (the R3 variant of the native tests).</summary>
        public GpcBridge WithCombinations(IReadOnlyList<(string Name, IReadOnlyList<(string Case, double Factor)> Factors)> combinations) =>
            new GpcBridge(Model, Workflow, Samples, Records, BeamAngles, combinations ?? throw new ArgumentNullException(nameof(combinations)), register: false);

        /// <summary>The merged samples of a beam by its BridGHe ID, or null when the bridge model has no such beam.</summary>
        public IReadOnlyList<(string Case, BeamForceSample Sample)> SamplesOf(string beamId) =>
            beamId != null && beams.TryGetValue(beamId, out int number) ? Samples[number - 1] : null;

        /// <summary>The former provenance of an action (phase model and case name), or null.</summary>
        public static (string SourceName, string SourceCase)? SourceOf(Model gpc, string actionKey)
        {
            var record = gpc.PreservedSourceData.LastOrDefault(r => r != null && r.Kind == GpcResultCatalog.Kind && r.SourceRecord == SourceRecordPrefix + actionKey);
            if (record == null) return null;
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(record.RawData);
            return (values.TryGetValue("SourceName", out var n) ? n : null, values.TryGetValue("SourceCase", out var c) ? c : null);
        }
    }

    /// <summary>A model of Build BridGHe Model as GPC data: its GPC model (a reference: the input model; an action source: the analysed model
    /// with its results catalog), its definition units and the BridGHe angle of every beam (the principal frame of the actions).</summary>
    public sealed class GpcBridgeSource
    {
        public Model Model { get; }
        public BridgeUnits Units { get; }
        /// <summary>The analysed model of the same GH datum as a reference (the source of its actions), or null.</summary>
        public Model Analysed { get; }
        private readonly Func<BeamElement, double> angle;

        private GpcBridgeSource(Model model, BridgeUnits units, Func<BeamElement, double> angle, Model analysed)
        { Model = model; Units = units; this.angle = angle; Analysed = analysed; }

        /// <summary>The BridGHe angle (degrees) of a beam of <see cref="Model"/>.</summary>
        public double Angle(BeamElement beam) => angle(beam);

        /// <summary>A reference model: its input conversion (structure, units, angles) and, when it was analysed, its analysed GPC model.</summary>
        public static GpcBridgeSource Reference(GpcModels.Conversion input, Model analysed)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return Reference(input.Model, input.Report.Units, input.Report.BeamAngles, analysed);
        }

        /// <summary>A reference model given directly as GPC (finding 3 of P2T; chain E: the input model of a GH model on GPC): the input model
        /// (N-mm, without datasets), its definition units, the BridGHe angle (degrees) of every beam by its number and, when it was analysed,
        /// the analysed GPC model of the same datum (same numbering).</summary>
        public static GpcBridgeSource Reference(Model input, BridgeUnits units, IReadOnlyDictionary<int, double> beamAngles, Model analysed)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (beamAngles == null) throw new ArgumentNullException(nameof(beamAngles));
            var angles = new Dictionary<int, double>(beamAngles);
            return new GpcBridgeSource(input, units,
                b => angles.TryGetValue(b.Id, out var a) ? a : throw new KeyNotFoundException("GpcBridgeBeamAngleMissing: " + b.Id), analysed);
        }

        /// <summary>An action source: the analysed GPC model with its results catalog (units and angles from the catalog).</summary>
        public static GpcBridgeSource Analysis(Model analysed)
        {
            if (analysed == null) throw new ArgumentNullException(nameof(analysed));
            var catalog = GpcResultCatalog.Of(analysed) ?? throw new ArgumentException("GpcActionSourceWithoutResults: " + analysed.Name);
            return new GpcBridgeSource(analysed, catalog.Units, catalog.BeamAngle, null);
        }

        internal bool Is(Model model) => ReferenceEquals(Model, model) || ReferenceEquals(Analysed, model);
    }

    public sealed record GpcBridgeBuild(GpcBridge Bridge, string[] Summary, string[] Warnings);

    /// <summary>
    /// F6 chain C: Build BridGHe Model on GPC. The bridge model is a copy of the input GPC model of the first reference without its loads,
    /// cases, combinations, stages and moving loads, with one force case per action ("key [FEM]"), one strain case per shrinkage increment
    /// ("key [strain]"), the default "Dead" case of the former model, the combinations of the table and the D3 recipe. The actions are merged
    /// from the analysed GPC models of their sources (the rows of the Results components): beams aligned by BridGHe ID and GPC geometry
    /// (end points, BridGHe angle, offsets), the union of the stations of every beam, each action resampled from its own grid. Validation,
    /// records, combinations, summary and warnings are those of <see cref="BridgeWorkflowBuilder"/> (same messages).
    /// <para>Identity: the bridge model has its own model GUID (<c>modelIdentity</c>, from the component), the copy keeps the element identities
    /// of the reference input model (the same physical elements: D3 and the excluded-concrete beams refer to them); cases and combinations by name.</para>
    /// <para>Local section states (Bridge State Override): the GPC section-state attributes of the beams of the GPC model of every action
    /// (<see cref="LocalSectionStates"/>, L11), with the rules and messages of the former FEM reading.</para>
    /// </summary>
    public static class GpcBridgeBuilder
    {
        public static GpcBridgeBuild Build(IReadOnlyList<GpcBridgeSource> references, IEnumerable<BridgePhaseDefinition> definitions, IReadOnlyList<string[]> combinations,
            BridgeWorkflowMethod method, double tolerance, bool allowAdditionalMembers, Guid modelIdentity)
        {
            var (phases, actions) = BridgeWorkflowBuilder.Validate(definitions, method, tolerance, gpcSources: true);
            var sources = new List<GpcBridgeSource>(references ?? Array.Empty<GpcBridgeSource>());
            if (sources.Any(m => m == null)) throw new ArgumentException("Null reference model.");
            int referenceCount = sources.Count;
            foreach (var a in actions.Select(a => a.Action))
            {
                if (!string.IsNullOrWhiteSpace(a.Case) && a.GpcSource == null)
                    throw new ArgumentException($"Action {a.Key}: its source has no analysed GPC model (analysed before F6); recalculate the analysis.");
                if (a.GpcSource != null && sources.FindIndex(s => s.Is(a.GpcSource)) < 0) sources.Add(GpcBridgeSource.Analysis(a.GpcSource));
            }
            if (referenceCount == 0) throw new ArgumentException("A reference model is required, including for strain-only workflows.");
            if (sources[0].Units.Units != DefinitionUnits.Nmm) throw new ArgumentException("Bridge checks require N-mm-MPa models.");
            var aligned = Align(sources, tolerance, allowAdditionalMembers);
            var reference = sources[0]; var beams = Beams(reference.Model);
            BridgeWorkflowBuilder.RequireKnownBeams(actions, beams.Select(b => b.Name));
            BridgeWorkflowBuilder.RejectReusedIncrements(actions, a => a.GpcSource);

            var records = new Dictionary<string, BridgeWorkflowAction>(StringComparer.Ordinal);
            var raw = beams.Select(_ => new List<(string Case, BeamForceSample Sample)>()).ToArray();
            var concentratedLoads = new Dictionary<(string Action, string Beam), double[]>();
            var warnings = new List<string>();
            if (allowAdditionalMembers)
                foreach (var source in sources)
                {
                    int count = source.Model.BeamElements.Count;
                    if (count > beams.Length) warnings.Add(BridgeWorkflowBuilder.AdditionalMembersWarning(source.Model.Name, count - beams.Length));
                }
            foreach (var (phase, action) in actions)
            {
                var record = BridgeWorkflowBuilder.Record(phase, action, action.GpcSource?.Name ?? action.Source?.Name, LocalSectionStates(phase, action));
                records.Add(action.Key, record);
                if (record.ForceCase != null)
                {
                    int sourceIndex = sources.FindIndex(s => s.Is(action.GpcSource));
                    var gpc = action.GpcSource; var catalog = GpcResultCatalog.Of(gpc) ?? throw new ArgumentException("GpcActionSourceWithoutResults: " + gpc.Name);
                    for (int i = 0; i < beams.Length; i++)
                    {
                        string id = beams[i].Name;
                        if (action.BeamIds.Length != 0 && !action.BeamIds.Contains(id, StringComparer.Ordinal)) continue;
                        var input = SourceBeam(gpc, aligned[sourceIndex][i]);
                        var results = Forces(catalog, gpc, input, action.Case);
                        concentratedLoads[(action.Key, id)] = ConcentratedLoads(catalog, gpc, input, action.Case);
                        BridgeWorkflowBuilder.RequireForces(phase, action, id, results);
                        foreach (var force in results) raw[i].Add((record.ForceCase, force));
                    }
                }
                if (action.PrimaryOnly) warnings.Add(BridgeWorkflowBuilder.PrimaryOnlyWarning(action));
            }
            // Different analyses can add different internal extrema. Retain their union and
            // resample each action from its own original grid, never from another action.
            int interpolatedSamples = 0;
            var samples = new IReadOnlyList<(string Case, BeamForceSample Sample)>[beams.Length];
            for (int i = 0; i < beams.Length; i++)
            {
                var beam = beams[i]; string id = beam.Name; double length = reference.Units.FromMm(beam.Length);
                var forces = raw[i];
                var stations = BridgeMergeValidation.CanonicalStations(forces.Select(f => f.Sample.Station), length);
                if (stations.Length == 0) stations = new[] { 0d, length };
                var merged = new List<(string Case, BeamForceSample Sample)>();
                foreach (var record in records.Values.Where(r => r.ForceCase != null))
                {
                    if (record.BeamIds.Length == 0 || record.BeamIds.Contains(id, StringComparer.Ordinal))
                    {
                        var own = forces.Where(f => f.Case == record.ForceCase).Select(f => f.Sample).ToArray();
                        var resampled = BridgeForceResampling.Resample(own, stations, length, $"Action {record.Key}, beam {id}", concentratedLoads[(record.Key, id)], out int added, out _);
                        foreach (var sample in resampled) merged.Add((record.ForceCase, sample));
                        interpolatedSamples += added;
                    }
                    else foreach (double station in stations) merged.Add((record.ForceCase, new BeamForceSample(station, 0, 0, 0, 0, 0, 0)));
                }
                // The checks derive their sampling grid from the merged results, even for a pure eigenstrain workflow.
                if (forces.Count == 0 && records.Values.All(r => r.ForceCase == null))
                    foreach (double station in stations) merged.Add((records.Values.First().StrainCase, new BeamForceSample(station, 0, 0, 0, 0, 0, 0)));
                samples[i] = merged.ToArray();
            }
            if (interpolatedSamples > 0) warnings.Add(BridgeWorkflowBuilder.InterpolationWarning(interpolatedSamples));
            var workflow = BridgeWorkflowBuilder.Workflow(phases, method, records);
            var table = BridgeWorkflowBuilder.Combinations(records, combinations);
            var summary = BridgeWorkflowBuilder.Finish(phases, method, records, table, workflow, warnings);

            var model = Structure(reference.Model, records, table, modelIdentity);
            foreach (var phase in workflow.Phases)
                foreach (var a in phase.Actions)
                    model.PreservedSourceData.Add(new PreservedAssignment
                    {
                        Kind = GpcResultCatalog.Kind, SourceRecord = GpcBridge.SourceRecordPrefix + a.Key,
                        RawData = JsonSerializer.Serialize(new Dictionary<string, string> { ["SourceName"] = a.SourceName, ["SourceCase"] = a.SourceCase }),
                        UnitsAndAxes = "", UnsupportedReason = "Former BridGHe action provenance (phase model and case name) kept for the round trip of the port; not a solver input.",
                    });
            Results(model, reference, samples);
            return new GpcBridgeBuild(new GpcBridge(model, Recipe(workflow, model), samples, workflow, beams.Select(b => reference.Angle(b)).ToArray(),
                table.Select(t => (t.Name, (IReadOnlyList<(string Case, double Factor)>)t.Factors.ToArray())).ToArray()), summary, warnings.ToArray());
        }

        /// <summary>The beams of a model in their numbering.</summary>
        private static BeamElement[] Beams(Model model) => model.BeamElements.Values.OrderBy(b => b.Id).ToArray();

        /// <summary>The per-beam local section states of an action (Bridge State Override): the GPC section-state attributes of the beams of the
        /// GPC model of its source (<see cref="BridgeActionDefinition.GpcModel"/>, else its analysed <see cref="BridgeActionDefinition.GpcSource"/>; the beams of the action, in their numbering), whether any is
        /// assigned and the beams whose concrete is excluded. The rules and messages of the former FEM reading
        /// (<see cref="BridgeWorkflowBuilder.LocalSectionStates"/>): one state per beam, the behaviour of the action in a composite phase, and
        /// the fingerprint of the beam property the state was computed for (a stale state is refused).</summary>
        public static (bool HasLocalSectionStates, string[] ConcreteExcludedBeamIds) LocalSectionStates(BridgePhaseDefinition phase, BridgeActionDefinition action)
        {
            var model = action.GpcModel ?? action.GpcSource; // the GPC model of the source: analysed (with its catalog) or its conversion
            if (action.Source != null && model == null) throw new ArgumentException($"Action {action.Key}: its source has no GPC model; recalculate the analysis.");
            // L12: the states are bridge data of the source by beam GUID (one per beam).
            var data = action.GpcBridgeData;
            var assigned = (model == null || data == null ? Array.Empty<BeamElement>() : Beams(model))
                .Where(b => action.BeamIds.Length == 0 || action.BeamIds.Contains(b.Name, StringComparer.Ordinal))
                .Select(b => (BeamId: b.Name, State: data.SectionState(b.Guid), Beam: b)).Where(a => a.State != null).ToArray();
            if (assigned.Select(a => a.BeamId).Distinct(StringComparer.Ordinal).Count() != assigned.Length ||
                assigned.Any(a => (BridgeActionBehavior)(int)a.State.Behavior != action.Behavior || phase.State != BridgeSectionState.Composite))
                throw new ArgumentException($"Action {action.Key}: local FEM section state does not match the mechanical phase. Do not reuse it for shrinkage.");
            if (assigned.Any(a => a.State.PropertyFingerprint != D3.BridgeWorkflowDefinition.PropertyFingerprint(a.Beam)))
                throw new ArgumentException($"Action {action.Key}: FEM properties changed after the local section-state assignment. Reapply Bridge State Override and recalculate.");
            return (assigned.Length > 0, assigned.Where(a => a.State.ConcreteExcluded).Select(a => a.BeamId).ToArray());
        }

        /// <summary>The beams of every model aligned to those of the first by BridGHe ID (GPC name), with the same end points, BridGHe angle and
        /// offsets (as the former FEM alignment, same messages; distances in the definition units).</summary>
        private static BeamElement[][] Align(IReadOnlyList<GpcBridgeSource> models, double tolerance, bool allowAdditionalMembers)
        {
            if (models.Count == 0 || models.Any(m => m == null)) throw new ArgumentException("Provide analysed models in the intended origin order.");
            var first = models[0];
            if (models.Any(m => m.Units.Units != first.Units.Units)) throw new ArgumentException("All phase models must use the same units.");
            var reference = Beams(first.Model);
            if (reference.Length == 0) throw new ArgumentException("No beam results to merge.");
            var units = first.Units;
            double Distance(Point3d a, Point3d b) => units.FromMm(Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z)));
            static Vector3d Offset(BeamElement b) => b.Assignments.OffsetI ?? new Vector3d(0, 0, 0);
            var aligned = new BeamElement[models.Count][];
            for (int i = 0; i < models.Count; i++)
            {
                var map = Beams(models[i].Model).ToDictionary(b => b.Name, StringComparer.Ordinal);
                if (!allowAdditionalMembers && map.Count != reference.Length) throw new ArgumentException($"Model {i + 1}: beam count differs from model 1.");
                aligned[i] = new BeamElement[reference.Length];
                for (int j = 0; j < reference.Length; j++)
                {
                    var source = reference[j];
                    if (!map.TryGetValue(source.Name, out var beam)) throw new ArgumentException($"Model {i + 1}: missing beam {source.Name}.");
                    var offset = Offset(source) - Offset(beam);
                    if (Distance(source.StartPoint, beam.StartPoint) > tolerance || Distance(source.EndPoint, beam.EndPoint) > tolerance
                        || Math.Abs(first.Angle(source) - models[i].Angle(beam)) > 1e-9 || units.FromMm(Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y)) > tolerance)
                        throw new ArgumentException($"Model {i + 1}, beam {source.Name}: geometry, orientation or offsets differ. Reversed axes cannot be merged without transforming results.");
                    aligned[i][j] = beam;
                }
            }
            return aligned;
        }

        /// <summary>The beam of the analysed model of an action for an aligned beam: the same beam when the alignment used that model, else the
        /// beam with its number in the analysed copy of the reference input (same BridGHe ID required).</summary>
        private static BeamElement SourceBeam(Model analysed, BeamElement aligned)
        {
            if (analysed.BeamElements.TryGetValue(aligned.Id, out var beam) && ReferenceEquals(beam, aligned)) return beam;
            if (beam == null || beam.Name != aligned.Name) throw new ArgumentException("GpcActionSourceNotTheAnalysedModel: " + analysed.Name);
            return beam;
        }

        /// <summary>The stored forces of a case (catalog label) of a beam, station in the definition length, zero without sign (the numbers of
        /// the Results components).</summary>
        private static BeamForceSample[] Forces(GpcResultCatalog catalog, Model gpc, BeamElement beam, string label) => GpcResultRows.Beam(catalog, gpc, beam)
            .Where(r => catalog.Entries[r.Entry].Label == label)
            .Select(r => new BeamForceSample(r.Station, GpcResultRows.Shown(r.Axial), GpcResultRows.Shown(r.Shear1), GpcResultRows.Shown(r.Shear2),
                GpcResultRows.Shown(r.Torque), GpcResultRows.Shown(r.Bending1), GpcResultRows.Shown(r.Bending2))).ToArray();

        /// <summary>The positions (definition length from the start) of the non-zero concentrated loads of the cases contributing to a case: those
        /// with a non-zero coefficient of the combination of that name, or the case itself.</summary>
        private static double[] ConcentratedLoads(GpcResultCatalog catalog, Model gpc, BeamElement beam, string label)
        {
            var contributing = gpc.Combinations.TryGetValue(label, out var combination)
                ? combination.GetLoadCaseCoefficientsPair().Where(p => p.Value != 0).Select(p => p.Key.Name).ToHashSet(StringComparer.Ordinal)
                : new[] { label }.ToHashSet(StringComparer.Ordinal);
            double length = catalog.Units.FromMm(beam.Length);
            return beam.Assignments.Loads.Where(l => l?.Concentrated?.LoadCase != null && contributing.Contains(l.Concentrated.LoadCase.Name))
                .Where(l => { var p = l.Concentrated; return p.F1 != 0 || p.F2 != 0 || p.F3 != 0 || p.M1 != 0 || p.M2 != 0 || p.M3 != 0; })
                .Select(l => l.Start * length).ToArray();
        }

        /// <summary>The structure of the bridge model: a copy of the reference input model without its loads (element loads, load assignments,
        /// case-dependent physical data, model loads), cases, combinations, stages, freedom cases, moving loads and source records; the action
        /// cases in the order of the actions; the default "Dead" case of the former model (self weight along −Z, 9806.65 mm/s², structural
        /// mass), which the former bridge model had from the legacy reader (the bridge model is never solved); the combinations of the table.</summary>
        private static Model Structure(Model reference, IReadOnlyDictionary<string, BridgeWorkflowAction> records, IReadOnlyList<(string Name, List<(string Case, double Factor)> Factors)> table, Guid modelIdentity)
        {
            if (reference.Datasets.Count != 0 || reference.Analysis != null) throw new ArgumentException("GpcBridgeReferenceNotAnInputModel: " + reference.Name);
            var model = GPC.Model.Core.ModelValues.Copy(reference, GPC.Model.Core.ModelCopyOptions.None, modelIdentity);
            model.Name = BridgeWorkflowBuilder.MergedModelName;
            model.LoadCases.Clear(); model.Combinations.Clear(); model.Stages.Clear(); model.FreedomCases.Clear();
            model.MovingLoads = null; model.PreservedSourceData.Clear();
            GpcModelTransforms.StripLoads(model);
            if (model.VolumeElements.Count != 0) throw new NotSupportedException("GpcBridgeVolumeElements");
            void Case(string name)
            {
                var c = new LoadCaseBase(GpcIdentity.Case(name), name) { InertiaMasses = new InertiaMassSelection { Structural = false, NonStructural = false } };
                model.LoadCases.Add(c.Name, c);
            }
            foreach (var record in records.Values)
            {
                if (record.ForceCase != null) Case(record.ForceCase);
                if (record.StrainCase != null) Case(record.StrainCase);
            }
            var dead = new LoadCase(GpcIdentity.Case("Dead"), "Dead", LoadCase.LoadCaseTypes.SelfWeight) { InertiaMasses = new InertiaMassSelection { Structural = true, NonStructural = false } };
            model.LoadCases.Add(dead.Name, dead);
            model.ModelLoads.Add(new ModelGravityLoad(Vector3d.ZAxis, -9806.65, dead, CoordinateSystem.Global, "Gravity Dead"));
            foreach (var (name, factors) in table)
            {
                var combination = new Combination(name, GpcIdentity.Combination(name));
                foreach (var (caseName, factor) in factors) combination.AddLoadCaseCoefficient(model.LoadCases[caseName], factor);
                model.Combinations.Add(combination.Name, combination);
            }
            return model;
        }

        /// <summary>The merged beam actions as one dataset (inverse of <see cref="GpcResults.BeamForces"/>, as the C5 reader of the F1 archives).</summary>
        private static void Results(Model gpc, GpcBridgeSource reference, IReadOnlyList<(string Case, BeamForceSample Sample)>[] samples)
        {
            // The element sources of the bridge model are those of the reference input (its element identities); the bridge has its own GUID.
        string revision = gpc.BeamElements.Values.First().Source.ModelRevision;
            var source = new AnalysisSource { Program = Legacy.LegacyModelImport.Program, SolverVersion = "BridGHe phase merge (Build Bridge Model)", ModelRevision = revision, AnalysisId = GpcBridge.DatasetName };
            gpc.AnalysisSource = source;
            var batch = new ResultImportBatch
            {
                Source = source, DatasetId = GpcBridge.DatasetName, SourceHash = "bridge-merge/" + gpc.Guid.ToString("D"), ExpectedInputFingerprint = ModelRevisions.AnalysisFingerprint(gpc),
                Units = new ResultUnits(1, 1, 1), ShellDenominatorLengthToMm = 1,
                ResolvedConvention = "Merged BridGHe beam actions: former principal frame, SF1 = -V1, SF2 = -V2, BM1 = M2, BM2 = -M1.",
                // The reader version of the former seam, kept: the dataset metadata stay those of the 99/50 reference fingerprints.
                ReaderVersion = "BridGHe.Common.FemmBridgeSeam/1", Semantics = AnalysisSemantics.LinearStatic, IsSynthetic = false,
            };
            ResultState State(string caseName) => new ResultState { Semantics = AnalysisSemantics.LinearStatic, ConcomitantStateId = "bridge action " + caseName,
                IsCombined = false, IsCumulative = true, Components = Enumerable.Repeat(ComponentAvailability.Available, 6).ToArray(), Coverage = "Merged actions of Build Bridge Model." };
            var units = reference.Units;
            for (int k = 0; k < samples.Length; k++)
            {
                var beam = gpc.BeamElements[k + 1]; string id = beam.Source.OriginalId; var axes = beam.Assignments.SectionAxes;
                var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, reference.Angle(beam));
                double length = beam.Length;
                foreach (var (c, r) in samples[k])
                {
                    if (!gpc.LoadCases.ContainsKey(c)) throw new ArgumentException("BridgeMergedResultCaseNotInModel: " + c);
                    double station = units.ToMm(r.Station) / length;
                    // Inverse of GpcResults.BeamForces.
                    var shear = principal.V1 * -units.ToN(r.Shear1) + principal.V2 * -units.ToN(r.Shear2);
                    var bending = principal.V2 * units.ToNmm(r.Bending1) + principal.V1 * -units.ToNmm(r.Bending2);
                    double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
                    var point = beam.StartPoint + (beam.EndPoint - beam.StartPoint) * station;
                    if (beam.Assignments.OffsetI != null) point += (beam.Assignments.OffsetAxes ?? axes).ToGlobal(beam.Assignments.OffsetI);
                    var at = new CoordinateSystem(new Point3d(point.X, point.Y, point.Z), new Vector3d(axes.V1.X, axes.V1.Y, axes.V1.Z), new Vector3d(axes.V2.X, axes.V2.Y, axes.V2.Z), new Vector3d(axes.V3.X, axes.V3.Y, axes.V3.Z));
                    batch.Beams.Add(new BeamForceRecord
                    {
                        ElementId = id, Case = c, State = State(c), Axes = at, Station = station, PhysicalDistance = length * station,
                        Body = ActionBody.PositiveSectionFace, StationDomain = "NodeToNode", Record = "bridge beam " + id + " station " + station.ToString("R") + " " + c,
                        Values = new double?[] { units.ToN(r.Axial), Dot(shear, axes.V1), Dot(shear, axes.V2), units.ToNmm(r.Torque), Dot(bending, axes.V1), Dot(bending, axes.V2) },
                    });
                }
            }
            var report = ResultMapper.Import(gpc, batch);
            if (report.Status != ImportStatus.Completed)
                throw new InvalidOperationException("BridgeMergedResultsNotImported: " + report.Status + " " + string.Join("; ", report.Diagnostics.Select(d => d.Code + " " + d.Message)));
        }

        /// <summary>The D3 recipe of the bridge model: cases by name, beams by BridGHe ID, every FEM action sourced from the bridge model and its
        /// force case (the merged actions are its dataset).</summary>
        private static D3.BridgeWorkflowDefinition Recipe(BridgeWorkflow workflow, Model gpc)
        {
            string fingerprint = gpc.AnalysisFingerprint();
            var beams = gpc.BeamElements.Values.ToDictionary(b => b.Source.OriginalId, b => b.Guid, StringComparer.Ordinal);
            Guid? Case(string name)
            {
                if (name == null) return null;
                return gpc.LoadCases.TryGetValue(name, out var c) ? c.Guid : throw new ArgumentException("BridgeWorkflowCaseNotInModel: " + name);
            }
            Guid[] Beams(string[] ids) => (ids ?? Array.Empty<string>()).Select(i => beams.TryGetValue(i ?? "", out var id) ? id : throw new ArgumentException("BridgeWorkflowBeamNotInModel: " + i)).ToArray();
            T Parse<T>(Enum value) where T : struct, Enum =>
                Enum.TryParse<T>(value.ToString(), false, out var parsed) && Enum.IsDefined(typeof(T), parsed) ? parsed : throw new ArgumentException("BridgeWorkflowEnumValue: " + typeof(T).Name + " " + value);
            return new D3.BridgeWorkflowDefinition
            {
                ModelGuid = gpc.Guid, AnalysisFingerprint = fingerprint, Method = Parse<D3.BridgeWorkflowMethod>(workflow.Method),
                Phases = workflow.Phases.Select(p => new D3.BridgeWorkflowPhaseDefinition
                {
                    Id = p.Id, Order = p.Order, Name = p.Name ?? "", State = Parse<D3.BridgeWorkflowSectionState>(p.State), Day = p.Day,
                    Actions = p.Actions.Select(a =>
                    {
                        var force = Case(a.ForceCase);
                        return new D3.BridgeWorkflowActionDefinition
                        {
                            Key = a.Key, SourceModelGuid = force.HasValue ? gpc.Guid : Guid.Empty, SourceAnalysisFingerprint = force.HasValue ? fingerprint : "",
                            SourceCaseGuid = force, SourceName = a.SourceName ?? "", Behavior = Parse<D3.BridgeActionBehavior>(a.Behavior),
                            ForceCaseGuid = force, StrainCaseGuid = Case(a.StrainCase), Strain = a.Strain,
                            BeamGuids = Beams(a.BeamIds), PrimaryOnly = !force.HasValue, ConcreteExcludedBeamGuids = Beams(a.ConcreteExcludedBeamIds),
                        };
                    }).ToArray(),
                }).ToArray(),
            };
        }
    }
}
