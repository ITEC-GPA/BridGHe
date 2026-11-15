using System;
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

        /// <summary>F6 (a)/(b): the same conversion of a merged sample (BridGHe convention: SF1, SF2, BM1, BM2) of the case <paramref name="caseName"/>.</summary>
        public static ResultBeamForces FromStraus(BeamForceSample force, string caseName, CoordinateSystem reference)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            return new ResultBeamForces(force.Axial, force.Shear1, force.Shear2, force.Torque,
                force.Bending2, -force.Bending1, CoordinateSystem.Global, name: caseName ?? "")
                .ToCoordinateSystem(reference);
        }
    }
}
