#if GPC_PILOT
using System;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

/// <summary>
/// F6 (a)/(b), R5 (native): Build BridGHe Model merges every FEM action from the rows of its analysed GPC model (the GPC source the Bridge
/// Action components attach), and the merged numbers equal bit for bit those of the former merge of the FEM result objects (zero without
/// sign on both sides). The check rebuilds the merge from the inputs of the component twice, with and without the GPC sources, and compares
/// every merged sample of every beam (case, station, six components); it requires every FEM action to have its GPC source.
/// <para><b>TRANSITIONAL (R6.6 lane R2b):</b> the parity with the FEM merge of the inputs of the 50 and 99 components runs only in those natives,
/// which the lane does not run; P2 retires it. Its constraints, reactions and limit cases are checked on GPC data alone, with explicit expected
/// values, by the regression <c>r2b-merge-forces</c> (<see cref="R2bMergeForcesChecks"/>). Blob d71cd49 (unchanged f2ef267, bbf84de, c23263a).</para>
/// </summary>
internal static class GpcMergeCheck
{
    internal static void Require(GH_Component merge, string label)
    {
        void Fail(string what) => throw new Exception("GPC MERGE FAIL (" + label + "): " + what);
        var models = merge.Params.Input[0].VolatileData.AllData(true).Select(g => (ModelModel)g.GetType().GetProperty("Value").GetValue(g)).ToArray();
        var phases = merge.Params.Input[1].VolatileData.AllData(true).Select(g => (BridgePhaseDefinition)((IGH_Goo)g).ScriptVariable()).ToArray();
        string Cell(IGH_Goo goo) => goo?.ScriptVariable() is IFormattable n ? n.ToString(null, CultureInfo.InvariantCulture) : goo?.ScriptVariable()?.ToString() ?? "";
        var table = merge.Params.Input[2].VolatileData;
        var combinations = Enumerable.Range(0, table.PathCount).Select(b => table.get_Branch(b).Cast<IGH_Goo>().Select(Cell).ToArray()).ToArray();
        int method = ((GH_Integer)merge.Params.Input[3].VolatileData.AllData(true).Single()).Value;
        bool additional = merge.Params.Input[4].VolatileData.AllData(true).Cast<GH_Boolean>().SingleOrDefault()?.Value ?? false;
        var forceActions = phases.SelectMany(p => p.Actions).Where(a => !string.IsNullOrWhiteSpace(a.Case)).ToArray();
        if (forceActions.Length == 0 || forceActions.Any(a => a.GpcSource == null)) Fail("every FEM action has its analysed GPC source");
        double tolerance = global::Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001; // Settings.Tolerance of the plug-in
        var gpc = BridgeWorkflowBuilder.Build(models, phases, combinations, (BridgeWorkflowMethod)method, tolerance, additional);
        var fem = BridgeWorkflowBuilder.Build(models, phases.Select(p => p with { Actions = p.Actions.Select(a => a with { GpcSource = null }).ToArray() }),
            combinations, (BridgeWorkflowMethod)method, tolerance, additional);
        static string Bits(double v) => BitConverter.DoubleToInt64Bits(v == 0 ? 0.0 : v).ToString("X16", CultureInfo.InvariantCulture);
        if (gpc.Samples.Length != fem.Samples.Length) Fail($"{gpc.Samples.Length} beams from GPC, {fem.Samples.Length} from FEM");
        long values = 0;
        for (int k = 0; k < gpc.Samples.Length; k++)
        {
            var a = gpc.Samples[k]; var b = fem.Samples[k];
            if (a.Count != b.Count) Fail($"beam {k + 1}: {a.Count} samples from GPC, {b.Count} from FEM");
            for (int i = 0; i < a.Count; i++)
            {
                var x = a[i]; var y = b[i];
                string tx = x.Case + "|" + Bits(x.Sample.Station) + "|" + string.Join(",", x.Sample.Values.Select(Bits));
                string ty = y.Case + "|" + Bits(y.Sample.Station) + "|" + string.Join(",", y.Sample.Values.Select(Bits));
                if (tx != ty) Fail($"beam {k + 1}, sample {i}: GPC {tx} / FEM {ty}");
                values += 8;
            }
        }
        if (!gpc.Warnings.SequenceEqual(fem.Warnings) || !gpc.Summary.SequenceEqual(fem.Summary)) Fail("warnings and summary equal");
        // R8, BridgeSupportReactions (Bridge Detail Stations): every support of the reference model, every combination, from the GPC rows of
        // the action sources equal to the former FEM reactions (or the same error).
        var stripped = phases.Select(p => p with { Actions = p.Actions.Select(a => a with { GpcSource = null }).ToArray() }).ToArray();
        var attachedOutput = merge.Params.Output[0].VolatileData.AllData(true).Single();
        var attachedBridge = (BridGHe.Common.Gpc.GpcBridge)attachedOutput.GetType().GetProperty("GpcBridge").GetValue(attachedOutput) ?? throw new Exception("GPC MERGE FAIL (" + label + "): no GPC bridge model attached");
        var supports = forceActions[0].Source.Nodes.Where(n => n.Results.OfType<NodeReactResultModel>().Any()).Select(n => n.Position).Take(8).ToArray();
        int reactions = 0, computed = 0;
        foreach (var support in supports)
            foreach (var combination in gpc.Model.LoadCombinations)
            {
                string Run(System.Collections.Generic.IEnumerable<BridgePhaseDefinition> definitions)
                {
                    try { return Bits(BridgeSupportReactions.SignedKN(gpc.Model, definitions, support, combination.Name, Rhino.Geometry.Vector3d.ZAxis, 1e-3)); }
                    catch (ArgumentException e) { return "error " + e.Message; }
                }
                // P2T request 3: the same reaction on the GPC bridge model alone (its records and combinations).
                string RunBridge() { try { return Bits(BridgeSupportReactions.SignedKN(attachedBridge, phases, support, combination.Name, Rhino.Geometry.Vector3d.ZAxis, 1e-3)); } catch (ArgumentException e) { return "error " + e.Message; } }
                string x = Run(phases), y = Run(stripped), z = RunBridge();
                if (x != z) Fail($"support reaction at {support}, combination {combination.Name}: merged FEM model {x} / GPC bridge model {z}");
                if (x != y) Fail($"support reaction at {support}, combination {combination.Name}: GPC {x} / FEM {y}");
                reactions++; if (!x.StartsWith("error ", StringComparison.Ordinal)) computed++;
            }
        Console.WriteLine($"GPC MERGE PASS ({label}): {forceActions.Length} FEM actions merged from their analysed GPC models, {gpc.Samples.Sum(s => s.Count)} merged samples of {gpc.Samples.Length} beams equal to the merge of the FEM results ({values} values); {reactions} support reactions ({supports.Length} supports, {computed} computed, the others the same error) equal.");
    }
}
#endif
