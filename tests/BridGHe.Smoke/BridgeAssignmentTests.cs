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
        Gpc();
    }

    /// <summary>R6.6 R2a: Bridge Member Assignment on GPC data alone (the path of P2): the default property and the rule given by their GPC
    /// properties (<see cref="R2aDesignFixtures.RectangleProperty"/>), the beams the drafts of the plans as the component builds them; the same
    /// assertions on the GPC data of the drafts (bridge member, Element ID, records, section axes, taper, archive of the draft).</summary>
    private static void Gpc()
    {
        var input = SimpleBridgeTests.Input() with { CrossfallPercent = new[] { 5d } };
        var geometry = SimpleBridgeBuilder.Build(input); var property = R2aDesignFixtures.RectangleProperty();
        var defaults = new Dictionary<int, BridGHe.Common.Gpc.Legacy.LegacyBeamProperty> { [-1] = property };
        var rule = new BridgeSectionRule { GpcProperty = R2aDesignFixtures.RectangleProperty("P2"), Group = 2, Start = 12345, End = 45000,
            ScaleFrom = new(1, 1), ScaleTo = new(1, 2) };
        var result = R2aGeneratorFixtures.Assign(geometry, defaults, new[] { rule });
        var beams = R2aGeneratorFixtures.Drafts(result.Plans, "assignment");
        Require(result.Beams.Length == 0 && result.Unassigned.Length == 0 && beams.Length == geometry.Members.Count + 1, "Rule boundary splits only selected girder");
        var selected = beams.Where(b => b.Beam.BeamProperty.Name == "P2").ToArray();
        Require(selected.Length > 0 && selected.All(b => R2aGeneratorFixtures.Member(b).Group == 2), "Schedule selects G2");
        foreach (var beam in beams)
        {
            Require(beam.Records.Groups.Count == 1 && beam.Records.Labels.Count == 1 && int.TryParse(beam.Records.Labels.Single(), out _),
                "Straus-compatible integer IDs and single hierarchical group");
            var data = R2aGeneratorFixtures.Member(beam);
            Require(R2aGeneratorFixtures.Dot(R2aGeneratorFixtures.Up(beam), data.SectionUp) > 1 - 1e-10, "Actual local axis matches requested frame");
            Require(geometry.Members.Any(m => m.PhysicalId == data.PhysicalId), "Physical identity survives subdivision");
        }
        var first = selected.First(); var taper = first.Beam.Assignments.Physical.Taper;
        Require(Math.Abs(taper.ScaleI2 - 1) < 1e-12, "Taper begins at exact schedule boundary");
        Require(Math.Abs(selected.Last().Beam.Assignments.Physical.Taper.ScaleJ2 - 2) < 1e-12, "Taper ends at exact schedule boundary");
        var clone = R2aGeneratorFixtures.Clone(beams[0]);
        Require(R2aGeneratorFixtures.Same(R2aGeneratorFixtures.Member(clone), R2aGeneratorFixtures.Member(beams[0])), "Metadata archives");
        Require(!ReferenceEquals(beams[0].Beam.BeamProperty, property.Property) && property.Property.Name == "P", "Input property isolation");
        var missing = R2aGeneratorFixtures.Assign(geometry, new Dictionary<int, BridGHe.Common.Gpc.Legacy.LegacyBeamProperty> { [0] = property });
        Require(missing.Unassigned.Length == geometry.Members.Count(m => m.Family != BridgeMemberFamily.Girder), "Missing properties exposed");
        Reject(() => R2aGeneratorFixtures.Assign(geometry, defaults, new[] { rule, rule with { Start = 20000 } }));
        Reject(() => R2aGeneratorFixtures.Assign(geometry, defaults, new[] { rule with { End = 100001 } }));
        var refined = SimpleBridgeBuilder.Build(input with { MaximumElementLength = 1250 });
        Require(refined.Members.Select(m => m.PhysicalId).Distinct().OrderBy(x => x).SequenceEqual(
            geometry.Members.Select(m => m.PhysicalId).Distinct().OrderBy(x => x)), "Physical identity independent of longitudinal mesh");
        Console.WriteLine("BRIDGE ASSIGNMENT PASS: exact boundaries, taper factors, local axes, physical IDs, archives, missing/overlapping rules.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE ASSIGNMENT: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Expected invalid schedule rejection"); }
}
