using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

/// <summary>
/// R6.2 (lane B): the GH outputs of Bridge Rigid Links, Bridge Connection Beams, Bridge Beam Offsets and Bridge Connections with their GPC
/// drafts built on GPC (identity of the conversion of the FEM output until R6.5), in the order of <c>BridgeConnections.Create</c>: per line, the
/// rigid pair (node A at the start with its rigid connection to the end, node B at the end) or one connection beam.
/// </summary>
internal static class GpcConnections
{
    internal static List<IGH_Goo> Elements(IReadOnlyList<ElementModel> elements, IReadOnlyList<Line> lines, bool rigid, GH_BeamProperty property)
    {
        var goos = elements.Select(e => e is NodeModel n ? (IGH_Goo)new GH_Node(n) : new GH_Beam((BeamModel)e)).ToList();
        for (int e = 0; e < goos.Count; e++)
        {
            int i = rigid ? e / 2 : e; var line = lines[i];
            if (goos[e] is GH_Node node)
            {
                bool first = e % 2 == 0;
                GpcBeamInputs.Attach(node, id => GpcBridgeGeometryBuilders.RigidNode(first ? line.From : line.To, $"Rigid{i + 1}{(first ? "A" : "B")}", first ? line.To : (Point3d?)null, id));
            }
            else GpcBeamInputs.Attach((GH_Beam)goos[e], id => GpcBridgeGeometryBuilders.ConnectionBeam(line.From, line.To, property.Gpc, $"Connection{i + 1}", id));
        }
        return goos;
    }

    /// <summary>Bridge Beam Offsets: the copies with the local offset set (Beam Override of the offset only).</summary>
    internal static List<GH_Beam> Offsets(IReadOnlyList<BeamModel> copies, IReadOnlyList<GH_Beam> sources, Vector3d offset)
    {
        var goos = copies.Select(b => new GH_Beam(b)).ToList();
        for (int i = 0; i < goos.Count; i++)
        {
            var source = sources[i];
            GpcBeamInputs.Attach(goos[i], id => GpcBeamBuilders.Override(source.Gpc, null, null, null, (offset.X, offset.Y), id));
        }
        return goos;
    }
}
