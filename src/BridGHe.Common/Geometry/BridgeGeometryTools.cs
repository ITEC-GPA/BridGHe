using System;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

/// <summary>Operations on the sampled geometric reference; distances are always mm.</summary>
public static class BridgeGeometryTools
{
    public static void Validate(SimpleBridgeGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Stations.Count < 2 || geometry.Frames.Count != geometry.Stations.Count ||
            geometry.Stations.Any(s => !double.IsFinite(s)) ||
            geometry.Stations.Zip(geometry.Stations.Skip(1)).Any(p => p.First >= p.Second) ||
            geometry.Frames.Any(f => !f.IsValid))
            throw new ArgumentException("Invalid Bridge Geometry station/frame grid.");
    }

    public static Plane FrameAt(SimpleBridgeGeometry geometry, double station)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (geometry.Stations.Count < 2 || geometry.Frames.Count != geometry.Stations.Count)
            throw new ArgumentException("Invalid Bridge Geometry station/frame grid.");
        if (!double.IsFinite(station) || station < geometry.Stations[0] - 1e-6 || station > geometry.Stations[^1] + 1e-6)
            throw new ArgumentException("Station is outside Bridge Geometry.");
        int index = geometry.Stations.BinarySearch(station);
        if (index >= 0) return geometry.Frames[index];
        index = Math.Clamp(~index - 1, 0, geometry.Stations.Count - 2);
        double t = Math.Clamp((station - geometry.Stations[index]) / (geometry.Stations[index + 1] - geometry.Stations[index]), 0, 1);
        var a = geometry.Frames[index]; var b = geometry.Frames[index + 1];
        return new Plane(a.Origin + t * (b.Origin - a.Origin), (1 - t) * a.XAxis + t * b.XAxis,
            (1 - t) * a.YAxis + t * b.YAxis);
    }

    public static Point3d GirderPoint(SimpleBridgeGeometry geometry, int girder, double station)
    {
        var matches = geometry.Members.Where(m => m.Family == BridgeMemberFamily.Girder && m.Group == girder &&
            station >= m.StationFrom - 1e-6 && station <= m.StationTo + 1e-6).ToArray();
        if (matches.Length == 0) throw new ArgumentException($"Girder {girder}: station {station:G8} is outside its geometry.");
        var member = matches[0];
        return member.Axis.PointAt(Math.Clamp((station - member.StationFrom) / (member.StationTo - member.StationFrom), 0, 1));
    }
}
