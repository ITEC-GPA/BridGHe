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
    /// <summary>R6.5 P1 lane 2a: the property given by its GPC datum alone (<see cref="Property"/> null): Bridge Slab Grillage on a material
    /// given by its GPC datum alone. Assignments with such a rule give their beams by their plans alone (<see cref="BridgeAssignment.Plans"/>).</summary>
    [field: NonSerialized] public Gpc.Legacy.LegacyBeamProperty GpcProperty { get; init; }
    /// <summary>P2CE (13/11/2026, phase 2b, coordinator decision 1): the ID of the FEM property of a rule of a 0.3.0 file, read by value with the
    /// rule (<c>GpcLegacyPayloads.Design</c>), for the text of the rule (<see cref="ToString"/>), which showed it; null otherwise.</summary>
    public string LegacyPropertyId { get; init; }
    public Vector2d ScaleFrom { get; init; } = new(1, 1);
    public Vector2d ScaleTo { get; init; } = new(1, 1);
    public void Validate()
    {
        if (!Enum.IsDefined(typeof(BridgeMemberFamily), Family) || Group < 0 || Segment < 0 ||
            !double.IsFinite(Start) || !double.IsFinite(End) || Start < 0 || End <= Start || !(Property?.IsValid == true || Property == null && GpcProperty?.Property != null))
            throw new ArgumentException("Section schedule requires a valid family/property, nonnegative selectors and 0 <= Start < End.");
        if (new[] { ScaleFrom.X, ScaleFrom.Y, ScaleTo.X, ScaleTo.Y }.Any(x => !double.IsFinite(x) || x <= 0))
            throw new ArgumentException("Section scale factors must be finite and positive.");
        if ((ScaleFrom != new Vector2d(1, 1) || ScaleTo != new Vector2d(1, 1)) && (Property != null
            ? Property.SectionType is SectionModel.SectionTypes.Generic or SectionModel.SectionTypes.GenericShapes
            : GpcProperty.Data?.Section?.Kind is Gpc.BridgeSectionKind.Generic or Gpc.BridgeSectionKind.GenericShapes))
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
    // R6.5 P1 lane 2b: a property given by its GPC datum alone by its name.
    public override string ToString() => $"Section schedule: {Property?.Id ?? LegacyPropertyId ?? GpcProperty?.Property?.Name}, family {Family}, group {Group}, segment {Segment}, {Start:G7}..{End:G7} mm";
}

public sealed record BridgeAssignment(BeamModel[] Beams, string[] Unassigned, string[] Diagnostics)
{
    /// <summary>R6.2 (lane B): what the GPC drafts of <see cref="Beams"/> are built from, one per beam in the same order
    /// (<c>GpcBridgeGeometryBuilders.Assigned</c>).</summary>
    public BridgeMemberPlan[] Plans { get; init; } = Array.Empty<BridgeMemberPlan>();
}

/// <summary>
/// R6.2 (lane B): one beam of Bridge Member Assignment without FEM values: its ends, its property (the GPC datum of the FEM property it was
/// assigned, <see cref="BridgeGpcProperties"/>), its export ID (the Element ID of the beam; <see cref="Label"/> false: removed, as Bridge Slab
/// Grillage does), its group (family, group, segment), super element (girders), bridge member (stations <see cref="StationFrom"/>,
/// <see cref="StationTo"/>, geometric ID), the frame of the deck at its mid station (the desired section y) and the taper scales (none: null).
/// The BridGHe angle and the section up vector are computed on GPC data (<see cref="BridgeBeamFrames"/>) with the principal angle of the GPC
/// property, as the FEM path computes them on the former beam.
/// </summary>
public sealed record BridgeMemberPlan(Point3d From, Point3d To, Func<Gpc.Legacy.LegacyBeamProperty> Property, string SolverId, bool Label, BridgeMemberFamily Family,
    int Group, int Segment, int Span, string PhysicalId, double StationFrom, double StationTo, string GeometryId, Vector3d DeckY, Vector3d DeckX, Vector2d? ScaleFrom, Vector2d? ScaleTo);

/// <summary>
/// R6.2 (lane B): the GPC datum of the FEM beam properties the bridge generators are given (keyed by the FEM instance: Bridge Section
/// Schedule, Bridge Family Property, Bridge Segment Sections and the default properties of Bridge Member Assignment attach the datum of their
/// GH input, or the one they build), until those records carry GPC properties (R6.5). A FEM property without one (a record read from a file)
/// gives the conversion of its FEM value, as a <c>GH_BeamProperty</c> without an attached datum does.
/// </summary>
public static class BridgeGpcProperties
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<BeamPropertyModel, Gpc.Legacy.LegacyBeamProperty> Table = new();

    public static BeamPropertyModel Attach(BeamPropertyModel property, Gpc.Legacy.LegacyBeamProperty gpc)
    {
        if (property != null && gpc != null) Table.AddOrUpdate(property, gpc);
        return property;
    }

    public static Gpc.Legacy.LegacyBeamProperty Of(BeamPropertyModel property) =>
        property == null ? throw new ArgumentNullException(nameof(property))
        : Table.TryGetValue(property, out var gpc) ? gpc : Gpc.GpcModels.BeamProperty(property, Gpc.GpcBeamDrafts.DraftUnits);

}

