using System;
using System.Collections.Generic;
using GPC.Geometry;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Loads.Assignments;

namespace BridGHe.Common.Gpc
{
    /// <summary>Distribution of a BridGHe distributed beam action (former BeamDistributedLoadModel.LoadSchema), same order as
    /// the Straus7 codes the former exporter wrote (dlConstant … dlTrapezoidal).</summary>
    public enum BridgeLoadSchema { Uniform, Keystone, Triangular, TriangularEnd1_Keystone, Keystone_TriangularEnd2, TriangularEnds_Keystone }

    /// <summary>Beam actions as GPC <see cref="BeamLoadAssignment"/> pieces, N-mm. Every piece is linear between two stations;
    /// the BridGHe schemas are decomposed with the Straus7 meaning of PA, PB, P1, P2, a, b (positions normalized to the beam):
    /// Uniform PA on [a, 1-b]; Keystone PA at a to PB at 1-b; Triangular P1 at 0, PA at a, P2 at 1; Triangular + Keystone
    /// P1 at 0, PA at a, PB at 1-b; Keystone + Triangular PA at a, PB at 1-b, P2 at 1; Trapezoidal P1, PA, PB, P2.
    /// The eccentricity is the beam offset: BridGHe actions act on the offset reference line, as the native actions did.</summary>
    public static class GpcBeamLoads
    {
        public static IEnumerable<(double Station, double Value)> Points(BridgeLoadSchema schema, double pa, double pb, double p1, double p2, double a, double b)
        {
            double end = 1 - b;
            if (!(a >= 0) || !(end <= 1) || a > end) throw new ArgumentException("BeamLoadPositionsOutsideBeam");
            switch (schema)
            {
                case BridgeLoadSchema.Uniform: return new[] { (a, pa), (end, pa) };
                case BridgeLoadSchema.Keystone: return new[] { (a, pa), (end, pb) };
                case BridgeLoadSchema.Triangular: return new[] { (0.0, p1), (a, pa), (1.0, p2) };
                case BridgeLoadSchema.TriangularEnd1_Keystone: return new[] { (0.0, p1), (a, pa), (end, pb) };
                case BridgeLoadSchema.Keystone_TriangularEnd2: return new[] { (a, pa), (end, pb), (1.0, p2) };
                case BridgeLoadSchema.TriangularEnds_Keystone: return new[] { (0.0, p1), (a, pa), (end, pb), (1.0, p2) };
                default: throw new ArgumentOutOfRangeException(nameof(schema));
            }
        }

        /// <param name="direction">Unit direction of the components (global or in <paramref name="axes"/>).</param>
        /// <param name="axes">Frame of the components: null for global.</param>
        /// <param name="projectionNormal">Global unit normal of the projection plane for loads per projected length, else null.</param>
        /// <param name="scale">Unit conversion of the intensities (force/length or moment/length to N-mm).</param>
        public static List<BeamLoadAssignment> Distributed(LoadCaseBase loadCase, Point3d start, Point3d end, BridgeLoadSchema schema,
            double pa, double pb, double p1, double p2, double a, double b, Vector3d direction, bool moment, CoordinateSystem axes,
            Vector3d projectionNormal, Vector3d eccentricity, double scale, string source)
        {
            var result = new List<BeamLoadAssignment>(); var points = new List<(double Station, double Value)>(Points(schema, pa, pb, p1, p2, a, b));
            var line = new Line3d(start, end);
            for (int k = 1; k < points.Count; k++)
            {
                var (s0, v0) = points[k - 1]; var (s1, v1) = points[k];
                if (!(s1 > s0) || v0 == 0 && v1 == 0) continue;
                result.Add(new BeamLoadAssignment
                {
                    Start = s0, End = s1,
                    StartIntensity = Intensity(direction, moment, v0 * scale, line, loadCase, axes),
                    EndIntensity = Intensity(direction, moment, v1 * scale, line, loadCase, axes),
                    Eccentricity = eccentricity ?? new Vector3d(0, 0, 0),
                    EccentricityCoordinateSystem = CoordinateSystem.Global,
                    LengthConvention = projectionNormal == null ? BeamLoadLengthConvention.ActualLength : BeamLoadLengthConvention.ProjectedLength,
                    ProjectionPlaneNormal = projectionNormal,
                    StationDomain = "NodeToNode",
                    OriginalAssignmentId = source, SourceRecord = source + "/segment " + k,
                });
            }
            return result;
        }

        /// <summary>The frame of the load components: the beam axes with the origin at the global origin. Before GPCModel df660262 (L9,
        /// decision 8 of 4/11) LineLoad.GetGlobalForces subtracted the frame origin from the rotated vectors, so a beam frame with its
        /// origin at the start node shifted the intensities; the library is now correct and this frame stays valid (directions only).
        /// The beam frames could be used directly (open, no change: the archived loads keep this frame).</summary>
        internal static CoordinateSystem ComponentFrame(CoordinateSystem axes) => axes == null ? null : new CoordinateSystem(new Point3d(0, 0, 0), axes.V1, axes.V2);

        private static LineLoad Intensity(Vector3d direction, bool moment, double value, Line3d line, LoadCaseBase loadCase, CoordinateSystem axes)
        {
            axes = ComponentFrame(axes);
            var v = direction * value; var zero = new Vector3d(0, 0, 0);
            return axes == null ? new LineLoad(moment ? zero : v, moment ? v : zero, line, loadCase)
                : new LineLoad(moment ? zero : v, moment ? v : zero, line, loadCase, axes);
        }

        /// <summary>Concentrated action at the normalized station <paramref name="station"/> of the node line.</summary>
        public static BeamLoadAssignment Concentrated(LoadCaseBase loadCase, Point3d start, Point3d end, double station, Vector3d value, bool moment,
            CoordinateSystem axes, Vector3d eccentricity, double scale, string source)
        {
            if (!(station >= 0 && station <= 1)) throw new ArgumentException("BeamPointLoadOutsideBeam");
            var point = new Point3d(start.X + (end.X - start.X) * station, start.Y + (end.Y - start.Y) * station, start.Z + (end.Z - start.Z) * station);
            var v = value * scale; var zero = new Vector3d(0, 0, 0); axes = ComponentFrame(axes);
            var load = axes == null ? new PointLoad(moment ? zero : v, moment ? v : zero, point, loadCase)
                : new PointLoad(moment ? zero : v, moment ? v : zero, point, loadCase, axes);
            return new BeamLoadAssignment
            {
                Start = station, End = station, Concentrated = load, Eccentricity = eccentricity ?? new Vector3d(0, 0, 0),
                EccentricityCoordinateSystem = CoordinateSystem.Global, LengthConvention = BeamLoadLengthConvention.ActualLength,
                StationDomain = "NodeToNode", OriginalAssignmentId = source, SourceRecord = source,
            };
        }
    }
}
