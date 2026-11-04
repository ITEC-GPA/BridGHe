using System;
using System.Linq;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Straus;

public static class NodeCoordinateExport
{
    public static Plane? Frame(NodeModel node)
    {
        var frames = node.Attributes.OfType<NodeCoordinateAttributeModel>().Select(a => a.Value).Distinct().ToArray();
        if (frames.Length > 1 || frames.Any(p => !p.IsValid))
            throw new ArgumentException($"Node {node.NodeId}: conflicting/invalid restraint coordinate systems.");
        return frames.Length == 0 ? null : frames[0];
    }
    public static double[] Coordinates(Plane frame)
    {
        if (!frame.IsValid) throw new ArgumentException("Invalid Cartesian frame.");
        var a = frame.Origin; var b = a + frame.XAxis; var c = a + frame.YAxis;
        return new[] { a.X, a.Y, a.Z, b.X, b.Y, b.Z, c.X, c.Y, c.Z, 0 };
    }
}
