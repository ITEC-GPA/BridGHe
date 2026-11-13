using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

internal static class BridgeSegmentSectionsTests
{
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("SEGMENT SECTIONS: " + name); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected section table rejection."); }
    internal static BeamPropertyModel BaseProperty()
    {
        var section = new SectionModel { SectionType = SectionModel.SectionTypes.I, B1 = 600, B2 = 600, D = 2500, T1 = 30, T2 = 30, T3 = 16 };
        section.CalculateSection();
        return new BeamPropertyModel("Base") { PropertyType = BeamPropertyType.Beam, Section = section,
            Material = new MaterialModel("S355") { Kind = MaterialModel.MaterialKind.Steel, Modulus = 210000, PoissonRatio = .3 } };
    }
    internal static string Edit(string line, int column, string text)
    { var cells = line.Split(';'); cells[column] = text; return string.Join(";", cells); }
    internal static void Run()
    {
        Gpc();
    }

    /// <summary>R6.6 R2a: Bridge Segment Sections on a base property given by its GPC datum alone (<see cref="BridgeSegmentSections.CreateGpc"/>,
    /// the path of P2; <see cref="R2aDesignFixtures.BaseProperty"/>): the same tables, rejections and analytical values, read on the GPC data
    /// (the section recipe and the GPC section of each concio, its top offset), and the assignment on GPC data alone.</summary>
    private static void Gpc()
    {
        var input = SimpleBridgeTests.Input(); var geometry = SimpleBridgeBuilder.Build(input); var property = R2aDesignFixtures.BaseProperty();
        double T1(BridgeSectionRule r) => r.GpcProperty.Data.Section.T1;
        GPC.Model.Sections.ISectionShape Section(BridgeSectionRule r) => ((GPC.Model.ElementProperties.ElasticBeamProperty)r.GpcProperty.Property).Section;
        var initial = BridgeSegmentSections.CreateGpc(geometry, property, null);
        Check(initial.Rules.Length == geometry.Segments.Count, "one rule for every fabrication segment");
        Check(initial.Rules.All(r => Math.Abs(R2aDesignFixtures.Area(r.GpcProperty) - R2aDesignFixtures.Area(property)) < 1e-6), "base dimensions preserved");
        var table = initial.Table.ToArray(); table[1] = Edit(table[1], 6, "50"); // Only G1-C1 bottom flange thicker.
        var result = BridgeSegmentSections.CreateGpc(geometry, property, table.Reverse());
        var first = Section(result.Rules[0]);
        double hw = 2500 - 50 - 30, ab = 600 * 50, at = 600 * 30, aw = 16 * hw;
        double area = ab + at + aw, yc = (ab * 25 + at * 2485 + aw * (50 + hw / 2)) / area;
        double ix = 600 * Math.Pow(50, 3) / 12 + ab * Math.Pow(25 - yc, 2) +
                    600 * Math.Pow(30, 3) / 12 + at * Math.Pow(2485 - yc, 2) +
                    16 * Math.Pow(hw, 3) / 12 + aw * Math.Pow(50 + hw / 2 - yc, 2);
        // The centroid from the bottom: D minus the top offset of the concio (D - yG).
        Check(Math.Abs(first.Area - area) < 1e-6 && Math.Abs(2500 - result.Tops[0] - yc) < 1e-6 && Math.Abs(first.Jxx / ix - 1) < 1e-9,
            $"asymmetric section area, centroid and bending inertia agree with independent three-rectangle calculation (A {first.Area}, yG {2500 - result.Tops[0]}, Jxx {first.Jxx}, J11 {first.J11}, J22 {first.J22})");
        Check(result.Rules.Skip(1).All(r => T1(r) == 30) && property.Data.Section.T1 == 30, "edit is local and base input is unchanged");
        var beams = R2aGeneratorFixtures.Drafts(R2aGeneratorFixtures.Assign(geometry, new Dictionary<int, BridGHe.Common.Gpc.Legacy.LegacyBeamProperty> { [-1] = property }, result.Rules).Plans, "segment sections");
        Check(beams.Length > 0, "the assignment gives the beams");
        foreach (var b in beams)
        {
            var m = R2aGeneratorFixtures.Member(b);
            Check(R2aGeneratorFixtures.T1(b) == (m.Family == GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder && m.Group == 1 && m.Segment == 1 ? 50 : 30),
                "correct property on every FEM member and only on the selected concio");
        }
        var refined = SimpleBridgeBuilder.Build(input with { MaximumElementLength = 1250 });
        Check(BridgeSegmentSections.CreateGpc(refined, property, table).ResolvedTable.SequenceEqual(result.ResolvedTable), "mesh-independent assignments");
        var changedBase = R2aDesignFixtures.BaseProperty(t1: 35);
        var inherited = BridgeSegmentSections.CreateGpc(geometry, changedBase, table);
        Check(T1(inherited.Rules[0]) == 50 && inherited.Rules.Skip(1).All(r => T1(r) == 35), "wildcards follow base sliders; overrides survive");
        // Test decimal comma independently, without altering station text.
        var decimalTable = table.ToArray(); decimalTable[1] = Edit(decimalTable[1], 6, "50,5").Replace(';', '\t');
        Check(T1(BridgeSegmentSections.CreateGpc(geometry, property, decimalTable).Rules[0]) == 50.5, "paste tab-separated values and decimal commas");
        Check(BridgeSegmentSections.Synchronize(geometry, table).Table.SequenceEqual(result.Table), "refresh preserves overrides exactly");
        var changed = SimpleBridgeBuilder.Build(input with { Segments = new BridgeSegments { MaximumLength = 7000 } });
        Reject(() => BridgeSegmentSections.CreateGpc(changed, property, table));
        var refresh = BridgeSegmentSections.Synchronize(changed, table);
        Check(refresh.Changes.Length > 0 && BridgeSegmentSections.CreateGpc(changed, property, refresh.Table).Rules.All(r => T1(r) == 30), "explicit refresh resets changed fabrication ranges");
        Reject(() => BridgeSegmentSections.CreateGpc(geometry, property, table.Concat(new[] { table[1] })));
        Reject(() => BridgeSegmentSections.CreateGpc(geometry, property, table.Skip(2)));
        foreach ((int col, string value) in new[] { (0, "G99-C999"), (1, "1"), (3, "NaN"), (3, "Infinity"), (3, ""), (6, "0"), (5, "40"), (8, "700"), (9, "1000") })
        {
            var invalid = table.ToArray(); invalid[1] = Edit(invalid[1], col, value);
            Reject(() => BridgeSegmentSections.CreateGpc(geometry, property, invalid));
        }
        Reject(() => BridgeSegmentSections.CreateGpc(geometry, property, new[] { "G1-C1;1" }));
        // A base property whose material is not steel (the kind recorded with the property, as the former material kind).
        var notSteel = BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Beam("Base", 5, R2aDesignFixtures.BaseSection(), R2aDesignFixtures.Steel(), "Concrete");
        Reject(() => BridgeSegmentSections.CreateGpc(geometry, notSteel, table));
        Console.WriteLine("SEGMENT SECTIONS PASS: keyed FEM assignments, analytical area/centroid/inertia, mesh independence, overrides, decimal paste, refresh and invalid/stale table rejection.");
    }
}
