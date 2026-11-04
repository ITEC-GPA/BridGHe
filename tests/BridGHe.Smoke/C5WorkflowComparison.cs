#if GPC_PILOT
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Checkers.CompositeBridge;
using BridgeActionBehavior = GPC.Checkers.CompositeBridge.BridgeActionBehavior;
using BridgeWorkflowMethod = GPC.Checkers.CompositeBridge.BridgeWorkflowMethod;
using GPC.Converter;
using GPC.Geometry;
using GPC.Model.Results.Locations;
using GPC.Model.Results.Queries;

/// <summary>
/// F6A C5 (tests only): the GPC workflow executor (D3, <see cref="BridgeWorkflowExecutor"/>) against the checks of the same native run.
/// Inputs: the merged bridge model of Build Bridge Model (<see cref="MergedCapture"/>, BRIDGHE_MERGED_CAPTURE) and the gPC inputs of every
/// section result (<see cref="GpcSectionBench"/>, BRIDGHE_GPC_CAPTURE).
/// <para><c>--c5-compare &lt;merged.bgmodel&gt; &lt;inputs.jsonl.gz&gt; [--every N] [--out file.tsv]</c>: the merged model through the legacy
/// readers (<see cref="LegacyModelImport"/>, <see cref="LegacyResultsReader"/>, <see cref="LegacyWorkflowImport"/>) as a GPC model with its
/// results and workflow recipe; for every captured row an evaluation (beam, exact station, combination coefficients, the φ of the row,
/// the section input of the row without its phases, section axes as BridGHe's reference frame (−1, −2, 3), gross centroid) compiled by
/// <see cref="BridgeWorkflowExecutor.BuildInput(BridgeWorkflowDefinition, GPC.Model.Models.Model, IEnumerable{GPC.Model.Models.Model}, BridgeWorkflowEvaluation)"/>.
/// Compared: the phases (sums of the increments by kind, φ, ψL, reference and homogenisation: BridGHe sums the cases of a phase, D3 keeps
/// one increment per action) and the row (points, τ, final stage with its checks) of the recalculated captured input, whose hash must
/// reproduce the captured row, and of D3's input.</para>
/// <para><c>--c5-describe &lt;merged&gt;</c> prints the shape of the merged model.</para>
/// </summary>
internal static class C5WorkflowComparison
{
    private sealed class Outcome
    {
        public string Key, Error, PhaseDifference; public bool Reproduced, StrainMismatch; public double StationOffset;
        /// <summary>By path (array indices dropped): the largest differences and whether any value exceeds 1e-9 / 1e-6 (relative and absolute).</summary>
        public Dictionary<string, PathStat> Numbers = new(StringComparer.Ordinal);
        public List<(string Path, string Detail)> Structure = new();
    }

    private sealed class PathStat { public double Abs, Rel, X, Y; public bool Over9, Over6; }

