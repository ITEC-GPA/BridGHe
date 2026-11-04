#if GPC_PILOT
using System;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using GPC.Geometry;
using GPC.Model.Results;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// F6 (a)/(b), R1/R4 (native): on the GPC workflow path Super Element Check takes its stations and its "Tension Forces" output from the
/// merged samples of the GPC bridge model. The check recomputes the former output from the FEM results of the composite beams (stations,
/// <see cref="BridgePhaseAssembler"/>, total of the steel, long-term, short-term and shrinkage actions per combination) and requires the
/// same tree: same paths, same counts, every component bit for bit (zero without sign on both sides).
/// </summary>
internal static class GpcSecForcesCheck
{
    internal static void Require(GH_Component check, string label)
    {
        void Fail(string what) => throw new Exception("GPC SEC FORCES FAIL (" + label + " " + check.NickName + "): " + what);
        var goo = check.Params.Input[0].VolatileData.AllData(true).Single();
        var model = (ModelModel)goo.GetType().GetProperty("Value").GetValue(goo);
        var beams = check.Params.Input[1].VolatileData.AllData(true).Select(g => (CompositeBeamModel)((IGH_Goo)g).ScriptVariable()).ToArray();
        var output = check.Params.Output[2].VolatileData;
        var bridge = (BridGHe.Common.Gpc.Legacy.FemmBridgeSeam.Bridge)goo.GetType().GetProperty("GpcBridge").GetValue(goo) ?? throw new Exception("GPC SEC FORCES FAIL (" + label + "): no GPC bridge model");
        static string Bits(double v) => BitConverter.DoubleToInt64Bits(v == 0 ? 0.0 : v).ToString("X16", CultureInfo.InvariantCulture);
        static string Token(ResultBeamForces f) => string.Join(",", new[] { f.N, f.V1, f.V2, f.T, f.M1, f.M2 }.Select(Bits));
        var cs = new CoordinateSystem(Point3d.Origin, new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0));
        long values = 0; int branches = 0;
        for (int i = 0; i < beams.Length; i++)
        {
            var beam = beams[i];
            var stations = beam.Results.OfType<BeamForceResultModel>().Select(r => Math.Round(r.NormalizedLength ? r.Station * beam.Length : r.Station, 15)).Distinct().OrderBy(s => s).ToArray();
            var index = BridgePhaseAssembler.CreateIndex(beam, beam.Length, GeometryBase.Tolerance);
            // The stations of the plots and of Super Element Check from the merged samples of the GPC bridge model: the FEM stations, bit for bit.
            var fromSamples = bridge.SamplesOf(beam.BeamId)?.Select(x => Math.Round(x.Sample.Station, 15)).Distinct().OrderBy(x => x).ToArray();
            if (fromSamples == null || !fromSamples.Select(Bits).SequenceEqual(stations.Select(Bits))) Fail($"beam {beam.BeamId}: stations of the GPC bridge samples differ from the FEM stations");
            for (int j = 0; j < stations.Length; j++)
            {
                var path = new GH_Path(i, j); int b = output.Paths.IndexOf(path);
                if (b < 0) Fail($"no output branch {path}");
                var items = output.get_Branch(b).Cast<object>().Select(o => (ResultBeamForces)((o as IGH_Goo)?.ScriptVariable() ?? o)).ToArray();
                if (items.Length != model.LoadCombinations.Count) Fail($"{path}: {items.Length} forces, {model.LoadCombinations.Count} combinations");
                for (int c = 0; c < model.LoadCombinations.Count; c++)
                {
                    var a = index.Assemble(stations[j], model.LoadCombinations[c], cs);
                    var total = a.Steel + a.Infinite + a.Instant; if (a.Shrinkage != null) total += a.Shrinkage;
                    if (Token(total) != Token(items[c])) Fail($"{path}, combination {model.LoadCombinations[c].Name}: FEM {Token(total)} / GPC {Token(items[c])}");
                    values += 6;
                }
                branches++;
            }
        }
        if (branches != output.PathCount) Fail($"{output.PathCount} output branches, {branches} stations from the FEM results");
        Console.WriteLine($"GPC SEC FORCES PASS ({label} {check.NickName}): {beams.Length} beams, {branches} stations, forces from the GPC bridge samples equal to the former FEM output ({values} values).");
    }
}
#endif
