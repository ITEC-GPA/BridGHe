#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using GPC.Model.Sections;
using GPC.Model.Sections.Concrete;
using Grasshopper.Kernel;

/// <summary>
/// Chain E step 1 (coordinator decision of 5/11, option ii): the homogenised analysis properties of a composite girder from the Checker
/// definition (<see cref="BridgeCompositeSectionDefinition.AnalysisSection"/> with the rows kept by <see cref="GpcCompositeSections"/>;
/// <see cref="GpcCompositeSections.Homogenised"/>, <see cref="GpcCompositeSections.AnalysisProperties"/>,
/// <see cref="GpcCompositeSections.SteelTorsionConstant"/>) against the FEM property they replace
/// (<see cref="CompositeBeamPropertyModel.CreateHomogenizedProperty"/>, <see cref="CompositeBeamPropertyModel.GetHomogeneizedMechanicalProperties"/>,
/// <see cref="CompositeBeamPropertyModel.GetReinforcedConcreteSection"/>), every value by its bits, the same exceptions (by type):
/// <list type="bullet">
/// <item>the bars of the analysis section (positions, diameters, modulus), the slab and the steel H;</item>
/// <item>the eleven homogenised values for every choice of concrete, bars and steel (Composite Section Geometric Property) at n0, n∞, n,sh
/// and two other ratios;</item>
/// <item>the generic homogenised property (area, I11, I22, J, shear areas, centroid, angle) with Consider Concrete as given and toggled
/// (Bridge State Override: uncracked and cracked) at the same ratios.</item>
/// </list>
/// Native: every composite property of the solved definitions of 50 and 99 (<see cref="Require"/>); not native: <c>gpc-composite-sections</c>.
/// </summary>
internal static class CompositeAnalysisPropertyChecks
{
    private static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

    /// <summary>Self-tension results whose echoed rebar material differs (bar ultimate strain of the property other than 0.1).</summary>
    internal static int EchoedRebarDifferences;

