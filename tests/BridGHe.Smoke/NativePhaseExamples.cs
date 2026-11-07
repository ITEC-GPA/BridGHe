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

/// <summary>
/// P2S (7/11), decision (i) = A, examples 11–18 and 19 REV003 (native): the saved definition as it is, its Straus phase analyses run natively
/// (Output Paths redirected to artifacts/phase-examples-native, the file never saved), then Build BridGHe Model and Super Element Check. The
/// actions of these examples come from Build Model outputs rebuilt from the beams of Super Element Filter on the analyses: Build BridGHe Model
/// must now build the GPC bridge model from them (<c>GH_Model.LinkedGpcResults</c>) and Super Element Check run on it (D3). The checks are
/// compared with the former path («fasi BridGHe», <see cref="FemPhasesReference"/>) within the C5 tolerances, no status changed.
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
        var rows = new List<string>();
        foreach (var relative in files)
        {
            string path = Path.Combine(ExamplePaths.Root, relative); string label = Path.GetFileNameWithoutExtension(path);
            byte[] original = File.ReadAllBytes(path);
            string folder = Path.GetFullPath(Path.Combine("artifacts/phase-examples-native", label)); Directory.CreateDirectory(folder);
            using var doc = GrasshopperBridgeExample.Read(path);
            foreach (var toggle in doc.Objects.OfType<GH_BooleanToggle>()) toggle.Value = false;
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
                        Console.WriteLine("PHASE DEBUG rebuild inputs: " + string.Join(",", items.Select(g => { var v = (BridGHe.Common.Models.BeamModel)g.ScriptVariable(); var d = g.GetType().GetProperty("Gpc").GetValue(g);
                            var be = (GPC.Model.Elements.BeamElement)d.GetType().GetProperty("Beam").GetValue(d); var an = g.GetType().GetProperty("AnalysedGpc").GetValue(g) as GPC.Model.Elements.BeamElement;
                            return v.BeamId + "/" + be.Name + "/" + an?.Name + "@" + v.PointFrom.X.ToString("F0") + ";" + v.PointFrom.Y.ToString("F0") + "-" + v.PointTo.X.ToString("F0") + ";" + v.PointTo.Y.ToString("F0"); })));
                    Console.WriteLine($"PHASE DEBUG Build Model {bm.NickName}: {items.Length} inputs ({string.Join(",", items.GroupBy(g => g.GetType().Name).Select(g => g.Key + " " + g.Count()))}), {guids.Distinct().Count()} distinct GH GUIDs, sources {bm.Params.Input[0].SourceCount}, mapping {bm.Params.Input[0].DataMapping}");
                }
                foreach (var m in models.Take(1))
                {
                    var fem = (BridGHe.Common.Models.ModelModel)m.ScriptVariable();
                    var conv = (BridGHe.Common.Gpc.GpcModels.Conversion)m.GetType().GetProperty("GpcInput").GetValue(m);
                    Console.WriteLine("PHASE DEBUG FEM ids " + string.Join(",", fem.Beams.Select(b => b.BeamId)));
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
                var c = FemPhasesReference.Compare(check, label + " " + check.NickName);
                rows.Add($"{label} {check.NickName}: {c.Results} results, {c.Identical} identical, max |Δσ| {c.MaxStress:G3} MPa, |Δu| {c.MaxUtilization:G3}, |Δratio| {c.MaxRatio:G3}, {c.Statuses} status changes");
            }
            if (!original.AsSpan().SequenceEqual(File.ReadAllBytes(path))) throw new Exception(label + ": the definition changed on disk");
            Console.WriteLine($"PHASE EXAMPLE NATIVE PASS ({label}): {analyses.Length} analyses, GPC bridge model, {checks.Length} Super Element Check on D3.");
        }
        foreach (var row in rows) Console.WriteLine("PHASE EXAMPLE DIFFERENCES " + row);
        Console.WriteLine($"PHASE EXAMPLES NATIVE PASS: {files.Length} definitions.");
        return 0;
    }

    private static string Errors(GH_Document doc) => string.Join("\n", doc.Objects.OfType<GH_ActiveObject>()
        .SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => o.NickName + ": " + m)));
}
#endif
