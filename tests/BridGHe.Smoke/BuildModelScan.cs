#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// Chain E (e3, P2K, coordinator decision 2 of 5/11): what Build Model does in the examples with the 0.3.0 rule (<c>ModelHelper.BuildModel</c>
/// and the legacy reader), measured on the FEM values of its Elements input, for every Build Model run of a definition:
/// - merges by GH GUID per family (a datum whose GH GUID came earlier), split into the same goo instance, a copy with the same content
///   (archive bytes) and a datum with another content, with what <c>ElementModel.Merge</c> appends and what its <c>Contains</c> skips
///   (loads, attributes, groups: the exact 0.3.0 rule, run on copies);
/// - the node merge by position (RTree of Build Model: distinct GH data within the tolerance), beam ends and plate corners moved onto a
///   node, ends and corners that create a node, zero-length beams removed;
/// - the ends moved again by the legacy reader (node lookup in mm: the beam of the conversion against the FEM beam of the output).
/// <c>--build-model-scan [file ...]</c>: every definition of the L12 scope (01–10, 19, 29–49, 50, 99 with their revisions) or the given ones,
/// with only the upstream of the Build Model components solved (the others locked; analyses behind a Run button stay unsolved and their
/// Build Model runs are reported as without data). <see cref="Print"/> is also called by the native runners of 50 and 99 after their solve.
/// </summary>
internal static class BuildModelScan
{
    internal static readonly string[] Scope = Enumerable.Range(1, 10).Concat(new[] { 19 }).Concat(Enumerable.Range(29, 21)).Concat(new[] { 50, 99 }).Select(i => i.ToString("00") + "-").ToArray();

    internal static int Run(string[] args)
    {
        BridgeExampleMigration.Initialize();
        var files = args.SkipWhile(a => a != "--build-model-scan").Skip(1).Where(a => !a.StartsWith("--")).Select(a => Path.GetFullPath(Path.Combine(ExamplePaths.Root, a))).ToList();
        if (files.Count == 0)
            files = Directory.GetDirectories(ExamplePaths.Root).Where(d => Scope.Any(p => Path.GetFileName(d).StartsWith(p, StringComparison.Ordinal)))
                .SelectMany(d => Directory.GetFiles(d, "*.gh*", SearchOption.AllDirectories)).Where(f => (f.EndsWith(".gh") || f.EndsWith(".ghx")) && !f.Contains(Path.DirectorySeparatorChar + "calcoli" + Path.DirectorySeparatorChar))
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var total = new Counts(); int failures = 0;
        foreach (var file in files)
        {
            string label = Path.GetRelativePath(ExamplePaths.Root, file);
            try
            {
                using var doc = GrasshopperBridgeExample.Read(file); doc.FilePath = file;
                var builds = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BuildModelComponent").ToArray();
                if (builds.Length == 0) { Console.WriteLine($"BUILD MODEL SCAN {label}: no Build Model"); continue; }
                var upstream = new HashSet<IGH_DocumentObject>();
                void Up(IGH_DocumentObject o)
                {
                    if (!upstream.Add(o)) return;
                    var parameters = o is IGH_Component c ? c.Params.Input : o is IGH_Param single ? new List<IGH_Param> { single } : new List<IGH_Param>();
                    foreach (var p in parameters) foreach (var s in p.Sources) Up(s.Attributes.GetTopLevel.DocObject);
                }
                foreach (var b in builds) Up(b);
                foreach (var o in doc.Objects.OfType<IGH_ActiveObject>()) if (!upstream.Contains(o)) o.Locked = true;
                BridgeSegmentExcelTests.Solve(doc, true);
                total.Add(Print(doc, label));
            }
            catch (Exception e) { failures++; Console.WriteLine($"BUILD MODEL SCAN {label}: FAILED {e.GetType().Name}: {e.Message}"); }
        }
        Console.WriteLine($"BUILD MODEL SCAN TOTAL {files.Count} files, {failures} failed: " + total);
        return failures == 0 ? 0 : 1;
    }

