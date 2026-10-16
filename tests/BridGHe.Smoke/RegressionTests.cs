using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class RegressionTests
{
    public static int Run(string selection)
    {
        var tests = new Dictionary<string, Action>
        {
            ["composite-archive"] = CompositeArchive,
            ["composite-preview"] = CompositePreview
        };
        if (selection != "all" && !tests.ContainsKey(selection)) throw new ArgumentException("Unknown regression: " + selection);
        int count = 0;
        foreach (var test in tests.Where(t => selection == "all" || t.Key == selection))
        {
            test.Value(); count++; Console.WriteLine("PASS REGRESSION: " + test.Key);
        }
        Console.WriteLine($"REGRESSION TOTAL {count}");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("REGRESSION FAILED: " + message);
    }

    private static CompositeBeamModel CompositeBeam()
    {
        var steel = new SectionModel { SectionType = SectionModel.SectionTypes.I, D = 500, B1 = 200, B2 = 200, T1 = 20, T2 = 20, T3 = 10 };
        var material = new MaterialModel("S355") { Modulus = 210000 };
        var property = new CompositeBeamPropertyModel("C") { SteelSection = steel,
            ConcreteSection = new SectionConcreteSlabModel(200, 1000, 1000, 40), SteelMaterial = material,
            ConcreteMaterial = material, RebarMaterial = material };
        return new CompositeBeamModel(Point3d.Origin, new Point3d(3000, 0, 0), property);
    }

    private static void CompositeArchive()
    {
        var beam = CompositeBeam();
        var loadCase = new LoadCaseModel("1", "Q");
        beam.Results.Add(new BeamForceResultModel(loadCase, 500, 0, "") { AxialForce = 1234 });
        var copy = ModelArchive.Clone(beam);
        Require(copy.IsValid && copy.Shapes.Length == 2 && copy.Length == 3000, "composite geometry after archive");
        Require(((BeamForceResultModel)copy.Results[0]).AxialForce == 1234, "composite results after archive");
        Require(ReferenceEquals(copy.StartBeamProperty, copy.EndBeamProperty), "shared composite property after archive");
    }

    private static void CompositePreview()
    {
        var beam = CompositeBeam();
        for (int i = 0; i < 3; i++)
        {
            beam.PointTo = new Point3d(4000 + i * 1000, 0, 0);
            Require(beam.Shapes.Length == 2, "preview solids must be replaced, not accumulated");
            Require(Math.Abs(beam.Shapes.Max(s => s.GetBoundingBox(true).Max.X) - beam.PointTo.X) < 1e-6, "preview follows current endpoint");
        }
        beam.EndBeamProperty = null;
        Require(!beam.IsValid && beam.Shapes.Length == 0, "missing section clears obsolete preview");
    }
}
