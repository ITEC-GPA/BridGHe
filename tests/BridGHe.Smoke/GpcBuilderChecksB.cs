using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using Grasshopper.Kernel.Types;
using R = Rhino.Geometry;

/// <summary>
/// R6.2, lane B (beams): the runs of the converted producer components (regression <c>gpc-builders-b</c>, harness <see cref="GpcBuilderChecks"/>):
/// one run per component and per input variant the component distinguishes; every output datum of a GH type with a GPC datum carries a datum
/// built on GPC, compared on attach with the conversion of its FEM value (<see cref="GpcDraftCheckHost"/>, a difference fails the run).
/// Variants the reader refuses (and the builder alike) give an output without a datum (<c>refused</c> below).
/// <para>P2CB (13/11/2026, rule U3): the inputs that were FEM values (beam properties, groups, a plate, coordinate systems, composite
/// properties, a concrete) are the GH data given by their GPC datum alone (<c>GH_*.FromGpc</c>, <see cref="R2aDesignFixtures.Goo"/>), with the
/// numbers of the former FEM values; the runs and their checks are unchanged.</para>
/// </summary>
internal static class GpcBuilderChecksB
{
    private static readonly Dictionary<string, int> PerComponent = new Dictionary<string, int>(StringComparer.Ordinal);

