using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;

/// <summary>
/// R6.6 lane R3 (11/11): the 0.3.0 models of the tests read as bytes, without the FEM classes (no <c>ModelArchive.Read</c>, no FEM writer):
/// <list type="bullet">
/// <item>the 77 FEM inputs captured from the former exporter (<c>P2B/results/capture-1/*.bgmodel</c>, BGHA1 as the former writer stored them,
/// with the tolerance of their <c>.txt</c>);</item>
/// <item>the examples 01–10 captured with it (<c>P2B/results/examples-1/*/input.bgmodel</c>);</item>
/// <item>the frozen payloads of <see cref="LegacyPayloadFixtures"/> that the import-dump corpus built in memory before (same names: node,
/// beam and plate data in every unit system, the refused duplicates), size and SHA-256 checked. The FEM writer gave the same bytes for
/// them (<c>GpcLegacyImportTests.Fem.cs</c>, parity checked while the FEM classes exist).</item>
/// </list>
/// The ids are those of the former corpus, so the outputs compare with the frozen references (<c>p2k-import-ref</c>, the R6.0 capture).
/// </summary>
internal static class R3LegacyCorpus
{
    internal const string Results = @"C:\Users\g.pacini\Documents\Repository\gpc-f6-claude\P2B\results";

    internal sealed record Item(string Id, byte[] Bytes, double Tolerance, string Fixture)
    {
        private LegacyValue root;
        /// <summary>The archive read as data (once).</summary>
        internal LegacyValue Root => root ??= LegacyArchive.Read(Bytes, long.MaxValue);
        /// <summary>The former section-state signatures of the beams (<see cref="Signatures"/>), null when every state is current.</summary>
        internal Func<int, string> Signatures => R3LegacyCorpus.Signatures(this);
        /// <summary>True when the former model has moving paths (they come after Build Model, from Attach Moving Paths).</summary>
        internal bool HasMovingPaths => Root.Has("MovingPaths") && Root.String("MovingPaths") is string s && s.Trim() != "[]" && s.Trim().Length != 0;
    }

    /// <summary>The captured FEM inputs (<c>capture-</c> + file name), in the order of the former corpus.</summary>
    internal static IEnumerable<Item> Captures()
    {
        string folder = Path.Combine(Results, "capture-1");
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("R3 LEGACY CORPUS FAIL: no captures in " + folder);
        foreach (var file in Directory.GetFiles(folder, "*.bgmodel").OrderBy(f => f, StringComparer.Ordinal))
        {
            string info = Path.ChangeExtension(file, ".txt");
            double tolerance = File.Exists(info) ? double.Parse(File.ReadAllLines(info)[1], CultureInfo.InvariantCulture) : 1e-6;
            yield return new Item("capture-" + Path.GetFileNameWithoutExtension(file), File.ReadAllBytes(file), tolerance, null);
        }
    }

    /// <summary>The captured examples 01–10 (<c>examples-</c> + folder + file name), tolerance 1E-6 as the former corpus.</summary>
    internal static IEnumerable<Item> Examples()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Results, "examples-1"), "*.bgmodel", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            yield return new Item("examples-" + Path.GetFileName(Path.GetDirectoryName(file)) + "-" + Path.GetFileNameWithoutExtension(file), File.ReadAllBytes(file), 1e-6, null);
    }

    /// <summary>The unit systems of the frozen payloads, in the order of the former enumeration (<c>ModelModel.ModelUnits</c>: Knm, SI, Nmm, IPS).</summary>
    internal static readonly string[] Units = { "Knm", "SI", "Nmm", "IPS" };

    /// <summary>The frozen payloads of the former in-memory corpus of the import dump (tolerance 1E-3, as it was): per unit system the node
    /// data, the labelled node with a rigid link, the beam and plate data; then the refused duplicates.</summary>
    internal static IEnumerable<Item> Fixtures(bool nodes = true, bool labelled = true, bool beams = true, bool plates = true, bool duplicates = true)
    {
        foreach (var u in Units)
        {
            if (nodes) yield return Fixture("nodes-" + u);
            if (labelled) yield return Fixture("nodes-labelled-rigid-" + u);
        }
        foreach (var u in Units)
        {
            if (beams) yield return Fixture("beams-" + u);
            if (plates) yield return Fixture("plates-" + u);
        }
        if (duplicates) foreach (var d in new[] { "releases", "taper", "super-elements", "members", "prestrain" }) yield return Fixture("beams-duplicate-" + d);
    }

    internal static Item Fixture(string name) => new Item(name, LegacyPayloadFixtures.Bytes(name), 1e-3, name);

    /// <summary>
    /// The former section-state signatures of the beams of a 0.3.0 model (the reader marks a state stale when its signature is not that of the
    /// property of its beam). The signature of 0.3.0 (<c>BridgeSectionAssignment.Signature</c>: the SHA-256 of the FEM archive of ten property
    /// values) needs the FEM writer; without it:
    /// <list type="bullet">
    /// <item>a model without section states: null (nothing to tell, every state current);</item>
    /// <item>the frozen beam data (<c>beams-*</c>, <c>beams-duplicate-*</c>): by the definition of the fixture (<c>ImportDump.BeamData</c>) every
    /// beam has the property «PI», and the first state of beam 2 was written with the 0.3.0 signature of «PI» (beam 3: «old», stale): the
    /// signature of every beam is the one stored there;</item>
    /// <item>any other model with section states: refused (no such model in the corpus: the captures and examples have none).</item>
    /// </list>
    /// </summary>
    internal static Func<int, string> Signatures(Item item)
    {
        var beams = item.Root.Field("Elements").Items.Where(e => e.Is("BeamModel")).ToArray();
        static IEnumerable<LegacyValue> States(LegacyValue b) => b.Field("Attributes").Items.Where(a => a.Is("BridgeSectionStateAttributeModel"));
        if (!beams.Any(b => States(b).Any())) return null;
        if (item.Fixture == null || !item.Fixture.StartsWith("beams-", StringComparison.Ordinal))
            throw new NotSupportedException("R3 LEGACY CORPUS: " + item.Id + " has section states and no known former signature (the corpus has none outside the frozen beam data)");
        if (beams.Length < 2 || beams.Any(b => b.Field("BeamProperty").String("Name") != "PI"))
            throw new InvalidDataException("R3 LEGACY CORPUS: " + item.Id + " is not the frozen beam data of ImportDump.BeamData (every beam on «PI»)");
        string signature = States(beams[1]).First().Field("SectionAssignment").String("PropertySignature");
        if (signature == null || signature.Length != 64 || !signature.All(Uri.IsHexDigit))
            throw new InvalidDataException("R3 LEGACY CORPUS: " + item.Id + ": the first state of beam 2 does not hold a 0.3.0 signature: " + signature);
        return _ => signature;
    }
}

