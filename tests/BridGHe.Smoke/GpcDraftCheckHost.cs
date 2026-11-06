#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;

/// <summary>
/// R6.1 (frozen API for R6.2): the check of <see cref="GpcDraftCheck"/> in every run of the Smoke host. Every GPC datum a component attaches
/// to a GH type while its FEM value is still built beside it is compared with the conversion of that FEM value (same identity), by the
/// canonical texts of the capture (<see cref="GpcDraftCapture.Text"/>): a difference is printed (first different line) and thrown in the
/// component (its output fails), and the run exits 1. The summary line <c>DRAFT CHECK</c> gives the counts by GH type.
/// </summary>
internal static class GpcDraftCheckHost
{
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, int> Equal = new Dictionary<string, int>(StringComparer.Ordinal);
    private static int different;

    internal static void Install() => GpcDraftCheck.Verify = Verify;

    private static void Verify(string type, object datum, Func<object> conversion)
    {
        object fem;
        try { fem = conversion(); }
        catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException) { fem = null; }
        // Bridge 3 (until R6.3): a beam draft built on GPC with a GPC property (Bridge State Override: signature "GPC:", no section recipe)
        // takes, in Build Model, the property data of the conversion of its FEM value; the rest of the draft is compared as it is.
        if (datum is BridGHe.Common.Gpc.Legacy.LegacyBeamDraft b && b.Property?.Signature?.StartsWith("GPC:", StringComparison.Ordinal) == true
            && fem is BridGHe.Common.Gpc.Legacy.LegacyBeamDraft f)
        {
            datum = GpcBeamDrafts.WithPropertyOf(b, f);
            type += " (GPC property: the property data of the conversion, bridge 3)";
        }
        string attached = GpcDraftCapture.Text(datum), converted = fem == null ? "REFUSED" : GpcDraftCapture.Text(fem);
        lock (Gate)
        {
            if (attached == converted) { Equal.TryGetValue(type, out int n); Equal[type] = n + 1; return; }
            different++;
            Console.WriteLine("DRAFT CHECK DIFFERENCE " + type + ": the attached GPC datum differs from the conversion of its FEM value\n"
                + GpcDraftArchiveChecks.FirstDifference(converted, attached));
        }
        throw new InvalidOperationException("GpcDraftCheckDifference: the GPC datum attached to a " + type + " differs from the conversion of its FEM value.");
    }

    /// <summary>Prints the summary (when a datum was attached) and turns a passing exit code into 1 after a difference.</summary>
    internal static int Finish(int code)
    {
        lock (Gate)
        {
            if (Equal.Count == 0 && different == 0) return code;
            Console.WriteLine($"DRAFT CHECK: {Equal.Values.Sum()} attached GPC data equal to the conversion of their FEM values ({string.Join(", ", Equal.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + " " + e.Value))}), {different} different");
            return different > 0 && code == 0 ? 1 : code;
        }
    }
}
#endif
