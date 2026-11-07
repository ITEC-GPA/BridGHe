#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;

/// <summary>
/// P2T (F6, Smoke tests on GPC): Build BridGHe Model on GPC (<see cref="GpcBridgeBuilder"/>) from GPC data only, for the tests of the merge
/// (stations, resampling, records, combinations, warnings). Every reference is a GPC input model with its analysed copy built in GPC
/// (<see cref="GpcBridgeSource.Reference(GPC.Model.Models.Model, BridgeUnits, IReadOnlyDictionary{int, double}, GPC.Model.Models.Model)"/>,
/// the direct reference of P2F 5/11: units and angles from the results catalog of the analysed copy), and the actions carry only their
/// analysed GPC model (<c>GpcSource</c>): <see cref="Build"/> drops any FEM source of the actions, accepted by the GPC validation
/// (<c>BridgeWorkflowBuilder.Validate(..., gpcSources: true)</c>). The FEM fixtures of the tests remain only for the parity with the FEM merge
/// (<see cref="RequireSame"/>), removed with the FeMM models.
/// <para>The local section states are read as in the component (<see cref="GpcBridgeBuilder.LocalSectionStates"/>, the GPC section-state
/// attributes of the GPC model of the source, P2F 5/11; none in these tests).</para>
/// </summary>
internal static class GpcBridgeMerge
{
    /// <param name="references">The models of the Models input: the GPC input model (structure, no results) and its analysed copy.</param>
    internal static GpcBridgeBuild Build(IReadOnlyList<(GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed)> references, IEnumerable<BridgePhaseDefinition> phases,
        IReadOnlyList<string[]> table, string label, BridgeWorkflowMethod method = BridgeWorkflowMethod.Compatible, double tolerance = 1e-6, bool additional = false,
        GPC.Checkers.CompositeBridge.BridgeModelData bridge = null) =>
        GpcBridgeBuilder.Build(references.Select((r, i) => Reference(r, i == 0 ? bridge : null)).ToArray(), GpcOnly(phases), table, method, tolerance, additional,
            GpcIdentity.Derive(GpcIdentity.NameSpace, "p2t-bridge:" + label));

    /// <summary>The direct GPC reference of an input model and its analysed copy (units and BridGHe angles from the catalog of the copy).</summary>
    /// <param name="bridge">P2S: the bridge data of the input model (super elements, members, local states: those of the GPC input of a model of
    /// Build Model), or none.</param>
    internal static GpcBridgeSource Reference((GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed) model) => Reference(model, null);
    internal static GpcBridgeSource Reference((GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed) model, GPC.Checkers.CompositeBridge.BridgeModelData bridge)
    {
        var catalog = GpcResultCatalog.Of(model.Analysed) ?? throw new ArgumentException("GpcTestReferenceWithoutResults: " + model.Analysed.Name);
        return GpcBridgeSource.Reference(model.Input, catalog.Units, model.Input.BeamElements.Values.ToDictionary(b => b.Id, catalog.BeamAngle), model.Analysed, bridge);
    }

    /// <summary>The phases with the actions on their GPC source only (the FEM source, if any, removed).</summary>
    internal static BridgePhaseDefinition[] GpcOnly(IEnumerable<BridgePhaseDefinition> phases) =>
        phases.Select(p => p with { Actions = p.Actions.Select(a => a with { Source = null }).ToArray() }).ToArray();

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
