using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;

/// <summary>
/// R6.6 lane R1 (11/11): the R6.0 capture <c>scan</c> (<see cref="GpcDraftCapture"/>, written on 6/11 from the FEM path, frozen: read only, never
/// written) as the oracle of the GPC-only replays of the definitions, mapped one to one. The capture holds, for every definition of the scope of
/// Build Model, the drafts of every Build Model input by location (<c>file|Build Model|in&lt;i&gt;|&lt;path&gt;|&lt;item&gt;</c>), the conversion of every
/// Build Model output (<c>file|Build Model|model</c>) and, by content within the file (<c>file|kind|SHA-256 of the text</c>), every material, section,
/// beam and plate property, case, combination and composite section of the GH data of the definition.
/// <para>A datum of a GPC-only output is compared with the captured one as the capture compares (<see cref="GpcDraftCapture.Session.Record"/>): equal
/// text, equal but for GUIDs (the GPC-only outputs take identities derived without a FEM value), or a declared difference by name (the R6.2/R6.3
/// rules of <see cref="GpcDraftCapture"/>: float tokens, the GPC signature of a beam property, a Build Model output renamed by its owner identity with
/// its bridge data in another GUID order). The rules are those of the capture (the same functions where they are reachable, the same code where
/// they are private to <see cref="GpcDraftCapture"/>, a file of another lane).</para>
/// <para>Switch: the variable of the capture, <c>BRIDGHE_CAPTURE=compare:&lt;folder&gt;</c> (the gates and the lane runs set it); without it the
/// mapping is not checked and the replay says so.</para>
/// </summary>
internal sealed class R1ScanCapture
{
    internal enum Outcome { Equal, GuidsOnly, Declared, Different, Missing }

    private sealed class Entry { internal string Kind, Sha; internal byte[] Data, Records, Text; }

    private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    /// <summary>The content keys of a file and kind (<c>file|kind</c>).</summary>
    private readonly Dictionary<string, List<string>> contents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    /// <summary>A content key whose captured text holds a declared digest: the key it takes with the declared text.</summary>
    private readonly Dictionary<string, string> declaredKeys = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> signatureOf = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> signatureOfEntry = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
    private readonly HashSet<string> ambiguous = new HashSet<string>(StringComparer.Ordinal);
    internal readonly string Folder;

    private static R1ScanCapture shared; private static bool opened;

    /// <summary>The capture of the variable <c>BRIDGHE_CAPTURE=compare:&lt;folder&gt;</c> (read once per process), or null.</summary>
    internal static R1ScanCapture Open()
    {
        if (opened) return shared;
        opened = true;
        string setting = Environment.GetEnvironmentVariable(GpcDraftCapture.Variable);
        if (string.IsNullOrWhiteSpace(setting) || !setting.StartsWith("compare:", StringComparison.Ordinal)) return null;
        string folder = setting.Substring("compare:".Length);
        if (!File.Exists(Path.Combine(folder, "scan.bgcap"))) return null;
        return shared = new R1ScanCapture(folder);
    }

