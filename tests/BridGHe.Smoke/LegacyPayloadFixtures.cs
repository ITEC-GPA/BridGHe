#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;

/// <summary>
/// R6.6 (P2U, coordinator decision of 6/11, point 1): the frozen BGHA1 payloads of the tests in <c>tests/BridGHe.Smoke/Fixtures/legacy-payloads</c>,
/// written once by the FEM writer while it existed (<c>LegacyPayloadFixtures.Fem.cs</c>; commit, generator and SHA-256 of every file in
/// <c>PROVENIENZA.md</c>) and never regenerated: the former FEM models the tests built (every unit system, the refusals of the reader), read
/// as data with no FeMM class. <c>index.tsv</c>: name, units, bytes, SHA-256, the outcome of the reader when they were written;
/// <c>units-axes.tsv</c>: the former local axes of the beams of the units model.
/// </summary>
internal static partial class LegacyPayloadFixtures
{
    /// <summary>The folder of the fixtures (from the root of the BridGHe checkout, the working folder of the Smoke runs).</summary>
    internal static string Folder => Path.GetFullPath(Path.Combine("tests", "BridGHe.Smoke", "Fixtures", "legacy-payloads"));

    internal sealed record Entry(string Name, string Units, int Bytes, string Sha256, string Outcome);

    private static List<Entry> index;

    /// <summary>The fixtures in the order of the index.</summary>
    internal static IReadOnlyList<Entry> Index()
    {
        if (index != null) return index;
        string path = Path.Combine(Folder, "index.tsv");
        if (!File.Exists(path)) throw new FileNotFoundException("LEGACY PAYLOAD FIXTURES FAIL: no index (" + path + ")");
        index = File.ReadLines(path).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t'))
            .Select(c => new Entry(c[0], c[1], int.Parse(c[2], CultureInfo.InvariantCulture), c[3], c[4])).ToList();
        return index;
    }

    /// <summary>The bytes of a fixture, checked against the SHA-256 and the size of the index.</summary>
    internal static byte[] Bytes(string name)
    {
        var e = Index().SingleOrDefault(x => x.Name == name) ?? throw new Exception("LEGACY PAYLOAD FIXTURES FAIL: no fixture " + name);
        var bytes = File.ReadAllBytes(Path.Combine(Folder, name + ".bgha1"));
        if (bytes.Length != e.Bytes || Convert.ToHexString(SHA256.HashData(bytes)) != e.Sha256) throw new Exception("LEGACY PAYLOAD FIXTURES FAIL: " + name + " differs from its SHA-256 in the index");
        return bytes;
    }

    /// <summary>The former local axes (BeamModel.GetLocalAxes) of a beam of the units model, by their bits.</summary>
    internal static ((double, double, double) V1, (double, double, double) V2, (double, double, double) V3) Axes(string units, int beam)
    {
        var line = File.ReadLines(Path.Combine(Folder, "units-axes.tsv")).Skip(1).Select(l => l.Split('\t')).Single(c => c[0] == units && c[1] == beam.ToString(CultureInfo.InvariantCulture));
        (double, double, double) V(string s) { var b = s.Split(' ').Select(x => BitConverter.Int64BitsToDouble(long.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture))).ToArray(); return (b[0], b[1], b[2]); }
        return (V(line[2]), V(line[3]), V(line[4]));
    }

    /// <summary>The outcome of the reader of this branch on a payload: <c>ACCEPTED</c> or <c>REFUSED &lt;type&gt;: &lt;message&gt;</c> (the
    /// errors of the reader: argument, not supported, invalid operation, serialization).</summary>
    internal static string Outcome(string name, byte[] payload)
    {
        try
        {
            LegacyModelImport.Import(LegacyArchive.Read(payload), GpcIdentity.Derive(GpcIdentity.NameSpace, "fixture:" + name), 1e-3, new LegacyImportReport());
            return "ACCEPTED";
        }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is InvalidOperationException || e is System.Runtime.Serialization.SerializationException)
        { return "REFUSED " + e.GetType().Name + ": " + e.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    }
}
#endif
