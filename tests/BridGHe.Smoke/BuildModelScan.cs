using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// Chain E (e3, P2K, coordinator decision 2 of 5/11): what Build Model does in the examples with the 0.3.0 rule (<c>ModelHelper.BuildModel</c>
/// and the legacy reader), measured on the GPC data of its Elements input (R6.6 lane R0: the drafts of the GH data, no FEM value read), for every
/// Build Model run of a definition:
/// - merges by GH GUID per family (a datum whose GH GUID came earlier), split into the same goo instance, a draft with the same canonical text
///   and a datum with another content, with what the merge appends and skips (load records, attribute records, groups: the merge of Build
///   Model on GPC, run on the drafts, never on the inputs);
/// - the node merge by position (RTree of Build Model on the draft positions, definition units: distinct GH data within the tolerance), beam
///   ends and plate corners moved onto a node, ends and corners that create a node, zero-length beams removed;
/// - the ends moved again by the legacy reader (node lookup in mm: the beam of the GPC input against the draft of the output, on its nodes).
/// <c>--build-model-scan [file ...]</c>: every definition of the L12 scope (01–10, 19, 29–49, 50, 99 with their revisions) or the given ones,
/// with only the upstream of the Build Model components solved (the others locked; analyses behind a Run button stay unsolved and their
/// Build Model runs are reported as without data). <see cref="Print"/> is also called by the native runners of 50 and 99 after their solve.
/// </summary>
internal static class BuildModelScan
{
    internal static readonly string[] Scope = Enumerable.Range(1, 10).Concat(new[] { 19 }).Concat(Enumerable.Range(29, 21)).Concat(new[] { 50, 99 }).Select(i => i.ToString("00") + "-").ToArray();

