#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using Grasshopper.Kernel.Types;
using R = Rhino.Geometry;

/// <summary>
/// R6.2, lane B (beams): the runs of the converted producer components (regression <c>gpc-builders-b</c>, harness <see cref="GpcBuilderChecks"/>):
/// one run per component and per input variant the component distinguishes; every output datum of a GH type with a GPC datum carries a datum
/// built on GPC, compared on attach with the conversion of its FEM value (<see cref="GpcDraftCheckHost"/>, a difference fails the run).
/// Variants the reader refuses (and the builder alike) give an output without a datum (<c>refused</c> below).
/// </summary>
internal static class GpcBuilderChecksB
{
    private static readonly Dictionary<string, int> PerComponent = new Dictionary<string, int>(StringComparer.Ordinal);

    internal static void Regression()
    {
        using var h = new GpcBuilderChecks("B");
        List<IGH_Goo>[] Run(string type, int[] attached, params (int, object)[] inputs)
        {
            PerComponent.TryGetValue(type, out int n); PerComponent[type] = n + 1;
            return h.Run(type, attached, inputs);
        }
        IGH_Goo Out(List<IGH_Goo>[] o, int i = 0) => GpcBuilderChecks.Output(o, i);
        var one = new[] { 0 }; var none = new int[0];
        var refusedField = System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly).GetType("BridGHe.Grasshopper.Helpers.GpcBeamInputs").GetField("Refused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        int Refused() => (int)refusedField.GetValue(null);
        int refusedBefore = Refused();

        // Inputs: two load cases, groups (nested and plain), properties (I section, explicit section with a principal angle, a truss).
        var dead = Out(Run("LoadCaseComponent", none, (0, "Dead")));
        var live = Out(Run("LoadCaseComponent", none, (0, "Live"), (1, true)));
        var steel = new MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, UsePoisson = true, Density = 7.85e-9 };
        var iProperty = h.Goo("GH_BeamProperty", new BeamPropertyModel("PI") { Name = "PI", Material = steel, PropertyType = BeamPropertyType.Beam,
            Section = new SectionModel { Name = "I", SectionType = SectionModel.SectionTypes.I, D = 500, B1 = 300, B2 = 100, T1 = 20, T2 = 10, T3 = 5 } });
        var generic = new SectionModel { Name = "G", SectionType = SectionModel.SectionTypes.Generic, SectionArea = 12000, I11 = 4.5e8, I22 = 1.2e8, J = 3e6, AngleX1Rad = 0.3,
            Centroid = new R.Point2d(15, -40), ShearA1 = 5000, ShearA2 = 4000, ShearL1 = 3, ShearL2 = -2 };
        var gProperty = h.Goo("GH_BeamProperty", new BeamPropertyModel("PG") { Name = "PG", Material = steel, PropertyType = BeamPropertyType.Beam, Section = generic });
        var truss = h.Goo("GH_BeamProperty", new BeamPropertyModel("PT") { Name = "PT", Material = steel, PropertyType = BeamPropertyType.Truss,
            Section = new SectionModel { Name = "R", SectionType = SectionModel.SectionTypes.SolidRectangle, D = 100, B = 50 } });
        var deck = new GroupModel("Deck"); var nested = h.Goo("GH_Group", new GroupModel("Segment2", new GroupModel("Girder1", deck))); var plain = h.Goo("GH_Group", new GroupModel("Other"));
        var axisX = new R.LineCurve(new R.Point3d(0, 0, 0), new R.Point3d(12000, 0, 0));
        var skew = new R.LineCurve(new R.Point3d(100, -250, 30), new R.Point3d(9100.5, 3200.25, 812.75));
        var vertical = new R.LineCurve(new R.Point3d(500, 500, 0), new R.Point3d(500, 500, 4000));

