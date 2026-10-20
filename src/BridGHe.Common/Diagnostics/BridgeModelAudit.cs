using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using BridGHe.Common.Geometry;
using BridGHe.Common.Straus;
using Rhino.Geometry;

namespace BridGHe.Common.Diagnostics;

public sealed record ModelIssue(string Severity, string Code, string Element, string Description);

/// <summary>Read-only input diagnostics. Geometric connectivity is not a stiffness/stability test.</summary>
public static class BridgeModelAudit
{
    public static ModelIssue[] Inspect(ModelModel model, double tolerance, SimpleBridgeGeometry expectedGeometry = null,
        IReadOnlyList<ModelModel> phaseModels = null)
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
            if (!double.IsFinite(beam.AngleDeg) || !beam.Offset.IsValid)
                Add("Error", "INVALID_ORIENTATION", beam.BeamId, "Non-finite section angle/offset.");
            var metadata = beam.Attributes.OfType<BridgeMemberAttributeModel>().ToArray();
            if (metadata.Length > 1) Add("Error", "MULTIPLE_BRIDGE_IDENTITIES", beam.BeamId, "More than one physical member identity.");
            if (metadata.Length == 1)
            {
                var d = metadata[0].Value;
                if (d == null || string.IsNullOrWhiteSpace(d.PhysicalId) || !double.IsFinite(d.StationFrom) || !double.IsFinite(d.StationTo) ||
                    d.Family == BridgeMemberFamily.Girder && d.StationTo <= d.StationFrom)
                    Add("Error", "INVALID_BRIDGE_IDENTITY", beam.BeamId, "Invalid physical member station range.");
                else if (beam.IsValid && d.SectionUp.IsValid)
                {
                    beam.GetLocalAxes(false, out _, out var up, out _, out _);
                    if (up * d.SectionUp < 1 - 1e-6) Add("Warning", "SECTION_AXIS_CHANGED", beam.BeamId, "Current section orientation differs from the stored bridge assignment frame.");
                }
            }
        }
        foreach (var plate in model.Plates)
        {
            if (plate.Points.Count < 3 || plate.Points.Any(p => !p.IsValid) || plate.PlateProperty?.IsValid != true)
                Add("Error", "INVALID_PLATE", plate.PlateId, "Invalid plate geometry or property.");
            var points = plate.Points.Select(Vertex).Where(i => i >= 0).ToArray();
            foreach (int i in points.Skip(1)) Join(points[0], i);
        }
        try
        {
            foreach (var link in RigidConnectionExport.Resolve(model, tolerance))
            { int a = Vertex(model.Nodes[link.From - 1].Position), b = Vertex(model.Nodes[link.To - 1].Position); if (a >= 0 && b >= 0) Join(a, b); }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        { Add("Error", "INVALID_RIGID_CONNECTION", "Model", e.Message); }
        int parts = Enumerable.Range(0, vertices.Count).Select(Find).Distinct().Count();
        if (parts > 1) Add("Warning", "DISCONNECTED", "Model", $"{parts} geometric connected parts. Intersections without shared endpoints/plate vertices are not connections.");
        structuralCount = vertices.Count;
        foreach (var node in model.Nodes)
        {
            if (!node.Position.IsValid) { Add("Error", "INVALID_NODE", node.NodeId, "Invalid coordinate."); continue; }
            Vertex(node.Position);
            if (!touchesElement) Add("Warning", "ISOLATED_NODE", node.NodeId, "Node does not coincide with an element vertex within tolerance.");
            try { NodeCoordinateExport.Frame(node); }
            catch (ArgumentException e) { Add("Error", "CONFLICTING_BEARING_FRAME", node.NodeId, e.Message); }
            foreach (var k in node.Attributes.OfType<NodeStiffnessAttributeModel>())
                if (new[] { k.Value.Tx, k.Value.Ty, k.Value.Tz, k.Value.Rx, k.Value.Ry, k.Value.Rz }.Any(v => !double.IsFinite(v) || v < 0))
                    Add("Error", "INVALID_BEARING_STIFFNESS", node.NodeId, "Nodal spring stiffness must be finite and nonnegative.");
        }
        if (!model.Elements.SelectMany(e => e.Attributes).Any(a => a is NodeRestraintAttributeModel or NodeStiffnessAttributeModel or BeamSupportAttributeModel or PlateSupportAttributeModel))
            Add("Warning", "NO_SUPPORT", "Model", "No restraint or elastic support attribute. Stability must be checked in Straus.");
        foreach (var e in model.Elements)
            foreach (var result in e.Results)
                if (result.LoadCase == null || string.IsNullOrWhiteSpace(result.LoadCase.Name) || !double.IsFinite(result.StepNum))
                    Add("Error", "INVALID_RESULT", "Model", "A stored result has no case identity or a non-finite step.");
        if (model.Plates.Any(p => p.PlateProperty?.PropertyType != PlatePropertyType.LoadPatch) &&
            model.Beams.Any(b => b.BeamProperty?.Section?.CompositePreview != null))
            Add("Warning", "POSSIBLE_DOUBLE_CONCRETE", "Model", "Structural plates coexist with composite/homogenized beam properties. Check whether slab stiffness is represented twice.");
        if (expectedGeometry != null)
        {
            var assigned = model.Beams.SelectMany(b => b.Attributes.OfType<BridgeMemberAttributeModel>()).Select(a => a.Value)
                .Where(v => v != null && !string.IsNullOrWhiteSpace(v.PhysicalId) && double.IsFinite(v.StationFrom) && double.IsFinite(v.StationTo)).ToArray();
            var byPhysical = assigned.ToLookup(a => a.PhysicalId);
            var coverage = new Dictionary<string, List<(double Start, double End)>>(StringComparer.Ordinal);
            foreach (var group in byPhysical)
            {
                var intervals = new List<(double Start, double End)>();
                foreach (var p in group.Where(a => a.Family == BridgeMemberFamily.Girder).OrderBy(a => a.StationFrom))
                {
                    if (intervals.Count > 0 && p.StationFrom <= intervals[^1].End + tolerance)
                        intervals[^1] = (intervals[^1].Start, Math.Max(intervals[^1].End, p.StationTo));
                    else intervals.Add((p.StationFrom, p.StationTo));
                }
                coverage[group.Key] = intervals;
            }
            foreach (var m in expectedGeometry.Members)
            {
                if (m.Family == BridgeMemberFamily.Girder)
                {
                    if (!coverage.TryGetValue(m.PhysicalId, out var intervals) ||
                        !intervals.Any(p => p.Start <= m.StationFrom + tolerance && p.End >= m.StationTo - tolerance))
                        Add("Error", "UNASSIGNED_BRIDGE_MEMBER", m.Id, "Expected longitudinal geometry has a gap in assigned FEM coverage.");
                }
                else if (!byPhysical[m.PhysicalId].Any(a => a.GeometryId == m.Id || a.GeometryId?.StartsWith(m.Id + "/P", StringComparison.Ordinal) == true))
                    Add("Error", "UNASSIGNED_BRIDGE_MEMBER", m.Id, "Expected geometric member has no assigned FEM element.");
            }
        }
        if (phaseModels != null)
        {
            var original = model.Beams.Where(b => !string.IsNullOrWhiteSpace(b.BeamId)).GroupBy(b => b.BeamId)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
            foreach (var phase in phaseModels)
            {
                if (phase == null) { Add("Error", "EMPTY_PHASE_MODEL", "Phase", "Null model."); continue; }
                if (phase.Units != model.Units) Add("Error", "PHASE_UNITS", phase.Name, "Phase units differ from the reference model.");
                foreach (var b in phase.Beams)
                    if (string.IsNullOrWhiteSpace(b.BeamId) || !original.TryGetValue(b.BeamId, out var reference) || b.PointFrom.DistanceTo(reference.PointFrom) > tolerance ||
                        b.PointTo.DistanceTo(reference.PointTo) > tolerance || Math.Abs(b.AngleDeg - reference.AngleDeg) > 1e-9 || (b.Offset - reference.Offset).Length > tolerance)
                        Add("Error", "PHASE_GEOMETRY_MISMATCH", phase.Name + "/" + b.BeamId, "Missing/reversed/moved beam or changed local axes/offset. Property changes are allowed.");
            }
        }
        var availableGroups = model.Elements.SelectMany(e => e.Groups).SelectMany(GroupModel.GetBranch).Select(g => g.FullName).ToHashSet(StringComparer.Ordinal);
        var availableCases = model.LoadCases.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var stage in model.StagedConstruction)
        {
            if (stage.Groups.Any(g => !availableGroups.Contains(g.FullName))) Add("Error", "UNKNOWN_STAGE_GROUP", stage.Name, "Stage references a group absent from the model.");
            if (stage.LoadCombinations.Any(c => c.Values.Any(v => !availableCases.Contains(v.Key.Name) || !double.IsFinite(v.Value))))
                Add("Error", "INVALID_STAGE_INCREMENT", stage.Name, "Stage increment references an unknown load case or non-finite factor.");
        }
        return issues.ToArray();
    }
}
