using System;
using System.Linq;
using BridGHe.Common.Diagnostics;
using Rhino.Geometry;

internal static class BridgeFatigueTests
{
    /// <summary>P2T: <see cref="HistoryModel"/> built directly in GPC: one 1000 mm beam with the axial force of each transit case at both ends.</summary>
    internal static GPC.Model.Models.Model HistoryGpc()
    {
        var test = new GpcTestModel("Synthetic transit for testing");
        var beam = test.Beam("1", test.Node("1", 0, 0, 0), test.Node("2", 1000, 0, 0));
        var values = new[] { 0d, 10, 2, 8, 0 };
        for (int i = 0; i < values.Length; i++)
        {
            test.Case($"Q{i}");
            foreach (double x in new[] { 0d, 1000 }) test.Forces(beam, $"Q{i}", x, values[i]);
        }
        return test.Analyse();
    }
    internal static void Run()
    {
        var nested = BridgeFatigueHistory.Count(new[] { 0d, 10, 2, 8, 0 }, 3, 1);
        Require(nested.Count == 2 && Math.Abs(nested.EquivalentRange - Math.Pow(1216, 1d / 3)) < 1e-12, "Nested ranges 10 and 6, one cycle each");
        Require(nested.Cycles.Where(c => c.Range == 6).Sum(c => c.Count) == 1 && nested.Cycles.Where(c => c.Range == 10).Sum(c => c.Count) == 1, "Residual half cycles retained");
        var plateaus = BridgeFatigueHistory.Count(new[] { 0d, 0, 10, 10, 0, 0 }, 3, 1);
        Require(plateaus.Count == 1 && plateaus.EquivalentRange == 10, "Repeated plateaus do not add cycles");
        Require(BridgeFatigueHistory.Count(new[] { 7d, 7, 7 }, 3, 1).Count == 0, "Constant history");
        var monotonic = BridgeFatigueHistory.Count(new[] { 0d, 1, 2 }, 1, .5);
        Require(monotonic.Count == .5 && monotonic.EquivalentRange == 2, "Open monotonic history is one half cycle");
        // R6.6 R2b: the history of the analysed GPC model built in GPC alone, no FEM model.
        var analysed = HistoryGpc(); var gpcCases = analysed.LoadCases.Keys.ToArray();
        Require(gpcCases.SequenceEqual(new[] { "Q0", "Q1", "Q2", "Q3", "Q4" }), "GPC: cases in transit order");
        var gpcHistory = BridgeFatigueHistory.Extract(analysed, "1", 500, gpcCases, 0);
        Require(gpcHistory.Values.SequenceEqual(new[] { 0d, 10, 2, 8, 0 }) && gpcHistory.Sources[1].Contains("Q1"), "GPC: Explicit transit order and physical station interpolation");
        var gpcStress = BridgeFatigueHistory.Extract(analysed, "1", 500, gpcCases, 0, new[] { .1, 0, 0, 0, 0, 0 });
        Require(gpcStress.Values[1] == 1, "GPC: Explicit response transform");
        Reject(() => BridgeFatigueHistory.Extract(analysed, "1", 500, null, 0));
        Reject(() => BridgeFatigueHistory.Extract(analysed, "1", 500, new[] { "missing" }, 0));
        Reject(() => BridgeFatigueHistory.Extract(analysed, "2", 500, gpcCases, 0));
        Reject(() => BridgeFatigueHistory.Extract(analysed, "1", 1500, gpcCases, 0));
        // Explicit expected histories (they replace the former parity with the FEM history): the axial force of the transit is constant along
        // the beam, the other components are zero; with the response factors the response is 0.1 N. At both ends, mid length (resampled) and an
        // off-grid station, for every component; the source names the model, the beam, the station, the case and the step (0, no step type).
        var transit = new[] { 0d, 10, 2, 8, 0 };
        foreach (double station in new[] { 0d, 250, 500, 1000 })
            foreach (var factors in new[] { null, new[] { .1, .2, .3, .4, .5, .6 } })
                for (int component = 0; component < 6; component++)
                {
                    var b = BridgeFatigueHistory.Extract(analysed, "1", station, gpcCases, component, factors);
                    var expected = transit.Select(v => factors != null ? v * .1 : component == 0 ? v : 0).ToArray();
                    var sources = gpcCases.Select(c => FormattableString.Invariant($"Synthetic transit for testing/1/{station:G17}/{c}/0/")).ToArray();
                    Require(b.Values.Select(R2bFixtures.Bits).SequenceEqual(expected.Select(R2bFixtures.Bits)) && b.Sources.SequenceEqual(sources),
                        $"GPC history: explicit values and sources (station {station}, component {component}, factors {factors != null}): {string.Join(",", b.Values)} / {string.Join(" ; ", b.Sources)}");
                }
        Reject(() => BridgeFatigueHistory.Count(new[] { 0d, double.NaN }, 3, 1));
        Reject(() => BridgeFatigueHistory.Count(new[] { 0d, 1 }, 0, 1));
        Reject(() => BridgeFatigueHistory.Count(new[] { -double.MaxValue, double.MaxValue }, 3, 1));
        var large = BridgeFatigueHistory.Count(new[] { 1e308, 1.1e308 }, 3, 1);
        Require(double.IsFinite(large.Cycles.Single().Mean), "Finite large mean does not overflow");
        Console.WriteLine("BRIDGE FATIGUE PASS: hand-counted nested/monotonic/plateau histories, transit order, station interpolation and explicit response factors.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE FATIGUE: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid fatigue input accepted"); }
}
