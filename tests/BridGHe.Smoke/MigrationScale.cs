using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;

// Baseline of the existing plugin, not the proposed domain. Archive formats differ by plugin.
internal static class MigrationScale
{
    private static StreamWriter output;
    private static int nodeCount, plateCount;
    private static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes));
    // R6.6 lane R4a: the same bench on GPC data alone (no FEM model): the grid of nodes and triangular plates as drafts of the product builders
    // (GpcNodeBuilders.Create, GpcPlateBuilders.Create on a shell of 100 mm, E 210000), assembled by Build Model on GPC; a copy is a copy of every
    // draft (GpcDrafts.Copy, the copy primitive of the GH data) and a mutation of a copied node does not reach the source; the archive is the
    // BGHGPC1 of the GPC input (GpcDraftArchive.Write/ReadConversion). The former FEM measures stay historical data (another plugin format).
    private sealed record GpcGrid(List<LegacyNodeDraft> Nodes, List<LegacyPlateDraft> Plates);
    internal static int Run()
    {
        string path = Environment.GetEnvironmentVariable("MIGRATION_SCALE_OUTPUT");
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Set MIGRATION_SCALE_OUTPUT to a new CSV file.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        using (output = new StreamWriter(new FileStream(path, FileMode.CreateNew)))
        {
            output.WriteLine("runtime,nodes,plates,operation,repetition,seconds,threadAllocatedBytes,heapDeltaBytes");
            Assemble(Grid(3));
            foreach (int side in new[] { 10, 30, 100 })
            {
                nodeCount = side * side; plateCount = 2 * (side - 1) * (side - 1);
                GpcGrid grid = null, copy = null; GpcModels.Conversion model = null, read = null; (byte[] Data, byte[] Records) archive = default;
                Measure("build-and-assemble", 0, () => { grid = Grid(side); model = Assemble(grid); });
                if (model.Model.NodesElements.Count != nodeCount || model.Model.AreaElements.Count != plateCount) throw new Exception("Entity loss in assembly");
                Measure("copy-first", 0, () => copy = Copy(grid));
                for (int i = 1; i <= 5; i++)
                {
                    Measure("copy-warm", i, () => copy = Copy(grid));
                    if (copy.Nodes.Count != grid.Nodes.Count || copy.Plates.Count != grid.Plates.Count) throw new Exception("Entity loss in copy");
                    var point = copy.Nodes[0].Node.Position;
                    copy.Nodes[0].Node.Position = new GPC.Geometry.Point3d(point.X + 17, point.Y, point.Z);
                    if (grid.Nodes[0].Node.Position.X != point.X) throw new Exception("Node mutation leaked into source");
                    copy = null;
                }
                Measure("save-memory", 0, () => archive = GpcDraftArchive.Write(model));
                Measure("load-memory", 0, () => read = GpcDraftArchive.ReadConversion(archive.Data, archive.Records));
                if (read.Model.NodesElements.Count != nodeCount || read.Model.AreaElements.Count != plateCount) throw new Exception("Archive entity loss");
                byte[] hash = null;
                Measure("archive-sha256", 0, () => { using (var sha = SHA256.Create()) hash = sha.ComputeHash(archive.Data.Concat(archive.Records).ToArray()); });
                Console.WriteLine("PASS " + nodeCount + "/" + plateCount + ", archive bytes " + (archive.Data.Length + archive.Records.Length) + ", SHA256 " + BitConverter.ToString(hash).Replace("-", "") + " (GPC data alone)");
            }
        }
        return 0;
    }

    private static Guid Id(string what) => GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r4a-migration-scale:" + what);

    private static GpcGrid Grid(int side)
    {
        var material = R2aDesignFixtures.Material("Undefined", "S", 210000, .3);
        var property = GpcPropertyBuilders.Plate("Synthetic 100 mm", 0, material, 100, 100);
        var nodes = new List<LegacyNodeDraft>(); var plates = new List<LegacyPlateDraft>();
        var points = new (double X, double Y, double Z)[side, side];
        for (int j = 0; j < side; j++) for (int i = 0; i < side; i++)
        {
            points[i, j] = (1000.0 * i, 1000.0 * j, 0);
            string nodeId = (1 + j * side + i).ToString(CultureInfo.InvariantCulture);
            nodes.Add(GpcNodeBuilders.Create(points[i, j].X, points[i, j].Y, points[i, j].Z, nodeId, null, Id(side + ":node:" + nodeId)));
        }
        int id = 0;
        for (int j = 0; j < side - 1; j++) for (int i = 0; i < side - 1; i++)
        {
            string a = (++id).ToString(CultureInfo.InvariantCulture);
            plates.Add(GpcPlateBuilders.Create(new[] { points[i, j], points[i + 1, j], points[i + 1, j + 1] }, property, null, 0, 0, a, Id(side + ":plate:" + a)));
            string b = (++id).ToString(CultureInfo.InvariantCulture);
            plates.Add(GpcPlateBuilders.Create(new[] { points[i, j], points[i + 1, j + 1], points[i, j + 1] }, property, null, 0, 0, b, Id(side + ":plate:" + b)));
        }
        return new GpcGrid(nodes, plates);
    }

    private static GpcModels.Conversion Assemble(GpcGrid grid) =>
        LegacyBuildModel.Build("Migration scale", new BridgeUnits(DefinitionUnits.Nmm), 1e-6, grid.Nodes.Select(d => (d.Node.Guid, d)).ToList(), new List<(Guid, LegacyBeamDraft)>(),
            grid.Plates.Select(d => (d.Plate.Guid, d)).ToList(), new List<LegacyCombinationDraft>(), new List<LegacyStageDraft>());

    private static GpcGrid Copy(GpcGrid grid) => new GpcGrid(grid.Nodes.Select(d => GpcDrafts.Copy(d)).ToList(), grid.Plates.Select(d => GpcDrafts.Copy(d)).ToList());
    private static void Measure(string name, int repetition, Action action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = Allocated(), heap = GC.GetTotalMemory(false);
        var watch = Stopwatch.StartNew(); action(); watch.Stop();
        output.WriteLine(string.Join(",", RuntimeInformation.FrameworkDescription, nodeCount, plateCount, name, repetition,
            watch.Elapsed.TotalSeconds.ToString("R", CultureInfo.InvariantCulture), Allocated() - before, GC.GetTotalMemory(false) - heap));
        output.Flush();
        Console.WriteLine(nodeCount + "/" + plateCount + " " + name + ": " + watch.Elapsed.TotalSeconds.ToString("F4", CultureInfo.InvariantCulture) + " s");
    }

}

