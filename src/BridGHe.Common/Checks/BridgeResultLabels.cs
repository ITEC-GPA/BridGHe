using System;

namespace BridGHe.Common.Checks;

public enum BridgeResultLabelFormat { Complete = 0, BeamStation = 1, Combination = 2, Phase = 3 }

public static class BridgeResultLabels
{
    /// <param name="position">Position in the caller's data tree, used only when FEM provenance is unavailable.</param>
    public static string Format(BridgeStressResult result, BridgeResultLabelFormat format, string position)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        return format switch
        {
            BridgeResultLabelFormat.Complete => result.Source?.Label ?? $"Result {position} / {result.Stage.Name}",
            BridgeResultLabelFormat.BeamStation => result.Source?.LocationLabel ?? $"Result {position} / beam and station unavailable",
            BridgeResultLabelFormat.Combination => result.Source?.Combination ?? $"Result {position} / combination unavailable",
            BridgeResultLabelFormat.Phase => result.Stage.Name,
            _ => throw new ArgumentOutOfRangeException(nameof(format), "Choose a label format from 0 to 3.")
        };
    }
}
