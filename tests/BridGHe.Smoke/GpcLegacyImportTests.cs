#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.LoadCases;
using G = GPC.Geometry;

/// <summary>F6A conversions: former FEM payloads (BGHA1) read as data and converted to GPC with the semantics of the former
/// Straus7 exporter.
/// <para>R6.6 (P2U): this file has no FeMM class. The load schemas are GPC only; the GH data archive takes its former payloads from the
/// example definitions saved by BridGHe 0.3.0 (the locations the R6.0 capture <c>payloads</c> lists). The conversions of the former FEM
/// models built in the test (N-mm and kN-m, data no example payload has) are in <c>GpcLegacyImportTests.Fem.cs</c>, until the decision on
/// their payloads (the example definitions and the frozen corpus have N-mm models only).</para></summary>
internal static partial class GpcLegacyImportTests
{
    private static int checks;
    private static void Assert(bool condition, string name) { if (!condition) throw new Exception("GPC LEGACY IMPORT FAIL: " + name); checks++; }
    private static bool Near(double a, double b, double tolerance = 1e-12) => Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    /// <summary>The conversions of the former FEM models built in the test (<c>GpcLegacyImportTests.Fem.cs</c>); the call disappears with that file.</summary>
    static partial void FemCases();

    internal static void Run()
    {
        checks = 0;
        FemCases();
        Schemas();
        Archives();
        Console.WriteLine($"GPC LEGACY IMPORT PASS: {checks} checks (units, nodes, restraint frames, rigid links, beam axes, I order, sections, loads, cases, combinations, write plan).");
    }

    /// <summary>The first BGHA1 payload of the example definition <paramref name="file"/> (relative to the example folder) whose root is a
    /// <paramref name="rootType"/>, as BridGHe 0.3.0 saved it, with its location; the R6.0 capture <c>payloads</c> must list that location.</summary>
    internal static (byte[] Bytes, string Location) ExamplePayload(string file, string rootType)
    {
        var archive = new GH_IO.Serialization.GH_Archive();
        string path = Path.Combine(ExamplePaths.Root, file);
        if (!archive.ReadFromFile(path)) throw new Exception("GPC LEGACY IMPORT FAIL: example not readable: " + path);
        byte[] found = null; string location = null;
        void Walk(GH_IO.Serialization.GH_IChunk chunk, string where)
        {
            foreach (var item in chunk.Items)
            {
                if (found != null || item.Type != GH_IO.Types.GH_Types.gh_bytearray || !LegacyArchive.IsLegacy(item._bytearray)) continue;
                if (LegacyArchive.Read(item._bytearray).ShortType == rootType) { found = item._bytearray; location = file + ":" + where + "/" + item.Name; }
            }
            foreach (var c in chunk.Chunks) if (found == null) Walk(c, where + "/" + c.Name + (c.Index > 0 ? "[" + c.Index + "]" : ""));
        }
        Walk(archive.GetRootNode, "");
        if (found == null) throw new Exception($"GPC LEGACY IMPORT FAIL: no {rootType} payload in {file}");
        string index = Path.Combine(GpcCompositeSectionChecks.CaptureFolder(), "payloads.tsv");
        if (!File.ReadLines(index).Any(l => l.StartsWith(location + "\t", StringComparison.Ordinal))) throw new Exception($"GPC LEGACY IMPORT FAIL: {location} is not in the R6.0 capture ({index})");
        return (found, location);
    }

