#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;

/// <summary>FEM path (the native runners of 50 and 99, until the GPC <c>GH_Model</c> of R6.4 and the removal of R6.5): the check of the GPC
/// workflow executor in a native run, with the recipe of the FEM merged model written to a former archive and read back by the C1 reader.</summary>
internal static partial class C5WorkflowComparison
{
    /// <summary>C5 in the native runners: Build Bridge Model attached the GPC bridge model, whose recipe (read from the merged objects) is the one the
    /// C1 reader reads from the merged archive (when <paramref name="compareArchive"/>: 8.6 MB for 99, 448 MB for 50), and every Super Element
    /// Check used the GPC workflow executor (no remark of BridGHe's phases).</summary>
    internal static void RequireGpcChecks(object mergedGoo, BridGHe.Common.Models.ModelModel merged, IEnumerable<Grasshopper.Kernel.GH_Component> checks, bool compareArchive)
    {
        var bridge = (BridGHe.Common.Gpc.GpcBridge)mergedGoo.GetType().GetProperty("GpcBridge").GetValue(mergedGoo);
        if (bridge == null) throw new Exception("C5: Build Bridge Model gave no GPC bridge model.");
        bridge.Workflow.Validate(bridge.Model); bridge.Workflow.ValidateSources(bridge.Model, new[] { bridge.Model });
        if (compareArchive)
        {
            var legacy = LegacyWorkflowImport.Import(LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(merged), int.MaxValue), bridge.Model);
            if (legacy.Fingerprint() != bridge.Workflow.Fingerprint()) throw new Exception("C5: the recipe of the merged objects differs from the one of the merged archive.");
        }
        var phases = checks.SelectMany(c => c.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Remark)).Where(m => m.StartsWith("Verifiche con le fasi BridGHe", StringComparison.Ordinal)).ToArray();
        if (phases.Length != 0) throw new Exception("C5: a Super Element Check used BridGHe's phases: " + phases[0]);
        Console.WriteLine($"C5 GPC CHECKS: GPC bridge model {bridge.Model.BeamElements.Count} beams, {bridge.Workflow.Phases.Length} phases, {bridge.Workflow.Phases.Sum(p => p.Actions.Length)} actions; " +
            $"{checks.Count()} Super Element Check with the GPC workflow executor" + (compareArchive ? "; recipe equal to the C1 reader of the merged archive" : ""));
    }
}
#endif
