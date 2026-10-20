using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using Rhino.Geometry;

namespace BridGHe.Common.Diagnostics;

public sealed record BridgeDeflectionResult(double[] Stations, double[] Absolute, double[] Relative, double[] Camber,
    PolylineCurve Deflected, PolylineCurve Cambered, string Source, string[] Diagnostics);

public static class BridgeDeflectionCamber
{
    public static BridgeDeflectionResult Evaluate(ModelModel model, SimpleBridgeGeometry geometry, int girder, string loadCase,
        double step, string stepType, Vector3d direction, double camberFactor, bool relativeCamber, double displayScale)
    {
        ArgumentNullException.ThrowIfNull(model); BridgeGeometryTools.Validate(geometry);
        if (model.Units != ModelModel.ModelUnits.Nmm) throw new ArgumentException("Bridge Geometry and analysed model must use N-mm.");
        if (string.IsNullOrWhiteSpace(loadCase) || !double.IsFinite(step) || !direction.IsValid || !direction.Unitize() ||
            !double.IsFinite(camberFactor) || camberFactor < 0 || !double.IsFinite(displayScale) || displayScale <= 0)
            throw new ArgumentException("Specify a case/step, nonzero projection direction, nonnegative camber factor and positive display scale.");
        var supports = geometry.Supports.Where(p => p.Girder == girder).OrderBy(p => p.Station).ToArray();
        if (supports.Length < 2) throw new ArgumentException("Girder requires at least two support positions.");
        var stations = geometry.Members.Where(m => m.Family == BridgeMemberFamily.Girder && m.Group == girder)
            .SelectMany(m => new[] { m.StationFrom, m.StationTo })
            .Concat(model.Beams.SelectMany(b => b.Attributes.OfType<BridgeMemberAttributeModel>()).Where(a => a.Value.Family == BridgeMemberFamily.Girder && a.Value.Group == girder)
                .SelectMany(a => new[] { a.Value.StationFrom, a.Value.StationTo }))
            .Where(s => s >= supports[0].Station - 1e-6 && s <= supports[^1].Station + 1e-6).Distinct().OrderBy(s => s).ToArray();
        using var index = new StrausNodeIndex(model.Nodes, 1e-5);
        Vector3d Displacement(double station)
        {
            var match = index.Find(BridgeGeometryTools.GirderPoint(geometry, girder, station));
            if (match.Matches != 1) throw new ArgumentException("Displacement station matches more than one FEM node.");
            var results = model.Nodes[match.Number - 1].Results.OfType<NodeDisplResultModel>().Where(r => r.LoadCase?.Name == loadCase &&
                r.StepNum == step && (string.IsNullOrEmpty(stepType) || r.StepType == stepType)).ToArray();
            if (results.Length != 1 || !results[0].Displacement.IsValid)
                throw new ArgumentException($"G{girder}, station {station:G9}: expected one finite displacement for {loadCase}/{step}/{stepType}, found {results.Length}.");
            return results[0].Displacement;
        }
        var supportValues = supports.Select(s => Displacement(s.Station) * direction).ToArray();
        var absolute = new List<double>(); var relative = new List<double>(); var camber = new List<double>();
        var deformed = new List<Point3d>(); var cambered = new List<Point3d>();
        foreach (double s in stations)
        {
            var u = Displacement(s); double value = u * direction;
            int span = Math.Clamp(Array.FindLastIndex(supports, p => p.Station <= s), 0, supports.Length - 2);
            double t = (s - supports[span].Station) / (supports[span + 1].Station - supports[span].Station);
            double chord = (1 - t) * supportValues[span] + t * supportValues[span + 1];
            double rel = value - chord, c = -camberFactor * (relativeCamber ? rel : value);
            var p = BridgeGeometryTools.GirderPoint(geometry, girder, s);
            absolute.Add(value); relative.Add(rel); camber.Add(c); deformed.Add(p + displayScale * u); cambered.Add(p + displayScale * c * direction);
        }
        return new(stations, absolute.ToArray(), relative.ToArray(), camber.ToArray(), new(deformed), new(cambered),
            $"{model.Name} / {loadCase} / step {step:G17} / {stepType}", new[]
            {
                "Displacements are sampled only at real FEM nodes; no invented interior beam displacement field.",
                "Relative displacement subtracts the straight interpolation of support displacements for each span, using alignment station.",
                "Camber = -factor * selected displacement; this is an explicit compensation rule, not a construction-history calculation.",
                "Overhangs outside the first/last support are omitted. Numerical outputs are mm; display scale affects curves only."
            });
    }
}
