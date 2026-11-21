using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

/// <summary>BridGHe 0.7.2 (proposals 1, 2 and 8 of 21/11/2026; texts in English, user 21/11): the unit warnings of the alignment, the labels
/// of the Grillage Mesh preview and the member families by name. Run inside existing regressions (bridge-alignment-inputs, bridge-assignment),
/// so the count of the regressions stays 107.</summary>
internal static class Usability072Tests
{
    private static int checks;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("USABILITY 0.7.2: " + message); checks++; }
    private static Assembly Plugin => BridgeMergeTests.Initialize();

    /// <summary>Proposal 1: the warnings for the Rhino document unit (the real factor) and for an alignment shorter than 1 m.</summary>
    internal static void Units()
    {
        checks = 0;
        var helper = Plugin.GetType("BridGHe.Grasshopper.Helpers.AlignmentUnits", true);
        var warnings = helper.GetMethod("Warnings", BindingFlags.NonPublic | BindingFlags.Static);
        string[] Of(double length, UnitSystem? units) => ((IEnumerable<string>)warnings.Invoke(null, new object[] { length, units })).ToArray();
        Check(Of(100000, UnitSystem.Millimeters).Length == 0 && Of(100000, null).Length == 0, "a 100 m alignment in mm, or without a document, gives no warning");
        var metres = Of(100000, UnitSystem.Meters);
        Check(metres.Length == 1 && metres[0].Contains("metres") && metres[0].Contains("1000 times smaller") && metres[0].Contains("scale the alignment by 1000"),
            "a document in metres: the factor 1000 from the real unit: " + string.Join(" / ", metres));
        Check(Of(100000, UnitSystem.Centimeters).Single().Contains("10 times smaller"), "a document in centimetres: factor 10");
        Check(Of(100000, UnitSystem.Inches).Single().Contains("25.4 times smaller"), "a document in inches: factor 25.4");
        Check(Of(100000, UnitSystem.Microns).Single().Contains("1000 times larger"), "a document in microns: 1000 times larger");
        var shortOne = Of(85, null);
        Check(shortOne.Length == 1 && shortOne[0].StartsWith("Alignment 85 mm long: probably drawn in metres", StringComparison.Ordinal), "an 85 mm alignment without a document: " + string.Join(" / ", shortOne));
        Check(Of(85, UnitSystem.Meters).Length == 2 && Of(1000, null).Length == 0, "both warnings together; 1 m is accepted");
        // The components: the length warning on Bridge Support Stations and Bridge Alignment Curve, never an error, outputs unchanged.
        string documentUnits = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToString() ?? "none";
        using var doc = new GH_Document();
        var stations = BridgeSegmentExcelTests.Add(doc, "BridgeSupportStationsComponent");
        var curve = BridgeSegmentExcelTests.Add(doc, "BridgeAlignmentCurveComponent");
        ((Param_Curve)stations.Params.Input[0]).PersistentData.Append(new GH_Curve(new LineCurve(Point3d.Origin, new Point3d(85, 0, 0))));
        ((Param_Number)curve.Params.Input[0]).PersistentData.Append(new GH_Number(85));
        BridgeSegmentExcelTests.Solve(doc);
        foreach (var c in new[] { stations, curve })
        {
            var w = c.RuntimeMessages(GH_RuntimeMessageLevel.Warning);
            Check(w.Any(m => m.StartsWith("Alignment 85 mm long", StringComparison.Ordinal)) && c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0
                && c.Params.Output[0].VolatileDataCount == 1, c.Name + ": the short alignment warns, without an error: " + string.Join(" / ", w));
        }
        ((Param_Curve)stations.Params.Input[0]).PersistentData.Clear();
        ((Param_Curve)stations.Params.Input[0]).PersistentData.Append(new GH_Curve(new LineCurve(Point3d.Origin, new Point3d(100000, 0, 0))));
        ((Param_Number)curve.Params.Input[0]).PersistentData.Clear(); ((Param_Number)curve.Params.Input[0]).PersistentData.Append(new GH_Number(100000));
        stations.Params.Input[0].ExpireSolution(false); curve.Params.Input[0].ExpireSolution(false); BridgeSegmentExcelTests.Solve(doc);
        foreach (var c in new[] { stations, curve })
            Check(!c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.StartsWith("Alignment ", StringComparison.Ordinal)), c.Name + ": a 100 m alignment gives no length warning");
        Console.WriteLine($"USABILITY 0.7.2 UNITS PASS: {checks} checks; document unit factor (m, cm, in, um), alignment shorter than 1 m, both components; active document of this host: {documentUnits}.");
        Labels();
    }

    /// <summary>Proposal 2 (preview only): the labels computed for Labels = True.</summary>
    private static void Labels()
    {
        checks = 0;
        var input = SimpleBridgeTests.Input();
        var geometry = SimpleBridgeBuilder.Build(input);
        var type = Plugin.GetType("BridGHe.Grasshopper.Components.Geometry.SimpleBridgeComponent+PreviewLabels", true);
        var labels = type.GetMethod("Of", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { input, geometry });
        var dots = ((IEnumerable)type.GetProperty("Dots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(labels)).Cast<object>()
            .Select(d => ((ValueTuple<Point3d, string, Color>)d)).ToArray();
        var arrow = (Line)type.GetProperty("Arrow", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(labels);
        int girders = geometry.Members.Where(m => m.Family == BridgeMemberFamily.Girder).Select(m => m.Group).Distinct().Count();
        for (int g = 1; g <= girders; g++) Check(dots.Count(d => d.Item2 == "G" + g) == 2, $"G{g} at the start and at the end of the girder");
        Check(geometry.Segments.All(s => dots.Count(d => d.Item2 == BridgeSegmentSections.Id(s)) == 1), "one concio ID (G1-C1) at the middle of every concio");
        Check(dots.Count(d => d.Item2 == "Start 0.000 m") == 1 && arrow.IsValid && arrow.Length > 0, "the direction arrow and «Start 0.000 m» at the alignment start");
        for (int i = 0; i < input.Supports.Length; i++)
        {
            string text = $"S{i + 1} · " + (input.Supports[i] / 1000).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + " m";
            Check(dots.Count(d => d.Item2 == text) == 1, "the progressive of the support: " + text);
        }
        var left = dots.Single(d => d.Item2 == "Left").Item1; var right = dots.Single(d => d.Item2 == "Right").Item1;
        Check(geometry.LeftEdge.ClosestPoint(left).DistanceTo(left) < geometry.RightEdge.ClosestPoint(left).DistanceTo(left)
            && geometry.RightEdge.ClosestPoint(right).DistanceTo(right) < geometry.LeftEdge.ClosestPoint(right).DistanceTo(right), "Left beside the left edge, Right beside the right edge");
        var girderStart = geometry.Members.Where(m => m.Family == BridgeMemberFamily.Girder && m.Group == 1).OrderBy(m => m.StationFrom).First().Axis.From;
        Check(dots.Where(d => d.Item2 == "G1").Min(d => d.Item1.DistanceTo(girderStart)) >= 499, "G1 drawn before the girder start, not over the support label");
        Console.WriteLine($"USABILITY 0.7.2 LABELS PASS: {checks} checks; {dots.Length} labels: G1..G{girders}, {geometry.Segments.Count} conci, {input.Supports.Length} supports, start arrow, Left/Right.");
    }

    /// <summary>Proposal 8: the named values of Family on Bridge Section Schedule (as Bridge Family Property) and the Value Lists filled with the
    /// family names, keeping the value selected; a list of other values is left alone.</summary>
    internal static void Families()
    {
        checks = 0;
        using var doc = new GH_Document();
        var schedule = BridgeSegmentExcelTests.Add(doc, "BridgeSectionScheduleComponent");
        var family = BridgeSegmentExcelTests.Add(doc, "BridgeFamilyPropertyComponent");
        Check(((Param_Integer)schedule.Params.Input[3]).HasNamedValues && ((Param_Integer)family.Params.Input[1]).HasNamedValues, "Family has named values on both components");
        GH_ValueList List(params (string Name, string Value, bool Selected)[] items)
        {
            var list = new GH_ValueList(); list.CreateAttributes(); list.ListItems.Clear();
            foreach (var i in items) list.ListItems.Add(new GH_ValueListItem(i.Name, i.Value) { Selected = i.Selected });
            doc.AddObject(list, false); return list;
        }
        string[] names = Enum.GetNames(typeof(BridgeMemberFamily));
        var travi = List(("Travi", "0", false), ("Correnti", "2", true));
        schedule.Params.Input[3].AddSource(travi);
        Check(travi.ListItems.Select(i => i.Name).SequenceEqual(names) && travi.ListItems.Select(i => i.Expression).SequenceEqual(Enumerable.Range(0, 9).Select(v => v.ToString()))
            && travi.SelectedItems.Single().Name == "TopChord", "Section Schedule: the list filled with the 9 family names, value 2 still selected: " + string.Join(",", travi.ListItems.Select(i => i.Name + (i.Selected ? "*" : ""))));
        var steel = List(("Acciaio", "-1", true), ("Soletta", "8", false));
        family.Params.Input[1].AddSource(steel);
        Check(steel.ListItems.Count == 10 && steel.ListItems[0].Name == "All steel families" && steel.ListItems[0].Expression == "-1" && steel.SelectedItems.Single().Expression == "-1"
            && steel.ListItems.Skip(1).Select(i => i.Name).SequenceEqual(names), "Family Property: All steel families first, value -1 kept");
        var other = List(("Altro", "12", true), ("Travi", "0", false));
        var otherSchedule = BridgeSegmentExcelTests.Add(doc, "BridgeSectionScheduleComponent");
        otherSchedule.Params.Input[3].AddSource(other);
        Check(other.ListItems.Count == 2 && other.ListItems[0].Name == "Altro", "a list with a value that is not a family is left as it is");
        int before = travi.ListItems.Count; schedule.Params.Input[3].RemoveAllSources(); schedule.Params.Input[3].AddSource(travi);
        Check(travi.ListItems.Count == before && travi.SelectedItems.Single().Name == "TopChord", "a list already filled is untouched");
        Console.WriteLine($"USABILITY 0.7.2 FAMILIES PASS: {checks} checks; named values on Section Schedule and Family Property, Value Lists filled keeping the value, other lists untouched.");
    }
}
