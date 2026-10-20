using System;
using System.Drawing;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Geometry;

internal static class BridgeCheckMapTests
{
    internal static void Run()
    {
        var model = BridgeConstructionTests.Model(SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()));
        var rows = new[]
        {
            new BridgeMappedCheck("1", 0, "P1", "C1", "Shear", .8, "Entro limite", ""),
            new BridgeMappedCheck("1", 500, "P2", "C2", "Stud", null, "Da verificare", ""),
            new BridgeMappedCheck("2", 0, "P3", "C3", "Web", 1.2, "Non verificato", ""),
            new BridgeMappedCheck("2", 100, "P4", "C4", "Flange", 1.2, "Non verificato", "")
        };
        var map = BridgeCheckMap.Evaluate(model, rows);
        Require(map[0].Color == Color.DarkOrange && map[0].Unavailable == 1 && map[0].Maximum == .8, "Partial checks remain amber");
        Require(map[1].Color == Color.Crimson && map[1].Governing.Contains("C3") && map[1].Governing.Contains("C4"), "Failed ratio and tied provenance");
        Require(map[2].Color == Color.SlateGray && double.IsNaN(map[2].Maximum), "Unmapped is not zero/pass");
        try { BridgeCheckMap.Evaluate(model, new[] { rows[0] with { BeamId = "missing" } }); }
        catch (ArgumentException) { Console.WriteLine("BRIDGE CHECK MAP PASS: missing/partial/failed checks, tied case/phase provenance and unknown-ID rejection."); return; }
        throw new InvalidOperationException("Unknown check beam accepted");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE CHECK MAP: " + text); }
}
