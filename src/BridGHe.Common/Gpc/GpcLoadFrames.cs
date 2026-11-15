using System;
using GPC.Geometry;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// R6.5 P1 lane 2b (coordinator, 8/11): the coordinate system of the load components (Beam Distributed Load, Beam Point Load, Beam Point
    /// Moment, Plate Pressure) on GPC: the GPC form of the load frame, a <see cref="CoordinateSystem"/> (the frame the GPC loads take), named as
    /// the former system. Beam load components distinguish the element's Local selection from an absolute frame: an absolute frame must
    /// be Global by <see cref="IsGlobal(CoordinateSystem)"/>; other frames are refused. Legacy archive readers retain their declared interpretation.
    /// </summary>
    public static class GpcLoadFrames
    {
        /// <summary>P2CE (13/11/2026, phase 2b): the global system of the GH datum (its default and the «Global» menu): the frame of the former
        /// global system (CoordinateSystemModel.Global: origin 0, points (1, 0, 0) and (0, 1, 0), name «Global»), a new frame each time.</summary>
        public static CoordinateSystem Global() => new CoordinateSystem(new Point3d(0, 0, 0), new Point3d(1, 0, 0), new Point3d(0, 1, 0), "Global");


        /// <summary>
        /// The one rule of a global load system (R65-C2-01, user decision U6 of 12/11), declared here and used everywhere: by the load
        /// components on the frame of their system and by the reader of the 0.3.0 files on the frame of the former points
        /// (<see cref="IsGlobal(Point3d, Point3d, Point3d)"/>). A frame is global when its origin is 0 and its axes 1 and 2 are X and Y, exactly.
        /// The frame is normalised and orthogonalised by its constructor, so a former system with the points (2, 0, 0) and (0, 3, 0) is global
        /// (before P2 the reader and a system with a FEM value took the points themselves: (1, 0, 0) and (0, 1, 0) only).
        /// </summary>
        public static bool IsGlobal(CoordinateSystem frame) =>
            frame != null && frame.Origin.X == 0 && frame.Origin.Y == 0 && frame.Origin.Z == 0
            && frame.V1.X == 1 && frame.V1.Y == 0 && frame.V1.Z == 0 && frame.V2.X == 0 && frame.V2.Y == 1 && frame.V2.Z == 0;

        /// <summary>The rule of <see cref="IsGlobal(CoordinateSystem)"/> on the points of a former system (origin, a point on axis 1, a point in
        /// the plane 1-2): the frame they make; points that make no frame (coincident or aligned) are not the global system.</summary>
        public static bool IsGlobal(Point3d origin, Point3d point1, Point3d point2)
        {
            CoordinateSystem frame;
            try { frame = new CoordinateSystem(origin, point1, point2, ""); }
            catch (Exception x) when (x is ArgumentException || x is InvalidOperationException || x is ArithmeticException) { return false; }
            return IsGlobal(frame);
        }
    }
}