/// <summary>
/// R6.2 (lane B): the local axes of a beam without the FEM beam (<c>BeamModel.GetLocalAxes</c>, the same Rhino operations one for one): axis 3
/// along the beam, axis 1 = Z × axis 3 (horizontal) or, for a vertical beam, axis 2 = axis 3 × Y; the frame rotated by the BridGHe angle
/// (principal) and back by the principal angle of the section (section axes).
/// </summary>
public static class BridgeBeamFrames
{
    public static void LocalAxes(Point3d from, Point3d to, double angleDeg, double principalAngleRad, bool getPrincipal, out Vector3d axes1, out Vector3d axes2, out Vector3d axes3)
    {
        double r = angleDeg / 180 * Math.PI;
        if (!getPrincipal) r -= principalAngleRad;
        axes3 = to - from;
        axes3.Unitize();
        Vector3d v = to - from; v.Unitize();
        if (!(Vector3d.CrossProduct(Vector3d.ZAxis, v).Length < 1.0E-12))
        {
            axes1 = Vector3d.CrossProduct(Vector3d.ZAxis, axes3);
            axes1.Unitize();
            axes2 = Vector3d.CrossProduct(axes3, axes1);
            axes2.Unitize();
        }
        else
        {
            axes2 = Vector3d.CrossProduct(axes3, Vector3d.YAxis);
            axes2.Unitize();
            axes1 = Vector3d.CrossProduct(axes2, axes3);
            axes1.Unitize();
        }
        Plane pl = new Plane(from, axes1, axes2);
        pl.Rotate(r, pl.Normal);
        axes1 = pl.XAxis;
        axes2 = pl.YAxis;
    }

    /// <summary>Bridge Member Assignment: the BridGHe angle (degrees, from a beam at angle 0) and the unit section up vector of a beam whose
    /// section y follows <paramref name="deckY"/> (or <paramref name="deckX"/> when the beam is nearly along it), as the FEM path computes them.</summary>
    public static (double AngleDeg, Vector3d Up) Orientation(Point3d from, Point3d to, double principalAngleRad, Vector3d deckY, Vector3d deckX)
    {
        var desiredUp = deckY;
        var direction = to - from; direction.Unitize();
        if (Math.Abs(direction * desiredUp) > .9999) desiredUp = deckX;
        LocalAxes(from, to, 0, principalAngleRad, false, out _, out var oldUp, out var axis);
        var up = desiredUp - (desiredUp * axis) * axis; up.Unitize();
        double angle = 0;
        angle += Math.Atan2(up * Vector3d.CrossProduct(axis, oldUp), up * oldUp) * 180 / Math.PI;
        return (angle, up);
    }
}

