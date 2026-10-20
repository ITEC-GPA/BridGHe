using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;

namespace BridGHe.Common.Checks;

/// <summary>Resamples one case on a physical beam grid, using only original FEM samples.</summary>
internal static class BridgeForceResampling
{
    internal static BeamForceResultModel[] Resample(BeamForceResultModel[] original, double[] stations,
        double length, string label, double[] pointLoads, out int interpolated)
    {
        double tolerance = BridgeMergeValidation.StationTolerance(length);
        var samples = original.OrderBy(f => f.Station).ToArray();
        if (samples.Length < 2 || Math.Abs(samples[0].Station) > tolerance ||
            Math.Abs(samples[^1].Station - length) > tolerance)
            throw new ArgumentException($"{label}: FEM samples at both beam ends (0 and {length:G9}) are required; extrapolation is not allowed.");
        for (int i = 1; i < samples.Length; i++)
            if (samples[i].Station - samples[i - 1].Station <= tolerance)
                throw new ArgumentException($"{label}, station {samples[i].Station:G9}: expected one FEM sample; duplicate/one-sided results cannot be merged without an explicit side. Split the beam at concentrated loads.");

        interpolated = 0;
        var output = new List<BeamForceResultModel>(stations.Length);
        int right = 0;
        foreach (double station in stations)
        {
            while (right < samples.Length && samples[right].Station < station - tolerance) right++;
            if (right < samples.Length && Math.Abs(samples[right].Station - station) <= tolerance)
            {
                if (right + 1 < samples.Length && Math.Abs(samples[right + 1].Station - station) <= tolerance)
                    throw new ArgumentException($"{label}, station {station:G9}: expected one FEM sample; ambiguous station tolerance.");
                var originalSample = samples[right];
                output.Add(new BeamForceResultModel(originalSample.LoadCase, station, 0, "", false)
                {
                    AxialForce = originalSample.AxialForce, ShearForce = originalSample.ShearForce,
                    Torque = originalSample.Torque, BendingMoment = originalSample.BendingMoment
                });
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
            var result = new BeamForceResultModel(a.LoadCase, station, 0, "", false)
            {
                AxialForce = Linear(a.AxialForce, b.AxialForce),
                Vx = Linear(a.Vx, b.Vx), Vy = Linear(a.Vy, b.Vy),
                Torque = Linear(a.Torque, b.Torque),
                Mx = Moment(a.Mx, b.Mx, a.Vx, b.Vx),
                My = Moment(a.My, b.My, a.Vy, b.Vy)
            };
            if (new[] { result.AxialForce, result.Vx, result.Vy, result.Torque, result.Mx, result.My }.Any(v => !double.IsFinite(v)))
                throw new ArgumentException($"{label}, station {station:G9}: non-finite interpolated force.");
            output.Add(result);
            interpolated++;
        }
        return output.ToArray();
    }
}
