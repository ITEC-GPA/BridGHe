using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public enum BridgeBearingKind { FixedTranslation, GuidedLongitudinal, Multidirectional, Elastic }
public sealed record BridgeBearingLayout(Plane[] Frames, string[] Labels)
{
    /// <summary>R6.2 lane A: the GPC draft of every node, built on GPC with the node builders from the same data (node ID, point, group, frame,
    /// restraint or stiffness), with a provisional identity of its label (P2CE, 13/11/2026, phase 1: no FEM node; the component gives the drafts
    /// the identity by content).</summary>
    public BridGHe.Common.Gpc.Legacy.LegacyNodeDraft[] Drafts { get; init; }
}

public static class BridgeBearings
{
    /// <summary>P2K (P1), P2CE (phase 1: always): the nodes by their GPC drafts alone (the path of the component), no FEM node.</summary>
    public static BridgeBearingLayout Create(SimpleBridgeGeometry geometry, IReadOnlyList<int> supportIds, IReadOnlyList<int> girderIds,
        IReadOnlyList<int> kinds, double guideAngle, Vector3d translationStiffness, Vector3d rotationStiffness, Plane? orientation = null)
    {
        BridgeGeometryTools.Validate(geometry);
        supportIds ??= Array.Empty<int>(); girderIds ??= Array.Empty<int>();
        if (supportIds.Any(s => !geometry.Supports.Any(p => p.Support == s)) ||
            girderIds.Any(g => !geometry.Supports.Any(p => p.Girder == g)))
            throw new ArgumentException("Support/girder selector does not exist in Bridge Geometry.");
        var points = geometry.Supports.Where(s => (supportIds.Count == 0 || supportIds.Contains(s.Support)) &&
            (girderIds.Count == 0 || girderIds.Contains(s.Girder))).OrderBy(s => s.Support).ThenBy(s => s.Girder).ToArray();
        if (points.Length == 0 || kinds == null || kinds.Count != 1 && kinds.Count != points.Length ||
            kinds.Any(k => !Enum.IsDefined(typeof(BridgeBearingKind), k)))
            throw new ArgumentException("Provide one bearing type or one per selected point (support, then girder order); types 0..3.");
        if (!double.IsFinite(guideAngle) || !translationStiffness.IsValid || !rotationStiffness.IsValid ||
            new[] { translationStiffness.X, translationStiffness.Y, translationStiffness.Z, rotationStiffness.X, rotationStiffness.Y, rotationStiffness.Z }.Any(k => k < 0))
            throw new ArgumentException("Guide angle must be finite and all stiffnesses finite/nonnegative.");
        if (kinds.Contains(3) && translationStiffness.Length == 0 && rotationStiffness.Length == 0)
            throw new ArgumentException("An elastic bearing requires at least one nonzero stiffness.");
        if (!kinds.Contains(3) && (translationStiffness.Length != 0 || rotationStiffness.Length != 0))
            throw new ArgumentException("Nonzero stiffness inputs require an Elastic bearing; they are not silently ignored.");
        if (orientation.HasValue && !orientation.Value.IsValid) throw new ArgumentException("Invalid bearing orientation.");
        var frames = new List<Plane>(); var labels = new List<string>(); var drafts = new List<BridGHe.Common.Gpc.Legacy.LegacyNodeDraft>();
        for (int i = 0; i < points.Length; i++)
        {
            var point = points[i]; var frame = orientation ?? Plane.WorldXY;
            if (!orientation.HasValue)
            {
                var tangent = BridgeGeometryTools.FrameAt(geometry, point.Station).ZAxis;
                tangent.Z = 0;
                if (!tangent.Unitize()) throw new ArgumentException("Cannot define horizontal longitudinal bearing direction.");
                frame = new Plane(point.Point, tangent, Vector3d.CrossProduct(Vector3d.ZAxis, tangent));
            }
            frame.Origin = point.Point; frame.Rotate(guideAngle * Math.PI / 180, frame.ZAxis, frame.Origin);
            var kind = (BridgeBearingKind)kinds[kinds.Count == 1 ? 0 : i];
            string label = $"S{point.Support}/G{point.Girder}/{kind}";
            frames.Add(frame); labels.Add(label);
            // R6.2 lane A: the same node on GPC (the reader of the former node: groups, then the frame, then the restraint or the stiffness).
            var identity = BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace, "BridgeBearings:" + label);
            var draft = BridGHe.Common.Gpc.Builders.GpcNodeBuilders.Create(point.Point.X, point.Point.Y, point.Point.Z, label,
                new[] { new[] { $"Bearing/S{point.Support}/G{point.Girder}" } }, identity);
            draft = BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithFrame(draft, frame.Origin.X, frame.Origin.Y, frame.Origin.Z,
                frame.XAxis.X, frame.XAxis.Y, frame.XAxis.Z, frame.YAxis.X, frame.YAxis.Y, frame.YAxis.Z, identity);
            draft = kind == BridgeBearingKind.Elastic
                ? BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithStiffness(draft, translationStiffness.X, translationStiffness.Y, translationStiffness.Z,
                    rotationStiffness.X, rotationStiffness.Y, rotationStiffness.Z, identity)
                : BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithRestraint(draft, kind == BridgeBearingKind.FixedTranslation,
                    kind is BridgeBearingKind.FixedTranslation or BridgeBearingKind.GuidedLongitudinal, true, false, false, false, identity);
            drafts.Add(draft);
        }
        return new(frames.ToArray(), labels.ToArray()) { Drafts = drafts.ToArray() };
    }
}