public static class BridgeMemberAssignment
{
    /// <param name="gpcDefaults">R6.5 P1 lane 2a: default properties given by their GPC datum alone (no FEM value), paired with families as
    /// <paramref name="defaults"/>. With one of them, or a rule with a GPC property alone, the assignment is on GPC only (rule of passo 75): no
    /// FEM beams, only the plans (<see cref="BridgeAssignment.Plans"/>), with the same checks, messages and diagnostics.</param>
    // P2CE phase 2: delete with Models the FEM defaults and BridgeAssignment.Beams (product callers give null; callers with FEM defaults: GpcOnlyReplay
    // of lane P2CC and its helper BridgeConstructionTests.Model).
    public static BridgeAssignment Assign(SimpleBridgeGeometry geometry, IReadOnlyDictionary<int, BeamPropertyModel> defaults,
        IReadOnlyList<BridgeSectionRule> schedule, IReadOnlyDictionary<int, Gpc.Legacy.LegacyBeamProperty> gpcDefaults = null)
    {
        BridgeGeometryTools.Validate(geometry);
        defaults ??= new Dictionary<int, BeamPropertyModel>(); schedule ??= Array.Empty<BridgeSectionRule>();
        gpcDefaults ??= new Dictionary<int, Gpc.Legacy.LegacyBeamProperty>();
        bool gpcOnly = gpcDefaults.Count > 0 || schedule.Any(r => r != null && r.Property == null && r.GpcProperty != null);
        foreach (var entry in gpcDefaults)
            if ((entry.Key != -1 && !Enum.IsDefined(typeof(BridgeMemberFamily), entry.Key)) || entry.Value?.Property == null)
                throw new ArgumentException("Default properties require valid family codes (-1 = all) and valid beam properties.");
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
        var properties = gpcOnly ? new Dictionary<BeamPropertyModel, BeamPropertyModel>() : defaults.Values.Concat(schedule.Select(r => r.Property)).Distinct()
            .ToDictionary(p => p, ModelArchive.Clone);
        var beams = new List<BeamModel>(); var missing = new List<string>(); var used = new HashSet<BridgeSectionRule>();
        var plans = new List<BridgeMemberPlan>();
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
                // R6.5 P1 lane 2a: a property is the FEM one or, on GPC only, its GPC datum alone (the same precedence).
                object property = rule != null ? (object)rule.Property ?? rule.GpcProperty
                    : (object)defaults.GetValueOrDefault((int)member.Family) ?? gpcDefaults.GetValueOrDefault((int)member.Family) ??
                    (member.Family == BridgeMemberFamily.DeckTransverse ? null : (object)defaults.GetValueOrDefault(-1) ?? gpcDefaults.GetValueOrDefault(-1));
                string id = parts == 1 ? member.Id : $"{member.Id}/P{part + 1}";
                if (property == null) { missing.Add(id); continue; }
                if (rule != null) used.Add(rule);
                double t0 = member.Family == BridgeMemberFamily.Girder ? (a - member.StationFrom) / (member.StationTo - member.StationFrom) : 0;
                double t1 = member.Family == BridgeMemberFamily.Girder ? (b - member.StationFrom) / (member.StationTo - member.StationFrom) : 1;
                string solverId = (plans.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var frame = BridgeGeometryTools.FrameAt(geometry, mid);
                Vector2d? scaleFrom = null, scaleTo = null;
                if (rule != null)
                {
                    var sf = rule.Scale(a); var st = rule.Scale(b);
                    if (sf != new Vector2d(1, 1) || st != new Vector2d(1, 1)) { scaleFrom = sf; scaleTo = st; }
                }
                Func<Gpc.Legacy.LegacyBeamProperty> planProperty = property is BeamPropertyModel femProperty ? () => BridgeGpcProperties.Of(femProperty)
                    : () => (Gpc.Legacy.LegacyBeamProperty)property;
                // R6.5 P1 lane 2a: on GPC only the plan alone (the FEM beam reads its FEM property).
                if (gpcOnly)
                {
                    plans.Add(new BridgeMemberPlan(member.Axis.PointAt(t0), member.Axis.PointAt(t1), planProperty, solverId, true, member.Family,
                        member.Group, member.Segment, member.Span, member.PhysicalId, a, b, id, frame.YAxis, frame.XAxis, scaleFrom, scaleTo));
                    continue;
                }
                var beam = new BeamModel(solverId, member.Axis.PointAt(t0), member.Axis.PointAt(t1), properties[(BeamPropertyModel)property]) { BeamId = solverId };
                var familyGroup = new GroupModel(member.Family.ToString());
                var memberGroup = new GroupModel($"Group{member.Group}", familyGroup);
                beam.Groups.Add(new GroupModel($"Segment{member.Segment}", memberGroup));
                if (member.Family == BridgeMemberFamily.Girder)
                {
                    beam.Attributes.Add(new SuperElementAttributeModel($"G{member.Group}"));
                }
                var desiredUp = frame.YAxis;
                var direction = beam.ToVector(); direction.Unitize();
                if (Math.Abs(direction * desiredUp) > .9999) desiredUp = frame.XAxis;
                beam.GetLocalAxes(false, out _, out var oldUp, out var axis, out _);
                var up = desiredUp - (desiredUp * axis) * axis; up.Unitize();
                beam.AngleDeg += Math.Atan2(up * Vector3d.CrossProduct(axis, oldUp), up * oldUp) * 180 / Math.PI;
                beam.Attributes.Add(new BridgeMemberAttributeModel(new(member.PhysicalId, member.Family, member.Group,
                    member.Segment, member.Span, a, b, up, id)));
                if (scaleFrom != null)
                    beam.Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData
                    { NFromX = scaleFrom.Value.X, NFromY = scaleFrom.Value.Y, NToX = scaleTo.Value.X, NToY = scaleTo.Value.Y }));
                beams.Add(beam);
                // R6.2 (lane B): the same beam without FEM values, for its GPC draft (the property: the GPC datum of the one assigned).
                plans.Add(new BridgeMemberPlan(member.Axis.PointAt(t0), member.Axis.PointAt(t1), planProperty, solverId, true, member.Family,
                    member.Group, member.Segment, member.Span, member.PhysicalId, a, b, id, frame.YAxis, frame.XAxis, scaleFrom, scaleTo));
            }
        }
        return new(beams.ToArray(), missing.ToArray(), new[]
        {
            $"{plans.Count} beam elements, {missing.Count} unassigned, {schedule.Count - used.Count} unused schedule entries.",
            "N-mm properties required. Local section y follows projected deck normal; vertical posts use deck transverse direction.",
            "Each beam has one hierarchical Straus group (family/group/physical segment) and an integer export ID; geometric IDs are retained in metadata.",
            "Section changes split existing geometric chords. Fabrication segment IDs remain independent of this FEM subdivision.",
            "Scale interpolation tapers the dimensional section about its centre; it is not interpolation of arbitrary section properties."
        }) { Plans = plans.ToArray() };
    }
}
