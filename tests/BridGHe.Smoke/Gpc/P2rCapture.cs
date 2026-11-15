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

    /// <summary>
    /// The declared rules of the replay capture (P2R, 12/11; found by the first compare in another process, p2rz1): a captured value is
    /// <c>lines messages outputs raw</c> (<see cref="GpcOnlyReplay"/>). Not a tolerance: each rule names the field it does not compare, the
    /// others must be equal.
    /// <list type="bullet">
    /// <item><b>messages «*»</b>: the messages of the replay hold the warning of the plug-in build check (<c>PluginBuildCheck</c>, «Bin non
    /// aggiornato…»: the libraries of the bin against lib/GPC and those loaded first, an environmental text whose list depends on what the
    /// process loaded before); the line count, the outputs and the raw outputs are compared. P2CG (13/11/2026, RF-01): «*» is followed by the
    /// digest of the other messages, compared with the capture; the capture itself holds the warning of its bin in the digest of 13 keys (7
    /// instances of Creep And Shrinkage Concrete in gpc-only-properties, 6 in gpc-only-models): 9 with the warning alone
    /// (<see cref="WarningAloneAtCapture"/>: no other message now), 4 with other messages. F8B (14/11/2026): the rule holds for a run without the
    /// warning (the plain build from lib/GPC). P2CI (14/11/2026, user decision 2a): the 4 keys with other messages are no longer declared by name:
    /// their lines are captured again without the warning (patch for the user's OK), so their messages are compared as every other key's.</item>
    /// <item><b>raw of the in-memory Bridge Phase Model and Bridge Construction Sequence cases</b> (gpc-only-models; P2CG, RF-03: the closed list of
    /// 18 keys <see cref="PhaseIdentityKeys"/>, before every captured key of the two prefixes): their input
    /// elements are built for the case in each process (GH types given by their drafts without a saved GH GUID), so the identities of the
    /// outputs change from one process to the next; within a process two copies give the same identities (checked), and the definitions
    /// (saved GUIDs) are compared in full.</item>
    /// </list>
    /// </summary>
    internal static string Declared(string file, string key, string captured, string now)
    {
        var c = captured.Split(' '); var n = now.Split(' ');
        if (c.Length != 4 || n.Length != 4) return null;
        // P2CG (13/11/2026, review of ChatGPT tasks 53 CF-02 and 64 RF-01): «*» marks a run with the environmental warning, followed by the digest
        // of its other messages, which are compared (before: «*» alone, no message compared, for any key).
        if (n[1].StartsWith("*", StringComparison.Ordinal) && c[0] == n[0] && c[2] == n[2] && c[3] == n[3])
        {
            string others = n[1].Substring(1);
            if (others == c[1]) return "messages with the plug-in build-check warning (environmental): the other messages compared, equal";
            if (c[1] == WarningAloneAtCapture && others == Sha(string.Join("\n", Array.Empty<string>())))
                return "messages: the capture holds the build-check warning alone (" + WarningAloneAtCapture + "), the run no other message";
            return null;
        }
        // F8B (14/11/2026): the plain build (a bin built from lib/GPC, the set its manifest describes) shows no build-check warning, while the
        // capture holds the warning of its bin in the 9 keys of the warning alone: the same declaration from a run without «*», whose messages
        // are then all compared (no environmental message to leave out). With the warning alone at capture, the run must have no message at all.
        // P2CI (14/11/2026): the declaration by name of the 4 keys with other messages is removed (their lines captured again without it).
        if (!n[1].StartsWith("*", StringComparison.Ordinal) && c[0] == n[0] && c[2] == n[2] && c[3] == n[3])
        {
            if (c[1] == WarningAloneAtCapture && n[1] == Sha(string.Join("\n", Array.Empty<string>())))
                return "messages: the capture holds the build-check warning alone (" + WarningAloneAtCapture + "), the run no other message";
        }
        // P2CG (13/11/2026, review of ChatGPT task 64, RF-03): the closed list of 18 keys (before: every captured key of the two prefixes).
        if (file == "gpc-only-models" && PhaseIdentityKeys.Contains(key) && c[0] == n[0] && c[1] == n[1] && c[2] == n[2])
            return "identities of an in-memory case with input elements new per process, not compared";
        if (P2rRetired.ReplayRaw(file, key) is string raw && c[0] == n[0] && c[1] == n[1] && c[2] == n[2]) return raw; // P2F (12/11)
        return null;
    }

    /// <summary>P2CG (RF-01): the environmental warning of the plug-in build check (<c>PluginBuildCheck</c>, <c>GpcReleaseGuard</c>: «Bin non
    /// aggiornato…», «Librerie GPC caricate diverse…»: the libraries and the folder of the bin of this process), a whole runtime message of its own,
    /// as the replay records it (<c>level: text</c>).</summary>
    internal static bool Environmental(string message)
    {
        const string Level = "Warning: ";
        return message != null && message.StartsWith(Level, StringComparison.Ordinal)
            && (string.CompareOrdinal(message, Level.Length, "Bin non aggiornato", 0, 18) == 0 || string.CompareOrdinal(message, Level.Length, "Librerie GPC caricate diverse", 0, 29) == 0);
    }

    /// <summary>P2CG (RF-01): the captured digest of the messages of 9 instances of two components in 9 definitions (Creep And Shrinkage Concrete in
    /// 50, 50 REV001–REV003, 99 REV002–REV004, gpc-only-properties; Bridge Section Check in 44 and 47, gpc-only-models): one text for all, the build-check
    /// warning of the capture's bin alone. With it, the other messages of the run must be none.</summary>
    internal const string WarningAloneAtCapture = "F9A9899E4115C2C7";

    // P2CI (14/11/2026, user decision 2a): the 4 keys of P2CG (RF-01) whose captured messages held the build-check warning of the capture's bin
    // with other messages (Super Element Check of 47 and 48, Composite Section Self Tension and Linear Tension Analysis in memory) were declared by
    // name here (WarningWithOthersAtCapture). The declaration is removed: their lines are captured again without the warning, a patch of the
    // frozen capture for the user's OK (INT\rapporti\P2CI-catture-20261114.diff).

    /// <summary>P2CG (13/11/2026, review of ChatGPT task 64, RF-03): the in-memory Bridge Phase Model and Bridge Construction Sequence cases of
    /// gpc-only-models whose raw outputs are not compared (their input elements are new in each process), a closed list: the 18 keys of task 64
    /// (lines 573–599 of the capture), not the 8 error cases nor the kN-m case retired by name. Checked against the capture at the close.</summary>
    internal static readonly HashSet<string> PhaseIdentityKeys = new(StringComparer.Ordinal)
    {
        "in memory: Bridge Phase Model (a beam on a replacement, loads cleared)",
        "in memory: Bridge Phase Model (every beam, loads kept)",
        "in memory: Bridge Construction Sequence (everything, no increments)",
        "in memory: Bridge Construction Sequence (everything with an increment: native stage)",
        "in memory: Bridge Construction Sequence (a parent group with an increment: native stage)",
        "in memory: Bridge Construction Sequence (a group of a beam and a plate)",
        "in memory: Bridge Construction Sequence (a child group: part of a parent)",
        "in memory: Bridge Construction Sequence (a beam in two groups: no native stage)",
        "in memory: Bridge Construction Sequence (a beam without a group: no native stage)",
        "in memory: Bridge Construction Sequence (bearings kept: every one)",
        "in memory: Bridge Construction Sequence (bearings: one)",
        "in memory: Bridge Construction Sequence (bearings: disabled)",
        "in memory: Bridge Construction Sequence (a rigid link: no native stage)",
        "in memory: Bridge Construction Sequence (girders of Bridge Member Assignment: a group, override, no bearings)",
        "in memory: Bridge Construction Sequence (girders of Bridge Member Assignment: a physical member)",
        "in memory: Bridge Phase Model (A09: the first beam of a name replaced: the next registers its own property)",
        "in memory: Bridge Phase Model (A09: the replacement takes the name)",
        "in memory: Bridge Construction Sequence (A09: the first beam of a name inactive)",
    };

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
        private readonly SortedDictionary<string, int> declared = new(StringComparer.Ordinal);
        private int recorded, equal;
        // P2CF (13/11, item 4): the declarations by name of this file used by the run (a raw field not compared, a key retired).
        private readonly HashSet<string> usedRaw = new(StringComparer.Ordinal), usedRetired = new(StringComparer.Ordinal);

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
            // P2F (12/11): a key retired by name (P2rRetired.ReplayKeys) is not compared.
            if (P2rRetired.ReplayKeys.TryGetValue((Path.GetFileNameWithoutExtension(path), key), out var retired))
            {
                // P2CF (item 4): a key retired by name is a key of the capture (checked before the exemption).
                if (!captured.ContainsKey(key)) { differences.Add($"retired by name but not in the capture: {key}"); return; }
                usedRetired.Add(key); declared["retired by name: " + retired] = declared.GetValueOrDefault("retired by name: " + retired) + 1; return;
            }
            if (!captured.TryGetValue(key, out var before)) differences.Add($"not in the capture: {key} = {value}");
            else if (before == value) equal++;
            else if (Declared(Path.GetFileNameWithoutExtension(path), key, before, value) is string rule)
            {
                if (rule == P2rRetired.ReplayRaw(Path.GetFileNameWithoutExtension(path), key)) usedRaw.Add(key);
                declared[rule] = declared.GetValueOrDefault(rule) + 1;
            }
            else differences.Add($"{key}: captured {before} / now {value}");
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
            // P2CF (13/11, item 4): a declaration by name of this file (a raw field, P2rRetired.ReplayRawKeys*; a retired key, ReplayKeys) the run
            // did not use is an error: the line would stay in the registry with nothing to declare.
            foreach (var k in P2rRetired.ReplayRawDeclarations(file).Where(k => !usedRaw.Contains(k))) differences.Add("raw field declared, never used: " + k);
            foreach (var k in P2rRetired.ReplayKeys.Keys.Where(k => k.File == file && !usedRetired.Contains(k.Key))) differences.Add("retired by name, never used: " + k.Key);
            // P2CG (RF-03, RF-01): the closed lists name keys of the capture, never a key retired by name.
            if (file == "gpc-only-models")
                foreach (var k in PhaseIdentityKeys)
                    if (!captured.ContainsKey(k) || P2rRetired.ReplayKeys.ContainsKey((file, k))) differences.Add("raw identities declared, not a key of the capture (or retired): " + k);
            Console.WriteLine($"P2R CAPTURE COMPARE {file}: {seen.Count} of {captured.Count} keys seen, {equal} equal, " + string.Concat(declared.Select(d => $"{d.Value} declared ({d.Key}), ")) + $"{differences.Count} different or new, {missing.Count} missing"
                + (differences.Count + missing.Count == 0 ? "" : " FAIL") + " (" + path + ")");
            return differences.Concat(missing.Select(k => "captured, not seen: " + k)).Select(d => "P2R CAPTURE " + file + ": " + d).ToList();
        }
    }
}
