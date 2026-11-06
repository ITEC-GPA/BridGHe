#if GPC_PILOT
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
        Refused r => "REFUSED " + r.Kind + " " + r.Message,
        Nothing n => "NOTHING " + n.Kind + " " + n.Why,
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
        private readonly List<string> failures = new List<string>(), identityOnly = new List<string>();
        private int entries, readBack, readBackFailures; private long bytes;

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
                // (i) on the frozen bytes: every captured entry read back by the current final reader gives the captured text.
                foreach (var p in reference)
                {
                    readBack++;
                    string text = null;
                    try { text = ReadBack(p.Value.Kind, p.Value.Data, p.Value.Records, Inflate(p.Value.Text)); }
                    catch (Exception x) { readBackFailures++; if (failures.Count < 30) failures.Add($"{p.Key}: captured {p.Value.Kind} not read back: {x.GetType().Name}: {x.Message}"); continue; }
                    if (Sha(text) != p.Value.Sha) { readBackFailures++; if (failures.Count < 30) failures.Add($"{p.Key}: captured {p.Value.Kind} read back to another text\n" + GpcDraftArchiveChecks.FirstDifference(Inflate(p.Value.Text), text)); }
                }
            }
        }

        private void Count(string what) { counts.TryGetValue(what, out int c); counts[what] = c + 1; }

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
            if (!reference.TryGetValue(key, out var e)) { Count("new"); if (failures.Count < 30) failures.Add(key + ": not in the capture (" + datum.GetType().Name + ")"); return; }
            if (Sha(text) == e.Sha) { Count("equal"); return; }
            string captured = Inflate(e.Text);
            if (WithoutGuids(captured) == WithoutGuids(text)) { Count("equal but for GUIDs"); if (identityOnly.Count < 30) identityOnly.Add(key); return; }
            Count("different"); if (failures.Count < 30) failures.Add(key + ": differs from the capture (GUIDs replaced by their ordinals)\n" + GpcDraftArchiveChecks.FirstDifference(WithoutGuids(captured), WithoutGuids(text)));
        }

        public void Dispose()
        {
            if (Writing)
            {
                pack.Dispose(); index.Dispose();
                Console.WriteLine($"CAPTURE WRITE {source}: {entries} entries ({string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + " " + c.Value))}), {bytes / 1048576.0:0.0} MB in {folder}");
                return;
            }
            int missing = reference.Keys.Count(k => !seen.Contains(k));
            foreach (var k in reference.Keys.Where(k => !seen.Contains(k)).Take(Math.Max(0, 30 - failures.Count))) failures.Add(k + ": captured, not recorded now");
            foreach (var f in failures) Console.WriteLine("  " + f);
            foreach (var k in identityOnly) Console.WriteLine("  GUIDs only: " + k);
            counts.TryGetValue("different", out int different); counts.TryGetValue("new", out int added);
            bool failed = different != 0 || added != 0 || missing != 0 || readBackFailures != 0;
            if (failed) Failed = true;
            Console.WriteLine($"CAPTURE COMPARE {source}: {string.Join(", ", counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + " " + c.Value))}, {missing} missing; {readBack} captured entries read back by the final reader ({readBackFailures} failures){(failed ? " FAIL" : "")}");
        }
    }
}
#endif
