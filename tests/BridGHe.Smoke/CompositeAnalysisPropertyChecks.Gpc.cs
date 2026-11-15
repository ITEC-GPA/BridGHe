using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;

internal static partial class CompositeAnalysisPropertyChecks
{
    /// <summary>
    /// Native gate of 50 and 99: every composite property among the outputs of the components of the solved <paramref name="doc"/> (the
    /// definition its GH type carries, each once by its fingerprint): the analysis values of the definition (analysis section, steel torsion,
    /// homogenised values for every choice, homogenised property uncracked and cracked, self tension, analysis steel, shrinkage actions) at
    /// n0, n∞, n,sh, 1 and 15.3.
    /// <para><b>P2R (T2/R5, 12/11):</b> on GPC data alone, against the frozen native capture (keys <c>composite|&lt;fingerprint&gt;|&lt;what&gt;</c>), whose
    /// values are those of the FEM properties (<see cref="CompareFem"/>, bit for bit equal to these when it was written). The same texts as the
    /// GPC side of <see cref="CompareFem"/>: the moduli ratios n0, n∞, n,sh of the definition stand for NInstant, NInfinite, NShrinkage.</para>
    /// </summary>
    internal static void Require(GH_Document doc, string label)
    {
        int properties = 0, variants = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in doc.Objects.OfType<GH_Component>())
            foreach (var output in component.Params.Output)
                foreach (var goo in output.VolatileData.AllData(true))
                {
                    if (goo == null || goo.GetType().Name != "GH_CompositeBeamProperty") continue;
                    // Not a valid definition (no slab, a vertical offset: the getter throws, as BarLayoutMeasurement skips it): no analysis values.
                    BridgeCompositeSectionDefinition definition;
                    try { definition = goo.GetType().GetProperty("Definition")?.GetValue(goo) as BridgeCompositeSectionDefinition; }
                    catch (System.Reflection.TargetInvocationException) { continue; }
                    if (definition?.Section == null) continue;
                    string fingerprint = definition.Fingerprint();
                    if (!seen.Add(fingerprint)) continue;
                    string key = "composite|" + fingerprint + "|";
                    variants += Gpc(definition, label, (what, value) => NativeParityCapture.Datum(label, key + what, null, NativeParityCapture.Value(value)));
                    properties++;
                }
        if (properties == 0) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}): no composite property in the outputs");
        Console.WriteLine($"COMPOSITE ANALYSIS PROPERTY PASS ({label}): {properties} composite properties, {variants} values or variants of the definition recorded against the native capture " +
            "(analysis section, steel torsion, homogenised values for every choice, homogenised property uncracked and cracked, self tension, shrinkage actions, analysis steel, at n0, n∞, n,sh, 1 and 15.3).");
    }

    /// <summary>The analysis values of a definition by the names of <see cref="CompareFem"/> (its GPC side), each given to <paramref name="datum"/>.</summary>
    private static int Gpc(BridgeCompositeSectionDefinition definition, string label, Action<string, string> datum)
    {
        int compared = 0;
        void Value(string what, string value) { datum(what, value); compared++; }
        if (definition.RebarRows == null) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}): definition without rows");
        Value("analysis section", Try(() => Bars(definition.AnalysisSection())));
        Value("steel torsion", B(GpcCompositeSections.SteelTorsionConstant(definition)));
        var ratios = new[] { definition.NShortTerm, definition.NLongTerm, definition.NShrinkage, 1, 15.3 }.Where(n => double.IsFinite(n) && n > 0).Distinct().ToArray();
        foreach (double n in ratios)
        {
            foreach (bool c in new[] { true, false })
                foreach (bool r in new[] { true, false })
                    foreach (bool s in new[] { true, false })
                        Value($"homogenised n={n:R} concrete={c} rebars={r} steel={s}", Try(() => Values(GpcCompositeSections.AnalysisProperties(definition, n, c, r, s))));
            foreach (bool concrete in new[] { true, false })
                Value($"homogenised property n={n:R} concrete={concrete}", Try(() => Property(GpcCompositeSections.Homogenised(definition, n, concrete))));
            foreach (double strain in new[] { -2e-4, -3.5e-4, 1e-4 })
            {
                string gpc = Try(() => GpcCompositeSectionChecks.Dump(BridGHe.Common.Checks.CompositeSelfTension.Calculate(definition.AnalysisSection(), n, strain)));
                // The lines that echo the rebar of the definition are not compared (the rule of CompareFem).
                static bool Echo(string line) => line.StartsWith(".Analysis.Input.Materials.Rebar.Et=", StringComparison.Ordinal) || line.StartsWith(".Analysis.Input.Materials.Rebar.StrainU", StringComparison.Ordinal);
                Value($"self tension n={n:R} strain={strain:R}", string.Join("\n", gpc.Split('\n').Where(l => !Echo(l))));
            }
        }
        if (definition.AnalysisSteel == null) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}): definition without the analysis steel");
        Value("analysis steel", GPC.Model.Core.ModelValues.Fingerprint(new object[] { definition.AnalysisSteel }));
        foreach (double strain in new[] { -2e-4, -3.5e-4, 1e-4, 0 })
            Value($"shrinkage strain={strain:R}", Try(() => Shrinkage(BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(definition, strain))));
        return compared;
    }
}