        // Beam: no groups, angle or offset; groups (nested first), angle and offset; explicit section with a principal angle; vertical; truss.
        var b0 = Out(Run("BeamComponent", one, (0, axisX), (1, iProperty)));
        var b1 = Out(Run("BeamComponent", one, (0, skew), (1, iProperty), (2, new[] { nested, plain }), (3, 17.5), (4, new R.Vector3d(120, -35, 0))));
        var b2 = Out(Run("BeamComponent", one, (0, skew), (1, gProperty), (2, new[] { plain }), (3, -40.0), (4, new R.Vector3d(0, 60, 0))));
        var b3 = Out(Run("BeamComponent", one, (0, vertical), (1, iProperty), (3, 90.0)));
        Run("BeamComponent", one, (0, vertical), (1, truss), (4, new R.Vector3d(25, 0, 0)));
        Run("BeamComponent", one, (0, axisX), (1, gProperty), (3, 0.0), (4, new R.Vector3d(0, 0, 0)));
        var bases = new[] { b0, b1, b2, b3 };

        // Attributes on every base beam (axes, offsets and properties differ), then chained.
        foreach (var b in bases)
        {
            Run("BeamOutputStationsComponent", one, (0, b), (1, 9));
            Run("BeamReleasesComponent", one, (0, b), (1, 0.0), (6, 2.5e3), (9, 0.0), (12, 7e6));
            Run("BeamSupportComponent", one, (0, b), (1, 3.5), (2, 3));
            Run("BeamTaperComponent", one, (0, b), (1, 2.0), (2, 1.0), (3, 1.0), (4, 1.5), (5, 0), (6, 2));
            Run("SuperElementComponent", one, (0, b), (1, "G1"));
        }
        // Releases: every DOF at both ends, partial and full; Support: every direction, compression only with a gap, tension-compression.
        Run("BeamReleasesComponent", one, Enumerable.Range(1, 12).Select(i => (i, (object)(i % 3 == 0 ? 0.0 : i * 1000.0))).Prepend((0, (object)b1)).ToArray());
        for (int d = 0; d < 4; d++) Run("BeamSupportComponent", one, (0, b1), (1, 0.25 + d), (2, d), (3, d % 2 == 0), (4, d % 2 == 0 ? 1.5 : 0.0));
        // Taper: the alignments Top, Center, Bottom in both directions.
        for (int ax = 0; ax < 3; ax++) Run("BeamTaperComponent", one, (0, b2), (1, 1.2), (2, 0.8), (3, 1.4), (4, 0.6), (5, ax), (6, 2 - ax));
        // String group: a list of beams.
        Run("BeamStringGroupComponent", one, (0, new[] { b0, b1, b2 }), (1, 7));
        // Super element: a second name (two names at list level), the same name twice; on a plate (refused by the reader and the builder).
        var se = Out(Run("SuperElementComponent", one, (0, b1), (1, "SE-A")));
        Run("SuperElementComponent", one, (0, se), (1, "SE-B"));
        Run("SuperElementComponent", one, (0, se), (1, "SE-A"));
        var plate = h.Goo("GH_Plate", new PlateModel("", new[] { new R.Point3d(0, 0, 0), new R.Point3d(1000, 0, 0), new R.Point3d(1000, 1000, 0) },
            new PlatePropertyModel("PP") { Name = "PP", Material = steel, MembraneThickness = 200, BendingThickness = 200 }), null);
        // Decision of the user (7/11): a super element takes only beams; a plate is an error with no output.
        var refusedPlate = Run("SuperElementComponent", none, (0, plate), (1, "SE-P"));
        h.ExpectError("A Super-Element takes only beams");
        if (refusedPlate[0].Count != 0) h.Failures.Add("B SuperElementComponent: a plate gave an output");
        // Chained attributes: two output stations records, two tapers (counted), string group after supports.
        var chain = Out(Run("BeamOutputStationsComponent", one, (0, b1), (1, 3)));
        chain = Out(Run("BeamTaperComponent", one, (0, chain), (1, 1.1), (2, 1.0), (3, 1.0), (4, 1.0), (5, 1), (6, 1)));
        chain = Out(Run("BeamTaperComponent", one, (0, chain), (1, 1.3), (2, 1.0), (3, 1.0), (4, 1.0), (5, 0), (6, 0)));
        chain = Out(Run("BeamSupportComponent", one, (0, chain), (1, 2.0), (2, 1)));
        chain = Out(Run("BeamStringGroupComponent", one, (0, new[] { chain }), (1, 3)));
        chain = Out(Run("BeamOutputStationsComponent", one, (0, chain), (1, 11)));

