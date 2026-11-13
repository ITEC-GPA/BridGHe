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
using GPC.Model.Materials;

/// <summary>
/// R6.0 (coordinator, 6/11): the frozen capture of the GPC data of the GH types, written with the final writer (BGHGPC1 + BridGHeRecords,
/// <see cref="GpcDraftArchive"/>) before FeMM is removed. It becomes the reference of the checks that compare with the FEM path today
/// (the FEM path disappears in R6.5). No existing baseline is regenerated: this is a new capture.
/// <para>Switch: environment variable <c>BRIDGHE_CAPTURE</c> = <c>write:&lt;folder&gt;</c> or <c>compare:&lt;folder&gt;</c>. The traversals
/// that record (one source each): regression <c>gpc-draft-archive</c> (source <c>corpus</c>: every datum of the import-dump corpus),
/// <c>--legacy-payload-scan</c> (source <c>payloads</c>: the datum of every BGHA1 payload of the example definitions, by its location),
/// <c>--material-scan</c> (source <c>scan</c>: the drafts of every Build Model input and output of the scope of L12, every GPC material with
/// its check views, every other datum of the GH types, by location).</para>
/// <para>An entry: key (location, with <c>#n</c> for the n-th repetition), kind, SHA-256 of the canonical text
/// (<see cref="GpcDraftArchiveChecks.Text(LegacyBeamDraft)"/> and the others; materials with their check views), the two chunks of the final
/// writer, and the canonical text (deflated, for the first difference). Compare: the text of the datum recorded now against the captured one
/// (equal, equal but for GUIDs, different), every captured key seen, and every captured entry read back by the current final reader to the
/// captured text (part (i) on the frozen bytes).</para>
/// </summary>
internal static class GpcDraftCapture
{
    internal const string Variable = "BRIDGHE_CAPTURE";
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("BGCAP1");

    /// <summary>A datum the FEM path (or the reader) refuses: the message is captured.</summary>
    internal sealed record Refused(string Kind, string Message);

    /// <summary>A payload from which 0.3.0 reads no datum (a null model).</summary>
    internal sealed record Nothing(string Kind, string Why);

    /// <summary>True when a compare session found a difference (the run that opened it exits 1).</summary>
    internal static bool Failed { get; private set; }

