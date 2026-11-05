#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Model.Elements;

/// <summary>
/// P2T (F6, Smoke tests on GPC): the analysed source models of the bridge workflow tests built directly in GPC, the GPC form of
/// <see cref="BridgeWorkflowTests.Source"/> (two parallel 10 m beams B0 and B1, 2 m apart, case G with axial force and bending at three
/// stations) and of its variants. They are the analysed GPC models a Bridge Action carries (<c>BridgeActionDefinition.GpcSource</c>), read by
/// the GPC paths of Build BridGHe Model (merge from the GPC rows) and of the support reactions.
/// <para>The beams have their end nodes (the GPC result mapper needs them; the FEM fixture has none, and no consumer of these tests reads
/// them): nodes 1, 2 for B0 and 3, 4 for B1.</para>
/// </summary>
internal static class GpcWorkflowSources
{
    /// <summary>The GPC form of <see cref="BridgeWorkflowTests.Source"/>: N = force + i, BM2 (My) = force·1000·(1 + x/L) at x = 0, L/2, L.</summary>
    /// <param name="reaction">Vertical support reaction of case G at the beam end nodes (null: none, as the FEM fixture).</param>
    /// <param name="change">Last changes before the analysis (samples or loads added).</param>
    /// <param name="stations">Stations of the samples (mm from the beam start), the same on both beams.</param>
    /// <param name="values">The six actions (N, SF1, SF2, T, BM1, BM2) of beam i at station x.</param>
    /// <param name="reactionAt">Nodes (1-based) that get the reaction (null: all).</param>
    /// <param name="reversed">Beams and samples stored in the reverse order (B1 first, last station first).</param>
    /// <param name="firstEndZ">Level of the end of B0 (a changed geometry).</param>
    /// <param name="analyse">False: the input model (structure only, <see cref="GpcTestModel.Structure"/>), the reference of Build BridGHe Model.</param>
    internal static GPC.Model.Models.Model Source(string name, double force, double? reaction = null, Action<GpcTestModel, BeamElement[], NodeElement[]> change = null,
        IReadOnlyList<double> stations = null, Func<int, double, double[]> values = null, Func<int, bool> reactionAt = null, bool reversed = false, double firstEndZ = 0,
        bool analyse = true)
    {
        var test = new GpcTestModel(name); test.Case("G");
        var beams = new BeamElement[2];
        var nodes = new List<NodeElement>();
        foreach (int i in reversed ? new[] { 1, 0 } : new[] { 0, 1 })
        {
            var a = test.Node((nodes.Count + 1).ToString(), 0, i * 2000, 0); nodes.Add(a);
            var b = test.Node((nodes.Count + 1).ToString(), 10000, i * 2000, i == 0 ? firstEndZ : 0); nodes.Add(b);
            beams[i] = test.Beam("B" + i, a, b);
        }
        var grid = (stations ?? new[] { 0d, 5000, 10000 }).ToArray();
        if (reversed) Array.Reverse(grid);
        foreach (int i in reversed ? new[] { 1, 0 } : new[] { 0, 1 })
            foreach (double x in grid)
            {
                var v = values?.Invoke(i, x) ?? new[] { force + i, 0, 0, 0, 0, force * 1000 * (1 + x / 10000) };
                test.Forces(beams[i], "G", x, v[0], v[1], v[2], v[3], v[4], v[5]);
            }
        if (reaction.HasValue) foreach (var n in nodes.Where(n => reactionAt?.Invoke(n.Id) ?? true)) test.Reaction(n, "G", 0, 0, reaction.Value);
        change?.Invoke(test, beams, nodes.ToArray());
        return analyse ? test.Analyse() : test.Structure();
    }

    /// <summary>A model of the Models input of Build BridGHe Model as GPC data: its input model and its analysed copy (the same datum).</summary>
    internal static (GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed) Pair(string name, double force, double? reaction = null,
        Action<GpcTestModel, BeamElement[], NodeElement[]> change = null, IReadOnlyList<double> stations = null, Func<int, double, double[]> values = null,
        Func<int, bool> reactionAt = null, bool reversed = false, double firstEndZ = 0) =>
        (Source(name, force, reaction, change, stations, values, reactionAt, reversed, firstEndZ, analyse: false),
         Source(name, force, reaction, change, stations, values, reactionAt, reversed, firstEndZ));
}
#endif