        // Loads: every schema in the principal axes 1, 2, 3 (force and moment); global directions; a global system and a system that is not.
        var cs = new CoordinateSystemModel("1", "Global"); var gcs = h.Goo("GH_CoordinateSystem", cs);
        var other = new CoordinateSystemModel("2", "Rotated") { Point1 = new R.Point3d(0, 1, 0), Point2 = new R.Point3d(-1, 0, 0) }; var ocs = h.Goo("GH_CoordinateSystem", other);
        (int, object)[] Values(int schema) => schema switch
        {
            0 => new (int, object)[] { (6, -12.5), (9, 0.1), (10, 0.2) },
            1 => new (int, object)[] { (6, -12.5), (7, -3.0), (9, 0.1), (10, 0.25) },
            2 => new (int, object)[] { (5, 1.0), (6, -4.0), (8, 2.0), (9, 0.4), (10, 0.0) },
            3 => new (int, object)[] { (5, 1.0), (6, -4.0), (7, -6.0), (9, 0.3), (10, 0.2) },
            4 => new (int, object)[] { (6, -4.0), (7, -6.0), (8, 0.5), (9, 0.0), (10, 0.35) },
            _ => new (int, object)[] { (5, 1.0), (6, -4.0), (7, -6.0), (8, 0.5), (9, 0.15), (10, 0.3) },
        };
        foreach (var b in new[] { b0, b1, b2, b3 })
            for (int schema = 0; schema < 6; schema++)
                for (int dir = 0; dir < 3; dir++)
                {
                    var inputs = new List<(int, object)> { (0, b), (1, dead), (2, schema), (4, dir) }; inputs.AddRange(Values(schema));
                    Run("BeamDistributedLoadComponent", one, inputs.ToArray());
                    if (b == b1) Run("BeamDistributedMomentComponent", one, inputs.Select(i => i.Item1 > 3 ? (i.Item1 - 1, i.Item2) : i).ToArray());
                }
        for (int dir = 0; dir < 8; dir++)
            foreach (var b in new[] { b1, b3 })
                Run("BeamDistributedLoadComponent", one, new (int, object)[] { (0, b), (1, live), (2, 1), (3, gcs), (4, dir) }.Concat(Values(1)).ToArray());
        Run("BeamDistributedLoadComponent", none, new (int, object)[] { (0, b1), (1, dead), (2, 0), (3, ocs), (4, 2) }.Concat(Values(0)).ToArray());
        // Point loads and moments (a coordinate system given is ignored: the principal axes), at the ends and inside.
        foreach (var b in bases)
            foreach (var a in new[] { 0.0, 0.37, 0.999 })
            {
                Run("BeamPointLoadComponent", one, (0, b), (1, dead), (3, 10.0), (4, -20.0), (5, 3.5e3), (6, a));
                Run("BeamPointMomentComponent", one, (0, b), (1, live), (2, gcs), (3, 1e6), (4, 0.0), (5, -2e5), (6, a));
            }
        // Gradients and prestrains (tension and strain).
        foreach (var b in bases)
        {
            Run("BeamGradientLoadComponent", one, (0, b), (1, dead), (2, 0.012), (3, -0.004));
            Run("BeamPreLoadComponent", one, (0, b), (1, live), (2, 0), (3, 1.5e5));
            Run("BeamPreLoadComponent", one, (0, b), (1, dead), (2, 1), (3, -2e-4));
        }
        // A beam with many loads in the same and in other cases (the case instance shared by name; the load records in order).
        var loaded = Out(Run("BeamDistributedLoadComponent", one, new (int, object)[] { (0, chain), (1, dead), (2, 0), (4, 1) }.Concat(Values(0)).ToArray()));
        loaded = Out(Run("BeamPreLoadComponent", one, (0, loaded), (1, dead), (2, 0), (3, 2e4)));
        loaded = Out(Run("BeamDistributedLoadComponent", one, new (int, object)[] { (0, loaded), (1, live), (2, 5), (3, gcs), (4, 6) }.Concat(Values(5)).ToArray()));
        loaded = Out(Run("BeamPointLoadComponent", one, (0, loaded), (1, dead), (3, 1.0), (4, 2.0), (5, 3.0), (6, 0.5)));
        loaded = Out(Run("BeamGradientLoadComponent", one, (0, loaded), (1, live), (2, 0.01), (3, 0.02)));
        loaded = Out(Run("BeamDistributedMomentComponent", one, new (int, object)[] { (0, loaded), (1, dead), (2, 2), (3, 2) }.Concat(Values(2).Select(i => (i.Item1 - 1, i.Item2))).ToArray()));
        loaded = Out(Run("BeamReleasesComponent", one, (0, loaded), (7, 0.0), (8, 0.0)));
        loaded = Out(Run("SuperElementComponent", one, (0, loaded), (1, "G3")));

