#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// P2R (12/11, user decision U3, step 0): the two captures written once on the base of P2 (<c>d62ae3f</c>), before the cut, as the oracles that
/// replace the FEM-backed comparisons:
/// <list type="bullet">
/// <item><see cref="Replays"/>: the GPC-only replays of R1 (<see cref="GpcOnlyReplay"/>: every replayed instance of <c>gpc-only-elements</c>,
/// <c>-generators</c>, <c>-properties</c> and <c>-models</c>, definitions and in-memory cases): its messages, its outputs modulo GUIDs (the texts the
/// FEM-backed copy was compared with) and its raw outputs with the identities. One file per lane.</item>
/// <item><see cref="D3"/>: the section results of every Super Element Check of the phase examples 11–19 solved natively (R4b,
/// <see cref="NativePhaseExamples"/>): per result the stresses of points 5 and 8, the utilization of the stage and every check (name, status,
/// ratio), as the bits of the doubles. One file per definition.</item>
/// </list>
/// <para>Format: <c>&lt;file&gt;.tsv</c>, UTF-8 without BOM, LF, a header line <c>key\tvalue</c>, one line per key in the order of recording. Switch:
/// <c>BRIDGHE_P2R_REPLAY</c> / <c>BRIDGHE_P2R_D3</c> = <c>write:&lt;folder&gt;</c> (the file is created new, never overwritten),
/// <c>compare:&lt;folder&gt;</c> or <c>off</c>; unset: compare with the frozen folder (<see cref="Frozen"/>), as the gates run. Compare: every key of
/// the run must be in the capture with the same value, and every captured key of a closed file must have been seen. Nothing is ever rewritten.</para>
/// </summary>
internal sealed class P2rCapture
{
    internal static readonly P2rCapture Replays = new("BRIDGHE_P2R_REPLAY", @"C:\Users\g.pacini\Documents\Repository\gpc-f6-claude\P2B\results\r1-replay-capture-p2r-20261112");
    internal static readonly P2rCapture D3 = new("BRIDGHE_P2R_D3", @"C:\Users\g.pacini\Documents\Repository\gpc-f6-claude\P2T\results\r4b-d3-capture-p2r-20261112");

    private readonly string variable;
    internal readonly string Frozen;
    private readonly object gate = new();
    private readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private P2rCapture(string variable, string frozen) { this.variable = variable; Frozen = frozen; }

    /// <summary>The mode of this process: "write", "compare" or "off", and the folder.</summary>
    internal (string Mode, string Folder) Setting
    {
        get
        {
            string s = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(s)) return ("compare", Frozen);
            if (s == "off") return ("off", null);
            int colon = s.IndexOf(':');
            string mode = colon < 0 ? "" : s.Substring(0, colon);
            if (mode != "write" && mode != "compare") throw new ArgumentException(variable + " must be write:<folder>, compare:<folder> or off");
            return (mode, s.Substring(colon + 1));
        }
    }

    /// <summary>A short SHA-256 (16 hexadecimal digits) of a text.</summary>
    internal static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? "<null>"))).Substring(0, 16);

    internal static string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

    /// <summary>Records one datum of a file (write) or checks it (compare). A key met again in the same file gets the suffix <c>#n</c> (its
    /// occurrence in the order of the run), so the instances of a component with the same name and nickname in a definition stay apart.</summary>
    internal void Datum(string file, string key, string value)
    {
        var (mode, folder) = Setting;
        if (mode == "off") return;
        if (key.IndexOfAny(new[] { '\t', '\n', '\r' }) >= 0) key = key.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        if (value.IndexOfAny(new[] { '\t', '\n', '\r' }) >= 0) throw new ArgumentException("P2R CAPTURE: tab or new line in a value of " + key);
        lock (gate)
        {
            if (!sessions.TryGetValue(file, out var s)) sessions[file] = s = new Session(Path.Combine(folder, file + ".tsv"), mode == "write");
            s.Datum(key, value);
        }
    }

    /// <summary>Closes the file: the counts (printed) and the differences, as failures of the caller (empty when it holds).</summary>
    internal List<string> Close(string file)
    {
        lock (gate)
        {
            if (!sessions.Remove(file, out var s)) return Setting.Mode == "off" ? new List<string>() : new List<string> { $"P2R CAPTURE {file}: nothing recorded" };
            return s.Close(file);
        }
    }

    private sealed class Session
    {
        private readonly string path; private readonly bool write;
        private readonly StreamWriter output; private readonly Dictionary<string, string> captured;
        private readonly Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        private readonly List<string> differences = new();
        private int recorded, equal;

        internal Session(string path, bool write)
        {
            this.path = path; this.write = write;
            if (write)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                // Flushed at every line: a run that stops halfway keeps what it recorded (the file is then moved aside, never completed).
                output = new StreamWriter(new FileStream(path, FileMode.CreateNew), new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
                output.WriteLine("key\tvalue");
            }
            else
            {
                if (!File.Exists(path)) throw new FileNotFoundException("P2R CAPTURE: not found (written once in step 0 of P2R): " + path);
                captured = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var line in File.ReadLines(path).Skip(1)) { int tab = line.IndexOf('\t'); captured.Add(line.Substring(0, tab), line.Substring(tab + 1)); }
            }
        }

        internal void Datum(string key, string value)
        {
            int n = occurrences[key] = occurrences.GetValueOrDefault(key) + 1;
            if (n > 1) key += "#" + n;
            recorded++;
            if (write) { output.WriteLine(key + "\t" + value); return; }
            seen.Add(key);
            if (!captured.TryGetValue(key, out var before)) differences.Add($"not in the capture: {key} = {value}");
            else if (before != value) differences.Add($"{key}: captured {before} / now {value}");
            else equal++;
        }

        internal List<string> Close(string file)
        {
            if (write)
            {
                output.Dispose();
                Console.WriteLine($"P2R CAPTURE WRITE {file}: {recorded} keys in {path}");
                return new List<string>();
            }
            var missing = captured.Keys.Where(k => !seen.Contains(k)).ToList();
            Console.WriteLine($"P2R CAPTURE COMPARE {file}: {seen.Count} of {captured.Count} keys seen, {equal} equal, {differences.Count} different or new, {missing.Count} missing"
                + (differences.Count + missing.Count == 0 ? "" : " FAIL") + " (" + path + ")");
            return differences.Concat(missing.Select(k => "captured, not seen: " + k)).Select(d => "P2R CAPTURE " + file + ": " + d).ToList();
        }
    }
}
#endif
