#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using GPC.Converter;
using GPC.Geometry;
using GPC.Model.Analysis;
using GPC.Model.Core.Units;
using GPC.Model.Models;
using GPC.Model.Results.Locations;
using GPC.Model.Results.State;
using GPC.Model.Structure.Assignments;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using D3 = GPC.Checkers.CompositeBridge;

/// <summary>
/// F6 chain C: the GPC bridge model of Build BridGHe Model (<see cref="GpcBridgeBuilder"/>, from the GPC models only) against the former
/// seam on the merged FEM model (FemmBridgeSeam.Build, F6 C5 to step 24; its code is kept below as the TESTS ONLY reference, removed with the
/// FEM models). The seam converted the merged FEM model with the identity of its own archive; here it converts it with the identity of the
/// input model of the first reference, the identity the GPC bridge model keeps (a GPC copy). With that identity everything must be equal
/// bit for bit: the merged samples (every bit, sign of zero included), the summary and the warnings of the FEM merge, the analysis
/// fingerprint and the complete GPC archive of the model (structure, dataset, source records) and the D3 recipe (fingerprint).
/// </summary>
internal static class GpcBridgeComparison
{
    /// <summary>TESTS ONLY: the former seam on the merged FEM model, with an explicit identity.</summary>
    internal static (Model Model, D3.BridgeWorkflowDefinition Workflow) Legacy(ModelModel merged, double tolerance, IReadOnlyList<(string Case, BeamForceSample Sample)>[] samples, Guid owner, Guid modelIdentity)
    {
        var results = merged.Elements.Select(e => e.Results.ToList()).ToArray();
        Model gpc;
        try
        {
            foreach (var e in merged.Elements) e.Results.Clear();
            gpc = LegacyModelImport.Import(LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(merged)), owner, tolerance, new LegacyImportReport(), GpcModels.SectionStateSignatures(merged));
        }
        finally { for (int i = 0; i < results.Length; i++) { var list = merged.Elements[i].Results; list.Clear(); foreach (var r in results[i]) list.Add(r); } }
        // The bridge model has its own model identity and the element identities of the reference (L11 copy under a new identity).
        gpc = GPC.Model.Core.ModelValues.Copy(gpc, GPC.Model.Core.ModelCopyOptions.None, modelIdentity);
        foreach (var phase in merged.BridgeWorkflow.Phases)
            foreach (var a in phase.Actions)
                gpc.PreservedSourceData.Add(new PreservedAssignment
                {
                    Kind = GpcResultCatalog.Kind, SourceRecord = GpcBridge.SourceRecordPrefix + a.Key,
                    RawData = JsonSerializer.Serialize(new Dictionary<string, string> { ["SourceName"] = a.SourceName, ["SourceCase"] = a.SourceCase }),
                    UnitsAndAxes = "", UnsupportedReason = "Former BridGHe action provenance (phase model and case name) kept for the round trip of the port; not a solver input.",
                });
        LegacyResults(merged, gpc, samples);
        return (gpc, LegacyWorkflow(merged.BridgeWorkflow, gpc));
    }

    private static void LegacyResults(ModelModel merged, Model gpc, IReadOnlyList<(string Case, BeamForceSample Sample)>[] samples)
    {
        // The element sources of the bridge model are those of the reference input (its element identities); the bridge has its own GUID.
        string revision = gpc.BeamElements.Values.First().Source.ModelRevision;
        var source = new AnalysisSource { Program = LegacyModelImport.Program, SolverVersion = "BridGHe phase merge (Build Bridge Model)", ModelRevision = revision, AnalysisId = GpcBridge.DatasetName };
        gpc.AnalysisSource = source;
        var batch = new ResultImportBatch
        {
            Source = source, DatasetId = GpcBridge.DatasetName, SourceHash = "bridge-merge/" + gpc.Guid.ToString("D"), ExpectedInputFingerprint = ModelRevisions.AnalysisFingerprint(gpc),
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

    private static D3.BridgeWorkflowDefinition LegacyWorkflow(BridgeWorkflow workflow, Model gpc)
    {
        string fingerprint = gpc.AnalysisFingerprint();
        var beams = gpc.BeamElements.Values.ToDictionary(b => b.Source.OriginalId, b => b.Guid, StringComparer.Ordinal);
        Guid? Case(string name) => name == null ? null : gpc.LoadCases.TryGetValue(name, out var c) ? c.Guid : throw new ArgumentException("BridgeWorkflowCaseNotInModel: " + name);
        Guid[] Beams(string[] ids) => (ids ?? Array.Empty<string>()).Select(i => beams.TryGetValue(i ?? "", out var id) ? id : throw new ArgumentException("BridgeWorkflowBeamNotInModel: " + i)).ToArray();
        T Parse<T>(Enum value) where T : struct, Enum => Enum.TryParse<T>(value.ToString(), false, out var parsed) && Enum.IsDefined(typeof(T), parsed) ? parsed : throw new ArgumentException("BridgeWorkflowEnumValue: " + value);
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

    private static string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
    private static readonly System.Text.RegularExpressions.Regex RandomGuid = new System.Text.RegularExpressions.Regex("[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static string Fp(params object[] values) => GPC.Model.Core.ModelValues.Fingerprint(values);

    /// <summary>The GPC bridge model against the former seam on the FEM merge of the same inputs (see the class). Returns a summary line.</summary>
    internal static string Require(string label, GpcBridgeBuild gpc, BridgeWorkflowBuildResult fem, double tolerance)
    {
        void Fail(string what) => throw new Exception("GPC BRIDGE FAIL (" + label + "): " + what);
        var bridge = gpc.Bridge;
        if (bridge.Samples.Length != fem.Samples.Length) Fail($"{bridge.Samples.Length} beams on GPC, {fem.Samples.Length} in the FEM merge");
        // P2T request 3: the checks on the GPC bridge model alone derive what they derived from the merged FEM model (combination factors,
        // principal frames, stations, property fingerprints, assumptions), bit for bit, or fail the same way.
        string Checks(Func<GpcBridgeChecks> create) { try { return create().Signature(); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { return "error " + e.Message; } }
        string withFem = Checks(() => new GpcBridgeChecks(bridge, fem.Model)), alone = Checks(() => new GpcBridgeChecks(bridge));
        if (withFem != alone)
        {
            var x = withFem.Split('\n'); var y = alone.Split('\n'); int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            Fail("the checks on the GPC bridge model alone differ from those with the merged FEM model: " + (line < 0 ? $"{x.Length} / {y.Length} lines"
                : $"line {line + 1}: FEM '{(x[line].Length > 300 ? x[line].Substring(0, 300) : x[line])}' / GPC '{(y[line].Length > 300 ? y[line].Substring(0, 300) : y[line])}'"));
        }
        long values = 0;
        for (int k = 0; k < fem.Samples.Length; k++)
        {
            var a = bridge.Samples[k]; var b = fem.Samples[k];
            if (a.Count != b.Count) Fail($"beam {k + 1}: {a.Count} samples on GPC, {b.Count} in the FEM merge");
            for (int i = 0; i < a.Count; i++)
            {
                string x = a[i].Case + "|" + Bits(a[i].Sample.Station) + "|" + string.Join(",", a[i].Sample.Values.Select(Bits));
                string y = b[i].Case + "|" + Bits(b[i].Sample.Station) + "|" + string.Join(",", b[i].Sample.Values.Select(Bits));
                if (x != y) Fail($"beam {k + 1}, sample {i}: GPC {x} / FEM {y}");
                values += 7;
            }
        }
        if (!gpc.Summary.SequenceEqual(fem.Summary)) Fail("summary: GPC [" + string.Join(" / ", gpc.Summary) + "] FEM [" + string.Join(" / ", fem.Summary) + "]");
        if (!gpc.Warnings.SequenceEqual(fem.Warnings)) Fail("warnings: GPC [" + string.Join(" / ", gpc.Warnings) + "] FEM [" + string.Join(" / ", fem.Warnings) + "]");
        var owner = GpcInputComparison.ElementOwner(bridge.Model);
        if (owner == bridge.Model.Guid) Fail("the bridge model has the model GUID of its reference");
        var (model, workflow) = Legacy(fem.Model, tolerance, fem.Samples, owner, bridge.Model.Guid);
        List<string> Parts() => PartsDiffer(bridge.Model, model);
        if (bridge.Model.AnalysisFingerprint() != model.AnalysisFingerprint()) Fail("analysis fingerprint differs: " + string.Join("; ", Parts().Take(12)));
        // Every part of the model by its content fingerprint (cases, combinations, model loads, freedom cases, stages, groups, members,
        // source records, constraints, dataset metadata, every element): always, also where the XML document cannot be written.
        var parts = Parts();
        if (parts.Count != 0) Fail("parts differ: " + string.Join("; ", parts.Take(12)) + (parts.Count > 12 ? $"; ... {parts.Count} in all" : ""));
        // Every stored result of the dataset (station, side, six components), bit for bit.
        long results = 0;
        foreach (var beam in bridge.Model.BeamElements.Values)
        {
            var other = model.BeamElements[beam.Id];
            foreach (var c in bridge.Model.LoadCases.Keys)
            {
                string Row(GPC.Model.Results.Locations.StationResultBeamForces s) { var f = s.ResultBeamForces; return Bits(s.ParametricDistance) + "|" + s.Side + "|" + string.Join(",", new[] { f.N, f.V1, f.V2, f.T, f.M1, f.M2 }.Select(Bits)); }
                var a = GPC.Model.Results.Queries.ResultQueries.BeamSamples(beam, bridge.Dataset, c).Select(Row).ToArray();
                var b = GPC.Model.Results.Queries.ResultQueries.BeamSamples(other, GpcBridge.DatasetName, c).Select(Row).ToArray();
                if (!a.SequenceEqual(b)) Fail($"stored results of beam {beam.Id}, case {c}: {a.Length} / {b.Length} samples, first difference " + a.Zip(b).FirstOrDefault(p => p.First != p.Second));
                results += a.Length;
            }
        }
        // The complete XML document of the model (every field of every object), with only the random identities masked: the geometry values
        // of GPC (points, vectors, frames) carry a new version-4 GUID each time they are built, so two constructions are never byte-identical;
        // every deterministic identity (model, elements, cases, combinations, groups) stays in the comparison. Not writable beyond the
        // 2,000,000 objects of the serializer (example 50 with its 118k merged samples): there the parts and the results above decide.
        string Document(Model m) => GpcInputComparison.Document(m); // canonical: random GUIDs masked, references inlined
        string document = null, expected = null, xml;
        try { document = Document(bridge.Model); expected = Document(model); }
        catch (System.Runtime.Serialization.SerializationException e) when (e.Message.Contains("Maximum number of items")) { document = expected = null; }
        if (document == null) xml = "XML document beyond the serializer limit (compared by parts)";
        else if (document != expected)
        {
            string folder = Path.Combine(Path.GetTempPath(), "bridghe-gpc-bridge-diff"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, label.Replace(' ', '-') + "-gpc.xml"), document); File.WriteAllText(Path.Combine(folder, label.Replace(' ', '-') + "-seam.xml"), expected);
            var x = document.Split('\n'); var y = expected.Split('\n');
            int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            string first = line < 0 ? $"equal up to line {Math.Min(x.Length, y.Length)} ({x.Length} / {y.Length} lines)" : $"line {line + 1}: GPC '{x[line].Trim()}' / seam '{y[line].Trim()}'";
            Fail($"XML document differs, both in {folder}: {first}");
            xml = null;
        }
        else xml = $"XML document equal ({document.Length} characters)";
        if (bridge.Workflow.Fingerprint() != workflow.Fingerprint()) Fail("D3 recipe differs");
        bridge.Workflow.Validate(bridge.Model); bridge.Workflow.ValidateSources(bridge.Model, new[] { bridge.Model });
        return $"{bridge.Model.BeamElements.Count} beams, {bridge.Model.LoadCases.Count} cases, {bridge.Model.Combinations.Count} combinations, {fem.Samples.Sum(s => s.Count)} merged samples ({values} numbers), {results} stored results, parts equal, {xml}, recipe {bridge.Workflow.Phases.Sum(p => p.Actions.Length)} actions";
    }

    /// <summary>Native (99, 50), finding 1 of P2T: every action with a FEM case that reaches a component (Build BridGHe Model, Bridge Detail
    /// Stations, ...) on a wire, alone or in a phase, carries its analysed GPC source and the GPC model of its source (the same model): no
    /// consumer falls back to the FEM results silently.</summary>
    internal static void RequireGpcLinks(GH_Document doc, string label)
    {
        int actions = 0, components = 0;
        foreach (var c in doc.Objects.OfType<GH_Component>())
        {
            bool any = false;
            foreach (var p in c.Params.Input)
                foreach (var goo in p.VolatileData.AllData(true).Where(g => g?.GetType().Name == "BridgeWorkflowGoo"))
                {
                    var value = goo.ScriptVariable();
                    var list = value is BridgePhaseDefinition phase ? phase.Actions : value is BridgeActionDefinition a ? new[] { a } : Array.Empty<BridgeActionDefinition>();
                    foreach (var action in list.Where(x => !string.IsNullOrWhiteSpace(x.Case)))
                    {
                        if (action.GpcSource == null || action.GpcModel == null || !ReferenceEquals(action.GpcSource, action.GpcModel))
                            throw new Exception($"GPC BRIDGE FAIL ({label}): {c.Name} '{c.NickName}', input {p.Name}: action {action.Key} without its GPC source/model");
                        actions++; any = true;
                    }
                }
            if (any) components++;
        }
        if (actions == 0) throw new Exception($"GPC BRIDGE FAIL ({label}): no action with a FEM case on the wires");
        Console.WriteLine($"GPC LINKS PASS ({label}): {actions} actions with a FEM case on the inputs of {components} components, all with their GPC source and model.");
    }

    /// <summary>Regression, finding 1 of P2T: a duplicated action or phase (GH copy) keeps the GPC links of its actions by reference.</summary>
    private static void DuplicateKeepsGpcLinks(BridgeActionDefinition action, BridgePhaseDefinition phase)
    {
        var plugin = BridgeMergeTests.Initialize();
        var type = plugin.GetTypes().Single(t => t.Name == "BridgeWorkflowGoo");
        foreach (var value in new object[] { action, phase })
        {
            var goo = (IGH_Goo)Activator.CreateInstance(type, new[] { value });
            var copy = goo.Duplicate().ScriptVariable();
            var pairs = value is BridgePhaseDefinition p ? p.Actions.Zip(((BridgePhaseDefinition)copy).Actions) : new[] { ((BridgeActionDefinition)value, (BridgeActionDefinition)copy) };
            foreach (var (s, d) in pairs)
            {
                if (ReferenceEquals(s, d) || ReferenceEquals(s.Source, d.Source) && s.Source != null) throw new Exception("GPC BRIDGE FAIL (duplicate): the copy is not a copy");
                if (!ReferenceEquals(s.GpcSource, d.GpcSource) || !ReferenceEquals(s.GpcModel, d.GpcModel) || d.GpcSource == null && s.Key == action.Key)
                    throw new Exception($"GPC BRIDGE FAIL (duplicate): the copy of {s.Key} lost its GPC source or model");
            }
        }
        Console.WriteLine("GPC BRIDGE (duplicate): a duplicated action and phase keep the GPC source and model of their actions.");
    }

    /// <summary>Native (99, 50): the GPC bridge model attached by Build BridGHe Model, rebuilt from the inputs of the component on GPC and
    /// compared bit for bit with the former seam on the FEM merge (see the class); the attached one has the same fingerprints.</summary>
    internal static void RequireComponent(GH_Component merge, string label)
    {
        var goos = merge.Params.Input[0].VolatileData.AllData(true).ToArray();
        var models = goos.Select(g => (ModelModel)g.GetType().GetProperty("Value").GetValue(g)).ToArray();
        var references = goos.Select(g => GpcBridgeSource.Reference((GpcModels.Conversion)g.GetType().GetProperty("GpcInput").GetValue(g), (Model)g.GetType().GetProperty("GpcResults").GetValue(g))).ToArray();
        var phases = merge.Params.Input[1].VolatileData.AllData(true).Select(g => (BridgePhaseDefinition)((IGH_Goo)g).ScriptVariable()).ToArray();
        string Cell(IGH_Goo goo) => goo?.ScriptVariable() is IFormattable n ? n.ToString(null, CultureInfo.InvariantCulture) : goo?.ScriptVariable()?.ToString() ?? "";
        var data = merge.Params.Input[2].VolatileData;
        var table = Enumerable.Range(0, data.PathCount).Select(b => data.get_Branch(b).Cast<IGH_Goo>().Select(Cell).ToArray()).ToArray();
        var method = (BridgeWorkflowMethod)((GH_Integer)merge.Params.Input[3].VolatileData.AllData(true).Single()).Value;
        bool additional = merge.Params.Input[4].VolatileData.AllData(true).Cast<GH_Boolean>().SingleOrDefault()?.Value ?? false;
        double tolerance = global::Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001; // Settings.Tolerance of the plug-in
        // L11: the local section states of every action read from the GPC section-state attributes equal the former FEM reading.
        int withStates = 0, excludedBeams = 0;
        foreach (var phase in phases)
            foreach (var a in phase.Actions)
            {
                string femError = null, gpcError = null; (bool Has, string[] Excluded) f = default, g = default;
                try { f = BridgeWorkflowBuilder.LocalSectionStates(phase, a); } catch (ArgumentException e) { femError = e.Message; }
                try { g = GpcBridgeBuilder.LocalSectionStates(phase, a); } catch (ArgumentException e) { gpcError = e.Message; }
                if (femError != gpcError || f.Has != g.Has || !(f.Excluded ?? Array.Empty<string>()).SequenceEqual(g.Excluded ?? Array.Empty<string>(), StringComparer.Ordinal))
                    throw new Exception($"GPC BRIDGE FAIL ({label}): local section states of {a.Key}: FEM {f.Has}/{f.Excluded?.Length} '{femError}', GPC {g.Has}/{g.Excluded?.Length} '{gpcError}'");
                if (g.Has) { withStates++; excludedBeams += g.Excluded.Length; }
            }
        Console.WriteLine($"GPC BRIDGE ({label}): local section states from the GPC attributes equal to the FEM reading: {withStates} actions with states, {excludedBeams} concrete-excluded beams.");
        var started = DateTime.UtcNow;
        var gpc = GpcBridgeBuilder.Build(references, phases, table, method, tolerance, additional, GpcIdentity.Derive(merge.InstanceGuid, "bridge-model"));
        double seconds = (DateTime.UtcNow - started).TotalSeconds;
        var fem = BridgeWorkflowBuilder.Build(models, phases, table, method, tolerance, additional);
        string summary = Require(label, gpc, fem, tolerance);
        var output = merge.Params.Output[0].VolatileData.AllData(true).Single();
        var attached = (GpcBridge)output.GetType().GetProperty("GpcBridge").GetValue(output) ?? throw new Exception("GPC BRIDGE FAIL (" + label + "): Build BridGHe Model attached no GPC bridge model");
        if (attached.Model.AnalysisFingerprint() != gpc.Bridge.Model.AnalysisFingerprint() || attached.Workflow.Fingerprint() != gpc.Bridge.Workflow.Fingerprint()
            || attached.Model.Datasets.Count != gpc.Bridge.Model.Datasets.Count)
            throw new Exception("GPC BRIDGE FAIL (" + label + "): the attached GPC bridge model differs from the one rebuilt from the inputs");
        Console.WriteLine($"GPC BRIDGE PASS ({label}): the GPC bridge model built from the GPC models ({seconds:F1} s) equals bit for bit the former seam on the FEM merge with the reference identity: {summary}; the attached one is the same.");
    }

    /// <summary>Regression (not native) <c>gpc-bridge-model</c>: the examples 01–10 (node, beam and plate loads, combinations, moving loads,
    /// stages) as reference models with synthetic beam results of an extra case, a steel action from them and a primary shrinkage, plus the
    /// two-model workflow of <see cref="BridgeWorkflowTests"/> and a strain-only workflow: GPC bridge model = former seam bit for bit, or the
    /// same error.</summary>
    internal static void Regression()
    {
        const double tolerance = 1e-6;
        int compared = 0, errors = 0;
        void Compare(string label, ModelModel[] models, Func<ModelModel, GPC.Model.Models.Model> analysed, BridgePhaseDefinition[] phases, string[][] table)
        {
            var sources = models.Select(m => (Fem: m, Gpc: analysed(m))).ToArray();
            BridgeActionDefinition Link(BridgeActionDefinition a) => a.Source == null ? a : a with { GpcSource = sources.First(s => ReferenceEquals(s.Fem, a.Source)).Gpc, GpcModel = sources.First(s => ReferenceEquals(s.Fem, a.Source)).Gpc };
            var linked = phases.Select(p => p with { Actions = p.Actions.Select(Link).ToArray() }).ToArray();
            string femError = null, gpcError = null; BridgeWorkflowBuildResult fem = null; GpcBridgeBuild gpc = null;
            try { fem = BridgeWorkflowBuilder.Build(models, linked, table, BridgeWorkflowMethod.Compatible, tolerance); } catch (ArgumentException e) { femError = e.Message; }
            try
            {
                var references = sources.Select(s => GpcBridgeSource.Reference(GpcModels.Convert(s.Fem, tolerance), s.Gpc)).ToArray();
                gpc = GpcBridgeBuilder.Build(references, linked, table, BridgeWorkflowMethod.Compatible, tolerance, false, GpcIdentity.Derive(GpcIdentity.NameSpace, "test-bridge:" + label));
            }
            catch (ArgumentException e) { gpcError = e.Message; }
            if (femError != null || gpcError != null)
            {
                if (femError != gpcError) throw new Exception($"GPC BRIDGE FAIL ({label}): FEM error '{femError}', GPC error '{gpcError}'");
                Console.WriteLine($"GPC BRIDGE ({label}): same error on both paths: {femError}"); errors++; return;
            }
            Console.WriteLine($"GPC BRIDGE ({label}): " + Require(label, gpc, fem, tolerance)); compared++;
        }
        var shrinkage = new BridgeActionDefinition { Key = "RIT", Behavior = BridgeActionBehavior.Shrinkage, Strain = -2e-4, PrimaryOnly = true };
        foreach (var example in NativeExamples.Create())
        {
            var m = example.Model;
            if (m.Beams.Count == 0) { Console.WriteLine($"GPC BRIDGE ({example.Id}): no beams, no bridge workflow (skipped)"); continue; }
            var lc = new LoadCaseModel("R", "R"); m.LoadCases.Add(lc);
            for (int k = 0; k < m.Beams.Count; k++)
                foreach (double x in new[] { 0d, .5, 1d })
                    m.Beams[k].Results.Add(new BeamForceResultModel(lc, x, 0, "", true) { AxialForce = 10 * (k + 1), Vx = -3 * x, Vy = 2 + x, Torque = 0.5 * k, Mx = 70 * x * (1 - x), My = 1000 * (1 + x) * (k + 1) });
            var steel = new BridgeActionDefinition { Key = "G1", Source = m, Case = "R", Behavior = BridgeActionBehavior.Steel };
            var phases = new[] { new BridgePhaseDefinition { Order = 1, Name = "Steel", State = BridgeSectionState.Steel, Actions = new[] { steel } },
                new BridgePhaseDefinition { Order = 2, Name = "Composite", State = BridgeSectionState.Composite, Actions = new[] { shrinkage } } };
            Compare(example.Id, new[] { m }, f => GpcSyntheticResults.Analysed(f, tolerance), phases, new[] { new[] { "", "G1", "RIT" }, new[] { "SLS", "1", "0.7" } });
        }
        {
            // Sources with nodes (the legacy reader needs them): the four-beam simply supported example with results of a case "G".
            ModelModel Source(string name, double force, params double[] grid)
            {
                var m = NativeExamples.Create().Single(e => e.Id == "02-simply-supported").Model; m.Name = name; var g = new LoadCaseModel("G", "G"); m.LoadCases.Add(g);
                for (int k = 0; k < m.Beams.Count; k++)
                    foreach (double x in grid) m.Beams[k].Results.Add(new BeamForceResultModel(g, x, 0, "", true) { AxialForce = force + k, Vy = force * (1 - 2 * x), My = force * 1000 * (1 + x) * (k + 1) });
                return m;
            }
            var steel = Source("Steel", 100, 0, .25, .5, 1); var shrink = Source("Shrinkage", 20, 0, .5, 1); var traffic = Source("Traffic", 50, 0, .4, .75, 1); // different grids: union and resampling
            var a = new BridgeActionDefinition { Key = "G1", Source = steel, Case = "G", Behavior = BridgeActionBehavior.Steel };
            var r = new BridgeActionDefinition { Key = "RITIRO", Source = shrink, Case = "G", Behavior = BridgeActionBehavior.Shrinkage, Strain = -200e-6 };
            var q = new BridgeActionDefinition { Key = "Q1", Source = traffic, Case = "G", Behavior = BridgeActionBehavior.ShortTerm, BeamIds = new[] { "2" } };
            var p1 = new BridgePhaseDefinition { Order = 10, Name = "Getto", State = BridgeSectionState.Steel, Actions = new[] { a }, Day = 0 };
            var p2 = new BridgePhaseDefinition { Order = 20, Name = "Misto", State = BridgeSectionState.Composite, Actions = new[] { r }, Day = 28 };
            var p3 = new BridgePhaseDefinition { Order = 30, Name = "Traffico", State = BridgeSectionState.Composite, Actions = new[] { q } };
            Compare("two models", new[] { steel, shrink }, f => GpcSyntheticResults.Analysed(f, tolerance), new[] { p2, p1 },
                new[] { new[] { "", "G1", "RITIRO" }, new[] { "SLS", "1", "0.7" }, new[] { "ULS", "1.35", "1" } });
            // A beam selection (zero rows on the other beam) on the source of the shrinkage, and an action absent from the combinations.
            Compare("selection", new[] { steel, shrink, traffic }, f => GpcSyntheticResults.Analysed(f, tolerance), new[] { p1, p2, p3 }, new[] { new[] { "", "G1", "RITIRO" }, new[] { "SLS", "1", "1" } });
            // The same FEM increment on overlapping beams: the same error on both paths.
            Compare("reused increment", new[] { steel, shrink, traffic }, f => GpcSyntheticResults.Analysed(f, tolerance), new[] { p1, p2, p3 with { Actions = new[] { q with { Case = "G", Source = steel, Behavior = BridgeActionBehavior.ShortTerm, BeamIds = Array.Empty<string>() } } } },
                new[] { new[] { "", "G1", "RITIRO", "Q1" }, new[] { "SLS", "1", "1", "1" } });
            // L11: local section states (Bridge State Override) on the beams of the traffic source, read from the GPC section-state attributes:
            // current states (one cracked, one uncracked), a stale state (property changed after the assignment), a state of another behaviour,
            // a state on a shrinkage source; the same records (HasLocalSectionStates, concrete-excluded beams) or the same error.
            ModelModel WithStates(Func<BeamModel, int, BridgeSectionAssignment> state)
            {
                var m = new ModelModel(traffic);
                for (int k = 0; k < m.Beams.Count; k++) { var s = state(m.Beams[k], k); if (s != null) m.Beams[k].Attributes.Add(new BridgeSectionStateAttributeModel(s)); }
                return m;
            }
            var all = q with { BeamIds = Array.Empty<string>() };
            foreach (var (variant, states, action) in new (string, ModelModel, BridgeActionDefinition)[]
            {
                ("states", WithStates((b, k) => new BridgeSectionAssignment(BridgeActionBehavior.ShortTerm, k % 2 == 1, 6.2 + k, "G", BridgeSectionAssignment.Signature(b.BeamProperty))), all),
                ("states selection", WithStates((b, k) => new BridgeSectionAssignment(BridgeActionBehavior.ShortTerm, true, 6.2, null, BridgeSectionAssignment.Signature(b.BeamProperty))), q),
                ("stale state", WithStates((b, k) => k == 1 ? new BridgeSectionAssignment(BridgeActionBehavior.ShortTerm, true, 6.2, "G", "changed") : null), all),
                ("state of another behaviour", WithStates((b, k) => k == 0 ? new BridgeSectionAssignment(BridgeActionBehavior.LongTerm, false, 18, "G", BridgeSectionAssignment.Signature(b.BeamProperty)) : null), all),
            })
            {
                var linkedAction = action with { Source = states };
                Compare("local " + variant, new[] { steel, shrink, states }, f => GpcSyntheticResults.Analysed(f, tolerance), new[] { p1, p2, p3 with { Actions = new[] { linkedAction } } },
                    new[] { new[] { "", "G1", "RITIRO", "Q1" }, new[] { "SLS", "1", "1", "1" } });
            }
            var shrinkStates = WithStates((b, k) => new BridgeSectionAssignment(BridgeActionBehavior.ShortTerm, false, 6.2, "G", BridgeSectionAssignment.Signature(b.BeamProperty)));
            Compare("local state on shrinkage", new[] { steel, shrinkStates }, f => GpcSyntheticResults.Analysed(f, tolerance), new[] { p1, p2 with { Actions = new[] { r with { Source = shrinkStates } } } },
                new[] { new[] { "", "G1", "RITIRO" }, new[] { "SLS", "1", "0.7" } });
            var analysedTraffic = GpcSyntheticResults.Analysed(traffic, tolerance);
            var linkedQ = q with { GpcSource = analysedTraffic, GpcModel = analysedTraffic };
            DuplicateKeepsGpcLinks(linkedQ, p3 with { Actions = new[] { linkedQ, r } });
        }
        {
            var m = NativeExamples.Create().Single(e => e.Id == "02-simply-supported").Model;
            var phases = new[] { new BridgePhaseDefinition { Order = 1, Name = "Steel", State = BridgeSectionState.Steel },
                new BridgePhaseDefinition { Order = 2, Name = "Composite", State = BridgeSectionState.Composite, Actions = new[] { shrinkage } } };
            Compare("strain only", new[] { m }, f => null, phases, new[] { new[] { "", "RIT" }, new[] { "SLS", "1" } });
        }
        if (compared < 10) throw new Exception($"GPC BRIDGE FAIL: only {compared} workflows compared ({errors} errors)");
        Console.WriteLine($"GPC BRIDGE MODEL PASS: {compared} workflows bit for bit, {errors} with the same error on both paths.");
    }

    /// <summary>The parts of two models whose content fingerprints differ (cases, combinations, model loads, freedom cases, stages, groups,
    /// members, source records, constraints, dataset metadata, every element).</summary>
    internal static List<string> PartsDiffer(Model m, Model e)
    {
        var lines = new List<string>();
            void Same(string what, object x, object y) { string fx = Fp(x), fy = Fp(y); if (fx != fy) lines.Add(what); }
            Same("name/guid", new object[] { m.Name, m.Guid }, new object[] { e.Name, e.Guid });
            Same("load cases [" + string.Join(", ", m.LoadCases.Keys) + "] / [" + string.Join(", ", e.LoadCases.Keys) + "]", m.LoadCases.Values.ToArray(), e.LoadCases.Values.ToArray());
            Same("combinations", m.Combinations.Values.ToArray(), e.Combinations.Values.ToArray());
            Same("model loads", m.ModelLoads.Values.ToArray(), e.ModelLoads.Values.ToArray());
            Same("freedom cases/stages", new object[] { m.FreedomCases.Values.ToArray(), m.Stages.Values.ToArray() }, new object[] { e.FreedomCases.Values.ToArray(), e.Stages.Values.ToArray() });
            Same("groups", m.Groups.Values.Select(g => new object[] { g.Guid, g.Name, g.Parent?.Guid }).ToArray(), e.Groups.Values.Select(g => new object[] { g.Guid, g.Name, g.Parent?.Guid }).ToArray());
            Same("physical members", m.PhysicalMembers.Values.ToArray(), e.PhysicalMembers.Values.ToArray());
            Same("preserved", m.PreservedSourceData.ToArray(), e.PreservedSourceData.ToArray());
            Same("constraints", m.Costrains.Values.ToArray(), e.Costrains.Values.ToArray());
            Same("datasets", new object[] { m.Datasets.Keys.ToArray(), m.AnalysisSource }, new object[] { e.Datasets.Keys.ToArray(), e.AnalysisSource });
            if (m.NodesElements.Count != e.NodesElements.Count || m.BeamElements.Count != e.BeamElements.Count || m.AreaElements.Count != e.AreaElements.Count) lines.Add("element counts");
            else
            {
                foreach (var n in m.NodesElements.Values) { var o = e.NodesElements[n.Id]; Same("node " + n.Id, new object[] { n.Guid, n.Position, n.Loads, n.Attributes, n.Assignments.Physical, n.Assignments.Restrains.Select(r => r.Restrain.Restrains).ToArray() }, new object[] { o.Guid, o.Position, o.Loads, o.Attributes, o.Assignments.Physical, o.Assignments.Restrains.Select(r => r.Restrain.Restrains).ToArray() }); }
                foreach (var b in m.BeamElements.Values) { var o = e.BeamElements[b.Id]; Same("beam " + b.Id, new object[] { b.Guid, b.Source, b.StartPoint, b.EndPoint, b.Loads, b.Attributes, b.Assignments.Physical, b.Assignments.Loads.ToArray(), b.Assignments.SectionAxes, b.BeamProperty }, new object[] { o.Guid, o.Source, o.StartPoint, o.EndPoint, o.Loads, o.Attributes, o.Assignments.Physical, o.Assignments.Loads.ToArray(), o.Assignments.SectionAxes, o.BeamProperty }); }
                foreach (var p in m.AreaElements.Values) { var o = e.AreaElements[p.Id]; Same("plate " + p.Id, new object[] { p.Guid, p.Loads, p.Attributes, p.Assignments.Physical }, new object[] { o.Guid, o.Loads, o.Attributes, o.Assignments.Physical }); }
            }
            return lines;
    }
}
#endif
