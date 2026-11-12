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
        if (missing != 0) throw new Exception($"NATIVE CAPTURE FAIL: {missing} captured keys not seen or different in this run");
    }

    /// <summary>
    /// P2X, the declared rule «F7d CompositeBridge 1.31» (the only one; user decision of 8/11: Checker <c>3b40e2a0</c>, the steel consolidation,
    /// joins the release, «accetta se spiegate ma riportamelo come cambio di comportamento»). GPCChecker.CompositeBridge 1.26 → 1.31.0.1 solves
    /// the elastic fields of the H bridge engine directly (commit <c>1092c29f</c>, with <c>7b726fc8</c> and <c>87985942</c>; correzioni-F7d.md §7.2):
    /// the stresses of Composite Beam Self Tension change in the last digits (relative differences measured in passo 74 of correzioni-P2B.md, within
    /// the rounding F7d measured; no outcome depends on them). Their capture values are SHA-256 of the canonical texts, so the rule is an explicit
    /// list, never a bound: <c>Fixtures/native-capture-rules/F7d-CompositeBridge-1.31.tsv</c> (label, key, captured value, value with
    /// <c>3b40e2a0</c>; only <c>composite|…|self tension …</c> keys, written from the texts of <c>--composite-texts</c> before and after). A captured value
    /// that differs is accepted only for a listed key, with exactly the listed value before and both the FEM and the GPC value equal to the value
    /// after. The capture itself is never rewritten.
    /// <para>The same rule explains the new SHA-256 of the result fingerprints of the two native runs (<c>ResultFingerprint</c>, compared by the
    /// gates, not by code): 50 <c>99FA70F1…</c> → <c>F9426C0A…</c>, 99 <c>CB903536…</c> → <c>C9517DC7…</c>; their rows, compared field by field in
    /// passo 74: numbers only, no row, text, class or utilisation across 1 changed; the equilibrium residuals of the H engine fall to rounding
    /// (99: at most 3.2e-6 before, 1.8e-16 now) and twelve unloaded flanges of 99 (stress exactly 0 before, ±1e-15 now) count as compressed, with
    /// ρ = 1 and the same effective width.</para>
    /// </summary>
    internal static class SteelRule
    {
        internal const string Name = "F7d CompositeBridge 1.31";
        internal static readonly string File = Path.GetFullPath(Path.Combine("tests", "BridGHe.Smoke", "Fixtures", "native-capture-rules", "F7d-CompositeBridge-1.31.tsv"));
        private static Dictionary<(string Label, string Key), (string Before, string After)> entries;

        private static Dictionary<(string Label, string Key), (string Before, string After)> Entries()
        {
            if (entries != null) return entries;
            var e = new Dictionary<(string Label, string Key), (string Before, string After)>();
            foreach (var line in System.IO.File.ReadLines(File).Skip(1))
            {
                var f = line.Split('\t');
                if (f.Length != 4 || !f[1].StartsWith("composite|", StringComparison.Ordinal) || !f[1].Contains("|self tension ", StringComparison.Ordinal))
                    throw new InvalidDataException($"NATIVE CAPTURE: the rule «{Name}» lists only composite self-tension keys: {line}");
                e.Add((f[0], f[1]), (f[2], f[3]));
            }
            return entries = e;
        }

        /// <summary>Whether the rule lists this difference: the key of this run, the captured value before, FEM and GPC values (where given) after.</summary>
        internal static bool Accepts(string label, string key, string captured, string fem, string gpc) =>
            Entries().TryGetValue((label, key), out var v) && v.Before == captured && (fem == null || fem == v.After) && (gpc == null || gpc == v.After) && (fem ?? gpc) != null;

        /// <summary>The keys the rule lists for a run.</summary>
        internal static int Listed(string label) => Entries().Keys.Count(k => k.Label == label);
    }

    private sealed class Session
    {
        private readonly bool write; private readonly string path;
        private readonly StreamWriter output; private readonly Dictionary<string, string> captured, recorded = new(StringComparer.Ordinal);
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        private int equal, femOnly, different, ruled; private bool closed;
        /// <summary>The label of the run (the name of the capture file: 50, 99).</summary>
        private string Label => Path.GetFileNameWithoutExtension(path);

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
            if ((fem != null && fem != value) || (gpc != null && gpc != value))
            {
                // P2X: a difference the declared steel rule lists (this key, the captured value before, both values now the one after) is
                // accepted and counted apart; any other is listed (all of them, the run goes on) and fails the run at the end (Close).
                if (SteelRule.Accepts(Label, key, value, fem, gpc)) { ruled++; return; }
                different++;
                Console.WriteLine($"NATIVE CAPTURE DIFF {key}: captured '{value}' / FEM '{fem}' / GPC '{gpc}'");
                return;
            }
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
            // P2R (U3, 12/11): the keys retired by name (P2rRetired.NativeKeys, the FEM-only «fem-constant» data) are not required; a retired key
            // recorded again would be compared as any other.
            // P2F (12/11): and the key c5|recipe of 99 (P2rRetired.NativeKeysP2f).
            bool Retired(string k) => !seen.Contains(k) && (P2rRetired.NativeKeys.ContainsKey((label, k)) || P2rRetired.NativeKeysP2f.ContainsKey((label, k)));
            int retired = captured.Keys.Count(Retired), listedRetired = P2rRetired.NativeKeys.Keys.Count(k => k.Label == label) + P2rRetired.NativeKeysP2f.Keys.Count(k => k.Label == label);
            if (retired != listedRetired) { Console.WriteLine($"NATIVE CAPTURE FAIL {label}: {retired} of the {listedRetired} keys retired by name are in the capture and not seen"); different++; }
            int missing = captured.Keys.Count(k => !seen.Contains(k) && !Retired(k));
            int listed = SteelRule.Listed(label), unused = listed - ruled;
            Console.WriteLine($"NATIVE CAPTURE COMPARE {label}: {seen.Count} of {captured.Count} keys seen, {retired} retired by name (P2R, {P2rRetired.Decision}), {equal} GPC values equal to the captured ones, {femOnly} FEM-only data, {missing} missing"
                + (ruled == 0 ? "" : $", {ruled} by the declared rule «{SteelRule.Name}» ({listed} listed)") + (different == 0 ? "" : $", {different} different")
                + (missing == 0 && different == 0 ? "" : " FAIL"));
            if (unused != 0) Console.WriteLine($"NATIVE CAPTURE NOTE {label}: {unused} keys of the rule «{SteelRule.Name}» not applied (the captured value came back or the key was not seen)");
            if (missing != 0) foreach (var k in captured.Keys.Where(k => !seen.Contains(k) && !Retired(k)).Take(20)) Console.WriteLine("  missing: " + k);
            return missing + different;
        }
    }
}
#endif
