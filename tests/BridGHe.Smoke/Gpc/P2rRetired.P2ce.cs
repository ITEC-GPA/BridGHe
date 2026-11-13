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
    }

    /// <summary>The reason of a retirement of this lane, as <see cref="P2rRetired.Checks"/> records it.</summary>
    internal static string Reason(string key) => P2rRetired.Checks.TryGetValue(key, out var reason) ? reason : throw new KeyNotFoundException("P2CE: not retired by name: " + key);
}
