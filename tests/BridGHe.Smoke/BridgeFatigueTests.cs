using System;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class BridgeFatigueTests
{
    internal static ModelModel HistoryModel()
    {
        var model = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = "Synthetic transit for testing" };
        var beam = new BeamModel(Point3d.Origin, new Point3d(1000, 0, 0), BridgeAssignmentTests.Property()) { BeamId = "1" };
        model.AddElements(new[] { beam });
        var values = new[] { 0d, 10, 2, 8, 0 };
        for (int i = 0; i < values.Length; i++)
        {
            var c = new LoadCaseModel($"Q{i}", $"Q{i}"); model.LoadCases.Add(c);
            foreach (double x in new[] { 0d, 1 })
                beam.Results.Add(new BeamForceResultModel(c, x, 0, "Transit", true) { AxialForce = values[i] });
        }
        return model;
    }
#if GPC_PILOT
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
#endif
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
#if GPC_PILOT
        // P2T: the history of the analysed GPC model built in GPC (stays after the FeMM removal; the FEM part below is its parity reference).
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
#endif
        // FEM path (removed with the FeMM models).
        var model = HistoryModel(); var cases = model.LoadCases.Select(c => c.Name).ToArray();
        var extracted = BridgeFatigueHistory.Extract(model, "1", 500, cases, 0);
        Require(extracted.Values.SequenceEqual(new[] { 0d, 10, 2, 8, 0 }) && extracted.Sources[1].Contains("Q1"), "Explicit transit order and physical station interpolation");
        var stress = BridgeFatigueHistory.Extract(model, "1", 500, cases, 0, new[] { .1, 0, 0, 0, 0, 0 });
        Require(stress.Values[1] == 1, "Explicit response transform");
        Reject(() => BridgeFatigueHistory.Extract(model, "1", 500, null, 0));
#if GPC_PILOT
        // Parity: the same numbers bit for bit at both ends, mid length (resampled) and an off-grid station, for every component and with
        // the response transform; the source text is the same except the former FEM step type ("Transit"), which GPC results do not have.
        static string Bits(double v) => BitConverter.DoubleToInt64Bits(v == 0 ? 0.0 : v).ToString("X16"); // zero without sign, as ConsumersCheck
        static string WithoutStepType(string s) => s.Substring(0, s.LastIndexOf('/'));
        foreach (double station in new[] { 0d, 250, 500, 1000 })
            foreach (var factors in new[] { null, new[] { .1, .2, .3, .4, .5, .6 } })
                for (int component = 0; component < 6; component++)
                {
                    var a = BridgeFatigueHistory.Extract(model, "1", station, cases, component, factors); var b = BridgeFatigueHistory.Extract(analysed, "1", station, gpcCases, component, factors);
                    Require(a.Values.Select(Bits).SequenceEqual(b.Values.Select(Bits)) && a.Sources.Select(WithoutStepType).SequenceEqual(b.Sources.Select(WithoutStepType)),
                        $"GPC history equal to the FEM one bit for bit (station {station}, component {component}, factors {factors != null})");
                }
#endif
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
