#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.5 P1 (coordinator, 7/11): the components of a lane replayed with their inputs given by the GPC data alone. In every definition of the
/// Build Model scope (the example scan, solved without analyses), each instance of a component of the lane is copied into an empty
/// document twice: once with the inputs it had (the FEM-backed GH types of today), once with the same inputs where every GH type of the
/// converted kinds is replaced by the type made from its GPC datum alone (<c>FromGpc</c>, no FEM value). The two copies must give the same
/// outputs: the same trees, the same messages, every GH type with the same GPC datum (the text of the datum, exact), every other value the same
/// text; the outputs of the GPC-only copy built from GPC-only inputs have no FEM value. Regression <c>gpc-only-&lt;lane&gt;</c> (not native).
/// </summary>
internal static class GpcOnlyReplay
{
    /// <summary>Lane 1, elements: the consumers and producers of nodes, beams and plates (attributes, loads, filters and tools).</summary>
    internal static readonly string[] Lane1Types =
    {
        // Attributes
        "BeamOutputStationsComponent", "BeamReleasesComponent", "BeamStringGroupComponent", "BeamSupportComponent", "BeamTaperComponent",
        "ElementIdComponent", "LoadPatchTypeComponent", "NodeRestraintsComponent", "NodeStiffnessComponent", "PlateSupportComponent", "SuperElementComponent",
        // Loads
        "BeamDistributedLoadComponent", "BeamDistributedMomentComponent", "BeamGradientLoadComponent", "BeamPointLoadComponent", "BeamPointMomentComponent",
        "BeamPreLoadComponent", "NodeDisplacementLoadComponent", "NodeLoadComponent", "NodeMomentComponent", "NodeTemperatureComponent",
        "PlateNSMassComponent", "PlatePreStressComponent", "PlatePressureComponent",
        // Tools
        "AlignBeamAxesComponent", "AlignPlateAxesComponent", "BeamFilterComponent", "BeamOverrideComponent", "ElementFilterComponent",
        "MergeGeometryComponent", "NodeFilterComponent", "PlateFilterComponent",
    };

    /// <summary>The GH element types converted to their GPC data alone in lane 1.</summary>
    internal static readonly string[] ElementGoo = { "GH_Node", "GH_Beam", "GH_Plate" };

