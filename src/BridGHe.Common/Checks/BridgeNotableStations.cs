using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Common.Checks
{
    /// <summary>The notable sections of a girder for the Detail Check (user rule of 20/11/2026): both ends (S0/S1) and the middle (SM) of every
    /// section-constant segment, the supports, the sagging maximum of each span (from the Super Element Check results); all on the girder
    /// nodes (the Detail Stations match existing result stations, no interpolation). The guard lists the segments where the shear or the stud
    /// flow of the Super Element Check is higher away from the chosen points: the checks only the Detail makes (shear buckling with stiffeners,
    /// studs) follow those two quantities.</summary>
    public static class BridgeNotableStations
    {
        /// <summary>A point in mm.</summary>
        public readonly record struct P3(double X, double Y, double Z)
        {
            public double DistanceTo(P3 o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y) + (Z - o.Z) * (Z - o.Z));
            public static P3 Lerp(P3 a, P3 b, double t) => new P3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }

        /// <summary>An element of a girder: its ID, its ends and the key of its section (elements of one segment share the key).</summary>
        public sealed record Element(string BeamId, P3 Start, P3 End, string Section);

        /// <summary>A result of the Super Element Check at a station of an element: the bottom-fibre steel stress (tension positive, the
        /// sagging measure), the total shear and the stud flow (absolute values are compared).</summary>
        public sealed record Sample(string BeamId, double StationMm, double BottomSteelSigma, double Shear, double StudFlow);

        /// <summary>A notable point: the node, its chain coordinate from the first node of the girder, the kinds that put it there.</summary>
        public sealed record NotablePoint(P3 Point, double ChainMm, int Node, IReadOnlyList<string> Kinds);

        /// <summary>A guard warning: the segment, the quantity, its maximum in the segment and where, the maximum at the chosen points.</summary>
        public sealed record GuardWarning(int Segment, string Section, string Quantity, double SegmentMax, double SegmentMaxChainMm, double NotableMax);

        public sealed record GirderResult(IReadOnlyList<NotablePoint> Points, IReadOnlyList<GuardWarning> Guard, IReadOnlyList<string> Notes);

        public const string KindEnd = "estremo di tratto";
        public const string KindMiddle = "metà tratto";
        public const string KindSupport = "appoggio";
        public const string KindSpanMax = "massimo positivo di campata";

        /// <summary>The girders whose points are given (0.6.0, 99 REV006: a Detail Check fed by the Super Element Check of one girder): with
        /// results, the girders with at least one element in them, since Bridge Detail Stations finds no station on the others; without
        /// results, all the girders. In the order given.</summary>
        public static IReadOnlyList<string> Covered(IReadOnlyList<(string Girder, IReadOnlyList<string> BeamIds)> girders, IReadOnlyCollection<string> resultBeams)
        {
            if (resultBeams == null || resultBeams.Count == 0) return girders.Select(g => g.Girder).ToArray();
            var set = resultBeams as ISet<string> ?? new HashSet<string>(resultBeams, StringComparer.Ordinal);
            return girders.Where(g => g.BeamIds.Any(set.Contains)).Select(g => g.Girder).ToArray();
        }

        /// <summary>The ordered chain of a girder: nodes and, for every element, its first node and whether it runs against the chain.</summary>
        public sealed class Chain
        {
            public IReadOnlyList<P3> Nodes { get; }
            public IReadOnlyList<double> Coordinates { get; }
            public IReadOnlyList<Element> Elements { get; }
            public IReadOnlyList<bool> Reversed { get; }

            internal Chain(List<P3> nodes, List<Element> elements, List<bool> reversed)
            {
                Nodes = nodes; Elements = elements; Reversed = reversed;
                var s = new double[nodes.Count];
                for (int k = 1; k < nodes.Count; k++) s[k] = s[k - 1] + nodes[k - 1].DistanceTo(nodes[k]);
                Coordinates = s;
            }

            /// <summary>The chain coordinate of a station of element <paramref name="index"/> (station from its own start).</summary>
            public double At(int index, double station) => Reversed[index] ? Coordinates[index + 1] - station : Coordinates[index] + station;

            /// <summary>The node nearest to a chain coordinate.</summary>
            public int NearestNode(double s)
            {
                int best = 0; double d = double.PositiveInfinity;
                for (int k = 0; k < Coordinates.Count; k++) { double e = Math.Abs(Coordinates[k] - s); if (e < d) { d = e; best = k; } }
                return best;
            }
        }

        /// <summary>Orders the elements of one girder into a chain by connectivity (ends within <paramref name="tolerance"/> mm). A girder that
        /// is not a single open chain (branches, gaps, loops) is refused.</summary>
        public static Chain Order(IReadOnlyList<Element> elements, double tolerance)
        {
            if (elements == null || elements.Count == 0) throw new ArgumentException("Trave senza elementi.");
            if (!(tolerance > 0)) throw new ArgumentException("Tolleranza non valida.");
            var used = new bool[elements.Count];
            bool Same(P3 a, P3 b) => a.DistanceTo(b) <= tolerance;
            int Degree(P3 p) => elements.Count(e => Same(e.Start, p) || Same(e.End, p));
            // the first node: of the free ends (a node of only one element), the one with the lowest X (then Y, Z), whatever the order of the
            // elements in the model
            var free = new List<(int Element, bool Reversed, P3 Node)>();
            for (int i = 0; i < elements.Count; i++)
            {
                if (Degree(elements[i].Start) == 1) free.Add((i, false, elements[i].Start));
                if (Degree(elements[i].End) == 1) free.Add((i, true, elements[i].End));
            }
            if (free.Count == 0) throw new ArgumentException("La trave non è una catena aperta di elementi (anello o elementi sovrapposti).");
            var start = free.OrderBy(f => f.Node.X).ThenBy(f => f.Node.Y).ThenBy(f => f.Node.Z).First();
            int first = start.Element; bool firstReversed = start.Reversed;
            var nodes = new List<P3>(); var ordered = new List<Element>(); var reversed = new List<bool>();
            int current = first; bool rev = firstReversed;
            nodes.Add(rev ? elements[current].End : elements[current].Start);
            while (current >= 0)
            {
                used[current] = true; ordered.Add(elements[current]); reversed.Add(rev);
                var tail = rev ? elements[current].Start : elements[current].End;
                nodes.Add(tail);
                int next = -1; bool nextRev = false;
                for (int i = 0; i < elements.Count; i++)
                {
                    if (used[i]) continue;
                    if (Same(elements[i].Start, tail)) { if (next >= 0) throw Branch(tail); next = i; nextRev = false; }
                    else if (Same(elements[i].End, tail)) { if (next >= 0) throw Branch(tail); next = i; nextRev = true; }
                }
                current = next; rev = nextRev;
            }
            if (used.Any(u => !u)) throw new ArgumentException("La trave non è una catena unica: ci sono elementi staccati.");
            return new Chain(nodes, ordered, reversed);
        }

        private static ArgumentException Branch(P3 p) => new ArgumentException($"La trave si ramifica al nodo ({p.X:0.#}; {p.Y:0.#}; {p.Z:0.#}).");

        /// <summary>The notable points of one girder and the guard.</summary>
        /// <param name="chain">The ordered girder.</param>
        /// <param name="supports">Support points (any girder: those farther than <paramref name="tolerance"/> from every node are ignored).</param>
        /// <param name="samples">Super Element Check results of this girder (any element of it), or empty: then no span maxima and no guard.</param>
        /// <param name="middles">Whether to add the middle of every segment.</param>
        /// <param name="tolerance">Point matching tolerance [mm].</param>
        /// <param name="guardRatio">Relative margin of the guard (0.01 = a segment is listed when its maximum is 1 % above the chosen points).</param>
        public static GirderResult Points(Chain chain, IEnumerable<P3> supports, IReadOnlyList<Sample> samples, bool middles, double tolerance, double guardRatio = 0.01)
        {
            var kinds = new SortedDictionary<int, List<string>>();
            void Add(int node, string kind) { if (!kinds.TryGetValue(node, out var list)) kinds[node] = list = new List<string>(); if (!list.Contains(kind)) list.Add(kind); }
            var notes = new List<string>();
            // segments: runs of elements with the same section key
            var segments = new List<(int FirstNode, int LastNode, string Section)>();
            int startNode = 0;
            for (int e = 1; e <= chain.Elements.Count; e++)
                if (e == chain.Elements.Count || !string.Equals(chain.Elements[e].Section, chain.Elements[e - 1].Section, StringComparison.Ordinal))
                { segments.Add((startNode, e, chain.Elements[e - 1].Section)); startNode = e; }
            foreach (var seg in segments)
            {
                Add(seg.FirstNode, KindEnd); Add(seg.LastNode, KindEnd);
                if (middles && seg.LastNode - seg.FirstNode >= 2)
                {
                    double mid = (chain.Coordinates[seg.FirstNode] + chain.Coordinates[seg.LastNode]) / 2;
                    int node = Enumerable.Range(seg.FirstNode + 1, seg.LastNode - seg.FirstNode - 1).OrderBy(k => Math.Abs(chain.Coordinates[k] - mid)).First();
                    Add(node, KindMiddle);
                }
            }
            // supports on this girder, on its nodes
            var supportNodes = new SortedSet<int>();
            foreach (var p in supports ?? Enumerable.Empty<P3>())
            {
                int best = -1; double d = double.PositiveInfinity;
                for (int k = 0; k < chain.Nodes.Count; k++) { double e = chain.Nodes[k].DistanceTo(p); if (e < d) { d = e; best = k; } }
                if (best >= 0 && d <= tolerance) { supportNodes.Add(best); Add(best, KindSupport); }
            }
            // results by chain coordinate
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int e = 0; e < chain.Elements.Count; e++) index[chain.Elements[e].BeamId] = e;
            var located = new List<(double S, Sample Sample)>();
            foreach (var r in samples ?? Array.Empty<Sample>())
                if (r != null && index.TryGetValue(r.BeamId, out int e)) located.Add((chain.At(e, r.StationMm), r));
            if (located.Count == 0) notes.Add("Senza risultati del Super Element Check: niente massimi di campata e niente controllo.");
            else if (supportNodes.Count < 2) notes.Add("Meno di due appoggi su questa trave: niente massimi di campata.");
            else
            {
                var spans = supportNodes.ToArray();
                for (int k = 0; k + 1 < spans.Length; k++)
                {
                    double a = chain.Coordinates[spans[k]], b = chain.Coordinates[spans[k + 1]];
                    var inside = located.Where(l => l.S > a + tolerance && l.S < b - tolerance && !double.IsNaN(l.Sample.BottomSteelSigma)).ToArray();
                    if (inside.Length == 0) continue;
                    var top = inside.OrderByDescending(l => l.Sample.BottomSteelSigma).First();
                    if (top.Sample.BottomSteelSigma > 0) Add(chain.NearestNode(top.S), KindSpanMax);
                }
            }
            var points = kinds.Select(k => new NotablePoint(chain.Nodes[k.Key], chain.Coordinates[k.Key], k.Key, k.Value)).ToArray();
            // guard: per segment, |V| and stud flow, everywhere against the chosen points
            var guard = new List<GuardWarning>();
            if (located.Count > 0)
            {
                var chosen = points.Select(p => p.ChainMm).ToArray();
                bool AtChosen(double s) => chosen.Any(c => Math.Abs(c - s) <= tolerance);
                for (int g = 0; g < segments.Count; g++)
                {
                    double a = chain.Coordinates[segments[g].FirstNode], b = chain.Coordinates[segments[g].LastNode];
                    var inSegment = located.Where(l => l.S >= a - tolerance && l.S <= b + tolerance).ToArray();
                    foreach (var (name, value) in new (string, Func<Sample, double>)[] { ("taglio", s => Math.Abs(s.Shear)), ("flusso pioli", s => Math.Abs(s.StudFlow)) })
                    {
                        var valid = inSegment.Where(l => !double.IsNaN(value(l.Sample))).ToArray();
                        if (valid.Length == 0) continue;
                        var max = valid.OrderByDescending(l => value(l.Sample)).First();
                        double atChosen = valid.Where(l => AtChosen(l.S)).Select(l => value(l.Sample)).DefaultIfEmpty(0).Max();
                        if (value(max.Sample) > 0 && atChosen < value(max.Sample) * (1 - guardRatio))
                            guard.Add(new GuardWarning(g, segments[g].Section, name, value(max.Sample), max.S, atChosen));
                    }
                }
            }
            return new GirderResult(points, guard, notes);
        }
    }
}
