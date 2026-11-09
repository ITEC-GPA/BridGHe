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
        // R6.3: bridge 3 removed (the drafts of Bridge State Override carry their own property data, signed with the GPC fingerprint as
        // every draft): compared as they are.
        // R6.2 part C: the Aluminium component (user decision of 6/11): fu 262 on GPC, 165.5 in the conversion of its FEM value (0.3.0), a declared
        // difference by name; every other value of the material compared.
        if (datum is GPC.Model.Materials.AluminiumMaterial && fem is GPC.Model.Materials.Material fm && GpcMaterialKindChecks.DeclaredAluminium((GPC.Model.Materials.Material)datum, fm))
        {
            lock (Gate) { string t = type + " (Aluminium: fu 165.5 -> 262, declared)"; Equal.TryGetValue(t, out int c); Equal[t] = c + 1; }
            return;
        }
        // L2C: with the BridGHe data beside the datum that the canonical texts do not carry (the material kind of a beam property).
        // PAR (A08): a stale section state compared as stale (the reader marks it with the FEM signature, a builder with the GPC fingerprint).
        string attached, converted;
        GpcDraftArchiveChecks.CompareStaleAsStale = true;
        try { attached = GpcDraftCapture.Text(datum) + GpcDraftArchiveChecks.Extras(datum); converted = fem == null ? "REFUSED" : GpcDraftCapture.Text(fem) + GpcDraftArchiveChecks.Extras(fem); }
        finally { GpcDraftArchiveChecks.CompareStaleAsStale = false; }
        lock (Gate)
        {
            if (attached == converted) { Equal.TryGetValue(type, out int n); Equal[type] = n + 1; return; }
            different++;
            // P2Z: the two texts of the first difference in the folder named by BRIDGHE_DRAFT_DIFF (diagnosis).
            if (different == 1 && Environment.GetEnvironmentVariable("BRIDGHE_DRAFT_DIFF") is { Length: > 0 } dump)
            {
                System.IO.Directory.CreateDirectory(dump);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dump, "converted.txt"), converted); System.IO.File.WriteAllText(System.IO.Path.Combine(dump, "attached.txt"), attached);
            }
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