    internal static int Compare(string[] args)
    {
        string Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        // --rhino: inside a started Rhino host, as the native runners (the host changes the last bits of some results).
        using var rhino = args.Contains("--rhino") ? new Rhino.Runtime.InProcess.RhinoCore(new[] { "/nosplash" }, Rhino.Runtime.InProcess.WindowStyle.Hidden) : null;
        string merged = Arg("--c5-compare"), capture = args.SkipWhile(a => a != "--c5-compare").Skip(2).FirstOrDefault()
            ?? throw new ArgumentException("--c5-compare <merged.bgmodel> <inputs.jsonl.gz>");
        int every = Arg("--every") is { } n ? int.Parse(n, CultureInfo.InvariantCulture) : 1; string output = Arg("--out");
        var culture = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var legacy = LegacyArchive.Read(File.ReadAllBytes(merged), int.MaxValue); // the merged model of example 50 is above the former limit
            string label = Path.GetFileName(merged), dataset = "C5 " + label;
            var owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "c5:" + label);
            var report = new LegacyImportReport();
            var gpc = LegacyModelImport.Import(legacy, owner, 0.001, report);
            var imported = LegacyResultsReader.Import(gpc, legacy, owner, dataset);
            if (imported.Report.Status != ImportStatus.Completed)
                throw new InvalidOperationException("C5ResultsNotImported: " + imported.Report.Status + " " + string.Join("; ", imported.Report.Diagnostics.Select(d => d.Code + " " + d.Message)));
            var workflow = LegacyWorkflowImport.Import(legacy, gpc); var sources = new[] { gpc };
            workflow.Validate(gpc); workflow.ValidateSources(gpc, sources);
            Console.WriteLine($"C5 {label}: {gpc.BeamElements.Count} beams, {gpc.LoadCases.Count} cases, {imported.Report.ImportedSamples} result samples ({imported.SkippedCombinationSamples} of combinations not stored), " +
                $"workflow {workflow.Method} {workflow.Phases.Length} phases {workflow.Phases.Sum(p => p.Actions.Length)} actions");
            foreach (var p in workflow.Phases.OrderBy(p => p.Order))
                Console.WriteLine($"  PHASE {p.Order} {p.Name} {p.State}: " + string.Join(", ", p.Actions.Select(a => $"{a.Key} {a.Behavior}{(a.Strain.HasValue ? " strain " + a.Strain.Value.ToString("R") : "")}")));