    internal static void Regression()
    {
        using var h = new GpcBuilderChecks("B");
        // The components whose load is a load assignment of the beam (the gradient and the pre-load are other assignments, not counted here).
        var beamLoads = new HashSet<string>(StringComparer.Ordinal) { "BeamDistributedLoadComponent", "BeamDistributedMomentComponent", "BeamPointLoadComponent", "BeamPointMomentComponent" };
        static int Loads(object goo) { try { return goo is IGH_Goo g && g.GetType().GetProperty("Gpc")?.GetValue(g) is BridGHe.Common.Gpc.Legacy.LegacyBeamDraft d ? d.Beam.Assignments.Loads.Count : -1; } catch (Exception) { return -1; } }
        List<IGH_Goo>[] Run(string type, int[] attached, params (int, object)[] inputs)
        {
            PerComponent.TryGetValue(type, out int n); PerComponent[type] = n + 1;
            var outputs = h.Run(type, attached, inputs);
            // R1 (explicit, from the inputs): a beam load component gives the beam with one load more than the beam it takes (a point force or moment);
            // a distributed load or moment adds the pieces of its schema (one or more: the patch between a and b, and the parts before and after it).
            if (beamLoads.Contains(type) && inputs.FirstOrDefault(i => i.Item1 == 0).Item2 is IGH_Goo beam && Loads(beam) >= 0 && outputs[0].FirstOrDefault() is { } loaded && Loads(loaded) >= 0)
            {
                bool distributed = type is "BeamDistributedLoadComponent" or "BeamDistributedMomentComponent";
                h.Expect(distributed ? Loads(loaded) >= Loads(beam) + 1 && Loads(loaded) <= Loads(beam) + 3 : Loads(loaded) == Loads(beam) + 1,
                    $"{type}: {Loads(beam)} loads in, {Loads(loaded)} out ({(distributed ? "one to three pieces" : "one")} more expected)");
            }
            return outputs;
        }
        IGH_Goo Out(List<IGH_Goo>[] o, int i = 0) => GpcBuilderChecks.Output(o, i);
        var one = new[] { 0 }; var none = new int[0];
        var refusedField = System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly).GetType("BridGHe.Grasshopper.Helpers.GpcBeamInputs").GetField("Refused", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        int Refused() => (int)refusedField.GetValue(null);
        int refusedBefore = Refused();

        // Inputs: two load cases, groups (nested and plain), properties (I section, explicit section with a principal angle, a truss).
        var dead = Out(Run("LoadCaseComponent", none, (0, "Dead")));
        var live = Out(Run("LoadCaseComponent", none, (0, "Live"), (1, true)));
        var steel = GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, UsePoisson = true, Density = 7.85e-9 });
        var iProperty = R2aDesignFixtures.Property(GpcPropertyBuilders.Beam("PI", 5, GpcSectionBuilders.Standard(BridGHe.Common.Gpc.BridgeSectionKind.I, 500, b1: 300, b2: 100, t1: 20, t2: 10, t3: 5), steel));
        var generic = GpcSectionBuilders.Generic(12000, 4.5e8, 1.2e8, 3e6, 3, -2, 5000, 4000, 15, -40, 0.3);
        var gProperty = R2aDesignFixtures.Property(GpcPropertyBuilders.Beam("PG", 5, generic, steel));
        var truss = R2aDesignFixtures.Property(GpcPropertyBuilders.Beam("PT", 2, GpcSectionBuilders.Standard(BridGHe.Common.Gpc.BridgeSectionKind.SolidRectangle, 100, b: 50), steel));
        var deck = GpcCaseBuilders.Group("Deck", null);
        var nested = R2aDesignFixtures.Goo("GH_Group", GpcCaseBuilders.Group("Segment2", GpcCaseBuilders.Group("Girder1", deck))); var plain = R2aDesignFixtures.Goo("GH_Group", GpcCaseBuilders.Group("Other", null));
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
        // R1 (explicit expected values, from the inputs): a beam on the ends of its line, exactly (N-mm, the units of the drafts), and its BridGHe angle.
        static BridGHe.Common.Gpc.Legacy.LegacyBeamDraft Draft(IGH_Goo g) => (BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)g.GetType().GetProperty("Gpc").GetValue(g);
        static bool At(GPC.Geometry.Point3d p, R.Point3d q) => p.X == q.X && p.Y == q.Y && p.Z == q.Z;
        foreach (var (beam, line, angle) in new[] { (b0, axisX, 0.0), (b1, skew, 17.5), (b2, skew, -40.0), (b3, vertical, 90.0) })
            h.Expect(At(Draft(beam).Beam.StartPoint, line.PointAtStart) && At(Draft(beam).Beam.EndPoint, line.PointAtEnd) && Draft(beam).AngleDeg == angle,
                $"Beam on {line.PointAtStart} - {line.PointAtEnd}, angle {angle}: the draft at {Draft(beam).Beam.StartPoint} - {Draft(beam).Beam.EndPoint}, angle {Draft(beam).AngleDeg:R}");

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
        h.Expect(Draft(se).Bridge?.SuperElement == "SE-A", "Super Element «SE-A»: the bridge data of the draft name " + (Draft(se).Bridge?.SuperElement ?? "none")); // R1
        Run("SuperElementComponent", one, (0, se), (1, "SE-B"));
        Run("SuperElementComponent", one, (0, se), (1, "SE-A"));
        var plate = R2aDesignFixtures.Plate(GpcPlateBuilders.Create(new[] { (0.0, 0.0, 0.0), (1000.0, 0.0, 0.0), (1000.0, 1000.0, 0.0) },
            GpcPropertyBuilders.Plate("PP", 0, steel, 200, 200), null, 0, 0, "", R2aDesignFixtures.Id("builders-b:plate")));
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
        // The coordinate systems by their GPC frames (origin, a point of axis 1, a point of the plane 1-2, as GpcLoadFrames reads the former values).
        var gcs = R2aDesignFixtures.Goo("GH_CoordinateSystem", new GPC.Geometry.CoordinateSystem(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(1, 0, 0), new GPC.Geometry.Point3d(0, 1, 0), "Global"));
        var ocs = R2aDesignFixtures.Goo("GH_CoordinateSystem", new GPC.Geometry.CoordinateSystem(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(0, 1, 0), new GPC.Geometry.Point3d(-1, 0, 0), "Rotated"));
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
        // R1: on the path of P2 the refusal of the builder (the reader's message) is the error of the component, no output (decision 2 of passo 73).
        // P2R (T3): since P2K the component itself refuses the load in a non-global system on both paths (the error, no output; changelog of P2K),
        // the reader no longer meets it: the error expected on both runs.
        h.ExpectError("LegacyNonGlobalLoadCoordinateSystemWasIgnored");
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
        var baseProperty = R2aDesignFixtures.Property(R2aDesignFixtures.BaseProperty()); // the GPC twin of BridgeSegmentSectionsTests.BaseProperty
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
        // The composite properties by their definitions: the steel I of the base property, slab 250 × 3000 with 1500 mm² of top bars at 40 (bottom
        // none, cover 50), S355, C30, B450, n0 6.36, n∞ 18 (the numbers of the former FEM properties).
        var composites = segmentIds.Select(id => R2aDesignFixtures.Composite(GpcCompositeDefinitions.Create(new GpcCompositeDefinitions.Numbers
        {
            Name = id, Height = 2500, Web = 16, TopWidth = 600, TopThickness = 30, BottomWidth = 600, BottomThickness = 30, SlabWidth = 3000, SlabHeight = 250,
            TopArea = 1500, TopCover = 40, BottomArea = 0, BottomCover = 50, Concrete = "C30", Fck = 30, ConcreteE = 33000, Steel = "S355", SteelE = 210000, SteelFy = 355, SteelFu = 510,
            SteelPoisson = 0.3, Rebar = "B450", RebarE = 200000, RebarFy = 450, RebarFu = 540, NShortTerm = 6.36, NLongTerm = 18.0, NShrinkage = 18.0, ConsiderConcrete = true, ConsiderRebar = false,
        }))).ToArray();
        Run("BridgeSegmentCompositeComponent", new[] { 0, 1 }, (0, girders[0].ToArray()), (1, segmentIds), (2, composites));
        // Bridge Slab Grillage on the combined grillage geometry (transverse strips, unequal stations).
        var grillageInput = input with { DeckBeamStations = new[] { 1234d, 13777, 45001 } };
        var grillage = BridGHe.Common.Geometry.BridgeDeckGrillage.Build(grillageInput, BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(grillageInput));
        var concrete = R2aDesignFixtures.MaterialGoo(GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Kind = "Concrete", Name = "C35/45", Modulus = 34077, PoissonRatio = 0.2, ShearModulus = 14198.75, Density = 2.5e-9 }), "Concrete");
        Run("BridgeSlabGrillageComponent", new[] { 0, 1 }, (0, h.Goo("BridgeGeometryGoo", grillage.Geometry)), (1, concrete), (2, "Soletta"));
        // Connections: rigid links, deformable beams, offsets (both components and every mode of Bridge Connections).
        var links = grillage.Connections.Take(4).Select(c => (object)c.Axis).ToArray();
        Run("BridgeRigidLinksComponent", one, (0, links));
        Run("BridgeConnectionBeamsComponent", one, (0, links), (1, truss));
        Run("BridgeBeamOffsetsComponent", one, (0, fem[0].Take(5).ToArray()), (1, new R.Vector3d(0, -120.5, 0)));
        Run("BridgeConnectionsComponent", one, (0, 0), (1, links));
        Run("BridgeConnectionsComponent", one, (0, 1), (1, links), (2, iProperty));
        Run("BridgeConnectionsComponent", one, (0, 2), (3, new[] { b1, loaded, moved }), (4, new R.Vector3d(15, 25, 0)));

        // R6.5 (coordinator, 7/11): Build Model with two GH data of the same beam ID (the FEM model merged them by ID): the same data is kept
        // once, other data with the same ID is an error naming the ID.
        {
            var copyA = Out(Run("ElementIdComponent", one, (0, Out(Run("BeamComponent", one, (0, axisX), (1, iProperty)))), (1, "SAME")));
            var copyB = Out(Run("ElementIdComponent", one, (0, Out(Run("BeamComponent", one, (0, axisX), (1, iProperty)))), (1, "SAME")));
            var otherData = Out(Run("ElementIdComponent", one, (0, Out(Run("BeamComponent", one, (0, axisX), (1, gProperty)))), (1, "SAME")));
            if (copyA.GetType().GetProperty("Guid").GetValue(copyA).Equals(copyB.GetType().GetProperty("Guid").GetValue(copyB))) h.Failures.Add("B same ID: the two copies share a GH GUID");
            var built = Out(Run("BuildModelComponent", none, (0, new[] { copyA, copyB })));
            var conversion = built.GetType().GetProperty("GpcInput").GetValue(built);
            var gpc = (GPC.Model.Models.Model)conversion.GetType().GetProperty("Model").GetValue(conversion);
            if (gpc.BeamElements.Count != 1 || gpc.BeamElements.Values.Single().Name != "SAME") h.Failures.Add("B same ID: identical copies not kept once (" + gpc.BeamElements.Count + " beams)");
            Run("BuildModelComponent", none, (0, new[] { copyA, otherData }));
            h.ExpectError("Beam ID SAME is given by two elements with different data");
        }

        int refused = Refused() - refusedBefore;
        if (refused != 0) h.Failures.Add("B: expected no output refused by the reader and the builder alike (P2K: the component refuses the load in a non-global system itself), got " + refused);
        Console.WriteLine("GPC BUILDERS B RUNS " + string.Join(", ", PerComponent.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + " " + p.Value)) + $"; refused alike {refused}");
        h.Finish(150);
    }

}