    internal sealed class Counts
    {
        internal int Runs, Empty, Nodes, Beams, Plates;
        internal int[] Merges = new int[3], SameGoo = new int[3], SameContent = new int[3], OtherContent = new int[3];
        internal int LoadsAppended, LoadsSkipped, AttributesAppended, AttributesSkipped, GroupsAppended;
        internal int PositionMerges, EndsMoved, EndNodes, ZeroLength, CornersMoved, CornerNodes, ReaderEndsMoved, ReaderCornersMoved;
        internal readonly List<string> Details = new List<string>();
        internal void Add(Counts c)
        {
            Runs += c.Runs; Empty += c.Empty; Nodes += c.Nodes; Beams += c.Beams; Plates += c.Plates;
            for (int f = 0; f < 3; f++) { Merges[f] += c.Merges[f]; SameGoo[f] += c.SameGoo[f]; SameContent[f] += c.SameContent[f]; OtherContent[f] += c.OtherContent[f]; }
            LoadsAppended += c.LoadsAppended; LoadsSkipped += c.LoadsSkipped; AttributesAppended += c.AttributesAppended; AttributesSkipped += c.AttributesSkipped; GroupsAppended += c.GroupsAppended;
            PositionMerges += c.PositionMerges; EndsMoved += c.EndsMoved; EndNodes += c.EndNodes; ZeroLength += c.ZeroLength; CornersMoved += c.CornersMoved; CornerNodes += c.CornerNodes;
            ReaderEndsMoved += c.ReaderEndsMoved; ReaderCornersMoved += c.ReaderCornersMoved;
        }
        public override string ToString() =>
            $"{Runs} runs ({Empty} without data), {Nodes}/{Beams}/{Plates} input nodes/beams/plates; merges by GH GUID nodes/beams/plates {string.Join("/", Merges)} "
            + $"(same goo {string.Join("/", SameGoo)}, same content {string.Join("/", SameContent)}, other content {string.Join("/", OtherContent)}); "
            + $"Merge appended {LoadsAppended} loads, {AttributesAppended} attributes, {GroupsAppended} groups, skipped (Contains) {LoadsSkipped} loads, {AttributesSkipped} attributes; "
            + $"position merges {PositionMerges}, beam ends moved {EndsMoved}, end nodes {EndNodes}, zero-length beams {ZeroLength}, plate corners moved {CornersMoved}, corner nodes {CornerNodes}; "
            + $"reader moved {ReaderEndsMoved} beam ends, {ReaderCornersMoved} plate corners";
    }

