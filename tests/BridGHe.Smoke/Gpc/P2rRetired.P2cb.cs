using System;
using System.Collections.Generic;

/// <summary>
/// P2CB (13/11/2026, rule U3 of the user, brief P2CT): the checks of the native, parity and example side retired by name with the FEM model types
/// they used, key <c>scope|name</c> → reason (with the GPC coverage that replaces it). Read by <see cref="P2rRetired.Checks"/> (one hook line), so
/// <c>--regression &lt;retired name&gt;</c> answers with its reason as for the retirements of P2CUT. Scopes as in <see cref="P2rRetired.Checks"/>, and
/// <c>native</c> for a targeted native run that no gate runs. A retirement is never a widened tolerance: every other check keeps its oracle.
/// </summary>
internal static class P2rRetiredP2cb
{
    /// <summary>The retirements of lane P2CB, added to <see cref="P2rRetired.Checks"/> by its builder (the reason gets the decision of P2CUT).</summary>
    internal static void AddTo(Action<string, string> add)
    {
        const string FemType = "a check of the FEM model types (BridGHe.Common.Models), which the GPC path does not use";
        add("core-native|par native transitional parity difference", "transitional (R4b): the 0.3.0 parity model against the conversion of its former FEM model (R4bInputs.Former, a printed line, not a condition); "
            + "the GPC parity model read back from Straus7 against the 0.3.0 rules: ParityNative (PAR, core-native)");
        add("native|gpc-pilot", FemType + " (the F4 pilot, --gpc-pilot, in no gate: the FEM solve of Build Model against GpcPilotAdapter, the F4 adapter of a FEM model); "
            + "on GPC: the analysis of the GPC-only input (gpc-only-analysis-native, --examples), the archive of an analysed GPC model and its reopening (gpc-results-native, gpc-draft-archive)");
    }

    /// <summary>The reason of a retirement of this lane, as <see cref="P2rRetired.Checks"/> records it.</summary>
    internal static string Reason(string key) => P2rRetired.Checks.TryGetValue(key, out var reason) ? reason : throw new KeyNotFoundException("P2CB: not retired by name: " + key);
}
