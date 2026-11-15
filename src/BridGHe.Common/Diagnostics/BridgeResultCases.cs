using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using GPC.Model.Models;

namespace BridGHe.Common.Diagnostics;

/// <summary>F6 (a)/(b): the stored results of an analysed GPC model as Bridge Result Cases lists them. Every row the Results components
/// show is one record (its catalog label, step 0, no step type), with the name of the result class the analysis wrote before F6, so the
/// "Result type" output keeps its values.</summary>
public static class BridgeResultCases
{
    public const string NodeDisplacement = "NodeDisplResultModel", NodeReaction = "NodeReactResultModel", BeamForce = "BeamForceResultModel", PlateForce = "PlateForceResultModel";

    public static (string Case, double Step, string Type, string Result)[] Records(Model analysed)
    {
        ArgumentNullException.ThrowIfNull(analysed);
        var catalog = GpcResultCatalog.Of(analysed) ?? throw new ArgumentException("The model has no stored results of a BridGHe analysis.");
        string Label(int entry) => catalog.Entries[entry].Label;
        var records = new List<(string, double, string, string)>();
        foreach (var node in analysed.NodesElements.Values)
            foreach (var row in GpcResultRows.Node(catalog, analysed, node))
            {
                if (row.Displacement != null) records.Add((Label(row.Entry), 0, "", NodeDisplacement));
                if (row.Reaction != null) records.Add((Label(row.Entry), 0, "", NodeReaction));
            }
        foreach (var beam in analysed.BeamElements.Values)
            foreach (var row in GpcResultRows.Beam(catalog, analysed, beam)) records.Add((Label(row.Entry), 0, "", BeamForce));
        foreach (var plate in analysed.AreaElements.Values)
            foreach (var row in GpcResultRows.Plate(catalog, analysed, plate)) records.Add((Label(row.Entry), 0, "", PlateForce));
        return records.ToArray();
    }
}
