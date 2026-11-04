using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;

namespace BridGHe.Common.Checks;

/// <summary>F6 (a)/(b): one beam force sample as numbers, BridGHe convention (station from the beam start in the definition length, then
/// axial force, SF1, SF2, torque, BM1, BM2), the form the GPC result rows have (<see cref="Gpc.GpcResultRows.BeamRow"/>).</summary>
public readonly record struct BeamForceSample(double Station, double Axial, double Shear1, double Shear2, double Torque, double Bending1, double Bending2)
{
    public double[] Values => new[] { Axial, Shear1, Shear2, Torque, Bending1, Bending2 };
    public static BeamForceSample Of(Gpc.GpcResultRows.BeamRow row) => new(row.Station, row.Axial, row.Shear1, row.Shear2, row.Torque, row.Bending1, row.Bending2);
}

/// <summary>F6 (a)/(b), R10: a beam force entered by hand (component Force): its case name, the sample (station as entered, BridGHe
/// convention) and whether the station is normalized. Replaces the former result object the component built.</summary>
public sealed record BridgeForce(string Case, BeamForceSample Force, bool NormalizedStation)
{
    public override string ToString() => $"{Case} @ {Force.Station:G6}{(NormalizedStation ? " (normalized)" : "")}: N {Force.Axial:G6}, V {Force.Shear1:G6}/{Force.Shear2:G6}, T {Force.Torque:G6}, M {Force.Bending1:G6}/{Force.Bending2:G6}";
}

/// <summary>Resamples one case on a physical beam grid, using only original FEM samples.</summary>
internal static class BridgeForceResampling
{
    /// <summary>The former result objects through the numeric core (same arithmetic, so both give the same numbers bit for bit).</summary>
    internal static BeamForceResultModel[] Resample(BeamForceResultModel[] original, double[] stations,
        double length, string label, double[] pointLoads, out int interpolated)
    {
        var samples = original.Select(f => new BeamForceSample(f.Station, f.AxialForce, f.Vx, f.Vy, f.Torque, f.Mx, f.My)).ToArray();
        var resampled = Resample(samples, stations, length, label, pointLoads, out interpolated, out var sources);
        var output = new BeamForceResultModel[resampled.Length];
        for (int i = 0; i < resampled.Length; i++)
        {
            var r = resampled[i];
            output[i] = new BeamForceResultModel(original[sources[i]].LoadCase, r.Station, 0, "", false)
            { AxialForce = r.Axial, Vx = r.Shear1, Vy = r.Shear2, Torque = r.Torque, Mx = r.Bending1, My = r.Bending2 };
        }
        return output;
    }

    /// <param name="sources">For every output sample, the index in <paramref name="original"/> of the sample it copies, or of the left
    /// sample of the interval it interpolates.</param>
    internal static BeamForceSample[] Resample(BeamForceSample[] original, double[] stations,
        double length, string label, double[] pointLoads, out int interpolated, out int[] sources)
    {
        double tolerance = BridgeMergeValidation.StationTolerance(length);
        var order = Enumerable.Range(0, original.Length).OrderBy(i => original[i].Station).ToArray();
        var samples = order.Select(i => original[i]).ToArray();
        if (samples.Length < 2 || Math.Abs(samples[0].Station) > tolerance ||
            Math.Abs(samples[^1].Station - length) > tolerance)
            throw new ArgumentException($"{label}: FEM samples at both beam ends (0 and {length:G9}) are required; extrapolation is not allowed.");
        for (int i = 1; i < samples.Length; i++)
            if (samples[i].Station - samples[i - 1].Station <= tolerance)
                throw new ArgumentException($"{label}, station {samples[i].Station:G9}: expected one FEM sample; duplicate/one-sided results cannot be merged without an explicit side. Split the beam at concentrated loads.");

        interpolated = 0;
        var output = new List<BeamForceSample>(stations.Length);
        var from = new List<int>(stations.Length);
        int right = 0;
        foreach (double station in stations)
        {
            while (right < samples.Length && samples[right].Station < station - tolerance) right++;
            if (right < samples.Length && Math.Abs(samples[right].Station - station) <= tolerance)
            {
                if (right + 1 < samples.Length && Math.Abs(samples[right + 1].Station - station) <= tolerance)
                    throw new ArgumentException($"{label}, station {station:G9}: expected one FEM sample; ambiguous station tolerance.");
                output.Add(samples[right] with { Station = station });
                from.Add(order[right]);
                continue;
            }
            if (right == 0 || right == samples.Length)
                throw new ArgumentException($"{label}, station {station:G9}: outside the sampled interval; extrapolation is not allowed.");
            var a = samples[right - 1];
            var b = samples[right];
            if (pointLoads.Any(x => x > tolerance && x < length - tolerance &&
                x >= a.Station - tolerance && x <= b.Station + tolerance))
                throw new ArgumentException($"{label}, station {station:G9}: interpolation would cross or touch a concentrated load. Split the beam at the load to retain both sides.");
            double h = b.Station - a.Station;
            double t = (station - a.Station) / h;
            double Linear(double left, double next) => (1 - t) * left + t * next;
            // Straus diagram convention: dM1/dx = V1, dM2/dx = V2.
            // Integrate the linear shear variation, then anchor the result at BOTH FEM moments.
            // Exact for linear shear / quadratic moment; unlike recursive integration, no drift
            // depends on the number/order of stations contributed by the other phase models.
            double Moment(double ma, double mb, double va, double vb) =>
                Linear(ma, mb) + 0.5 * h * (va - vb) * t * (1 - t);
            var result = new BeamForceSample(station, Linear(a.Axial, b.Axial), Linear(a.Shear1, b.Shear1), Linear(a.Shear2, b.Shear2),
                Linear(a.Torque, b.Torque), Moment(a.Bending1, b.Bending1, a.Shear1, b.Shear1), Moment(a.Bending2, b.Bending2, a.Shear2, b.Shear2));
            if (result.Values.Any(v => !double.IsFinite(v)))
                throw new ArgumentException($"{label}, station {station:G9}: non-finite interpolated force.");
            output.Add(result);
            from.Add(order[right - 1]);
            interpolated++;
        }
        sources = from.ToArray();
        return output.ToArray();
    }
}
