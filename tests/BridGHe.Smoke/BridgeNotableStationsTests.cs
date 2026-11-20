using System;
using System.Linq;
using BridGHe.Common.Checks;

/// <summary>0.6.0 (user rule of 20/11/2026): the analysis stations of an element and the notable sections of a girder for the Detail Check.</summary>
internal static class BridgeNotableStationsTests
{
    public static void Run()
    {
        int checks = 0;
        void Require(bool ok, string what) { if (!ok) throw new Exception("NOTABLE STATIONS: " + what); checks++; }
        // Analysis stations per element: the FEM stations 0, 250, 500, 750, 1000 (and 172 where a load starts).
        double[] fem = { 0, 172, 250, 500, 750, 1000 };
        Require(BridgeCheckStations.Select(fem, 1000, 2).SequenceEqual(new double[] { 0, 1000 }), "2 stations = the two nodes");
        Require(BridgeCheckStations.Select(fem, 1000, 3).SequenceEqual(new double[] { 0, 500, 1000 }), "3 stations = nodes and middle");
        Require(BridgeCheckStations.Select(fem, 1000, 5).SequenceEqual(new double[] { 0, 250, 500, 750, 1000 }), "5 stations = the quarter points");
        Require(ReferenceEquals(BridgeCheckStations.Select(fem, 1000, 0), fem), "0 = all the stations, unchanged");
        Require(BridgeCheckStations.Select(new double[] { 0, 1000 }, 1000, 2).Count == 2, "an element with only its ends");
        Require(Throws(() => BridgeCheckStations.Select(fem, 1000, 1)) && Throws(() => BridgeCheckStations.Select(fem, 1000, -1)), "1 and negative counts are refused");

        // A girder of 3 segments on 3 supports: C1 0-4 m (4 elements), C2 4-6 m (2 elements), C3 6-10 m (4 elements); spans 0-4 and 4-10 m.
        // Elements of C2 given reversed and out of order, to test the chain.
        BridgeNotableStations.Element E(string id, double a, double b, string section) => new(id, new(a, 0, 0), new(b, 0, 0), section);
        var elements = new[] { E("1", 0, 1000, "C1"), E("2", 1000, 2000, "C1"), E("3", 2000, 3000, "C1"), E("4", 3000, 4000, "C1"),
            E("6", 6000, 5000, "C2"), E("5", 5000, 4000, "C2"),
            E("7", 6000, 7000, "C3"), E("8", 7000, 8000, "C3"), E("9", 8000, 9000, "C3"), E("10", 9000, 10000, "C3") }.Reverse().ToArray();
        var chain = BridgeNotableStations.Order(elements, 1);
        Require(chain.Nodes.Count == 11 && Math.Abs(chain.Coordinates[10] - 10000) < 1e-9, "chain of 10 elements and 11 nodes, 10 m long");
        Require(chain.Coordinates.Zip(chain.Coordinates.Skip(1), (a, b) => b > a).All(x => x), "chain coordinates increase along the girder");
        var supports = new BridgeNotableStations.P3[] { new(0, 0, 0), new(4000, 0, 0), new(10000, 0, 0), new(5000, 3600, 0) };
        // Results: sagging measure peaking at 1.6 m (span 1) and at 7.3 m (span 2); shear highest at 5.5 m (inside C2), stud flow at the ends.
        BridgeNotableStations.Sample S(string id, double station, double sigma, double v, double flow) => new(id, station, sigma, v, flow);
        var samples = new[] {
            S("2", 500, 120, 100, 10), S("2", 600, 125, 100, 10), S("2", 0, 100, 100, 10), S("2", 1000, 110, 100, 10),
            S("8", 300, 200, 50, 5), S("8", 0, 150, 50, 5), S("8", 1000, 180, 50, 5),
            S("5", 0, 0, 300, 30), S("5", 1000, 0, 280, 30), S("6", 500, 0, 900, 30), S("6", 0, 0, 280, 30), S("6", 1000, 0, 300, 30) };
        var result = BridgeNotableStations.Points(chain, supports, samples, true, 1);
        double[] At(string kind) => result.Points.Where(p => p.Kinds.Contains(kind)).Select(p => p.ChainMm).ToArray();
        Require(At(BridgeNotableStations.KindEnd).SequenceEqual(new double[] { 0, 4000, 6000, 10000 }), "segment ends: 0, 4, 6, 10 m");
        Require(At(BridgeNotableStations.KindMiddle).SequenceEqual(new double[] { 2000, 5000, 8000 }), "segment middles on nodes: 2, 5, 8 m");
        Require(At(BridgeNotableStations.KindSupport).SequenceEqual(new double[] { 0, 4000, 10000 }), "supports on this girder only: 0, 4, 10 m");
        // Element 2 runs 1-2 m: its station 600 is 1.6 m, nearest node 2 m; element 8 runs 7-8 m: station 300 is 7.3 m, nearest node 7 m.
        Require(At(BridgeNotableStations.KindSpanMax).SequenceEqual(new double[] { 2000, 7000 }), "span sagging maxima on the nearest nodes: 2 and 7 m");
        Require(result.Points.Select(p => p.ChainMm).Distinct().Count() == result.Points.Count, "one point per node, with all its kinds");
        // Guard: the shear of C2 peaks at 5.5 m (element 6 reversed: 6 m - 500), away from the points (4, 5, 6 m).
        Require(result.Guard.Count == 1 && result.Guard[0].Section == "C2" && result.Guard[0].Quantity == "taglio" && Math.Abs(result.Guard[0].SegmentMaxChainMm - 5500) < 1e-9
            && result.Guard[0].SegmentMax == 900 && result.Guard[0].NotableMax == 300, "guard: the shear of C2 at 5.5 m");
        // Without middles and without results: only the ends and the supports, a note, no guard.
        var bare = BridgeNotableStations.Points(chain, supports, Array.Empty<BridgeNotableStations.Sample>(), false, 1);
        Require(bare.Points.Count == 4 && bare.Guard.Count == 0 && bare.Notes.Count == 1, "without middles and results: 4 points and a note");
        // A branched girder is refused.
        Require(Throws(() => BridgeNotableStations.Order(new[] { E("a", 0, 1000, "X"), E("b", 1000, 2000, "X"), E("c", 1000, 1500, "X") }, 1)), "a branched girder is refused");
        Console.WriteLine($"BRIDGE NOTABLE STATIONS PASS: {checks} checks (analysis stations per element, chain, segment ends and middles, supports, span maxima, guard).");
    }

    private static bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }
}
