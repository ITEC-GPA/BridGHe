using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Checks;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// F6 (a)/(b), R3 (native, 99): «Consider Concrete» off on some beams of the path by cases (Compatible, no local states). A variant of a
/// Super Element Check of the example: every other composite beam with concrete off, the combinations without the slab strain coefficients
/// (D3 rejects a strain on an excluded slab; the shrinkage actions are left out entirely, D3 requires one coefficient for their strain and
/// forces), on the GPC bridge model (D3, the combination table of the bridge model with those coefficients), and the same check with concrete
/// everywhere: the two hypotheses run, have the same tree, and the variant changes results. Until decision (i) = A of the user (7/11) a third
/// component on the bare merged FEM model (the former BridGHe phases with concrete off) was the parity reference; that path is gone. The
/// components are removed afterwards (the fingerprint is written before).
/// <para>R6.6 lane R4c-moving: built by the GPC data alone (no FEM value): the combination table of the GPC bridge model (<c>GpcBridge.Combinations</c>,
/// the former <c>LoadCombinations</c> of the merged FEM model) with the shrinkage coefficients set to zero; the model of the two checks given by its GPC
/// input alone (<c>FromGpc</c>) with the variant of the bridge model attached (and the drafts it carries); the composite beams given by their GPC datum
/// alone (<c>GH_CompositeBeam.FromGpc</c>: the draft of the beam, the two definitions, the groups), the beams with concrete off with their two composite
/// definitions rebuilt in GPC with Consider Concrete off (the same section, rows, analysis steel, ratios and offset: <see cref="WithoutConcrete"/>),
/// the former <c>ConsiderConcrete = false</c> on the two FEM properties of the beam.</para>
/// </summary>
internal static class R3ConcreteExclusionCheck
{
    private static object Prop(object o, string name) => o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);

    /// <summary>The composite definition <paramref name="d"/> with Consider Concrete off: the same section, rows of bars, analysis steel, modular ratios,
    /// vertical offset and Consider Rebar (the definition the former FEM property with <c>ConsiderConcrete = false</c> gave).</summary>
    internal static BridgeCompositeSectionDefinition WithoutConcrete(BridgeCompositeSectionDefinition d)
    {
        if (d == null) return null;
        var c = d.Copy();
        return c.RebarRows == null ? new BridgeCompositeSectionDefinition(c.Name, c.Section, c.NLongTerm, c.NShortTerm, c.NShrinkage, c.VerticalOffsetMm, false, c.ConsiderRebar)
            : c.AnalysisSteel == null ? new BridgeCompositeSectionDefinition(c.Name, c.Section, c.RebarRows, c.NLongTerm, c.NShortTerm, c.NShrinkage, c.VerticalOffsetMm, false, c.ConsiderRebar)
            : new BridgeCompositeSectionDefinition(c.Name, c.Section, c.RebarRows, c.AnalysisSteel, c.NLongTerm, c.NShortTerm, c.NShrinkage, c.VerticalOffsetMm, false, c.ConsiderRebar);
    }

    internal static void Run(GH_Document doc, GH_Component check, object mergedGoo, string label)
    {
        void Fail(string what) => throw new Exception("R3 CONCRETE EXCLUSION FAIL (" + label + "): " + what);
        var assembly = mergedGoo.GetType().Assembly;
        var modelType = mergedGoo.GetType();
        var bridge = R4cMoving.Bridge(mergedGoo) ?? throw new Exception("R3: no GPC bridge model");
        var input = R4cMoving.Input(mergedGoo) ?? throw new Exception("R3: the merged model has no GPC input");
        // A shrinkage action has one coefficient for its strain and its FEM forces (D3 rejects different ones): both set to zero.
        int strainFactors = 0;
        var combinations = bridge.Combinations.Select(c =>
        {
            var actions = c.Factors.Where(f => f.Case.EndsWith(" [strain]", StringComparison.Ordinal)).Select(f => f.Case[..^" [strain]".Length]).ToHashSet(StringComparer.Ordinal);
            var factors = c.Factors.Select(f =>
            {
                if (f.Factor == 0 || !actions.Any(x => f.Case == x + " [strain]" || f.Case == x + " [FEM]")) return f;
                strainFactors++; return (f.Case, 0.0);
            }).ToArray();
            return (c.Name, (IReadOnlyList<(string Case, double Factor)>)factors);
        }).ToArray();
        // P2T request 3: the checks read the combinations of the GPC bridge model (its table), so the variant carries the coefficients above.
        var variant = bridge.WithCombinations(combinations);
        object Goo()
        {
            var goo = modelType.GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { input });
            modelType.GetMethod("AttachBridge").Invoke(goo, new object[] { variant });
            if (input.Drafts == null && Prop(mergedGoo, "GpcDrafts") is { } drafts) modelType.GetMethod("AttachDrafts").Invoke(goo, new[] { drafts });
            return goo;
        }
        var beamType = assembly.GetType("BridGHe.Grasshopper.DataTypes.GH_CompositeBeam", true);
        var datumType = assembly.GetType("BridGHe.Grasshopper.DataTypes.GpcCompositeBeam", true);
        var fromGpc = beamType.GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static);
        IGH_Goo Beam(IGH_Goo source, bool concrete)
        {
            var datum = Prop(source, "Gpc") ?? throw new Exception("R3: a composite beam of the check without its GPC datum");
            if (!concrete)
            {
                var start = (BridgeCompositeSectionDefinition)Prop(datum, "Start"); var end = (BridgeCompositeSectionDefinition)Prop(datum, "End");
                var off = WithoutConcrete(start);
                datum = Activator.CreateInstance(datumType, Prop(datum, "Beam"), off, ReferenceEquals(end, start) ? off : WithoutConcrete(end), Prop(datum, "Groups"));
            }
            var beam = (IGH_Goo)fromGpc.Invoke(null, new object[] { datum, Prop(source, "Guid") });
            foreach (var name in new[] { "AnalysedGpc", "AnalysedGpcModel" }) beamType.GetProperty(name).SetValue(beam, beamType.GetProperty(name).GetValue(source));
            return beam;
        }
        var source = check.Params.Input[1].VolatileData.AllData(true).OfType<IGH_Goo>().ToArray();
        var excluded = new List<string>();
        var beams = source.Select((b, i) => { if (i % 2 == 1) excluded.Add((string)Prop(Prop(b, "Gpc"), "BeamId")); return Beam(b, i % 2 == 0); }).ToArray();
        var included = source.Select(b => Beam(b, true)).ToArray();
        if (beams.Where((b, i) => i % 2 == 1).Any(b => ((BridgeCompositeSectionDefinition)Prop(Prop(b, "Gpc"), "Start"))?.ConsiderConcrete == true))Fail("a beam with concrete off kept its concrete");
        GH_Component Make(object modelGoo, IGH_Goo[] beams)
        {
            var c = (GH_Component)Activator.CreateInstance(check.GetType()); c.CreateAttributes(); doc.AddObject(c, false);
            ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append((IGH_Goo)modelGoo);
            foreach (var b in beams) ((Param_GenericObject)c.Params.Input[1]).PersistentData.Append(b);
            // 0.6.0: the instabilities and the stations are in the Options of the check (Super Element Check Options): the same options.
            var options = check.Params.Input[2].VolatileData.AllData(true).OfType<IGH_Goo>().ToArray();
            if (options.Length > 1) Fail("more than one Options item on the check");
            var op = (Param_GenericObject)c.Params.Input[2]; op.PersistentData.Clear(); foreach (var o in options) op.PersistentData.Append(o);
            c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true); c.ExpireSolution(false);
            return c;
        }
        var d3 = Make(Goo(), beams); var reference = Make(Goo(), included);
        try
        {
            using (R2aDesignFixtures.WithoutFem()) BridgeSegmentExcelTests.Solve(doc, true);
            foreach (var c in new[] { d3, reference })
            {
                var errors = c.RuntimeMessages(GH_RuntimeMessageLevel.Error);
                if (errors.Count != 0) Fail((c == d3 ? "D3" : "concrete everywhere") + " component error: " + string.Join("; ", errors));
            }
            var a = d3.Params.Output[0].VolatileData; var r = reference.Params.Output[0].VolatileData;
            // The variant must change the excluded beams: the same check with concrete everywhere differs from it.
            int changed = 0;
            for (int p = 0; p < a.PathCount; p++)
            {
                var x = a.get_Branch(p).Cast<IGH_Goo>().Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
                var z = r.get_Branch(r.Paths.IndexOf(a.get_Path(p))).Cast<IGH_Goo>().Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
                changed += x.Zip(z).Count(q => q.First.Point8_SteelSigma != q.Second.Point8_SteelSigma || q.First.Point5_SteelSigma != q.Second.Point5_SteelSigma);
            }
            if (changed == 0) Fail("excluding the concrete changed no result: the variant proves nothing");
            // Decision (i) = A of the user (7/11): the former path (BridGHe's phases on the bare merged FEM model, the parity reference of this
            // variant) is gone; a Super Element Check without the GPC bridge model is an error. What stays: the D3 variant runs, has the tree of the
            // same check with concrete everywhere, and changes its results.
            if (a.PathCount != r.PathCount || a.DataCount != r.DataCount) Fail($"trees differ: D3 {a.PathCount}/{a.DataCount}, concrete everywhere {r.PathCount}/{r.DataCount}");
            var inv = CultureInfo.InvariantCulture;
            Console.WriteLine(string.Format(inv, "R3 CONCRETE EXCLUSION ({0}): {1} of {2} beams with concrete off, {3} shrinkage coefficients (strain and FEM) set to zero; {4} section results; " +
                "{5} results differ from the same check with concrete everywhere.", label, excluded.Count, beams.Length, strainFactors, a.DataCount, changed));
            if (a.DataCount == 0) Fail("no section results");
            Console.WriteLine($"R3 CONCRETE EXCLUSION PASS ({label}, GPC data alone)");
        }
        finally { doc.RemoveObject(d3, false); doc.RemoveObject(reference, false); }
    }
}
