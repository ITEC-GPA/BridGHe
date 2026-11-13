using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;

/// <summary>
/// R6.6 lane R0: small GPC fixtures built with the product builders (the GPC data the GH components give) and Build Model on GPC
/// (<see cref="LegacyBuildModel.Build"/>), without any FEM model: the cantilever of the GH model contract (<see cref="GhModelGpcChecks"/>, the
/// results file) and the single beam of the Build Model parameters. Identities derived from the name, the length of the cantilever and the step: a
/// rebuild gives the same GUIDs, another length other GUIDs (as the content identities of the components).
/// </summary>
internal static class GpcFixtures
{
    private static Guid Id(string name, string step) => GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r0-fixture:" + name + ":" + step);
    private static Guid Id(string name, double length, string step) => Id(name + ":" + length.ToString("R", System.Globalization.CultureInfo.InvariantCulture), step);

    /// <summary>S355 (E 210000, ν 0.3, Use Poisson, density 7.85E-9, α 1.2E-5): the material of the former cantilever of the GH model tests.</summary>
    internal static GPC.Model.Materials.Material Steel() =>
        GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, UsePoisson = true, Density = 7.85e-9, ThermalExpansion = 1.2e-5 });

    /// <summary>Beam Property «P»: type Beam, solid rectangle B 300, D 500, <see cref="Steel"/>.</summary>
    internal static LegacyBeamProperty RectangleProperty(string name = "P") =>
        GpcPropertyBuilders.Beam(name, 5, GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, 500, 300), Steel());

    /// <summary>The single beam of Build Model parameters: (0,0,0)–(1000,0,0), property <see cref="RectangleProperty"/>, no group, no node.</summary>
    internal static LegacyBeamDraft SingleBeam(string name = "single beam") =>
        GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(1000, 0, 0), RectangleProperty(), null, 0, 0, 0, Id(name, "beam"));

    /// <summary>The inputs of the cantilever: node «1» at the origin, fixed; node «2» at 1000 <paramref name="length"/> along X; one beam of
    /// <see cref="RectangleProperty"/> in the group «Girder» between them, Element ID «B1», super element «G1».</summary>
    internal static (List<LegacyNodeDraft> Nodes, List<LegacyBeamDraft> Beams) CantileverInputs(string name, double length = 1)
    {
        var a = GpcNodeBuilders.WithRestraint(GpcNodeBuilders.Create(0, 0, 0, "1", null, Id(name, length, "node 1")), true, true, true, true, true, true, Id(name, length, "node 1 restraint"));
        var b = GpcNodeBuilders.Create(1000 * length, 0, 0, "2", null, Id(name, length, "node 2"));
        var beam = GpcBeamBuilders.New(a.Node.Position, b.Node.Position, RectangleProperty(), new[] { GpcCaseBuilders.Group("Girder", null) }, 0, 0, 0, Id(name, length, "beam"), "1");
        beam = GpcElementBuilders.WithLabel(beam, "B1", Id(name, length, "beam label"));
        beam = GpcBeamBuilders.WithSuperElement(beam, "G1", Id(name, length, "beam super element"));
        return (new List<LegacyNodeDraft> { a, b }, new List<LegacyBeamDraft> { beam });
    }

    /// <summary>The cantilever as Build Model on GPC gives it (N-mm, tolerance 0.001: the plug-in tolerance without a Rhino document), named
    /// <paramref name="name"/>.</summary>
    internal static GpcModels.Conversion Cantilever(string name = "cantilever", double length = 1)
    {
        var (nodes, beams) = CantileverInputs(name, length);
        return LegacyBuildModel.Build(name, new BridgeUnits(DefinitionUnits.Nmm), 0.001, nodes.Select(d => (d.Node.Guid, d)).ToList(), beams.Select(d => (d.Beam.Guid, d)).ToList(),
            new List<(Guid, LegacyPlateDraft)>(), new List<LegacyCombinationDraft>(), new List<LegacyStageDraft>());
    }
}
