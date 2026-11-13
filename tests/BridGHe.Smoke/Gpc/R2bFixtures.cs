using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.6 lane R2b: the shared helpers of the analysis, results and workflow tests on GPC data alone (no FEM model): the expected merged samples
/// of a bridge model written from the analytic diagrams of the sources, the canonical tokens of the stored results of an analysed GPC model
/// (to show that a consumer does not change its source), the GPC input of a model built in GPC, and the GH model given by its GPC input
/// alone (<c>GH_Model.FromGpc</c>, reached by reflection: the Smoke host does not reference the plug-in) with its analysed model or its GPC
/// bridge model attached, as the analysis components and Build BridGHe Model give it.
/// </summary>
internal static class R2bFixtures
{
    internal static string Bits(double v) => BitConverter.DoubleToInt64Bits(v == 0 ? 0.0 : v).ToString("X16", CultureInfo.InvariantCulture);

    /// <summary>The samples of <paramref name="caseName"/> merged on beam <paramref name="beam"/>, in their stored order, each equal bit for bit
    /// to the analytic value <paramref name="expected"/> at its station; the stations are <paramref name="stations"/>, in order.</summary>
    internal static void RequireSamples(GpcBridgeBuild build, int beam, string caseName, IReadOnlyList<double> stations, Func<double, double[]> expected, string label)
    {
        var samples = build.Bridge.Samples[beam].Where(s => s.Case == caseName).Select(s => s.Sample).ToArray();
        if (!samples.Select(s => Bits(s.Station)).SequenceEqual(stations.Select(Bits)))
            throw new Exception($"{label}: beam {beam}, case {caseName}: stations {string.Join(",", samples.Select(s => s.Station))}, expected {string.Join(",", stations)}");
        foreach (var s in samples)
        {
            var want = expected(s.Station);
            if (!s.Values.Select(Bits).SequenceEqual(want.Select(Bits)))
                throw new Exception($"{label}: beam {beam}, case {caseName}, station {s.Station:R}: {string.Join(",", s.Values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))}, expected {string.Join(",", want.Select(v => v.ToString("R", CultureInfo.InvariantCulture)))}");
        }
    }

    /// <summary>Every stored result of an analysed GPC model as the Results components read it (beam, node rows), as exact tokens.</summary>
    internal static string[] ResultTokens(GPC.Model.Models.Model analysed)
    {
        var catalog = GpcResultCatalog.Of(analysed) ?? throw new ArgumentException("R2b: no results catalog: " + analysed.Name);
        var beams = analysed.BeamElements.Values.OrderBy(b => b.Id).SelectMany(b => GpcResultRows.Beam(catalog, analysed, b)
            .Select(r => "B" + b.Id + "|" + r.Entry + "|" + Bits(r.Station) + "|" + string.Join(",", new[] { r.Axial, r.Shear1, r.Shear2, r.Torque, r.Bending1, r.Bending2 }.Select(Bits))));
        var nodes = analysed.NodesElements.Values.OrderBy(n => n.Id).SelectMany(n => GpcResultRows.Node(catalog, analysed, n)
            .Select(r => "N" + n.Id + "|" + r.Entry + "|" + string.Join(",", (r.Displacement ?? Array.Empty<double>()).Concat(r.Reaction ?? Array.Empty<double>()).Select(Bits))));
        return beams.Concat(nodes).Append(GPC.Model.Models.ModelRevisions.AnalysisFingerprint(analysed)).ToArray();
    }

    /// <summary>The GPC input of a model built in GPC (N-mm, the BridGHe angle of every beam from <paramref name="angles"/> or 0°), as the
    /// model transformers and the audit read it.</summary>
    internal static GpcModels.Conversion Input(GPC.Model.Models.Model model, IReadOnlyDictionary<int, double> angles = null, GPC.Checkers.CompositeBridge.BridgeModelData bridge = null) =>
        GpcModels.Conversion.FromGpc(model, new BridgeUnits(DefinitionUnits.Nmm), angles ?? model.BeamElements.Values.ToDictionary(b => b.Id, _ => 0d), 1e-6, bridge: bridge);

    internal static Assembly Plugin => BridgeMergeTests.Initialize();
    internal static Type GhModel => Plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);

    /// <summary>The GH model given by its GPC input alone, with the analysed model of the same datum attached (an analysis component), or not.</summary>
    internal static IGH_Goo Model(GpcModels.Conversion input, GPC.Model.Models.Model analysed = null)
    {
        var goo = (IGH_Goo)GhModel.GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { input });
        if (analysed != null) GhModel.GetMethod("AttachGpc").Invoke(goo, new object[] { analysed });
        Require(GhModel.GetProperty("Value").GetValue(goo) == null, "R2b: the GH model has no FEM value");
        return goo;
    }

    /// <summary>The GH model of a source pair of <see cref="GpcWorkflowSources.Pair"/>: its input model with its analysed copy attached.</summary>
    internal static IGH_Goo Model((GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed) pair)
    {
        var catalog = GpcResultCatalog.Of(pair.Analysed);
        return Model(Input(pair.Input, pair.Input.BeamElements.Values.ToDictionary(b => b.Id, catalog.BeamAngle)), pair.Analysed);
    }

    /// <summary>The GH model of Build BridGHe Model on GPC data alone: the GPC input of the bridge model with the bridge attached.</summary>
    internal static IGH_Goo Model(GpcBridgeBuild build)
    {
        var goo = Model(build.Input ?? throw new ArgumentException("R2b: the build has no GPC input (structureInput)"));
        GhModel.GetMethod("AttachBridge").Invoke(goo, new object[] { build.Bridge });
        return goo;
    }

    /// <summary>The GPC bridge model of Build BridGHe Model with its GPC input (structureInput, as the component on GPC data alone).</summary>
    internal static GpcBridgeBuild Bridge(IReadOnlyList<(GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed)> references, IEnumerable<BridgePhaseDefinition> phases,
        IReadOnlyList<string[]> table, string label, BridgeWorkflowMethod method = BridgeWorkflowMethod.Compatible, bool additional = false) =>
        GpcBridgeBuilder.Build(references.Select(r => GpcBridgeMerge.Reference(r)).ToArray(), GpcBridgeMerge.GpcOnly(phases), table, method, 1e-6, additional,
            GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r2b-bridge:" + label), structureInput: true);

    internal static T Get<T>(object goo, string property) => (T)goo.GetType().GetProperty(property).GetValue(goo);

    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}
