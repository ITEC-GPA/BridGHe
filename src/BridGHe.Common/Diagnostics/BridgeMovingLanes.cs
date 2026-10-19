using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.MovingLoads;
using Rhino.Geometry;

namespace BridGHe.Common.Diagnostics;

public static class BridgeMovingLanes
{
    public static MovingPath[] Create(MovingPath prototype, IReadOnlyList<double> offsets, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(prototype); prototype.Validate();
        if (prototype.Shape != 0) throw new ArgumentException("Parallel lanes currently require a straight prototype. Define curved paths individually with Moving Load Path.");
        if (offsets == null || names == null || offsets.Count == 0 || offsets.Count != names.Count || offsets.Any(v => !double.IsFinite(v)) ||
            names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Count || offsets.Distinct().Count() != offsets.Count)
            throw new ArgumentException("Supply distinct finite offsets and one unique non-empty case name for each path.");
        var c = prototype.Coordinates;
        var axis = new Vector3d(c[3] - c[0], c[4] - c[1], c[5] - c[2]); axis.Unitize();
        var lateral = new Vector3d(c[6] - c[0], c[7] - c[1], c[8] - c[2]);
        lateral -= (lateral * axis) * axis;
        if (!lateral.Unitize()) throw new ArgumentException("Prototype has an invalid lateral direction.");
        return offsets.Select((offset, i) =>
        {
            var copy = (MovingPath)prototype.Clone(); copy.LoadCaseName = names[i];
            for (int p = 0; p < 3; p++) { copy.Coordinates[p * 3] += offset * lateral.X; copy.Coordinates[p * 3 + 1] += offset * lateral.Y; copy.Coordinates[p * 3 + 2] += offset * lateral.Z; }
            copy.Validate(); return copy;
        }).ToArray();
    }
}