            var units = new BridgeUnits(BridgeUnits.Parse(legacy.Enum("Units")));
            var caseByName = gpc.LoadCases.Values.ToDictionary(c => c.Name, StringComparer.Ordinal);
            var caseName = gpc.LoadCases.Values.ToDictionary(c => c.Guid, c => c.Name);
            var factors = legacy.Field("LoadCombinations").Items.ToDictionary(c => c.String("Name"), c =>
            {
                var keys = c.Field("Keys").Items; var values = c.Field("Values").Items;
                return (IReadOnlyDictionary<Guid, double>)keys.Select((k, i) => (Guid: caseByName[k.String("Name")].Guid, Factor: values[i].AsDouble())).ToDictionary(x => x.Guid, x => x.Factor);
            }, StringComparer.Ordinal);
            var forceCases = workflow.Phases.SelectMany(p => p.Actions).Where(a => a.ForceCaseGuid.HasValue).Select(a => a.ForceCaseGuid.Value).Distinct().ToArray();
            var selections = (IReadOnlyDictionary<Guid, ResultSelection>)forceCases.ToDictionary(g => g, g => new ResultSelection { Dataset = dataset, Case = caseName[g] });
            var legacyBeams = legacy.Field("Elements").Items.Where(e => e.Is("BeamModel")).ToList();
            var gpcBeams = new Dictionary<string, (GPC.Model.Elements.BeamElement Beam, LegacyValue Legacy, double[] Stations)>(StringComparer.Ordinal);
            for (int k = 0; k < legacyBeams.Count; k++)
            {
                var beam = gpc.BeamElements[k + 1];
                var stations = ResultQueries.BeamSamples(beam, dataset, caseName[forceCases[0]]).Select(s => s.ParametricDistance).Distinct().OrderBy(x => x).ToArray();
                gpcBeams.Add(beam.Source.OriginalId, (beam, legacyBeams[k], stations));
            }
            // BridGHe reads the strain of a shrinkage increment from the slab strain loads of each beam; D3 from the action.
            var strainActions = workflow.Phases.SelectMany(p => p.Actions).Where(a => a.StrainCaseGuid.HasValue && a.Strain.HasValue).ToArray();
            double StrainLoad(LegacyValue l) { var s = l["Strain"]; return s.Text != null ? s.AsDouble() : s.Double("Strain"); }
            var beamStrainMismatch = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (id, item) in gpcBeams)
                foreach (var a in strainActions)
                {
                    double sum = item.Legacy.Field("Loads").Items.Where(l => l.Is("CompositeBeamSlabStrainModel") && l.Field("LoadCase").String("Name") == caseName[a.StrainCaseGuid.Value]).Sum(StrainLoad);
                    if (Math.Abs(sum - a.Strain.Value) > 1e-12 * Math.Max(1, Math.Abs(a.Strain.Value))) beamStrainMismatch.Add(id);
                }
            Console.WriteLine($"  slab strain of the beams different from the action strain: {beamStrainMismatch.Count} beams{(beamStrainMismatch.Count == 0 ? "" : " (" + string.Join(", ", beamStrainMismatch.Take(10)) + ")")}");

            var rows = new List<GpcSectionBench.Row>(); int read = 0;
            using (var gz = new GZipStream(File.OpenRead(capture), CompressionMode.Decompress))
            using (var reader = new StreamReader(gz, Encoding.UTF8))
                for (string line; (line = reader.ReadLine()) != null; read++)
                    if (read % every == 0) rows.Add(JsonSerializer.Deserialize<GpcSectionBench.Row>(line, ResultFingerprint.Json));
            Console.WriteLine($"  rows: {rows.Count} of {read} (every {every})");

            var behaviours = workflow.Phases.SelectMany(p => p.Actions.Select(a => (Name: p.Order + " · " + p.Name + " / " + a.Key, a.Behavior)))
                .ToDictionary(x => x.Name, x => x.Behavior, StringComparer.Ordinal);
            var activations = new HashSet<string>(workflow.Phases.Where(p => p.State != BridgeWorkflowSectionState.Steel).Select(p => p.Order + " · " + p.Name), StringComparer.Ordinal);
            var outcomes = new Outcome[rows.Count];
            Parallel.For(0, rows.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                var row = rows[i]; var o = outcomes[i] = new Outcome { Key = row.Key };
                try
                {
                    if (row.History) throw new NotSupportedException("C5HistoryRowNotCompared");
                    var input = GpcSectionBench.Input(row);
                    var baseline = HBridgeSection.Calculate(input);
                    var baseResult = new BridgeStressResult(baseline, row.Stage, row.Source, row.Assumptions);
                    o.Reproduced = ResultFingerprint.RowSha256(baseResult, ResultFingerprint.InputSha256(baseline)) == row.RowSha256;
                    if (row.Stage != baseline.Stages.Count - 1) o.Structure.Add(("stage", $"captured stage {row.Stage} of {baseline.Stages.Count}"));
                    var (beam, legacyBeam, stations) = gpcBeams[row.Source.BeamId];
                    double length = beam.Length / units.Length;
                    double station = stations.OrderBy(s => Math.Abs(s * length - row.Source.StationMm)).First();
                    o.StationOffset = Math.Abs(station * length - row.Source.StationMm);
                    o.StrainMismatch = beamStrainMismatch.Contains(row.Source.BeamId);
                    var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, legacyBeam.Double("Angle"));
                    var point = beam.StartPoint + (beam.EndPoint - beam.StartPoint) * station;
                    if (beam.Assignments.OffsetI != null) point += (beam.Assignments.OffsetAxes ?? beam.Assignments.SectionAxes).ToGlobal(beam.Assignments.OffsetI);
                    // The φ of the row: by the names of BridGHe's phases (cases path: "Infinite Time", ...; workflow path, with local states:
                    // "order · phase / action / FEM|strain", the behaviour of the action; an unloaded activation "order · phase" has the short-term φ).
                    double Phi(BridgeActionBehavior behaviour, params string[] names)
                    {
                        foreach (var phase in input.Phases)
                        {
                            if (names.Contains(phase.Name)) return phase.Phi;
                            string name = phase.Name.EndsWith(" / FEM", StringComparison.Ordinal) ? phase.Name[..^6] : phase.Name.EndsWith(" / strain", StringComparison.Ordinal) ? phase.Name[..^9] : null;
                            if (name != null && behaviours.TryGetValue(name, out var b) && b == behaviour) return phase.Phi;
                            if (behaviour == BridgeActionBehavior.ShortTerm && activations.Contains(phase.Name)) return phase.Phi;
                        }
                        return double.NaN;
                    }
                    double longTerm = Phi(BridgeActionBehavior.LongTerm, "Infinite Time"), shortTerm = Phi(BridgeActionBehavior.ShortTerm, "Instant Time"),
                        shrinkage = Phi(BridgeActionBehavior.Shrinkage, "Shrinkage FEM", "Strain Infinite");
                    var evaluation = new BridgeWorkflowEvaluation
                    {
                        BeamGuid = beam.Guid, PhysicalPropertyFingerprint = BridgeWorkflowDefinition.PropertyFingerprint(beam), Station = station, Side = SectionSide.Unspecified,
                        SectionInput = input with { Phases = Array.Empty<BridgePhase>() },
                        // BridGHe's reference frame: (-x, -y) of the principal axes (BridgeForceConvention, SuperElementCheckComponent).
                        SectionAxes = new CoordinateSystem(point, principal.V1 * -1, principal.V2 * -1, principal.V3),
                        LoadReference = BridgeLoadReference.GrossCentroid, Factors = factors[row.Source.Combination], Results = selections,
                        LongTermPhi = double.IsNaN(longTerm) ? 0 : longTerm, ShortTermPhi = double.IsNaN(shortTerm) ? 0 : shortTerm,
                        ShrinkagePhi = double.IsNaN(shrinkage) ? (double.IsNaN(longTerm) ? 0 : longTerm) : shrinkage,
                    };
                    var compiled = BridgeWorkflowExecutor.BuildInput(workflow, gpc, sources, evaluation);
                    o.PhaseDifference = PhaseDifference(input.Phases, compiled.Phases);
                    var d3 = BridgeWorkflowExecutor.Calculate(workflow, gpc, sources, evaluation);
                    var d3Result = new BridgeStressResult(d3, d3.Stages.Count - 1, row.Source, row.Assumptions);
                    Walk("", JsonNode.Parse(ResultFingerprint.RowPayload(baseResult, "")), JsonNode.Parse(ResultFingerprint.RowPayload(d3Result, "")), o);
                }
                catch (Exception e) { o.Error = e.GetType().Name + ": " + e.Message; }
            });

            int errors = outcomes.Count(o => o.Error != null), reproduced = outcomes.Count(o => o.Reproduced), phases = outcomes.Count(o => o.PhaseDifference != null);
            Console.WriteLine($"  captured rows reproduced (row hash): {reproduced}/{rows.Count}; errors: {errors}; phase sums different: {phases}; " +
                $"largest station offset {outcomes.Where(o => o.Error == null).Select(o => o.StationOffset).DefaultIfEmpty(0).Max():G3} mm; rows on beams with a different slab strain: {outcomes.Count(o => o.StrainMismatch)}");
            foreach (var g in outcomes.Where(o => o.Error != null).GroupBy(o => o.Error).OrderByDescending(g => g.Count()).Take(10))
                Console.WriteLine($"  ERROR x{g.Count()}: {g.Key} (first {g.First().Key.Replace('\t', ' ')})");
            foreach (var g in outcomes.Where(o => o.PhaseDifference != null).GroupBy(o => o.PhaseDifference.Split(':')[0]).OrderByDescending(g => g.Count()).Take(10))
                Console.WriteLine($"  PHASES x{g.Count()}: {g.First().PhaseDifference} (first {g.First().Key.Replace('\t', ' ')})");
            var numbers = outcomes.SelectMany(o => o.Numbers.Select(x => (o.Key, Path: x.Key, x.Value.Abs, x.Value.Rel, Detail: $"{x.Value.X:R} / {x.Value.Y:R}"))).ToArray();
            // Solver diagnostics (iterations and residuals of the final stage) are reported, not compared.
            static bool Diagnostic(string path) => path.EndsWith(".Iterations", StringComparison.Ordinal) || path.EndsWith("Residual", StringComparison.Ordinal);
            int differentRows = outcomes.Count(o => o.Numbers.Any(x => !Diagnostic(x.Key) && x.Value.Over9));
            // The effective sections (class 4) are iterative: D3's increments change the start of the iterations of the final stage, so its
            // totals agree within the convergence of the solver, not bit for bit. Pass: no total differs by more than 1e-6 (relative and absolute).
            int outsideRows = outcomes.Count(o => o.Numbers.Any(x => !Diagnostic(x.Key) && x.Value.Over6));
            int structureRows = outcomes.Count(o => o.Structure.Count != 0);
            double Worst(Func<string, bool> paths) => numbers.Where(x => paths(x.Path)).Select(x => x.Abs).DefaultIfEmpty(0).Max();
            Console.WriteLine($"  rows with a total different by more than 1e-9 (relative and absolute): {differentRows}/{rows.Count - errors}; by more than 1e-6: {outsideRows}; " +
                $"with a different shape: {structureRows}");
            Console.WriteLine($"  largest absolute differences: stresses {Worst(p => p is ".Points[]" or ".Tau[]" or ".Stage.Points[].Stress"):E2} MPa, utilisations and ratios " +
                $"{Worst(p => p.EndsWith("Utilization", StringComparison.Ordinal) || p.EndsWith("Ratio", StringComparison.Ordinal)):E2}");
            foreach (var g in numbers.GroupBy(x => x.Path).Select(g => g.OrderByDescending(x => x.Rel).First()).Where(x => x.Abs > 0).OrderByDescending(x => x.Rel).Take(25))
                Console.WriteLine($"    {g.Path}: worst relative {g.Rel:E2}, absolute {g.Abs:E2} ({g.Detail}; {g.Key.Replace('\t', ' ')})");
            foreach (var g in outcomes.SelectMany(o => o.Structure.Select(x => (o.Key, x.Path, x.Detail))).GroupBy(x => x.Path + " | " + x.Detail).OrderByDescending(g => g.Count()).Take(20))
                Console.WriteLine($"  STRUCTURE x{g.Count()}: {g.Key} (first {g.First().Key.Replace('\t', ' ')})");
            if (output != null)
                using (var tsv = new StreamWriter(output, false, new UTF8Encoding(false)) { NewLine = "\n" })
                {
                    tsv.WriteLine("key\treproduced\terror\tphases\tworstPath\tworstRel\tworstAbs");
                    foreach (var o in outcomes)
                    {
                        var worst = o.Numbers.OrderByDescending(x => x.Value.Rel).FirstOrDefault();
                        tsv.WriteLine(string.Join("\t", o.Key.Replace('\t', ' '), o.Reproduced ? 1 : 0, o.Error ?? "", o.PhaseDifference ?? "", worst.Key ?? "",
                            (worst.Value?.Rel ?? 0).ToString("R"), (worst.Value?.Abs ?? 0).ToString("R")));
                    }
                }
            bool pass = errors == 0 && reproduced == rows.Count && phases == 0 && outsideRows == 0 && structureRows == 0;
            Console.WriteLine(pass ? "C5 PASS" : "C5 DIFFERENCES");
            return pass ? 0 : 2;
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    /// <summary>Null when the sums of the increments agree by kind, φ, ψL, reference and homogenisation (zero sums ignored); else the first difference.</summary>
    private static string PhaseDifference(IReadOnlyList<BridgePhase> bridghe, IReadOnlyList<BridgePhase> d3)
    {
        static Dictionary<string, double[]> Sums(IEnumerable<BridgePhase> phases)
        {
            var sums = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var p in phases)
            {
                bool strain = p.Kind == BridgePhaseKind.Shrinkage;
                string key = $"{p.Kind}|phi {p.Phi:R}|psiL {p.PsiL:R}|{p.Reference}|{p.HomogenizationSource}|n {p.N:R}";
                if (!sums.TryGetValue(key, out var s)) sums.Add(key, s = new double[5]);
                s[0] += p.ForceKN; s[1] += p.MomentKNm; s[2] += p.ShearKN; s[3] += p.TorsionKNm; s[4] += p.ShrinkageMicrostrain;
            }
            return sums.Where(p => p.Value.Any(v => v != 0)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        }
        var a = Sums(bridghe); var b = Sums(d3);
        foreach (var key in a.Keys.Union(b.Keys))
        {
            if (!a.TryGetValue(key, out var x)) return "only in D3: " + key + " " + string.Join(" ", b[key].Select(v => v.ToString("G6")));
            if (!b.TryGetValue(key, out var y)) return "only in BridGHe: " + key + " " + string.Join(" ", x.Select(v => v.ToString("G6")));
            double scale = Math.Max(1e-9, x.Concat(y).Max(Math.Abs));
            for (int k = 0; k < 5; k++)
                if (Math.Abs(x[k] - y[k]) > 1e-9 * scale) return $"sums differ: {key} component {k} {x[k]:R} / {y[k]:R}";
        }
        return null;
    }

    /// <summary>Numbers compared by their absolute and relative difference; strings and shapes must agree, except names and warnings
    /// (D3 names its increments by phase and action, BridGHe by phase kind).</summary>
    private static void Walk(string path, JsonNode a, JsonNode b, Outcome o)
    {
        if (a == null || b == null) { if (a != null || b != null) o.Structure.Add((path, a == null ? "null in BridGHe" : "null in D3")); return; }
        switch (a)
        {
            case JsonObject oa when b is JsonObject ob:
                foreach (var key in oa.Select(p => p.Key).Union(ob.Select(p => p.Key)))
                {
                    // Contributions: the breakdown by increment (BridGHe: one per phase kind, D3: one per action) differs by design;
                    // the totals (points, τ, final stage stresses and checks) are compared.
                    if (key is "Name" or "Warnings" or "InputSha256" or "Contributions") continue;
                    if (!oa.ContainsKey(key) || !ob.ContainsKey(key)) { o.Structure.Add((path + "." + key, oa.ContainsKey(key) ? "missing in D3" : "missing in BridGHe")); continue; }
                    Walk(path + "." + key, oa[key], ob[key], o);
                }
                return;
            case JsonArray aa when b is JsonArray ab:
                if (aa.Count != ab.Count) { o.Structure.Add((path + "[]", $"{aa.Count} / {ab.Count} items")); return; }
                for (int i = 0; i < aa.Count; i++) Walk(path + "[]", aa[i], ab[i], o);
                return;
            case JsonValue va when b is JsonValue vb:
                if (va.TryGetValue(out double x) && vb.TryGetValue(out double y))
                {
                    double abs = Math.Abs(x - y), rel = abs / Math.Max(1e-12, Math.Max(Math.Abs(x), Math.Abs(y)));
                    if (abs > 0)
                    {
                        if (!o.Numbers.TryGetValue(path, out var stat)) o.Numbers.Add(path, stat = new PathStat());
                        if (rel > stat.Rel) { stat.Rel = rel; stat.X = x; stat.Y = y; }
                        stat.Abs = Math.Max(stat.Abs, abs); stat.Over9 |= rel > 1e-9 && abs > 1e-9; stat.Over6 |= rel > 1e-6 && abs > 1e-6;
                    }
                }
                else if (va.ToJsonString() != vb.ToJsonString()) o.Structure.Add((path, Clip(va.ToJsonString()) + " / " + Clip(vb.ToJsonString())));
                return;
            default: o.Structure.Add((path, "different kinds")); return;
        }
    }
    private static string Clip(string s) => s.Length > 80 ? s.Substring(0, 80) + "…" : s;

    internal static int Describe(string path)
    {
        var m = LegacyArchive.Read(File.ReadAllBytes(path), int.MaxValue);
        Console.WriteLine("MODEL " + m.Type + ": " + string.Join(", ", m.Fields.Keys));
        var elements = m.Field("Elements").Items;
        foreach (var g in elements.GroupBy(e => e.ShortType)) Console.WriteLine($"  {g.Key}: {g.Count()}");
        foreach (var name in new[] { "Beams", "LoadCases", "LoadCombinations" })
            if (m.Has(name)) Console.WriteLine($"{name}: {m[name].Items.Count} ({string.Join(", ", m[name].Items.GroupBy(i => i.ShortType).Select(g => g.Key + " " + g.Count()))})");
        if (m.Has("LoadCases"))
            foreach (var c in m["LoadCases"].Items.Take(400))
            {
                var stages = c["Stages"].IsNull ? "" : string.Join("; ", c["Stages"].Items.Select(s => string.Join(",", s.Fields.Where(f => !f.Value.IsCollection && f.Value.Text != null).Select(f => f.Key + "=" + f.Value.Text))));
                Console.WriteLine($"  CASE {c.String("Name")} [{c.ShortType}] stages: {stages}");
            }
        if (m.Has("LoadCombinations"))
            foreach (var c in m["LoadCombinations"].Items.Take(20))
                Console.WriteLine($"  COMBINATION {c.String("Name")}: {Describe(c["Values"], 2)}");
        if (m.Has("BridgeWorkflow"))
        {
            var w = m["BridgeWorkflow"]; Console.WriteLine("WORKFLOW " + w.Enum("Method"));
            foreach (var p in w["Phases"].Items)
            {
                Console.WriteLine($"  PHASE {p.Int("Order")} {p.String("Name")} {p.Enum("State")} day {p.String("Day")} actions {p["Actions"].Items.Count}");
                foreach (var a in p["Actions"].Items)
                    Console.WriteLine($"    ACTION {a.String("Key")} {a.Enum("Behavior")} source {a.String("SourceName")}/{a.String("SourceCase")} force {a.String("ForceCase")} strain {a.String("StrainCase")} {a.String("Strain")} beams {a["BeamIds"].Items.Count} excluded {a["ConcreteExcludedBeamIds"].Items.Count} local {a.String("HasLocalSectionStates")}");
            }
        }
        var beams = elements.Where(e => e.Is("BeamModel") || e.Is("CompositeBeamModel")).ToList();
        foreach (var b in beams.Take(3))
        {
            Console.WriteLine($"BEAM {b.ShortType}: {string.Join(", ", b.Fields.Keys)}");
            foreach (var r in b["Results"].Items.Take(2)) Console.WriteLine("  RESULT " + r.ShortType + ": " + string.Join(", ", r.Fields.Keys) + " case " + r["Case"].String("Name"));
            foreach (var l in b["Loads"].Items.Take(3)) Console.WriteLine("  LOAD " + l.ShortType + ": " + Describe(l, 2));
            foreach (var t in b["Attributes"].Items) Console.WriteLine("  ATTRIBUTE " + t.ShortType + ": " + Describe(t, 3));
        }
        return 0;
    }

    private static string Describe(LegacyValue v, int depth)
    {
        if (v.IsNull) return "null";
        if (v.IsCollection) return depth == 0 ? v.ToString() : "[" + string.Join("; ", v.Items.Take(6).Select(i => Describe(i, depth - 1))) + (v.Items.Count > 6 ? "; ..." : "") + "]";
        if (v.Text != null) return v.Text;
        return depth == 0 ? v.ShortType : v.ShortType + "{" + string.Join(", ", v.Fields.Take(8).Select(f => f.Key + "=" + Describe(f.Value, depth - 1))) + "}";
    }
}
#endif
