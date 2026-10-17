using System;
using BridGHe.Common.Models;
using GPC.Geometry;
using GPC.Model.Results;

namespace BridGHe.Common.Checks
{
    /// <summary>Converts Straus diagram moments (planes) to gPC moments (axes).
    /// BeamForceResultModel preserves SF1, SF2, BM1, BM2 in Vx, Vy, Mx, My.
    /// Positive BM2 compresses the top of a section whose y axis points upward.
    /// See Strand7 R3 Help: ResultsInterpretationBeamInternalForceConvention.</summary>
    public static class BridgeForceConvention
    {
        public static ResultBeamForces FromStraus(BeamForceResultModel force, CoordinateSystem reference)
        {
            if (force == null) throw new ArgumentNullException(nameof(force));
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            // The diagram values use the section's positive axes (1 across, 2 up).
            // 'reference' is the checker's destination frame, often (-x,-y), not
            // the frame in which Straus reported the values. Rotate the values as
            // well as the frame, otherwise ToGlobalCoordinateSystem flips them twice.
            return new ResultBeamForces(force.AxialForce, force.Vx, force.Vy, force.Torque,
                force.My, -force.Mx, CoordinateSystem.Global, name: force.LoadCase?.Name ?? "")
                .ToCoordinateSystem(reference);
        }
    }
}
