#if GPC_PILOT
using System;

/// <summary>FEM path (removed with the FeMM models, R6.5): the frozen payloads of the tests written once by the FEM writer
/// (<c>BRIDGHE_LEGACY_FIXTURES=write:&lt;new folder&gt;</c>, <see cref="LegacyPayloadFixtures.Write"/>) and, while the FeMM classes exist, checked
/// against them: every payload equal to what the FEM writer gives now, byte for byte, and the former local axes of the units model
/// (<see cref="LegacyPayloadFixtures.Parity"/>).</summary>
internal static partial class GpcLegacyImportTests
{
    static partial void FemCases()
    {
        string setting = Environment.GetEnvironmentVariable(LegacyPayloadFixtures.Variable);
        if (setting != null && setting.StartsWith("write:", StringComparison.Ordinal)) LegacyPayloadFixtures.Write(setting.Substring("write:".Length));
        int parity = LegacyPayloadFixtures.Parity(); checks += parity;
        Console.WriteLine($"GPC LEGACY IMPORT FEM PARITY (until R6.5): {parity} checks (the frozen payloads equal to the FEM writer now, byte for byte; the former local axes).");
    }
}
#endif
