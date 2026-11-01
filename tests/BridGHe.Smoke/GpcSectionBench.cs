using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BridGHe.Common.Checks;
using GPC.Checkers.CompositeBridge;
using GPC.Checkers.CompositeBridge.History;
using GPC.Model.Materials;
using Grasshopper.Kernel;

/// <summary>
/// Repeatable bench of the gPC section calculations (HBridgeSection.Calculate) of a solved document, without Straus and Grasshopper.
/// With BRIDGHE_GPC_CAPTURE=&lt;folder&gt; the native runners write, for every section result of Super Element Check and Bridge Detail Check,
/// the gPC input, the stage, the source and the row hash of <see cref="ResultFingerprint"/>. <c>--gpc-bench &lt;capture&gt;</c> recalculates
/// all the rows or a regular subset, times them and compares every row hash with the captured one, so a gPC set can be timed and
/// checked bit for bit in minutes. Model materials are rebuilt from their constructors; the input hash of the row proves the rebuild.
/// </summary>
internal static class GpcSectionBench
{
    internal static string Folder => Environment.GetEnvironmentVariable("BRIDGHE_GPC_CAPTURE") is { Length: > 0 } f ? Path.GetFullPath(f) : null;

    private sealed record ConcreteData(string Type, string Name, double Fck, ConcreteMaterial.CompressionStressStrainDiagrams Diagram,
        double Ni, double Density, double Alpha, ConcreteMaterial.CementTypes Cement);
    private sealed record SteelData(string Type, string Name, double E, double Fyk, double Fu, double StrainU, SteelMaterial.StressStrainCurveType Curve,
        SteelMaterial.SteelTypes SteelType, double Ni, double Density, double Alpha);
    private sealed record MaterialData(ConcreteData Concrete, SteelData Steel, SteelData Rebar);
    private sealed record Row(string Key, string Component, string NickName, int Stage, bool History, BridgeResultSource Source, string[] Assumptions,
        string InputSha256, string RowSha256, MaterialData Materials, JsonNode Input);

    private static ConcreteData Describe(ConcreteMaterialEN1992 c) =>
        new(c.GetType().FullName, c.Name, c.Fck, c.CompressionStressStrainDiagram, c.Ni, c.Density, c.AlfaThermalExpansion, c.CementType);
    private static SteelData Describe(SteelMaterial s) =>
        new(s.GetType().FullName, s.Name, s.ElasticModulusTension, s.Fyk, s.Fu, s.StrainUTension, s.StressStrainCurve, s.SteelType, s.Ni, s.Density, s.AlfaThermalExpansion);
    // One object per material, as in the native runs (the sections of a beam share their materials): equal descriptors give the same object,
    // so the batches can group the combinations of a station
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<object, object> Materials = new();
    private static ConcreteMaterialEN1992 Build(ConcreteData c) => (ConcreteMaterialEN1992)Materials.GetOrAdd(c, _ => NewConcrete(c));
    private static SteelMaterial Build(SteelData s) => (SteelMaterial)Materials.GetOrAdd(s, _ => NewSteel(s));
    private static ConcreteMaterialEN1992 NewConcrete(ConcreteData c) =>
        c.Type == typeof(ConcreteMaterialEN1992).FullName ? new ConcreteMaterialEN1992(c.Name, c.Fck, c.Diagram, c.Ni, c.Density, c.Alpha, c.Cement)
            : throw new NotSupportedException("Concrete type " + c.Type);
    private static SteelMaterial NewSteel(SteelData s) => s.Type == typeof(SteelMaterialEN1992).FullName
        ? new SteelMaterialEN1992(s.Name, s.E, s.Fyk, s.Fu, s.StrainU, s.Curve, s.SteelType, s.Ni, s.Density, s.Alpha)
        : s.Type == typeof(SteelMaterial).FullName ? new SteelMaterial(s.Name, s.E, s.Fyk, s.Fu, s.StrainU, s.Curve, s.SteelType, s.Ni, s.Density, s.Alpha)
        : throw new NotSupportedException("Steel type " + s.Type);

