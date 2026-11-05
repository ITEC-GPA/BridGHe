#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;

/// <summary>
/// P2T (F6, Smoke tests on GPC): Build BridGHe Model on GPC (<see cref="GpcBridgeBuilder"/>) with action sources built in GPC, for the tests
/// of the merge (stations, resampling, records, combinations, warnings). The actions carry their analysed GPC model (<c>GpcSource</c>): the
/// merge reads only its GPC rows.
/// <para>Two FEM dependencies remain until the FeMM removal (chain E), both outside the merge:</para>
/// <list type="bullet">
/// <item>the actions also carry their FEM source, because the shared validation of Build BridGHe Model (<c>BridgeWorkflowBuilder.Validate</c>,
/// «a FEM case needs its source model») still requires it;</item>
/// <item>the structure of a reference (the input model, without results: <see cref="GpcBridgeSource.Reference"/>) is the conversion of its FEM
/// model, as <c>GH_Model.GpcInput</c> in the component: <see cref="GpcModels.Conversion"/> has no public constructor. The FEM fixtures of
/// these tests have beams without nodes, which the legacy reader refuses: the reference gets nodes at the beam ends (no effect on the
/// merge, which aligns beams by ID and geometry).</item>
/// </list>
/// <para>The local section states are read as in the component (<see cref="BridgeWorkflowBuilder.LocalSectionStates"/>, the FEM source beams
/// until the L11 attribute is lifted; none in these tests).</para>
/// </summary>
internal static class GpcBridgeMerge
{
    /// <param name="references">The models of the Models input: the FEM model (structure of the reference) and its analysed GPC model.</param>
    internal static GpcBridgeBuild Build(IReadOnlyList<(BridGHe.Common.Models.ModelModel Fem, GPC.Model.Models.Model Analysed)> references, IEnumerable<BridgePhaseDefinition> phases,
        IReadOnlyList<string[]> table, string label, BridgeWorkflowMethod method = BridgeWorkflowMethod.Compatible, double tolerance = 1e-6, bool additional = false) =>
        GpcBridgeBuilder.Build(references.Select(r => GpcBridgeSource.Reference(GpcModels.Convert(WithNodes(r.Fem), tolerance), r.Analysed)).ToArray(), phases, table, method, tolerance, additional,
            GpcIdentity.Derive(GpcIdentity.NameSpace, "p2t-bridge:" + label), BridgeWorkflowBuilder.LocalSectionStates);

    /// <summary>A copy of a FEM fixture with nodes at the beam ends when it has none (the legacy reader needs them), without results.</summary>
    private static BridGHe.Common.Models.ModelModel WithNodes(BridGHe.Common.Models.ModelModel fem)
    {
        var copy = BridGHe.Common.Helpers.ModelArchive.Clone(fem);
        foreach (var e in copy.Elements) e.Results.Clear();
        if (copy.Nodes.Count == 0)
            copy.AddElements(copy.Beams.SelectMany(b => new[] { b.PointFrom, b.PointTo }).Distinct()
                .Select((p, i) => new BridGHe.Common.Models.NodeModel((i + 1).ToString(CultureInfo.InvariantCulture), p)).ToArray());
        return copy;
    }

    /// <summary>The merged samples of beam <paramref name="index"/> of the GPC bridge model, in their stored order.</summary>
    internal static IReadOnlyList<(string Case, BeamForceSample Sample)> Samples(GpcBridgeBuild build, int index) => build.Bridge.Samples[index];

    private static string Bits(double v) => BitConverter.DoubleToInt64Bits(v == 0 ? 0.0 : v).ToString("X16", CultureInfo.InvariantCulture);

    /// <summary>Every merged sample as an exact token (case, station and the six actions; zero without sign, as ConsumersCheck).</summary>
    internal static IEnumerable<string> Tokens(IReadOnlyList<(string Case, BeamForceSample Sample)>[] samples) => samples.SelectMany((beam, k) =>
        beam.Select(s => k + "|" + s.Case + "|" + Bits(s.Sample.Station) + "|" + string.Join(",", s.Sample.Values.Select(Bits))));

    /// <summary>Parity (removed with the FeMM models): the GPC merge equals the FEM merge bit for bit (samples, summary, warnings).</summary>
    internal static void RequireSame(GpcBridgeBuild gpc, BridgeWorkflowBuildResult fem, string label)
    {
        var a = Tokens(fem.Samples).ToArray(); var b = Tokens(gpc.Bridge.Samples).ToArray();
        if (!a.SequenceEqual(b, StringComparer.Ordinal))
        {
            int i = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(k => a[k] != b[k]);
            throw new Exception($"GPC MERGE FAIL ({label}): {a.Length} FEM samples, {b.Length} GPC samples; first difference {i}: FEM {(i < a.Length ? a[i] : "-")} / GPC {(i < b.Length ? b[i] : "-")}");
        }
        if (!fem.Summary.SequenceEqual(gpc.Summary, StringComparer.Ordinal) || !fem.Warnings.SequenceEqual(gpc.Warnings, StringComparer.Ordinal))
            throw new Exception($"GPC MERGE FAIL ({label}): summary or warnings differ:\nFEM {string.Join(" / ", fem.Summary.Concat(fem.Warnings))}\nGPC {string.Join(" / ", gpc.Summary.Concat(gpc.Warnings))}");
    }
}
#endif
