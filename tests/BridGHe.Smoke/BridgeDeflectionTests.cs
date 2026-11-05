using System;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class BridgeDeflectionTests
{
    internal static ModelModel AnalyticModel(SimpleBridgeGeometry geometry)
    {
        var model = BridgeConstructionTests.Model(geometry);
        var supports = geometry.Supports.Where(s => s.Girder == 1).Select(s => s.Station).ToArray();
        foreach (var node in model.Nodes)
        {
            double s = node.Position.X;
            int span = Math.Clamp(Array.FindLastIndex(supports, p => p <= s), 0, supports.Length - 2);
            double t = (s - supports[span]) / (supports[span + 1] - supports[span]);
            node.Results.Add(new NodeDisplResultModel(model.LoadCases[0]) { Displacement = new Vector3d(0, 0, -5 - .0002 * s - 80 * t * (1 - t)) });
        }
        return model;
    }
#if GPC_PILOT
    /// <summary>P2T: the same analytical girder as <see cref="AnalyticModel"/>, built directly in GPC (no FEM model): the nodes of the
    /// geometry, one beam per geometry member with its bridge metadata (physical members, as Bridge Member Assignment and the legacy
    /// conversion give them) and the analytical displacements of case G1 as stored results.</summary>
    internal static GPC.Model.Models.Model AnalyticGpc(SimpleBridgeGeometry geometry)
    {
        var test = new GpcTestModel("Bridge design test"); test.Case("G1");
        var nodes = geometry.Nodes.Select((p, i) => test.Node((i + 1).ToString(), p.X, p.Y, p.Z)).ToArray();
        foreach (var member in geometry.Members)
            test.Member(test.Beam(member.Id, nodes[member.NodeFrom - 1], nodes[member.NodeTo - 1]), Enum.Parse<GPC.Model.Structure.Members.BridgeMemberFamily>(member.Family.ToString()),
                member.Group, member.Segment, member.Span, member.StationFrom, member.StationTo);
        var supports = geometry.Supports.Where(s => s.Girder == 1).Select(s => s.Station).ToArray();
        for (int i = 0; i < nodes.Length; i++)
        {
            double s = geometry.Nodes[i].X;
            int span = Math.Clamp(Array.FindLastIndex(supports, p => p <= s), 0, supports.Length - 2);
            double t = (s - supports[span]) / (supports[span + 1] - supports[span]);
            test.Displacement(nodes[i], "G1", 0, 0, -5 - .0002 * s - 80 * t * (1 - t));
        }
        return test.Analyse();
    }
#endif
    /// <summary>The analytical assertions of the test, on the FEM or the GPC evaluation (the same meaning on both paths).</summary>
    private static void Verify(Func<double, BridgeDeflectionResult> evaluate, Action missing, string path)
    {
        var result = evaluate(10);
        int mid = Array.IndexOf(result.Stations, 15000d);
        Require(mid >= 0 && Math.Abs(result.Absolute[mid] + 28) < 1e-10 && Math.Abs(result.Relative[mid] + 20) < 1e-10, path + "Analytical parabola plus support settlements");
        Require(Math.Abs(result.Camber[mid] - 20) < 1e-10, path + "Opposite relative camber");
        foreach (double support in new[] { 0d, 30000, 70000, 100000 })
            Require(Math.Abs(result.Relative[Array.IndexOf(result.Stations, support)]) < 1e-10, path + "Zero relative displacement at supports");
        var normal = evaluate(1);
        Require(result.Camber.SequenceEqual(normal.Camber) && result.Absolute.SequenceEqual(normal.Absolute), path + "Display scale does not modify numerical results");
        Require(result.Deflected.PointAtStart.DistanceTo(normal.Deflected.PointAtStart) > 1, path + "Display scale affects geometry");
        try { missing(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException(path + "Missing displacement case accepted");
    }

    internal static void Run()
    {
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
#if GPC_PILOT
        // P2T: the test on the analysed GPC model built in GPC (girder stations from the physical members, displacements from the GPC
        // rows). It stays after the FeMM removal; the FEM part below becomes its parity reference until then.
        var analysed = AnalyticGpc(geometry);
        BridgeDeflectionResult Gpc(double scale) => BridgeDeflectionCamber.Evaluate(analysed, geometry, 1, "G1", 0, "", Vector3d.ZAxis, 1, true, scale);
        Verify(Gpc, () => BridgeDeflectionCamber.Evaluate(analysed, geometry, 1, "unknown", 0, "", Vector3d.ZAxis, 1, true, 1), "GPC: ");
#endif
        // FEM path (removed with the FeMM models): the same assertions, and the GPC evaluations equal to it bit for bit.
        var model = AnalyticModel(geometry);
        BridgeDeflectionResult Fem(double scale) => BridgeDeflectionCamber.Evaluate(model, geometry, 1, "G1", 0, "", Vector3d.ZAxis, 1, true, scale);
        Verify(Fem, () => BridgeDeflectionCamber.Evaluate(model, geometry, 1, "unknown", 0, "", Vector3d.ZAxis, 1, true, 1), "FEM: ");
#if GPC_PILOT
        static bool Same(double[] a, double[] b) => a.Length == b.Length && a.Zip(b).All(p => BitConverter.DoubleToInt64Bits(p.First) == BitConverter.DoubleToInt64Bits(p.Second));
        static double[] Coordinates(PolylineCurve c) => c.ToPolyline().SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray();
        var result = Fem(10);
        // The GPC model built in GPC and the conversion of the FEM model (GpcSyntheticResults, the route of GH_Model.Gpc today).
        foreach (var (gpc, label) in new[] { (Gpc(10), "GPC model built in GPC"), (BridgeDeflectionCamber.Evaluate(GpcSyntheticResults.Analysed(model), geometry, 1, "G1", 0, "", Vector3d.ZAxis, 1, true, 10), "converted FEM model") })
            Require(Same(gpc.Stations, result.Stations) && Same(gpc.Absolute, result.Absolute) && Same(gpc.Relative, result.Relative) && Same(gpc.Camber, result.Camber)
                && Same(Coordinates(gpc.Deflected), Coordinates(result.Deflected)) && Same(Coordinates(gpc.Cambered), Coordinates(result.Cambered))
                && gpc.Source == result.Source && gpc.Diagnostics.SequenceEqual(result.Diagnostics), "GPC evaluation (" + label + ") equal to the FEM one bit for bit");
#endif
        Console.WriteLine("BRIDGE DEFLECTION PASS: analytical deflection, settlement chord, camber sign, display invariance and missing result rejection"
#if GPC_PILOT
            + " (GPC model built in GPC; FEM path and converted FEM model equal bit for bit)"
#endif
            + ".");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE DEFLECTION: " + text); }
}
