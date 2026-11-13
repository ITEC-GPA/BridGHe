using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

/// <summary>
/// P2CC (13/11/2026, user decision «Congela», 12:35): the frozen 0.3.0 payloads of the in-memory FEM models, beam properties and beams of Lane 3d
/// and of the audits on the examples 02 and 03 of <see cref="GpcOnlyReplay"/>, in <c>tests/BridGHe.Smoke/Fixtures/lane3d-payloads</c>, in the form
/// of <c>legacy-payloads</c>: written once by the former FEM writer (generator: commit 38f7e05, <c>BRIDGHE_P2CC_FIXTURES=write:&lt;folder&gt;
/// --regression gpc-only-models</c>; see its PROVENIENZA.md), each read back by value equal to its FEM value, and never regenerated.
/// <c>index.tsv</c>: file, units, bytes, SHA-256, outcome. Read as data, with no FeMM class.
/// </summary>
internal static class Lane3dPayloads
{
    /// <summary>The folder of the fixture (from the root of the BridGHe checkout, the working folder of the Smoke runs).</summary>
    internal static string Folder => Path.GetFullPath(Path.Combine("tests", "BridGHe.Smoke", "Fixtures", "lane3d-payloads"));

    internal sealed record Entry(string Name, string Units, int Bytes, string Sha256, string Outcome);

    private static List<Entry> index;

    /// <summary>The files of the fixture in the order of the index.</summary>
    internal static IReadOnlyList<Entry> Index()
    {
        if (index != null) return index;
        string path = Path.Combine(Folder, "index.tsv");
        if (!File.Exists(path)) throw new FileNotFoundException("LANE 3D PAYLOADS FAIL: no index (" + path + ")");
        index = File.ReadLines(path).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t'))
            .Select(c => new Entry(c[0], c[1], int.Parse(c[2], CultureInfo.InvariantCulture), c[3], c[4])).ToList();
        return index;
    }

    /// <summary>The bytes of a payload of the fixture (<c>name.bgha1</c>), checked against the size and the SHA-256 of the index.</summary>
    internal static byte[] Bytes(string name)
    {
        var e = Index().SingleOrDefault(x => x.Name == name) ?? throw new Exception("LANE 3D PAYLOADS FAIL: no file " + name);
        var bytes = File.ReadAllBytes(Path.Combine(Folder, name + ".bgha1")); // the index names the payloads without their extension
        if (bytes.Length != e.Bytes || Convert.ToHexString(SHA256.HashData(bytes)) != e.Sha256) throw new Exception("LANE 3D PAYLOADS FAIL: " + name + " differs from its SHA-256 in the index");
        return bytes;
    }
}
