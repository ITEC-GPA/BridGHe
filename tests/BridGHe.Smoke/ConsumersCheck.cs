#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Gpc;
using BridGHe.Common.Models;

/// <summary>
/// F6 (a)/(b), native: the result consumers ported to GPC (R8 of the plan) give on the analysed GPC model what their former FEM path
/// gives on the FEM results the analysis wrote from the same rows. Called by <see cref="ResultsComponentsCheck"/> on every analysed model
/// of the native suites (examples 01-10, the four phases of 99, the kN-m native section, the two analyses reopened from the BGHR2).
/// Every number is compared bit for bit, with zero without sign on both sides (the FEM copies of the GH data write -0 as 0); an input
/// the FEM path rejects must be rejected by the GPC path with the same message.
/// </summary>
internal static class ConsumersCheck
{
    internal static long Verify(ModelModel fem, GPC.Model.Models.Model gpc, string label)
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("RESULT CONSUMERS GPC FAIL (" + label + "): " + what); }
        long values = 0;
        // Envelopes: every kind, component and mode, on all elements and cases.
        for (int kind = 0; kind < 3; kind++)
            for (int component = 0; component < 6; component++)
                for (int mode = 0; mode < 3; mode++)
                {
                    string what = $"envelope kind {kind} component {component} mode {mode}";
                    var a = Outcome(() => Envelope(BridgeResultEnvelope.Evaluate(fem, kind, null, null, component, mode), kind));
                    var b = Outcome(() => Envelope(BridgeResultEnvelope.Evaluate(gpc, kind, null, null, component, mode), kind));
                    Compare(a, b, what, Require); values += a.Tokens?.Count ?? 0;
                }
        // Bridge Result Cases: the grouped rows of the component.
        static IEnumerable<string> Groups(IEnumerable<(string Case, double Step, string Type, string Result)> all) => all.GroupBy(r => r)
            .OrderBy(g => g.Key.Case, StringComparer.Ordinal).ThenBy(g => g.Key.Step).ThenBy(g => g.Key.Type, StringComparer.Ordinal).ThenBy(g => g.Key.Result, StringComparer.Ordinal)
            .Select(g => g.Key.Case + "|" + Bits(g.Key.Step) + "|" + g.Key.Type + "|" + g.Key.Result + "|" + g.Count());
        var femCases = Groups(fem.Elements.SelectMany(e => e.Results).Select(r => (r.LoadCase?.Name, r.StepNum, r.StepType, r.GetType().Name))).ToArray();
        var gpcCases = Groups(BridgeResultCases.Records(gpc)).ToArray();
        Require(femCases.SequenceEqual(gpcCases, StringComparer.Ordinal), $"result cases: FEM {string.Join("; ", femCases)} / GPC {string.Join("; ", gpcCases)}");
        values += femCases.Length;
        // Fatigue history: every case of a beam in their stored order, at both ends and at mid length (resampled), per component.
        var catalog = GpcResultCatalog.Of(gpc);
        foreach (var beam in fem.Beams.Take(24))
        {
            var cases = beam.Results.Select(r => r.LoadCase?.Name).Where(c => c != null).Distinct().ToArray();
            foreach (double station in new[] { 0, beam.Length / 2, beam.Length })
                foreach (int component in new[] { 0, 4, 5 })
                {
                    string what = $"fatigue beam {beam.BeamId} station {station:R} component {component}";
                    var a = Outcome(() => Fatigue(BridgeFatigueHistory.Extract(fem, beam.BeamId, station, cases, component)));
                    var b = Outcome(() => Fatigue(BridgeFatigueHistory.Extract(gpc, beam.BeamId, station, cases, component)));
                    Compare(a, b, what, Require); values += a.Tokens?.Count ?? 0;
                }
        }
        Console.WriteLine($"RESULT CONSUMERS GPC PASS ({label}): envelopes, result cases and fatigue histories equal ({values} values).");
        return values;
    }

    private sealed record Result(List<string> Tokens, string Error);

    private static Result Outcome(Func<List<string>> run)
    {
        try { return new Result(run(), null); }
        catch (ArgumentException e) { return new Result(null, e.Message); }
    }

    private static void Compare(Result a, Result b, string what, Action<bool, string> require)
    {
        require(a.Error == b.Error, $"{what}: FEM {(a.Error ?? "no error")} / GPC {(b.Error ?? "no error")}");
        if (a.Tokens == null) return;
        require(a.Tokens.Count == b.Tokens.Count, $"{what}: {a.Tokens.Count} FEM values, {b.Tokens.Count} GPC values");
        for (int i = 0; i < a.Tokens.Count; i++) require(a.Tokens[i] == b.Tokens[i], $"{what}, value {i}: FEM {a.Tokens[i]} / GPC {b.Tokens[i]}");
    }

    private static string Bits(double v) => BitConverter.DoubleToInt64Bits(v == 0 ? 0.0 : v).ToString("X16", CultureInfo.InvariantCulture);

    private static List<string> Envelope(EnvelopeRow[] rows, int kind) => rows.SelectMany(r => new[] { r.Element, Bits(r.Station), r.Case, Bits(r.Step), r.StepType, Bits(r.Value), r.Coverage }
        .Concat(r.Components.Select(Bits)).Concat(SourceValues(r.Source, kind).Select(Bits))).ToList();

    /// <summary>The numbers of the governing stored result: the FEM result object or the GPC row (its six components).</summary>
    private static double[] SourceValues(object source, int kind) => source switch
    {
        ResultModel r => BridgeResultEnvelope.Values(r),
        GpcResultRows.BeamRow r => new[] { r.Axial, r.Shear1, r.Shear2, r.Torque, r.Bending1, r.Bending2 },
        GpcResultRows.NodeRow r => (kind == 1 ? r.Reaction : r.Displacement).ToArray(),
        _ => throw new ArgumentException("Unexpected envelope source " + source?.GetType().Name),
    };

    private static List<string> Fatigue(BridgeFatigueSeries s) => s.Values.Select(Bits).Concat(s.Sources).ToList();
}
#endif
