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
    internal static void Run()
    {
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()); var model = AnalyticModel(geometry);
        var result = BridgeDeflectionCamber.Evaluate(model, geometry, 1, "G1", 0, "", Vector3d.ZAxis, 1, true, 10);
        int mid = Array.IndexOf(result.Stations, 15000d);
        Require(mid >= 0 && Math.Abs(result.Absolute[mid] + 28) < 1e-10 && Math.Abs(result.Relative[mid] + 20) < 1e-10, "Analytical parabola plus support settlements");
        Require(Math.Abs(result.Camber[mid] - 20) < 1e-10, "Opposite relative camber");
        foreach (double support in new[] { 0d, 30000, 70000, 100000 })
            Require(Math.Abs(result.Relative[Array.IndexOf(result.Stations, support)]) < 1e-10, "Zero relative displacement at supports");
        var normal = BridgeDeflectionCamber.Evaluate(model, geometry, 1, "G1", 0, "", Vector3d.ZAxis, 1, true, 1);
        Require(result.Camber.SequenceEqual(normal.Camber) && result.Absolute.SequenceEqual(normal.Absolute), "Display scale does not modify numerical results");
        Require(result.Deflected.PointAtStart.DistanceTo(normal.Deflected.PointAtStart) > 1, "Display scale affects geometry");
        try { BridgeDeflectionCamber.Evaluate(model, geometry, 1, "unknown", 0, "", Vector3d.ZAxis, 1, true, 1); }
        catch (ArgumentException) { Console.WriteLine("BRIDGE DEFLECTION PASS: analytical deflection, settlement chord, camber sign, display invariance and missing result rejection."); return; }
        throw new InvalidOperationException("Missing displacement case accepted");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE DEFLECTION: " + text); }
}
