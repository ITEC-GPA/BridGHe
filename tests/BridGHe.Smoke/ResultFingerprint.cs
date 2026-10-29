using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BridGHe.Common.Checks;
using Grasshopper.Kernel;

/// <summary>
/// Opt-in numerical fingerprint of the gPC section results, for comparing library sets bit for bit.
/// Active only when BRIDGHE_RESULT_DUMP names a folder; it reads the solved document and never changes it.
/// </summary>
internal static class ResultFingerprint
{
    private static readonly List<object> marks = new();
    private static readonly JsonSerializerOptions json = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 64,
        // gPC materials receive Guid.NewGuid() at construction: identities, not content.
        Converters = { new IdentityConverter() }
    };
    private sealed class IdentityConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue("guid");
    }
    internal static string Folder => Environment.GetEnvironmentVariable("BRIDGHE_RESULT_DUMP") is { Length: > 0 } f ? Path.GetFullPath(f) : null;
    private static double Seconds => (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalSeconds;

    /// <summary>Phase boundary, in seconds since the process started.</summary>
    internal static void Mark(string label)
    {
        if (Folder == null) return;
        double s = Seconds; marks.Add(new { Label = label, Seconds = s });
        Console.WriteLine(FormattableString.Invariant($"TIMING {s:F3} s: {label}"));
    }

    /// <summary>Write rows, compressed full results and a summary for every section result of the solved document.</summary>
    internal static void Write(string label, GH_Document doc)
    {
        string folder = Folder; if (folder == null) return;
        Mark(label + ": fingerprint start");
        // Profiler times belong to the last solution: read them before serializing anything.
        var times = doc.Objects.OfType<GH_ActiveObject>().Where(o => o.ProcessorTime.TotalMilliseconds >= 1)
            .OrderByDescending(o => o.ProcessorTime).Select(o => new { Type = o.GetType().Name, o.NickName, Id = o.InstanceGuid, Milliseconds = o.ProcessorTime.TotalMilliseconds }).ToArray();
        var culture = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Directory.CreateDirectory(folder);
            var components = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name is "SuperElementCheckComponent" or "BridgeDetailCheckComponent")
                .OrderBy(c => c.GetType().Name, StringComparer.Ordinal).ThenBy(c => c.NickName, StringComparer.Ordinal).ThenBy(c => c.InstanceGuid).ToArray();
            var inputs = new ConditionalWeakTable<object, string>();
            var all = SHA256.Create(); var perComponent = new List<object>(); var statuses = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int rows = 0, errors = 0, pointsOver = 0, checksOver = 0; double maxStage = double.NegativeInfinity, maxRatio = double.NegativeInfinity; string maxStageKey = null, maxRatioKey = null;
            using var tsv = new StreamWriter(Path.Combine(folder, label + ".rows.tsv"), false, new UTF8Encoding(false)) { NewLine = "\n" };
            using var gz = new GZipStream(File.Create(Path.Combine(folder, label + ".rows.jsonl.gz")), CompressionLevel.Optimal);
            using var full = new StreamWriter(gz, new UTF8Encoding(false)) { NewLine = "\n" };
            tsv.WriteLine("kind\tcomponent\tnickname\tpath\tindex\tsource\tstage\tmaxUtilization\tpoint5SteelSigma\tpoint8SteelSigma\tchecks\tmaxCheckRatio\tinputSha256\trowSha256");
            foreach (var c in components)
            {
                var componentHash = SHA256.Create(); int count = 0, other = 0;
                for (int port = 0; port < c.Params.Output.Count; port++)
                {
                    var tree = c.Params.Output[port].VolatileData;
                    foreach (var path in tree.Paths)
                    {
                        var branch = tree.get_Branch(path);
                        for (int i = 0; i < branch.Count; i++)
                        {
                            object value = (branch[i] as Grasshopper.Kernel.Types.IGH_Goo)?.ScriptVariable() ?? branch[i];
                            // Check lists, forces and Tau b repeat data already in the section result (stage checks, input hash): count them only.
                            if (value is not BridgeStressResult r) { other++; continue; }
                            string key = "section\t" + c.GetType().Name + "\t" + c.NickName + "\t" + path + "\t" + i;
                            string payload, source = "", stage = "", inputSha = "";
                            double stageMax = double.NaN, p5 = double.NaN, p8 = double.NaN, ratio = double.NaN; int checks = 0;
                            try
                            {
                                {
                                    inputSha = inputs.GetValue(r.Analysis, a => Text(() => Hash(JsonSerializer.Serialize(new { r.Analysis.Method, r.Analysis.Scope, r.Analysis.Input, r.Analysis.Geometry, r.Analysis.Materials }, json))));
                                    source = r.Source?.Label ?? ""; stage = r.Stage.Name; stageMax = r.Stage.MaxUtilization; p5 = r.Point5_SteelSigma; p8 = r.Point8_SteelSigma;
                                    var local = BridgeGpc.Checks(r.Stage).ToArray(); checks = local.Length;
                                    foreach (var check in local)
                                    {
                                        string status = check.Name + " | " + check.Status; statuses[status] = statuses.TryGetValue(status, out int n) ? n + 1 : 1;
                                        if (check.Ratio is double q && double.IsFinite(q)) { if (q > 1) checksOver++; if (!(q <= ratio)) ratio = q; if (q > maxRatio) { maxRatio = q; maxRatioKey = key + " | " + source + " | " + check.Name; } }
                                    }
                                    pointsOver += r.Stage.Points.Count(p => p.Utilization > 1);
                                    if (stageMax > maxStage) { maxStage = stageMax; maxStageKey = key + " | " + source; }
                                    var points = new[] { r.Point1_ConcreteSigma, r.Point1_ConcreteEpsilon, r.Point2_RebarSigma, r.Point3_RebarSigma, r.Point4_ConcreteSigma,
                                        r.Point5_SteelSigma, r.Point6_SteelSigma, r.Point7_SteelSigma, r.Point8_SteelSigma };
                                    var tau = Try(() => new[] { r.Point6_SteelTau, r.Point7_SteelTau });
                                    try { payload = JsonSerializer.Serialize(new { Source = r.Source, r.Assumptions, InputSha256 = inputSha, Points = points, Tau = tau, Stage = r.Stage }, json); }
                                    catch (Exception e)
                                    {
                                        // Keep the comparable core when a gPC member cannot be serialized.
                                        errors++;
                                        payload = JsonSerializer.Serialize(new { Source = r.Source, r.Assumptions, InputSha256 = inputSha, Points = points, Tau = tau,
                                            Stage = new { r.Stage.Name, r.Stage.Iterations, r.Stage.Residual, r.Stage.Points, r.Stage.Warnings, Checks = local }, SerializationError = e.GetType().Name + ": " + e.Message }, json);
                                    }
                                }
                            }
                            catch (Exception e) { errors++; payload = "ERROR " + e.GetType().Name + ": " + e.Message; }
                            string row = Hash(payload); rows++; count++;
                            byte[] line = Encoding.UTF8.GetBytes(key + "\t" + row + "\n"); componentHash.TransformBlock(line, 0, line.Length, null, 0); all.TransformBlock(line, 0, line.Length, null, 0);
                            tsv.WriteLine(string.Join("\t", key, source, stage, R(stageMax), R(p5), R(p8), checks, R(ratio), inputSha, row));
                            full.WriteLine(JsonSerializer.Serialize(new { Key = key, RowSha256 = row }, json).TrimEnd('}') + ",\"Payload\":" + (payload.StartsWith("ERROR ", StringComparison.Ordinal) ? JsonSerializer.Serialize(payload) : payload) + "}");
                        }
                    }
                }
                componentHash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                perComponent.Add(new { Type = c.GetType().Name, c.NickName, Id = c.InstanceGuid, Items = count, OtherOutputItems = other, Sha256 = Convert.ToHexString(componentHash.Hash) });
            }
            all.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Where(a => a.GetName().Name is { } n && (n.StartsWith("GPC", StringComparison.Ordinal) || n.StartsWith("BridGHe", StringComparison.Ordinal) || n is "DelaunayMesh" or "MathNet.Numerics"))
                .OrderBy(a => a.GetName().Name, StringComparer.Ordinal)
                .Select(a => new { Name = a.GetName().Name, Version = a.GetName().Version?.ToString(), a.Location, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))) }).ToArray();
            Mark(label + ": fingerprint end");
            var summary = new
            {
                Label = label, Created = DateTime.Now.ToString("o"), Machine = Environment.MachineName, Processors = Environment.ProcessorCount,
                Rows = rows, SerializationErrors = errors, RowsSha256 = Convert.ToHexString(all.Hash), Components = perComponent,
                MaxStageUtilization = R(maxStage), MaxStageKey = maxStageKey, MaxCheckRatio = R(maxRatio), MaxCheckKey = maxRatioKey,
                StressPointsOverOne = pointsOver, LocalChecksOverOne = checksOver, LocalCheckStatuses = statuses,
                LoadedAssemblies = assemblies, ProcessorTimes = times, Marks = marks
            };
            // Component Ids identify the profiler rows: keep them in the summary.
            File.WriteAllText(Path.Combine(folder, label + ".summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals, WriteIndented = true }));
            Console.WriteLine($"FINGERPRINT {label}: {rows} rows, {errors} serialization errors, SHA-256 {Convert.ToHexString(all.Hash)}");
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static string R(double v) => double.IsNaN(v) ? "NaN" : v.ToString("R", CultureInfo.InvariantCulture);
    private static double[] Try(Func<double[]> f) { try { return f(); } catch (Exception) { return null; } }
    private static string Text(Func<string> f) { try { return f(); } catch (Exception e) { return "ERROR " + e.GetType().Name; } }
}
