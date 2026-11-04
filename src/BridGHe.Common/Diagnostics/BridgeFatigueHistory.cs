using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;
using BridGHe.Common.Models;
using GPC.Model.Models;

namespace BridGHe.Common.Diagnostics;

public sealed record BridgeFatigueCycle(double Range, double Mean, double Count, int From, int To);
public sealed record BridgeFatigueSeries(double[] Values, string[] Sources);
public sealed record BridgeFatigueSpectrum(BridgeFatigueCycle[] Cycles, double Minimum, double Maximum, double EquivalentRange, double Count);

public static class BridgeFatigueHistory
{
    /// <summary>One stored beam force of the history: case identity and the sample (station from the beam start, mm).</summary>
    private readonly record struct Stored(string Case, double Step, string StepType, BeamForceSample Force);

    public static BridgeFatigueSeries Extract(ModelModel model, string beamId, double station, IReadOnlyList<string> orderedCases,
        int component, IReadOnlyList<double> responseFactors = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (model.Units != ModelModel.ModelUnits.Nmm) throw new ArgumentException("History extraction expects N-mm result units.");
        var matches = model.Beams.Where(b => b.BeamId == beamId).ToArray();
        if (matches.Length != 1) throw new ArgumentException("Provide exactly one existing beam ID.");
        var beam = matches[0];
        Arguments(station, beam.Length, component, responseFactors);
        var forces = beam.Results.OfType<BeamForceResultModel>().ToArray();
        if (forces.Any(r => r.LoadCase == null || !double.IsFinite(r.StepNum))) throw new ArgumentException("Invalid stored history identity.");
        var available = forces.Select(r => new Stored(r.LoadCase.Name, r.StepNum, r.StepType,
            new BeamForceSample(r.NormalizedLength ? r.Station * beam.Length : r.Station, r.AxialForce, r.Vx, r.Vy, r.Torque, r.Mx, r.My))).ToArray();
        // Results may belong to combinations: conservatively retain every known force discontinuity.
        var pointLoads = beam.Loads.OfType<BeamPointLoadModel>().Select(l => l.Value.A * beam.Length).ToArray();
        return Extract(model.Name, beamId, beam.Length, station, available, pointLoads, orderedCases, component, responseFactors);
    }

    /// <summary>F6 (a)/(b): the history of an analysed GPC model (<see cref="GpcResultCatalog"/>), from the rows the Results components
    /// show (the same numbers, in the same order, as the former result objects the analysis wrote from them: catalog label, step 0, no
    /// step type); the concentrated loads of the beam from its GPC load assignments.</summary>
    public static BridgeFatigueSeries Extract(Model analysed, string beamId, double station, IReadOnlyList<string> orderedCases,
        int component, IReadOnlyList<double> responseFactors = null)
    {
        ArgumentNullException.ThrowIfNull(analysed);
        var catalog = GpcResultCatalog.Of(analysed) ?? throw new ArgumentException("The model has no stored results of a BridGHe analysis.");
        if (catalog.Units.Units != DefinitionUnits.Nmm) throw new ArgumentException("History extraction expects N-mm result units.");
        var matches = analysed.BeamElements.Values.Where(b => b.Name == beamId).ToArray();
        if (matches.Length != 1) throw new ArgumentException("Provide exactly one existing beam ID.");
        var beam = matches[0]; double length = beam.Length;
        Arguments(station, length, component, responseFactors);
        var available = GpcResultRows.Beam(catalog, analysed, beam).Select(r => new Stored(catalog.Entries[r.Entry].Label, 0, "", BeamForceSample.Of(r))).ToArray();
        var pointLoads = beam.Assignments.Loads.Where(l => l?.Concentrated != null).Select(l => l.Start * length).ToArray();
        return Extract(analysed.Name, beamId, length, station, available, pointLoads, orderedCases, component, responseFactors);
    }

    private static void Arguments(double station, double length, int component, IReadOnlyList<double> responseFactors)
    {
        if (!double.IsFinite(station) || station < 0 || station > length || component < 0 || component > 5)
            throw new ArgumentException("Station must lie on the beam in mm; component 0=N,1=Vx,2=Vy,3=T,4=Mx,5=My.");
        if (responseFactors != null && responseFactors.Count > 0 && (responseFactors.Count != 6 || responseFactors.Any(v => !double.IsFinite(v))))
            throw new ArgumentException("Provide six finite linear response factors for N,Vx,Vy,T,Mx,My, or none.");
    }

