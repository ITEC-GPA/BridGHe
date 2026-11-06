using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// R6.2, lane B: the rule of Beam Filter on the GPC drafts of the beams (<c>GH_Beam.Gpc</c>), the same as on the former values: a beam is
    /// selected by its first Element ID (<see cref="LegacyElementRecords.FirstLabel"/>, the BridGHe ID), by one of its groups (full names of the
    /// records), by the name of its property, or by its start or end point within the tolerance, in this order.
    /// Declared limits (none in the examples): a former property without a name (the GPC property takes its ID), a group with an empty name
    /// (the full names join differently).
    /// </summary>
    internal static class GpcBeamFilter
    {
        /// <summary>The selection of every beam (true: filtered), or null when the draft of a beam is refused by the reader (the partition of the
        /// FEM values stays the output, as before R6.2).</summary>
        internal static bool[] Select(IReadOnlyList<GH_Beam> beams, IReadOnlyList<string> ids, IReadOnlyList<Rhino.Geometry.Point3d> starts, IReadOnlyList<Rhino.Geometry.Point3d> ends,
            IReadOnlyList<string> properties, IReadOnlyList<GH_Group> groups, double tolerance)
        {
            var names = groups.Select(g => Common.Gpc.Builders.GpcBeamBuilders.FullName(GpcBeamInputs.GroupChain(g.Value))).ToArray();
            var drafts = new LegacyBeamDraft[beams.Count];
            try { for (int i = 0; i < beams.Count; i++) drafts[i] = beams[i].Gpc; }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException) { return null; }
            return drafts.Select(d =>
            {
                string label = d.Records.FirstLabel;
                if (label != null && ids.Any(id => label.Equals(id))) return true;
                if (names.Any(n => d.Records.Groups.Contains(n, StringComparer.Ordinal))) return true;
                if (properties.Any(p => p == d.Beam.BeamProperty?.Name)) return true;
                var s = d.Beam.StartPoint; var e = d.Beam.EndPoint;
                if (starts.Any(p => new Rhino.Geometry.Point3d(s.X, s.Y, s.Z).DistanceTo(p) < tolerance)) return true;
                return ends.Any(p => new Rhino.Geometry.Point3d(e.X, e.Y, e.Z).DistanceTo(p) < tolerance);
            }).ToArray();
        }
    }
}
