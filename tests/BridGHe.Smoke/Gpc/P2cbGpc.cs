using System.Collections.Generic;
using BridGHe.Common.Gpc;

/// <summary>
/// P2CB (13/11/2026): the GPC-only entries the ported checks of lane P2CB call, in one place, so that the serial lane that removes the FEM APIs
/// moves one line when it moves the GPC overloads of <c>FemmAnalysisSeam</c> (P2CUT, «Da sapere prima di cancellare»).
/// </summary>
internal static class P2cbGpc
{
    /// <summary>Straus R3 Export of a model given by its GPC input alone (the GPC overload of the former seam: the GPC input written with the 0.3.0
    /// options, no solve); the warnings when asked.</summary>
    internal static void Export(GpcModels.Conversion input, string outputPath, List<string> warnings = null) =>
        BridGHe.Common.Gpc.Legacy.FemmAnalysisSeam.Export(null, input, outputPath, input.Tolerance, warnings ?? new List<string>());
}
