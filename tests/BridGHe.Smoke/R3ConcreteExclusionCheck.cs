#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// F6 (a)/(b), R3 (native, 99): «Consider Concrete» off on some beams of the path by cases (Compatible, no local states). A variant of a
/// Super Element Check of the example: every other composite beam with concrete off, the combinations without the slab strain coefficients
/// (D3 rejects a strain on an excluded slab; the shrinkage actions are left out entirely, D3 requires one coefficient for their strain and
/// forces), on the GPC bridge model (D3, recipe copy with ConcreteExcludedBeamGuids), and the same check with concrete everywhere: the variant
/// runs, has the same tree and changes results. Until decision (i) = A of the user (7/11) a third component on the bare merged FEM model (the
/// former BridGHe phases with concrete off) was the parity reference; that path is gone. The components are removed afterwards (the
/// fingerprint is written before).
/// <para>TRANSITIONAL (R6.6 R2a, P2 decides): the variant is built on the FEM value of the merged model (its combinations and the FEM
/// composite beams of the check, <c>ModelArchive.Clone</c>); its GPC rewrite (the model by its GPC input with the variant of the bridge
/// model, the composite beams by their GPC data with definitions without concrete) needs the native 99, outside lane R2a.</para>
/// </summary>
internal static class R3ConcreteExclusionCheck
{
    internal static void Run(GH_Document doc, GH_Component check, object mergedGoo, string label)
    {
        void Fail(string what) => throw new Exception("R3 CONCRETE EXCLUSION FAIL (" + label + "): " + what);
        var assembly = mergedGoo.GetType().Assembly;
        var modelType = mergedGoo.GetType();
        var merged = (ModelModel)modelType.GetProperty("Value").GetValue(mergedGoo);
        var bridge = modelType.GetProperty("GpcBridge").GetValue(mergedGoo) ?? throw new Exception("R3: no GPC bridge model");
        var copy = ModelArchive.Clone(merged);
        int strainFactors = 0;
        foreach (var combination in copy.LoadCombinations)
        {
            // A shrinkage action has one coefficient for its strain and its FEM forces (D3 rejects different ones): both set to zero.
            var actions = combination.Values.Keys.Where(k => k.Name.EndsWith(" [strain]", StringComparison.Ordinal)).Select(k => k.Name[..^" [strain]".Length]).ToHashSet(StringComparer.Ordinal);
            foreach (var key in combination.Values.Keys.Where(k => actions.Any(x => k.Name == x + " [strain]" || k.Name == x + " [FEM]")).ToArray())
                if (combination.Values[key] != 0) { combination.Values[key] = 0; strainFactors++; }
        }
        // P2T request 3: the checks read the combinations of the GPC bridge model (its table), so the variant carries the same coefficients.
        var variant = ((BridGHe.Common.Gpc.GpcBridge)bridge).WithCombinations(copy.LoadCombinations
            .Select(c => (c.Name, (IReadOnlyList<(string Case, double Factor)>)c.Values.Select(v => (v.Key.Name, v.Value)).ToArray())).ToArray());
        object Goo(bool withBridge)
        {
            var goo = modelType.GetMethod("FromModel", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { copy, null });
            if (withBridge) modelType.GetMethod("AttachBridge").Invoke(goo, new object[] { variant });
            return goo;
        }
        var beamType = assembly.GetType("BridGHe.Grasshopper.DataTypes.GH_CompositeBeam", true);
        var source = check.Params.Input[1].VolatileData.AllData(true).Select(g => (CompositeBeamModel)((IGH_Goo)g).ScriptVariable()).ToArray();
        var excluded = new List<string>();
        var beams = source.Select((b, i) =>
        {
            var beam = ModelArchive.Clone(b);
            if (i % 2 == 1) { beam.StartBeamProperty.ConsiderConcrete = false; beam.EndBeamProperty.ConsiderConcrete = false; excluded.Add(beam.BeamId); }
            return (IGH_Goo)Activator.CreateInstance(beamType, beam, null);
        }).ToArray();
        var included = source.Select(b => (IGH_Goo)Activator.CreateInstance(beamType, ModelArchive.Clone(b), null)).ToArray();
        GH_Component Make(object modelGoo, IGH_Goo[] beams)
        {
            var c = (GH_Component)Activator.CreateInstance(check.GetType()); c.CreateAttributes(); doc.AddObject(c, false);
            ((Param_GenericObject)c.Params.Input[0]).PersistentData.Append((IGH_Goo)modelGoo);
            foreach (var b in beams) ((Param_GenericObject)c.Params.Input[1]).PersistentData.Append(b);
            for (int k = 2; k < 5; k++)
            {
                var value = check.Params.Input[k].VolatileData.AllData(true).OfType<GH_Boolean>().Single().Value;
                var p = (Param_Boolean)c.Params.Input[k]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Boolean(value));
            }
            c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true); c.ExpireSolution(false);
            return c;
        }
        var d3 = Make(Goo(true), beams); var reference = Make(Goo(true), included);
        try
        {
            BridgeSegmentExcelTests.Solve(doc, true);
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
            Console.WriteLine($"R3 CONCRETE EXCLUSION PASS ({label})");
        }
        finally { doc.RemoveObject(d3, false); doc.RemoveObject(reference, false); }
    }
}
#endif
