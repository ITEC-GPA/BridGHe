using System;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// R6.1, frozen API for R6.2 (§1bis): the check of every GPC datum a component attaches to a GH type while the FEM value is still
    /// built beside it (until R6.5). A GH type calls <see cref="Attached"/> from its <c>AttachGpc</c> with the attached datum and the
    /// conversion of its FEM value with the same identity (<c>GpcBeamDrafts.FromFemm(value, guid)</c> and the others); the test host sets
    /// <see cref="Verify"/> (the Smoke host compares the canonical texts on every run of every gate and fails the run on a difference).
    /// Outside the tests <see cref="Verify"/> is null and nothing is converted.
    /// </summary>
    public static class GpcDraftCheck
    {
        /// <summary>The check: (GH type name, attached datum, conversion of the FEM value). Null: no check.</summary>
        public static Action<string, object, Func<object>> Verify { get; set; }

        public static void Attached(string type, object datum, Func<object> conversion)
        {
            var verify = Verify;
            if (verify != null && datum != null && conversion != null) verify(type, datum, conversion);
        }
    }

    /// <summary>L2C (lane 2c): the datum of a Plate Property parameter as the check compares it: the GPC property and the bending thickness of
    /// a membrane beside it (<c>GH_PlateProperty.GpcMembraneBending</c>; the GPC membrane has none).</summary>
    public sealed record GpcPlatePropertyDatum(GPC.Model.ElementProperties.PlateProperty Property, double MembraneBending);
}
