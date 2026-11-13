using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Straus;

/// <summary>
/// --beam-support (native; R6.6 lane R4b: on GPC data alone, <see cref="R4bInputs.BeamSupport"/>): the distributed beam supports of Beam Support
/// on a beam given by its GPC draft, through Build Model on GPC and the GPC-only analysis:
/// <list type="bullet">
/// <item>two bilateral springs (+1 axis k 10, −2 axis k 20) under uniform loads: the uniform displacement q/k of each case (analytic);</item>
/// <item>the four directions written by the Converter and read back with its API: the Straus7 direction code of the former exporter (−1 → 0, +1 → 1,
/// −2 → 2, +2 → 3), the stiffness, the gap and the compression-only flag as given;</item>
/// <item>the supports of the draft through the BGHGPC1 archive of the draft and its copy unchanged; the longitudinal axis refused by the builder;</item>
/// <item>the two contacts with their gaps under the combinations «Positive» and «Negative» (nonlinear solver): q = k·u + k_c·(u − gap) on the
/// engaged side (analytic); a negative gap kept as given.</item>
/// </list>
/// </summary>
internal static class NativeBeamSupportTests
{
    internal static void Run()
    {
        var checks = new NativeExamples.Recorder(); string folder = Path.GetFullPath("artifacts/beam-support"); Directory.CreateDirectory(folder);
        var bilateral = R4bInputs.BeamSupport(2, false);
        var solved = new GpcNativeExamples.Results(StrausAnalysis.Run(bilateral, Path.Combine(folder, "bilateral.st7"), 0, new[] { "Q", "R" }, false, out var warnings));
        for (int n = 0; n < solved.NodeCount; n++)
            foreach (var result in solved.NodeRows(n).Where(r => r.Displacement != null))
            {
                string name = solved.Catalog.Entries[result.Entry].Label, node = solved.Model.NodesElements[n + 1].Name;
                double sign = name == "Q" ? 1 : -.5;
                checks.Near(name + " node " + node + " Uy [mm]", result.Displacement[1], .2 * sign, 1e-8, 1e-10);
                checks.Near(name + " node " + node + " Uz [mm]", result.Displacement[2], .2 * sign, 1e-8, 1e-10);
            }
        var four = R4bInputs.BeamSupport(4, false);
        string file = Path.Combine(folder, "four-directions.st7");
        // F6A: export by the GPC Converter writer (export component path, here the GPC input alone) and readback with the Converter API.
        GpcAnalysisSeam.Export(four, file, new List<string>());
        string scratch = Path.Combine(Path.GetTempPath(), "bridghe-support-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        try
        {
            using var api = new GPC.Converter.Straus7.Straus7NativeApi();
            api.OpenModelReadOnly(file, scratch);
            foreach (var support in R4bInputs.Supports)
            {
                // The Straus7 direction of the former exporter (BeamSupportExport.Resolve): −1 → 0, +1 → 1, −2 → 2, +2 → 3; compression only as given.
                int direction = Array.IndexOf(new[] { "negative1Axis", "positive1Axis", "negative2Axis", "positive2Axis" }, support.Direction);
                bool compression = support.Spring == "CompressionOnly";
                var data = ((GPC.Converter.Straus7.IStraus7PhysicalReadApi)api).ReadBeamSupport(1, direction, 1, out bool compressionOnly);
                checks.Near($"Direction {direction} stiffness", data[0], support.Stiffness, 0, 0);
                checks.Near($"Direction {direction} gap", data[1], support.Gap, 0, 1e-12);
                checks.Near($"Direction {direction} unilateral", compressionOnly ? 1 : 0, compression ? 1 : 0, 0, 0);
            }
            api.CloseModel();
        }
        finally { try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        // The supports of the draft: the BGHGPC1 archive of the draft and its copy keep every value and gap (before: the archive and copy of the FEM attribute).
        var draft = four.Drafts.Beams.Single();
        string Supports(LegacyBeamDraft d) => string.Join(";", d.Beam.Assignments.Physical.Supports.Select(s => $"{s.Stiffness:R} {s.Gap:R} {s.Behavior} {s.Direction.X:R},{s.Direction.Y:R},{s.Direction.Z:R}"));
        var (data2, records) = GpcDraftArchive.Write(draft);
        var read = GpcDraftArchive.ReadBeam(data2, records); var copy = GpcDrafts.Copy(draft);
        if (draft.Beam.Assignments.Physical.Supports.Count != 4 || Supports(read) != Supports(draft) || Supports(copy) != Supports(draft) || ReferenceEquals(copy.Beam, draft.Beam))
            throw new Exception("Support archive/copy lost its value or gap: " + Supports(draft) + " / archive " + Supports(read) + " / copy " + Supports(copy));
        bool rejected = false;
        try { GpcBeamBuilders.WithSupport(draft, 10, 0, "positive3Axis", "TensionCompression", R4bInputs.Id("beam-support:longitudinal")); } catch (NotSupportedException) { rejected = true; }
        if (!rejected) throw new Exception("Longitudinal distributed support must be rejected.");
        var contact = R4bInputs.BeamSupport(4, true);
        var nonlinear = new GpcNativeExamples.Results(StrausAnalysis.Run(contact, Path.Combine(folder, "contact.st7"), 1, new[] { "Positive", "Negative" }, false, out var nonlinearWarnings));
        for (int n = 0; n < nonlinear.NodeCount; n++)
            foreach (var result in nonlinear.NodeRows(n).Where(r => r.Displacement != null))
            {
                string name = nonlinear.Catalog.Entries[result.Entry].Label;
                bool plus = name == "Positive";
                // Uniform displacement: q = k_bilateral*u + k_contact*(u-gap) on the engaged side.
                double uy = plus ? .2 : -(1 + 30 * .04) / (10 + 30), uz = plus ? (4 + 40 * .05) / (20 + 40) : -.1;
                checks.Near(name + " contact Uy [mm]", result.Displacement[1], uy, 1e-6, 1e-8);
                checks.Near(name + " contact Uz [mm]", result.Displacement[2], uz, 1e-6, 1e-8);
            }
        var precompressed = GpcBeamBuilders.WithSupport(draft, 10, -.05, "positive2Axis", "CompressionOnly", R4bInputs.Id("beam-support:precompressed"));
        checks.Near("Negative gap retained", precompressed.Beam.Assignments.Physical.Supports.Last().Gap, -.05, 0, 0);
        File.WriteAllText(Path.Combine(folder, "report.json"), System.Text.Json.JsonSerializer.Serialize(new { Warnings = warnings, NonlinearWarnings = nonlinearWarnings, checks.Measurements }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (checks.Measurements.Any(c => !c.Passed)) throw new Exception("Native beam support checks failed.");
        Console.WriteLine($"BEAM SUPPORT: {checks.Measurements.Count} native checks passed (GPC data alone).");
    }
}
