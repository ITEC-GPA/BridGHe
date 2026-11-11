#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using BridGHe.Common.Checks;
using GPC.Checkers.CompositeBridge;

/// <summary>
/// P2S (7/11), decision (i) = A, examples 11–18 and 19 REV003 (native): the saved definition as it is, its Straus phase analyses run natively
/// (Output Paths redirected to artifacts/phase-examples-native, the file never saved), then Build BridGHe Model and Super Element Check. The
/// actions of these examples come from Build Model outputs rebuilt from the beams of Super Element Filter on the analyses: Build BridGHe Model
/// must now build the GPC bridge model from them (<c>GH_Model.LinkedGpcResults</c>) and Super Element Check run on it (D3). The checks are
/// compared with the former path («fasi BridGHe», <see cref="FemPhasesReference"/>) within the C5 tolerances, no status changed.
/// <para>R6.6 lane R4b: the comparison with the former path is <b>transitional</b> (P2 retires it with <see cref="FemPhasesReference"/>): no capture
/// holds the Super Element Check results of these definitions, so it stays until one does; the diagnostics of BRIDGHE_PHASE_DEBUG read GPC data
/// only. <c>BRIDGHE_PHASE_INSPECT=1</c> (non-native) lists the internalised data of each definition with a FEM value and what the reading of P2
/// (<see cref="R4aFixtures.GpcOnly"/>) makes of them, solving nothing. <c>BRIDGHE_PHASE_GPC_ONLY=1</c> (native) runs each definition a second time
/// read as P2 reads it (every internalised datum with a FEM value given by its GPC datum alone: the 11–19 definitions internalise beam and
/// composite properties, load cases and materials, no model) and requires the same section results as the first run (C5 tolerances).</para>
/// </summary>
internal static class NativePhaseExamples
{
    internal static readonly string[] Current =
    {
        @"11-ponte-graticcio\11-ponte-graticcio.ghx", @"12-continuo-2campate\12-continuo-2campate.ghx", @"13-continuo-3campate\13-continuo-3campate.ghx",
        @"14-curvo-1campata\14-curvo-1campata.ghx", @"15-curvo-2campate\15-curvo-2campate.ghx", @"16-spina-1campata\16-spina-1campata.ghx",
        @"17-spina-3campate\17-spina-3campate.ghx", @"18-spina-curva-2campate\18-spina-curva-2campate.ghx", @"19-flusso-acciaio-misto\19-flusso-acciaio-misto_REV003.ghx",
    };
    private static IGH_Param Input(GH_Component c, string name) => c.Params.Input.Single(p => p.Name == name);
    private static void Press(GH_Component c) { c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true); c.ExpireSolution(false); }

    internal static int Run(string selection)
    {
        BridgeExampleMigration.Initialize();
        var files = selection == null || selection == "all" ? Current : Current.Where(f => f.StartsWith(selection, StringComparison.Ordinal)).ToArray();
        if (files.Length == 0) throw new ArgumentException("No phase example matches " + selection);
        var rows = new List<string>(); var gpcOnlyFailures = new List<string>();
        bool inspect = Environment.GetEnvironmentVariable("BRIDGHE_PHASE_INSPECT") is "1" or "2";
        bool gpcOnly = Environment.GetEnvironmentVariable("BRIDGHE_PHASE_GPC_ONLY") == "1";
        foreach (var relative in files)
        {
            string path = Path.Combine(ExamplePaths.Root, relative); string label = Path.GetFileNameWithoutExtension(path);
            byte[] original = File.ReadAllBytes(path);
            if (inspect)
            {
                using var read = GrasshopperBridgeExample.Read(path);
                foreach (var toggle in read.Objects.OfType<GH_BooleanToggle>()) toggle.Value = false;
                rows.Add(Inspect(read, label));
                if (Environment.GetEnvironmentVariable("BRIDGHE_PHASE_INSPECT") == "2") { rows.Add(Upstream(path, label, false)); rows.Add(Upstream(path, label, true)); }
                continue;
            }
            var (analyses, checks, reference) = Pass(path, label, label, false, rows);
            // R6.6 R4b (BRIDGHE_PHASE_GPC_ONLY=1): the same definition read as P2 reads it, solved again natively, its checks against the first run.
            if (gpcOnly)
            {
                // A failure of the run read as P2 is listed (the first runs of the other definitions still run) and fails the runner at the end.
                try { rows.Add(SameChecks(label, reference, Pass(path, label + " GPC-only", label + "-gpc-only", true, rows).Results)); }
                catch (Exception ex) { gpcOnlyFailures.Add(label + ": " + ex.GetBaseException().Message); Console.WriteLine("PHASE EXAMPLE GPC-ONLY FAIL (" + label + "): " + ex); }
            }
            if (!original.AsSpan().SequenceEqual(File.ReadAllBytes(path))) throw new Exception(label + ": the definition changed on disk");
            Console.WriteLine($"PHASE EXAMPLE NATIVE PASS ({label}): {analyses} analyses, GPC bridge model, {checks} Super Element Check on D3{(gpcOnly && !gpcOnlyFailures.Any(f => f.StartsWith(label + ":", StringComparison.Ordinal)) ? "; the definition read as P2 reads it: the same checks" : "")}.");
        }
        if (inspect) { foreach (var row in rows) Console.WriteLine("PHASE EXAMPLE INSPECT " + row); Console.WriteLine($"PHASE EXAMPLES INSPECT: {files.Length} definitions, nothing solved."); return 0; }
        foreach (var row in rows) Console.WriteLine("PHASE EXAMPLE DIFFERENCES " + row);
        foreach (var f in gpcOnlyFailures) Console.WriteLine("PHASE EXAMPLE GPC-ONLY FAILED " + f);
        if (gpcOnlyFailures.Count > 0) { Console.WriteLine($"PHASE EXAMPLES GPC-ONLY FAIL: {gpcOnlyFailures.Count} of {files.Length} definitions read as P2 reads them (the first runs passed)"); return 1; }
        Console.WriteLine($"PHASE EXAMPLES NATIVE PASS: {files.Length} definitions{(gpcOnly ? ", each also read as P2 reads it" : "")}.");
        return 0;
    }

    /// <summary>One native run of a definition (the body of the former loop): the analyses redirected and pressed, Build BridGHe Model, Super Element
    /// Check; with <paramref name="gpcOnly"/> the document read as P2 reads it first (<see cref="R4aFixtures.GpcOnly"/>: every internalised GH datum
    /// with a FEM value given by its GPC datum alone) and no comparison with the former path (it needs the FEM values). The section results of
    /// every Super Element Check by nickname and branch.</summary>
    private static (int Analyses, int Checks, Dictionary<string, BridgeStressResult[]> Results) Pass(string path, string label, string folderName, bool gpcOnly, List<string> rows)
    {
            string folder = Path.GetFullPath(Path.Combine("artifacts/phase-examples-native", folderName)); Directory.CreateDirectory(folder);
            using var doc = GrasshopperBridgeExample.Read(path);
            foreach (var toggle in doc.Objects.OfType<GH_BooleanToggle>()) toggle.Value = false;
            if (gpcOnly) Console.WriteLine($"PHASE EXAMPLE GPC-ONLY ({label}): {R4aFixtures.GpcOnly(doc).Length} internalised analysed models; every internalised datum with a FEM value given by its GPC datum alone");
            var analyses = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "StrausR3AnalysisComponent").OrderBy(c => c.Attributes.Pivot.Y).ThenBy(c => c.Attributes.Pivot.X).ToArray();
            if (analyses.Length == 0) throw new Exception(label + ": no Straus analysis");
            for (int i = 0; i < analyses.Length; i++)
            {
                var a = analyses[i];
                var field = a.GetType().GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic); field.SetValue(a, Activator.CreateInstance(field.FieldType));
                a.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(a, false);
                var keep = (Param_Boolean)Input(a, "Keep Running"); keep.RemoveAllSources(); keep.PersistentData.Clear(); keep.SetPersistentData(false);
                // The Output Path of the definition is redirected in memory (never into the main checkout), the file is never saved.
                var output = (Param_String)Input(a, "Output Path"); output.RemoveAllSources(); output.PersistentData.Clear(); output.SetPersistentData(Path.Combine(folder, $"{i + 1:00}.st7"));
            }
            BridgeSegmentExcelTests.Solve(doc, true);
            foreach (var a in analyses) { Press(a); BridgeSegmentExcelTests.Solve(doc, true); }
            foreach (var a in analyses)
            {
                var all = a.Params.Output[0].VolatileData.AllData(true).ToArray();
                if (all.Length == 0 || all.Any(goo => goo?.GetType().GetProperty("GpcResults")?.GetValue(goo) == null))
                    throw new Exception(label + ": no analysed GPC model from " + a.NickName + " (" + all.Length + " outputs: " + string.Join(", ", all.Select(g => g?.GetType().Name)) + ")\n" + Errors(doc));
            }
            var merge = doc.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == "BuildBridgeModel");
            Press(merge); BridgeSegmentExcelTests.Solve(doc, true);
            var mergedGoo = merge.Params.Output[0].VolatileData.AllData(true).SingleOrDefault() ?? throw new Exception(label + ": Build BridGHe Model gave no model\n" + Errors(doc));
            if (mergedGoo.GetType().GetProperty("GpcBridge").GetValue(mergedGoo) == null && Environment.GetEnvironmentVariable("BRIDGHE_PHASE_DEBUG") == "1")
            {
                // Diagnostics: the GPC build of the component again, to show where it fails.
                var models = merge.Params.Input[0].VolatileData.AllData(true).ToArray();
                var references = models.Select(m => BridGHe.Common.Gpc.GpcBridgeSource.Reference((BridGHe.Common.Gpc.GpcModels.Conversion)m.GetType().GetProperty("GpcInput").GetValue(m),
                    (GPC.Model.Models.Model)m.GetType().GetProperty("GpcActionResults").GetValue(m))).ToArray();
                var phases = merge.Params.Input[1].VolatileData.AllData(true).Select(g => (BridGHe.Common.Checks.BridgePhaseDefinition)g.ScriptVariable()).ToArray();
                var tree = (Grasshopper.Kernel.Data.IGH_Structure)merge.Params.Input[2].VolatileData;
                var table = Enumerable.Range(0, tree.PathCount).Select(p => tree.get_Branch(p).Cast<Grasshopper.Kernel.Types.IGH_Goo>().Select(g => g.ScriptVariable() is IFormattable f ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : g.ScriptVariable()?.ToString() ?? "").ToArray()).ToArray();
                string Dups(GPC.Model.Models.Model m) => m == null ? "null" : m.Name + " " + m.BeamElements.Count + " beams, duplicate names: " + string.Join(",", m.BeamElements.Values.GroupBy(b => b.Name).Where(g => g.Count() > 1).Select(g => g.Key + "x" + g.Count()).Take(5));
                foreach (var r in references) Console.WriteLine("PHASE DEBUG reference " + Dups(r.Model) + " | analysed " + Dups(r.Analysed));
                foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name is "StrausR3AnalysisComponent" or "SuperElementFilterComponent"))
                    Console.WriteLine($"PHASE DEBUG {c.GetType().Name} {c.InstanceGuid}: inputs {string.Join(",", c.Params.Input.Select(p => p.VolatileDataCount))}; input 0 {c.Params.Input[0].VolatileDataCount} items in {c.Params.Input[0].VolatileData.PathCount} paths from {c.Params.Input[0].SourceCount} sources, input 1 {c.Params.Input[1].VolatileDataCount}; output 0 {c.Params.Output[0].VolatileDataCount} in {c.Params.Output[0].VolatileData.PathCount} paths");
                foreach (var bm in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BuildModelComponent"))
                {
                    var items = bm.Params.Input[0].VolatileData.AllData(true).ToArray();
                    var guids = items.Select(g => g.GetType().GetProperty("Guid")?.GetValue(g)).ToArray();
                    if (items.All(g => g.GetType().Name == "GH_Beam"))
                        Console.WriteLine("PHASE DEBUG rebuild inputs: " + string.Join(",", items.Select(g => { var d = g.GetType().GetProperty("Gpc").GetValue(g);
                            var be = (GPC.Model.Elements.BeamElement)d.GetType().GetProperty("Beam").GetValue(d); var an = g.GetType().GetProperty("AnalysedGpc").GetValue(g) as GPC.Model.Elements.BeamElement;
                            return be.Name + "/" + an?.Name + "@" + be.StartPoint.X.ToString("F0") + ";" + be.StartPoint.Y.ToString("F0") + "-" + be.EndPoint.X.ToString("F0") + ";" + be.EndPoint.Y.ToString("F0"); })));
                    Console.WriteLine($"PHASE DEBUG Build Model {bm.NickName}: {items.Length} inputs ({string.Join(",", items.GroupBy(g => g.GetType().Name).Select(g => g.Key + " " + g.Count()))}), {guids.Distinct().Count()} distinct GH GUIDs, sources {bm.Params.Input[0].SourceCount}, mapping {bm.Params.Input[0].DataMapping}");
                }
                foreach (var m in models.Take(1))
                {
                    var conv = (BridGHe.Common.Gpc.GpcModels.Conversion)m.GetType().GetProperty("GpcInput").GetValue(m);
                    Console.WriteLine("PHASE DEBUG GPC names " + string.Join(",", conv.Model.BeamElements.Values.OrderBy(b => b.Id).Select(b => b.Name + "/" + b.Source.OriginalId)));
                }
                foreach (var a in phases.SelectMany(p => p.Actions)) Console.WriteLine("PHASE DEBUG action " + a.Key + " model " + Dups(a.GpcModel) + " | source " + Dups(a.GpcSource) + " | same " + ReferenceEquals(a.GpcModel, a.GpcSource));
                try { BridGHe.Common.Gpc.GpcBridgeBuilder.Build(references, phases, table, BridGHe.Common.Checks.BridgeWorkflowMethod.Compatible, 1e-3, false, Guid.NewGuid()); }
                catch (Exception e) { Console.WriteLine("PHASE DEBUG " + label + ": " + e); }
            }
            if (mergedGoo.GetType().GetProperty("GpcBridge").GetValue(mergedGoo) == null)
                throw new Exception(label + ": Build BridGHe Model gave no GPC bridge model: " + string.Join("; ", merge.RuntimeMessages(GH_RuntimeMessageLevel.Warning)));
            var checks = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "SuperElementCheckComponent").ToArray();
            foreach (var check in checks) { Press(check); BridgeSegmentExcelTests.Solve(doc, true); }
            var errors = Errors(doc);
            if (errors.Length > 0) throw new Exception(label + ": " + errors);
            foreach (var check in checks)
            {
                if (gpcOnly) continue;
                // TRANSITIONAL (P2 retires it with FemPhasesReference): the former path on the same inputs; no capture holds these results.
                var c = FemPhasesReference.Compare(check, label + " " + check.NickName);
                rows.Add($"{label} {check.NickName}: {c.Results} results, {c.Identical} identical, max |Δσ| {c.MaxStress:G3} MPa, |Δu| {c.MaxUtilization:G3}, |Δratio| {c.MaxRatio:G3}, {c.Statuses} status changes");
            }
            var results = new Dictionary<string, BridgeStressResult[]>(StringComparer.Ordinal);
            foreach (var check in checks)
            {
                var tree = check.Params.Output[0].VolatileData;
                for (int p = 0; p < tree.PathCount; p++)
                    results.Add(check.NickName + " " + tree.get_Path(p), tree.get_Branch(p).Cast<Grasshopper.Kernel.Types.IGH_Goo>().Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray());
            }
            return (analyses.Length, checks.Length, results);
    }

    /// <summary>R6.6 R4b: the section results of the run on the definition read as P2 reads it against the first run (same branches and counts; the
    /// stresses of points 5 and 8, the utilization and every check with the tolerances of the C5 comparison, no status changed).</summary>
    private static string SameChecks(string label, Dictionary<string, BridgeStressResult[]> first, Dictionary<string, BridgeStressResult[]> only)
    {
        void Fail(string what) => throw new Exception("PHASE EXAMPLE GPC-ONLY FAIL (" + label + "): " + what);
        static double Diff(double x, double y) => double.IsNaN(x) && double.IsNaN(y) ? 0 : Math.Abs(x - y);
        if (!first.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(only.Keys.OrderBy(k => k, StringComparer.Ordinal))) Fail($"branches {first.Count} / {only.Count}");
        double maxStress = 0, maxUtilization = 0, maxRatio = 0; int count = 0, identical = 0, statuses = 0;
        foreach (var (key, x) in first)
        {
            var y = only[key];
            if (x.Length != y.Length) Fail($"{key}: {x.Length} / {y.Length} results");
            for (int k = 0; k < x.Length; k++)
            {
                count++;
                double ds = Math.Max(Diff(x[k].Point5_SteelSigma, y[k].Point5_SteelSigma), Diff(x[k].Point8_SteelSigma, y[k].Point8_SteelSigma)), du = Diff(x[k].Stage.MaxUtilization, y[k].Stage.MaxUtilization), dr = 0;
                var cx = BridgeGpc.Checks(x[k].Stage).ToArray(); var cy = BridgeGpc.Checks(y[k].Stage).ToArray();
                if (cx.Length != cy.Length) Fail($"{key}[{k}]: {cx.Length} / {cy.Length} checks");
                for (int c = 0; c < cx.Length; c++)
                {
                    if (cx[c].Name != cy[c].Name) Fail($"{key}[{k}]: check {cx[c].Name} / {cy[c].Name}");
                    if (cx[c].Status != cy[c].Status) statuses++;
                    dr = Math.Max(dr, Diff(cx[c].Ratio ?? double.NaN, cy[c].Ratio ?? double.NaN));
                }
                if (ds == 0 && du == 0 && dr == 0) identical++;
                maxStress = Math.Max(maxStress, ds); maxUtilization = Math.Max(maxUtilization, du); maxRatio = Math.Max(maxRatio, dr);
            }
        }
        string row = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} GPC-ONLY vs first run: {1} results, {2} identical, max |Δσ| {3:G3} MPa, |Δu| {4:G3}, |Δratio| {5:G3}, {6} status changes",
            label, count, identical, maxStress, maxUtilization, maxRatio, statuses);
        if (count == 0) Fail("no section results");
        if (statuses != 0 || maxStress > 1e-3 || maxUtilization > 1e-6 || maxRatio > 1e-6) Fail(row);
        return row;
    }

    private static string Errors(GH_Document doc) => string.Join("\n", doc.Objects.OfType<GH_ActiveObject>()
        .SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => o.NickName + ": " + m)));


    /// <summary>R6.6 lane R4b, <c>BRIDGHE_PHASE_INSPECT=2</c> (non-native): the definition solved with every analysis blocked (Keep Running and Run off,
    /// never pressed, Output Paths into a scratch folder of the run): the errors of the components upstream of the analyses and the models the
    /// analyses receive (with or without a FEM value), as read and read as P2 reads it (<see cref="R4aFixtures.GpcOnly"/>).</summary>
    private static string Upstream(string path, string label, bool gpcOnly)
    {
        using var doc = GrasshopperBridgeExample.Read(path);
        foreach (var toggle in doc.Objects.OfType<GH_BooleanToggle>()) toggle.Value = false;
        if (gpcOnly) R4aFixtures.GpcOnly(doc);
        string scratch = Path.Combine(Path.GetTempPath(), "bridghe-r4b-upstream-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        var analyses = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent").ToArray();
        int k = 0;
        foreach (var a in analyses)
        {
            a.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(a, false);
            foreach (var name in new[] { "Keep Running", "Run" })
                if (a.Params.Input.SingleOrDefault(p => p.Name == name) is Param_Boolean b) { b.RemoveAllSources(); b.PersistentData.Clear(); b.SetPersistentData(false); }
            foreach (var p in a.Params.Input.Where(p => p.Name is "Output Path" or "Output path").OfType<Param_String>()) { p.RemoveAllSources(); p.PersistentData.Clear(); p.SetPersistentData(Path.Combine(scratch, $"{++k:00}.st7")); }
        }
        var upstream = new HashSet<Guid>();
        void Walk(IGH_Param p) { foreach (var s in p.Sources) { var o = s.Attributes?.GetTopLevel?.DocObject ?? s; if (!upstream.Add(o.InstanceGuid)) continue; if (o is GH_Component c) foreach (var i in c.Params.Input) Walk(i); else if (o is IGH_Param q) Walk(q); } }
        foreach (var a in analyses) foreach (var i in a.Params.Input) Walk(i);
        BridgeSegmentExcelTests.Solve(doc, true);
        var errors = doc.Objects.OfType<GH_ActiveObject>().Where(o => upstream.Contains(o.InstanceGuid)).SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => o.Name + ": " + m)).ToArray();
        var models = analyses.SelectMany(a => a.Params.Input[0].VolatileData.AllData(true)).Where(g => g != null).ToArray();
        int fem = models.Count(g => g.GetType().GetProperty("Value")?.GetValue(g) != null);
        return $"{label} ({(gpcOnly ? "read as P2 reads it" : "as read")}): {upstream.Count} objects upstream of {analyses.Length} analyses, {errors.Length} errors{(errors.Length > 0 ? " (" + string.Join(" | ", errors.Distinct().Take(3)) + ")" : "")}; "
            + $"the analyses receive {models.Length} models, {fem} with a FEM value";
    }
    /// <summary>R6.6 lane R4b, <c>BRIDGHE_PHASE_INSPECT=1</c> (non-native: nothing is solved, the toggles are off): the internalised GH data of a
    /// definition that carry a FEM value (by GH type), its internalised models (and those without drafts), and what the reading of P2
    /// (<see cref="R4aFixtures.GpcOnly"/>) leaves of them.</summary>
    private static string Inspect(GH_Document doc, string label)
    {
        string[] Fem() => doc.Objects.OfType<IGH_Param>().Concat(doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input))
            .SelectMany(p => (p.GetType().GetProperty("PersistentData")?.GetValue(p) as Grasshopper.Kernel.Data.IGH_Structure)?.AllData(true) ?? Enumerable.Empty<Grasshopper.Kernel.Types.IGH_Goo>())
            .Where(g => g != null && g.GetType().FullName.StartsWith("BridGHe.", StringComparison.Ordinal) && g.GetType().GetProperty("Gpc") != null && g.GetType().GetProperty("Value")?.GetValue(g) != null)
            .GroupBy(g => g.GetType().Name).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + " " + g.Count()).ToArray();
        var before = Fem(); int models = R4aFixtures.Models(doc).Length, withoutDrafts = R4aFixtures.WithoutDrafts(doc);
        string after;
        try { var analysed = R4aFixtures.GpcOnly(doc); after = "with a FEM value " + (Fem() is { Length: > 0 } left ? string.Join(", ", left) : "none") + ", " + analysed.Length + " analysed models carried"; }
        catch (Exception ex) { after = "refused: " + ex.GetBaseException().Message; }
        return $"{label}: internalised data with a FEM value {(before.Length == 0 ? "none" : string.Join(", ", before))}; {models} internalised models ({withoutDrafts} without drafts); read as P2 reads it: {after}";
    }
}
#endif
