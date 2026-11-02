using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

// Baseline of the existing plugin, not the proposed domain. Archive formats differ by plugin.
internal static class MigrationScale
{
    private static StreamWriter output;
    private static int nodeCount, plateCount;
    private static readonly Func<long> Allocated = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes));
    internal static int Run()
    {
        string path = Environment.GetEnvironmentVariable("MIGRATION_SCALE_OUTPUT");
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Set MIGRATION_SCALE_OUTPUT to a new CSV file.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        using (output = new StreamWriter(new FileStream(path, FileMode.CreateNew)))
        {
            output.WriteLine("runtime,nodes,plates,operation,repetition,seconds,threadAllocatedBytes,heapDeltaBytes");
            new ModelModel(Build(3));
            foreach (int side in new[] { 10, 30, 100 })
            {
                nodeCount = side * side; plateCount = 2 * (side - 1) * (side - 1);
                ModelModel model = null, copy = null; byte[] archive = null;
                Measure("build-and-assemble", 0, () => model = Build(side));
                Measure("copy-first", 0, () => copy = new ModelModel(model));
                for (int i = 1; i <= 5; i++)
                {
                    Measure("copy-warm", i, () => copy = new ModelModel(model));
                    if (copy.Nodes.Count != model.Nodes.Count || copy.Plates.Count != model.Plates.Count) throw new Exception("Entity loss in copy");
                    var point = copy.Nodes[0].Position;
                    copy.Nodes[0].Position = new Point3d(point.X + 17, point.Y, point.Z);
                    if (model.Nodes[0].Position.X != point.X) throw new Exception("Node mutation leaked into source");
                    copy = null;
                }
                Measure("save-memory", 0, () => archive = ModelArchive.Write(model));
                Measure("load-memory", 0, () => copy = ModelArchive.Read<ModelModel>(archive));
                if (copy.Nodes.Count != nodeCount || copy.Plates.Count != plateCount) throw new Exception("Archive entity loss");
                byte[] hash = null;
                Measure("archive-sha256", 0, () => { using (var sha = SHA256.Create()) hash = sha.ComputeHash(archive); });
                Console.WriteLine("PASS " + nodeCount + "/" + plateCount + ", archive bytes " + archive.Length + ", SHA256 " + BitConverter.ToString(hash).Replace("-", ""));
            }
            
        }
        return 0;
    }

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

    private static ModelModel Build(int side)
    {
        var model = new ModelModel(ModelModel.ModelUnits.Nmm);
        var property = new PlatePropertyModel("P") { Name = "Synthetic 100 mm", MembraneThickness = 100, BendingThickness = 100, Material = new MaterialModel("S") { Modulus = 210000 }, PropertyType = PlatePropertyType.ShellThin };
        var elements = new List<ElementModel>();
        var points = new Point3d[side, side];
        for (int j = 0; j < side; j++) for (int i = 0; i < side; i++)
        {
            points[i,j] = new Point3d(1000.0*i,1000.0*j,0);
            elements.Add(new NodeModel((1+j*side+i).ToString(CultureInfo.InvariantCulture), points[i,j]));
        }
        int id = 0;
        for (int j = 0; j < side-1; j++) for (int i = 0; i < side-1; i++)
        {
            elements.Add(new PlateModel((++id).ToString(CultureInfo.InvariantCulture), new[] { points[i,j], points[i+1,j], points[i+1,j+1] }, property));
            elements.Add(new PlateModel((++id).ToString(CultureInfo.InvariantCulture), new[] { points[i,j], points[i+1,j+1], points[i,j+1] }, property));
        }
        model.AddElements(elements);
        return model;
    }
    
}