/// <summary>
/// R6.6 lane R3: the identities the FEM path gave the data of the corpus, read from the frozen R6.0 capture (<c>corpus</c>, never rewritten),
/// so that the same data read by value compare with it exactly. The FEM path named a draft by the digest of the archive of its FEM value on its
/// own (<c>GpcBeamDrafts.Identity</c>) and a model by the digest of the archive of the FEM model written again (<c>GpcModels.Convert</c>):
/// archives the FEM writer makes, which a 0.3.0 model read by value does not have. Here: the GUID of every captured node, beam and plate draft
/// (by key), and the owner of every captured conversion (the line <c>OWNER</c> of its text). Only with <c>BRIDGHE_CAPTURE=compare:</c>.
/// </summary>
internal sealed class R3CaptureIdentities
{
    private readonly Dictionary<string, Guid> guids = new Dictionary<string, Guid>(StringComparer.Ordinal);
    internal int Count => guids.Count;

    /// <summary>The identities of the capture of <paramref name="source"/> in compare mode, else null.</summary>
    internal static R3CaptureIdentities Open(string source)
    {
        string setting = Environment.GetEnvironmentVariable(GpcDraftCapture.Variable);
        if (string.IsNullOrWhiteSpace(setting) || !setting.StartsWith("compare:", StringComparison.Ordinal)) return null;
        var result = new R3CaptureIdentities();
        string pack = Path.Combine(setting.Substring("compare:".Length), source + ".bgcap");
        using var r = new BinaryReader(File.OpenRead(pack), System.Text.Encoding.UTF8);
        if (System.Text.Encoding.ASCII.GetString(r.ReadBytes(6)) != "BGCAP1") throw new InvalidDataException("R3 CAPTURE IDENTITIES: not a capture: " + pack);
        var owner = new System.Text.RegularExpressions.Regex("\nOWNER ([0-9a-fA-F-]{36})\n");
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            string key = r.ReadString(), kind = r.ReadString(); r.ReadString();
            int n = r.ReadInt32(); byte[] data = n < 0 ? null : r.ReadBytes(n);
            n = r.ReadInt32(); byte[] records = n < 0 ? null : r.ReadBytes(n);
            n = r.ReadInt32(); byte[] text = r.ReadBytes(n);
            switch (kind)
            {
                case "node": result.guids[key] = BridGHe.Common.Gpc.GpcDraftArchive.ReadNode(data, records).Node.Guid; break;
                case "beam": result.guids[key] = BridGHe.Common.Gpc.GpcDraftArchive.ReadBeam(data, records).Beam.Guid; break;
                case "plate": result.guids[key] = BridGHe.Common.Gpc.GpcDraftArchive.ReadPlate(data, records).Plate.Guid; break;
                case "conversion":
                {
                    using var input = new MemoryStream(text); using var d = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
                    using var s = new StreamReader(d, System.Text.Encoding.UTF8);
                    var m = owner.Match(s.ReadToEnd());
                    if (!m.Success) throw new InvalidDataException("R3 CAPTURE IDENTITIES: no owner in the captured conversion " + key);
                    result.guids[key] = Guid.Parse(m.Groups[1].Value); break;
                }
            }
        }
        return result;
    }

    /// <summary>The captured identity of the datum at <paramref name="key"/>, or <paramref name="otherwise"/> (not captured: the comparison
    /// reports the key as new).</summary>
    internal Guid Of(string key, Guid otherwise) => guids.TryGetValue(key, out var g) ? g : otherwise;
}
