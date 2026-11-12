#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>The check of the GPC workflow executor in the native runners of 50 and 99.
/// <para><b>P2R (T2/R5, 12/11):</b> on GPC data alone. Until P2 the recipe of the FEM merged model, written to a former archive and read back by
/// the C1 reader, had to equal the recipe of the merged objects (key <c>c5|recipe</c> of the native capture of 99, the fingerprint of the
/// recipe the C1 reader read from the FEM merged archive). The FEM merged model is gone (P2K/P2D): the recipe of the GPC bridge model is
/// compared with the captured fingerprint instead.</para></summary>
internal static partial class C5WorkflowComparison
{
    /// <summary>C5 in the native runners: Build Bridge Model attached the GPC bridge model, whose workflow validates on its model, and every Super
    /// Element Check used the GPC workflow executor (no remark of BridGHe's phases); with <paramref name="recipe"/> the fingerprint of the recipe
    /// against the native capture of <paramref name="label"/> (key <c>c5|recipe</c>, 99 only).</summary>
    internal static void RequireGpcChecks(object mergedGoo, IEnumerable<Grasshopper.Kernel.GH_Component> checks, bool recipe, string label)
    {
        var bridge = (BridGHe.Common.Gpc.GpcBridge)mergedGoo.GetType().GetProperty("GpcBridge").GetValue(mergedGoo);
        if (bridge == null) throw new Exception("C5: Build Bridge Model gave no GPC bridge model.");
        bridge.Workflow.Validate(bridge.Model); bridge.Workflow.ValidateSources(bridge.Model, new[] { bridge.Model });
        if (recipe) NativeParityCapture.Datum(label, "c5|recipe", null, bridge.Workflow.Fingerprint());
        var phases = checks.SelectMany(c => c.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Remark)).Where(m => m.StartsWith("Verifiche con le fasi BridGHe", StringComparison.Ordinal)).ToArray();
        if (phases.Length != 0) throw new Exception("C5: a Super Element Check used BridGHe's phases: " + phases[0]);
        Console.WriteLine($"C5 GPC CHECKS: GPC bridge model {bridge.Model.BeamElements.Count} beams, {bridge.Workflow.Phases.Length} phases, {bridge.Workflow.Phases.Sum(p => p.Actions.Length)} actions; " +
            $"{checks.Count()} Super Element Check with the GPC workflow executor" + (recipe ? "; recipe recorded against the native capture" : ""));
    }
}
#endif
