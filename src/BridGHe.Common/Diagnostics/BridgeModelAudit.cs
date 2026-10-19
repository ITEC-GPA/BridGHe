using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Diagnostics;

public sealed record ModelIssue(string Severity, string Code, string Element, string Description);

/// <summary>Read-only input diagnostics. Geometric connectivity is not a stiffness/stability test.</summary>
public static class BridgeModelAudit
{
    public static ModelIssue[] Inspect(ModelModel model, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentException("Tolerance must be finite and positive, in model length units.");
        var issues = new List<ModelIssue>();
        void Add(string severity, string code, string id, string text) => issues.Add(new(severity, code, id, text));
        void Ids(IEnumerable<string> values, string kind)
        {
            var list = values.ToArray();
            if (list.Any(string.IsNullOrWhiteSpace)) Add("Error", "EMPTY_ID", kind, "One or more identifiers are empty.");
            foreach (var g in list.GroupBy(x => x).Where(g => g.Count() > 1)) Add("Error", "DUPLICATE_ID", kind + ":" + g.Key, $"{g.Count()} repeated identifiers.");
        }
        Ids(model.Beams.Select(b => b.BeamId), "Beam"); Ids(model.Nodes.Select(n => n.NodeId), "Node");
        Ids(model.Plates.Select(p => p.PlateId), "Plate"); Ids(model.LoadCases.Select(c => c.Name), "Case");
        if (model.Beams.Count + model.Plates.Count == 0) Add("Error", "EMPTY_MODEL", "Model", "No beam or plate elements.");
        var vertices = new List<Point3d>(); var parent = new List<int>();
        var cells = new Dictionary<(long, long, long), List<int>>();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Join(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }
        int structuralCount = int.MaxValue; bool touchesElement = false;
        int Vertex(Point3d point)
        {
            touchesElement = false;
            if (!point.IsValid) return -1;
            double cx = Math.Floor(point.X / tolerance), cy = Math.Floor(point.Y / tolerance), cz = Math.Floor(point.Z / tolerance);
            if (Math.Max(Math.Abs(cx), Math.Max(Math.Abs(cy), Math.Abs(cz))) > 9e18) throw new ArgumentException("Tolerance too small for the model coordinate range.");
            var cell = ((long)cx, (long)cy, (long)cz); int index = vertices.Count;
            vertices.Add(point); parent.Add(index);
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                if (cells.TryGetValue((cell.Item1 + x, cell.Item2 + y, cell.Item3 + z), out var neighbours))
                    foreach (int n in neighbours) if (vertices[n].DistanceTo(point) <= tolerance)
                    { Join(index, n); if (n < structuralCount) touchesElement = true; }
            if (!cells.TryGetValue(cell, out var bucket)) cells[cell] = bucket = new List<int>();
            bucket.Add(index); return index;
        }
        foreach (var beam in model.Beams)
        {
            if (!beam.IsValid || !double.IsFinite(beam.Length) || beam.Length <= tolerance)
                Add("Error", "INVALID_BEAM", beam.BeamId, "Invalid geometry/property or length at/below tolerance.");
            int a = Vertex(beam.PointFrom), b = Vertex(beam.PointTo); if (a >= 0 && b >= 0) Join(a, b);
        }
        foreach (var plate in model.Plates)
        {
            if (plate.Points.Count < 3 || plate.Points.Any(p => !p.IsValid) || plate.PlateProperty?.IsValid != true)
                Add("Error", "INVALID_PLATE", plate.PlateId, "Invalid plate geometry or property.");
            var points = plate.Points.Select(Vertex).Where(i => i >= 0).ToArray();
            foreach (int i in points.Skip(1)) Join(points[0], i);
        }
        int parts = Enumerable.Range(0, vertices.Count).Select(Find).Distinct().Count();
        if (parts > 1) Add("Warning", "DISCONNECTED", "Model", $"{parts} geometric connected parts. Intersections without shared endpoints/plate vertices are not connections.");
        structuralCount = vertices.Count;
        foreach (var node in model.Nodes)
        {
            if (!node.Position.IsValid) { Add("Error", "INVALID_NODE", node.NodeId, "Invalid coordinate."); continue; }
            Vertex(node.Position);
            if (!touchesElement) Add("Warning", "ISOLATED_NODE", node.NodeId, "Node does not coincide with an element vertex within tolerance.");
        }
        if (!model.Elements.SelectMany(e => e.Attributes).Any(a => a is NodeRestraintAttributeModel or NodeStiffnessAttributeModel or BeamSupportAttributeModel or PlateSupportAttributeModel))
            Add("Warning", "NO_SUPPORT", "Model", "No restraint or elastic support attribute. Stability must be checked in Straus.");
        foreach (var e in model.Elements)
            foreach (var result in e.Results)
                if (result.LoadCase == null || string.IsNullOrWhiteSpace(result.LoadCase.Name) || !double.IsFinite(result.StepNum))
                    Add("Error", "INVALID_RESULT", "Model", "A stored result has no case identity or a non-finite step.");
        return issues.ToArray();
    }
}