    /// <summary>The folder of the analyses the native scan runs (never the folders their definitions name).</summary>
    private static readonly string ScanFiles = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "bridghe-build-model-scan"));

    internal static int Run(string[] args)
    {
        BridgeExampleMigration.Initialize();
        bool native = args.Contains("--native");
        var files = args.SkipWhile(a => a != "--build-model-scan").Skip(1).Where(a => !a.StartsWith("--")).Select(a => Path.GetFullPath(Path.Combine(ExamplePaths.Root, a))).ToList();
        if (files.Count == 0) files = ScopeFiles();
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
                // R6.6 R0: without --native no analysis may solve: Keep Running (Straus R3 Analysis) and Run (Moving Static Analysis) are set
                // to false in memory; the Output Path is kept, so an analysis restores the results saved next to it (a read, as before) and its
                // Build Model runs keep the data they had. An analysis of 99 REV001 with Keep Running set by a toggle solved natively in the scan,
                // without the key and the lock, and wrote 12 ignored files into the main checkout (11/11). With --native the Output Path of
                // every analysis upstream is redirected (Moving Static Analysis too), Straus R3 Analysis pressed as before.
                foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => upstream.Contains(c) && c.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent").ToArray())
                {
                    if (!native)
                    {
                        foreach (var p in c.Params.Input.OfType<Grasshopper.Kernel.Parameters.Param_Boolean>().Where(p => p.Name is "Keep Running" or "Run"))
                        {
                            foreach (var s in p.Sources.ToArray()) p.RemoveSource(s);
                            p.PersistentData.Clear(); p.SetPersistentData(false);
                        }
                        // P2F (T6, proposal of P2R accepted 12/11): the Output Path goes to TEMP, with a copy of the results saved next to it
                        // under the same file name: the analysis restores the same results (a read) and the analysed model keeps its name, and
                        // nothing next to the path of the definition (the main checkout for the archived definitions) is used.
                        Console.WriteLine($"BUILD MODEL SCAN {label}: '{c.NickName}' not solved (not native){ReadOnlyOutput(c, label)}");
                        continue;
                    }
                    var output = (Grasshopper.Kernel.Parameters.Param_String)c.Params.Input.Single(p => string.Equals(p.Name, "Output Path", StringComparison.OrdinalIgnoreCase));
                    string folder = Path.Combine(ScanFiles, string.Concat(label.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '-' : ch)));
                    Directory.CreateDirectory(folder);
                    string target = Path.Combine(folder, c.InstanceGuid.ToString("N") + ".st7");
                    foreach (var s in output.Sources.ToArray()) output.RemoveSource(s);
                    output.PersistentData.Clear(); output.SetPersistentData(target);
                    if (!Path.GetFullPath(target).StartsWith(ScanFiles, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("BuildModelScanOutputOutsideTheScanFolder: " + target);
                    if (c.GetType().Name != "StrausR3AnalysisComponent") { Console.WriteLine($"BUILD MODEL SCAN {label}: '{c.NickName}' output {target}"); continue; }
                    c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true);
                    Console.WriteLine($"BUILD MODEL SCAN {label}: Run of '{c.NickName}' pressed, output {target}");
                }
                BridgeSegmentExcelTests.Solve(doc, true);
                total.Add(Print(doc, label));
            }
            catch (Exception e) { failures++; Console.WriteLine($"BUILD MODEL SCAN {label}: FAILED {e.GetType().Name}: {e.Message}"); }
        }
        Console.WriteLine($"BUILD MODEL SCAN TOTAL {files.Count} files, {failures} failed: " + total);
        return failures == 0 && total.CountFailures == 0 ? 0 : 1;
    }

    /// <summary>P2F (T6): the Output Path of an analysis not solved, moved to a folder of its own in TEMP with a copy of its saved results
    /// (<c>GpcResultsFile.PathFor</c>, the same file name); the path read from the internalised value of the input or of its one source (a panel
    /// or a text parameter). Returns the note printed with the analysis.</summary>
    private static string ReadOnlyOutput(GH_Component c, string label)
    {
        var output = c.Params.Input.OfType<Grasshopper.Kernel.Parameters.Param_String>().SingleOrDefault(p => string.Equals(p.Name, "Output Path", StringComparison.OrdinalIgnoreCase));
        if (output == null) return "";
        static string Text(IGH_Param p) => p is Grasshopper.Kernel.Special.GH_Panel panel ? panel.UserText?.Trim()
            : p is Grasshopper.Kernel.Parameters.Param_String s && s.PersistentData.DataCount == 1 ? s.PersistentData.AllData(true).Single()?.ToString() : null;
        string path = output.Sources.Count == 0 ? Text(output) : output.Sources.Count == 1 ? Text(output.Sources[0]) : null;
        if (string.IsNullOrWhiteSpace(path)) return "; Output Path not resolved, kept";
        string results = BridGHe.Common.Gpc.GpcResultsFile.PathFor(path);
        string folder = Path.Combine(ScanFiles, "read", string.Concat(label.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '-' : ch)), c.InstanceGuid.ToString("N"));
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, Path.GetFileName(path));
        if (!Path.GetFullPath(target).StartsWith(ScanFiles, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("BuildModelScanOutputOutsideTheScanFolder: " + target);
        bool copied = File.Exists(results);
        if (copied) File.Copy(results, BridGHe.Common.Gpc.GpcResultsFile.PathFor(target), true);
        foreach (var s in output.Sources.ToArray()) output.RemoveSource(s);
        output.PersistentData.Clear(); output.SetPersistentData(target);
        return $"; Output Path {target}" + (copied ? " with a copy of its saved results" : " (no results saved next to it)");
    }

    /// <summary>The definitions of the scope of L12 in the worktree (every .gh and .ghx, archives included, outputs folders excluded).</summary>
    internal static List<string> ScopeFiles() =>
        Directory.GetDirectories(ExamplePaths.Root).Where(d => Scope.Any(p => Path.GetFileName(d).StartsWith(p, StringComparison.Ordinal)))
            .SelectMany(d => Directory.GetFiles(d, "*.gh*", SearchOption.AllDirectories)).Where(f => (f.EndsWith(".gh") || f.EndsWith(".ghx")) && !f.Contains(Path.DirectorySeparatorChar + "calcoli" + Path.DirectorySeparatorChar))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

    internal sealed class Counts
    {
        internal int Runs, Empty, Nodes, Beams, Plates;
        internal int[] Merges = new int[3], SameGoo = new int[3], SameContent = new int[3], OtherContent = new int[3];
        internal int LoadsAppended, LoadsSkipped, AttributesAppended, AttributesSkipped, GroupsAppended;
        internal int PositionMerges, EndsMoved, EndNodes, ZeroLength, CornersMoved, CornerNodes, ReaderEndsMoved, ReaderCornersMoved;
        /// <summary>e3 point 6: runs whose GPC counters equal the 0.3.0 counts, and runs where they differ (or no GPC input).</summary>
        internal int CountsEqual, CountFailures, NoOutput;
        internal readonly List<string> Details = new List<string>();
        internal void Add(Counts c)
        {
            Runs += c.Runs; Empty += c.Empty; Nodes += c.Nodes; Beams += c.Beams; Plates += c.Plates; CountsEqual += c.CountsEqual; CountFailures += c.CountFailures; NoOutput += c.NoOutput;
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
            + $"reader moved {ReaderEndsMoved} beam ends, {ReaderCornersMoved} plate corners; GPC counters equal in {CountsEqual} runs, different or missing in {CountFailures}, runs without a model (FEM refused) {NoOutput}";
    }

    private static object Get(object o, string property) => o.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance).GetValue(o);

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
                // The reader: the GPC input of the output against its FEM beams (in mm: e3 point 5, every unit system).
                // The output at the path of the input, else the output branch in the same position (a grafted output: input {0}, output {0;0}).
                var outPath = outputs.PathExists(path) ? path : outputs.PathCount == input.PathCount ? outputs.Paths[input.Paths.IndexOf(path)] : null;
                var output = outPath != null ? outputs.get_Branch(outPath).Cast<object>().FirstOrDefault(o => o != null) : null;
                if (output == null)
                {
                    // No model: the FEM path refused the input (the GPC path is not attached then); the messages of the component say why.
                    run.NoOutput++;
                    run.Details.Add($"NO OUTPUT (locked {build.Locked}, output paths {string.Join(",", outputs.Paths)}, input paths {string.Join(",", input.Paths)}, phase {build.Phase}): " + string.Join(" | ", build.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(build.RuntimeMessages(GH_RuntimeMessageLevel.Warning))
                        .Concat(build.RuntimeMessages(GH_RuntimeMessageLevel.Remark))));
                }
                else
                {
                    try
                    {
                        // R6.6 R0: the GPC input of the output against the drafts of its elements (definition units; the beams and plates of the
                        // output in assembly order, their ends and corners on the nodes Build Model found): no FEM value read.
                        var attached = (BridGHe.Common.Gpc.GpcModels.Conversion)Get(output, "GpcInput"); var gpc = attached.Model;
                        var drafts = attached.Drafts ?? (BridGHe.Common.Gpc.GpcModelDrafts)Get(output, "GpcDrafts") ?? throw new InvalidOperationException("no drafts of the output");
                        var u = attached.Report.Units;
                        bool SameMm(GPC.Geometry.Point3d a, GPC.Geometry.Point3d b) => a.X == u.ToMm(b.X) && a.Y == u.ToMm(b.Y) && a.Z == u.ToMm(b.Z);
                        for (int k = 0; k < drafts.Beams.Count; k++)
                        {
                            var g = gpc.BeamElements[k + 1]; var d = drafts.Beams[k].Beam;
                            if (!SameMm(g.StartPoint, d.StartPoint)) run.ReaderEndsMoved++;
                            if (!SameMm(g.EndPoint, d.EndPoint)) run.ReaderEndsMoved++;
                        }
                        for (int k = 0; k < drafts.Plates.Count; k++)
                        {
                            var nodes = gpc.AreaElements[k + 1].Nodes; var corners = drafts.Plates[k].Corners;
                            for (int j = 0; j < corners.Count; j++) if (!SameMm(nodes[j].Position, corners[j])) run.ReaderCornersMoved++;
                        }
                    }
                    catch (Exception e) { run.Details.Add("reader comparison not available: " + e.Message); }
                    // e3 point 6: the counters of Build Model on GPC (the report of the attached input) equal to the 0.3.0 rule measured above on
                    // the FEM values (a self-merge on GPC: the same draft or a copy with the same content, as the same goo or the same archive here).
                    if (!(bool)Get(output, "HasAttachedInput")) { run.CountFailures++; run.Details.Add("GPC COUNTS FAIL: no GPC input attached"); }
                    else
                    {
                        var b = ((BridGHe.Common.Gpc.GpcModels.Conversion)Get(output, "GpcInput")).Report.BuildModel;
                        string Text(int[] merges, IEnumerable<int> self, int position, int endsMoved, int endNodes, int zero, int cornersMoved, int cornerNodes, int reader) =>
                            $"merges {string.Join("/", merges)} (self {string.Join("/", self)}), position merges {position}, ends moved {endsMoved}, end nodes {endNodes}, zero-length {zero}, corners moved {cornersMoved}, corner nodes {cornerNodes}, reader moved {reader}";
                        string gpcText = b == null ? "none" : Text(b.Merges, b.SelfMerges, b.PositionMerges, b.EndsMoved, b.EndNodes, b.ZeroLength, b.CornersMoved, b.CornerNodes, b.ReaderMoved);
                        string femText = Text(run.Merges, run.SameGoo.Zip(run.SameContent, (x, y) => x + y), run.PositionMerges, run.EndsMoved, run.EndNodes, run.ZeroLength, run.CornersMoved, run.CornerNodes,
                            run.ReaderEndsMoved + run.ReaderCornersMoved);
                        if (gpcText == femText) { run.CountsEqual++; run.Details.Add("GPC COUNTS EQUAL: " + gpcText + ", re-anchored " + b.Reanchored); }
                        else { run.CountFailures++; run.Details.Add("GPC COUNTS FAIL: GPC " + gpcText + " / 0.3.0 " + femText); }
                    }
                }
                Console.WriteLine($"BUILD MODEL SCAN {label} '{build.NickName}' {path}: " + run);
                foreach (var d in run.Details) Console.WriteLine("  " + d);
                counts.Add(run);
            }
        }
        Console.WriteLine($"BUILD MODEL SCAN {label} TOTAL: " + counts);
        return counts;
    }

    /// <summary>R6.6 R0: the attribute records of a draft (attributes and node restraint assignments), for the merge counts.</summary>
    private static int Attributes(object draft) => draft switch
    {
        BridGHe.Common.Gpc.Legacy.LegacyNodeDraft n => n.Node.Attributes.Count + n.Node.Assignments.Restrains.Count,
        BridGHe.Common.Gpc.Legacy.LegacyBeamDraft b => b.Beam.Attributes.Count,
        BridGHe.Common.Gpc.Legacy.LegacyPlateDraft p => p.Plate.Attributes.Count,
        _ => 0,
    };

    private static BridGHe.Common.Gpc.Legacy.LegacyElementRecords Records(object draft) => draft switch
    {
        BridGHe.Common.Gpc.Legacy.LegacyNodeDraft n => n.Records,
        BridGHe.Common.Gpc.Legacy.LegacyBeamDraft b => b.Records,
        BridGHe.Common.Gpc.Legacy.LegacyPlateDraft p => p.Records,
        _ => throw new ArgumentException(draft?.GetType().Name),
    };

    private static object Merge(object first, object other) => (first, other) switch
    {
        (BridGHe.Common.Gpc.Legacy.LegacyNodeDraft a, BridGHe.Common.Gpc.Legacy.LegacyNodeDraft b) => BridGHe.Common.Gpc.Legacy.LegacyBuildModel.Merge(a, b),
        (BridGHe.Common.Gpc.Legacy.LegacyBeamDraft a, BridGHe.Common.Gpc.Legacy.LegacyBeamDraft b) => BridGHe.Common.Gpc.Legacy.LegacyBuildModel.Merge(a, b),
        (BridGHe.Common.Gpc.Legacy.LegacyPlateDraft a, BridGHe.Common.Gpc.Legacy.LegacyPlateDraft b) => BridGHe.Common.Gpc.Legacy.LegacyBuildModel.Merge(a, b),
        _ => throw new ArgumentException(first?.GetType().Name + "/" + other?.GetType().Name),
    };

    private static Point3d R(GPC.Geometry.Point3d p) => new Point3d(p.X, p.Y, p.Z);

    /// <summary>The loops of the 0.3.0 rule (<c>ModelHelper.BuildModel</c>) on the GPC drafts of the GH data (R6.6 R0: no FEM value read), with the
    /// counters: the merges by GH GUID (the same goo, a draft with the same canonical text, another content) with what the merge appends (the
    /// merge of Build Model on GPC on the drafts, never on the inputs: load records, attribute records, groups), the RTree on the draft positions
    /// (definition units), the beam ends and plate corners.</summary>
    private static void Scan(object[] items, double tol, Counts run, string where)
    {
        string[] families = { "GH_Node", "GH_Beam", "GH_Plate" };
        for (int f = 0; f < 3; f++)
        {
            var goos = items.Where(g => g.GetType().Name == families[f]).ToArray();
            if (f == 0) run.Nodes = goos.Length; else if (f == 1) run.Beams = goos.Length; else run.Plates = goos.Length;
            var first = new Dictionary<Guid, (object Goo, object Merged)>();
            foreach (var goo in goos)
            {
                var guid = (Guid)Get(goo, "Guid"); var draft = Get(goo, "Gpc");
                if (!first.TryGetValue(guid, out var known)) { first.Add(guid, (goo, draft)); continue; }
                run.Merges[f]++;
                var knownDraft = Get(known.Goo, "Gpc");
                string kind = ReferenceEquals(goo, known.Goo) ? "same goo" : GpcDraftCapture.Text(draft) == GpcDraftCapture.Text(knownDraft) ? "same content" : "other content";
                if (kind == "same goo") run.SameGoo[f]++; else if (kind == "same content") run.SameContent[f]++; else run.OtherContent[f]++;
                var before = known.Merged; var merged = Merge(before, draft); first[guid] = (known.Goo, merged);
                int la = Records(merged).Loads - Records(before).Loads, aa = Attributes(merged) - Attributes(before), ga = Records(merged).Groups.Count - Records(before).Groups.Count;
                int loads = Records(draft).Loads, attributes = Attributes(draft);
                run.LoadsAppended += la; run.LoadsSkipped += loads - la; run.AttributesAppended += aa; run.AttributesSkipped += attributes - aa; run.GroupsAppended += ga;
                run.Details.Add($"merge {families[f]} {guid}: {kind}; appended {la}/{loads} loads ({string.Join(",", Records(draft).LoadRecords.Select(l => l.Kind + ":" + l.Case).Distinct())}), "
                    + $"{aa}/{attributes} attributes, {ga}/{Records(draft).Groups.Count} groups ({where})");
            }
        }
        // The RTree part of the 0.3.0 rule on positions (merged data keep the position of the first).
        var nodes = items.Where(g => g.GetType().Name == "GH_Node").GroupBy(g => (Guid)Get(g, "Guid")).Select(g => R(((BridGHe.Common.Gpc.Legacy.LegacyNodeDraft)Get(g.First(), "Gpc")).Node.Position)).ToList();
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
        var beams = items.Where(g => g.GetType().Name == "GH_Beam").GroupBy(g => (Guid)Get(g, "Guid")).Select(g => (BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Get(g.First(), "Gpc")).ToList();
        var ordered = new List<(int Key, int Index, BridGHe.Common.Gpc.Legacy.LegacyBeamDraft Beam)>(); int next = 1;
        var labels = beams.Select(b => b.Records.FirstLabel).ToList();
        var used = items.Where(g => g.GetType().Name == "GH_Beam").Select(g => ((BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Get(g, "Gpc")).Records.FirstLabel).Where(v => v != null).ToList();
        for (int i = 0; i < beams.Count; i++)
        {
            string id = labels[i];
            if (id == null) { while (used.Contains(next.ToString())) next++; id = next.ToString(); next++; }
            ordered.Add((int.TryParse(id, out int key) ? key : int.MaxValue, i, beams[i]));
        }
        foreach (var (_, _, beam) in ordered.OrderBy(o => o.Key).ThenBy(o => o.Index))
        {
            var a = Anchor(R(beam.Beam.StartPoint), ref run.EndsMoved, ref run.EndNodes); var b = Anchor(R(beam.Beam.EndPoint), ref run.EndsMoved, ref run.EndNodes);
            if (a == b) run.ZeroLength++;
        }
        foreach (var plate in items.Where(g => g.GetType().Name == "GH_Plate").GroupBy(g => (Guid)Get(g, "Guid")).Select(g => (BridGHe.Common.Gpc.Legacy.LegacyPlateDraft)Get(g.First(), "Gpc")))
            foreach (var p in plate.Corners) Anchor(R(p), ref run.CornersMoved, ref run.CornerNodes);
    }
}
