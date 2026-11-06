#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;

/// <summary>
/// FEM path (removed with the FeMM models, R6.5): the analysis values of the definitions against the FEM composite properties themselves
/// (<see cref="CompositeBeamPropertyModel.CreateHomogenizedProperty"/>, <see cref="CompositeBeamPropertyModel.GetHomogeneizedMechanicalProperties"/>,
/// <see cref="CompositeBeamPropertyModel.GetReinforcedConcreteSection"/>, <c>CompositeShrinkageActions.Calculate(property, strain)</c>,
/// <c>GpcModels.Material</c>): the parity of <c>gpc-composite-sections</c> and the native gate of 50 and 99 (<see cref="Require"/>, on the
/// composite properties among the outputs of the solved definitions). The FEM-free comparison is <see cref="Compare"/>, on the numbers.
/// </summary>
internal static partial class CompositeAnalysisPropertyChecks
{
    /// <summary>Compares the analysis values of the FEM <paramref name="property"/> with those of its definition; the number of variants
    /// compared (an exception with the first difference otherwise).</summary>
    internal static int CompareFem(CompositeBeamPropertyModel property, BridgeCompositeSectionDefinition definition, string label)
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
                Same($"homogenised property n={n:R} concrete={concrete}", expected, Try(() => Property(GpcCompositeSections.Homogenised(definition, n, concrete))));
            }
            foreach (double strain in new[] { -2e-4, -3.5e-4, 1e-4 })
            {
                string former = Try(() => GpcCompositeSectionChecks.Dump(BridGHe.Common.Checks.CompositeSelfTension.Calculate(property.GetReinforcedConcreteSection(), n, strain)));
                string gpc = Try(() => GpcCompositeSectionChecks.Dump(BridGHe.Common.Checks.CompositeSelfTension.Calculate(definition.AnalysisSection(), n, strain)));
                bool Echo(string line) => line.StartsWith(".Analysis.Input.Materials.Rebar.Et=", StringComparison.Ordinal) || line.StartsWith(".Analysis.Input.Materials.Rebar.StrainU", StringComparison.Ordinal);
                string Kept(string dump) => string.Join("\n", dump.Split('\n').Where(l => !Echo(l)));
                Same($"self tension n={n:R} strain={strain:R}", Kept(former), Kept(gpc));
                if (former != gpc) EchoedRebarDifferences++;
            }
        }
        if (definition.AnalysisSteel == null) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}, {property.Name}): definition without the analysis steel");
        Same("analysis steel", GPC.Model.Core.ModelValues.Fingerprint(new object[] { GpcModels.Material(property.SteelMaterial) }), GPC.Model.Core.ModelValues.Fingerprint(new object[] { definition.AnalysisSteel }));
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
                    variants += CompareFem(p, definition, label); properties++;
                    if ((p.RebarMaterial?.ReinforcementUltimateStrain ?? 0.1) != 0.1) strains++;
                }
        if (properties == 0) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}): no composite property in the outputs");
        Console.WriteLine($"COMPOSITE ANALYSIS PROPERTY PASS ({label}): {properties} composite properties, {variants} values or variants of the definition " +
            $"equal bit for bit to the FEM property (analysis section, steel torsion, homogenised values for every choice, homogenised property uncracked " +
            $"and cracked, self tension, shrinkage actions, analysis steel, at n0, n∞, n,sh, 1 and 15.3); {strains} with a rebar ultimate strain other than 0.1 (not in the homogenised values or the stresses; {EchoedRebarDifferences} self-tension results echo the rebar of the definition).");
    }
}
#endif
