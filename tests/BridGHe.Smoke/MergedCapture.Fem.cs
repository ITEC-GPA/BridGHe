using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

/// <summary>F6A C5 (tests only): with BRIDGHE_MERGED_CAPTURE=&lt;folder&gt; the native runners of examples 50 and 99 write the merged bridge
/// model of Build Bridge Model (FEM results of every phase, bridge workflow, composite properties) as a former archive, the input of the
/// offline comparison of the GPC workflow executor (D3) with the checks of the same run (BRIDGHE_GPC_CAPTURE rows).
/// <para>FEM path, retired at R6.5 (coordinator decision of 6/11): there is no FEM merged model to write after the removal. What goes with
/// this file: the variable <c>BRIDGHE_MERGED_CAPTURE</c> and its two calls in the native runners of 50 (<c>BridgeMovingRevisionNative</c>) and
/// 99 (<c>NativeModularBridgeTests</c>). What stays, as frozen references: the merged archives already written
/// (<c>gpc-f6-claude\P2B\results\s12-native-50\merged\example50-rev003.merged.bgmodel</c>, 448 MB, and
/// <c>s12-native-99\merged\modular-flusso-acciaio-misto_REV004.merged.bgmodel</c>, 8.6 MB), which <c>--c5-compare</c> and
/// <c>--legacy-beam-results</c> read as data (no FeMM class).</para></summary>
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
