using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.MovingLoads;

/// <summary>
/// R6.6 lane R4b: the inputs of the native tests of the lane (A05/A06 in four units, A09, the 0.3.0 parity model, the beam supports, the bridge
/// phases and the results file) built as GPC data, without any FEM model: the product builders (the GPC data the GH components give) assembled by
/// Build Model on GPC (<see cref="LegacyBuildModel.Build"/>) and, with moving paths, Attach Moving Paths on GPC
/// (<see cref="GpcModelTransforms.ReplacePaths"/>). The same geometry, properties, groups, loads, cases, combinations and moving data as the FEM
/// models the tests built before; identities derived from the input and the step (a rebuild gives the same GUIDs).
/// <para>The transitional comparison with the former FEM models (<c>--regression r4b-inputs</c>, and the line of <see cref="ParityNative"/>) is
/// retired by name with the FEM models (P2CUT and P2CB, rule U3): the natives compare their GPC results with the captures.</para>
/// </summary>
internal static class R4bInputs
{
    /// <summary>The model tolerance of the former tests (definition units).</summary>
    internal const double Tolerance = 1e-6;

    internal static Guid Id(string tag) => GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r4b:" + tag);

    /// <summary>Build Model on GPC of the data of a builder, in the units given.</summary>
    internal static GpcModels.Conversion Build(GpcNativeExamples.Builder b, string name, DefinitionUnits units = DefinitionUnits.Nmm) =>
        LegacyBuildModel.Build(name, new BridgeUnits(units), Tolerance, b.Nodes.Select(d => (d.Node.Guid, d)).ToList(), b.Beams.Select(d => (d.Beam.Guid, d)).ToList(),
            b.Plates.Select(d => (d.Plate.Guid, d)).ToList(), b.Combinations.ToList(), b.Stages.ToList());

    /// <summary>A beam between two nodes of the builder, with the BridGHe ID, the Element ID (label), the BridGHe angle and offset given.</summary>
    internal static int Beam(GpcNativeExamples.Builder b, int from, int to, LegacyBeamProperty property, IReadOnlyList<string> group, string beamId, string label = null,
        double angleDeg = 0, double offsetX = 0, double offsetY = 0)
    {
        var beam = GpcBeamBuilders.New(b.Nodes[from].Node.Position, b.Nodes[to].Node.Position, property, group == null ? null : new[] { group }, angleDeg, offsetX, offsetY, b.Next("beam"), beamId);
        if (label != null) beam = GpcElementBuilders.WithLabel(beam, label, b.Next("label"));
        b.Beams.Add(beam);
        return b.Beams.Count - 1;
    }

