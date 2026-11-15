using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public static class BridgeConnections
{
    /// <summary>P2CE (13/11/2026, phase 1): the FEM Create and Offset deleted (no caller). P2K (P1): the checks of the former <c>Create</c> without building FEM elements (the GPC path of the components builds the drafts):
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

    /// <summary>P2K (P1): the check of the former <c>Offset</c> without FEM beams (the GPC path gives the offset on the drafts).</summary>
    public static void CheckOffset(int beams, bool anyNull, Vector2d offset)
    {
        if (beams == 0 || !offset.IsValid || anyNull)
            throw new ArgumentException("Provide beams and a finite local x/y offset in mm.");
    }


}
