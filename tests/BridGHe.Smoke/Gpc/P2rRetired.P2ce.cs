using System;
using System.Collections.Generic;

/// <summary>
/// P2CE (13/11/2026, rule U3 of the user, brief P2CE): the checks retired by name with the FEM code that the serial lane removes from the
/// product, key <c>scope|name</c> → reason (with the GPC coverage that replaces it). Read by <see cref="P2rRetired.Checks"/> (one hook line).
/// A retirement is never a widened tolerance: every other check keeps its oracle.
/// </summary>
internal static class P2rRetiredP2ce
{
    /// <summary>The retirements of lane P2CE, added to <see cref="P2rRetired.Checks"/> by its builder.</summary>
    internal static void AddTo(Action<string, string> add)
    {
        add("regression-part|gpc-builders-b: Merge Geometry equals ModelHelper.MergeGeometry",
            "the FEM helper ModelHelper.MergeGeometry, deleted in phase 1: it had the same code as the product function BridgeGeometryMerge.Merge, which Merge Geometry "
            + "uses; on GPC: the run of Merge Geometry (gpc-builders-b) and the explicit vertex, face and winding checks of merge-mesh, now on BridgeGeometryMerge.Merge (1 check)");
        add("regression-part|p2ce-reader: «Design» by value equals the CLR reading",
            "the FEM property of the section rules and family properties, deleted in phase 2b (decision 1 of the coordinator): the parity of the reading by value "
            + "(GpcLegacyPayloads.Design) with the archive of the CLR types, measured at 8e5a538 on 42 records built in memory with 12 FEM properties of the corpus "
            + "(fields, text of the rules with the ID of their property, GPC datum without GUIDs) and on the goo of a 0.3.0 rule: all equal; the corpus has no "
            + "«Design» item (inventory of phase 2a); on GPC: the 0.4.0 rules and family properties of gpc-only-properties and gpc-null-values");
    }

    /// <summary>The reason of a retirement of this lane, as <see cref="P2rRetired.Checks"/> records it.</summary>
    internal static string Reason(string key) => P2rRetired.Checks.TryGetValue(key, out var reason) ? reason : throw new KeyNotFoundException("P2CE: not retired by name: " + key);
}