    private R1ScanCapture(string folder)
    {
        Folder = folder;
        using (var r = new BinaryReader(File.OpenRead(Path.Combine(folder, "scan.bgcap")), Encoding.UTF8))
        {
            if (!r.ReadBytes(6).SequenceEqual(Encoding.ASCII.GetBytes("BGCAP1"))) throw new InvalidDataException("Not a capture: " + folder);
            while (r.BaseStream.Position < r.BaseStream.Length)
            {
                string key = r.ReadString();
                var e = new Entry { Kind = r.ReadString(), Sha = r.ReadString() };
                int n = r.ReadInt32(); e.Data = n < 0 ? null : r.ReadBytes(n);
                n = r.ReadInt32(); e.Records = n < 0 ? null : r.ReadBytes(n);
                n = r.ReadInt32(); e.Text = r.ReadBytes(n);
                entries.Add(key, e);
            }
        }
        // The signature maps of the capture (R6.3, GpcDraftCapture.MapSignatures): every former digest to the GPC fingerprint of the property read back.
        foreach (var p in entries)
        {
            try
            {
                switch (p.Value.Kind)
                {
                    case "beam": { var d = GpcDraftArchive.ReadBeam(p.Value.Data, p.Value.Records); Map(p.Key, d.Property?.Signature, d.Beam.BeamProperty); break; }
                    case "beam-property": { var b = GpcDraftArchive.ReadBeamProperty(p.Value.Data, p.Value.Records); Map(p.Key, b.Data.Signature, b.Property); break; }
                    case "conversion":
                    {
                        var c = GpcDraftArchive.ReadConversion(p.Value.Data, p.Value.Records);
                        foreach (var x in c.Report.BeamProperties) if (c.Model.BeamProperties.TryGetValue(x.Key, out var property)) Map(p.Key, x.Value.Signature, property);
                        break;
                    }
                }
            }
            catch (InvalidDataException) { throw; }
            catch (Exception) { /* an entry not read back: the capture compare of the gates counts it */ }
        }
        foreach (var p in entries)
        {
            var parts = p.Key.Split('|');
            if (parts.Length == 3 && parts[2] == p.Value.Sha)
            {
                string fileKind = parts[0] + "|" + parts[1];
                if (!contents.TryGetValue(fileKind, out var list)) contents[fileKind] = list = new List<string>();
                list.Add(p.Key);
                var (declaredText, names) = WithDeclaredDigests(Inflate(p.Value.Text), p.Key);
                if (names.Count > 0) declaredKeys[fileKind + "|" + Sha(declaredText)] = p.Key;
            }
        }
    }

    private void Map(string key, string digest, GPC.Model.ElementProperties.BeamProperty property)
    {
        if (digest == null || property == null || digest.StartsWith("GPC:", StringComparison.Ordinal)) return;
        string now = LegacyModelImport.PropertySignature(property);
        if (!signatureOfEntry.TryGetValue(key, out var local)) signatureOfEntry[key] = local = new Dictionary<string, string>(StringComparer.Ordinal);
        local[digest] = now;
        if (signatureOf.TryGetValue(digest, out var known) && known != now) ambiguous.Add(digest);
        signatureOf[digest] = now;
    }

