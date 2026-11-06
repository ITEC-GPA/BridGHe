#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// R6.6 (P2U, coordinator decision of 6/11): the frozen native capture of the FEM values that the native parity checks compare
/// (<c>GpcSecForcesCheck.Require</c>, <c>RequireMomentDispatch</c>, <c>RequireGirders</c>, <c>GpcCompositeSectionChecks.RequireSuperElementCheck</c>,
/// <c>CompositeAnalysisPropertyChecks.Require</c>, <c>C5WorkflowComparison.RequireGpcChecks</c>), so that after R6.5 they compare the GPC values
/// with the capture instead of the FEM path.
/// <para>Switch: <c>BRIDGHE_NATIVE_CAPTURE</c> = <c>write:&lt;folder&gt;</c> or <c>compare:&lt;folder&gt;</c>. One file per native run,
/// <c>&lt;label&gt;.tsv</c> (the label of the checks: 50, 99), UTF-8 without BOM, LF, a header line <c>key\tvalue</c>, then one line per key in the
/// order of recording. A key names the check, the component and the datum (documented in <c>FORMATO.md</c> next to the capture); a value is the
/// canonical text of the FEM datum (doubles as the 16 hexadecimal digits of their bits, zero without sign where the check said so), or
/// <c>sha256:&lt;64 hex&gt;</c> of a longer canonical text. Write: the file is created new (never overwritten); every key is recorded once
/// (a key recorded again must have the same value) and the GPC value of the same key must equal the FEM one (the check of today). Compare:
/// the FEM value (while it exists) and the GPC value must equal the captured one; at the end of the process every captured key must have
/// been seen.</para>
/// </summary>
internal static class NativeParityCapture
{
    internal const string Variable = "BRIDGHE_NATIVE_CAPTURE";
    private static readonly object gate = new();
    private static readonly Dictionary<string, Session> sessions = new(StringComparer.Ordinal);
    private static bool? writing; private static string folder; private static bool hooked;

    /// <summary>True when a capture is written or compared in this process.</summary>
    internal static bool Active { get { Init(); return writing.HasValue; } }

    private static void Init()
    {
        if (folder != null || writing.HasValue) return;
        string setting = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(setting)) return;
        int colon = setting.IndexOf(':');
        string mode = colon < 0 ? "" : setting.Substring(0, colon);
        if (mode != "write" && mode != "compare") throw new ArgumentException(Variable + " must be write:<folder> or compare:<folder>");
        writing = mode == "write"; folder = setting.Substring(colon + 1);
    }

    /// <summary>The SHA-256 form of a long canonical text.</summary>
    internal static string Sha(string text) => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    internal static string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

    /// <summary>A canonical text as a value: itself when short and on one line (at most 120 characters, no tab), else its SHA-256 form.</summary>
    internal static string Value(string text) => text.Length <= 120 && text.IndexOfAny(new[] { '\t', '\n', '\r' }) < 0 ? text : Sha(text);

    /// <summary>One datum: its FEM value (null after R6.5) and its GPC value (null where the check has none: a FEM-only datum).</summary>
    internal static void Datum(string label, string key, string fem, string gpc)
    {
        if (!Active) return;
        if (key.Contains('\t') || key.Contains('\n') || (fem != null && (fem.Contains('\t') || fem.Contains('\n'))) || (gpc != null && (gpc.Contains('\t') || gpc.Contains('\n'))))
            throw new ArgumentException("NATIVE CAPTURE: tab or new line in a key or value: " + key);
        lock (gate) Open(label).Datum(key, fem, gpc);
    }

    private static Session Open(string label)
    {
        if (!sessions.TryGetValue(label, out var s))
        {
            sessions[label] = s = new Session(Path.Combine(folder, label + ".tsv"), writing.Value);
            if (!hooked) { hooked = true; AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Close(); } catch (Exception e) { Console.WriteLine(e.Message); } }; }
        }
        return s;
    }

    /// <summary>Closes the sessions and prints their counts (called at the end of a native run, where a captured key not seen fails the run;
    /// also at the exit of the process).</summary>
    internal static void Close()
    {
        int missing = 0;
        lock (gate)
        {
            foreach (var p in sessions) missing += p.Value.Close(p.Key);
            sessions.Clear();
        }
        if (missing != 0) throw new Exception($"NATIVE CAPTURE FAIL: {missing} captured keys not seen in this run");
    }

    private sealed class Session
    {
        private readonly bool write; private readonly string path;
        private readonly StreamWriter output; private readonly Dictionary<string, string> captured, recorded = new(StringComparer.Ordinal);
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        private int equal, femOnly; private bool closed;

        internal Session(string path, bool write)
        {
            this.path = path; this.write = write;
            if (write)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                output = new StreamWriter(new FileStream(path, FileMode.CreateNew), new UTF8Encoding(false)) { NewLine = "\n" };
                output.WriteLine("key\tvalue");
            }
            else
            {
                captured = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var line in File.ReadLines(path).Skip(1)) { int tab = line.IndexOf('\t'); captured.Add(line.Substring(0, tab), line.Substring(tab + 1)); }
            }
        }

        internal void Datum(string key, string fem, string gpc)
        {
            if (write)
            {
                if (fem == null) throw new InvalidOperationException("NATIVE CAPTURE: no FEM value to write for " + key);
                if (recorded.TryGetValue(key, out var before))
                {
                    if (before != fem) throw new Exception($"NATIVE CAPTURE FAIL: {key} recorded twice with different FEM values: '{before}' / '{fem}'");
                }
                else { recorded.Add(key, fem); output.WriteLine(key + "\t" + fem); }
                if (gpc != null && gpc != fem) throw new Exception($"NATIVE CAPTURE FAIL: {key}: GPC '{gpc}' / FEM '{fem}'");
                if (gpc == null) femOnly++; else equal++;
                return;
            }
            seen.Add(key);
            if (!captured.TryGetValue(key, out var value)) throw new Exception("NATIVE CAPTURE FAIL: not in the capture: " + key);
            if (fem != null && fem != value) throw new Exception($"NATIVE CAPTURE FAIL: {key}: FEM '{fem}' / captured '{value}'");
            if (gpc != null && gpc != value) throw new Exception($"NATIVE CAPTURE FAIL: {key}: GPC '{gpc}' / captured '{value}'");
            if (gpc == null) femOnly++; else equal++;
        }

        internal int Close(string label)
        {
            if (closed) return 0; closed = true;
            if (write)
            {
                output.Dispose();
                Console.WriteLine($"NATIVE CAPTURE WRITE {label}: {recorded.Count} keys ({equal} GPC values equal to the FEM ones, {femOnly} FEM-only data) in {path}");
                return 0;
            }
            int missing = captured.Keys.Count(k => !seen.Contains(k));
            Console.WriteLine($"NATIVE CAPTURE COMPARE {label}: {seen.Count} of {captured.Count} keys seen, {equal} GPC values equal to the captured ones, {femOnly} FEM-only data, {missing} missing" + (missing == 0 ? "" : " FAIL"));
            if (missing != 0) foreach (var k in captured.Keys.Where(k => !seen.Contains(k)).Take(20)) Console.WriteLine("  missing: " + k);
            return missing;
        }
    }
}
#endif