        // Beam Override: property, groups, angle, offset, each alone and together, on a plain beam and on the loaded beam (re-anchoring).
        foreach (var b in new[] { b0, b1, loaded })
        {
            Run("BeamOverrideComponent", one, (0, b), (1, gProperty));
            Run("BeamOverrideComponent", one, (0, b), (2, new[] { plain, nested }));
            Run("BeamOverrideComponent", one, (0, b), (3, 33.0));
            Run("BeamOverrideComponent", one, (0, b), (4, new R.Vector3d(-80, 45, 0)));
            Run("BeamOverrideComponent", one, (0, b), (1, iProperty), (2, new[] { nested }), (3, -12.0), (4, new R.Vector3d(0, 0, 0)));
            Run("BeamOverrideComponent", one, (0, b), (3, 0.0), (4, new R.Vector3d(10, 0, 0)));
        }
        var moved = Out(Run("BeamOverrideComponent", one, (0, loaded), (1, gProperty), (3, 61.0), (4, new R.Vector3d(30, -15, 0))));
        Run("BeamPointLoadComponent", one, (0, moved), (1, live), (3, 5.0), (4, 0.0), (5, -1.0), (6, 0.8));
        Run("BeamOverrideComponent", one, (0, moved), (1, iProperty), (3, 0.0), (4, new R.Vector3d(0, 0, 0)));

        // Beam Filter: by Element ID, group, property name, start and end point (the partition of the GPC drafts equals the FEM one, checked
        // inside the component), and a list without matches.
        var labelled = Out(Run("BridgeConnectionBeamsComponent", one, (0, new[] { new R.Line(new R.Point3d(0, 0, 0), new R.Point3d(0, 0, 1000)) }), (1, iProperty)));
        var pool = new IGH_Goo[] { b0, b1, b2, b3, chain, loaded, moved, labelled };
        Run("BeamFilterComponent", none, (0, pool), (1, new[] { "Connection1", "X" }));
        Run("BeamFilterComponent", none, (0, pool), (6, new[] { nested }));
        Run("BeamFilterComponent", none, (0, pool), (5, new[] { "PG", "PT" }));
        Run("BeamFilterComponent", none, (0, pool), (2, new[] { new R.Point3d(100, -250, 30) }), (3, new[] { new R.Point3d(12000, 0, 0.05) }));
        Run("BeamFilterComponent", none, (0, pool), (1, new[] { "none" }), (5, new[] { "none" }));

