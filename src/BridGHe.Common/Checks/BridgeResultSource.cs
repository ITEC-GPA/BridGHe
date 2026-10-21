using System.Globalization;

namespace BridGHe.Common.Checks;

/// <summary>Result provenance only; StationMm is measured from the FEM beam start, not along the alignment.</summary>
public sealed record BridgeResultSource(string BeamId, double StationMm, string Combination, string SuperElement = null)
{
    public string LocationLabel => (string.IsNullOrWhiteSpace(SuperElement) ? "" : SuperElement + " / ")
        + "Beam " + BeamId + " / s=" + StationMm.ToString("G12", CultureInfo.InvariantCulture) + " mm";
    public string Label => LocationLabel + " / " + Combination;
}
