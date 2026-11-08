using System;
using BridGHe.Common.Models;
using GPC.Geometry;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// R6.5 P1 lane 2b (coordinator, 8/11): the coordinate system of the load components (Beam Distributed Load, Beam Point Load, Beam Point
    /// Moment, Plate Pressure) on GPC: the GPC form of the load frame, a <see cref="CoordinateSystem"/> (the frame the GPC loads take), named as
    /// the former system. The loads use only whether a system is given (none: the principal or local axes) and, for a distributed load,
    /// whether it is the global one (any other is refused, as the former exporter ignored it: <c>LegacyNonGlobalLoadCoordinateSystemWasIgnored</c>).
    /// </summary>
    public static class GpcLoadFrames
    {
        /// <summary>The GPC frame of a former coordinate system: origin, a point on axis 1 and a point in the plane 1-2 (the former
        /// <c>Origin</c>, <c>Point1</c>, <c>Point2</c>), with its name. The global system (origin 0, (1, 0, 0), (0, 1, 0)) gives the global
        /// axes exactly.</summary>
        public static CoordinateSystem Of(CoordinateSystemModel cs)
        {
            if (cs == null) throw new ArgumentNullException(nameof(cs));
            static Point3d P(Rhino.Geometry.Point3d p) => new Point3d(p.X, p.Y, p.Z);
            return new CoordinateSystem(P(cs.Origin), P(cs.Point1), P(cs.Point2), cs.Name ?? "");
        }

        /// <summary>Whether a frame is the global one: origin 0 and axes 1, 2 along X, Y, exactly. For every system the plug-in makes (the
        /// global system of the parameter) the rule of the reader on the former points (<c>LegacyModelImport.IsGlobal</c>).</summary>
        public static bool IsGlobal(CoordinateSystem frame) =>
            frame != null && frame.Origin.X == 0 && frame.Origin.Y == 0 && frame.Origin.Z == 0
            && frame.V1.X == 1 && frame.V1.Y == 0 && frame.V1.Z == 0 && frame.V2.X == 0 && frame.V2.Y == 1 && frame.V2.Z == 0;
    }
}