    private static object Get(object o, string property) => o.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance).GetValue(o);
    private static byte[] Bytes(object value) => BridGHe.Common.Helpers.ModelArchive.Write(value);

    /// <summary>The statistics of every Build Model run of a solved definition (one line per run, the details of every merge).</summary>
    internal static Counts Print(GH_Document doc, string label)
    {
        var counts = new Counts();
        var plugin = BridgeMergeTests.Initialize();
        var settingsType = plugin.GetType("BridGHe.Grasshopper.Settings");
        double tol = (double)settingsType.GetProperty("Tolerance").GetValue(settingsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null));
        foreach (var build in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BuildModelComponent"))
        {
            var input = build.Params.Input[0].VolatileData;
            var outputs = build.Params.Output[0].VolatileData;
            foreach (var path in input.Paths)
            {
                var run = new Counts { Runs = 1 };
                var items = input.get_Branch(path).Cast<object>().Where(g => g != null).ToArray();
                if (items.Length == 0) { run.Empty = 1; counts.Add(run); Console.WriteLine($"BUILD MODEL SCAN {label} '{build.NickName}' {path}: no data"); continue; }
                Scan(items, tol, run, $"{label} '{build.NickName}' {build.InstanceGuid:N} {path}");
                // The reader: the conversion of the output against its FEM beams (N-mm models only).
                var output = outputs.PathExists(path) ? outputs.get_Branch(path).Cast<object>().FirstOrDefault(o => o != null) : null;
                if (output != null && ((ModelModel)Get(output, "Value")).Units == ModelModel.ModelUnits.Nmm)
                {
                    try
                    {
                        var fem = (ModelModel)Get(output, "Value"); var gpc = ((BridGHe.Common.Gpc.GpcModels.Conversion)Get(output, "GpcInput")).Model;
                        var beams = fem.Beams.ToArray();
                        for (int k = 0; k < beams.Length; k++)
                        {
                            var g = gpc.BeamElements[k + 1];
                            if (!Same(g.StartPoint, beams[k].PointFrom)) run.ReaderEndsMoved++;
                            if (!Same(g.EndPoint, beams[k].PointTo)) run.ReaderEndsMoved++;
                        }
                        var plates = fem.Plates.ToArray();
                        for (int k = 0; k < plates.Length; k++)
                        {
                            var nodes = gpc.AreaElements[k + 1].Nodes;
                            for (int j = 0; j < plates[k].Points.Count; j++) if (!Same(nodes[j].Position, plates[k].Points[j])) run.ReaderCornersMoved++;
                        }
                    }
                    catch (Exception e) { run.Details.Add("reader comparison not available: " + e.Message); }
                }
                Console.WriteLine($"BUILD MODEL SCAN {label} '{build.NickName}' {path}: " + run);
                foreach (var d in run.Details) Console.WriteLine("  " + d);
                counts.Add(run);
            }
        }
        Console.WriteLine($"BUILD MODEL SCAN {label} TOTAL: " + counts);
        return counts;
    }

    private static bool Same(GPC.Geometry.Point3d a, Point3d b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>The loops of <c>ModelHelper.BuildModel</c> on the FEM values, with the counters (the values are copied, never changed).</summary>
    private static void Scan(object[] items, double tol, Counts run, string where)
    {
        string[] families = { "GH_Node", "GH_Beam", "GH_Plate" };
        for (int f = 0; f < 3; f++)
        {
            var goos = items.Where(g => g.GetType().Name == families[f]).ToArray();
            if (f == 0) run.Nodes = goos.Length; else if (f == 1) run.Beams = goos.Length; else run.Plates = goos.Length;
            var first = new Dictionary<Guid, (object Goo, ElementModel Copy)>();
            foreach (var goo in goos)
            {
                var guid = (Guid)Get(goo, "Guid"); var value = (ElementModel)Get(goo, "Value");
                if (!first.TryGetValue(guid, out var known))
                {
                    first.Add(guid, (goo, value is NodeModel n ? new NodeModel(n) : value is BeamModel b ? new BeamModel(b) : (ElementModel)new PlateModel((PlateModel)value)));
                    continue;
                }
                run.Merges[f]++;
                var knownValue = (ElementModel)Get(known.Goo, "Value");
                string kind = ReferenceEquals(goo, known.Goo) ? "same goo" : Bytes(value).AsSpan().SequenceEqual(Bytes(knownValue)) ? "same content" : "other content";
                if (kind == "same goo") run.SameGoo[f]++; else if (kind == "same content") run.SameContent[f]++; else run.OtherContent[f]++;
                int loads = known.Copy.Loads.Count, attributes = known.Copy.Attributes.Count, groups = known.Copy.Groups.Count;
                known.Copy.Merge(value);
                int la = known.Copy.Loads.Count - loads, aa = known.Copy.Attributes.Count - attributes, ga = known.Copy.Groups.Count - groups;
                run.LoadsAppended += la; run.LoadsSkipped += value.Loads.Count - la; run.AttributesAppended += aa; run.AttributesSkipped += value.Attributes.Count - aa; run.GroupsAppended += ga;
                run.Details.Add($"merge {families[f]} {guid}: {kind}; appended {la}/{value.Loads.Count} loads ({string.Join(",", value.Loads.Select(l => l.GetType().Name + ":" + l.LoadCase?.Name).Distinct())}), "
                    + $"{aa}/{value.Attributes.Count} attributes ({string.Join(",", value.Attributes.Select(a => a.GetType().Name).Distinct())}), {ga}/{value.Groups.Count} groups ({where})");
            }
        }
        // The RTree part of ModelHelper.BuildModel on positions (merged data keep the position of the first).
        var nodes = items.Where(g => g.GetType().Name == "GH_Node").GroupBy(g => (Guid)Get(g, "Guid")).Select(g => ((NodeModel)Get(g.First(), "Value")).Position).ToList();
        var kept = new List<Point3d>();
        using var tree = new RTree();
        foreach (var pt in nodes)
        {
            bool found = false; int id = -1; double nearest = double.PositiveInfinity;
            tree.Search(new Sphere(pt, tol), (s, e) => { double d = kept[e.Id].DistanceToSquared(pt); if (d < nearest || d == nearest && e.Id < id) { found = true; id = e.Id; nearest = d; } });
            if (!found) { kept.Add(pt); tree.Insert(pt, kept.Count - 1); } else run.PositionMerges++;
        }
        Point3d Anchor(Point3d p, ref int moved, ref int created)
        {
            bool found = false; int id = -1;
            tree.Search(new Sphere(p, tol), (s, e) => { found = true; id = e.Id; });
            if (found && id != -1) { if (kept[id] != p) moved++; return kept[id]; }
            kept.Add(p); tree.Insert(p, kept.Count - 1); created++; return p;
        }
        // Beams in the order of Build Model: by integer BridGHe ID (labels or the first free integer, in input order); the counts do not
        // depend on the order except through the nodes created at the ends, so the input order of the first data is used with the labels.
        var beams = items.Where(g => g.GetType().Name == "GH_Beam").GroupBy(g => (Guid)Get(g, "Guid")).Select(g => (BeamModel)Get(g.First(), "Value")).ToList();
        var ids = new List<string>(); int next = 1; var ordered = new List<(int Key, int Index, BeamModel Beam)>();
        var labels = beams.Select(b => b.Attributes.OfType<ElementIdAttributeModel>().FirstOrDefault()?.Value).ToList();
        var used = items.Where(g => g.GetType().Name == "GH_Beam").Select(g => ((BeamModel)Get(g, "Value")).Attributes.OfType<ElementIdAttributeModel>().FirstOrDefault()?.Value).Where(v => v != null).ToList();
        for (int i = 0; i < beams.Count; i++)
        {
            string id = labels[i];
            if (id == null) { while (used.Contains(next.ToString())) next++; id = next.ToString(); next++; }
            ordered.Add((int.TryParse(id, out int key) ? key : int.MaxValue, i, beams[i]));
        }
        foreach (var (_, _, beam) in ordered.OrderBy(o => o.Key).ThenBy(o => o.Index))
        {
            var a = Anchor(beam.PointFrom, ref run.EndsMoved, ref run.EndNodes); var b = Anchor(beam.PointTo, ref run.EndsMoved, ref run.EndNodes);
            if (a == b) run.ZeroLength++;
        }
        foreach (var plate in items.Where(g => g.GetType().Name == "GH_Plate").GroupBy(g => (Guid)Get(g, "Guid")).Select(g => (PlateModel)Get(g.First(), "Value")))
            foreach (var p in plate.Points) Anchor(p, ref run.CornersMoved, ref run.CornerNodes);
    }
}
#endif
