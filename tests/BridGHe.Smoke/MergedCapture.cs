using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

/// <summary>F6A C5 (tests only): with BRIDGHE_MERGED_CAPTURE=&lt;folder&gt; the native runners of examples 50 and 99 write the merged bridge
/// model of Build Bridge Model (FEM results of every phase, bridge workflow, composite properties) as a former archive, the input of the
/// offline comparison of the GPC workflow executor (D3) with the checks of the same run (BRIDGHE_GPC_CAPTURE rows).</summary>
internal static class MergedCapture
{
    internal static void Write(string label, ModelModel merged)
    {
        string folder = Environment.GetEnvironmentVariable("BRIDGHE_MERGED_CAPTURE");
        if (string.IsNullOrEmpty(folder) || merged == null) return;
        Directory.CreateDirectory(folder);
        string path = Path.Combine(Path.GetFullPath(folder), label + ".merged.bgmodel");
        File.WriteAllBytes(path, ModelArchive.Write(merged));
        Console.WriteLine("MERGED CAPTURE " + label + ": " + new FileInfo(path).Length + " bytes, " + merged.Beams.Count + " beams, " + merged.Beams.Sum(beam => beam.Results?.Count() ?? 0) + " beam results, " + merged.BridgeWorkflow.Phases.Length + " phases");
    }
}