    /// <summary>GH data archive (BGHGPC1): GPC values and models round trip with their fingerprint; legacy payloads and foreign or
    /// corrupted bytes are recognised and refused, never read as GPC data. The former payloads: a steel material of example 19 and the
    /// internalized model of example 01, as BridGHe 0.3.0 saved them.</summary>
    private static void Archives()
    {
        var material = new GPC.Model.Materials.Material("S355", 210000, 0.3, 7.85e-9, 1.2e-5);
        var bytes = GpcGooArchive.Value(material); var copy = GpcGooArchive.ReadValue<GPC.Model.Materials.Material>(bytes);
        Assert(GpcGooArchive.IsGpc(bytes) && !GpcGooArchive.IsLegacy(bytes), "GH archive header");
        Assert(copy.Name == "S355" && copy.E == 210000 && copy.Ni == 0.3 && copy.Density == 7.85e-9 && copy.AlfaThermalExpansion == 1.2e-5, "GH archive: material values");
        Assert(GPC.Model.Core.ModelValues.Fingerprint(new object[] { copy }) == GPC.Model.Core.ModelValues.Fingerprint(new object[] { material }), "GH archive: material fingerprint");
        var (saved, where) = ExamplePayload(Path.Combine("01-cantilever", "01-cantilever.ghx"), "ModelModel");
        var tree = LegacyArchive.Read(saved);
        Assert(tree.Is("ModelModel"), "legacy payload read as data (" + where + ")");
        var model = LegacyModelImport.Import(tree, GpcIdentity.Derive(GpcIdentity.NameSpace, "test"), 1e-3, new LegacyImportReport());
        var modelBytes = GpcGooArchive.Model(model); var reopened = GpcGooArchive.ReadModel(modelBytes);
        Assert(model.NodesElements.Count > 0 && GPC.Model.Models.ModelRevisions.AnalysisFingerprint(reopened) == GPC.Model.Models.ModelRevisions.AnalysisFingerprint(model)
            && reopened.NodesElements.Count == model.NodesElements.Count, "GH archive: model fingerprint (" + where + ")");
        bool Refused(Action read) { try { read(); return false; } catch (System.Runtime.Serialization.SerializationException) { return true; } }
        var (payload, payloadWhere) = ExamplePayload(Path.Combine("19-flusso-acciaio-misto", "19-flusso-acciaio-misto_REV003.ghx"), "MaterialModel");
        Assert(GpcGooArchive.IsLegacy(payload) && Refused(() => GpcGooArchive.ReadValue<GPC.Model.Materials.Material>(payload)), "GH archive: legacy payload recognised and refused (" + payloadWhere + ")");
        Assert(Refused(() => GpcGooArchive.ReadModel(bytes)), "GH archive: kind mismatch refused");
        var version = (byte[])bytes.Clone(); version[7] = 9; Assert(Refused(() => GpcGooArchive.ReadValue<GPC.Model.Materials.Material>(version)), "GH archive: unknown version refused");
        Assert(Refused(() => GpcGooArchive.ReadValue<GPC.Model.Materials.Material>(new byte[] { 1, 2, 3 })), "GH archive: foreign bytes refused");
        bool Fails(Action read) { try { read(); return false; } catch (Exception) { return true; } }
        Assert(Fails(() => GpcGooArchive.ReadValue<GPC.Model.Materials.Material>(bytes.Take(bytes.Length / 2).ToArray())), "GH archive: truncated data refused");
    }

    /// <summary>The six distributions decomposed into linear pieces: each piece reproduces the native law at its ends.</summary>
    private static void Schemas()
    {
        const double pa = 3, pb = 5, p1 = 1, p2 = 7, a = 0.2, b = 0.3;
        var expected = new Dictionary<BridgeLoadSchema, (double, double)[]>
        {
            [BridgeLoadSchema.Uniform] = new[] { (a, pa), (1 - b, pa) },
            [BridgeLoadSchema.Keystone] = new[] { (a, pa), (1 - b, pb) },
            [BridgeLoadSchema.Triangular] = new[] { (0.0, p1), (a, pa), (1.0, p2) },
            [BridgeLoadSchema.TriangularEnd1_Keystone] = new[] { (0.0, p1), (a, pa), (1 - b, pb) },
            [BridgeLoadSchema.Keystone_TriangularEnd2] = new[] { (a, pa), (1 - b, pb), (1.0, p2) },
            [BridgeLoadSchema.TriangularEnds_Keystone] = new[] { (0.0, p1), (a, pa), (1 - b, pb), (1.0, p2) },
        };
        var cs = new LoadCaseBase(GpcIdentity.Case("q"), "q");
        foreach (var schema in expected)
        {
            var pieces = GpcBeamLoads.Distributed(cs, new G.Point3d(0, 0, 0), new G.Point3d(1000, 0, 0), schema.Key, pa, pb, p1, p2, a, b, new G.Vector3d(0, 0, 1), false, null, null, null, 1, "test");
            Assert(pieces.Count == schema.Value.Length - 1, "schema pieces " + schema.Key);
            for (int k = 0; k < pieces.Count; k++)
                Assert(pieces[k].Start == schema.Value[k].Item1 && pieces[k].End == schema.Value[k + 1].Item1
                    && pieces[k].StartIntensity.GetGlobalForces()[2] == schema.Value[k].Item2 && pieces[k].EndIntensity.GetGlobalForces()[2] == schema.Value[k + 1].Item2, "schema law " + schema.Key + " piece " + k);
        }
    }
}
#endif
