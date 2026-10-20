using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public static class BridgeConnections
{
    public static ElementModel[] Create(IReadOnlyList<Line> lines, bool rigid, BeamPropertyModel property = null)
    {
        if (lines == null || lines.Count == 0 || lines.Any(l => !l.IsValid || l.Length <= SimpleBridgeBuilder.JoinTolerance))
            throw new ArgumentException("Provide nonzero connection lines in mm.");
        if (!rigid && (property?.IsValid != true || property.PropertyType is not (BeamPropertyType.Beam or BeamPropertyType.Truss)))
            throw new ArgumentException("Deformable connections require an explicit valid Beam or Truss property in N-mm.");
        var unique = new HashSet<(Point3d, Point3d)>();
        var result = new List<ElementModel>();
        var copiedProperty = rigid ? null : ModelArchive.Clone(property);
        foreach (var (line, i) in lines.Select((l, i) => (l, i)))
        {
            if (unique.Contains((line.From, line.To)) || unique.Contains((line.To, line.From)))
                throw new ArgumentException("Duplicate connection line.");
            unique.Add((line.From, line.To));
            if (rigid)
            {
                var from = new NodeModel($"Rigid{i + 1}A", line.From);
                from.Attributes.Add(new RigidConnectionAttributeModel(line.To));
                result.Add(from); result.Add(new NodeModel($"Rigid{i + 1}B", line.To));
            }
            else
            {
                // Export numbers are assigned by Build Model; keep semantic identity in its group.
                var beam = new BeamModel(line.From, line.To, copiedProperty) { BeamId = $"Connection{i + 1}" };
                beam.Groups.Add(new GroupModel("DeformableConnections")); result.Add(beam);
            }
        }
        return result.ToArray();
    }

    public static BeamModel[] Offset(IReadOnlyList<BeamModel> beams, Vector2d offset)
    {
        if (beams == null || beams.Count == 0 || !offset.IsValid || beams.Any(b => b == null))
            throw new ArgumentException("Provide beams and a finite local x/y offset in mm.");
        return beams.Select(b => { var clone = new BeamModel(b) { Offset = offset }; clone.Results.Clear(); return clone; }).ToArray();
    }
}