    internal static Session Open(string source)
    {
        string setting = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(setting)) return null;
        int colon = setting.IndexOf(':');
        if (colon < 0) throw new ArgumentException(Variable + " must be write:<folder> or compare:<folder>");
        string mode = setting.Substring(0, colon), folder = setting.Substring(colon + 1);
        if (mode != "write" && mode != "compare") throw new ArgumentException(Variable + " must be write:<folder> or compare:<folder>");
        return new Session(source, folder, mode == "write");
    }

    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

    private static string Views(Material m)
    {
        static string Try(Func<Material> f) { try { return GpcMaterialKindChecks.Dump(f()); } catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { return "ERROR " + x.GetType().Name + " " + x.Message; } }
        var steel = Try(() => GpcMaterials.StructuralSteel(m)); var bars = Try(() => GpcMaterials.Reinforcement(m)); var concrete = Try(() => GpcMaterials.Concrete(m));
        var analysis = Try(() => GpcMaterials.Analysis(m));
        return "\nanalysis " + analysis + "\nstructural steel " + steel + "\nbars " + bars + "\nconcrete " + concrete;
    }

    /// <summary>The canonical text of a datum (the texts of the round trip of part (i); a material with its analysis and check views).</summary>
    internal static string Text(object datum) => datum switch
    {
        LegacyNodeDraft n => GpcDraftArchiveChecks.Text(n),
        LegacyBeamDraft b => GpcDraftArchiveChecks.Text(b),
        LegacyPlateDraft p => GpcDraftArchiveChecks.Text(p),
        Material m => GpcDraftArchiveChecks.Text(m) + Views(m),
        LegacyCaseData c => GpcDraftArchiveChecks.Text(c),
        LegacyCombinationDraft c => GpcDraftArchiveChecks.Text(c),
        LegacyStageDraft s => GpcDraftArchiveChecks.Text(s),
        LegacySectionRecipe s => GpcDraftArchiveChecks.Text(s),
        LegacyBeamProperty p => GpcDraftArchiveChecks.Text(p),
        GPC.Model.ElementProperties.PlateProperty p => GpcDraftArchiveChecks.Text(p),
        GpcDraftArchiveChecks.ModelDatum m => GpcDraftArchiveChecks.Text(m.Model, m.Units),
        GpcModels.Conversion c => ConversionText(c),
        GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition c => GpcDraftArchiveChecks.Text(c),
        LegacyGroupChain g => "GROUP " + g.Names.Count + "\n" + string.Join("\n", g.Names.Select(n => n == null ? "<null>" : "'" + n + "'")),
        Refused r => "REFUSED " + r.Kind + " " + r.Message,
        Nothing n => "NOTHING " + n.Kind + " " + n.Why,
        // R6.4: a text the GPC path gives beside the FEM path (the summary and diagnostics of Build BridGHe Model), compared as it is.
        string t => "TEXT\n" + t,
        _ => throw new ArgumentException("Not a capturable datum: " + datum?.GetType().Name),
    };

    /// <summary>The two chunks of the final writer for a datum (null where the datum has none).</summary>
    private static (string Kind, byte[] Data, byte[] Records) Archive(object datum)
    {
        switch (datum)
        {
            case LegacyNodeDraft n: { var (d, r) = GpcDraftArchive.Write(n); return ("node", d, r); }
            case LegacyBeamDraft b: { var (d, r) = GpcDraftArchive.Write(b); return ("beam", d, r); }
            case LegacyPlateDraft p: { var (d, r) = GpcDraftArchive.Write(p); return ("plate", d, r); }
            case Material m: return ("material", GpcDraftArchive.Write(m), null);
            case LegacyCaseData c: return ("case", null, GpcDraftArchive.Write(c));
            case LegacyCombinationDraft c: return ("combination", null, GpcDraftArchive.Write(c));
            case LegacyStageDraft s: return ("stage", null, GpcDraftArchive.Write(s));
            case LegacySectionRecipe s: return ("section", null, GpcDraftArchive.Write(s));
            case LegacyBeamProperty p: { var (d, r) = GpcDraftArchive.Write(p); return ("beam-property", d, r); }
            case GPC.Model.ElementProperties.PlateProperty p: { var (d, r) = GpcDraftArchive.Write(p); return ("plate-property", d, r); }
            case GpcDraftArchiveChecks.ModelDatum m: { var (d, r) = GpcDraftArchive.Write(m.Model, m.Units); return ("model", d, r); }
            case GpcModels.Conversion c: { var (d, r) = GpcDraftArchive.Write(c); return ("conversion", d, r); }
            case GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition c: return ("composite", GpcDraftArchive.Write(c), null);
            case Refused r: return ("refused", null, null);
            case Nothing n: return ("nothing", null, null);
            default: throw new ArgumentException("Not a capturable datum: " + datum?.GetType().Name);
        }
    }

    /// <summary>The datum of captured chunks, read by the current final reader.</summary>
    private static string ReadBack(string kind, byte[] data, byte[] records, string capturedText) => kind switch
    {
        "node" => Text(GpcDraftArchive.ReadNode(data, records)),
        "beam" => Text(GpcDraftArchive.ReadBeam(data, records)),
        "plate" => Text(GpcDraftArchive.ReadPlate(data, records)),
        "material" => Text(GpcDraftArchive.ReadMaterial(data)),
        "case" => Text(GpcDraftArchive.ReadCase(records)),
        "combination" => Text(GpcDraftArchive.ReadCombination(records)),
        "stage" => Text(GpcDraftArchive.ReadStage(records)),
        "section" => Text(GpcDraftArchive.ReadSection(records)),
        "beam-property" => Text(GpcDraftArchive.ReadBeamProperty(data, records)),
        "plate-property" => Text(GpcDraftArchive.ReadPlateProperty(data, records)),
        "model" => ((Func<string>)(() => { var (m, u) = GpcDraftArchive.ReadModel(data, records); return GpcDraftArchiveChecks.Text(m, u); }))(),
        "composite" => Text(GpcDraftArchive.ReadComposite(data)),
        "conversion" => Text(GpcDraftArchive.ReadConversion(data, records)),
        "refused" or "nothing" => capturedText,
        _ => throw new InvalidDataException("Unknown captured kind " + kind),
    };

    /// <summary>A model with its conversion report and tolerance (<c>GH_Model</c>): the model text, the report of the import dump, and the
    /// native sections and property data in full.</summary>
    internal static string ConversionText(GpcModels.Conversion c)
    {
        var s = new StringBuilder(GpcDraftArchiveChecks.Text(c.BridgeModel, c.Report.Units));
        s.Append("\ntolerance ").Append(B(c.Tolerance)).Append('\n').Append(ImportDump.Report(c.Report));
        foreach (var n in c.Report.NativeSections) s.Append("NATIVE SECTION ").Append(n.Key).Append(' ').Append(n.Value == null ? "null" : Convert.ToHexString(SHA256.HashData(n.Value.Save()))).Append('\n');
        foreach (var p in c.Report.BeamProperties) s.Append("PROPERTY DATA ").Append(p.Key).Append(' ').Append(GpcDraftArchiveChecks.Property(p.Value)).Append('\n');
        return s.ToString();
    }

    private static readonly Regex GuidPattern = new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    /// <summary>A text with every GUID replaced by its ordinal of first appearance (to tell a change of identity from a change of data).</summary>
    internal static string WithoutGuids(string text)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return GuidPattern.Replace(text, m => { if (!seen.TryGetValue(m.Value, out int n)) seen[m.Value] = n = seen.Count + 1; return "G" + n; });
    }

    /// <summary>
    /// R6.2 part C (P2N, 6/11): the declared differences of the comparison with the capture, by name (the capture is never rewritten).
    /// <para><b>Float tokens of <see cref="LegacyArchive"/></b> (finding A of P2U, fix approved by the coordinator on 6/11): the binary XML
    /// writer stores a double that a float represents exactly as a float token, and the reader took its text, the float's shortest string, which
    /// reads to another double. Fixed, the content text of a former value spells the exact double where the two differ. In the three captures
    /// one content changes: the FEM section of the beam property «Montanti» (examples 49 and 50: RHS 200×200×8, Steel «Acciaio carpenteria»),
    /// whose <c>_iyy</c> and <c>I22</c> are 37814272, spelled 37814270 before. The reader reads its GPC section from the recipe (dimensions):
    /// only the content digest of the property (<see cref="LegacyBeamProperty.Signature"/>) changes, in the property (6 files, keyed by content),
    /// in the 288 beam drafts that carry it (49(1), two Build Model) and in the two Build Model outputs, where the owner identity, made from the
    /// content of the drafts, renames the GUIDs (a bijection) and so re-keys the bridge data (BRIDGE BEAM lines in GUID order, the fingerprint
    /// of the bridge data). No number of any datum changes.</para>
    /// </summary>
    internal static readonly (string Before, string After, string Name)[] DeclaredDigests =
    {
        ("99963D9A269D94D5DA579C570F9BCAFBE86E0376200E7C921F422E4914E96491", "9CFA2B863B914CA80C45F54595AC7E7BB3931CA939F5365C6F107059A017EAD2",
            "float tokens: content digest of the beam property Montanti (_iyy, I22 = 37814272 read as 37814270 before the fix)"),
    };

    /// <summary>
    /// R6.3 (bridges 2 and 3 removed, decision 4 of the pre-removal point): every draft signs its beam property with the GPC fingerprint of
    /// the property (<c>LegacyModelImport.PropertySignature</c>) instead of the digest of the former content. The digests of the capture map
    /// to the fingerprints of the GPC properties read back from the captured bytes by the current final reader (beam drafts, beam properties,
    /// the registered properties of the conversions): in the captured text a digest is replaced by its fingerprint (a declared difference by
    /// name, the capture is not regenerated); the rest of the text is compared as it is (and the owner GUIDs, made from the content of the
    /// drafts, by <see cref="RenamedOnly"/>).
    /// </summary>
    internal const string SignatureRule = "R6.3: the signature of a beam property is the GPC fingerprint of the property (was the digest of the former content)";

    private static readonly Regex DigestToken = new Regex("(?<![0-9A-F:])[0-9A-F]{64}(?![0-9A-F])", RegexOptions.Compiled);

    /// <summary>The fingerprint of every digest of the reference (filled when a capture is opened for comparison).</summary>
    private static readonly Dictionary<string, string> SignatureOf = new Dictionary<string, string>(StringComparer.Ordinal);
    // A digest of the former content does not know the units of the draft (the same former property in kN-m and in N-mm drafts): the
    // fingerprints of the properties of an entry first (by key), the global map only for a digest with one fingerprint.
    private static readonly Dictionary<string, Dictionary<string, string>> SignatureOfEntry = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
    private static readonly HashSet<string> Ambiguous = new HashSet<string>(StringComparer.Ordinal);

    private static void MapSignature(string key, string digest, GPC.Model.ElementProperties.BeamProperty property)
    {
        if (digest == null || property == null || digest.StartsWith("GPC:", StringComparison.Ordinal)) return;
        string now = LegacyModelImport.PropertySignature(property);
        lock (SignatureOf)
        {
            if (!SignatureOfEntry.TryGetValue(key, out var local)) SignatureOfEntry[key] = local = new Dictionary<string, string>(StringComparer.Ordinal);
            if (local.TryGetValue(digest, out var mine) && mine != now) throw new InvalidDataException("Two GPC fingerprints for the digest " + digest + " in " + key + ": " + mine + ", " + now);
            local[digest] = now;
            if (SignatureOf.TryGetValue(digest, out var known) && known != now) Ambiguous.Add(digest);
            SignatureOf[digest] = now;
        }
    }

    /// <summary>R6.3: the digests of the captured entries mapped to the fingerprints of their GPC properties (read back).</summary>
    private static void MapSignatures(string key, string kind, byte[] data, byte[] records)
    {
        switch (kind)
        {
            case "beam": { var d = GpcDraftArchive.ReadBeam(data, records); MapSignature(key, d.Property?.Signature, d.Beam.BeamProperty); break; }
            case "beam-property": { var p = GpcDraftArchive.ReadBeamProperty(data, records); MapSignature(key, p.Data.Signature, p.Property); break; }
            case "conversion":
            {
                var c = GpcDraftArchive.ReadConversion(data, records);
                foreach (var r in c.Report.BeamProperties)
                    if (c.Model.BeamProperties.TryGetValue(r.Key, out var property)) MapSignature(key, r.Value.Signature, property);
                break;
            }
        }
    }

    /// <summary>The captured text with the declared digests replaced, and their names (none: the text unchanged).</summary>
    private static (string Text, List<string> Names) WithDeclaredDigests(string captured, string key = null)
    {
        var names = new List<string>();
        if (SignatureOf.Count > 0)
        {
            bool any = false;
            Dictionary<string, string> local = null;
            if (key != null) SignatureOfEntry.TryGetValue(key, out local);
            captured = DigestToken.Replace(captured, m => local != null && local.TryGetValue(m.Value, out var mine) ? (any = true, mine).mine
                : !Ambiguous.Contains(m.Value) && SignatureOf.TryGetValue(m.Value, out var now) ? (any = true, now).now : m.Value);
            if (any) names.Add(SignatureRule);
        }
        foreach (var (before, after, name) in DeclaredDigests)
            if (captured.Contains(before, StringComparison.Ordinal)) { captured = captured.Replace(before, after, StringComparison.Ordinal); names.Add(name); }
        return (captured, names);
    }

    private static readonly Regex BridgeFingerprintLine = new Regex("^bridge ([0-9]+) [0-9A-F]{64}$", RegexOptions.Compiled);

    /// <summary>
    /// True when <paramref name="now"/> is <paramref name="before"/> with its GUIDs renamed by a bijection, read from the lines whose text
    /// without GUIDs is unique in both, as a set of lines, but for the fingerprint line of the bridge data (<c>bridge n fingerprint</c>, the
    /// same n), which hashes data keyed by beam GUID: the proof of passo 53 that an owner identity renamed the GUIDs only.
    /// </summary>
    internal static bool RenamedOnly(string before, string now, out string detail)
    {
        detail = null;
        var a = before.Split('\n'); var b = now.Split('\n');
        if (a.Length != b.Length) return false;
        static string Mask(string l) => GuidPattern.Replace(l, "G");
        // The mapping from the lines aligned by their text without GUIDs (each side sorted, stable); a conflict refuses. Whatever the
        // alignment, the check below accepts only a bijection under which the two texts are the same lines.
        var sa = a.OrderBy(Mask, StringComparer.Ordinal).ToArray(); var sb = b.OrderBy(Mask, StringComparer.Ordinal).ToArray();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var image = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int k = 0; k < sa.Length; k++)
        {
            if (Mask(sa[k]) != Mask(sb[k])) continue;
            var ga = GuidPattern.Matches(sa[k]); var gb = GuidPattern.Matches(sb[k]);
            if (ga.Count != gb.Count) return false;
            for (int i = 0; i < ga.Count; i++)
            {
                string x = ga[i].Value.ToLowerInvariant(), y = gb[i].Value.ToLowerInvariant();
                if (map.TryGetValue(x, out var m) && m != y) return false;
                if (image.TryGetValue(y, out var back) && back != x) return false;
                map[x] = y; image[y] = x;
            }
        }
        var renamed = a.Select(l => GuidPattern.Replace(l, m => map.TryGetValue(m.Value, out var y) ? y : m.Value.ToLowerInvariant())).ToList();
        var current = b.Select(l => GuidPattern.Replace(l, m => m.Value.ToLowerInvariant())).ToList();
        var left = renamed.GroupBy(l => l, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        foreach (var l in current) { if (!left.TryGetValue(l, out int c)) { left[l] = -1; continue; } left[l] = c - 1; }
        var onlyBefore = left.Where(p => p.Value > 0).SelectMany(p => Enumerable.Repeat(p.Key, p.Value)).ToList();
        var onlyNow = left.Where(p => p.Value < 0).SelectMany(p => Enumerable.Repeat(p.Key, -p.Value)).ToList();
        if (onlyBefore.Count > 1 || onlyNow.Count != onlyBefore.Count) return false;
        if (onlyBefore.Count == 1)
        {
            var mb = BridgeFingerprintLine.Match(onlyBefore[0]); var mn = BridgeFingerprintLine.Match(onlyNow[0]);
            if (!mb.Success || !mn.Success || mb.Groups[1].Value != mn.Groups[1].Value) return false;
        }
        // Only the bridge data (keyed by beam GUID) may move: the BRIDGE lines and the fingerprint line.
        var movedLines = renamed.Zip(current, (x, y) => (x, y)).Where(p => p.x != p.y).ToList();
        if (movedLines.Any(p => !(p.x.StartsWith("BRIDGE ", StringComparison.Ordinal) || BridgeFingerprintLine.IsMatch(p.x)))) return false;
        int moved = movedLines.Count;
        detail = $"{map.Count(p => p.Key != p.Value)} GUIDs renamed of {map.Count} (a bijection), {moved} lines of the bridge data in another place{(onlyBefore.Count == 1 ? ", the bridge-data fingerprint" : "")}";
        return true;
    }

    /// <summary>True when the captured text differs from the text now only by a declared difference (<see cref="DeclaredDigests"/>).</summary>
    /// <summary>
    /// R6.3 (bridge 3 removed): Build Model takes the drafts as they are; the owner identity of the model, made from the keys of the drafts
    /// with their signatures, renames the GUIDs (<see cref="SignatureRule"/>), so the bridge data of the model, keyed by beam GUID, come in
    /// another order (the BRIDGE lines of the report) and their fingerprint changes (and the drafts' bridge records are shared by the model,
    /// no longer copied through the bridge path: references in the graph the fingerprint reads). Declared for a Build Model output when its
    /// text, the GUIDs replaced by their ordinals (a bijection in the order of the text), is the captured one but for the BRIDGE lines (the
    /// same multiset) and the bridge-data fingerprint line, and the bridge data have the same values beam by beam (<see cref="BridgeValues"/>:
    /// member, super element, section state and the canonical texts of the start and end composite definitions, as a multiset, read back
    /// from the captured bytes).
    /// </summary>
    internal const string SharedCompositeRule = "R6.3: Build Model output: owner GUIDs from the GPC signatures, the bridge data in another GUID order (BRIDGE lines, bridge-data fingerprint), the bridge values equal beam by beam";

    private static bool OnlyBridgeFingerprint(string before, string now, out string detail)
    {
        detail = null;
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
        if (!bridgeA.SequenceEqual(bridgeB, StringComparer.Ordinal)) return false;
        detail = $"the text equal with the GUIDs replaced by their ordinals, {bridgeA.Count} BRIDGE lines in another order{(differing == 1 ? ", the bridge-data fingerprint" : "")}";
        return true;
    }

    /// <summary>
    /// P2F (12/11): since P2K the GPC path derives the GUIDs of the drafts of the Build Model inputs (rule of the derived identities declared by
    /// P2K; the node and plate drafts of the scan are equal but for GUIDs). The bridge line of a beam draft (<c>bridge &lt;beam GUID&gt;
    /// &lt;fingerprint&gt;</c>) hashes the bridge data keyed by the beam GUID, so its fingerprint changes with the GUID alone. Declared when the
    /// text, the declared digests applied and the GUIDs replaced by their ordinals, is the captured one but for that line (the same ordinal),
    /// and the bridge values of the beam (<see cref="BridgeValues"/>: member, super element, section state, composite definitions, read back
    /// from the captured bytes) are equal.
    /// </summary>
    internal const string BeamGuidRule = "P2F: beam draft with the GUIDs derived on the GPC path (P2K), its bridge line keyed by the beam GUID, the bridge values equal";

    private static readonly Regex BeamBridgeLine = new Regex("^bridge G([0-9]+) [0-9A-F]{64}$", RegexOptions.Compiled);

    /// <summary>True when the two texts differ only by the bridge line of the beam (<c>bridge G&lt;n&gt; fingerprint</c>, the same ordinal) and,
    /// at most, the bridge line of its report (<c>bridge &lt;count&gt; fingerprint</c>, the same count: the same data keyed by the beam GUID).</summary>
    private static bool OnlyBeamBridgeLine(string before, string now)
    {
        var a = before.Split('\n'); var b = now.Split('\n');
        if (a.Length != b.Length) return false;
        int beam = 0, report = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == b[i]) continue;
            var ma = BeamBridgeLine.Match(a[i]); var mb = BeamBridgeLine.Match(b[i]);
            if (ma.Success && mb.Success && ma.Groups[1].Value == mb.Groups[1].Value) { if (++beam > 1) return false; continue; }
            var ra = BridgeFingerprintLine.Match(a[i]); var rb = BridgeFingerprintLine.Match(b[i]);
            if (ra.Success && rb.Success && ra.Groups[1].Value == rb.Groups[1].Value) { if (++report > 1) return false; continue; }
            return false;
        }
        return beam == 1;
    }

    private static GPC.Checkers.CompositeBridge.BridgeModelData OneBeam(GPC.Checkers.CompositeBridge.BridgeBeamData d)
    {
        var one = new GPC.Checkers.CompositeBridge.BridgeModelData();
        if (d != null) one.Set(d);
        return one;
    }

    internal static List<string> BridgeValues(GPC.Checkers.CompositeBridge.BridgeModelData data)
    {
        string Def(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition d) => d == null ? "-" : Sha(GpcDraftArchiveChecks.Text(d));
        var list = data.Beams.Select(b => string.Join("|", b.Member == null ? "-" : GPC.Model.Core.ModelValues.Fingerprint(new object[] { b.Member }), b.SuperElement ?? "-",
            b.SectionState == null ? "-" : $"{b.SectionState.Behavior} {b.SectionState.ConcreteExcluded} {b.SectionState.ModularRatio:R} {b.SectionState.ClassificationCase} {b.SectionState.PropertyFingerprint}",
            b.CompositeSection == null ? "-" : Def(b.CompositeSection.Start) + " " + Def(b.CompositeSection.End))).ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    internal static bool Declared(string captured, string now, out string why, string key = null)
    {
        why = null;
        var (text, names) = WithDeclaredDigests(captured, key);
        if (names.Count == 0) return false;
        if (text == now) { why = string.Join("; ", names); return true; }
        if (RenamedOnly(text, now, out string detail)) { why = string.Join("; ", names) + "; owner identity from the content of the drafts: " + detail; return true; }
        return false;
    }

    private static byte[] Deflate(string text)
    {
        using var output = new MemoryStream();
        using (var d = new DeflateStream(output, CompressionLevel.Fastest, true)) { var b = Encoding.UTF8.GetBytes(text); d.Write(b, 0, b.Length); }
        return output.ToArray();
    }

    private static string Inflate(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); using var d = new DeflateStream(input, CompressionMode.Decompress); using var r = new StreamReader(d, Encoding.UTF8);
        return r.ReadToEnd();
    }

    private sealed class Entry { internal string Kind, Sha; internal byte[] Data, Records, Text; }

    internal sealed class Session : IDisposable
    {
        private readonly string source, folder; internal readonly bool Writing;
        private readonly Dictionary<string, int> occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly BinaryWriter pack; private readonly StreamWriter index;
        private readonly Dictionary<string, Entry> reference;
        private readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<string> failures = new List<string>(), identityOnly = new List<string>(), declared = new List<string>();
        // R6.2 part C: a captured entry keyed by its content (file|kind|SHA of the text) whose text holds a declared digest: the key it takes now.
        private readonly Dictionary<string, string> declaredKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        private int entries, readBack, readBackFailures; private long bytes;
        // Lane INR (12/11): BRIDGHE_CAPTURE_MOVED=<folder> in compare mode writes every entry that is different or new now, in the format of the
        // capture (<source>.moved.bgcap, the entry as the writer would write it today), and the list of every different, new and missing key
        // with the first difference (<source>.moved.tsv), without the limit of 30 of the listing. For a new copy of a capture, never for this one.
        private BinaryWriter movedPack; private StreamWriter movedIndex;
        private void Moved(string status, string key, object datum, string text, string difference)
        {
            string folder = Environment.GetEnvironmentVariable("BRIDGHE_CAPTURE_MOVED");
            if (string.IsNullOrWhiteSpace(folder)) return;
            if (movedIndex == null)
            {
                Directory.CreateDirectory(folder);
                string packPath = Path.Combine(folder, source + ".moved.bgcap");
                if (File.Exists(packPath) || Path.GetFullPath(folder).TrimEnd('\\', '/').Equals(Path.GetFullPath(this.folder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Moved entries already written or written into the capture compared (never overwritten): " + packPath);
                movedPack = new BinaryWriter(new FileStream(packPath, FileMode.CreateNew), Encoding.UTF8); movedPack.Write(Header);
                movedIndex = new StreamWriter(Path.Combine(folder, source + ".moved.tsv"), false, new UTF8Encoding(false));
                movedIndex.WriteLine("status\tkey\tkind\ttext-sha256\tdata-bytes\trecords-bytes\tfirst difference (GUIDs replaced by their ordinals)");
            }
            string one(string s) => s == null ? "" : s.Replace("\r", "").Replace('\n', ' ').Replace('\t', ' ');
            if (datum == null) { movedIndex.WriteLine($"{status}\t{key}\t\t\t\t\t{one(difference)}"); return; }
            var (kind, data, records) = Archive(datum); var t = Deflate(text);
            movedPack.Write(key); movedPack.Write(kind); movedPack.Write(Sha(text));
            movedPack.Write(data?.Length ?? -1); if (data != null) movedPack.Write(data);
            movedPack.Write(records?.Length ?? -1); if (records != null) movedPack.Write(records);
            movedPack.Write(t.Length); movedPack.Write(t);
            movedIndex.WriteLine($"{status}\t{key}\t{kind}\t{Sha(text)}\t{data?.Length ?? -1}\t{records?.Length ?? -1}\t{one(difference)}");
        }

        internal Session(string source, string folder, bool writing)
        {
            this.source = source; this.folder = folder; Writing = writing;
            string packPath = Path.Combine(folder, source + ".bgcap");
            if (writing)
            {
                Directory.CreateDirectory(folder);
                if (File.Exists(packPath)) throw new IOException("Capture already written (never overwritten): " + packPath);
                pack = new BinaryWriter(new FileStream(packPath, FileMode.CreateNew), Encoding.UTF8);
                pack.Write(Header);
                index = new StreamWriter(Path.Combine(folder, source + ".tsv"), false, new UTF8Encoding(false));
                index.WriteLine("key\tkind\ttext-sha256\tdata-bytes\trecords-bytes");
            }
            else
            {
                reference = new Dictionary<string, Entry>(StringComparer.Ordinal);
                using var r = new BinaryReader(File.OpenRead(packPath), Encoding.UTF8);
                if (!r.ReadBytes(Header.Length).SequenceEqual(Header)) throw new InvalidDataException("Not a capture: " + packPath);
                while (r.BaseStream.Position < r.BaseStream.Length)
                {
                    string key = r.ReadString();
                    var e = new Entry { Kind = r.ReadString(), Sha = r.ReadString() };
                    int n = r.ReadInt32(); e.Data = n < 0 ? null : r.ReadBytes(n);
                    n = r.ReadInt32(); e.Records = n < 0 ? null : r.ReadBytes(n);
                    n = r.ReadInt32(); e.Text = r.ReadBytes(n);
                    reference.Add(key, e);
                }
                // P2CF (13/11, RT-02): the keys retired by name against the capture, before any exemption.
                P2rRetired.ValidateCapture(source, reference.ContainsKey);
                // R6.3: the digests of the reference mapped to the fingerprints of their GPC properties (declared signature rule).
                foreach (var p in reference)
                {
                    try { MapSignatures(p.Key, p.Value.Kind, p.Value.Data, p.Value.Records); }
                    catch (InvalidDataException) { throw; }
                    catch (Exception) { /* not read back: counted by the loop below */ }
                }
                // (i) on the frozen bytes: every captured entry read back by the current final reader gives the captured text.
                foreach (var p in reference)
                {
                    readBack++;
                    string text = null;
                    string capturedText = Inflate(p.Value.Text);
                    if (p.Key.EndsWith("|" + p.Value.Sha, StringComparison.Ordinal))
                    {
                        var (declaredText, names) = WithDeclaredDigests(capturedText, p.Key);
                        if (names.Count > 0) declaredKeys[p.Key.Substring(0, p.Key.Length - p.Value.Sha.Length) + Sha(declaredText)] = p.Key;
                    }
                    try { text = ReadBack(p.Value.Kind, p.Value.Data, p.Value.Records, capturedText); }
                    catch (Exception x) { readBackFailures++; if (failures.Count < 30) failures.Add($"{p.Key}: captured {p.Value.Kind} not read back: {x.GetType().Name}: {x.Message}"); continue; }
                    if (Sha(text) != p.Value.Sha) { readBackFailures++; if (failures.Count < 30) failures.Add($"{p.Key}: captured {p.Value.Kind} read back to another text\n" + GpcDraftArchiveChecks.FirstDifference(Inflate(p.Value.Text), text)); }
                }
            }
        }

        // Diagnostic (P2N, R6.3): BRIDGHE_CAPTURE_BRIDGE_DIFF=<folder> writes the bridge data of the first differing conversions, now and as
        // captured (read back), for a comparison of their XML.
        private int bridgeDumps;
        private void DumpBridge(object datum, Entry e)
        {
            string dir = Environment.GetEnvironmentVariable("BRIDGHE_CAPTURE_BRIDGE_DIFF");
            if (string.IsNullOrEmpty(dir) || bridgeDumps >= 2 || !(datum is GpcModels.Conversion c) || e.Kind != "conversion") return;
            bridgeDumps++;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, bridgeDumps + "-now.xml"), c.Report.Bridge.Save());
            File.WriteAllBytes(Path.Combine(dir, bridgeDumps + "-captured.xml"), GpcDraftArchive.ReadConversion(e.Data, e.Records).Report.Bridge.Save());
        }

        private void Count(string what) { counts.TryGetValue(what, out int c); counts[what] = c + 1; }

        // The declared differences by reason (counted), every entry whose reason carries a detail (a renamed owner) listed, the others up to 5 by reason.
        private readonly Dictionary<string, int> declaredReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        private void Declare(string key, string why)
        {
            int cut = why.IndexOf("; owner identity", StringComparison.Ordinal);
            string reason = cut < 0 ? why : why.Substring(0, cut) + "; owner GUIDs renamed";
            declaredReasons.TryGetValue(reason, out int c); declaredReasons[reason] = c + 1;
            if (cut >= 0 || c < 5) declared.Add(key + ": " + why);
        }

        /// <summary>Records one datum at a location; the n-th repetition of a location takes the key <c>location#n</c>.</summary>
        internal void Record(string location, object datum)
        {
            occurrences.TryGetValue(location, out int n); occurrences[location] = ++n;
            string key = n == 1 ? location : location + "#" + n;
            string text = Text(datum);
            if (Writing)
            {
                var (kind, data, records) = Archive(datum);
                var t = Deflate(text);
                pack.Write(key); pack.Write(kind); pack.Write(Sha(text));
                pack.Write(data?.Length ?? -1); if (data != null) pack.Write(data);
                pack.Write(records?.Length ?? -1); if (records != null) pack.Write(records);
                pack.Write(t.Length); pack.Write(t);
                index.WriteLine($"{key}\t{kind}\t{Sha(text)}\t{data?.Length ?? -1}\t{records?.Length ?? -1}");
                entries++; bytes += (data?.Length ?? 0) + (records?.Length ?? 0) + t.Length; Count(kind);
                return;
            }
            seen.Add(key);
            // P2R (U3, 12/11): an entry retired by name (P2rRetired.CaptureSources) is not compared, seen or not.
            // P2CF (13/11, RT-02): by the full key alone; a key not listed is compared (a new one fails).
            if (P2rRetired.CaptureSources(source, key) is string retiredBecause) { Count("retired"); declaredReasons[retiredBecause] = declaredReasons.GetValueOrDefault(retiredBecause) + 1; return; }
            if (!reference.TryGetValue(key, out var e))
            {
                // A content key that a declared digest moved: compared with the captured entry of the former key.
                if (declaredKeys.TryGetValue(key, out var former) && !seen.Contains(former))
                {
                    seen.Add(former);
                    if (Declared(Inflate(reference[former].Text), text, out string because, former)) { Count("declared"); Declare(former + " -> " + key, because); return; }
                }
                Count("new"); if (failures.Count < 30) failures.Add(key + ": not in the capture (" + datum.GetType().Name + ")"); Moved("new", key, datum, text, null); return;
            }
            if (Sha(text) == e.Sha) { Count("equal"); return; }
            string captured = Inflate(e.Text);
            DumpBridge(datum, e);
            if (WithoutGuids(captured) == WithoutGuids(text)) { Count("equal but for GUIDs"); if (identityOnly.Count < 30) identityOnly.Add(key); return; }
            if (Declared(captured, text, out string why, key)) { Count("declared"); Declare(key, why); return; }
            // R6.3: a Build Model output (SharedCompositeRule): the GUIDs renamed, the bridge-data fingerprint changed, the bridge values equal.
            var bridgeNow = datum is GpcModels.Conversion cn && e.Kind == "conversion" ? cn.Report.Bridge
                : datum is GpcDraftArchiveChecks.ModelDatum mn && e.Kind == "model" ? mn.Model.Data : null;
            if (bridgeNow != null && OnlyBridgeFingerprint(WithoutGuids(WithDeclaredDigests(captured, key).Text), WithoutGuids(text), out string renamed)
                && BridgeValues(bridgeNow).SequenceEqual(BridgeValues(e.Kind == "conversion" ? GpcDraftArchive.ReadConversion(e.Data, e.Records).Report.Bridge
                    : GpcDraftArchive.ReadModel(e.Data, e.Records).Item1.Data), StringComparer.Ordinal))
            { Count("declared"); Declare(key, SharedCompositeRule + "; owner identity from the content of the drafts: " + renamed); return; }
            // P2F (12/11): a beam draft whose GUIDs the GPC path of P2K derives (BeamGuidRule).
            if (datum is LegacyBeamDraft beamNow && e.Kind == "beam" && OnlyBeamBridgeLine(WithoutGuids(WithDeclaredDigests(captured, key).Text), WithoutGuids(text))
                && GpcDraftArchive.ReadBeam(e.Data, e.Records) is var beamThen
                && BridgeValues(OneBeam(beamNow.Bridge)).SequenceEqual(BridgeValues(OneBeam(beamThen.Bridge)), StringComparer.Ordinal)
                && BridgeValues(beamNow.Report?.Bridge ?? OneBeam(null)).SequenceEqual(BridgeValues(beamThen.Report?.Bridge ?? OneBeam(null)), StringComparer.Ordinal))
            { Count("declared"); Declare(key, BeamGuidRule); return; }
            Count("different"); if (failures.Count < 30) failures.Add(key + ": differs from the capture (GUIDs replaced by their ordinals)\n" + GpcDraftArchiveChecks.FirstDifference(WithoutGuids(captured), WithoutGuids(text)));
            Moved("different", key, datum, text, GpcDraftArchiveChecks.FirstDifference(WithoutGuids(captured), WithoutGuids(text)));
        }

        public void Dispose()
        {
            if (Writing)
            {
                pack.Dispose(); index.Dispose();
                Console.WriteLine($"CAPTURE WRITE {source}: {entries} entries ({string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + " " + c.Value))}), {bytes / 1048576.0:0.0} MB in {folder}");
                return;
            }
            // P2R (U3, 12/11; before, P2S of 7/11 for the five examples of ExampleRemake04): the entries retired by name (P2rRetired.CaptureSources:
            // the memory sources of the corpus and the 7 examples remade after P2) are not required (the capture is not regenerated).
            bool Remade(string k) => P2rRetired.CaptureSources(source, k) != null;
            int retired = reference.Keys.Count(k => !seen.Contains(k) && Remade(k));
            // P2CF (13/11, RT-02): a key declared new (a payload of a remade example, not in the capture) that the run does not record is an
            // unused declaration.
            var unusedNew = P2rRetired.NewCaptureKeys(source).Where(k => !seen.Contains(k)).ToList();
            foreach (var k in unusedNew) failures.Add(k + ": declared new by name (P2CF), not recorded now (unused declaration)");
            if (retired > 0) { declaredReasons["retired by name, not recorded now (P2R, U3 12/11)"] = retired; counts["retired"] = (counts.TryGetValue("retired", out int dc) ? dc : 0) + retired; }
            int missing = reference.Keys.Count(k => !seen.Contains(k) && !Remade(k));
            foreach (var k in reference.Keys.Where(k => !seen.Contains(k) && !Remade(k)).Take(Math.Max(0, 30 - failures.Count))) failures.Add(k + ": captured, not recorded now");
            foreach (var k in reference.Keys.Where(k => !seen.Contains(k) && !Remade(k))) Moved("missing", k, null, null, "captured, not recorded now");
            movedPack?.Dispose(); movedIndex?.Dispose();
            foreach (var f in failures) Console.WriteLine("  " + f);
            foreach (var k in identityOnly) Console.WriteLine("  GUIDs only: " + k);
            foreach (var d in declared) Console.WriteLine("  declared: " + d);
            foreach (var r in declaredReasons.OrderBy(r => r.Key, StringComparer.Ordinal)) Console.WriteLine($"  DECLARED {source}: {r.Value} x {r.Key}");
            counts.TryGetValue("different", out int different); counts.TryGetValue("new", out int added);
            bool failed = different != 0 || added != 0 || missing != 0 || readBackFailures != 0 || unusedNew.Count != 0;
            if (failed) Failed = true;
            Console.WriteLine($"CAPTURE COMPARE {source}: {string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + " " + c.Value))}, {missing} missing; {readBack} captured entries read back by the final reader ({readBackFailures} failures){(failed ? " FAIL" : "")}");
        }
    }
}
