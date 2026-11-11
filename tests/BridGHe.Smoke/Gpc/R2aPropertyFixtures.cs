#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.ElementProperties;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.6 lane R2a (property tests): the GPC twins of the property fixtures of <c>GetBeamPropertyTests</c> and <c>BeamPropertyOverrideTests</c>
/// (the steel I property with its density, the homogenised composite property of the grillage example, the beam of 5 m along X), the
/// analysed GPC beams that stand for the former FEM results (the link of a GH beam to its analysed element and model) and the accessors of
/// a GPC beam property. Built with the product builders and <see cref="GpcCompositeDefinitions"/>, without any FEM model.
/// </summary>
internal static class R2aPropertyFixtures
{
    /// <summary>GPC twin of <c>GetBeamPropertyTests.Property</c>: the base I property (<see cref="R2aDesignFixtures.BaseSection"/>) named
    /// <paramref name="name"/>, steel S355 of modulus <paramref name="modulus"/>, ν 0.3, density 7.85E-9.</summary>
    internal static LegacyBeamProperty Property(string name, double modulus = 210000) =>
        GpcPropertyBuilders.Beam(name, 5, R2aDesignFixtures.BaseSection(), R2aDesignFixtures.Material("Steel", "S355", modulus, .3, 7.85e-9), "Steel");

    /// <summary>The composite section of <c>BridgeGrillageExample.Composite(new())</c> named <paramref name="prefix"/>
    /// (<see cref="GpcCompositeDefinitions.Grillage"/>), with the steel density given.</summary>
    internal static GpcCompositeDefinitions.Numbers Grillage(string prefix, double steelDensity = 0) =>
        GpcCompositeDefinitions.Grillage(new BridgeGrillageExample.Parameters()) with { Name = prefix, SteelDensity = steelDensity };

    /// <summary>GPC twin of <c>BridgeGrillageExample.Composite(new()).CreateHomogenizedProperty(n, prefix + suffix)</c>: the homogenised
    /// property of the grillage composite section (<see cref="GpcPropertyBuilders.Homogenised"/>, composite preview), named
    /// <paramref name="prefix"/> + <paramref name="suffix"/>, on the analysis steel of the section with the density given.</summary>
    internal static LegacyBeamProperty Homogenised(string prefix, double n, string suffix, double steelDensity = 0, double modulus = 0)
    {
        var numbers = Grillage(prefix, steelDensity);
        if (modulus != 0) numbers = numbers with { SteelE = modulus };
        return GpcPropertyBuilders.Homogenised(GpcCompositeDefinitions.Create(numbers), n, suffix, GpcCompositeDefinitions.AnalysisSteel(numbers), "Steel");
    }

    /// <summary>GPC twin of <c>GetBeamPropertyTests.Beam</c>: the beam (0,0,0)–(5000,0,0) with BridGHe ID <paramref name="id"/>, the
    /// property, groups, angle and offset given; identity derived from <paramref name="tag"/>.</summary>
    internal static LegacyBeamDraft Beam(string tag, string id, LegacyBeamProperty property, IReadOnlyList<IReadOnlyList<string>> groups = null,
        double angleDeg = 0, double offsetX = 0, double offsetY = 0) =>
        GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(5000, 0, 0), property, groups, angleDeg, offsetX, offsetY,
            R2aDesignFixtures.Id("property-beam:" + tag), id);

    internal static ElasticBeamProperty Elastic(LegacyBeamProperty p) => (ElasticBeamProperty)p.Property;
    internal static ElasticBeamProperty Elastic(LegacyBeamDraft d) => (ElasticBeamProperty)d.Beam.BeamProperty;
    internal static double Area(LegacyBeamProperty p) => Elastic(p).Section.Area;
    internal static double I11(LegacyBeamProperty p) => Elastic(p).Section.J11;
    internal static GPC.Model.Materials.Material Material(LegacyBeamProperty p) => Elastic(p).Material;

    /// <summary>Analysed GPC beams (one per BridGHe ID, 5 m along X at 1 m spacing) with an axial force of 123 in case «TEST» at station 0,
    /// and their analysed model: the analysis results a GH beam links to (<c>AnalysedGpc</c>, <c>AnalysedGpcModel</c>).</summary>
    internal static (GPC.Model.Elements.BeamElement[] Beams, GPC.Model.Models.Model Model) Analysed(string name, params string[] ids)
    {
        var test = new GpcTestModel(name); test.Case("TEST");
        var beams = ids.Select((id, i) => test.Beam(id, test.Node("a" + i, 0, 1000 * i, 0), test.Node("b" + i, 5000, 1000 * i, 0))).ToArray();
        foreach (var b in beams) test.Forces(b, "TEST", 0, 123);
        return (beams, test.Analyse());
    }

    /// <summary>Links a GH beam to its analysed GPC element and model.</summary>
    internal static IGH_Goo WithAnalysed(IGH_Goo beam, GPC.Model.Elements.BeamElement analysed, GPC.Model.Models.Model model)
    {
        beam.GetType().GetProperty("AnalysedGpc").SetValue(beam, analysed); beam.GetType().GetProperty("AnalysedGpcModel").SetValue(beam, model);
        return beam;
    }

    internal static object Analysed(IGH_Goo beam) => beam.GetType().GetProperty("AnalysedGpc").GetValue(beam);
    internal static object AnalysedModel(IGH_Goo beam) => beam.GetType().GetProperty("AnalysedGpcModel").GetValue(beam);
    internal static Guid Guid(IGH_Goo goo) => (Guid)goo.GetType().GetProperty("Guid").GetValue(goo);

    /// <summary>An empty GH value of the plug-in type (no FEM value, no GPC datum): an invalid input.</summary>
    internal static IGH_Goo Empty(string type) => (IGH_Goo)Activator.CreateInstance(R2aDesignFixtures.Plugin.GetTypes().Single(t => t.Name == type));
}
#endif
