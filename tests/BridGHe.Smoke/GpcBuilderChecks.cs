#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.1 (frozen API for R6.2): the harness of the regressions of the GPC builders of the producer components, one file per lane of R6.2
/// (<see cref="GpcBuilderChecksA"/> lane A: elements, attributes, loads; <see cref="GpcBuilderChecksB"/> lane B: sections, properties,
/// materials, bridge geometry). A run solves one component headless on the given inputs; every output datum of a GH type with a GPC datum
/// must have it attached (built on GPC), and <see cref="GpcDraftCheckHost"/> compares it with the conversion of its FEM value on attach
/// (a difference fails the component, so the run). The inputs of a run are goos, or the outputs of an earlier run (<see cref="Output"/>).
/// </summary>
internal sealed class GpcBuilderChecks : IDisposable
{
    private readonly Assembly assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
    private readonly GH_Document document = new GH_Document { Enabled = true };
    internal readonly List<string> Failures = new List<string>();
    internal int Runs, Attached;
    private readonly string lane;

    internal GpcBuilderChecks(string lane) { this.lane = lane; GH_Document.EnableSolutions = true; }

    /// <summary>A goo of the plug-in by type name and constructor arguments (e.g. <c>Goo("GH_Node", new Rhino.Geometry.Point3d(1, 2, 3))</c>).</summary>
    internal IGH_Goo Goo(string type, params object[] args) => (IGH_Goo)Activator.CreateInstance(assembly.GetTypes().Single(t => t.Name == type), args);

    /// <summary>Solves the component <paramref name="type"/> with the inputs given (input index, goo or null: empty; a list for a list
    /// input), and returns the data of every output. <paramref name="attachedOutputs"/>: the outputs whose GH data must carry a datum
    /// attached by a GPC builder.</summary>
    internal List<IGH_Goo>[] Run(string type, int[] attachedOutputs, params (int Input, object Value)[] inputs)
    {
        var component = (GH_Component)Activator.CreateInstance(assembly.GetTypes().Single(t => t.Name == type));
        component.CreateAttributes(); document.AddObject(component, false);
        foreach (var (input, value) in inputs)
        {
            var p = component.Params.Input[input];
            var data = p.GetType().GetProperty("PersistentData").GetValue(p);
            data.GetType().GetMethod("Clear").Invoke(data, null);
            var append = data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 1);
            foreach (var v in value is System.Collections.IEnumerable list && !(value is string) && !(value is IGH_Goo) ? list.Cast<object>() : new[] { value })
                if (v != null) append.Invoke(data, new[] { v is IGH_Goo ? v : GH_Convert.ToGoo(v) });
            p.ExpireSolution(false);
        }
        document.NewSolution(false, GH_SolutionMode.Silent);
        Runs++;
        string label = lane + " " + type + " run " + Runs;
        var errors = component.RuntimeMessages(GH_RuntimeMessageLevel.Error);
        if (errors.Count > 0) Failures.Add(label + ": " + string.Join("; ", errors));
        var outputs = component.Params.Output.Select(o => o.VolatileData.AllData(true).ToList()).ToArray();
        foreach (int o in attachedOutputs)
        {
            if (outputs[o].Count == 0) { Failures.Add(label + ": no output " + o); continue; }
            foreach (var goo in outputs[o])
            {
                var flag = goo?.GetType().GetProperty("HasAttachedGpc");
                if (flag == null) { Failures.Add(label + ": output " + o + " is not a GH type with a GPC datum (" + goo?.GetType().Name + ")"); continue; }
                if (!(bool)flag.GetValue(goo)) Failures.Add(label + ": output " + o + " carries no GPC datum built on GPC");
                else Attached++;
            }
        }
        document.RemoveObject(component, false);
        return outputs;
    }

    /// <summary>The first datum of an output of a run (the input of the next).</summary>
    internal static IGH_Goo Output(List<IGH_Goo>[] outputs, int index) => outputs[index].First();

    internal void Finish(int minimumRuns)
    {
        if (Failures.Count > 0) throw new Exception("GPC BUILDERS " + lane + " FAIL:\n" + string.Join("\n", Failures.Take(30)));
        if (Runs < minimumRuns) throw new Exception($"GPC BUILDERS {lane} FAIL: only {Runs} runs");
        Console.WriteLine($"GPC BUILDERS {lane} PASS: {Runs} component runs, {Attached} output data built on GPC equal to the conversion of their FEM values (DRAFT CHECK)");
    }

    public void Dispose() => document.Dispose();
}

/// <summary>R6.2, lane A (elements, attributes, loads): the runs of the converted producer components. R6.1: the worked example (Node Load).</summary>
internal static class GpcBuilderChecksA
{
    internal static void Regression()
    {
        using var h = new GpcBuilderChecks("A");
        // R6.2 lane A (R62A): the runs, the worked example (Node Load) included, are in GpcBuilderChecksA.cs.
        GpcBuilderChecksARuns.Run(h);
    }
}

// R6.2, lane B (beams): GpcBuilderChecksB is in its own file (GpcBuilderChecksB.cs).
#endif
