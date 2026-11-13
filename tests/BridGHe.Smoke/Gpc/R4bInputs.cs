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
/// <para><see cref="Regression"/> (<c>--regression r4b-inputs</c>, non-native, <b>transitional</b>: P2 retires it with the FEM models) proves that
/// each GPC input is the conversion of the FEM model the test built before, field by field (<see cref="GpcInputComparison.Require"/>), so the native
/// readbacks of the tests keep their meaning. The 0.3.0 parity model needs the native host (Straus7 resolves its T, C and angle sections): it is
/// compared in <see cref="ParityNative"/>.</para>
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
    // Transitional (P2 retires it with the FEM models): each GPC input against the conversion of the FEM model the test built before.

    /// <summary><c>--regression r4b-inputs</c> (non-native, <b>transitional</b>): each input of this file against the conversion of the FEM model the
    /// native test built before (<see cref="Former"/>), by the legacy reader with the owner of the GPC input (<see cref="GpcInputComparison.Require"/>:
    /// report, bridge data, analysis fingerprint, parts and the canonical XML document, bit for bit). Declared form of the FEM side, as for the
    /// examples of R0 (<see cref="GpcNativeExamples.RequireSameAsFem"/>): the FEM model as Build Model and Attach Moving Paths give it, without the
    /// registered case records the hand-built FEM models add.</summary>
    internal static void Regression()
    {
        var lines = new List<string>();
        void Same(string label, GpcModels.Conversion gpc, BridGHe.Common.Models.ModelModel fem)
        {
            var built = AsBuilt(fem, out int renamed);
            string result = Compare(label, gpc, built);
            lines.Add(label + ": " + result);
            Console.WriteLine("R4B INPUT TRANSITIONAL PASS (" + label + "): the GPC input equals the conversion of the former FEM model; " + result + "; declared: " + renamed + " node IDs as Build Model names them");
        }
        foreach (DefinitionUnits u in new[] { DefinitionUnits.Nmm, DefinitionUnits.Knm, DefinitionUnits.SI, DefinitionUnits.IPS })
            Same("A05/A06 " + u, MassAndHeat(u), Former.MassAndHeat(u));
        Same("A09", Shadowed(), Former.Shadowed());
        Same("beam support, bilateral", BeamSupport(2, false), Former.BeamSupport(2, false));
        Same("beam support, four directions", BeamSupport(4, false), Former.BeamSupport(4, false));
        Same("beam support, contact", BeamSupport(4, true), Former.BeamSupport(4, true));
        Same("bridge phases", PhasesCantilever(Former.Es, Former.AsSteel, Former.IsSteel, Former.I22Steel), Former.PhasesCantilever());
        Same("results file cantilever", ResultsCantilever(), Former.ResultsCantilever());
        Console.WriteLine($"R4B INPUTS TRANSITIONAL PASS: {lines.Count} GPC inputs of the native tests of R4b equal to the conversion of the former FEM models (declared form: no registered case records)");
    }

    /// <summary><see cref="GpcInputComparison.Require"/> of a GPC input against the declared form of a former FEM model (<see cref="AsBuilt"/>: Build
    /// Model, and Attach Moving Paths for a model with paths). Declared: when only the XML documents differ (report, bridge data, analysis
    /// fingerprint and parts equal: Require compares them first), the documents are compared again without the type annotations of the serializer
    /// (<c>pN:type="dNpM:string"</c> and their namespace declarations): the serializer annotates a string the first time it writes a string instance
    /// shared by several fields (here a group name shared by the drafts of the nodes and the beam), which depends on the instances, not on the
    /// values (R3 normalised the same prefixes).</summary>
    internal static string Compare(string label, GpcModels.Conversion gpc, BridGHe.Common.Models.ModelModel built)
    {
        bool buildModel = built.MovingPaths.Count == 0;
        try { return buildModel ? GpcInputComparison.Require(label, gpc, built, Tolerance, buildModel: true) : GpcInputComparison.Require(label, gpc, built, Tolerance); }
        catch (Exception ex) when (ex.Message.Contains("XML document differs"))
        {
            var owner = GpcInputComparison.ElementOwner(gpc.Model);
            var converted = GpcInputComparison.Conversion(built, Tolerance, owner);
            var expected = buildModel ? converted : GPC.Model.Core.ModelValues.Copy(converted, GPC.Model.Core.ModelCopyOptions.None, gpc.Model.Guid);
            // Declared: a beam without BridGHe ID is named by Build Model after its Element ID (the rule of R3); the conversion of a hand-built FEM
            // beam without BridGHe ID leaves its name empty. The name enters no Straus7 readback of the tests.
            int named = 0;
            foreach (var (number, beam) in expected.BeamElements)
                if (string.IsNullOrEmpty(beam.Name) && gpc.Model.BeamElements.TryGetValue(number, out var given) && !string.IsNullOrEmpty(given.Name)) { beam.Name = given.Name; named++; }
            static string Plain(string document) => System.Text.RegularExpressions.Regex.Replace(document ?? "", @"\s+(p\d+:type=""d\d+p\d+:[a-zA-Z]+""|xmlns:p\d+=""http://www\.w3\.org/2001/XMLSchema-instance"")", "");
            string a = Plain(GpcInputComparison.Document(gpc.Model)), b = Plain(GpcInputComparison.Document(expected));
            if (a != b) throw;
            return $"{gpc.Model.NodesElements.Count} nodes, {gpc.Model.BeamElements.Count} beams, {gpc.Model.AreaElements.Count} plates, {gpc.Model.LoadCases.Count} cases, parts equal, XML document equal "
                + $"({a.Length} characters) without the type annotations of shared strings (declared){(named > 0 ? ", " + named + " beam names of Build Model (declared)" : "")}";
        }
    }

    /// <summary>The declared form of a former FEM model: as Build Model and Attach Moving Paths give it (no registered case records; the cases of the
    /// moving paths registered by Attach Moving Paths) and with the node names of Build Model (a node is named by its Element ID, else by a free
    /// number in node order: the node IDs of the former models, «N0», «A0»…, are not Element IDs; none of their nodes has one). The node names
    /// enter no Straus7 readback of the tests (Straus7 numbers the nodes). <paramref name="renamed"/>: the node IDs changed.</summary>
    internal static BridGHe.Common.Models.ModelModel AsBuilt(BridGHe.Common.Models.ModelModel fem, out int renamed)
    {
        var built = BridGHe.Common.Helpers.ModelArchive.Clone(fem); built.LoadCases.Clear(); renamed = 0;
        for (int i = 0; i < built.Nodes.Count; i++)
        {
            string number = (i + 1).ToString(CultureInfo.InvariantCulture);
            if (built.Nodes[i].NodeId != number) { built.Nodes[i].NodeId = number; renamed++; }
        }
        if (built.MovingPaths.Count != 0) { var paths = built.MovingPaths.ToList(); built.MovingPaths.Clear(); built = MovingModelBuilder.ReplacePaths(built, paths); built.Name = fem.Name; }
        return built;
    }

    /// <summary>The FEM models the native tests of R4b built before R6.6 (code of 37a70b0), for the transitional comparison only.</summary>
    internal static class Former
    {
        internal const double Es = 210000, AsSteel = 10600, IsSteel = (200.0 * 300 * 300 * 300 - 190.0 * 260 * 260 * 260) / 12;
        internal static readonly double I22Steel = 2 * 20 * Math.Pow(200, 3) / 12 + 260 * Math.Pow(10, 3) / 12;

        internal static BridGHe.Common.Models.ModelModel MassAndHeat(DefinitionUnits units)
        {
            var femUnits = units switch
            {
                DefinitionUnits.Knm => BridGHe.Common.Models.ModelModel.ModelUnits.Knm, DefinitionUnits.SI => BridGHe.Common.Models.ModelModel.ModelUnits.SI,
                DefinitionUnits.IPS => BridGHe.Common.Models.ModelModel.ModelUnits.IPS, _ => BridGHe.Common.Models.ModelModel.ModelUnits.Nmm,
            };
            var example = NativeExamples.Create().Single(e => e.Id == "03-plate-patch").Model;
            var m = new BridGHe.Common.Models.ModelModel(femUnits) { Name = "a05-a06-" + units };
            foreach (var lc in example.LoadCases) m.LoadCases.Add(lc);
            m.AddElements(example.Nodes.Cast<BridGHe.Common.Models.ElementModel>().Concat(example.Plates));
            var dead = new BridGHe.Common.Models.LoadCaseModel(BridGHe.Common.Models.LoadCaseModel.Dead);
            m.Plates[0].Loads.Add(new BridGHe.Common.Models.PlateNSMassModel(dead, new BridGHe.Common.Models.PlateNSMassModel.LoadData { Mass = Mass[0], DynFactor = Mass[1], Offset = new Rhino.Geometry.Vector3d(Mass[2], Mass[3], Mass[4]) }));
            m.MovingPaths.Add(HeatPath());
            return m;
        }

        internal static BridGHe.Common.Models.ModelModel Shadowed()
        {
            BridGHe.Common.Models.BeamPropertyModel Property(string name, double modulus) => new BridGHe.Common.Models.BeamPropertyModel(name) { Name = name, PropertyType = BridGHe.Common.Models.BeamPropertyType.Beam,
                Material = new BridGHe.Common.Models.MaterialModel("S355") { Name = "S355", Modulus = modulus, PoissonRatio = 0.3 },
                Section = new BridGHe.Common.Models.SectionModel { SectionType = BridGHe.Common.Models.SectionModel.SectionTypes.Generic, SectionArea = 60000, I11 = 450000000, I22 = 200000000, J = 100000000, ShearA1 = 0, ShearA2 = 0 } };
            var points = Enumerable.Range(0, 4).Select(i => new Rhino.Geometry.Point3d(5000 * i, 0, 0)).ToArray();
            var fem = new BridGHe.Common.Models.ModelModel(BridGHe.Common.Models.ModelModel.ModelUnits.Nmm) { Name = "a09" };
            var nodes = points.Select((p, i) => new BridGHe.Common.Models.NodeModel("N" + i, p)).ToArray();
            nodes[0].Attributes.Add(new BridGHe.Common.Models.NodeRestraintAttributeModel(true, true, true, true, true, true));
            var first = Property("Same", 210000); var second = Property("Same", 315000);
            fem.AddElements(nodes);
            fem.AddElements(new BridGHe.Common.Models.ElementModel[] { new BridGHe.Common.Models.BeamModel("B1", points[0], points[1], first) { BeamId = "B1" },
                new BridGHe.Common.Models.BeamModel("B2", points[1], points[2], second) { BeamId = "B2" }, new BridGHe.Common.Models.BeamModel("B3", points[2], points[3], second) { BeamId = "B3" } });
            return fem;
        }

        internal static BridGHe.Common.Models.ModelModel BeamSupport(int supports, bool combinations)
        {
            BridGHe.Common.Models.BeamSupportAttributeModel Make(Support s) => new(new BridGHe.Common.Models.BeamSupportAttributeModel.AttributeData()
            {
                SpringType = BridGHe.Common.Models.BeamSupportAttributeModel.SpringType.Simple, SpringTensionOrientation = BridGHe.Common.Models.BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis,
                LocalAxisDirection = Enum.Parse<BridGHe.Common.Models.BeamSupportAttributeModel.LocalAxisDirection>(s.Direction), Stiffness = s.Stiffness, Gap = s.Gap,
                SimpleSpringType = Enum.Parse<BridGHe.Common.Models.BeamSupportAttributeModel.SimpleSpringTypes>(s.Spring),
            });
            var group = new BridGHe.Common.Models.GroupModel("Beam"); var model = new BridGHe.Common.Models.ModelModel(BridGHe.Common.Models.ModelModel.ModelUnits.Nmm) { Name = "beam-support" };
            var property = new BridGHe.Common.Models.BeamPropertyModel("P") { Name = "Beam", PropertyType = BridGHe.Common.Models.BeamPropertyType.Beam, Material = new BridGHe.Common.Models.MaterialModel("S") { Name = "S355", Modulus = 210000, PoissonRatio = .3 },
                Section = new BridGHe.Common.Models.SectionModel { SectionType = BridGHe.Common.Models.SectionModel.SectionTypes.Generic, SectionArea = 10000, I11 = 1e8, I22 = 1e8, J = 1e7 } };
            var q = new BridGHe.Common.Models.LoadCaseModel("Q", "Q"); var r = new BridGHe.Common.Models.LoadCaseModel("R", "R"); model.LoadCases.Add(q); model.LoadCases.Add(r);
            for (int i = 0; i < 2; i++)
            {
                var node = new BridGHe.Common.Models.NodeModel((i + 1).ToString(), new Rhino.Geometry.Point3d(i * 1000, 0, 0)); node.Groups.Add(group);
                node.Attributes.Add(new BridGHe.Common.Models.NodeRestraintAttributeModel(true, false, false, true, true, true));
                model.AddElements(new[] { node });
            }
            var beam = new BridGHe.Common.Models.BeamModel(model.Nodes[0].Position, model.Nodes[1].Position, property, group) { BeamId = "1" }; model.AddElements(new[] { beam });
            foreach (var entry in new[] { (Case: q, Factor: 1.0), (Case: r, Factor: -.5) })
                foreach (var load in new[] { (Direction: BridGHe.Common.Models.BeamDistributedLoadModel.LoadDirection.Y, Value: 2.0), (Direction: BridGHe.Common.Models.BeamDistributedLoadModel.LoadDirection.Z, Value: 4.0) })
                    beam.Loads.Add(new BridGHe.Common.Models.BeamDistributedLoadModel(entry.Case, new() { LoadSchema = BridGHe.Common.Models.BeamDistributedLoadModel.LoadSchema.Uniform, CoordinateSystem = BridGHe.Common.Models.CoordinateSystemModel.Global,
                        LoadDirection = load.Direction, PA = load.Value * entry.Factor, PB = load.Value * entry.Factor }, BridGHe.Common.Models.BeamDistributedLoadModel.LoadType.Force));
            foreach (var s in Supports.Take(supports)) beam.Attributes.Add(Make(s));
            if (combinations)
            {
                var positive = new BridGHe.Common.Models.LoadCombinationModel("Positive"); positive.Values.Add(q, 1);
                var negative = new BridGHe.Common.Models.LoadCombinationModel("Negative"); negative.Values.Add(r, 1); model.AddLoadCombinations(new[] { positive, negative });
            }
            return model;
        }

        internal static BridGHe.Common.Models.ModelModel PhasesCantilever()
        {
            var model = new BridGHe.Common.Models.ModelModel(BridGHe.Common.Models.ModelModel.ModelUnits.Nmm) { Name = "bridge-phases" };
            var property = new BridGHe.Common.Models.BeamPropertyModel("1") { Name = "H 300", Material = new BridGHe.Common.Models.MaterialModel("S355") { Name = "S355", Modulus = Es, PoissonRatio = .3 }, PropertyType = BridGHe.Common.Models.BeamPropertyType.Beam,
                Section = new BridGHe.Common.Models.SectionModel { SectionType = BridGHe.Common.Models.SectionModel.SectionTypes.Generic, SectionArea = AsSteel, I11 = IsSteel, I22 = I22Steel, J = 1e6 } };
            var first = new BridGHe.Common.Models.NodeModel("1", Rhino.Geometry.Point3d.Origin); first.Attributes.Add(new BridGHe.Common.Models.NodeRestraintAttributeModel(true, true, true, true, true, true));
            var last = new BridGHe.Common.Models.NodeModel("2", new Rhino.Geometry.Point3d(3000, 0, 0)); var beam = new BridGHe.Common.Models.BeamModel(first.Position, last.Position, property) { BeamId = "1" };
            var phases = new[] { BridGHe.Common.Models.LoadCaseModel.Stage.BridgePhases.OnlySteel, BridGHe.Common.Models.LoadCaseModel.Stage.BridgePhases.NInfinite, BridGHe.Common.Models.LoadCaseModel.Stage.BridgePhases.NInstant };
            var cases = phases.Select(p => new BridGHe.Common.Models.LoadCaseModel(p.ToString(), p.ToString()) { Stages = new() { new BridGHe.Common.Models.LoadCaseModel.Stage(p.ToString()) { BridgePhase = p } } }).ToArray();
            for (int i = 0; i < 3; i++) { model.LoadCases.Add(cases[i]); last.Loads.Add(new BridGHe.Common.Models.NodeMomentLoadModel(cases[i], new Rhino.Geometry.Vector3d(0, -(i + 1) * 1e6, 0))); }
            model.AddElements(new BridGHe.Common.Models.ElementModel[] { first, last, beam });
            return model;
        }

        internal static BridGHe.Common.Models.ModelModel ResultsCantilever()
        {
            var loadCase = new BridGHe.Common.Models.LoadCaseModel("P", "P");
            var material = new BridGHe.Common.Models.MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3 };
            var section = new BridGHe.Common.Models.SectionModel { SectionType = BridGHe.Common.Models.SectionModel.SectionTypes.Generic, SectionArea = 60000, I11 = 450000000, I22 = 200000000, J = 100000000, ShearA1 = 0, ShearA2 = 0 };
            var property = new BridGHe.Common.Models.BeamPropertyModel("P") { Name = "P", Material = material, Section = section, PropertyType = BridGHe.Common.Models.BeamPropertyType.Beam };
            var first = new BridGHe.Common.Models.NodeModel("N1", Rhino.Geometry.Point3d.Origin); first.Attributes.Add(new BridGHe.Common.Models.NodeRestraintAttributeModel(true, true, true, true, true, true));
            var end = new BridGHe.Common.Models.NodeModel("N2", new Rhino.Geometry.Point3d(3000, 0, 0)); end.Loads.Add(new BridGHe.Common.Models.NodeForceLoadModel(loadCase, new Rhino.Geometry.Vector3d(0, 0, -1000)));
            var beam = new BridGHe.Common.Models.BeamModel("1", first.Position, end.Position, property);
            var input = new BridGHe.Common.Models.ModelModel(BridGHe.Common.Models.ModelModel.ModelUnits.Nmm) { Name = "cantilever" };
            input.LoadCases.Add(loadCase); input.AddElements(new BridGHe.Common.Models.ElementModel[] { first, end, beam });
            return input;
        }

        /// <summary>The parity model of <see cref="ParityNative"/> before R6.6 (code of 37a70b0): the FEM properties, the cantilevers and the shells.</summary>
        internal static BridGHe.Common.Models.ModelModel Parity()
        {
            var steel = new BridGHe.Common.Models.MaterialModel("S355") { Name = "S355", Kind = BridGHe.Common.Models.MaterialModel.MaterialKind.Steel, Modulus = 210000, PoissonRatio = 0.3, Density = 7.85e-9, ThermalExpansion = 1.2e-5 };
            BridGHe.Common.Models.BeamPropertyModel Property(string name, BridGHe.Common.Models.SectionModel section, BridGHe.Common.Models.BeamPropertyType type = BridGHe.Common.Models.BeamPropertyType.Beam, BridGHe.Common.Models.MaterialModel material = null, bool calculated = false)
            {
                if (!calculated) section.CalculateSection();
                var copy = new BridGHe.Common.Models.SectionModel { SectionType = section.SectionType, D = section.D, B = section.B, B1 = section.B1, B2 = section.B2, L = section.L, T = section.T, T1 = section.T1,
                    T2 = section.T2, T3 = section.T3, GenericShapes = section.GenericShapes, Mirror = section.Mirror, MirrorGapA = section.MirrorGapA, MirrorGapB = section.MirrorGapB };
                copy.CalculateSection();
                copy.I11 = section.I11; copy.I22 = section.I22; copy.W11 = section.W11; copy.W22 = section.W22; copy.SectionArea = section.SectionArea; copy.J = section.J; copy.Centroid = section.Centroid;
                return new BridGHe.Common.Models.BeamPropertyModel(name) { Name = name, Material = material ?? steel, Section = copy, PropertyType = type };
            }
            BridGHe.Common.Models.SectionModel.SectionTypes Type(BridgeSectionKind k) => k == BridgeSectionKind.T ? BridGHe.Common.Models.SectionModel.SectionTypes.T
                : k == BridgeSectionKind.C ? BridGHe.Common.Models.SectionModel.SectionTypes.C : BridGHe.Common.Models.SectionModel.SectionTypes.Angle;
            var properties = Standards.Select(s =>
            {
                var model = new BridGHe.Common.Models.SectionModel { SectionType = Type(s.Kind), B = s.B, T1 = s.T1, D = s.D, T2 = s.T2 };
                bool calculated = s.Double && s.Kind == BridgeSectionKind.C;
                if (calculated) model.CalculateSection();
                if (s.Double) { model.Mirror = BridGHe.Common.Models.SectionModel.MirrorTypes.Left; model.MirrorGapA = s.Gap; }
                return Property(s.Name, model, calculated: calculated);
            }).ToList();
            var fill = new GPC.Geometry.Polygon2d(); fill.Add(0, 0); fill.Add(200, 0); fill.Add(200, 400); fill.Add(0, 400);
            var hole = new GPC.Geometry.Polygon2d(); hole.Add(50, 50); hole.Add(150, 50); hole.Add(150, 350); hole.Add(50, 350);
            properties.Add(Property("PAR Brep", new BridGHe.Common.Models.SectionModel { SectionType = BridGHe.Common.Models.SectionModel.SectionTypes.GenericShapes, GenericShapes = new BridGHe.Common.Geometry.Shapes(new[] { new GPC.Geometry.Shape2d(fill, new[] { hole }) }) }));
            var cutoff = Property("PAR Cutoff", new BridGHe.Common.Models.SectionModel { SectionType = BridGHe.Common.Models.SectionModel.SectionTypes.I, D = 400, B1 = 200, B2 = 200, T1 = 15, T2 = 15, T3 = 10 }, BridGHe.Common.Models.BeamPropertyType.CutoffBar);
            cutoff.MaxTension = 1.5e5; cutoff.MaxCompression = 2.5e5; cutoff.CutoffType = BridGHe.Common.Models.CutoffType.Ductile; cutoff.KeepMass = true;
            properties.Add(cutoff);
            var tableSteel = new BridGHe.Common.Models.MaterialModel("S235 table") { Name = "S235 table", Modulus = 210000, PoissonRatio = 0.3, Density = 7.85e-9, StressStrainTable = Table.ToArray() };
            var shell = new BridGHe.Common.Models.PlatePropertyModel("1") { Name = "PAR Shell", Material = tableSteel, PropertyType = BridGHe.Common.Models.PlatePropertyType.ShellThin, MembraneThickness = 20, BendingThickness = 20 };
            var tableOnly = new BridGHe.Common.Models.MaterialModel("PAR Table") { Name = "PAR Table", Modulus = 0, PoissonRatio = 0.3, StressStrainTable = Table.ToArray() };
            var tableShell = new BridGHe.Common.Models.PlatePropertyModel("2") { Name = "PAR Table shell", Material = tableOnly, PropertyType = BridGHe.Common.Models.PlatePropertyType.ShellThin, MembraneThickness = 20, BendingThickness = 20 };
            properties.Add(Property("PAR Table beam", new BridGHe.Common.Models.SectionModel { SectionType = BridGHe.Common.Models.SectionModel.SectionTypes.SolidRectangle, B = 100, D = 200 }, material: tableOnly));
            var lc = new BridGHe.Common.Models.LoadCaseModel("P", "P");
            var input = new BridGHe.Common.Models.ModelModel(BridGHe.Common.Models.ModelModel.ModelUnits.Nmm) { Name = "parity" }; input.LoadCases.Add(lc);
            var elements = new List<BridGHe.Common.Models.ElementModel>();
            for (int k = 0; k < properties.Count; k++)
            {
                var a = new BridGHe.Common.Models.NodeModel("A" + k, new Rhino.Geometry.Point3d(0, 2000 * k, 0)); a.Attributes.Add(new BridGHe.Common.Models.NodeRestraintAttributeModel(true, true, true, true, true, true));
                var b = new BridGHe.Common.Models.NodeModel("B" + k, new Rhino.Geometry.Point3d(3000, 2000 * k, 0)); b.Loads.Add(new BridGHe.Common.Models.NodeForceLoadModel(lc, new Rhino.Geometry.Vector3d(1000, 0, -1000)));
                var beamModel = new BridGHe.Common.Models.BeamModel((k + 1).ToString(CultureInfo.InvariantCulture), a.Position, b.Position, properties[k]);
                if (properties[k].Name == "PAR 2L") { beamModel.AngleDeg = 30; beamModel.Offset = new Rhino.Geometry.Vector2d(5, 7); }
                elements.Add(a); elements.Add(b); elements.Add(beamModel);
            }
            void Shell(string prefix, double y, BridGHe.Common.Models.PlatePropertyModel property, string plateId)
            {
                var corners = new[] { new Rhino.Geometry.Point3d(0, y, 0), new Rhino.Geometry.Point3d(1000, y, 0), new Rhino.Geometry.Point3d(1000, y + 1000, 0), new Rhino.Geometry.Point3d(0, y + 1000, 0) };
                for (int k = 0; k < 4; k++) { var n = new BridGHe.Common.Models.NodeModel(prefix + k, corners[k]); n.Attributes.Add(new BridGHe.Common.Models.NodeRestraintAttributeModel(true, true, true, true, true, true)); elements.Add(n); }
                elements.Add(new BridGHe.Common.Models.PlateModel(plateId, corners, property));
            }
            Shell("S", -3000, shell, "1"); Shell("T", -6000, tableShell, "2");
            input.AddElements(elements.ToArray());
            return input;
        }

        /// <summary>The parity model (native host: Straus7 resolves its sections) against the conversion of the former FEM model, for the
        /// transitional line of <see cref="ParityNative"/>; the difference as text, null when equal.</summary>
        internal static string ParityDifference(GpcModels.Conversion gpc)
        {
            try
            {
                var built = AsBuilt(Parity(), out int renamed);
                return "equal: " + Compare("parity", gpc, built) + "; declared: " + renamed + " node IDs as Build Model names them";
            }
            catch (Exception ex) { return "DIFFERENT: " + ex.GetBaseException().Message; }
        }
    }
}