    private static object Prop(object o, string name) => o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);

    /// <summary>The GH type made from the GPC datum alone of a FEM-backed one, or the same goo (another kind, or no datum).</summary>
    private static IGH_Goo GpcOnly(IGH_Goo goo, ISet<string> kinds)
    {
        if (goo == null || !kinds.Contains(goo.GetType().Name) || goo.ScriptVariable() == null) return goo;
        var datum = Prop(goo, "Gpc");
        if (datum == null) return goo;
        var from = goo.GetType().GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static);
        var args = from.GetParameters().Length == 2 ? new[] { datum, Prop(goo, "Guid") } : new[] { datum };
        var only = (IGH_Goo)from.Invoke(null, args);
        foreach (var name in new[] { "AnalysedGpc", "AnalysedGpcModel" })
            if (goo.GetType().GetProperty(name) is { CanWrite: true } p) p.SetValue(only, p.GetValue(goo));
        return only;
    }

    /// <summary>A copy of a component (its settings and persistent data) in a new document.</summary>
    private static GH_Component Copy(GH_Component c, GH_Document into)
    {
        var archive = new GH_Archive();
        if (!archive.AppendObject(c, "component")) throw new Exception("GPC-ONLY REPLAY: " + c.Name + " not written");
        var copy = (GH_Component)Grasshopper.Instances.ComponentServer.EmitObject(c.ComponentGuid);
        copy.CreateAttributes();
        if (!archive.ExtractObject(copy, "component")) throw new Exception("GPC-ONLY REPLAY: " + c.Name + " not read");
        into.AddObject(copy, false);
        return copy;
    }

    private static void SetInput(IGH_Param param, IGH_Structure data, Func<IGH_Goo, IGH_Goo> map)
    {
        param.RemoveAllSources();
        var persistent = param.GetType().GetProperty("PersistentData")?.GetValue(param) ?? throw new Exception("GPC-ONLY REPLAY: input " + param.Name + " is not persistent");
        persistent.GetType().GetMethod("Clear").Invoke(persistent, null);
        var append = persistent.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(GH_Path));
        var ensure = persistent.GetType().GetMethod("EnsurePath", new[] { typeof(GH_Path) });
        for (int b = 0; b < data.PathCount; b++)
        {
            var path = data.get_Path(b);
            var branch = data.get_Branch(b);
            ensure?.Invoke(persistent, new object[] { path });
            foreach (var item in branch) append.Invoke(persistent, new object[] { item == null ? null : map((IGH_Goo)item), path });
        }
    }

    private static string Text(IGH_Goo goo)
    {
        if (goo == null) return "null";
        var datum = Prop(goo, "Gpc");
        if (datum != null) return goo.GetType().Name + " " + GpcNullValueChecks.TextOf(datum);
        return goo.GetType().Name + " " + goo;
    }

    private sealed class Run
    {
        internal List<string> Outputs = new List<string>();
        internal List<string> Messages = new List<string>();
        internal int FemOutputs;
    }

    private static Run Solve(GH_Component original, Func<IGH_Goo, IGH_Goo> map, ISet<string> kinds)
    {
        var doc = new GH_Document();
        try
        {
            var copy = Copy(original, doc);
            for (int i = 0; i < original.Params.Input.Count; i++)
                SetInput(copy.Params.Input[i], original.Params.Input[i].VolatileData, map);
            doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
            var run = new Run();
            foreach (var level in new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark })
                foreach (var m in copy.RuntimeMessages(level)) run.Messages.Add(level + ": " + m);
            foreach (var p in copy.Params.Output)
            {
                var data = p.VolatileData;
                for (int b = 0; b < data.PathCount; b++)
                {
                    var branch = data.get_Branch(b);
                    run.Outputs.Add($"{p.Name} {data.get_Path(b)} {branch.Count}");
                    foreach (var item in branch)
                    {
                        var goo = item as IGH_Goo;
                        run.Outputs.Add(Text(goo));
                        if (goo != null && kinds.Contains(goo.GetType().Name) && goo.ScriptVariable() != null) run.FemOutputs++;
                    }
                }
            }
            return run;
        }
        finally { doc.Dispose(); }
    }

    internal static int Regression(string lane)
    {
        var types = lane switch { "elements" => Lane1Types, _ => throw new ArgumentException("Unknown lane: " + lane) };
        var kinds = new HashSet<string>(lane switch { "elements" => ElementGoo, _ => Array.Empty<string>() }, StringComparer.Ordinal);
        BridgeExampleMigration.Initialize();
        var byType = new SortedDictionary<string, (int Instances, int Items)>(StringComparer.Ordinal);
        var failures = new List<string>();
        int files = 0;
        foreach (var file in BuildModelScan.ScopeFiles())
        {
            string label = Path.GetRelativePath(ExamplePaths.Root, file);
            using var doc = GrasshopperBridgeExample.Read(file); doc.FilePath = file;
            // No analysis runs (the Run button is never pressed: the definitions may name outputs in the main checkout).
            foreach (var o in doc.Objects.OfType<IGH_ActiveObject>())
                if (o.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent") o.Locked = true;
            BridgeSegmentExcelTests.Solve(doc, true);
            files++;
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => types.Contains(c.GetType().Name)).ToArray())
            {
                bool converted = c.Params.Input.Any(p => p.VolatileData.AllData(true).Any(g => g != null && kinds.Contains(g.GetType().Name)));
                if (!converted) continue;
                string where = $"{label}: {c.Name} [{c.NickName}]";
                Run fem, gpc;
                try { fem = Solve(c, g => g.Duplicate(), kinds); }
                catch (Exception ex) { failures.Add(where + ": the copy with the inputs of today fails: " + ex.Message); continue; }
                try { gpc = Solve(c, g => GpcOnly(g, kinds), kinds); }
                catch (Exception ex) { failures.Add(where + ": the copy with GPC-only inputs fails: " + ex.GetBaseException().Message); continue; }
                var (n, items) = byType.TryGetValue(c.GetType().Name, out var v) ? v : (0, 0);
                byType[c.GetType().Name] = (n + 1, items + fem.Outputs.Count);
                if (!fem.Messages.SequenceEqual(gpc.Messages)) failures.Add(where + ": messages " + string.Join(" / ", fem.Messages) + " -> " + string.Join(" / ", gpc.Messages));
                int diff = fem.Outputs.Zip(gpc.Outputs).Count(p => p.First != p.Second);
                if (fem.Outputs.Count != gpc.Outputs.Count || diff != 0)
                {
                    var first = fem.Outputs.Zip(gpc.Outputs).FirstOrDefault(p => p.First != p.Second);
                    failures.Add($"{where}: outputs {fem.Outputs.Count} -> {gpc.Outputs.Count}, {diff} different; first: {Short(first.First)} -> {Short(first.Second)}");
                }
                if (gpc.FemOutputs != 0) failures.Add($"{where}: {gpc.FemOutputs} outputs with a FEM value from GPC-only inputs");
            }
        }
        foreach (var t in types.Where(t => !byType.ContainsKey(t))) Console.WriteLine("GPC-ONLY " + lane + ": not met in the examples: " + t);
        foreach (var p in byType) Console.WriteLine($"GPC-ONLY {lane}: {p.Key} {p.Value.Instances} instances, {p.Value.Items} output lines");
        foreach (var f in failures.Take(60)) Console.WriteLine("  " + f);
        if (failures.Count != 0) throw new Exception($"GPC-ONLY {lane} FAIL: {failures.Count} differences or failures (first: {failures[0]})");
        Console.WriteLine($"GPC-ONLY {lane} PASS: {files} definitions, {byType.Sum(p => p.Value.Instances)} instances of {byType.Count} components replayed with GPC-only inputs, the same outputs");
        return 0;
    }

    private static string Short(string s) => s == null ? "null" : s.Length <= 240 ? s : s.Substring(0, 240) + "…";
}
#endif
