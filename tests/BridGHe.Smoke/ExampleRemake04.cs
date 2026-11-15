using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// P2S (7/11, coordinator decision 2 on passo 66): the examples whose result components read GH models internalised by the 0.3.0 with results
/// (24 synthetic samples, 42 synthetic displacements, 43 synthetic transit, 47/48 the archived native results of 19 REV003) remade in 0.4.0
/// form: every internalised GH model with stored results gets the analysed GPC model of the same results (<see cref="GpcSyntheticResults"/>:
/// the legacy reader and the results reader, as the synthetic results of the tests) and is saved with it (GH_Model chunk BridGHeResults);
/// a model whose beams have no node at an end (the beam-36 extracts of 47/48) gets the two end nodes first, so that it converts. The
/// document is read and written as it is: every object and its instance GUID are kept, only the internalised data change.
/// <para><c>--remake-examples-04 write</c> writes the five files of the worktree (never the main checkout); without <c>write</c> it only
/// reports. <c>--remade-examples</c> (regression <c>remade-examples</c>) opens the five files and solves them (Build BridGHe Model and Super
/// Element Check of 47/48 pressed, their checks compared with the former path, <see cref="FemPhasesReference"/>).</para>
/// </summary>
internal static class ExampleRemake04
{
    internal static readonly string[] Files =
    {
        @"24-inviluppi-concomitanti\24-inviluppi-concomitanti.ghx", @"42-frecce-controfreccia\42-frecce-controfreccia.ghx", @"43-storie-fatica\43-storie-fatica.ghx",
        @"47-pioli-irrigidimenti-verifiche\47-integrazione-modello.ghx", @"48-report-word\48-report-word.ghx",
    };

