using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using Rhino.Geometry;

internal static class BridgeSpanAdvisorTests
{
    internal static void Run()
    {
        using var alignment = new LineCurve(Point3d.Origin, new Point3d(100000, 0, 0));
        using var zone = new Rectangle3d(Plane.WorldXY, new Interval(40000, 60000), new Interval(-10000, 10000)).ToNurbsCurve();
        var advice = BridgeSpanAdvisor.Suggest(alignment, new[] { zone }, 20000, 45000, 35000, 1000, 2000, 5);
        Require(advice.Alternatives.Length == 5, "Five feasible alternatives");
        foreach (var a in advice.Alternatives)
        {
            Require(a.Stations[0] == 0 && a.Stations[^1] == 100000, "Fixed abutments");
            Require(a.Stations.All(s => s < 38000 || s > 62000), "Forbidden area and boundary clearance");
            Require(a.Lengths.All(s => s >= 20000 && s <= 45000) && a.Lengths.Sum() == 100000, "Span bounds and total length");
            Require(a.Points.Zip(a.Stations).All(p => Math.Abs(p.First.X - p.Second) < 1e-6), "Station/point pairing");
        }
        Require(advice.Alternatives.Zip(advice.Alternatives.Skip(1)).All(p => p.First.Score <= p.Second.Score), "Ranked scores");
        var required = BridgeSpanAdvisor.Suggest(alignment, Array.Empty<Curve>(), 10000, 50000, 35000, 1000, required: new[] { 27345d });
        Require(required.Alternatives.All(a => a.Stations.Contains(27345)), "Off-grid mandatory support preserved");
        var none = BridgeSpanAdvisor.Suggest(alignment, new[] { zone }, 10000, 20000, 15000, 1000, 1000);
        Require(none.Alternatives.Length == 0 && none.Diagnostics[0].Contains("grid"), "Infeasible grid reports no fabricated layout");

        // Independent exhaustive enumeration on a small grid checks the k-shortest-path objective.
        using var shortCurve = new LineCurve(Point3d.Origin, new Point3d(10, 0, 0));
        var scores = new List<double>();
        void Enumerate(double station, double cost)
        {
            if (station == 10) { scores.Add(cost); return; }
            for (int next = (int)station + 2; next <= Math.Min(10, station + 5); next++)
            {
                double target = station == 0 || next == 10 ? 3 * .8 : 3;
                Enumerate(next, cost + Math.Pow((next - station - target) / 3, 2) + (next == 10 ? 0 : .2));
            }
        }
        Enumerate(0, 0);
        var actual = BridgeSpanAdvisor.Suggest(shortCurve, null, 2, 5, 3, 1, alternatives: 10, pierPenalty: .2, endRatio: .8);
        Require(actual.Alternatives.Select(a => a.Score).Zip(scores.OrderBy(s => s).Take(10)).All(p => Math.Abs(p.First - p.Second) < 1e-12), "Top ten costs match exhaustive enumeration");
        using var arc = new ArcCurve(new Arc(new Circle(Plane.WorldXY, 100000), .9));
        var curved = BridgeSpanAdvisor.Suggest(arc, null, 20000, 40000, 30000, 1000);
        Require(curved.Alternatives.All(a => Math.Abs(a.Lengths.Sum() - arc.GetLength()) < 1e-6), "Curved alignment uses arc lengths");
        foreach (var a in curved.Alternatives)
            Require(a.Points.All(p => Math.Abs(p.DistanceTo(Point3d.Origin) - 100000) < 1e-6), "Curved support positions");
        Reject(() => BridgeSpanAdvisor.Suggest(alignment, new[] { zone }, 20000, 45000, 35000, 1000, required: new[] { 50000d }));
        Reject(() => BridgeSpanAdvisor.Suggest(alignment, null, 50000, 20000, 35000, 1000));
        Reject(() => BridgeSpanAdvisor.Suggest(alignment, null, 10000, 50000, 35000, 0));
        Reject(() => BridgeSpanAdvisor.Suggest(alignment, new[] { alignment }, 10000, 50000, 35000, 1000));
        Console.WriteLine("SPAN ADVISOR PASS: exclusions, clearance, arc lengths, required supports, no-feasible cases and exhaustive top-ten comparison.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("SPAN ADVISOR: " + text); }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new InvalidOperationException("SPAN ADVISOR: invalid input was accepted");
    }
}
