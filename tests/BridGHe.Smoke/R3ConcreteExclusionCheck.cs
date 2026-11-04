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
/// (BridGHe's phases, like D3, reject a strain on an excluded slab; the shrinkage actions are left out entirely, D3 requires one coefficient for their strain and forces). Two new components on the same inputs: one with the GPC bridge model
/// (D3, recipe copy with ConcreteExcludedBeamGuids), one with the bare merged FEM model (BridGHe's phases with concrete off, the former path).
/// Same tree; per section result the stage utilization, the stresses of points 5 and 8 and every check (ratio, status) compared; the largest
/// differences are reported and must stay within the tolerances of the C5 comparison (D3 keeps one increment per action, BridGHe sums them by
/// kind: class-4 iterations start differently); no status may change. The components are removed afterwards (the fingerprint is written before).
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
        object Goo(bool withBridge)
        {
            var goo = modelType.GetMethod("FromModel", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { copy, null });
            if (withBridge) modelType.GetMethod("AttachBridge").Invoke(goo, new[] { bridge });
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
        var d3 = Make(Goo(true), beams); var fem = Make(Goo(false), beams); var reference = Make(Goo(true), included);
        try
        {
            BridgeSegmentExcelTests.Solve(doc, true);
            foreach (var c in new[] { d3, fem, reference })
            {
                var errors = c.RuntimeMessages(GH_RuntimeMessageLevel.Error);
                if (errors.Count != 0) Fail((c == d3 ? "D3" : "FEM") + " component error: " + string.Join("; ", errors));
            }
            if (d3.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Any(m => m.StartsWith("Verifiche con le fasi BridGHe", StringComparison.Ordinal))) Fail("the D3 variant used BridGHe's phases");
            var a = d3.Params.Output[0].VolatileData; var b = fem.Params.Output[0].VolatileData; var r = reference.Params.Output[0].VolatileData;
            // The variant must change the excluded beams: the same check with concrete everywhere differs from it.
            int changed = 0;
            for (int p = 0; p < a.PathCount; p++)
            {
                var x = a.get_Branch(p).Cast<IGH_Goo>().Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
                var z = r.get_Branch(r.Paths.IndexOf(a.get_Path(p))).Cast<IGH_Goo>().Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
                changed += x.Zip(z).Count(q => q.First.Point8_SteelSigma != q.Second.Point8_SteelSigma || q.First.Point5_SteelSigma != q.Second.Point5_SteelSigma);
            }
            if (changed == 0) Fail("excluding the concrete changed no result: the variant proves nothing");
            if (a.PathCount != b.PathCount || a.DataCount != b.DataCount) Fail($"trees differ: D3 {a.PathCount}/{a.DataCount}, FEM {b.PathCount}/{b.DataCount}");
            double maxStress = 0, maxUtilization = 0, maxRatio = 0; int statuses = 0, results = 0, identical = 0; string worst = "";
            static double Diff(double x, double y) => double.IsNaN(x) && double.IsNaN(y) ? 0 : Math.Abs(x - y);
            for (int p = 0; p < a.PathCount; p++)
            {
                var x = a.get_Branch(p).Cast<IGH_Goo>().Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
                var y = b.get_Branch(b.Paths.IndexOf(a.get_Path(p))).Cast<IGH_Goo>().Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
                if (x.Length != y.Length) Fail($"branch {a.get_Path(p)}: {x.Length} D3 results, {y.Length} FEM");
                for (int k = 0; k < x.Length; k++)
                {
                    results++;
                    double ds = Math.Max(Diff(x[k].Point5_SteelSigma, y[k].Point5_SteelSigma), Diff(x[k].Point8_SteelSigma, y[k].Point8_SteelSigma));
                    double du = Diff(x[k].Stage.MaxUtilization, y[k].Stage.MaxUtilization);
                    var cx = BridgeGpc.Checks(x[k].Stage).ToArray(); var cy = BridgeGpc.Checks(y[k].Stage).ToArray();
                    if (cx.Length != cy.Length) Fail($"{a.get_Path(p)}[{k}]: {cx.Length} D3 checks, {cy.Length} FEM");
                    double dr = 0;
                    for (int j = 0; j < cx.Length; j++)
                    {
                        if (cx[j].Name != cy[j].Name) Fail($"{a.get_Path(p)}[{k}]: check {cx[j].Name} / {cy[j].Name}");
                        if (cx[j].Status != cy[j].Status) statuses++;
                        dr = Math.Max(dr, Diff(cx[j].Ratio ?? double.NaN, cy[j].Ratio ?? double.NaN));
                    }
                    if (ds == 0 && du == 0 && dr == 0) identical++;
                    if (ds > maxStress) { maxStress = ds; worst = $"{x[k].Source?.Label} ({a.get_Path(p)}[{k}])"; }
                    maxUtilization = Math.Max(maxUtilization, du); maxRatio = Math.Max(maxRatio, dr);
                }
            }
            var inv = CultureInfo.InvariantCulture;
            Console.WriteLine(string.Format(inv, "R3 CONCRETE EXCLUSION ({0}): {1} of {2} beams with concrete off, {3} shrinkage coefficients (strain and FEM) set to zero; {4} section results, {5} identical in every compared number; " +
                "max |Δσ| points 5/8 {6:G3} MPa (worst {7}), max |Δ utilization| {8:G3}, max |Δ ratio| {9:G3}, {10} status changes; {11} results differ from the same check with concrete everywhere.",
                label, excluded.Count, beams.Length, strainFactors, results, identical, maxStress, worst, maxUtilization, maxRatio, statuses, changed));
            if (results == 0) Fail("no section results");
            if (statuses != 0) Fail(statuses + " check statuses changed");
            if (maxStress > 1e-3 || maxUtilization > 1e-6 || maxRatio > 1e-6) Fail("differences beyond the tolerances of the C5 comparison (1e-3 MPa, 1e-6)");
            Console.WriteLine($"R3 CONCRETE EXCLUSION PASS ({label})");
        }
        finally { doc.RemoveObject(d3, false); doc.RemoveObject(fem, false); doc.RemoveObject(reference, false); }
    }
}
#endif