    /// <summary>The material of the former tests <c>MaterialModel(name) { Modulus, PoissonRatio = 0.3 }</c> (no kind, Use Poisson).</summary>
    internal static GPC.Model.Materials.Material Elastic(string name, double modulus) =>
        GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Name = name, Modulus = modulus, PoissonRatio = 0.3 });

    /// <summary>A generic beam property (type Beam) of the former tests.</summary>
    internal static LegacyBeamProperty Generic(string name, GPC.Model.Materials.Material material, double area, double i11, double i22, double j) =>
        GpcPropertyBuilders.Beam(name, 5, GpcSectionBuilders.Generic(area, i11, i22, j, 0, 0, 0, 0, 0, 0, 0), material);

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // A05/A06 (GpcOnlyAnalysisNative): the plate of example 03 with a non-structural mass in «Dead» and a moving path with a heat source.

    internal static readonly double[] Mass = { 0.2, 1.1, 0.01, -0.02, 0.03 }, Heat = { 0.1, 0.2, 0.3, 0.4, 5 };

    /// <summary>The moving path of A06: «Traffic», 5 divisions, target 2, along X from the origin; one vehicle «H» with a point force of −1 along Z
    /// and the heat source of <see cref="Heat"/> (X, Y, size X, size Y, heat), in the units of the definition.</summary>
    internal static MovingPath HeatPath() => new MovingPath
    {
        LoadCaseName = "Traffic", Divisions = 5, Target = 2, Coordinates = new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
        Template = new MovingTemplate { Name = "Heat", Vehicles = new[] { new MovingVehicle { Name = "H", Loads = new MovingObject[] { new MovingPointForce { Force = new double[] { 0, 0, -1 } },
            new MovingHeatSource { X = Heat[0], Y = Heat[1], SizeX = Heat[2], SizeY = Heat[3], Heat = Heat[4] } } } } },
    };

    /// <summary>A05/A06 in the units of a definition: the nodes, restraints, tension forces and plate of example 03 with the same numbers (the
    /// geometry is not what is compared), the non-structural mass of <see cref="Mass"/> in «Dead», then the path of <see cref="HeatPath"/>.</summary>
    internal static GpcModels.Conversion MassAndHeat(DefinitionUnits units)
    {
        var b = new GpcNativeExamples.Builder("r4b-a05-a06-" + units);
        var points = new[] { (0.0, 0.0, 0.0), (1000.0, 0.0, 0.0), (1000.0, 500.0, 0.0), (0.0, 500.0, 0.0) };
        for (int i = 0; i < 4; i++)
        {
            int n = b.Node(points[i].Item1, points[i].Item2, points[i].Item3, (i + 1).ToString(CultureInfo.InvariantCulture), null);
            b.Restrain(n, i == 0 || i == 3, i == 0, true, true, true, true);
            if (i == 1 || i == 2) b.Force(n, "Tension", 1000, 0, 0);
        }
        var plate = GpcPlateBuilders.Create(points, GpcPropertyBuilders.Plate("Shell patch", 0, GpcNativeExamples.Steel(), 10, 10), null, 0, 0, "1", b.Next("plate"));
        b.Plates.Add(GpcPlateBuilders.WithNonStructuralMass(plate, b.Case(GpcCaseBuilders.Dead), Mass[0], Mass[1], Mass[2], Mass[3], Mass[4], b.Next("mass")));
        var built = Build(b, "a05-a06-" + units, units);
        return GpcModelTransforms.ReplacePaths(built, new[] { HeatPath() }, Id("a05-a06-paths:" + units));
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // A09 (GpcOnlyAnalysisNative): Bridge Phase Model with properties of the same name.

    /// <summary>A09: four nodes 5000 mm apart along X (the first fixed), beams B1 on «Same» E 210000 and B2, B3 on «Same» E 315000 (generic
    /// section A 60000, I11 4.5E8, I22 2E8, J 1E8; S355 ν 0.3); the BridGHe IDs and Element IDs B1–B3.</summary>
    internal static GpcModels.Conversion Shadowed()
    {
        var b = new GpcNativeExamples.Builder("r4b-a09");
        for (int i = 0; i < 4; i++) b.Node(5000 * i, 0, 0, "N" + i.ToString(CultureInfo.InvariantCulture), null);
        b.Restrain(0, true, true, true, true, true, true);
        var first = ShadowedProperty("Same", 210000); var second = ShadowedProperty("Same", 315000);
        Beam(b, 0, 1, first, null, "B1", "B1"); Beam(b, 1, 2, second, null, "B2", "B2"); Beam(b, 2, 3, second, null, "B3", "B3");
        return Build(b, "a09");
    }

    internal static LegacyBeamProperty ShadowedProperty(string name, double modulus) => Generic(name, Elastic("S355", modulus), 60000, 450000000, 200000000, 100000000);

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // ParityNative: the sections, cutoff bar and tables 0.3.0 wrote.

    /// <summary>A standard section of the T, C and Angle components of 0.3.0 (inputs B, T1, D, T2; Double Sections and Gap for the double C and angle)
    /// with the native Straus7 type of 0.3.0.</summary>
    internal sealed record Standard(string Name, BridgeSectionKind Kind, double B, double T1, double D, double T2, int Native, bool Double, double Gap);

    internal static readonly Standard[] Standards =
    {
        new("PAR T", BridgeSectionKind.T, 200, 20, 300, 10, 8, false, 0),
        new("PAR C", BridgeSectionKind.C, 100, 10, 300, 8, 5, false, 0),
        new("PAR L", BridgeSectionKind.Angle, 150, 10, 100, 12, 9, false, 0),
        new("PAR 2C", BridgeSectionKind.C, 100, 10, 300, 8, 5, true, 12),
        new("PAR 2L", BridgeSectionKind.Angle, 150, 10, 100, 12, 9, true, 8),
    };

    /// <summary>The stress-strain table of A04 (stress, strain).</summary>
    internal static readonly (double stress, double strain)[] Table = { (0.0, 0.0), (235.0, 235.0 / 210000), (360.0, 0.15) };

    /// <summary>S355 of the parity model: steel, E 210000, ν 0.3, density 7.85E-9, α 1.2E-5.</summary>
    internal static GPC.Model.Materials.Material ParitySteel() => GpcMaterialBuilders.Material(new LegacyMaterialRecipe
        { Kind = "Steel", Name = "S355", Modulus = 210000, PoissonRatio = 0.3, Density = 7.85e-9, ThermalExpansion = 1.2e-5 });

    /// <summary>The properties of the parity model, in the order of its cantilevers: the five standard sections, the Brep section with a hole, the
    /// cutoff bar, the beam of a Table Material material.</summary>
    internal static List<LegacyBeamProperty> ParityProperties()
    {
        var steel = ParitySteel();
        var properties = Standards.Select(s => GpcPropertyBuilders.Beam(s.Name, 5, GpcSectionBuilders.Standard(s.Kind, s.D, b: s.B, t1: s.T1, t2: s.T2,
            mirror: s.Double ? 2 : LegacySectionRecipe.NoMirror, gapA: s.Gap), steel, "Steel")).ToList();
        // A02c: a Brep section with a hole (the shapes of the Brep component).
        var fill = new GPC.Geometry.Polygon2d(); fill.Add(0, 0); fill.Add(200, 0); fill.Add(200, 400); fill.Add(0, 400);
        var hole = new GPC.Geometry.Polygon2d(); hole.Add(50, 50); hole.Add(150, 50); hole.Add(150, 350); hole.Add(50, 350);
        properties.Add(GpcPropertyBuilders.Beam("PAR Brep", 5, GpcSectionBuilders.Shapes(new[] { new GPC.Geometry.Shape2d(fill, new[] { hole }) }), steel, "Steel"));
        // A01: a cutoff bar on an I section: ductile, keep mass, the two limits.
        properties.Add(GpcPropertyBuilders.Beam("PAR Cutoff", 3, GpcSectionBuilders.Standard(BridgeSectionKind.I, 400, b1: 200, b2: 200, t1: 15, t2: 15, t3: 10), steel, "Steel", (1.5e5, 2.5e5, 1, true)));
        // A04 (LIB): a material of the Table Material component (no E, the table) on a solid rectangle 100 x 200.
        properties.Add(GpcPropertyBuilders.Beam("PAR Table beam", 5, GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, 200, b: 100), TableOnly()));
        return properties;
    }

    /// <summary>A04: S235 with E 210000, ν 0.3, density 7.85E-9 and the table; A04 (LIB): the Table Material material, E 0, ν 0.3 and the table.</summary>
    internal static GPC.Model.Materials.Material TableSteel() => GpcMaterialBuilders.Material(new LegacyMaterialRecipe
        { Name = "S235 table", Modulus = 210000, PoissonRatio = 0.3, Density = 7.85e-9, StressStrainTable = Table.ToArray() });
    internal static GPC.Model.Materials.Material TableOnly() => GpcMaterialBuilders.Material(new LegacyMaterialRecipe
        { Name = "PAR Table", Modulus = 0, PoissonRatio = 0.3, StressStrainTable = Table.ToArray() });

    /// <summary>The parity model: one cantilever of 3000 mm along X per property (fixed at A k, 1000 N along X and −1000 N along Z in «P» at B k,
    /// 2000 mm apart along Y; the double angle with the BridGHe angle 30° and the offset 5, 7), Element IDs 1..n; two restrained thin shells of
    /// 1000 × 1000 mm, 20 mm, «PAR Shell» on S235 with the table and «PAR Table shell» on the Table Material material.</summary>
    internal static GpcModels.Conversion Parity(IReadOnlyList<LegacyBeamProperty> properties)
    {
        var b = new GpcNativeExamples.Builder("r4b-parity");
        for (int k = 0; k < properties.Count; k++)
        {
            int a = b.Node(0, 2000 * k, 0, "A" + k.ToString(CultureInfo.InvariantCulture), null); b.Restrain(a, true, true, true, true, true, true);
            int e = b.Node(3000, 2000 * k, 0, "B" + k.ToString(CultureInfo.InvariantCulture), null); b.Force(e, "P", 1000, 0, -1000);
            bool doubleAngle = properties[k].Property.Name == "PAR 2L";
            Beam(b, a, e, properties[k], null, "", (k + 1).ToString(CultureInfo.InvariantCulture), doubleAngle ? 30 : 0, doubleAngle ? 5 : 0, doubleAngle ? 7 : 0);
        }
        void Shell(string prefix, double y, string name, GPC.Model.Materials.Material material, string plateId)
        {
            var corners = new[] { (0.0, y, 0.0), (1000.0, y, 0.0), (1000.0, y + 1000, 0.0), (0.0, y + 1000, 0.0) };
            for (int k = 0; k < 4; k++) { int n = b.Node(corners[k].Item1, corners[k].Item2, corners[k].Item3, prefix + k.ToString(CultureInfo.InvariantCulture), null); b.Restrain(n, true, true, true, true, true, true); }
            b.Plates.Add(GpcPlateBuilders.Create(corners, GpcPropertyBuilders.Plate(name, 0, material, 20, 20), null, 0, 0, plateId, b.Next("plate")));
        }
        Shell("S", -3000, "PAR Shell", TableSteel(), "1");
        Shell("T", -6000, "PAR Table shell", TableOnly(), "2");
        return Build(b, "parity");
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // NativeBeamSupportTests.

    /// <summary>A Beam Support datum: stiffness, gap, the former direction code and spring type.</summary>
    internal sealed record Support(double Stiffness, double Gap, string Direction, string Spring);

    /// <summary>The bilateral supports (+1 axis k 10, −2 axis k 20), then the two contacts (−1 axis k 30 gap 0.04, +2 axis k 40 gap 0.05, compression only).</summary>
    internal static readonly Support[] Supports =
    {
        new(10, 0, "positive1Axis", "TensionCompression"), new(20, 0, "negative2Axis", "TensionCompression"),
        new(30, .04, "negative1Axis", "CompressionOnly"), new(40, .05, "positive2Axis", "CompressionOnly"),
    };

    /// <summary>The beam of the support tests: nodes 1 (0,0,0) and 2 (1000,0,0) in the group «Beam», each restrained along X and in rotation; one
    /// beam (BridGHe ID 1, group «Beam») on «Beam» (generic A 10000, I11 1E8, I22 1E8, J 1E7; S355 E 210000 ν 0.3); in Q the uniform global forces
    /// 2 along Y and 4 along Z, in R the same times −0.5; the first <paramref name="supports"/> of <see cref="Supports"/>; with
    /// <paramref name="combinations"/> «Positive» (1 Q) and «Negative» (1 R).</summary>
    internal static GpcModels.Conversion BeamSupport(int supports, bool combinations)
    {
        var b = new GpcNativeExamples.Builder("r4b-beam-support");
        var group = GpcCaseBuilders.Group("Beam", null);
        for (int i = 0; i < 2; i++) { int n = b.Node(i * 1000, 0, 0, (i + 1).ToString(CultureInfo.InvariantCulture), group); b.Restrain(n, true, false, false, true, true, true); }
        int beam = Beam(b, 0, 1, Generic("Beam", Elastic("S355", 210000), 10000, 1e8, 1e8, 1e7), group, "1");
        foreach (var (name, factor) in new[] { ("Q", 1.0), ("R", -.5) })
            foreach (var (direction, value) in new[] { ("Y", 2.0), ("Z", 4.0) })
                b.Beams[beam] = GpcBeamBuilders.WithDistributedLoad(b.Beams[beam], b.Case(name), BridgeLoadSchema.Uniform, false, direction, value * factor, value * factor, 0, 0, 0, 0, true, b.Next("distributed"));
        foreach (var s in Supports.Take(supports))
            b.Beams[beam] = GpcBeamBuilders.WithSupport(b.Beams[beam], s.Stiffness, s.Gap, s.Direction, s.Spring, b.Next("support"));
        if (combinations) { b.Combinations.Add(b.Combination("Positive", ("Q", 1))); b.Combinations.Add(b.Combination("Negative", ("R", 1))); }
        return Build(b, "beam-support");
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // NativeBridgePhases.

    /// <summary>The phase cases of the bridge phase test, in order (the former BridgePhases of their one stage).</summary>
    internal static readonly (string Name, BridGHe.Common.Checks.BridgeCasePhase Phase)[] PhaseCases =
    {
        ("OnlySteel", BridGHe.Common.Checks.BridgeCasePhase.OnlySteel), ("NInfinite", BridGHe.Common.Checks.BridgeCasePhase.NInfinite),
        ("NInstant", BridGHe.Common.Checks.BridgeCasePhase.NInstant), ("StrainInfinite", BridGHe.Common.Checks.BridgeCasePhase.StrainInfinite),
        ("StrainInstant", BridGHe.Common.Checks.BridgeCasePhase.StrainInstant),
    };

    /// <summary>The cantilever of the bridge phase test: node 1 fixed at the origin, node 2 at 3000 mm along X with the moments −1E6, −2E6, −3E6 N mm
    /// about Y in the first three phase cases; one beam (BridGHe ID 1) on «H 300» (generic: the steel H 300 of the test, S355 E <paramref name="es"/>
    /// ν 0.3).</summary>
    internal static GpcModels.Conversion PhasesCantilever(double es, double area, double i11, double i22)
    {
        var b = new GpcNativeExamples.Builder("r4b-bridge-phases");
        int first = b.Node(0, 0, 0, "1", null); b.Restrain(first, true, true, true, true, true, true);
        int last = b.Node(3000, 0, 0, "2", null);
        for (int i = 0; i < 3; i++) b.Nodes[last] = GpcNodeBuilders.WithMoment(b.Nodes[last], b.Case(PhaseCases[i].Name), 0, -(i + 1) * 1e6, 0, b.Next("moment"));
        Beam(b, first, last, Generic("H 300", Elastic("S355", es), area, i11, i22, 1e6), null, "1");
        return Build(b, "bridge-phases");
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // GpcResultsFileNative.

    /// <summary>The cantilever of the results file test: node N1 fixed at the origin, node N2 at 3000 mm along X with −1000 N along Z in «P»; one beam
    /// (Element ID 1) on «P» (generic A 60000, I11 4.5E8, I22 2E8, J 1E8; S355 E 210000 ν 0.3); named «cantilever».</summary>
    internal static GpcModels.Conversion ResultsCantilever()
    {
        var b = new GpcNativeExamples.Builder("r4b-results-cantilever");
        int first = b.Node(0, 0, 0, "N1", null); b.Restrain(first, true, true, true, true, true, true);
        int end = b.Node(3000, 0, 0, "N2", null); b.Force(end, "P", 0, 0, -1000);
        Beam(b, first, end, Generic("P", Elastic("S355", 210000), 60000, 450000000, 200000000, 100000000), null, "", "1");
        return Build(b, "cantilever");
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // Retired (P2CB and P2CD 13/11/2026, rule U3): the transitional comparison with the former FEM models (Regression, Compare, AsBuilt, Former)
    // went with the FEM types; --regression r4b-inputs was retired by P2CUT, the line of ParityNative by P2CB (P2rRetired.Checks).

}
