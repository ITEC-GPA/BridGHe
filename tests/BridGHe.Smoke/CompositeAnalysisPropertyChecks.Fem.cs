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
    /// <param name="datum">Called with every value compared (what, FEM, definition): the native capture of R6.6.</param>
    internal static int CompareFem(CompositeBeamPropertyModel property, BridgeCompositeSectionDefinition definition, string label, Action<string, string, string> datum = null)
    {
        int compared = 0;
        void Same(string what, string expected, string actual)
        {
            if (expected != actual) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}, {property.Name}, {what}): FEM '{expected}' / definition '{actual}'");
            datum?.Invoke(what, expected, actual);
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
    /// <summary>The composite properties among the outputs of the components of the solved <paramref name="doc"/> (each once), with the
    /// definition their GH type carries.</summary>
    internal static IEnumerable<(CompositeBeamPropertyModel Property, BridgeCompositeSectionDefinition Definition)> Properties(GH_Document doc)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in doc.Objects.OfType<GH_Component>())
            foreach (var output in component.Params.Output)
                foreach (var goo in output.VolatileData.AllData(true))
                {
                    if (goo == null || goo.GetType().Name != "GH_CompositeBeamProperty") continue;
                    if (goo.GetType().GetProperty("Value")?.GetValue(goo) is not CompositeBeamPropertyModel p || p.ConcreteSection?.IsValid != true) continue;
                    if (!seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ModelArchive.Write(p))))) continue;
                    yield return (p, (BridgeCompositeSectionDefinition)goo.GetType().GetProperty("Definition").GetValue(goo));
                }
    }

    /// <summary>P2X: the canonical texts (not hashed) of the values of the native capture of the composite properties of <paramref name="doc"/>,
    /// by their key (<c>composite|&lt;definition&gt;|&lt;what&gt;</c>): (key, FEM text, definition text). The evidence of a declared capture
    /// rule (<see cref="NativeParityCapture"/>): the capture keeps only the SHA-256 of a long text.</summary>
    internal static int Texts(GH_Document doc, string label, Action<string, string, string> text)
    {
        int count = 0;
        foreach (var (p, definition) in Properties(doc))
        {
            string key = "composite|" + definition.Fingerprint() + "|";
            count += CompareFem(p, definition, label, (what, fem, gpc) => text(key + what, fem, gpc));
        }
        return count;
    }
}
#endif
