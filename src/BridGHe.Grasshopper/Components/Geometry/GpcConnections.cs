using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

/// <summary>
/// R6.2 (lane B): the GH outputs of Bridge Rigid Links, Bridge Connection Beams, Bridge Beam Offsets and Bridge Connections, in the order of
/// <c>BridgeConnections.Create</c>: per line, the rigid pair (node A at the start with its rigid connection to the end, node B at the end) or one
/// connection beam. R6.5 P1 lane 1b (P2K: always): by their drafts alone, identity by content (<see cref="GpcOnlyElements.ContentIdentity"/> of
/// the parameters of the builder); a beam gets its offset on GPC (<see cref="GpcOnlyElements.NewBeam"/>, identity derived from the input draft).
/// </summary>
internal static class GpcConnections
{
    /// <summary>P2K (P1): the elements of Bridge Rigid Links, Bridge Connection Beams and Bridge Connections, by their drafts alone: the checks
    /// of <c>BridgeConnections.Check</c> (lines, a Beam or Truss property read from its GPC datum, duplicates), then the drafts.</summary>
    internal static List<IGH_Goo> Elements(GH_Component component, IReadOnlyList<Line> lines, bool rigid, GH_BeamProperty property)
    {
        int count = BridgeConnections.Check(lines, rigid, !rigid && GpcOnlyProperties.BeamOrTruss(property));
        return GpcOnly(component, count, lines, rigid, property);
    }

    /// <summary>R6.5 P1 lane 1b: the same elements by their drafts alone, in the same order (the FEM elements validated the lines and the
    /// property), each with the identity by content of its builder parameters; a refused one gives an error and is left out.</summary>
    private static List<IGH_Goo> GpcOnly(GH_Component component, int count, IReadOnlyList<Line> lines, bool rigid, GH_BeamProperty property)
    {
        var goos = new List<IGH_Goo>();
        for (int e = 0; e < count; e++)
        {
            int i = rigid ? e / 2 : e; var line = lines[i];
            if (rigid)
            {
                bool first = e % 2 == 0;
                var position = first ? line.From : line.To; string id = $"Rigid{i + 1}{(first ? "A" : "B")}"; Point3d? target = first ? line.To : null;
                var draft = GpcOnlyElements.Generated(component, () => GpcBridgeGeometryBuilders.RigidNode(position, id, target,
                    GpcOnlyElements.ContentIdentity(component.ComponentGuid, 0, position, id, target)));
                if (draft != null) goos.Add(GH_Node.FromGpc(draft));
            }
            else
            {
                string id = $"Connection{i + 1}";
                var draft = GpcOnlyElements.Generated(component, () => GpcBridgeGeometryBuilders.ConnectionBeam(line.From, line.To, property.Gpc, id,
                    GpcOnlyElements.ContentIdentity(component.ComponentGuid, 0, line.From, line.To, GpcOnlyElements.Lazy(() => property.Gpc), id)));
                if (draft != null) goos.Add(GH_Beam.FromGpc(draft));
            }
        }
        return goos;
    }

    /// <summary>Bridge Beam Offsets: the copies with the local offset set (Beam Override of the offset only). R6.5 P1 lane 1b: a beam given by
    /// its draft alone gets the offset on GPC (no FEM value); the checks of <c>BridgeConnections.Offset</c> first, with its message.</summary>
    internal static List<GH_Beam> Offsets(GH_Component component, IReadOnlyList<GH_Beam> sources, Vector3d offset)
    {
        BridgeConnections.CheckOffset(sources.Count, sources.Any(b => b == null), new Vector2d(offset.X, offset.Y));
        var outputs = new List<GH_Beam>();
        foreach (var source in sources)
        {
            // A new beam, as the former path gave a new copy with its results cleared (no analysed element, a new GH GUID); P2K (P1): for every
            // beam, also one with a FEM value (its draft; the output has none).
            var only = GpcOnlyElements.NewBeam(component, 0, source, id => GpcBeamBuilders.Override(source.Gpc, null, null, null, (offset.X, offset.Y), id), offset.X, offset.Y);
            if (only != null) outputs.Add(only);
        }
        return outputs;
    }
}