    private static IEnumerable<IGH_Param> Params(GH_Document doc) => doc.Objects.OfType<IGH_Param>()
        .Concat(doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input));

    /// <summary>Solves a remade example as the regression does (Build BridGHe Model and Super Element Check pressed): no errors, no warning of
    /// results missing or of a former version.</summary>
    private static void Solve(GH_Document doc, string relative)
    {
        BridgeSegmentExcelTests.Solve(doc, true);
        foreach (var name in new[] { "BuildBridgeModel", "SuperElementCheckComponent" })
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == name)) { c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true); c.ExpireSolution(false); BridgeSegmentExcelTests.Solve(doc, true); }
        var errors = doc.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => o.NickName + ": " + m)).ToArray();
        if (errors.Length > 0) throw new Exception(relative + ": " + string.Join("; ", errors));
        // P2CI (MU-03, 14/11): the warning of missing results is in English (GpcResultCatalog.NoAnalysedResultsMessage); the notices of a former
        // version keep «versione precedente».
        var warnings = doc.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Where(m => m.Contains("No results of a BridGHe analysis") || m.Contains("versione precedente")).Select(m => o.NickName + ": " + m)).ToArray();
        if (warnings.Length > 0) throw new Exception(relative + ": " + string.Join("; ", warnings));
    }

    /// <summary>The D3 section results of every Super Element Check of a document, as exact tokens (tree path, source, stresses of points 5 and 8,
    /// utilization, every check with its status and ratio), in component order.</summary>
    private static string[] CheckTokens(GH_Document doc) => doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "SuperElementCheckComponent").SelectMany(c =>
    {
        var tree = c.Params.Output[0].VolatileData;
        return Enumerable.Range(0, tree.PathCount).SelectMany(p => tree.get_Branch(p).Cast<IGH_Goo>().Select(g => (BridGHe.Common.Checks.BridgeStressResult)g.ScriptVariable())
            .Select(r => tree.get_Path(p) + "|" + r.Source?.Label + "|" + B(r.Point5_SteelSigma) + "|" + B(r.Point8_SteelSigma) + "|" + B(r.Stage.MaxUtilization) + "|"
                + string.Join(";", BridGHe.Common.Checks.BridgeGpc.Checks(r.Stage).Select(k => k.Name + "=" + k.Status + ":" + B(k.Ratio ?? double.NaN)))));
    }).ToArray();

    /// <summary>
    /// The result utilities of a remade example against the explicit values of the example (its description and its generator, independent of
    /// the code): 24 (BridgeUtilityExamples.Envelopes) the result cases G and Q, every signed beam envelope value negative with the concomitants
    /// N −50, V2 13, V3 −17, T 19, M2 −23 of the Q sample, the reaction Fz −120 and the displacement Uz −10 of the bearing node A; 42
    /// (BridgeDesignExamples.Deflection) at s = 15 m the absolute −28 mm, the relative −20 mm and the camber +20 mm, zero relative displacement
    /// at the supports 0, 30, 70, 100 m; 43 (BridgeDesignExamples.Fatigue) the equivalent range (1216)^(1/3) of the history 0, 10, 2, 8, 0 from
    /// both components. 47/48 have no result utility. Returns the number of outputs compared.
    /// </summary>
    private static int Expected(GH_Document doc, string relative)
    {
        int compared = 0;
        void Require(bool ok, string what) { if (!ok) throw new Exception(relative + ": " + what); compared++; }
        GH_Component[] Of(string type) => doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == type).ToArray();
        if (relative.StartsWith("24-", StringComparison.Ordinal))
        {
            Require(Out<string>(Of("BridgeResultCasesComponent").Single(), 0).SequenceEqual(new[] { "G", "Q" }), "Result Cases: the cases G and Q");
            var beam = Of("BridgeBeamEnvelopeComponent").Single(); var values = Out<double>(beam, 2); var components = Out<double>(beam, 6);
            Require(values.Length > 0 && values.All(v => v < 0) && Out<string>(beam, 3).All(c => c == "Q") && components.Length == 6 * values.Length, "beam envelope: signed values of Q, negative");
            for (int i = 0; i < values.Length; i++)
                Require(components.Skip(6 * i).Take(6).SequenceEqual(new[] { -50d, 13, -17, 19, -23, values[i] }), $"beam envelope row {i}: the concomitants of the Q sample");
            Require(Out<double>(Of("BridgeBearingEnvelopeComponent").Single(), 2).SequenceEqual(new[] { -120d }), "bearing envelope: Fz -120 at A");
            Require(Out<double>(Of("BridgeDisplacementEnvelopeComponent").Single(), 2).SequenceEqual(new[] { -10d }), "displacement envelope: Uz -10 at A");
        }
        else if (relative.StartsWith("42-", StringComparison.Ordinal))
        {
            var c = Of("BridgeDeflectionCamberComponent").Single(); var stations = Out<double>(c, 0);
            int mid = Array.IndexOf(stations, 15000d);
            Require(mid >= 0 && Math.Abs(Out<double>(c, 1)[mid] + 28) < 1e-9 && Math.Abs(Out<double>(c, 2)[mid] + 20) < 1e-9 && Math.Abs(Out<double>(c, 3)[mid] - 20) < 1e-9,
                "deflection at s = 15 m: absolute -28, relative -20, camber +20");
            foreach (double support in new[] { 0d, 30000, 70000, 100000 })
                Require(Array.IndexOf(stations, support) is int k && k >= 0 && Math.Abs(Out<double>(c, 2)[k]) < 1e-9, "zero relative displacement at the support " + support);
        }
        else if (relative.StartsWith("43-", StringComparison.Ordinal))
        {
            var cs = Of("BridgeFatigueHistoryComponent");
            Require(cs.Length == 2 && cs.All(c => Math.Abs(Out<double>(c, 5).Single() - Math.Pow(1216, 1d / 3)) < 1e-12), "fatigue: equivalent range (1216)^(1/3) from both components");
        }
        return compared;
    }

    private static T[] Out<T>(GH_Component c, int i) => c.Params.Output[i].VolatileData.AllData(true).Select(g => g.ScriptVariable()).OfType<T>().ToArray();
    private static T[] In<T>(GH_Component c, int i) => c.Params.Input[i].VolatileData.AllData(true).Select(g => g.ScriptVariable()).OfType<T>().ToArray();
    private static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");
}
