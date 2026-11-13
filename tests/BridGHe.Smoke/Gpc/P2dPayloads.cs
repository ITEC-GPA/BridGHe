using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

/// <summary>
/// P2CA (13/11/2026, user decision «Sì, congelale», about 07:00): the frozen payloads of the checks Development and Construction of
/// <see cref="P2dChecks"/> in <c>tests/BridGHe.Smoke/Fixtures/p2d-payloads</c>, in the form of <c>legacy-payloads</c>: the BGHA1 payloads the
/// former FEM writer gave for the elements, the material and the models of the checks, and the BGHGPC1 datums of the GPC conversions they used,
/// written once (generator: commit d922b94, <c>BRIDGHE_P2D_FIXTURES=write:&lt;folder&gt; --regression p2d</c>; see its PROVENIENZA.md) and
/// never regenerated. <c>index.tsv</c>: file, units, bytes, SHA-256, outcome. Read as data, with no FeMM class.
/// </summary>
internal static class P2dPayloads
{
    /// <summary>The folder of the fixture (from the root of the BridGHe checkout, the working folder of the Smoke runs).</summary>
    internal static string Folder => Path.GetFullPath(Path.Combine("tests", "BridGHe.Smoke", "Fixtures", "p2d-payloads"));

    internal sealed record Entry(string Name, string Units, int Bytes, string Sha256, string Outcome);

    private static List<Entry> index;

    /// <summary>The files of the fixture in the order of the index.</summary>
    internal static IReadOnlyList<Entry> Index()
    {
        if (index != null) return index;
        string path = Path.Combine(Folder, "index.tsv");
        if (!File.Exists(path)) throw new FileNotFoundException("P2D PAYLOADS FAIL: no index (" + path + ")");
        index = File.ReadLines(path).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t'))
            .Select(c => new Entry(c[0], c[1], int.Parse(c[2], CultureInfo.InvariantCulture), c[3], c[4])).ToList();
        return index;
    }

    /// <summary>The bytes of a file of the fixture, checked against the size and the SHA-256 of the index.</summary>
    internal static byte[] Bytes(string name)
    {
        var e = Index().SingleOrDefault(x => x.Name == name) ?? throw new Exception("P2D PAYLOADS FAIL: no file " + name);
        var bytes = File.ReadAllBytes(Path.Combine(Folder, name));
        if (bytes.Length != e.Bytes || Convert.ToHexString(SHA256.HashData(bytes)) != e.Sha256) throw new Exception("P2D PAYLOADS FAIL: " + name + " differs from its SHA-256 in the index");
        return bytes;
    }
}
