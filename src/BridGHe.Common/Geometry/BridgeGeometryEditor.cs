using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

internal sealed class BridgeGeometryEditor : IDisposable
{
    public SimpleBridgeGeometry Geometry { get; }
    private readonly Dictionary<(long, long, long), List<int>> cells = new();
    private readonly RTree memberIndex = new();
    private const double Tol = SimpleBridgeBuilder.JoinTolerance;
    public BridgeGeometryEditor(SimpleBridgeGeometry source)
    {
        Geometry = ModelArchive.Clone(source);
        for (int i = 0; i < Geometry.Nodes.Count; i++) Register(Geometry.Nodes[i], i);
        for (int i = 0; i < Geometry.Members.Count; i++) memberIndex.Insert(Geometry.Members[i].Axis.BoundingBox, i);
    }
    private static (long, long, long) Cell(Point3d p) => ((long)Math.Floor(p.X / Tol), (long)Math.Floor(p.Y / Tol), (long)Math.Floor(p.Z / Tol));
    private void Register(Point3d p, int index)
    {
        var c = Cell(p);
        if (!cells.TryGetValue(c, out var list)) cells[c] = list = new();
        list.Add(index);
    }
    public int Node(Point3d point)
    {
        if (!point.IsValid) throw new ArgumentException("Invalid bridge connection point.");
        var c = Cell(point);
        for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
            if (cells.TryGetValue((c.Item1 + x, c.Item2 + y, c.Item3 + z), out var bucket))
                foreach (int i in bucket) if (Geometry.Nodes[i].DistanceTo(point) <= Tol) return i + 1;
        Geometry.Nodes.Add(point); Register(point, Geometry.Nodes.Count - 1); return Geometry.Nodes.Count;
    }
    public void SplitAt(Point3d point)
    {
        var candidates = new List<int>();
        memberIndex.Search(new Sphere(point, Tol), (_, e) => candidates.Add(e.Id));
        foreach (int i in candidates)
        {
            var m = Geometry.Members[i]; double t = m.Axis.ClosestParameter(point);
            if (t <= 0 || t >= 1 || m.Axis.PointAt(t).DistanceTo(point) > Tol ||
                point.DistanceTo(m.Axis.From) <= Tol || point.DistanceTo(m.Axis.To) <= Tol) continue;
            int n = Node(point); var p = Geometry.Nodes[n - 1]; double s = m.StationFrom + t * (m.StationTo - m.StationFrom);
            memberIndex.Remove(m.Axis.BoundingBox, i);
            Geometry.Members[i] = m with { Id = $"{m.Id}/A", Axis = new(m.Axis.From, p), NodeTo = n, StationTo = s };
            Geometry.Members.Add(m with { Id = $"{m.Id}/B", Axis = new(p, m.Axis.To), NodeFrom = n, StationFrom = s });
            memberIndex.Insert(Geometry.Members[i].Axis.BoundingBox, i);
            memberIndex.Insert(Geometry.Members[^1].Axis.BoundingBox, Geometry.Members.Count - 1);
        }
    }
    public void Add(string id, BridgeMemberFamily family, int group, int segment, Point3d from, Point3d to, double start, double end)
    {
        int a = Node(from), b = Node(to);
        if (a == b) throw new ArgumentException("Generated bridge member has coincident endpoints.");
        if (Geometry.Members.Any(m => m.NodeFrom == a && m.NodeTo == b || m.NodeFrom == b && m.NodeTo == a)) return;
        Geometry.Members.Add(new(id, family, group, segment, 0, a, b, new(Geometry.Nodes[a - 1], Geometry.Nodes[b - 1]), start, end));
        memberIndex.Insert(Geometry.Members[^1].Axis.BoundingBox, Geometry.Members.Count - 1);
        if (Geometry.Members.Count > 200000) throw new ArgumentException("At most 200000 generated members.");
    }
    public void Dispose() => memberIndex.Dispose();
}