        // Merge Geometry: the FEM-free merge equals ModelHelper.MergeGeometry on points, curves and a mesh.
        {
            var points = new List<R.Point3d> { new(0, 0, 0), new(0.05, 0, 0), new(1000, 0, 0) };
            var curves = new List<R.Curve> { new R.LineCurve(new R.Point3d(0.02, 0, 0), new R.Point3d(999.97, 0, 0)), new R.LineCurve(new R.Point3d(1000, 0, 0), new R.Point3d(1000, 500, 0)) };
            var mesh = new R.Mesh(); mesh.Vertices.Add(0, 0, 0); mesh.Vertices.Add(1000.03, 0, 0); mesh.Vertices.Add(1000, 500.02, 0); mesh.Vertices.Add(0.01, 0, 0); mesh.Faces.AddFace(0, 1, 2); mesh.Faces.AddFace(3, 1, 2);
            var meshes = new List<R.Mesh> { mesh };
            List<R.Point3d> p1 = new(points), p2 = new(points); List<R.Curve> c1 = new(curves), c2 = new(curves); List<R.Mesh> m1 = new(meshes), m2 = new(meshes);
            BridGHe.Common.Helpers.ModelHelper.MergeGeometry(ref p1, ref c1, ref m1, 0.1);
            BridGHe.Common.Geometry.BridgeGeometryMerge.Merge(ref p2, ref c2, ref m2, 0.1);
            string Text(List<R.Point3d> p, List<R.Curve> c, List<R.Mesh> m) => string.Join(";", p.Select(x => x.ToString())) + "|" + string.Join(";", c.Select(x => x.PointAtStart + "/" + x.PointAtEnd))
                + "|" + string.Join(";", m.Select(x => string.Join(",", Enumerable.Range(0, x.Vertices.Count).Select(i => x.Vertices.Point3dAt(i).ToString())) + "#" + x.Faces.Count));
            if (Text(p1, c1, m1) != Text(p2, c2, m2)) h.Failures.Add("B: Merge Geometry differs from ModelHelper.MergeGeometry");
            Run("MergeGeometryComponent", none, (0, points.Select(p => (object)p).Concat(curves).Concat(meshes).ToArray()), (1, 0.1));
            PerComponent["MergeGeometry (FEM-free = ModelHelper)"] = 1;
        }

