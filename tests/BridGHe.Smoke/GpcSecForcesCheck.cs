#if GPC_PILOT
using System;
using System.Globalization;
using System.Linq;
using GPC.Model.Results;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// F6 (a)/(b), R1/R4 (native): on the GPC workflow path Super Element Check takes its stations and its "Tension Forces" output from the
/// merged samples of the GPC bridge model; Beam Moment Dispatch classifies from the GPC results; Bridge Girder Results takes its stations from
/// the merged samples.
/// <para><b>P2R (T2/R5, 12/11):</b> on GPC data alone. Until P2 the checks recomputed the former output from the FEM results of the composite
/// beams and of the merged FEM model and required the same values bit for bit, recording both sides in the frozen native capture
/// (<see cref="NativeParityCapture"/>, keys <c>forces|…</c>, <c>dispatch|…</c>, <c>girders|…</c>). The FEM values are gone (P2K/P2D): the oracle is
/// now the capture, whose values are the FEM ones (equal to the GPC ones when it was written): the same keys, the GPC values only, every captured
/// key seen. The stations and sign conventions are also checked on explicit GPC samples by <c>r2b-merge-forces</c>.</para>
/// </summary>
internal static class GpcSecForcesCheck
{
    private static string Bits(double v) => BitConverter.DoubleToInt64Bits(v == 0 ? 0.0 : v).ToString("X16", CultureInfo.InvariantCulture);
    private static string Token(ResultBeamForces f) => string.Join(",", new[] { f.N, f.V1, f.V2, f.T, f.M1, f.M2 }.Select(Bits));

    /// <summary>The ID of a composite beam in the GPC bridge model, as Super Element Check reads it (its analysed GPC beam, else its BridGHe ID).</summary>
    internal static string BeamId(object compositeBeam) =>
        compositeBeam.GetType().GetProperty("AnalysedGpc")?.GetValue(compositeBeam) is GPC.Model.Elements.BeamElement b ? b.Source.OriginalId
            : (string)compositeBeam.GetType().GetProperty("BeamId").GetValue(compositeBeam);

