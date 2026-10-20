using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class BridgeAssignmentTests
{
    internal static BeamPropertyModel Property(string name = "P") => new(name)
    {
        PropertyType = BeamPropertyType.Beam,
        Section = new SectionModel { SectionType = SectionModel.SectionTypes.SolidRectangle, B = 300, D = 1500 },
        Material = new MaterialModel("S") { Modulus = 210000, PoissonRatio = .3 }
    };
    internal static void Run()
    {
        var input = SimpleBridgeTests.Input() with { CrossfallPercent = new[] { 5d } };
        var geometry = SimpleBridgeBuilder.Build(input); var property = Property();
        var defaults = new Dictionary<int, BeamPropertyModel> { [-1] = property };
        var rule = new BridgeSectionRule { Property = Property("P2"), Group = 2, Start = 12345, End = 45000,
            ScaleFrom = new(1, 1), ScaleTo = new(1, 2) };
        var result = BridgeMemberAssignment.Assign(geometry, defaults, new[] { rule });
        Require(result.Unassigned.Length == 0 && result.Beams.Length == geometry.Members.Count + 1, "Rule boundary splits only selected girder");
        var selected = result.Beams.Where(b => b.BeamProperty.Id == "P2").ToArray();
        Require(selected.Length > 0 && selected.All(b => b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Group == 2), "Schedule selects G2");
        foreach (var beam in result.Beams)
        {
            var data = beam.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value;
            beam.GetLocalAxes(false, out _, out var up, out _, out _);
            Require(up * data.SectionUp > 1 - 1e-10, "Actual local axis matches requested frame");
            Require(geometry.Members.Any(m => m.PhysicalId == data.PhysicalId), "Physical identity survives subdivision");
        }
        var first = selected.First(); var taper = first.Attributes.OfType<BeamTaperAttributeModel>().Single();
        Require(Math.Abs(taper.Value.NFromY - 1) < 1e-12, "Taper begins at exact schedule boundary");
        Require(Math.Abs(selected.Last().Attributes.OfType<BeamTaperAttributeModel>().Single().Value.NToY - 2) < 1e-12, "Taper ends at exact schedule boundary");
        var clone = ModelArchive.Clone(result.Beams[0]);
        Require(clone.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value == result.Beams[0].Attributes.OfType<BridgeMemberAttributeModel>().Single().Value, "Metadata archives");
        Require(!ReferenceEquals(result.Beams[0].BeamProperty, property) && property.Id == "P", "Input property isolation");
        var missing = BridgeMemberAssignment.Assign(geometry, new Dictionary<int, BeamPropertyModel> { [0] = property }, Array.Empty<BridgeSectionRule>());
        Require(missing.Unassigned.Length == geometry.Members.Count(m => m.Family != BridgeMemberFamily.Girder), "Missing properties exposed");
        Reject(() => BridgeMemberAssignment.Assign(geometry, defaults, new[] { rule, rule with { Start = 20000 } }));
        Reject(() => BridgeMemberAssignment.Assign(geometry, defaults, new[] { rule with { End = 100001 } }));
        var refined = SimpleBridgeBuilder.Build(input with { MaximumElementLength = 1250 });
        Require(refined.Members.Select(m => m.PhysicalId).Distinct().OrderBy(x => x).SequenceEqual(
            geometry.Members.Select(m => m.PhysicalId).Distinct().OrderBy(x => x)), "Physical identity independent of longitudinal mesh");
        Console.WriteLine("BRIDGE ASSIGNMENT PASS: exact boundaries, taper factors, local axes, physical IDs, archives, missing/overlapping rules.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE ASSIGNMENT: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected invalid schedule rejection"); }
}