    internal static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Inflate(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); using var d = new DeflateStream(input, CompressionMode.Decompress); using var r = new StreamReader(d, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private static readonly Regex DigestToken = new Regex("(?<![0-9A-F:])[0-9A-F]{64}(?![0-9A-F])", RegexOptions.Compiled);
    private static readonly Regex BridgeFingerprintLine = new Regex("^bridge ([0-9]+) [0-9A-F]{64}$", RegexOptions.Compiled);
    private static readonly Regex BeamBridgeLine = new Regex("^bridge (G[0-9]+|[0-9]+) [0-9A-F]{64}\r?$", RegexOptions.Compiled);

    /// <summary>The captured text with the declared digests replaced (the code of <c>GpcDraftCapture.WithDeclaredDigests</c>, private there).</summary>
    private (string Text, List<string> Names) WithDeclaredDigests(string captured, string key)
    {
        var names = new List<string>();
        if (signatureOf.Count > 0)
        {
            bool any = false;
            signatureOfEntry.TryGetValue(key, out var local);
            captured = DigestToken.Replace(captured, m => local != null && local.TryGetValue(m.Value, out var mine) ? (any = true, mine).mine
                : !ambiguous.Contains(m.Value) && signatureOf.TryGetValue(m.Value, out var now) ? (any = true, now).now : m.Value);
            if (any) names.Add(GpcDraftCapture.SignatureRule);
        }
        foreach (var (before, after, name) in GpcDraftCapture.DeclaredDigests)
            if (captured.Contains(before, StringComparison.Ordinal)) { captured = captured.Replace(before, after, StringComparison.Ordinal); names.Add(name); }
        return (captured, names);
    }

    /// <summary>The bridge-data rule of a Build Model output (the code of <c>GpcDraftCapture.OnlyBridgeFingerprint</c>, private there).</summary>
    private static bool OnlyBridgeFingerprint(string before, string now)
    {
        var a = before.Split('\n'); var b = now.Split('\n');
        if (a.Length != b.Length) return false;
        int differing = 0; var bridgeA = new List<string>(); var bridgeB = new List<string>();
        for (int i = 0; i < a.Length; i++)
        {
            bool la = a[i].StartsWith("BRIDGE ", StringComparison.Ordinal), lb = b[i].StartsWith("BRIDGE ", StringComparison.Ordinal);
            if (la != lb) return false;
            if (la) { bridgeA.Add(a[i]); bridgeB.Add(b[i]); continue; }
            if (a[i] == b[i]) continue;
            var ma = BridgeFingerprintLine.Match(a[i]); var mb = BridgeFingerprintLine.Match(b[i]);
            if (!ma.Success || !mb.Success || ma.Groups[1].Value != mb.Groups[1].Value || ++differing > 1) return false;
        }
        bridgeA.Sort(StringComparer.Ordinal); bridgeB.Sort(StringComparer.Ordinal);
        return bridgeA.SequenceEqual(bridgeB, StringComparer.Ordinal);
    }

    /// <summary>A text now against a captured entry, as the capture compares.</summary>
    private Outcome Against(string key, Entry e, object datum, string text, out string detail)
    {
        detail = null;
        if (Sha(text) == e.Sha) return Outcome.Equal;
        string captured = Inflate(e.Text);
        if (GpcDraftCapture.WithoutGuids(captured) == GpcDraftCapture.WithoutGuids(text)) return Outcome.GuidsOnly;
        var (declared, names) = WithDeclaredDigests(captured, key);
        if (names.Count > 0 && (declared == text || GpcDraftCapture.RenamedOnly(declared, text, out _)
            || GpcDraftCapture.WithoutGuids(declared) == GpcDraftCapture.WithoutGuids(text))) { detail = string.Join("; ", names); return Outcome.Declared; }
        // R6.3 (GpcDraftCapture.SharedCompositeRule): a Build Model output, its bridge data in another GUID order, the bridge values equal beam by beam.
        var bridgeNow = datum is GpcModels.Conversion cn && e.Kind == "conversion" ? cn.Report.Bridge : null;
        if (bridgeNow != null && OnlyBridgeFingerprint(GpcDraftCapture.WithoutGuids(declared), GpcDraftCapture.WithoutGuids(text))
            && GpcDraftCapture.BridgeValues(bridgeNow).SequenceEqual(GpcDraftCapture.BridgeValues(GpcDraftArchive.ReadConversion(e.Data, e.Records).Report.Bridge), StringComparer.Ordinal))
        { detail = GpcDraftCapture.SharedCompositeRule; return Outcome.Declared; }
        // R1 (the declared rule of the replays, GpcOnlyReplay.Text): the bridge data of a beam draft are keyed by the GUID of the beam, so their
        // fingerprint (the line «bridge <GUID> <fingerprint>») changes with the identity: a beam draft equal but for that line, with the same bridge
        // values (member, super element, section state, composite sections: GpcDraftCapture.BridgeValues), is declared.
        if (datum is LegacyBeamDraft { Bridge: { } bridge } && e.Kind == "beam")
        {
            var a = GpcDraftCapture.WithoutGuids(declared).Split('\n'); var b = GpcDraftCapture.WithoutGuids(text).Split('\n');
            // The bridge line of the draft («bridge <GUID> <fingerprint>») and of its report («bridge <count> <fingerprint>»), GUIDs as ordinals.
            static bool BridgeLine(string l) => BeamBridgeLine.IsMatch(l);
            if (a.Length == b.Length && a.Zip(b).All(p => p.First == p.Second || BridgeLine(p.First) && BridgeLine(p.Second)))
            {
                var now = new GPC.Checkers.CompositeBridge.BridgeModelData(); now.Set(bridge);
                var capturedBridge = GpcDraftArchive.ReadBeam(e.Data, e.Records).Bridge;
                var before = new GPC.Checkers.CompositeBridge.BridgeModelData(); if (capturedBridge != null) before.Set(capturedBridge);
                if (capturedBridge != null && GpcDraftCapture.BridgeValues(now).SequenceEqual(GpcDraftCapture.BridgeValues(before), StringComparer.Ordinal))
                { detail = BeamBridgeRule; return Outcome.Declared; }
                detail = "the bridge values differ: " + string.Join(" ; ", GpcDraftCapture.BridgeValues(before)) + " -> " + string.Join(" ; ", GpcDraftCapture.BridgeValues(now));
                return Outcome.Different;
            }
            var differing = a.Zip(b).Where(p => p.First != p.Second).Take(3).Select(p => "  before " + p.First + "\n  after  " + p.Second);
            detail = $"{(a.Length == b.Length ? a.Zip(b).Count(p => p.First != p.Second) : -1)} lines differ ({a.Length} / {b.Length} lines):\n" + string.Join("\n", differing);
            return Outcome.Different;
        }
        detail = GpcDraftArchiveChecks.FirstDifference(GpcDraftCapture.WithoutGuids(captured), GpcDraftCapture.WithoutGuids(text));
        return Outcome.Different;
    }

    /// <summary>R1: the declared rule of a beam draft whose bridge data, keyed by the GUID of the beam, have another fingerprint and the same values.</summary>
    internal const string BeamBridgeRule = "R1: beam draft: the bridge data keyed by the GUID of the beam (another fingerprint), the bridge values equal (the rule of the replays)";

    /// <summary>True when the capture has an entry at the location.</summary>
    internal bool Has(string key) => entries.ContainsKey(key);

    /// <summary>A datum at a captured location (a Build Model input item, a Build Model output).</summary>
    internal Outcome Compare(string key, object datum, out string detail)
    {
        detail = null;
        if (!entries.TryGetValue(key, out var e)) return Outcome.Missing;
        return Against(key, e, datum, GpcDraftCapture.Text(datum), out detail);
    }

    /// <summary>The capture kind of a datum recorded by content (<see cref="GpcMaterialKindChecks.Scan"/>: «material» for a material, else the type
    /// name of the draft), or null for a datum the capture does not record by content.</summary>
    internal static string ContentKind(object datum) => datum switch
    {
        GPC.Model.Materials.Material => "material",
        LegacySectionRecipe or LegacyBeamProperty or LegacyCaseData or LegacyCombinationDraft or LegacyStageDraft
            or GPC.Model.ElementProperties.PlateProperty or GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition => datum.GetType().Name,
        _ => null,
    };

    /// <summary>A datum recorded by content within its file: one of the captured contents of the file and kind (equal, equal but for GUIDs or declared).</summary>
    internal Outcome CompareContent(string file, object datum, out string detail)
    {
        detail = null;
        string kind = ContentKind(datum);
        if (kind == null) return Outcome.Missing;
        string text = GpcDraftCapture.Text(datum), fileKind = file + "|" + kind;
        if (entries.ContainsKey(fileKind + "|" + Sha(text))) return Outcome.Equal;
        // A material is recorded as «material» (the datum of a GH_Material) or as «check-material» (the material of a property, by the same text).
        if (kind == "material" && entries.ContainsKey(file + "|check-material|" + Sha(text))) return Outcome.Equal;
        if (declaredKeys.TryGetValue(fileKind + "|" + Sha(text), out var former)) { detail = "declared digest of " + former; return Outcome.Declared; }
        var keys = (contents.TryGetValue(fileKind, out var own) ? own : new List<string>())
            .Concat(kind == "material" && contents.TryGetValue(file + "|check-material", out var checks) ? checks : Enumerable.Empty<string>()).ToList();
        if (keys.Count == 0) return Outcome.Missing;
        string nearest = null; int fewest = int.MaxValue;
        var now = GpcDraftCapture.WithoutGuids(text).Split('\n');
        foreach (var key in keys)
        {
            var outcome = Against(key, entries[key], datum, text, out string why);
            if (outcome == Outcome.GuidsOnly || outcome == Outcome.Declared) { detail = why; return outcome; }
            // For the report: the captured content of the file and kind nearest to the datum (the fewest differing lines).
            var before = GpcDraftCapture.WithoutGuids(WithDeclaredDigests(Inflate(entries[key].Text), key).Text).Split('\n');
            int differ = before.Length != now.Length ? int.MaxValue - 1 : before.Zip(now).Count(p => p.First != p.Second);
            if (differ < fewest) { fewest = differ; nearest = key + $" ({differ} lines differ)\n" + GpcDraftArchiveChecks.FirstDifference(string.Join("\n", before), string.Join("\n", now)); }
        }
        detail = nearest == null ? null : "nearest captured content " + nearest;
        return Outcome.Missing;
    }

    /// <summary>The number of entries (the PASS line).</summary>
    internal int Count => entries.Count;

    /// <summary>The definitions of the capture (the file part of the keys).</summary>
    internal HashSet<string> Files => new HashSet<string>(entries.Keys.Select(k => k.Split('|')[0]), StringComparer.Ordinal);
}
