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
            return new ResultBeamForces(force.AxialForce, force.Vx, force.Vy, force.Torque,
                force.My, -force.Mx, reference, name: force.LoadCase?.Name ?? "");
        }
    }
}
