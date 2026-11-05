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
/// </summary>
internal static class GpcWorkflowSources
{
    /// <summary>The GPC form of <see cref="BridgeWorkflowTests.Source"/>: N = force + i, BM2 (My) = force·1000·(1 + x) at x = 0, ½, 1.</summary>
    /// <param name="reaction">Vertical support reaction of case G at every beam end node (null: no nodes, as the FEM fixture).</param>
    /// <param name="change">Last changes before the analysis (samples removed or added).</param>
    /// <param name="reactionAt">Nodes (1-based) that get the reaction (null: all).</param>
    internal static GPC.Model.Models.Model Source(string name, double force, double? reaction = null, Action<GpcTestModel, BeamElement[], NodeElement[]> change = null,
        IReadOnlyList<double> stations = null, Func<int, double, double[]> values = null, Func<int, bool> reactionAt = null)
    {
        var test = new GpcTestModel(name); test.Case("G");
        var beams = new BeamElement[2];
        var nodes = new List<NodeElement>();
        for (int i = 0; i < 2; i++)
        {
            if (reaction.HasValue)
            {
                var a = test.Node((nodes.Count + 1).ToString(), 0, i * 2000, 0); nodes.Add(a);
                var b = test.Node((nodes.Count + 1).ToString(), 10000, i * 2000, 0); nodes.Add(b);
                beams[i] = test.Beam("B" + i, a, b);
            }
            else beams[i] = test.Beam("B" + i, 0, i * 2000, 0, 10000, i * 2000, 0);
            foreach (double x in stations ?? new[] { 0d, 5000, 10000 })
            {
                var v = values?.Invoke(i, x) ?? new[] { force + i, 0, 0, 0, 0, force * 1000 * (1 + x / 10000) };
                test.Forces(beams[i], "G", x, v[0], v[1], v[2], v[3], v[4], v[5]);
            }
        }
        if (reaction.HasValue) foreach (var n in nodes.Where(n => reactionAt?.Invoke(n.Id) ?? true)) test.Reaction(n, "G", 0, 0, reaction.Value);
        change?.Invoke(test, beams, nodes.ToArray());
        return test.Analyse();
    }
}
#endif