    internal static void Require(GH_Component check, string label)
    {
        void Fail(string what) => throw new Exception("GPC SEC FORCES FAIL (" + label + " " + check.NickName + "): " + what);
        var goo = check.Params.Input[0].VolatileData.AllData(true).Single();
        var bridge = (BridGHe.Common.Gpc.GpcBridge)goo.GetType().GetProperty("GpcBridge").GetValue(goo) ?? throw new Exception("GPC SEC FORCES FAIL (" + label + "): no GPC bridge model");
        var beams = check.Params.Input[1].VolatileData.AllData(true).Select(g => (object)g).ToArray();
        var output = check.Params.Output[2].VolatileData;
        var combinations = bridge.Combinations;
        long values = 0; int branches = 0;
        for (int i = 0; i < beams.Length; i++)
        {
            string id = BeamId(beams[i]);
            var stations = bridge.SamplesOf(id)?.Select(x => Math.Round(x.Sample.Station, 15)).Distinct().OrderBy(x => x).ToArray();
            if (stations == null) Fail($"beam {id}: not in the GPC bridge model");
            string beamKey = $"forces|{check.NickName}|{i}:{id}";
            NativeParityCapture.Datum(label, beamKey + "|stations", null, string.Join(",", stations.Select(Bits)));
            for (int j = 0; j < stations.Length; j++)
            {
                var path = new GH_Path(i, j); int b = output.Paths.IndexOf(path);
                if (b < 0) Fail($"no output branch {path}");
                var items = output.get_Branch(b).Cast<object>().Select(o => (ResultBeamForces)((o as IGH_Goo)?.ScriptVariable() ?? o)).ToArray();
                if (items.Length != combinations.Count) Fail($"{path}: {items.Length} forces, {combinations.Count} combinations");
                for (int c = 0; c < combinations.Count; c++) { NativeParityCapture.Datum(label, $"{beamKey}|{j}|{c}:{combinations[c].Name}", null, Token(items[c])); values += 6; }
                branches++;
            }
        }
        if (branches != output.PathCount) Fail($"{output.PathCount} output branches, {branches} stations of the GPC bridge samples");
        NativeParityCapture.Datum(label, $"forces|{check.NickName}|branches", null, output.PathCount.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine($"GPC SEC FORCES PASS ({label} {check.NickName}): {beams.Length} beams, {branches} stations, forces from the GPC bridge samples recorded against the native capture ({values} values).");
    }

    /// <summary>Beam Moment Dispatch (chain B): with the beams of an analysed model (their analysed GPC beam) the "States" output, classified from
    /// the GPC results, against the native capture (sign, extrema, case of every state); every beam must be on the GPC path.</summary>
    internal static void RequireMomentDispatch(GH_Document doc, string label)
    {
        int components = 0, states = 0;
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(o => o.GetType().Name == "BeamMomentDispatchComponent"))
        {
            var goos = c.Params.Input[0].VolatileData.AllData(true).ToArray();
            if (goos.Length == 0) continue;
            if (goos.Any(g => !(g.GetType().GetProperty("AnalysedGpc").GetValue(g) is GPC.Model.Elements.BeamElement)))
                throw new Exception("GPC MOMENT DISPATCH FAIL (" + label + " " + c.NickName + "): beams without their analysed GPC beam");
            var actual = c.Params.Output[5].VolatileData.AllData(true).Select(g => (BridGHe.Common.Geometry.BeamMomentState)((IGH_Goo)g).ScriptVariable()).ToArray();
            string key = $"dispatch|{components}:{c.NickName}";
            NativeParityCapture.Datum(label, key + "|count", null, actual.Length.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < actual.Length; i++)
            {
                var s = actual[i];
                NativeParityCapture.Datum(label, $"{key}|{i}", null, $"{s.BeamId ?? s.Source?.BeamId}|{s.Sign}|{s.Case}|{NativeParityCapture.Bits(s.Minimum)}|{NativeParityCapture.Bits(s.Maximum)}");
            }
            components++; states += actual.Length;
        }
        if (components == 0) throw new Exception("GPC MOMENT DISPATCH FAIL (" + label + "): no Beam Moment Dispatch with analysed beams");
        Console.WriteLine($"GPC MOMENT DISPATCH PASS ({label}): {components} components, {states} states classified from the GPC results recorded against the native capture.");
    }

    /// <summary>R8, Bridge Girder Results: with composite beams linked to the GPC bridge model the "Stations" output (from the merged samples)
    /// against the native capture; every component must be on the GPC path.</summary>
    internal static void RequireGirders(GH_Document doc, string label)
    {
        int components = 0, stations = 0;
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(o => o.GetType().Name == "BridgeGirderResultsComponent"))
        {
            var goos = c.Params.Input[0].VolatileData.AllData(true).ToArray();
            if (goos.Length == 0) continue;
            if (goos.Any(g => g.GetType().GetProperty("AnalysedGpcModel").GetValue(g) == null))
                throw new Exception("GPC GIRDER RESULTS FAIL (" + label + " " + c.NickName + "): composite beams without the link to the GPC bridge model");
            var actual = c.Params.Output[2].VolatileData.AllData(true).OfType<GH_Number>().Select(n => n.Value).ToArray();
            if (actual.Length == 0 || actual.Any(v => !double.IsFinite(v))) throw new Exception("GPC GIRDER RESULTS FAIL (" + label + " " + c.NickName + "): no stations, or a station not finite");
            NativeParityCapture.Datum(label, $"girders|{components}:{c.NickName}|stations", null, string.Join(",", actual.Select(Bits)));
            components++; stations += actual.Length;
        }
        if (components == 0) throw new Exception("GPC GIRDER RESULTS FAIL (" + label + "): no Bridge Girder Results with data");
        Console.WriteLine($"GPC GIRDER RESULTS PASS ({label}): {components} components on the GPC bridge samples, {stations} stations recorded against the native capture.");
    }
}
#endif
