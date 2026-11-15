using System;
using System.Collections.Generic;

/// <summary>
/// P2CD (13/11/2026, rule U3 of the user, brief P2CCD): the checks and tools of the shared helpers and the native side retired by name with the
/// FEM model types they used, key <c>scope|name</c> → reason (with the GPC coverage that replaces it). Read by <see cref="P2rRetired.Checks"/>
/// (one hook line), as the retirements of P2CUT and P2CB. A retirement is never a widened tolerance: every other check keeps its oracle.
/// </summary>
internal static class P2rRetiredP2cd
{
    /// <summary>The retirements of lane P2CD, added to <see cref="P2rRetired.Checks"/> by its builder.</summary>
    internal static void AddTo(Action<string, string> add)
    {
        add("regression-part|gpc-build-model: GPC NATIVE EXAMPLES (transitional) the GPC catalogue against the conversion of the FEM catalogue",
            "transitional (R0): each GPC example of GpcNativeExamples against the conversion of its FEM model of NativeExamples.Create (GpcInputComparison.Require, "
            + "RequireDrafts, Conversion: FEM types); on GPC: the analytic values of the examples 01-10 on the GPC-only path (--examples, core-native), Build Model on their "
            + "inputs (gpc-build-model), the R6.0 capture of their data (gpc-draft-archive) (10 printed lines)");
        add("tool|--composite-texts", "the evidence of the steel capture rule of P2X (CompositeTextDump): the texts of the FEM composite values of the GH data (CompositeAnalysisPropertyChecks.Texts, "
            + "CompareFem), which the GPC path does not carry; the declared rule itself stays in NativeParityCapture (natives 50 and 99)");
        add("generator|--example50-rev001-inputs", "an authoring tool of example 50 REV001 in no gate (BridgeRevisionInputsExample, decision E4 of P2R): it read the FEM base property "
            + "of the GH input and wrote the example; the examples on disk are not touched; the 50 definitions: the native 50 and its capture, scan, payloads");
        add("generator|--parametric-bridge-phases", "the generator of example 50 and its checks (ParametricBridgePhasesExample, ParametricBridgePhasesChecks.ValidateChecks on the FEM "
            + "outputs of its components), already without a command since P2R (E4): removed with the FEM types; the folder name stays (ParametricBridgePhasesExample.Id)");
    }

    /// <summary>The reason of a retirement of this lane, as <see cref="P2rRetired.Checks"/> records it.</summary>
    internal static string Reason(string key) => P2rRetired.Checks.TryGetValue(key, out var reason) ? reason : throw new KeyNotFoundException("P2CD: not retired by name: " + key);
}
