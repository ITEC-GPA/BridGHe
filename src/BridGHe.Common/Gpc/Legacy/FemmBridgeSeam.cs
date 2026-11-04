using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Models;
using GPC.Converter;
using GPC.Geometry;
using GPC.Model.Analysis;
using GPC.Model.Core.Units;
using GPC.Model.Elements;
using GPC.Model.Models;
using GPC.Model.Results.Locations;
using GPC.Model.Results.State;
using GPC.Model.Structure.Assignments;
using D3 = GPC.Checkers.CompositeBridge;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>
    /// TRANSITIONAL SEAM (F6 C5, removed with the FEM models, like <see cref="FemmAnalysisSeam"/>): the bridge model of Build Bridge Model as a
    /// GPC model with its results and the GPC recipe <see cref="D3.BridgeWorkflowDefinition"/>, for the GPC workflow executor (D3) of the checks.
    /// <para>The phase merge (beam alignment, one force case per action, union of the stations, resampling of each action from its own grid)
    /// stays the one of <see cref="Checks.BridgeWorkflowBuilder"/>, on the former result objects of the phase models, which the analysis wrote
    /// from the GPC rows bit for bit (<see cref="GpcResultRows"/>, C4). Its merged actions become one GPC dataset of the converted bridge model
    /// through the Converter result mapper (the inverse of the <see cref="GpcResults"/> conventions, verified by C5 on 50 and 99). When the GH
    /// types carry GPC models the merge moves onto the GPC results and this seam disappears.</para>
    /// <para>The recipe is the one of <see cref="LegacyWorkflowImport"/> read from the merged model objects: cases by name, beams by BeamId,
    /// every FEM action sourced from the bridge model and its force case. The former <c>SourceName</c> and <c>SourceCase</c> of every action
    /// (phase model and case name), which D3 does not keep, are BridGHe records of the model (<see cref="SourceRecordPrefix"/> + action key).</para>
    /// </summary>
    public static class FemmBridgeSeam
    {
        public const string Dataset = "BridGHe bridge model", SourceRecordPrefix = "BridgeWorkflowAction/";

        public sealed class Bridge
        {
            public GPC.Model.Models.Model Model { get; }
            public D3.BridgeWorkflowDefinition Workflow { get; }
            public string Dataset => FemmBridgeSeam.Dataset;
            /// <summary>F6 (a)/(b), R4: the merged beam actions as numbers, per beam of the bridge model (GPC beam k + 1), in their stored order:
            /// case name and sample (station mm, BridGHe convention), the numbers of the dataset before its mapping to the section axes.</summary>
            public IReadOnlyList<(string Case, Checks.BeamForceSample Sample)>[] Samples { get; }
            private readonly Dictionary<string, int> beams;
            internal Bridge(GPC.Model.Models.Model model, D3.BridgeWorkflowDefinition workflow, IReadOnlyList<(string Case, Checks.BeamForceSample Sample)>[] samples)
            {
                Model = model; Workflow = workflow; Samples = samples;
                beams = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var b in model.BeamElements) beams.TryAdd(b.Value.Source.OriginalId, b.Key);
            }
            /// <summary>The merged samples of a beam by its BridGHe ID, or null when the bridge model has no such beam.</summary>
            public IReadOnlyList<(string Case, Checks.BeamForceSample Sample)> SamplesOf(string beamId) =>
                beamId != null && beams.TryGetValue(beamId, out int number) ? Samples[number - 1] : null;
        }

        /// <summary>The GPC bridge model of a merged FEM model (Build Bridge Model), N-mm. The merged model is not changed.</summary>
        /// <param name="samples">F6 (a)/(b): the merged beam actions as numbers (<see cref="Checks.BridgeWorkflowBuildResult.Samples"/>), per beam of the
        /// merged model: the dataset is built from them instead of the result objects (the same numbers); null reads the result objects.</param>
        public static Bridge Build(ModelModel merged, double tolerance, IReadOnlyList<(string Case, Checks.BeamForceSample Sample)>[] samples = null)
        {
            if (merged == null) throw new ArgumentNullException(nameof(merged));
            if (merged.BridgeWorkflow == null) throw new ArgumentException("BridgeWorkflowMissing");
            if (merged.Units != ModelModel.ModelUnits.Nmm) throw new ArgumentException("Bridge checks require N-mm-MPa models.");
            // The conversion does not read results: they stay out of its archive (118k samples in example 50) and are put back.
            var results = merged.Elements.Select(e => e.Results.ToList()).ToArray();
            GPC.Model.Models.Model gpc;
            try
            {
                foreach (var e in merged.Elements) e.Results.Clear();
                gpc = GpcModels.FromFemm(merged, tolerance, out _);
            }
            finally { for (int i = 0; i < results.Length; i++) { var list = merged.Elements[i].Results; list.Clear(); foreach (var r in results[i]) list.Add(r); } }

            foreach (var phase in merged.BridgeWorkflow.Phases)
                foreach (var a in phase.Actions)
                    gpc.PreservedSourceData.Add(new PreservedAssignment
                    {
                        Kind = GpcResultCatalog.Kind, SourceRecord = SourceRecordPrefix + a.Key,
                        RawData = JsonSerializer.Serialize(new Dictionary<string, string> { ["SourceName"] = a.SourceName, ["SourceCase"] = a.SourceCase }),
                        UnitsAndAxes = "", UnsupportedReason = "Former BridGHe action provenance (phase model and case name) kept for the round trip of the port; not a solver input.",
                    });
            if (samples != null && samples.Length != merged.Beams.Count) throw new ArgumentException("BridgeMergedSamplesBeamCount");
            samples ??= merged.Beams.Select(b => (IReadOnlyList<(string, Checks.BeamForceSample)>)b.Results.OfType<BeamForceResultModel>()
                .Select(r => (r.LoadCase.Name, new Checks.BeamForceSample(r.NormalizedLength ? r.Station * b.Length : r.Station, r.AxialForce, r.Vx, r.Vy, r.Torque, r.Mx, r.My))).ToArray()).ToArray();
            Results(merged, gpc, samples);
            return new Bridge(gpc, Workflow(merged.BridgeWorkflow, gpc), samples);
        }

        /// <summary>The merged beam actions as one dataset (inverse of <see cref="GpcResults.BeamForces"/>, as the C5 reader of the F1 archives).</summary>
        private static void Results(ModelModel merged, GPC.Model.Models.Model gpc, IReadOnlyList<(string Case, Checks.BeamForceSample Sample)>[] samples)
        {
            string revision = gpc.Guid.ToString("D");
            var source = new AnalysisSource { Program = LegacyModelImport.Program, SolverVersion = "BridGHe phase merge (Build Bridge Model)", ModelRevision = revision, AnalysisId = Dataset };
            gpc.AnalysisSource = source;
            var batch = new ResultImportBatch
            {
                Source = source, DatasetId = Dataset, SourceHash = "bridge-merge/" + revision, ExpectedInputFingerprint = GPC.Model.Models.ModelRevisions.AnalysisFingerprint(gpc),
                Units = new ResultUnits(1, 1, 1), ShellDenominatorLengthToMm = 1,
                ResolvedConvention = "Merged BridGHe beam actions: former principal frame, SF1 = -V1, SF2 = -V2, BM1 = M2, BM2 = -M1.",
                ReaderVersion = "BridGHe.Common.FemmBridgeSeam/1", Semantics = AnalysisSemantics.LinearStatic, IsSynthetic = false,
            };
            ResultState State(string caseName) => new ResultState { Semantics = AnalysisSemantics.LinearStatic, ConcomitantStateId = "bridge action " + caseName,
                IsCombined = false, IsCumulative = true, Components = Enumerable.Repeat(ComponentAvailability.Available, 6).ToArray(), Coverage = "Merged actions of Build Bridge Model." };
            for (int k = 0; k < merged.Beams.Count; k++)
            {
                var legacy = merged.Beams[k]; var beam = gpc.BeamElements[k + 1]; string id = beam.Source.OriginalId; var axes = beam.Assignments.SectionAxes;
                var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, legacy.AngleDeg);
                double length = beam.Length;
                foreach (var (c, r) in samples[k])
                {
                    if (!gpc.LoadCases.ContainsKey(c)) throw new ArgumentException("BridgeMergedResultCaseNotInModel: " + c);
                    double station = r.Station / length;
                    // Inverse of GpcResults.BeamForces.
                    var shear = principal.V1 * -r.Shear1 + principal.V2 * -r.Shear2;
                    var bending = principal.V2 * r.Bending1 + principal.V1 * -r.Bending2;
                    double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
                    var point = beam.StartPoint + (beam.EndPoint - beam.StartPoint) * station;
                    if (beam.Assignments.OffsetI != null) point += (beam.Assignments.OffsetAxes ?? axes).ToGlobal(beam.Assignments.OffsetI);
                    var at = new CoordinateSystem(new Point3d(point.X, point.Y, point.Z), new Vector3d(axes.V1.X, axes.V1.Y, axes.V1.Z), new Vector3d(axes.V2.X, axes.V2.Y, axes.V2.Z), new Vector3d(axes.V3.X, axes.V3.Y, axes.V3.Z));
                    batch.Beams.Add(new BeamForceRecord
                    {
                        ElementId = id, Case = c, State = State(c), Axes = at, Station = station, PhysicalDistance = length * station,
                        Body = ActionBody.PositiveSectionFace, StationDomain = "NodeToNode", Record = "bridge beam " + id + " station " + station.ToString("R") + " " + c,
                        Values = new double?[] { r.Axial, Dot(shear, axes.V1), Dot(shear, axes.V2), r.Torque, Dot(bending, axes.V1), Dot(bending, axes.V2) },
                    });
                }
            }
            var report = ResultMapper.Import(gpc, batch);
            if (report.Status != ImportStatus.Completed)
                throw new InvalidOperationException("BridgeMergedResultsNotImported: " + report.Status + " " + string.Join("; ", report.Diagnostics.Select(d => d.Code + " " + d.Message)));
        }

        /// <summary>The recipe of the merged model objects, as <see cref="LegacyWorkflowImport.Import"/> reads it from their archive.</summary>
        public static D3.BridgeWorkflowDefinition Workflow(Checks.BridgeWorkflow workflow, GPC.Model.Models.Model gpc)
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

        /// <summary>The former provenance of an action (phase model and case name), or null.</summary>
        public static (string SourceName, string SourceCase)? SourceOf(GPC.Model.Models.Model gpc, string actionKey)
        {
            var record = gpc.PreservedSourceData.LastOrDefault(r => r != null && r.Kind == GpcResultCatalog.Kind && r.SourceRecord == SourceRecordPrefix + actionKey);
            if (record == null) return null;
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(record.RawData);
            return (values.TryGetValue("SourceName", out var n) ? n : null, values.TryGetValue("SourceCase", out var c) ? c : null);
        }
    }
}
