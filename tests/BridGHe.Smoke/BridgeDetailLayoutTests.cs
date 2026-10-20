using System;
using System.Linq;
using BridGHe.Common.Geometry;
using GPC.Checkers.CompositeBridge;
using Rhino.Geometry;

internal static class BridgeDetailLayoutTests
{
    internal static HSectionDimensions Section() => new() { SlabWidth = 2200, SlabHeight = 250, WebHeight = 1330,
        WebThickness = 16, TopWidth = 450, TopThickness = 30, BottomWidth = 600, BottomThickness = 40 };
    internal static BridgeStudOptions Studs() => new() { Enabled = true, CountPerRow = 2, Diameter = 22, Height = 180,
        HeadDiameter = 35, HeadThickness = 10, LongitudinalPitch = 200, TransversePitch = 150, RequiredCover = 40 };
    internal static BridgeIntermediateStiffener Stiffener() => new() { Enabled = true, Width = 200, Thickness = 20,
        RightWidth = 250, RightThickness = 25, Layout = BridgeStiffenerLayout.Symmetric };
    internal static void Run()
    {
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        var result = BridgeDetailLayout.Create(geometry, new[] { 2 }, 0, 10000, Section(), 1250, Studs(), Stiffener(), null, 4000, null);
        Require(result.StudAxes.Length == 100 && result.StudHeads.Length == 100, "50 rows, two studs per row");
        Require(result.Stiffeners.Length == 6 && result.Stiffeners.All(m => m.IsClosed && m.IsValid), "Three stiffener pairs, valid closed plate prisms");
        Require(result.StudAxes.All(l => Math.Abs(l.Length - 180) < 1e-8 && l.From.Z == -250 && l.To.Z == -70), "Stud datum and height");
        using var volume = VolumeMassProperties.Compute(result.Stiffeners[0]);
        Require(Math.Abs(Math.Abs(volume.Volume) - 200 * 1330 * 20) < 1e-3, "Analytical stiffener volume");
        var middle = (BridgeIntermediateStiffener)result.Details.Single(d => d.Kind == "Intermediate stiffener" && d.Station == 4000).Definition;
        Require(middle.LeftPanel == 4000 && middle.RightPanel == 4000 && !middle.EqualPanels, "Actual adjacent panel lengths");
        var second = BridgeDetailLayout.Create(geometry, new[] { 2 }, 10000, 20000, Section(), 1250, Studs(), null, null, 4000, null);
        Require(!result.Details.Select(d => d.Id).Intersect(second.Details.Select(d => d.Id)).Any(), "Adjacent layout zones have no duplicate boundary row");
        var box = BridgeDetailLayout.Create(geometry, new[] { 2 }, 0, 1000, Section() with { SectionType = BridgeSteelSectionType.Box, WebSpacing = 1800 },
            1250, Studs() with { CountPerRow = 4 }, Stiffener(), null, 4000, null);
        Require(box.StudAxes.Length == 20 && box.Stiffeners.Length == 4, "Box studs on both flanges and stiffeners on both webs");
        Reject(() => BridgeDetailLayout.Create(geometry, null, 0, 10000, Section(), 1250, Studs() with { CountPerRow = 2.5 }, null, null, 4000, null));
        Reject(() => BridgeDetailLayout.Create(geometry, null, 0, 10000, Section(), 1400, Studs(), null, null, 4000, null));
        Console.WriteLine("BRIDGE DETAILS PASS: gPC row counts, cover/datum, analytical plate volume, adjacent panels, box layout and zone boundaries.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE DETAILS: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid detail input accepted"); }
}
