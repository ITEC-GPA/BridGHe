using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

[Serializable]
public sealed record BridgeSectionRule
{
    public int Family { get; init; }
    public int Group { get; init; }
    public int Segment { get; init; }
    public double Start { get; init; }
    public double End { get; init; }
    public BeamPropertyModel Property { get; init; }
    public Vector2d ScaleFrom { get; init; } = new(1, 1);
    public Vector2d ScaleTo { get; init; } = new(1, 1);
    public void Validate()
    {
        if (!Enum.IsDefined(typeof(BridgeMemberFamily), Family) || Group < 0 || Segment < 0 ||
            !double.IsFinite(Start) || !double.IsFinite(End) || Start < 0 || End <= Start || Property?.IsValid != true)
            throw new ArgumentException("Section schedule requires a valid family/property, nonnegative selectors and 0 <= Start < End.");
        if (new[] { ScaleFrom.X, ScaleFrom.Y, ScaleTo.X, ScaleTo.Y }.Any(x => !double.IsFinite(x) || x <= 0))
            throw new ArgumentException("Section scale factors must be finite and positive.");
        if ((ScaleFrom != new Vector2d(1, 1) || ScaleTo != new Vector2d(1, 1)) &&
            Property.SectionType is SectionModel.SectionTypes.Generic or SectionModel.SectionTypes.GenericShapes)
            throw new ArgumentException("Taper scales require a dimensional Straus section, not generic/homogenized properties.");
        if (Family != 0 && ScaleFrom != ScaleTo)
            throw new ArgumentException("Station-based variable taper is supported on longitudinal girders only.");
    }
    public bool Selects(BridgeGeometryMember member) => (int)member.Family == Family &&
        (Group == 0 || Group == member.Group) && (Segment == 0 || Segment == member.Segment);
    public Vector2d Scale(double station)
    {
        double t = Math.Clamp((station - Start) / (End - Start), 0, 1);
        return new Vector2d(ScaleFrom.X + t * (ScaleTo.X - ScaleFrom.X), ScaleFrom.Y + t * (ScaleTo.Y - ScaleFrom.Y));
    }
    public override string ToString() => $"Section schedule: {Property?.Id}, family {Family}, group {Group}, segment {Segment}, {Start:G7}..{End:G7} mm";
}

public sealed record BridgeAssignment(BeamModel[] Beams, string[] Unassigned, string[] Diagnostics);

