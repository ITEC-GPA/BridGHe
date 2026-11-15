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
/// <para>R6.6 lane R1 (11/11): every run is solved twice. The dual run of today (FEM-backed inputs; the DRAFT CHECK against the conversion of the FEM
/// value is TRANSITIONAL, until P2) and the run on the path of P2 (<see cref="PathOfP2"/>: every input by its GPC datum alone, the switches of the
/// GPC-only producers on): no FEM value in its outputs, the errors the run declares on both paths, its data counted against the attached data (the
/// same builders: a consistency of the two paths, not an oracle). The oracles without FEM are the explicit expected values of the lanes
/// (<see cref="Expect"/>: positions, restraint flags, analytic areas, material kinds, messages), written from the inputs of the runs.</para>
/// </summary>
internal sealed class GpcBuilderChecks : IDisposable
{
    private readonly Assembly assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
    private readonly GH_Document document = new GH_Document { Enabled = true };
    internal readonly List<string> Failures = new List<string>();
    internal int Runs, Attached;
    /// <summary>R1: the explicit expected values checked (positions, restraints, analytic areas…), written from the inputs of the runs.</summary>
    internal int Explicit;
    /// <summary>R1: an explicit expected value (from the inputs of the run, not from the FEM path).</summary>
    internal void Expect(bool ok, string what) { Explicit++; if (!ok) Failures.Add(lane + " explicit: " + what); }
    private readonly string lane;

    internal GpcBuilderChecks(string lane) { this.lane = lane; GH_Document.EnableSolutions = true; }

    /// <summary>A goo of the plug-in by type name and constructor arguments (e.g. <c>Goo("GH_Node", new Rhino.Geometry.Point3d(1, 2, 3))</c>).</summary>
    internal IGH_Goo Goo(string type, params object[] args) => (IGH_Goo)Activator.CreateInstance(assembly.GetTypes().Single(t => t.Name == type), args);

    /// <summary>Solves the component <paramref name="type"/> with the inputs given (input index, goo or null: empty; a list for a list
    /// input), and returns the data of every output. <paramref name="attachedOutputs"/>: the outputs whose GH data must carry a datum
    /// attached by a GPC builder.</summary>
    internal List<IGH_Goo>[] Run(string type, int[] attachedOutputs, params (int Input, object Value)[] inputs)
    {
        var outputs = Dual(type, attachedOutputs, inputs);
        if (P2Path) PathOfP2(type, attachedOutputs, inputs, outputs);
        return outputs;
    }

    /// <summary>R6.6 lane R1: every run again on the path of P2 (off with <c>BRIDGHE_R1_P2_PATH=0</c>).</summary>
    internal static bool P2Path => Environment.GetEnvironmentVariable("BRIDGHE_R1_P2_PATH") != "0";
    internal int P2Runs, P2Data, P2Same;