    /// <summary>The section results of the solved document, in the order and with the keys of <see cref="ResultFingerprint"/>.</summary>
    internal static IEnumerable<(string Key, GH_Component Component, BridgeStressResult Result)> Results(GH_Document doc)
    {
        var components = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name is "SuperElementCheckComponent" or "BridgeDetailCheckComponent")
            .OrderBy(c => c.GetType().Name, StringComparer.Ordinal).ThenBy(c => c.NickName, StringComparer.Ordinal).ThenBy(c => c.InstanceGuid).ToArray();
        foreach (var c in components)
            for (int port = 0; port < c.Params.Output.Count; port++)
            {
                var tree = c.Params.Output[port].VolatileData;
                foreach (var path in tree.Paths)
                {
                    var branch = tree.get_Branch(path);
                    for (int i = 0; i < branch.Count; i++)
                        if (((branch[i] as Grasshopper.Kernel.Types.IGH_Goo)?.ScriptVariable() ?? branch[i]) is BridgeStressResult r)
                            yield return ("section\t" + c.GetType().Name + "\t" + c.NickName + "\t" + path + "\t" + i, c, r);
                }
            }
    }

    /// <summary>Write the inputs of every section result (opt-in, BRIDGHE_GPC_CAPTURE); it reads the solved document and never changes it.</summary>
    internal static void Capture(string label, GH_Document doc)
    {
        string folder = Folder; if (folder == null) return;
        var culture = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Directory.CreateDirectory(folder);
            var started = Stopwatch.StartNew(); int rows = 0;
            var inputs = new Dictionary<HBridgeAnalysisResult, string>(ReferenceEqualityComparer.Instance);
            using var gz = new GZipStream(File.Create(Path.Combine(folder, label + ".inputs.jsonl.gz")), CompressionLevel.Optimal);
            using var writer = new StreamWriter(gz, new UTF8Encoding(false)) { NewLine = "\n" };
            foreach (var (key, c, r) in Results(doc))
            {
                var a = r.Analysis;
                if (!inputs.TryGetValue(a, out string inputSha)) inputs[a] = inputSha = ResultFingerprint.InputSha256(a);
                int stage = a.Stages.IndexOf(r.Stage);
                var m = a.Input.Materials;
                var row = new Row(key, c.GetType().Name, c.NickName, stage, r.Stage.GetHistory() != null, r.Source, r.Assumptions.ToArray(), inputSha,
                    ResultFingerprint.RowSha256(r, inputSha), new MaterialData(Describe(m.Concrete), Describe(m.Steel), Describe(m.Rebar)),
                    JsonSerializer.SerializeToNode(a.Input with { Materials = null }, ResultFingerprint.Json));
                writer.WriteLine(JsonSerializer.Serialize(row, ResultFingerprint.Json)); rows++;
            }
            Console.WriteLine(FormattableString.Invariant($"GPC CAPTURE {label}: {rows} rows, {inputs.Count} analyses, {started.Elapsed.TotalSeconds:F1} s"));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    /// <summary>
    /// --gpc-bench &lt;capture.inputs.jsonl.gz&gt; [--every N] [--offset K] [--component text] [--repeat R] [--out file.tsv] [--alloc-types]:
    /// recalculates every N-th row (all by default) R times, then prints times, allocations (with --alloc-types also by type) and the
    /// comparison of the row hashes; --stream also prints the collections and the pauses of the garbage collector.
    /// Exit code 0 only when every recalculated row has the captured hash.
    /// </summary>
    internal static int Run(string[] args)
    {
        string Arg(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
        int Int(string name, int fallback) => Arg(name) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
        string file = Arg("--gpc-bench") ?? throw new ArgumentException("--gpc-bench <capture.inputs.jsonl.gz>");
        int every = Int("--every", 1), offset = Int("--offset", 0), repeat = Int("--repeat", 1);
        // --station-every N: the rows of every N-th station (all its combinations), for the batches
        int stationEvery = Int("--station-every", 0), stationIndex = -1; string lastStation = null;
        string component = Arg("--component"), output = Arg("--out"), dump = Arg("--dump");
        // --rhino: the gPC calculations inside a started Rhino host, as in the native runners.
        using var rhino = args.Contains("--rhino") ? new Rhino.Runtime.InProcess.RhinoCore(new[] { "/nosplash" }, Rhino.Runtime.InProcess.WindowStyle.Hidden) : null;
        var culture = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name.StartsWith("GPC", StringComparison.Ordinal) && !a.IsDynamic).OrderBy(a => a.GetName().Name))
                Console.WriteLine($"GPC {a.GetName().Name} {a.GetName().Version} {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location)))[..12]}");
            if (args.Contains("--stream")) return Stream(file, every, offset, component, args.Contains("--parallel"), args.Contains("--batch"));
            var rows = new List<Row>(); int read = 0;
            using (var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress))
            using (var reader = new StreamReader(gz, Encoding.UTF8))
                for (string line; (line = reader.ReadLine()) != null; read++)
                {
                    if (stationEvery == 0 && read % every != offset % every) continue;
                    var row = JsonSerializer.Deserialize<Row>(line, ResultFingerprint.Json);
                    if (stationEvery > 0)
                    {
                        if (Station(row) != lastStation) { stationIndex++; lastStation = Station(row); }
                        if (stationIndex % stationEvery != offset % stationEvery) continue;
                    }
                    if (component == null || (row.Component + "\t" + row.NickName).Contains(component, StringComparison.Ordinal)) rows.Add(row);
                }
            var inputs = rows.Select(r => r.Input.Deserialize<HBridgeInput>(ResultFingerprint.Json) with
                { Materials = new BridgeMaterialSet(Build(r.Materials.Concrete), Build(r.Materials.Steel), Build(r.Materials.Rebar)) }).ToArray();
            Console.WriteLine($"GPC BENCH {Path.GetFileName(file)}: {rows.Count} of {read} rows (every {every}, offset {offset}{(component == null ? "" : ", component " + component)}), {repeat} pass(es)");
            var analyses = new HBridgeAnalysisResult[rows.Count];
            var times = new double[repeat][];
            using var types = args.Contains("--alloc-types") ? new AllocationTypes() : null;
            for (int pass = 0; pass < repeat; pass++)
            {
                times[pass] = new double[rows.Count];
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                types?.Clear();
                long allocated = GC.GetTotalAllocatedBytes(true); int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                var cpu = Process.GetCurrentProcess().TotalProcessorTime; var total = Stopwatch.StartNew();
                // --batch: one gPC call for the checks of a station; its time is shared by its rows
                foreach (var station in Stations(rows, args.Contains("--batch")))
                {
                    long t0 = Stopwatch.GetTimestamp();
                    var computed = Calculate(station.Select(i => rows[i]).ToArray(), station.Select(i => inputs[i]).ToArray());
                    double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds / station.Length;
                    for (int k = 0; k < station.Length; k++) { times[pass][station[k]] = ms; if (pass == 0) analyses[station[k]] = computed[k]; }
                }
                double seconds = total.Elapsed.TotalSeconds, cpuSeconds = (Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalSeconds;
                var sorted = times[pass].OrderBy(t => t).ToArray();
                double Quantile(double q) => sorted.Length == 0 ? double.NaN : sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
                // The current culture is invariant here.
                Console.WriteLine($"PASS {pass + 1}: {seconds:F2} s wall, {cpuSeconds:F2} s CPU, {seconds * 1000 / Math.Max(1, rows.Count):F2} ms/row (median {Quantile(.5):F2}, p90 {Quantile(.9):F2}, max {Quantile(1):F2}), " +
                    $"{(GC.GetTotalAllocatedBytes(true) - allocated) / 1048576.0 / Math.Max(1, rows.Count):F2} MB/row allocated, GC {GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2}");
                foreach (var group in rows.Select((r, i) => (r, i)).GroupBy(x => x.r.Component + " | " + x.r.NickName).OrderBy(g => g.Key, StringComparer.Ordinal))
                    Console.WriteLine($"  {group.Key}: {group.Count()} rows, {group.Sum(x => times[pass][x.i]) / 1000:F2} s, {group.Average(x => times[pass][x.i]):F2} ms/row");
                types?.Print(rows.Count);
            }
            int same = 0, inputDiff = 0, resultDiff = 0; var first = new List<string>();
            using var tsv = output == null ? null : new StreamWriter(output, false, new UTF8Encoding(false)) { NewLine = "\n" };
            tsv?.WriteLine("key\tinputSha256\trowSha256\tcapturedRowSha256\tsame\t" + string.Join("\t", Enumerable.Range(1, repeat).Select(p => "ms" + p)));
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var result = new BridgeStressResult(analyses[i], r.Stage, r.Source, r.Assumptions);
                string inputSha = ResultFingerprint.InputSha256(analyses[i]), payload = ResultFingerprint.RowPayload(result, inputSha);
                string rowSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
                bool ok = rowSha == r.RowSha256;
                // The recalculated payload of the first differences, to compare with the rows of the fingerprint.
                if (!ok && dump != null && first.Count < 10) { Directory.CreateDirectory(dump); File.WriteAllText(Path.Combine(dump, $"diff{first.Count}.json"), payload); }
                if (ok) same++; else { if (inputSha != r.InputSha256) inputDiff++; else resultDiff++; if (first.Count < 10) first.Add(r.Key + (inputSha != r.InputSha256 ? " (input)" : "")); }
                tsv?.WriteLine(string.Join("\t", r.Key, inputSha, rowSha, r.RowSha256, ok ? "1" : "0", string.Join("\t", times.Select(t => t[i].ToString("F3")))));
            }
            Console.WriteLine($"COMPARE: {same}/{rows.Count} rows identical, {inputDiff} with a different input hash, {resultDiff} with a different result");
            foreach (string key in first) Console.WriteLine("  DIFF " + key.Replace('\t', ' '));
            return same == rows.Count ? 0 : 2;
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    /// <summary>--alloc-types: the allocated bytes by type, sampled by the allocation ticks of the runtime (one about every 100 KB)</summary>
    private sealed class AllocationTypes : System.Diagnostics.Tracing.EventListener
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> bytes = new(StringComparer.Ordinal);
        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)1);
        }
        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
        {
            if (e.EventName == null || !e.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal) || e.Payload == null) return;
            int type = e.PayloadNames.IndexOf("TypeName"), amount = e.PayloadNames.IndexOf("AllocationAmount64");
            if (type < 0 || amount < 0) return;
            bytes.AddOrUpdate(e.Payload[type] as string ?? "?", _ => (long)Convert.ToUInt64(e.Payload[amount]), (_, b) => b + (long)Convert.ToUInt64(e.Payload[amount]));
        }
        internal void Clear() => bytes.Clear();
        internal void Print(int rows)
        {
            long total = bytes.Values.Sum();
            Console.WriteLine($"  ALLOCATIONS BY TYPE (sampled {total / 1048576.0:F0} MB):");
            foreach (var (name, b) in bytes.OrderByDescending(p => p.Value).Take(40).Select(p => (p.Key, p.Value)))
                Console.WriteLine($"    {100.0 * b / Math.Max(1, total),5:F1}%  {b / 1024.0 / Math.Max(1, rows),9:F1} KB/row  {name}");
        }
    }

    private static HBridgeInput Input(Row r) => r.Input.Deserialize<HBridgeInput>(ResultFingerprint.Json) with
        { Materials = new BridgeMaterialSet(Build(r.Materials.Concrete), Build(r.Materials.Steel), Build(r.Materials.Rebar)) };

    /// <summary>The station of a row: component, beam and station (the rows of a station are consecutive in the captures)</summary>
    private static string Station(Row r) => r.Component + "|" + r.NickName + "|" + r.Source?.BeamId + "|" + r.Source?.StationMm.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>The rows calculated together: the consecutive rows of a station with --batch (not the histories), else one by one</summary>
    private static List<int[]> Stations(IReadOnlyList<Row> rows, bool batch)
    {
        var stations = new List<int[]>(); var current = new List<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (current.Count > 0 && (!batch || rows[i].History || rows[current[0]].History || Station(rows[i]) != Station(rows[current[0]]))) { stations.Add(current.ToArray()); current.Clear(); }
            current.Add(i);
        }
        if (current.Count > 0) stations.Add(current.ToArray());
        return stations;
    }

    /// <summary>The analyses of the rows of a station: one gPC batch call for several rows; an error of a row fails the bench</summary>
    private static HBridgeAnalysisResult[] Calculate(Row[] rows, HBridgeInput[] inputs)
    {
        if (rows.Length == 1) return new[] { Calculate(rows[0], inputs[0]) };
        return HBridgeSection.Calculate(inputs).Select((r, k) => r.Analysis ?? throw new InvalidOperationException(rows[k].Key + ": " + r.Error!.Message, r.Error)).ToArray();
    }
    private static HBridgeAnalysisResult Calculate(Row r, HBridgeInput input) =>
        r.History ? HBridgeHistoryResults.Calculate(input, new HBridgeHistoryOptions { MaterialMode = HistoryMaterialMode.Linear }) : HBridgeSection.Calculate(input);

    /// <summary>--stream [--parallel]: one pass over the whole capture in blocks, each row compared as soon as it is calculated (bounded memory)</summary>
    private static int Stream(string file, int every, int offset, string component, bool parallel, bool batch)
    {
        int read = 0, rows = 0, same = 0, inputDiff = 0, resultDiff = 0; double seconds = 0; var first = new List<string>();
        var block = new List<Row>();
        // the garbage collector of the whole run (reading included): collections, pauses and allocated memory
        int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
        var pause = GC.GetTotalPauseDuration(); long allocated = GC.GetTotalAllocatedBytes(false); var total = Stopwatch.StartNew();
        void Flush()
        {
            var ok = new int[block.Count]; var input = new bool[block.Count]; var watch = Stopwatch.StartNew();
            var stations = Stations(block, batch);
            void One(int s)
            {
                var station = stations[s];
                var computed = Calculate(station.Select(i => block[i]).ToArray(), station.Select(i => Input(block[i])).ToArray());
                for (int k = 0; k < station.Length; k++)
                {
                    int i = station[k]; var r = block[i]; var analysis = computed[k];
                    string inputSha = ResultFingerprint.InputSha256(analysis);
                    ok[i] = ResultFingerprint.RowSha256(new BridgeStressResult(analysis, r.Stage, r.Source, r.Assumptions), inputSha) == r.RowSha256 ? 1 : 0;
                    input[i] = inputSha == r.InputSha256;
                }
            }
            if (parallel) System.Threading.Tasks.Parallel.For(0, stations.Count, One); else for (int s = 0; s < stations.Count; s++) One(s);
            seconds += watch.Elapsed.TotalSeconds;
            for (int i = 0; i < block.Count; i++)
            {
                rows++;
                if (ok[i] == 1) { same++; continue; }
                if (input[i]) resultDiff++; else inputDiff++;
                if (first.Count < 10) first.Add(block[i].Key + (input[i] ? "" : " (input)"));
            }
            Console.WriteLine($"  {rows} rows, {same} identical, {seconds:F1} s");
            block.Clear();
        }
        using (var gz = new GZipStream(File.OpenRead(file), CompressionMode.Decompress))
        using (var reader = new StreamReader(gz, Encoding.UTF8))
            for (string line; (line = reader.ReadLine()) != null; read++)
            {
                if (read % every != offset % every) continue;
                var row = JsonSerializer.Deserialize<Row>(line, ResultFingerprint.Json);
                if (component != null && !(row.Component + "\t" + row.NickName).Contains(component, StringComparison.Ordinal)) continue;
                // blocks end between stations, so that a batch holds all the checks of its station
                if (block.Count >= 2000 && Station(row) != Station(block[block.Count - 1])) Flush();
                block.Add(row);
            }
        if (block.Count > 0) Flush();
        Console.WriteLine($"GPC BENCH STREAM {Path.GetFileName(file)}: {rows} of {read} rows{(parallel ? ", parallel" : "")}, {seconds:F1} s");
        Console.WriteLine($"GC: {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")}, {GC.CollectionCount(0) - g0}/{GC.CollectionCount(1) - g1}/{GC.CollectionCount(2) - g2} collections, " +
            $"pauses {(GC.GetTotalPauseDuration() - pause).TotalSeconds:F1} s of {total.Elapsed.TotalSeconds:F1} s, {(GC.GetTotalAllocatedBytes(false) - allocated) / 1073741824.0:F1} GB allocated");
        Console.WriteLine($"COMPARE: {same}/{rows} rows identical, {inputDiff} with a different input hash, {resultDiff} with a different result");
        foreach (string key in first) Console.WriteLine("  DIFF " + key.Replace('\t', ' '));
        return same == rows ? 0 : 2;
    }
}