    private static string Try(Func<string> value)
    {
        try { return value(); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { return "throws " + ex.GetType().Name; }
    }

    private static string Values((double areaH, double SxH, double SyH, GPC.Geometry.Point2d centroidH, double JxxH, double JyyH, double JxyH, double JpH, double J11H, double J22H, double angleX) p) =>
        string.Join(" ", new[] { p.areaH, p.SxH, p.SyH, p.centroidH.X, p.centroidH.Y, p.JxxH, p.JyyH, p.JxyH, p.JpH, p.J11H, p.J22H, p.angleX }.Select(B));

    private static string Bars(ReinforcedConcreteSection s)
    {
        var h = (SectionH)s.SteelSections[0].Section.SectionShape; var at = s.SteelSections[0];
        return string.Join(";", s.Rebars.Select(b => B(b.Position.X) + "," + B(b.Position.Y) + "," + B(b.RebarSection.Diameter) + "," + B(b.RebarSection.RebarMaterial.ElasticModulusTension)))
            + "|slab " + B(s.Width) + "," + B(s.Height) + "," + B(s.ConcreteMaterial.ElasticModulusCompression) + "," + s.ConcreteMaterial.Name
            + "|steel " + string.Join(",", new[] { h.Height, h.ThicknessWeb, h.LenghtTopFlange, h.ThicknessTopFlange, h.LenghtBottomFlange, h.ThicknessBottomFlange, h.Jt,
                at.Section.SteelMaterial.ElasticModulusTension, at.Rotation }.Select(B)) + "|" + s.Name;
    }

    /// <summary>Compares the analysis values of <paramref name="property"/> with those of its definition; the number of variants compared
    /// (an exception with the first difference otherwise).</summary>
    internal static int Compare(CompositeBeamPropertyModel property, BridgeCompositeSectionDefinition definition, string label)
    {
        int compared = 0;
        void Same(string what, string expected, string actual)
        {
            if (expected != actual) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}, {property.Name}, {what}): FEM '{expected}' / definition '{actual}'");
            compared++;
        }
        if (definition.RebarRows == null) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}, {property.Name}): definition without rows");
        Same("analysis section", Try(() => Bars(property.GetReinforcedConcreteSection())), Try(() => Bars(definition.AnalysisSection())));
        Same("steel torsion", B(property.GetOnlySteelMechanicalProperties().Jt), B(GpcCompositeSections.SteelTorsionConstant(definition)));
        var ratios = new[] { property.NInstant, property.NInfinite, property.NShrinkage, 1, 15.3 }.Where(n => double.IsFinite(n) && n > 0).Distinct().ToArray();
        foreach (double n in ratios)
        {
            foreach (bool c in new[] { true, false })
                foreach (bool r in new[] { true, false })
                    foreach (bool s in new[] { true, false })
                        Same($"homogenised n={n:R} concrete={c} rebars={r} steel={s}", Try(() => Values(property.GetHomogeneizedMechanicalProperties(n, c, r, s))),
                            Try(() => Values(GpcCompositeSections.AnalysisProperties(definition, n, c, r, s))));
            foreach (bool concrete in new[] { true, false })
            {
                var clone = ModelArchive.Clone(property); clone.ConsiderConcrete = concrete;
                string expected = Try(() =>
                {
                    var section = clone.CreateHomogenizedProperty(n, "").Section;
                    return string.Join(" ", new[] { section.SectionArea, section.I11, section.I22, section.J, section.ShearA1, section.ShearA2, section.Centroid.X, section.Centroid.Y, section.AngleX1Rad }.Select(B));
                });
                string actual = Try(() =>
                {
                    var h = GpcCompositeSections.Homogenised(definition, n, concrete);
                    return string.Join(" ", new[] { h.Area, h.I11, h.I22, h.J, h.ShearArea1, h.ShearArea2, h.CentroidX, h.CentroidY, 0 }.Select(B));
                });
                Same($"homogenised property n={n:R} concrete={concrete}", expected, actual);
            }
            // Composite Beam Self Tension: the stresses of the H bridge engine on the analysis section of the definition (bar ultimate strain
            // 0.1) and on the former section of the property (the strain of its rebar material, 0.075 for the B450C of BridGHe).
            // Every value of the result is compared except the rebar material it echoes (the input of the engine): ultimate strains and
            // hardening modulus Et follow the ultimate strain of the bars (listed difference, counted in EchoedRebarDifferences).
            foreach (double strain in new[] { -2e-4, -3.5e-4, 1e-4 })
            {
                string former = Try(() => GpcCompositeSectionChecks.Dump(BridGHe.Common.Checks.CompositeSelfTension.Calculate(property.GetReinforcedConcreteSection(), n, strain)));
                string gpc = Try(() => GpcCompositeSectionChecks.Dump(BridGHe.Common.Checks.CompositeSelfTension.Calculate(definition.AnalysisSection(), n, strain)));
                bool Echo(string line) => line.StartsWith(".Analysis.Input.Materials.Rebar.Et=", StringComparison.Ordinal) || line.StartsWith(".Analysis.Input.Materials.Rebar.StrainU", StringComparison.Ordinal);
                string Values(string dump) => string.Join("\n", dump.Split('\n').Where(l => !Echo(l)));
                Same($"self tension n={n:R} strain={strain:R}", Values(former), Values(gpc));
                if (former != gpc) EchoedRebarDifferences++;
            }
        }
        // The analysis steel of the definition: the steel material of the property with the rule of the legacy reader; Composite Shrinkage
        // Actions from the definition (n,sh, E and α of that steel, analysis section) = from the property.
        if (definition.AnalysisSteel == null) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}, {property.Name}): definition without the analysis steel");
        Same("analysis steel", GPC.Model.Core.ModelValues.Fingerprint(new object[] { GpcModels.Material(property.SteelMaterial) }), GPC.Model.Core.ModelValues.Fingerprint(new object[] { definition.AnalysisSteel }));
        string Shrinkage(BridGHe.Common.Checks.CompositeShrinkageActions a) => string.Join(" ", new[] { a.FreeStrain, a.Curvature, a.PreStrain, a.ThermalGradient, a.EigenForce, a.EigenMoment, a.Centroid }.Select(B));
        foreach (double strain in new[] { -2e-4, -3.5e-4, 1e-4, 0 })
            Same($"shrinkage strain={strain:R}", Try(() => Shrinkage(BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(property, strain))),
                Try(() => Shrinkage(BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(definition, strain))));
        return compared;
    }

    /// <summary>Native gate: every composite property among the outputs of the components of the solved <paramref name="doc"/>, with the
    /// definition its GH type carries (<c>GH_CompositeBeamProperty.Definition</c>).</summary>
    internal static void Require(GH_Document doc, string label)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal); int properties = 0, variants = 0, strains = 0;
        foreach (var component in doc.Objects.OfType<GH_Component>())
            foreach (var output in component.Params.Output)
                foreach (var goo in output.VolatileData.AllData(true))
                {
                    if (goo == null || goo.GetType().Name != "GH_CompositeBeamProperty") continue;
                    if (goo.GetType().GetProperty("Value")?.GetValue(goo) is not CompositeBeamPropertyModel p || p.ConcreteSection?.IsValid != true) continue;
                    if (!seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ModelArchive.Write(p))))) continue;
                    var definition = (BridgeCompositeSectionDefinition)goo.GetType().GetProperty("Definition").GetValue(goo);
                    variants += Compare(p, definition, label); properties++;
                    if ((p.RebarMaterial?.ReinforcementUltimateStrain ?? 0.1) != 0.1) strains++;
                }
        if (properties == 0) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}): no composite property in the outputs");
        Console.WriteLine($"COMPOSITE ANALYSIS PROPERTY PASS ({label}): {properties} composite properties, {variants} values or variants of the definition " +
            $"equal bit for bit to the FEM property (analysis section, steel torsion, homogenised values for every choice, homogenised property uncracked " +
            $"and cracked, self tension, shrinkage actions, analysis steel, at n0, n∞, n,sh, 1 and 15.3); {strains} with a rebar ultimate strain other than 0.1 (not in the homogenised values or the stresses; {EchoedRebarDifferences} self-tension results echo the rebar of the definition).");
    }
}
#endif
