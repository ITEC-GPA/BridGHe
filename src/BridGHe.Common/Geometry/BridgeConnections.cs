using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public static class BridgeConnections
{
    /// <summary>P2K (P1): the checks of <see cref="Create"/> without building FEM elements (the GPC path of the components builds the drafts):
    /// nonzero lines, an explicit Beam or Truss property for deformable members (<paramref name="beamOrTruss"/>, read by the caller from the
    /// GPC datum), no duplicate line; the number of elements, two per rigid line (nodes A and B), one per deformable line.</summary>
    public static int Check(IReadOnlyList<Line> lines, bool rigid, bool beamOrTruss)
    {
        if (lines == null || lines.Count == 0 || lines.Any(l => !l.IsValid || l.Length <= SimpleBridgeBuilder.JoinTolerance))
            throw new ArgumentException("Provide nonzero connection lines in mm.");
        if (!rigid && !beamOrTruss)
            throw new ArgumentException("Deformable connections require an explicit valid Beam or Truss property in N-mm.");
        var unique = new HashSet<(Point3d, Point3d)>();
        foreach (var line in lines)
        {
            if (unique.Contains((line.From, line.To)) || unique.Contains((line.To, line.From)))
                throw new ArgumentException("Duplicate connection line.");
            unique.Add((line.From, line.To));
        }
        return rigid ? 2 * lines.Count : lines.Count;
    }

    /// <summary>P2K (P1): the check of <see cref="Offset"/> without FEM beams (the GPC path gives the offset on the drafts).</summary>
    public static void CheckOffset(int beams, bool anyNull, Vector2d offset)
    {
        if (beams == 0 || !offset.IsValid || anyNull)
            throw new ArgumentException("Provide beams and a finite local x/y offset in mm.");
    }

    /// <param name="beamOrTrussOnGpc">R6.5 P1 lane 2a: for a property given by its GPC datum alone (no FEM value), whether it is a valid Beam
    /// or Truss property, read from the datum by the caller; the members then carry no FEM property (only the GPC path reads them).</param>
    public static ElementModel[] Create(IReadOnlyList<Line> lines, bool rigid, BeamPropertyModel property = null, bool? beamOrTrussOnGpc = null)
    {
        if (lines == null || lines.Count == 0 || lines.Any(l => !l.IsValid || l.Length <= SimpleBridgeBuilder.JoinTolerance))
            throw new ArgumentException("Provide nonzero connection lines in mm.");
        if (!rigid && !(beamOrTrussOnGpc ?? (property?.IsValid == true && property.PropertyType is BeamPropertyType.Beam or BeamPropertyType.Truss)))
            throw new ArgumentException("Deformable connections require an explicit valid Beam or Truss property in N-mm.");
        var unique = new HashSet<(Point3d, Point3d)>();
        var result = new List<ElementModel>();
        var copiedProperty = rigid || beamOrTrussOnGpc != null ? null : ModelArchive.Clone(property);
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
