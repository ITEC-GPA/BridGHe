using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using Rhino.Geometry;
using GpcModel = GPC.Model.Models.Model;

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
        Arguments(loadCase, step, ref direction, camberFactor, displayScale);
        var memberStations = model.Beams.SelectMany(b => b.Attributes.OfType<BridgeMemberAttributeModel>()).Where(a => a.Value.Family == BridgeMemberFamily.Girder && a.Value.Group == girder)
            .SelectMany(a => new[] { a.Value.StationFrom, a.Value.StationTo });
        using var index = new StrausNodeIndex(model.Nodes, 1e-5);
        Vector3d[] Displacements(int number) => model.Nodes[number - 1].Results.OfType<NodeDisplResultModel>().Where(r => r.LoadCase?.Name == loadCase &&
            r.StepNum == step && (string.IsNullOrEmpty(stepType) || r.StepType == stepType)).Select(r => r.Displacement).ToArray();
        return Evaluate(model.Name, geometry, girder, loadCase, step, stepType, direction, camberFactor, relativeCamber, displayScale, memberStations, index, Displacements);
    }

    /// <summary>F6 (a)/(b): the same evaluation on an analysed GPC model (<see cref="GpcResultCatalog"/>): girder stations from the bridge
    /// metadata of its physical members, displacements from the rows the Results components show (catalog label, step 0, no step type,
    /// as the former result objects the analysis wrote from them).</summary>
    /// <param name="bridge">L12: the bridge data of the model (member metadata by beam GUID), null: none.</param>
    public static BridgeDeflectionResult Evaluate(GpcModel analysed, GPC.Checkers.CompositeBridge.BridgeModelData bridge, SimpleBridgeGeometry geometry, int girder, string loadCase,
        double step, string stepType, Vector3d direction, double camberFactor, bool relativeCamber, double displayScale)
    {
        ArgumentNullException.ThrowIfNull(analysed); BridgeGeometryTools.Validate(geometry);
        var catalog = GpcResultCatalog.Of(analysed) ?? throw new ArgumentException("The model has no stored results of a BridGHe analysis.");
        if (catalog.Units.Units != DefinitionUnits.Nmm) throw new ArgumentException("Bridge Geometry and analysed model must use N-mm.");
        Arguments(loadCase, step, ref direction, camberFactor, displayScale);
        // The member metadata of the beams in numbering order, as the former model read it from the beams.
        var memberStations = analysed.BeamElements.Values.OrderBy(b => b.Id).Select(b => bridge?.Member(b.Guid))
            .Where(m => m != null && m.Family == GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder && m.Group == girder)
            .SelectMany(m => new[] { m.StationFromMm, m.StationToMm });
        var nodes = analysed.NodesElements.Values.ToArray();
        using var index = new StrausNodeIndex(nodes.Select(n => new Point3d(n.Position.X, n.Position.Y, n.Position.Z)).ToArray(), 1e-5);
        Vector3d[] Displacements(int number) => GpcResultRows.Node(catalog, analysed, nodes[number - 1])
            .Where(r => r.Displacement != null && catalog.Entries[r.Entry].Label == loadCase && 0 == step && (string.IsNullOrEmpty(stepType) || stepType == ""))
            .Select(r => new Vector3d(GpcResultRows.Shown(r.Displacement[0]), GpcResultRows.Shown(r.Displacement[1]), GpcResultRows.Shown(r.Displacement[2]))).ToArray();
        return Evaluate(analysed.Name, geometry, girder, loadCase, step, stepType, direction, camberFactor, relativeCamber, displayScale, memberStations, index, Displacements);
    }

    private static void Arguments(string loadCase, double step, ref Vector3d direction, double camberFactor, double displayScale)
    {
        if (string.IsNullOrWhiteSpace(loadCase) || !double.IsFinite(step) || !direction.IsValid || !direction.Unitize() ||
            !double.IsFinite(camberFactor) || camberFactor < 0 || !double.IsFinite(displayScale) || displayScale <= 0)
            throw new ArgumentException("Specify a case/step, nonzero projection direction, nonnegative camber factor and positive display scale.");
    }

    private static BridgeDeflectionResult Evaluate(string modelName, SimpleBridgeGeometry geometry, int girder, string loadCase,
        double step, string stepType, Vector3d direction, double camberFactor, bool relativeCamber, double displayScale,
        IEnumerable<double> memberStations, StrausNodeIndex index, Func<int, Vector3d[]> displacements)
    {
        var supports = geometry.Supports.Where(p => p.Girder == girder).OrderBy(p => p.Station).ToArray();
        if (supports.Length < 2) throw new ArgumentException("Girder requires at least two support positions.");
        var stations = geometry.Members.Where(m => m.Family == BridgeMemberFamily.Girder && m.Group == girder)
            .SelectMany(m => new[] { m.StationFrom, m.StationTo })
            .Concat(memberStations)
            .Where(s => s >= supports[0].Station - 1e-6 && s <= supports[^1].Station + 1e-6).Distinct().OrderBy(s => s).ToArray();
        Vector3d Displacement(double station)
        {
            var match = index.Find(BridgeGeometryTools.GirderPoint(geometry, girder, station));
            if (match.Matches != 1) throw new ArgumentException("Displacement station matches more than one FEM node.");
            var results = displacements(match.Number);
            if (results.Length != 1 || !results[0].IsValid)
                throw new ArgumentException($"G{girder}, station {station:G9}: expected one finite displacement for {loadCase}/{step}/{stepType}, found {results.Length}.");
            return results[0];
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
            $"{modelName} / {loadCase} / step {step:G17} / {stepType}", new[]
            {
                "Displacements are sampled only at real FEM nodes; no invented interior beam displacement field.",
                "Relative displacement subtracts the straight interpolation of support displacements for each span, using alignment station.",
                "Camber = -factor * selected displacement; this is an explicit compensation rule, not a construction-history calculation.",
                "Overhangs outside the first/last support are omitted. Numerical outputs are mm; display scale affects curves only."
            });
    }
}