    /// <summary>The GH type of a FEM-backed input given by its GPC datum alone (<c>FromGpc</c>, its GUID and declared material kind kept), or the input.</summary>
    internal object GpcOnly(object value)
    {
        if (value is IGH_Goo goo && goo.GetType().GetProperty("Gpc") is { } gpcProperty && goo.ScriptVariable() != null && goo.GetType().Name != "GH_Model")
        {
            object datum;
            try { datum = gpcProperty.GetValue(goo); } catch (TargetInvocationException) { unmappable = true; return value; } // a value the reader refuses: no GPC datum
            if (datum == null || goo.GetType().GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static) is not { } from) return value;
            var parameters = from.GetParameters();
            var args = new object[parameters.Length]; args[0] = datum;
            for (int i = 1; i < args.Length; i++)
                args[i] = parameters[i].Name == "guid" ? goo.GetType().GetProperty("Guid")?.GetValue(goo) : parameters[i].Name == "kind" ? goo.GetType().GetProperty("GpcKind")?.GetValue(goo) : null;
            var only = (IGH_Goo)from.Invoke(null, args);
            foreach (var name in new[] { "AnalysedGpc", "AnalysedGpcModel", "GpcMembraneBending" })
                if (goo.GetType().GetProperty(name) is { CanWrite: true } p) p.SetValue(only, p.GetValue(goo));
            return only;
        }
        return value is System.Collections.IEnumerable list && !(value is string) && !(value is IGH_Goo) ? list.Cast<object>().Select(GpcOnly).ToArray() : value;
    }

    /// <summary>R6.6 lane R1: the run on the path of P2: every input given by its GPC datum alone (<see cref="GpcOnly"/>), the switches of the GPC-only
    /// producers on. Independent of the FEM value: no output with a FEM value; the errors are those the run declares (the same as the dual run, which
    /// <see cref="ExpectError"/> and the error helpers of the lanes accept on both); an output where the dual run has one. Not an oracle of the data
    /// (the same GPC builders): its data equal to the data the dual run attaches, but for GUIDs, are counted (<see cref="P2Same"/>).</summary>
    private bool unmappable;
    internal int P2Skipped, P2Refused;

    private void PathOfP2(string type, int[] attachedOutputs, (int Input, object Value)[] inputs, List<IGH_Goo>[] dual)
    {
        // An input the reader refuses has no GPC datum: no run on the path of P2 (it cannot exist there), counted.
        unmappable = false;
        var mappedInputs = inputs.Select(i => (i.Input, Value: GpcOnly(i.Value))).ToArray();
        if (unmappable) { P2Skipped++; return; }
        var component = (GH_Component)Activator.CreateInstance(assembly.GetTypes().Single(t => t.Name == type));
        component.CreateAttributes(); document.AddObject(component, false);
        foreach (var (input, mapped) in mappedInputs)
        {
            var p = component.Params.Input[input];
            var data = p.GetType().GetProperty("PersistentData").GetValue(p);
            data.GetType().GetMethod("Clear").Invoke(data, null);
            var append = data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 1);
            foreach (var v in mapped is System.Collections.IEnumerable list && !(mapped is string) && !(mapped is IGH_Goo) ? list.Cast<object>() : new[] { mapped })
                if (v != null) append.Invoke(data, new[] { v is IGH_Goo ? v : GH_Convert.ToGoo(v) });
            p.ExpireSolution(false);
        }
        var switches = assembly.GetType("BridGHe.Grasshopper.Helpers.GpcOnlyElements", true);
        var generators = switches.GetProperty("GeneratorsWithoutFem"); var properties = switches.GetProperty("PropertiesWithoutFem");
        generators.SetValue(null, true); properties.SetValue(null, true);
        try { document.NewSolution(false, GH_SolutionMode.Silent); }
        finally { generators.SetValue(null, false); properties.SetValue(null, false); }
        P2Runs++;
        string label = lane + " " + type + " run " + Runs + " (path of P2)";
        var errors = component.RuntimeMessages(GH_RuntimeMessageLevel.Error);
        // A variant the builder refuses (the dual run gives its output without a datum attached): on the path of P2 the refusal of the builder is the
        // error of the component, with no output (decision 2 of passo 73), counted.
        bool refusedAlike = errors.Count > 0 && errors.All(e => e.Contains("refuses", StringComparison.Ordinal))
            && dual.SelectMany(o => o).Any(g => g?.GetType().GetProperty("HasAttachedGpc") is { } f && !(bool)f.GetValue(g)) && component.Params.Output.All(o => o.VolatileData.IsEmpty);
        if (refusedAlike) P2Refused++;
        else if (errors.Count > 0) Failures.Add(label + ": " + string.Join("; ", errors));
        var outputs = component.Params.Output.Select(o => o.VolatileData.AllData(true).ToList()).ToArray();
        for (int o = 0; o < outputs.Length; o++)
            foreach (var goo in outputs[o].Where(g => g?.GetType().GetProperty("Gpc") != null))
            {
                P2Data++;
                if (goo.ScriptVariable() != null && goo.GetType().GetProperty("HasAttachedGpc") != null) Failures.Add(label + ": output " + o + " has a FEM value on the path of P2");
            }
        foreach (int o in attachedOutputs)
        {
            if (dual[o].Count != 0 && outputs[o].Count == 0 && errors.Count == 0) { Failures.Add(label + ": no output " + o + " where the dual run has one"); continue; }
            foreach (var (a, b) in dual[o].Zip(outputs[o]))
            {
                // The texts of the replays (GpcOnlyReplay.Text): the datum modulo GUIDs, the bridge data by their values (keyed by the GUID of the beam).
                string x, y;
                try { x = GpcOnlyReplay.Text(a); y = GpcOnlyReplay.Text(b); }
                catch (Exception) { continue; }
                if (x == y) P2Same++;
                else Failures.Add(label + ": output " + o + ": the datum differs from the one the dual run attaches (GUIDs replaced by their ordinals)\n" + GpcDraftArchiveChecks.FirstDifference(x, y));
            }
        }
        document.RemoveObject(component, false);
    }

    /// <summary>The dual run of today: the FEM-backed inputs, the datum attached by the GPC builder beside the FEM value (TRANSITIONAL for the FEM value:
    /// <see cref="GpcDraftCheckHost"/> compares the datum with the conversion of the FEM value, until P2).</summary>
    private List<IGH_Goo>[] Dual(string type, int[] attachedOutputs, params (int Input, object Value)[] inputs)
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

    /// <summary>P2S: the last run had to fail with this error (a refused input, no output): its failure is the expected one.</summary>
    internal void ExpectError(string text)
    {
        // R1: the error of the dual run and the one of the run on the path of P2 (the same declared message).
        int removed = 0;
        while (Failures.Count > 0 && Failures[^1].Contains(text, StringComparison.Ordinal) && Failures[^1].Contains(" run " + Runs, StringComparison.Ordinal)) { Failures.RemoveAt(Failures.Count - 1); removed++; }
        if (removed == 0 || P2Path && removed < 2) Failures.Add(lane + " run " + Runs + ": expected the error " + text + (removed == 1 ? " on both paths (only one)" : ""));
    }

    /// <summary>R1: removes the failures of the last run on the path of P2 that contain <paramref name="text"/> (a refusal the run declares); true when one was there.</summary>
    internal bool ExpectP2Error(string text)
    {
        int before = Failures.Count;
        Failures.RemoveAll(f => f.Contains(" run " + Runs + " (path of P2)", StringComparison.Ordinal) && f.Contains(text, StringComparison.Ordinal));
        return Failures.Count < before;
    }

    /// <summary>The first datum of an output of a run (the input of the next).</summary>
    internal static IGH_Goo Output(List<IGH_Goo>[] outputs, int index) => outputs[index].First();

    internal void Finish(int minimumRuns)
    {
        if (Failures.Count > 0) throw new Exception("GPC BUILDERS " + lane + " FAIL:\n" + string.Join("\n", Failures.Take(30)));
        if (Runs < minimumRuns) throw new Exception($"GPC BUILDERS {lane} FAIL: only {Runs} runs");
        Console.WriteLine($"GPC BUILDERS {lane} PASS: {Runs} component runs, {Attached} output data built on GPC; " + (P2Path ? $"{P2Runs} runs on the path of P2 (inputs by their GPC data alone, switches on): {P2Data} output data without FEM value, {P2Same} equal to the data the dual run attaches but for GUIDs, the declared errors on both paths, {P2Refused} refusals of the builder as errors, {P2Skipped} runs on an input the reader refuses not run; " : "")
            + $"{Explicit} explicit expected values (R1); TRANSITIONAL: the attached data equal to the conversion of their FEM values (DRAFT CHECK)");
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
