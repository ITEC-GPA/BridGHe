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
        add("regression-part|p2ce-reader: «LoadCases» by value equals the CLR reading",
            "the archive of the CLR types of the FEM cases, deleted in phase 2b: the reading by value of the «LoadCases» item of Staged Construction "
            + "(GpcLegacyPayloads.Cases) equalled the CLR reading converted as before on 41 lists (the cases of the 61 example files as 0.3.0 wrote the item, "
            + "single cases, the empty list), measured in phase 2a (gates p2ce3/p2ce4) and 2b; the lists are not frozen (decision 2); on GPC: the 14 checks of "
            + "P2P-01 (the old development file with LoadCases = [], the records chunk, the damaged chunk)");
        add("regression-part|p2ce-reader: the constant empty «LoadCases» item equals the archive",
            "the archive of the CLR types (ModelArchive.Write of an empty list of FEM cases), deleted in phase 2b: the constant GpcLegacyPayloads.EmptyLoadCasesItem "
            + "was proven equal to it at 8e5a538 and in every run of p2ce-reader until 0bc9ed3; on GPC: the old development file of P2P-01 (the item read back empty)");
        add("regression-part|p2ce-reader: the global frame equals the conversion of the former global system",
            "the FEM global system (CoordinateSystemModel.Global and its conversion GpcLoadFrames.Of), deleted in phase 2b: GpcLoadFrames.Global was proven "
            + "equal to it (fingerprint and name) at 0bc9ed3; on GPC: the global system of gpc-null-values and the load components of gpc-builders-b");
        add("regression-part|model-guids: legacy archives regenerate complete maps (GPC model read by value)",
            "the 0.3.0 payload of two FEM nodes the check wrote with the FEM types, deleted in phase 2b (no frozen copy: a new fixture needs the user's OK); "
            + "on GPC: the 0.3.0 models of the corpus read by value with their drafts (payload scan, GH_Model read = by value) and the GPC part of model-guids");
        add("regression-part|p2ce-reader: the casts of Beam Section on GPC equal the former FEM cast",
            "the FEM cast of GH_BeamSection (a text or a curve on its FEM section, the recipe by the conversion of that section), replaced in phase 2b by the "
            + "cast on GPC (GpcSectionCasts): the parity measured at 2519e4a on 36 casts (29 texts, valid and refused, 7 curves: the recipe text and the text of "
            + "the datum) was equal; on GPC: 12 casts of p2ce-reader with their expected kind, dimensions and text (texts, refused texts and curves)");
    }

    /// <summary>The reason of a retirement of this lane, as <see cref="P2rRetired.Checks"/> records it.</summary>
    internal static string Reason(string key) => P2rRetired.Checks.TryGetValue(key, out var reason) ? reason : throw new KeyNotFoundException("P2CE: not retired by name: " + key);
}
