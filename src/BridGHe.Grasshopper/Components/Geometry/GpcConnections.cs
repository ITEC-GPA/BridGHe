using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

/// <summary>
/// R6.2 (lane B): the GH outputs of Bridge Rigid Links, Bridge Connection Beams, Bridge Beam Offsets and Bridge Connections with their GPC
/// drafts built on GPC (identity of the conversion of the FEM output until R6.5), in the order of <c>BridgeConnections.Create</c>: per line, the
/// rigid pair (node A at the start with its rigid connection to the end, node B at the end) or one connection beam.
/// <para>R6.5 P1 lane 1b: a beam given by its draft alone
/// gets its offset on GPC (<see cref="GpcOnlyElements.NewBeam"/>, identity derived from the input draft).</para>
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

    /// <summary>Bridge Beam Offsets: the copies with the local offset set (Beam Override of the offset only). R6.5 P1 lane 1b: a beam given by
    /// its draft alone gets the offset on GPC (no FEM value); the checks of <c>BridgeConnections.Offset</c> first, with its message.</summary>
    internal static List<GH_Beam> Offsets(GH_Component component, IReadOnlyList<GH_Beam> sources, Vector3d offset)
    {
        if (sources.All(b => b?.Value != null))
        {
            var copies = BridgeConnections.Offset(sources.Select(b => b.Value).ToArray(), new(offset.X, offset.Y));
            var goos = copies.Select(b => new GH_Beam(b)).ToList();
            for (int i = 0; i < goos.Count; i++)
            {
                var source = sources[i];
                GpcBeamInputs.Attach(goos[i], id => GpcBeamBuilders.Override(source.Gpc, null, null, null, (offset.X, offset.Y), id));
            }
            return goos;
        }
        if (sources.Count == 0 || !new Vector2d(offset.X, offset.Y).IsValid || sources.Any(b => b == null))
            throw new ArgumentException("Provide beams and a finite local x/y offset in mm.");
        var outputs = new List<GH_Beam>();
        foreach (var source in sources)
        {
            if (source.Value != null)
            {
                var copy = new GH_Beam(BridgeConnections.Offset(new[] { source.Value }, new(offset.X, offset.Y))[0]);
                GpcBeamInputs.Attach(copy, id => GpcBeamBuilders.Override(source.Gpc, null, null, null, (offset.X, offset.Y), id));
                outputs.Add(copy);
                continue;
            }
            // A new beam, as the FEM path gives a new copy with its results cleared (no analysed element, a new GH GUID).
            var only = GpcOnlyElements.NewBeam(component, 0, source, id => GpcBeamBuilders.Override(source.Gpc, null, null, null, (offset.X, offset.Y), id), offset.X, offset.Y);
            if (only != null) outputs.Add(only);
        }
        return outputs;
    }
}
