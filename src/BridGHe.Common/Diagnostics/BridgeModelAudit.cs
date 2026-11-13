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
    // R6.5 P1 lane 3a part 2: what the audit reads of a model, in model length units: the same checks and numbers on the FEM value or on the GPC input.
    private sealed record AuditBeam(string Id, bool Valid, double Length, Point3d From, Point3d To, double Angle, Vector2d Offset, BridgeMemberMetadata[] Members, Func<Vector3d> Up, bool Composite);
    private sealed record AuditPlate(string Id, bool Valid, Point3d[] Points, bool LoadPatch);
    private sealed record AuditNode(string Id, Point3d Position, Action CheckFrame, int BadStiffness);
    private sealed record AuditStage(string Name, bool UnknownGroup, bool BadIncrement);
    private sealed record AuditModel(AuditBeam[] Beams, AuditNode[] Nodes, AuditPlate[] Plates, string[] Cases, Func<IEnumerable<(Point3d From, Point3d To)>> RigidLinks, bool HasSupport, AuditStage[] Stages, Func<int> InvalidResults);
    private sealed record AuditPhase(string Name, object Units, AuditBeam[] Beams);



    private const string StageGroupWithoutElements = "Stage group without elements (not created natively by the former exporter)";

    /// <summary>The beam of a GPC input: BridGHe ID (name), geometry in model length units (the GPC model is in mm, <see cref="Gpc.BridgeUnits.FromMm"/>), BridGHe
    /// angle, the first bridge member (only one exists on GPC data: the reader keeps the first, so <c>MULTIPLE_BRIDGE_IDENTITIES</c> cannot happen), the «homogenised» mark from the recipe
    /// of the section of its property.</summary>
    private static AuditBeam Beam(Gpc.GpcModels.Conversion input, GPC.Model.Elements.BeamElement b)
    {
        var u = input.Report.Units;
        Point3d P(GPC.Geometry.Point3d p) => new Point3d(u.FromMm(p.X), u.FromMm(p.Y), u.FromMm(p.Z));
        var o = b.Assignments.OffsetI;
        var m = input.Bridge?.Member(b.Guid);
        var members = m == null ? Array.Empty<BridgeMemberMetadata>() : new[] { new BridgeMemberMetadata(m.PhysicalId, Enum.Parse<BridgeMemberFamily>(m.Family.ToString()), m.Group, m.Segment, m.Span,
            u.FromMm(m.StationFromMm), u.FromMm(m.StationToMm), new Vector3d(m.SectionUp.X, m.SectionUp.Y, m.SectionUp.Z), m.GeometryId) };
        return new AuditBeam(b.Name, b.BeamProperty != null && b.Length > 0, u.FromMm(b.Length), P(b.StartPoint), P(b.EndPoint), input.Report.BeamAngles.TryGetValue(b.Id, out double angle) ? angle : double.NaN,
            o == null ? Vector2d.Zero : new Vector2d(u.FromMm(o.X), u.FromMm(o.Y)), members,
            () => { var v = b.Assignments.SectionAxes.V2; return new Vector3d(v.X, v.Y, v.Z); },
            b.BeamProperty != null && input.Report.BeamProperties.TryGetValue(b.BeamProperty.Name, out var data) && data.Section?.CompositePreview == true);
    }

    private static AuditModel Of(Gpc.GpcModels.Conversion input)
    {
        var u = input.Report.Units; var model = input.Model;
        Point3d P(GPC.Geometry.Point3d p) => new Point3d(u.FromMm(p.X), u.FromMm(p.Y), u.FromMm(p.Z));
        bool Registered(string name) => input.Report.CaseOrigins.TryGetValue(name, out var o) && (o & Gpc.Legacy.LegacyCaseOrigins.Registered) != 0;
        var byGuid = model.LoadCases.Values.ToDictionary(c => c.Guid);
        // 12/11 (user decision): a stage increment is valid on any case of the model (registered, of element loads, combinations, stages or moving
        // paths), with a finite factor; 0.3.0 accepted only the registered cases, which Build Model never registers (see the FEM path above).
        var stages = model.Stages.Values.OrderBy(s => s.Id).Select(s => new AuditStage(s.Name, false, (s.AnalysisState?.Increments ?? Array.Empty<GPC.Model.Stages.StageLoadIncrement>())
            .Any(i => (i.LoadFactors ?? Array.Empty<GPC.Model.Stages.StageCaseFactor>()).Any(f => !double.IsFinite(f.Factor) || !byGuid.ContainsKey(f.CaseGuid))))).ToList();
        // A stage group without elements is not created on GPC data: the reader counts it, without the name of the stage.
        if (input.Report.NotExported.ContainsKey(StageGroupWithoutElements)) stages.Add(new AuditStage("Stage", true, false));
        return new AuditModel(model.BeamElements.Values.OrderBy(b => b.Id).Select(b => Beam(input, b)).ToArray(),
            model.NodesElements.Values.OrderBy(n => n.Id).Select(n => new AuditNode(n.Name, P(n.Position), () => { }, // a conflicting frame is refused by the reader, with the same message
                n.Assignments.Restrains.Count(r => r.Restrain.Restrains.Any(d => d.HasStiffness && (!double.IsFinite(d.Stiffness) || d.Stiffness < 0))))).ToArray(),
            model.AreaElements.Values.OrderBy(p => p.Id).Select(p => new AuditPlate(p.Name, p.PlateProperty != null && p.Nodes.Count >= 3, p.Nodes.Select(n => P(n.Position)).ToArray(),
                p.PlateProperty is GPC.Model.ElementProperties.ElasticPlateProperty { Formulation: GPC.Model.ElementProperties.ElasticPlateFormulation.LoadPatch })).ToArray(),
            model.LoadCases.Values.Where(c => Registered(c.Name)).Select(c => c.Name).ToArray(),
            () => model.GetCostrains().OfType<GPC.Model.Constraints.RigidLink>().SelectMany(l => l.EndNodes.Select(e => (P(l.StartNode.Position), P(e.Position)))),
            model.NodesElements.Values.Any(n => n.Assignments.Restrains.Any(r => r.SourceRecord != Gpc.Legacy.LegacyModelImport.FrameRecord && r.Restrain.Restrains.Any(d => d.IsRestrained || d.HasStiffness)))
                || model.BeamElements.Values.Any(b => b.Assignments.Physical?.Supports.Count > 0) || model.AreaElements.Values.Any(p => p.Assignments.Physical?.Supports.Count > 0),
            stages.ToArray(), () => 0);
    }


    /// <summary>R6.5 P1 lane 3a part 2: the audit of a model given by its GPC input (and of phases given by theirs): the checks of the FEM audit on the GPC data, in model length units. On GPC data
    /// <c>MULTIPLE_BRIDGE_IDENTITIES</c>, <c>CONFLICTING_BEARING_FRAME</c>, <c>INVALID_RIGID_CONNECTION</c> and a duplicate case name cannot happen (the reader refuses them or keeps one);
    /// the group of a stage that has no elements is reported once, for the model (the reader counts it without the stage).</summary>
    public static ModelIssue[] Inspect(Gpc.GpcModels.Conversion input, double tolerance, SimpleBridgeGeometry expectedGeometry = null,
        IReadOnlyList<Gpc.GpcModels.Conversion> phases = null, GPC.Model.Models.Model analysed = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Inspect(Of(input), tolerance, expectedGeometry, phases?.Select(p => p == null ? null : new AuditPhase(p.Model.Name, p.Report.Units.Units, p.Model.BeamElements.Values.OrderBy(b => b.Id).Select(b => Beam(p, b)).ToArray())).ToArray(), analysed, input.Report.Units.Units);
    }

    private static ModelIssue[] Inspect(AuditModel model, double tolerance, SimpleBridgeGeometry expectedGeometry, IReadOnlyList<AuditPhase> phaseModels, GPC.Model.Models.Model analysed, object units)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentException("Tolerance must be finite and positive, in model length units.");
        var issues = new List<ModelIssue>();
        void Add(string severity, string code, string id, string text) => issues.Add(new(severity, code, id, text));
        void Ids(IEnumerable<string> values, string kind)
        {
            var list = values.ToArray();
            if (list.Any(string.IsNullOrWhiteSpace)) Add("Error", "EMPTY_ID", kind, "One or more identifiers are empty.");
            foreach (var g in list.GroupBy(x => x).Where(g => g.Count() > 1)) Add("Error", "DUPLICATE_ID", kind + ":" + g.Key, $"{g.Count()} repeated identifiers.");
        }
        Ids(model.Beams.Select(b => b.Id), "Beam"); Ids(model.Nodes.Select(n => n.Id), "Node");
        Ids(model.Plates.Select(p => p.Id), "Plate"); Ids(model.Cases, "Case");
        if (model.Beams.Length + model.Plates.Length == 0) Add("Error", "EMPTY_MODEL", "Model", "No beam or plate elements.");
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
            if (!beam.Valid || !double.IsFinite(beam.Length) || beam.Length <= tolerance)
                Add("Error", "INVALID_BEAM", beam.Id, "Invalid geometry/property or length at/below tolerance.");
            int a = Vertex(beam.From), b = Vertex(beam.To); if (a >= 0 && b >= 0) Join(a, b);
            if (!double.IsFinite(beam.Angle) || !beam.Offset.IsValid)
                Add("Error", "INVALID_ORIENTATION", beam.Id, "Non-finite section angle/offset.");
            var metadata = beam.Members;
            if (metadata.Length > 1) Add("Error", "MULTIPLE_BRIDGE_IDENTITIES", beam.Id, "More than one physical member identity.");
            if (metadata.Length == 1)
            {
                var d = metadata[0];
                if (d == null || string.IsNullOrWhiteSpace(d.PhysicalId) || !double.IsFinite(d.StationFrom) || !double.IsFinite(d.StationTo) ||
                    d.Family == BridgeMemberFamily.Girder && d.StationTo <= d.StationFrom)
                    Add("Error", "INVALID_BRIDGE_IDENTITY", beam.Id, "Invalid physical member station range.");
                else if (beam.Valid && d.SectionUp.IsValid)
                {
                    var up = beam.Up();
                    if (up * d.SectionUp < 1 - 1e-6) Add("Warning", "SECTION_AXIS_CHANGED", beam.Id, "Current section orientation differs from the stored bridge assignment frame.");
                }
            }
        }
        foreach (var plate in model.Plates)
        {
            if (!plate.Valid) Add("Error", "INVALID_PLATE", plate.Id, "Invalid plate geometry or property.");
            var points = plate.Points.Select(Vertex).Where(i => i >= 0).ToArray();
            foreach (int i in points.Skip(1)) Join(points[0], i);
        }
        try
        {
            foreach (var link in model.RigidLinks())
            { int a = Vertex(link.From), b = Vertex(link.To); if (a >= 0 && b >= 0) Join(a, b); }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        { Add("Error", "INVALID_RIGID_CONNECTION", "Model", e.Message); }
        int parts = Enumerable.Range(0, vertices.Count).Select(Find).Distinct().Count();
        if (parts > 1) Add("Warning", "DISCONNECTED", "Model", $"{parts} geometric connected parts. Intersections without shared endpoints/plate vertices are not connections.");
        structuralCount = vertices.Count;
        foreach (var node in model.Nodes)
        {
            if (!node.Position.IsValid) { Add("Error", "INVALID_NODE", node.Id, "Invalid coordinate."); continue; }
            Vertex(node.Position);
            if (!touchesElement) Add("Warning", "ISOLATED_NODE", node.Id, "Node does not coincide with an element vertex within tolerance.");
            try { node.CheckFrame(); }
            catch (ArgumentException e) { Add("Error", "CONFLICTING_BEARING_FRAME", node.Id, e.Message); }
            for (int k = 0; k < node.BadStiffness; k++) Add("Error", "INVALID_BEARING_STIFFNESS", node.Id, "Nodal spring stiffness must be finite and nonnegative.");
        }
        if (!model.HasSupport)
            Add("Warning", "NO_SUPPORT", "Model", "No restraint or elastic support attribute. Stability must be checked in Straus.");
        var catalog = Gpc.GpcResultCatalog.Of(analysed);
        if (catalog != null)
        { if (Gpc.GpcResultCatalog.HasInvalidResult(analysed, catalog)) Add("Error", "INVALID_RESULT", "Model", "A stored result has no case identity or a non-finite step."); }
        else
            for (int k = model.InvalidResults(); k > 0; k--) Add("Error", "INVALID_RESULT", "Model", "A stored result has no case identity or a non-finite step.");
        if (model.Plates.Any(p => !p.LoadPatch) && model.Beams.Any(b => b.Composite))
            Add("Warning", "POSSIBLE_DOUBLE_CONCRETE", "Model", "Structural plates coexist with composite/homogenized beam properties. Check whether slab stiffness is represented twice.");
        if (expectedGeometry != null)
        {
            var assigned = model.Beams.SelectMany(b => b.Members)
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
            var original = model.Beams.Where(b => !string.IsNullOrWhiteSpace(b.Id)).GroupBy(b => b.Id)
                .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single());
            foreach (var phase in phaseModels)
            {
                if (phase == null) { Add("Error", "EMPTY_PHASE_MODEL", "Phase", "Null model."); continue; }
                if (!Equals(phase.Units, units)) Add("Error", "PHASE_UNITS", phase.Name, "Phase units differ from the reference model.");
                foreach (var b in phase.Beams)
                    if (string.IsNullOrWhiteSpace(b.Id) || !original.TryGetValue(b.Id, out var reference) || b.From.DistanceTo(reference.From) > tolerance ||
                        b.To.DistanceTo(reference.To) > tolerance || Math.Abs(b.Angle - reference.Angle) > 1e-9 || (b.Offset - reference.Offset).Length > tolerance)
                        Add("Error", "PHASE_GEOMETRY_MISMATCH", phase.Name + "/" + b.Id, "Missing/reversed/moved beam or changed local axes/offset. Property changes are allowed.");
            }
        }
        foreach (var stage in model.Stages)
        {
            if (stage.UnknownGroup) Add("Error", "UNKNOWN_STAGE_GROUP", stage.Name, "Stage references a group absent from the model.");
            if (stage.BadIncrement) Add("Error", "INVALID_STAGE_INCREMENT", stage.Name, "Stage increment references an unknown load case or non-finite factor.");
        }
        return issues.ToArray();
    }
}