        // Bridge generators on the simple bridge of the tests (steel I girders, cross members, segments): Section Schedule, Family Property,
        // Bridge FEM Members and Bridge Member Assignment (defaults, schedule, tapers, a generic property with a principal angle).
        var input = SimpleBridgeTests.Input();
        var steelGeometry = BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(input);
        var geometry = h.Goo("BridgeGeometryGoo", steelGeometry);
        var baseSteel = BridgeSegmentSectionsTests.BaseProperty();
        var baseProperty = h.Goo("GH_BeamProperty", baseSteel);
        var schedule = Out(Run("BridgeSectionScheduleComponent", none, (0, gProperty), (1, 0.0), (2, 9000.0), (3, 0), (4, 1), (5, 0)));
        var tapered = Out(Run("BridgeSectionScheduleComponent", none, (0, iProperty), (1, 9000.0), (2, 30000.0), (3, 0), (4, 2), (5, 0), (6, new R.Vector3d(1, 1.2, 0)), (7, new R.Vector3d(1, 1.6, 0))));
        var family = Out(Run("BridgeFamilyPropertyComponent", none, (0, baseProperty), (1, -1)));
        var crossFamily = Out(Run("BridgeFamilyPropertyComponent", none, (0, truss), (1, 1)));
        var fem = Run("BridgeFemMembersComponent", one, (0, geometry), (1, new[] { family, crossFamily }), (2, new[] { schedule, tapered }));
        Run("BridgeFemMembersComponent", one, (0, geometry), (1, new[] { family }));
        Run("BridgeMemberAssignmentComponent", one, (0, geometry), (1, new[] { baseProperty, gProperty }), (2, new[] { 0, -1 }), (3, new[] { tapered }));
        // The curved bridge: section up vectors with a −0 component (the former archive writes a zero without its sign).
        Run("BridgeMemberAssignmentComponent", one, (0, h.Goo("BridgeGeometryGoo", BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input(false, true)))), (1, new[] { baseProperty }));
        // Bridge Segment Sections (the base and a changed table) feeding Bridge FEM Members, then Bridge Segment Override (with and without
        // Preserve Mass) and Bridge Segment Composite.
        var sections = Run("BridgeSegmentSectionsComponent", none, (0, geometry), (1, baseProperty));
        var table = sections[4].Select(g => g.ToString()).ToArray(); table[1] = BridgeSegmentSectionsTests.Edit(table[1], 6, "50"); table[2] = BridgeSegmentSectionsTests.Edit(table[2], 5, "2300");
        sections = Run("BridgeSegmentSectionsComponent", new[] { 1, 2 }, (0, geometry), (1, baseProperty), (2, table));
        var girders = Run("BridgeFemMembersComponent", one, (0, geometry), (1, new[] { family }), (2, sections[0].ToArray()));
        var segmentIds = sections[3].Select(g => g.ToString()).ToArray();
        var replacements = sections[1].ToArray();
        foreach (bool mass in new[] { true, false })
            Run("BridgeSegmentOverrideComponent", new[] { 0, 1 }, (0, girders[0].ToArray()), (1, segmentIds), (2, replacements), (3, mass));
        Run("BridgeSegmentOverrideComponent", new[] { 0, 1 }, (0, girders[0].ToArray()), (1, segmentIds), (2, segmentIds.Select(_ => gProperty).ToArray()), (3, true));
        var composites = segmentIds.Select(id => h.Goo("GH_CompositeBeamProperty", new CompositeBeamPropertyModel(id) { Name = id, SteelSection = baseSteel.Section,
            ConcreteSection = new SectionConcreteSlabModel(250, 3000, 1500, 40), SteelMaterial = new MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, MinimumYieldStress = 355, MinimumTensileStress = 510 },
            ConcreteMaterial = new MaterialModel("C30") { Name = "C30", Modulus = 33000, PoissonRatio = 0.2, SpecificCompressiveStrength = 30 },
            RebarMaterial = new MaterialModel("B450") { Name = "B450", Modulus = 200000, MinimumYieldStress = 450, MinimumTensileStress = 540 }, NInstant = 6.36, NInfinite = 18.0,
            ConsiderConcrete = true, ConsiderRebar = false })).ToArray();
        Run("BridgeSegmentCompositeComponent", new[] { 0, 1 }, (0, girders[0].ToArray()), (1, segmentIds), (2, composites));
        // Bridge Slab Grillage on the combined grillage geometry (transverse strips, unequal stations).
        var grillageInput = input with { DeckBeamStations = new[] { 1234d, 13777, 45001 } };
        var grillage = BridGHe.Common.Geometry.BridgeDeckGrillage.Build(grillageInput, BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(grillageInput));
        var concrete = h.Goo("GH_Material", new MaterialModel("C35") { Name = "C35/45", Kind = MaterialModel.MaterialKind.Concrete, Modulus = 34077, PoissonRatio = 0.2, ShearModulus = 14198.75, Density = 2.5e-9 });
        Run("BridgeSlabGrillageComponent", new[] { 0, 1 }, (0, h.Goo("BridgeGeometryGoo", grillage.Geometry)), (1, concrete), (2, "Soletta"));
        // Connections: rigid links, deformable beams, offsets (both components and every mode of Bridge Connections).
        var links = grillage.Connections.Take(4).Select(c => (object)c.Axis).ToArray();
        Run("BridgeRigidLinksComponent", one, (0, links));
        Run("BridgeConnectionBeamsComponent", one, (0, links), (1, truss));
        Run("BridgeBeamOffsetsComponent", one, (0, fem[0].Take(5).ToArray()), (1, new R.Vector3d(0, -120.5, 0)));
        Run("BridgeConnectionsComponent", one, (0, 0), (1, links));
        Run("BridgeConnectionsComponent", one, (0, 1), (1, links), (2, iProperty));
        Run("BridgeConnectionsComponent", one, (0, 2), (3, new[] { b1, loaded, moved }), (4, new R.Vector3d(15, 25, 0)));

        int refused = Refused() - refusedBefore;
        if (refused != 1) h.Failures.Add("B: expected 1 output refused by the reader and the builder alike (distributed load in a non-global system), got " + refused);
        Console.WriteLine("GPC BUILDERS B RUNS " + string.Join(", ", PerComponent.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + " " + p.Value)) + $"; refused alike {refused}");
        h.Finish(150);
    }

}
#endif