public static class BridgeMemberAssignment
{
    public static BridgeAssignment Assign(SimpleBridgeGeometry geometry, IReadOnlyDictionary<int, BeamPropertyModel> defaults,
        IReadOnlyList<BridgeSectionRule> schedule)
    {
        BridgeGeometryTools.Validate(geometry);
        defaults ??= new Dictionary<int, BeamPropertyModel>(); schedule ??= Array.Empty<BridgeSectionRule>();
        foreach (var entry in defaults)
            if ((entry.Key != -1 && !Enum.IsDefined(typeof(BridgeMemberFamily), entry.Key)) || entry.Value?.IsValid != true)
                throw new ArgumentException("Default properties require valid family codes (-1 = all) and valid beam properties.");
        foreach (var rule in schedule)
        {
            if (rule == null) throw new ArgumentException("Empty section schedule entry.");
            rule.Validate();
            if (rule.End > geometry.Stations[^1] + 1e-6) throw new ArgumentException("Section schedule extends beyond Bridge Geometry.");
        }
        // Clone each property once, retaining normal sharing within the output but never mutating inputs.
        var properties = defaults.Values.Concat(schedule.Select(r => r.Property)).Distinct()
            .ToDictionary(p => p, ModelArchive.Clone);
        var beams = new List<BeamModel>(); var missing = new List<string>(); var used = new HashSet<BridgeSectionRule>();
        foreach (var member in geometry.Members)
        {
            var applicable = schedule.Where(r => r.Selects(member)).ToArray();
            var cuts = new SortedSet<double> { member.StationFrom, member.StationTo };
            if (member.Family == BridgeMemberFamily.Girder)
                foreach (var rule in applicable)
                {
                    if (rule.Start > member.StationFrom + 1e-6 && rule.Start < member.StationTo - 1e-6) cuts.Add(rule.Start);
                    if (rule.End > member.StationFrom + 1e-6 && rule.End < member.StationTo - 1e-6) cuts.Add(rule.End);
                }
            var stations = cuts.ToArray();
            // Transverse members may have identical or reversed reference stations (skew supports).
            int parts = member.Family == BridgeMemberFamily.Girder ? stations.Length - 1 : 1;
            for (int part = 0; part < parts; part++)
            {
                double a = member.Family == BridgeMemberFamily.Girder ? stations[part] : member.StationFrom;
                double b = member.Family == BridgeMemberFamily.Girder ? stations[part + 1] : member.StationTo;
                double mid = (a + b) / 2;
                var matches = applicable.Where(r => mid >= r.Start - 1e-7 && (mid < r.End - 1e-7 ||
                    Math.Abs(mid - geometry.Stations[^1]) < 1e-7 && Math.Abs(r.End - mid) < 1e-7)).ToArray();
                if (matches.Length > 1) throw new ArgumentException($"Overlapping section schedule on {member.Id} at {mid:G9} mm.");
                var rule = matches.SingleOrDefault();
                var property = rule?.Property ?? defaults.GetValueOrDefault((int)member.Family) ?? defaults.GetValueOrDefault(-1);
                string id = parts == 1 ? member.Id : $"{member.Id}/P{part + 1}";
                if (property == null) { missing.Add(id); continue; }
                if (rule != null) used.Add(rule);
                double t0 = member.Family == BridgeMemberFamily.Girder ? (a - member.StationFrom) / (member.StationTo - member.StationFrom) : 0;
                double t1 = member.Family == BridgeMemberFamily.Girder ? (b - member.StationFrom) / (member.StationTo - member.StationFrom) : 1;
                string solverId = (beams.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var beam = new BeamModel(solverId, member.Axis.PointAt(t0), member.Axis.PointAt(t1), properties[property]) { BeamId = solverId };
                var familyGroup = new GroupModel(member.Family.ToString());
                var memberGroup = new GroupModel($"Group{member.Group}", familyGroup);
                beam.Groups.Add(new GroupModel($"Segment{member.Segment}", memberGroup));
                if (member.Family == BridgeMemberFamily.Girder)
                {
                    beam.Attributes.Add(new SuperElementAttributeModel($"G{member.Group}"));
                }
                var frame = BridgeGeometryTools.FrameAt(geometry, mid);
                var desiredUp = frame.YAxis;
                var direction = beam.ToVector(); direction.Unitize();
                if (Math.Abs(direction * desiredUp) > .9999) desiredUp = frame.XAxis;
                beam.GetLocalAxes(false, out _, out var oldUp, out var axis, out _);
                var up = desiredUp - (desiredUp * axis) * axis; up.Unitize();
                beam.AngleDeg += Math.Atan2(up * Vector3d.CrossProduct(axis, oldUp), up * oldUp) * 180 / Math.PI;
                beam.Attributes.Add(new BridgeMemberAttributeModel(new(member.PhysicalId, member.Family, member.Group,
                    member.Segment, member.Span, a, b, up, id)));
                if (rule != null)
                {
                    var sf = rule.Scale(a); var st = rule.Scale(b);
                    if (sf != new Vector2d(1, 1) || st != new Vector2d(1, 1))
                        beam.Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData
                        { NFromX = sf.X, NFromY = sf.Y, NToX = st.X, NToY = st.Y }));
                }
                beams.Add(beam);
            }
        }
        return new(beams.ToArray(), missing.ToArray(), new[]
        {
            $"{beams.Count} beam elements, {missing.Count} unassigned, {schedule.Count - used.Count} unused schedule entries.",
            "N-mm properties required. Local section y follows projected deck normal; vertical posts use deck transverse direction.",
            "Each beam has one hierarchical Straus group (family/group/physical segment) and an integer export ID; geometric IDs are retained in metadata.",
            "Section changes split existing geometric chords. Fabrication segment IDs remain independent of this FEM subdivision.",
            "Scale interpolation tapers the dimensional section about its centre; it is not interpolation of arbitrary section properties."
        });
    }
}
