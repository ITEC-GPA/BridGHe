#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using GH_IO.Serialization;
using GPC.Model.ElementProperties;
using GPC.Model.Loads;
using GPC.Model.Loads.Assignments;
using GPC.Model.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.6 lane R4c-moving: what the native tests of the examples 50 (<see cref="BridgeMovingRevisionNative"/>, <see cref="BridgeMovingSlabTargets"/>) and
/// 99 (<see cref="NativeModularBridgeTests"/>, <see cref="R3ConcreteExclusionCheck"/>) read of the GH data, by their GPC data alone (no FEM value):
/// <list type="bullet">
/// <item>the GPC input of a GH model (<c>GpcInput</c>), the analysed GPC model of an analysis output with its results catalog (<c>GpcResults</c>), the
/// GPC bridge model of Build BridGHe Model (<c>GpcBridge</c>);</item>
/// <item>the result rows of the Results components (<see cref="GpcResultRows"/>), nodes in their GPC number order (the order of the former FEM nodes);</item>
/// <item>the independent integration of the vertical loads of a case or combination on the GPC input (<see cref="VerticalLoad"/>), the GPC form of the
/// former <c>NativeModularBridgeTests.VerticalLoad</c> on the FEM model: self-weight, distributed and concentrated beam forces, node forces;</item>
/// <item>the analysis of a copy of an analysis component on its input given by its GPC input alone (<see cref="GpcOnlyAnalysis"/>): the true
/// GPC-only output <c>FromGpc(input)</c> the consumers read on the path of P2.</item>
/// </list>
/// </summary>
internal static class R4cMoving
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static object Prop(object o, string name) => o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);

    /// <summary>The GPC input of a GH model (its conversion, the input a model transformer attached, or the input it was given alone).</summary>
    internal static GpcModels.Conversion Input(object goo) => Prop(goo, "GpcInput") as GpcModels.Conversion;

    /// <summary>The GPC bridge model of a Build BridGHe Model output, or null.</summary>
    internal static GpcBridge Bridge(object goo) => Prop(goo, "GpcBridge") as GpcBridge;

    /// <summary>The analysed GPC model of an analysis output and its results catalog; null when the output has none.</summary>
    internal static Analysed Results(object goo) =>
        Prop(goo, "GpcResults") is Model m && GpcResultCatalog.Of(m) is { } c ? new Analysed(m, c) : null;

    /// <summary>An analysed GPC model with its catalog and the node rows of every node, in node number order.</summary>
    internal sealed class Analysed
    {
        internal Model Model { get; }
        internal GpcResultCatalog Catalog { get; }
        private GpcResultRows.NodeRow[][] nodeRows;
        internal Analysed(Model model, GpcResultCatalog catalog) { Model = model; Catalog = catalog; }
        internal string Label(int entry) => Catalog.Entries[entry].Label;
        internal IReadOnlyList<GPC.Model.Elements.NodeElement> Nodes => Model.NodesElements.Values.ToList();
        /// <summary>The rows of Node Results of every node (node number order; each node its rows in the order of the catalog).</summary>
        internal GpcResultRows.NodeRow[][] NodeRows => nodeRows ??= Model.NodesElements.Values.Select(n => GpcResultRows.Node(Catalog, Model, n).ToArray()).ToArray();
        /// <summary>The labels of the results with node reactions, in the order of their first row (the former distinct case names of the
        /// reaction results of the FEM output, node by node).</summary>
        internal string[] ReactionLabels() => NodeRows.SelectMany(rows => rows.Where(r => r.Reaction != null)).Select(r => Label(r.Entry)).Distinct().ToArray();
        /// <summary>The sum of the vertical reactions of a result, node by node (the former sum over the reaction results of the FEM nodes).</summary>
        internal double ReactionZ(string label) => NodeRows.SelectMany(rows => rows.Where(r => r.Reaction != null && Label(r.Entry) == label)).Sum(r => r.Reaction[2]);
        /// <summary>Whether a beam has a row of Beam Results.</summary>
        internal bool HasBeamRows() => Model.BeamElements.Values.Any(b => GpcResultRows.Beam(Catalog, Model, b).Any());
    }

    /// <summary>
    /// The independent vertical load of a case or a combination on the GPC input <paramref name="model"/> (N-mm), as the vertical reaction it
    /// must balance (minus the sum of the vertical loads), in the units of <paramref name="units"/>: a combination is the sum of its factors
    /// times its cases; a case the self-weight of every beam of its gravity loads (the mass per length of the property as the FEM path
    /// computed it, <see cref="GpcSectionMass.MassPerLength"/>, times the length and the vertical gravity), the distributed beam forces (each
    /// linear piece integrated over its stations, on the actual or the projected length), the concentrated beam forces and the node forces.
    /// The loads without a net force (couples, strains, temperatures) have no vertical resultant and are counted apart (<paramref name="other"/>).
    /// </summary>
    internal static double VerticalLoad(Model model, string name, BridgeUnits units, Dictionary<string, int> other = null, Dictionary<string, double> parts = null)
    {
        if (model.Combinations.TryGetValue(name, out var combination))
            return combination.GetLoadCaseCoefficientsPair().Sum(p => p.Value * VerticalLoad(model, p.Key.Name, units, other, parts));
        if (!model.LoadCases.ContainsKey(name)) throw new ArgumentException("Unknown native result case: " + name);
        static bool Of(Load l, string c) => l?.LoadCase?.Name == c;
        void Other(object load) { if (other != null) { string k = load.GetType().Name; other[k] = other.GetValueOrDefault(k) + 1; } }
        void Part(string k, double v) { if (parts != null) parts[name + " " + k] = parts.GetValueOrDefault(name + " " + k) + v; }
        double sum = 0;
        foreach (var g in model.ModelLoads.Values.Where(l => Of(l, name)))
        {
            if (g is not ModelGravityLoad gravity) { Other(g); continue; }
            double w = model.BeamElements.Values.Sum(b => MassPerLength(b) * b.Length) * gravity.GravityVector.Z; sum -= w; Part("self-weight", -w);
        }
        foreach (var b in model.BeamElements.Values)
        {
            foreach (var a in b.Assignments.Loads)
            {
                if (a.Concentrated is PointLoad c) { if (Of(c, name)) { double f = c.GetGlobalLoadVector().force.Z; sum -= f; Part("beam concentrated", -f); } continue; }
                if (a.StartIntensity == null || !Of(a.StartIntensity, name)) continue;
                double length = b.Length;
                if (a.LengthConvention == BeamLoadLengthConvention.ProjectedLength)
                {
                    var t = b.EndPoint - b.StartPoint; var n = a.ProjectionPlaneNormal ?? throw new NotSupportedException("A load per projected length without its plane.");
                    double along = (t.X * n.X + t.Y * n.Y + t.Z * n.Z) / (Math.Sqrt(t.X * t.X + t.Y * t.Y + t.Z * t.Z) * Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z));
                    length *= Math.Sqrt(Math.Max(0, 1 - along * along));
                }
                double z0 = a.StartIntensity.GetGlobalForces()[2], z1 = (a.EndIntensity ?? a.StartIntensity).GetGlobalForces()[2]; // per unit length (GetGlobalLoadVector: the total on its line)
                double q = (a.End - a.Start) * length * (z0 + z1) / 2; sum -= q; Part("beam distributed", -q);
            }
            foreach (var l in b.Loads.Values.Where(l => Of(l, name))) Other(l);
        }
        foreach (var n in model.NodesElements.Values)
            foreach (var l in n.Loads.Values.Where(l => Of(l, name)))
            {
                if (l is PointLoad f) { double z = f.GetGlobalLoadVector().force.Z; sum -= z; Part("node", -z); } else Other(l);
            }
        foreach (var p in model.AreaElements.Values) foreach (var l in p.Loads.Values.Where(l => Of(l, name)))
            throw new NotSupportedException("Independent integration of a plate load is not implemented: " + l.GetType().Name);
        return units.FromN(sum);
    }

    /// <summary>Diagnosis (not native): the integration of the requested results of every analysis input by part, with samples of the loads.</summary>
    internal static void DryLoads((string Name, GpcModels.Conversion Input, string[] Requested)[] analyses)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var (name, input, requested) in analyses)
        {
            if (input == null) { Console.WriteLine($"R4C DRY {name}: no GPC input (requested {string.Join(", ", requested)})"); continue; }
            var units = input.Report.Units;
            Console.WriteLine($"R4C DRY {name}: units {units.Units}, {input.Model.BeamElements.Count} beams, cases {string.Join(", ", input.Model.LoadCases.Keys)}, combinations {string.Join(", ", input.Model.Combinations.Keys)}, requested {string.Join(", ", requested)}");
            foreach (var g in input.Model.ModelLoads.Values) Console.WriteLine($"  model load {g.GetType().Name} case {g.LoadCase?.Name}" + (g is ModelGravityLoad m ? $" vector {m.GravityVector.X.ToString("R", inv)} {m.GravityVector.Y.ToString("R", inv)} {m.GravityVector.Z.ToString("R", inv)}" : ""));
            var b0 = input.Model.BeamElements.Values.First();
            if (b0.BeamProperty is ElasticBeamProperty p0) Console.WriteLine($"  beam 1: length {b0.Length.ToString("R", inv)}, density {p0.Material.Density.ToString("R", inv)}, legacy area {GpcSectionMass.LegacyArea(p0.Section).ToString("R", inv)}, mass per length {GpcSectionMass.MassPerLength(p0).ToString("R", inv)}");
            foreach (var a in input.Model.BeamElements.Values.SelectMany(b => b.Assignments.Loads.Select(x => (b, x))).Take(3))
                Console.WriteLine($"  beam {a.b.Id} load: start {a.x.Start.ToString("R", inv)} end {a.x.End.ToString("R", inv)} length {a.b.Length.ToString("R", inv)} convention {a.x.LengthConvention} "
                    + (a.x.StartIntensity != null ? $"z0 {a.x.StartIntensity.GetGlobalForces()[2].ToString("R", inv)} z1 {(a.x.EndIntensity ?? a.x.StartIntensity).GetGlobalForces()[2].ToString("R", inv)} case {a.x.StartIntensity.LoadCase?.Name}" : "concentrated"));
            foreach (var r in requested.Where(r => input.Model.LoadCases.ContainsKey(r) || input.Model.Combinations.ContainsKey(r)))
            {
                var parts = new Dictionary<string, double>(); var other = new Dictionary<string, int>();
                double v = VerticalLoad(input.Model, r, units, other, parts);
                Console.WriteLine($"  {r}: {v.ToString("R", inv)} = " + string.Join(" + ", parts.Select(p => p.Key + " " + p.Value.ToString("R", inv))) + (other.Count == 0 ? "" : "; other " + string.Join(", ", other.Select(o => o.Key + " " + o.Value))));
            }
        }
    }

    private static double MassPerLength(GPC.Model.Elements.BeamElement b) => b.BeamProperty is ElasticBeamProperty p ? GpcSectionMass.MassPerLength(p)
        : throw new NotSupportedException("Independent self-weight of a beam property of kind " + b.BeamProperty?.GetType().Name);

    /// <summary>Opt-in capture of the GPC data of a native run (BRIDGHE_MIGRATION_CAPTURE=&lt;folder&gt;, the variable of the former FEM capture): the
    /// GPC input of an analysis (BGHGPC1, <c>&lt;label&gt;.gpc</c> + <c>.records</c>) or its analysed model (BGHR2, <c>&lt;label&gt;.bghr</c>).</summary>
    internal static void Capture(string label, GpcModels.Conversion input = null, Model analysed = null)
    {
        string folder = Environment.GetEnvironmentVariable("BRIDGHE_MIGRATION_CAPTURE");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        if (input != null) { var (data, records) = GpcDraftArchive.Write(input); File.WriteAllBytes(Path.Combine(folder, label + ".gpc"), data); File.WriteAllBytes(Path.Combine(folder, label + ".records"), records); }
        if (analysed != null) File.WriteAllBytes(Path.Combine(folder, label + ".bghr"), GpcResultsFile.ToBytes(analysed));
    }

    /// <summary>
    /// The true GPC-only output of an analysis component of a solved definition: a copy of <paramref name="analysis"/> (its settings) in a document of
    /// its own, its model given by its GPC input alone (<see cref="GpcOnlyReplay.ModelOnly"/>: <c>FromGpc(input)</c> with the drafts the input carries),
    /// the other inputs as in the definition, its Output Path <paramref name="outputPath"/>, pressed once with the switches of P2 on
    /// (<see cref="R2aDesignFixtures.WithoutFem"/>). Returns the output goo (null with the messages when none) and the messages.
    /// </summary>
    internal static (IGH_Goo Output, string[] Messages) GpcOnlyAnalysis(GH_Component analysis, string outputPath)
    {
        using var doc = new GH_Document();
        var archive = new GH_Archive();
        if (!archive.AppendObject(analysis, "analysis")) throw new Exception("R4C MOVING: " + analysis.Name + " not written");
        var copy = (GH_Component)Grasshopper.Instances.ComponentServer.EmitObject(analysis.ComponentGuid);
        copy.CreateAttributes();
        if (!archive.ExtractObject(copy, "analysis")) throw new Exception("R4C MOVING: " + analysis.Name + " not read");
        doc.AddObject(copy, false);
        for (int i = 0; i < analysis.Params.Input.Count; i++)
        {
            var source = analysis.Params.Input[i];
            bool model = source.Name == "Model", path = source.Name.Equals("Output Path", StringComparison.OrdinalIgnoreCase);
            var tree = new GH_Structure<IGH_Goo>();
            if (path) tree.Append(new GH_String(outputPath), new GH_Path(0));
            else
                for (int b = 0; b < source.VolatileData.PathCount; b++)
                {
                    var p = source.VolatileData.get_Path(b); tree.EnsurePath(p);
                    foreach (var item in source.VolatileData.get_Branch(b)) tree.Append(item == null ? null : model ? GpcOnlyReplay.ModelOnly((IGH_Goo)item) : (IGH_Goo)item, p);
                }
            Set(copy.Params.Input[i], tree);
        }
        copy.GetType().GetField("_run", Instance).SetValue(copy, true);
        using (R2aDesignFixtures.WithoutFem()) { GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent); }
        var messages = new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning }.SelectMany(l => copy.RuntimeMessages(l).Select(m => l + ": " + m)).ToArray();
        var output = copy.Params.Output[0].VolatileData.AllData(true).OfType<IGH_Goo>().SingleOrDefault();
        return (output, messages);
    }

    /// <summary>The persistent data of an input, without its sources.</summary>
    internal static void Set(IGH_Param param, GH_Structure<IGH_Goo> data)
    {
        param.RemoveAllSources();
        var persistent = param.GetType().GetProperty("PersistentData")?.GetValue(param) ?? throw new Exception("R4C MOVING: input " + param.Name + " is not persistent");
        persistent.GetType().GetMethod("Clear").Invoke(persistent, null);
        var append = persistent.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(GH_Path));
        var ensure = persistent.GetType().GetMethod("EnsurePath", new[] { typeof(GH_Path) });
        for (int b = 0; b < data.PathCount; b++)
        {
            var path = data.get_Path(b); ensure?.Invoke(persistent, new object[] { path });
            foreach (var item in data.get_Branch(b)) append.Invoke(persistent, new object[] { item, path });
        }
    }

    /// <summary>R4c-moving, item 3 of the brief (the OutOfMemoryException of the Bridge Report of the chain of the 50): the memory of the process at a
    /// point of the native run (private bytes, working set, managed heap, GC heap size and the last committed bytes of the GC).</summary>
    internal static string Memory(string where)
    {
        using var p = Process.GetCurrentProcess();
        var info = GC.GetGCMemoryInfo();
        static string G(long bytes) => (bytes / 1073741824.0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        string line = $"R4C MEMORY {where}: private {G(p.PrivateMemorySize64)}, working set {G(p.WorkingSet64)}, managed {G(GC.GetTotalMemory(false))}, GC heap {G(info.HeapSizeBytes)}, committed {G(info.TotalCommittedBytes)}, available to the GC {G(info.TotalAvailableMemoryBytes)}";
        Console.WriteLine(line);
        return line;
    }

    /// <summary>R4c-moving: the memory the native run no longer needs before the rehearsal (a full blocking collection with the large object heap
    /// compacted), measured before and after.</summary>
    internal static void Release(string where)
    {
        Memory(where + ", before the collection");
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true); GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
        Memory(where + ", after the collection");
    }
}
#endif
