#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

/// <summary>
/// TESTS ONLY, F6 (a)/(b): a FEM model of the regression tests with stored results as an analysed GPC model, so that a result consumer
/// ported to GPC runs on the same data as its former FEM path. The model is converted by the legacy reader, its stored results become a
/// GPC dataset through <see cref="LegacyResultsReader"/> (the inverse of the <see cref="GpcResults"/> conventions) and a results catalog
/// lists its cases in the order of their first stored result (label = case), as a BridGHe analysis does. Node displacements and reactions
/// are global and come back exactly; beam actions go through the principal frame (exact for the axis-aligned beams of these tests).
/// </summary>
internal static class GpcSyntheticResults
{
    internal const string Dataset = "BridGHe synthetic results";

    internal static GPC.Model.Models.Model Analysed(ModelModel fem, double tolerance = 1e-6)
    {
        byte[] archive = ModelArchive.Write(fem);
        var owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "synthetic:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archive)));
        var legacy = LegacyArchive.Read(archive);
        var gpc = LegacyModelImport.Import(legacy, owner, tolerance, new LegacyImportReport(), BridGHe.Common.Gpc.GpcModels.SectionStateSignatures(fem));
        var outcome = LegacyResultsReader.Import(gpc, legacy, owner, Dataset);
        if (outcome.Report.Status != GPC.Converter.ImportStatus.Completed)
            throw new InvalidOperationException("Synthetic results not imported: " + outcome.Report.Status + " " + string.Join("; ", outcome.Report.Diagnostics.Select(d => d.Code + " " + d.Message)));
        var cases = new List<string>();
        foreach (var r in fem.Elements.SelectMany(e => e.Results)) if (r.LoadCase != null && !cases.Contains(r.LoadCase.Name)) cases.Add(r.LoadCase.Name);
        var units = BridgeUnits.Parse(fem.Units.ToString());
        GpcResultCatalog.Attach(gpc, units, cases.Select(c => new GpcResultCatalog.Entry { Label = c, Case = c }).ToArray(),
            Enumerable.Range(0, fem.Beams.Count).ToDictionary(k => k + 1, k => fem.Beams[k].AngleDeg));
        return gpc;
    }
}
#endif