    private static BridgeFatigueSeries Extract(string modelName, string beamId, double length, double station, Stored[] available, double[] pointLoads,
        IReadOnlyList<string> orderedCases, int component, IReadOnlyList<double> responseFactors)
    {
        if (orderedCases == null || orderedCases.Count == 0)
        {
            orderedCases = available.Select(r => r.Case).Distinct().ToArray();
            if (orderedCases.Count != 1) throw new ArgumentException("Supply the explicit transit order of cases; case names are never sorted to infer a history.");
        }
        if (orderedCases.Any(c => !available.Any(r => r.Case == c))) throw new ArgumentException("Unknown history case.");
        var values = new List<double>(); var sources = new List<string>();
        foreach (string c in orderedCases)
        {
            var selected = available.Where(r => r.Case == c).ToArray();
            if (selected.Select(r => r.StepType ?? "").Distinct().Count() > 1)
                throw new ArgumentException($"Case {c} contains multiple step types; select a homogeneous transit history.");
            foreach (var step in selected.GroupBy(r => r.Step).OrderBy(g => g.Key))
            {
                var originals = step.Select(r => r.Force).ToArray();
                if (originals.Any(r => !double.IsFinite(r.Station) || r.Station < 0 || r.Station > length ||
                    r.Values.Any(v => !double.IsFinite(v)))) throw new ArgumentException("Non-finite/out-of-bounds FEM history sample.");
                var exact = originals.Where(r => Math.Abs(r.Station - station) <= BridgeMergeValidation.StationTolerance(length)).ToArray();
                if (exact.Length > 1) throw new ArgumentException("Duplicate/one-sided result at requested history station; choose an explicit side by splitting the beam.");
                var sample = exact.Length == 1 ? exact[0] : BridgeForceResampling.Resample(originals, new[] { station }, length,
                    $"Fatigue {beamId}/{c}/{step.Key}", pointLoads, out _, out _)[0];
                var force = sample.Values;
                values.Add(responseFactors?.Count == 6 ? force.Zip(responseFactors, (a, b) => a * b).Sum() : force[component]);
                sources.Add($"{modelName}/{beamId}/{station:G17}/{c}/{step.Key:G17}/{step.First().StepType}");
            }
        }
        return new(values.ToArray(), sources.ToArray());
    }


    /// <summary>Three-point rainflow counting of an open history; residual excursions remain half cycles.</summary>
    public static BridgeFatigueSpectrum Count(IReadOnlyList<double> values, double slope, double referenceCycles)
    {
        if (values == null || values.Count < 2 || values.Count > 1000000 || values.Any(v => !double.IsFinite(v)) ||
            !double.IsFinite(slope) || slope <= 0 || !double.IsFinite(referenceCycles) || referenceCycles <= 0)
            throw new ArgumentException("Provide 2..1000000 finite ordered history values and positive slope/reference cycles.");
        var distinct = new List<(double Value, int Index)> { (values[0], 0) };
        for (int i = 1; i < values.Count; i++) if (values[i] != distinct[^1].Value) distinct.Add((values[i], i));
        var turns = new List<(double Value, int Index)>();
        for (int i = 0; i < distinct.Count; i++)
            if (i == 0 || i == distinct.Count - 1 ||
                Math.Sign(distinct[i].Value - distinct[i - 1].Value) != Math.Sign(distinct[i + 1].Value - distinct[i].Value))
                turns.Add(distinct[i]);
        var stack = new List<(double Value, int Index)>(); var cycles = new List<BridgeFatigueCycle>();
        void Add((double Value, int Index) a, (double Value, int Index) b, double count)
        {
            double range = Math.Abs(a.Value - b.Value);
            if (!double.IsFinite(range)) throw new ArgumentException("History range overflow; check units.");
            if (range > 0) cycles.Add(new(range, a.Value / 2 + b.Value / 2, count, a.Index, b.Index));
        }
        foreach (var point in turns)
        {
            stack.Add(point);
            while (stack.Count >= 3)
            {
                double previous = Math.Abs(stack[^2].Value - stack[^3].Value), current = Math.Abs(stack[^1].Value - stack[^2].Value);
                if (current < previous) break;
                if (stack.Count == 3) { Add(stack[0], stack[1], .5); stack.RemoveAt(0); }
                else { Add(stack[^3], stack[^2], 1); stack.RemoveRange(stack.Count - 3, 2); }
            }
        }
        for (int i = 0; i < stack.Count - 1; i++) Add(stack[i], stack[i + 1], .5);
        // Scale before exponentiation to avoid overflow for ordinary large dimensional ranges.
        double max = cycles.Count == 0 ? 0 : cycles.Max(c => c.Range);
        double equivalent = max == 0 ? 0 : max * Math.Pow(cycles.Sum(c => c.Count * Math.Pow(c.Range / max, slope)) / referenceCycles, 1 / slope);
        if (!double.IsFinite(equivalent)) throw new ArgumentException("Equivalent range overflow; check history units and reference cycle count.");
        return new(cycles.ToArray(), values.Min(), values.Max(), equivalent, cycles.Sum(c => c.Count));
    }
}
