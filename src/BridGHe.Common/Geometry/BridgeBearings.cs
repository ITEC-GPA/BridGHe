using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public enum BridgeBearingKind { FixedTranslation, GuidedLongitudinal, Multidirectional, Elastic }
public sealed record BridgeBearingLayout(NodeModel[] Nodes, Plane[] Frames, string[] Labels);

public static class BridgeBearings
{
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
        var nodes = new List<NodeModel>(); var frames = new List<Plane>(); var labels = new List<string>();
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
            var node = new NodeModel(label, point.Point);
            node.Groups.Add(new GroupModel($"Bearing/S{point.Support}/G{point.Girder}"));
            node.Attributes.Add(new NodeCoordinateAttributeModel(frame));
            if (kind == BridgeBearingKind.Elastic)
                node.Attributes.Add(new NodeStiffnessAttributeModel(translationStiffness.X, translationStiffness.Y, translationStiffness.Z,
                    rotationStiffness.X, rotationStiffness.Y, rotationStiffness.Z));
            else
                node.Attributes.Add(new NodeRestraintAttributeModel(kind == BridgeBearingKind.FixedTranslation,
                    kind is BridgeBearingKind.FixedTranslation or BridgeBearingKind.GuidedLongitudinal, true, false, false, false));
            nodes.Add(node); frames.Add(frame); labels.Add(label);
        }
        return new(nodes.ToArray(), frames.ToArray(), labels.ToArray());
    }
}
