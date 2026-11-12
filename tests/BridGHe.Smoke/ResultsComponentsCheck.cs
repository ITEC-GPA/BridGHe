#if GPC_PILOT
using System;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.6 lane R2b (replaces the F6 C4 parity with the outputs of the former FEM branch of the components): Node, Beam and Plate Results read the
/// analysed GPC element that Decompose Model links. For an analysed GH model (an analysis component: the GPC input and the analysed model
/// attached): Decompose Model on the GH model given by its GPC input alone, then each Results component, every output equal bit for bit to
/// the rows of the GPC readers in the layout of the component (<see cref="GpcNativeExamples.ComponentsCheck"/>); a result datum without the
/// GPC link gives no result and the warning; the generated moving cases with their native numbers (after the load cases of the input); the
/// result consumers and the model audit on the analysed model (<see cref="ConsumersCheck"/>). No FEM value is read.
/// </summary>
internal static class ResultsComponentsCheck
{
    internal sealed record Outcome(int Nodes, int Beams, int Plates, long Values);

    /// <summary>The GH model an analysis component gives on the FEM path: the FEM results and the analysed GPC model attached (the kN-m native
    /// section of core-native, until <c>Program.CheckNativeSectionAnalysis</c> moves to GPC).</summary>
    internal static object AnalysedGoo(BridGHe.Common.Models.ModelModel result, GPC.Model.Models.Model analysed)
    {
        var type = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly).GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
        var goo = type.GetMethod("FromModel", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { result, null });
        type.GetMethod("AttachGpc").Invoke(goo, new object[] { analysed ?? throw new ArgumentNullException(nameof(analysed)) });
        return goo;
    }

    internal static Outcome Verify(object analysedModelGoo, string label)
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("RESULTS GPC FAIL (" + label + "): " + what); }
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var t = analysedModelGoo.GetType();
        Require((bool)t.GetProperty("HasAnalysedGpc").GetValue(analysedModelGoo), "the GH model has its analysed GPC model");
        var input = (BridGHe.Common.Gpc.GpcModels.Conversion)t.GetProperty("GpcInput").GetValue(analysedModelGoo);
        var analysed = (GPC.Model.Models.Model)t.GetProperty("Gpc").GetValue(analysedModelGoo);
        var catalog = BridGHe.Common.Gpc.GpcResultCatalog.Of(analysed);
        Require(input != null && catalog != null, "the GPC input and the results catalog of the analysed model");
        // Node, Beam and Plate Results on the GH model given by the GPC input alone, every output equal to the reader rows bit for bit.
        // Decompose Model refuses a GPC-only model without the drafts of its elements, so the input is given with them: its own; else those the
        // analysed GH model keeps apart (GpcDrafts: analysis outputs, reopened results, as R4aFixtures.GpcOnly); else, for a FEM-backed model (the
        // kN-m native section of core-native, until Program.CheckNativeSectionAnalysis moves to GPC), the conversion of its FEM values.
        var drafts = input?.Drafts ?? (BridGHe.Common.Gpc.GpcModelDrafts)t.GetProperty("GpcDrafts").GetValue(analysedModelGoo)
            ?? (t.GetProperty("Value").GetValue(analysedModelGoo) is BridGHe.Common.Models.ModelModel fem
                ? new BridGHe.Common.Gpc.GpcModelDrafts(fem.Nodes.Select(n => BridGHe.Common.Gpc.GpcBeamDrafts.FromFemm(n)),
                    fem.Beams.Select(b => BridGHe.Common.Gpc.GpcBeamDrafts.FromFemm(b)), fem.Plates.Select(p => BridGHe.Common.Gpc.GpcBeamDrafts.FromFemm(p)))
                : null);
        long values = GpcNativeExamples.ComponentsCheck(input == null || input.Drafts != null || drafts == null ? input : input.WithDrafts(drafts), analysed, label);
        // A result datum without the link to an analysed GPC element gives no result and the warning.
        int nodes = analysed.NodesElements.Count, beams = analysed.BeamElements.Count, plates = analysed.AreaElements.Count;
        int k = nodes > 0 ? 0 : beams > 0 ? 1 : 2;
        string[] components = { "NodeResultsComponent", "BeamResultsComponent", "PlateResultsComponent" };
        string[] types = { "GH_NodeResults", "GH_BeamResults", "GH_PlateResults" };
        using (var document = new GH_Document { Enabled = true })
        {
            GH_Document.EnableSolutions = true;
            var probe = (GH_Component)Activator.CreateInstance(assembly.GetTypes().Single(x => x.Name == components[k]));
            probe.CreateAttributes(); document.AddObject(probe, false);
            var unlinked = (IGH_Goo)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem." + types[k], true));
            ((Param_GenericObject)probe.Params.Input[0]).PersistentData.Append(unlinked);
            document.NewSolution(false, GH_SolutionMode.Silent);
            Require(probe.Params.Output.All(p => p.VolatileData.DataCount == 0) && probe.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains(BridGHe.Common.Gpc.GpcResultCatalog.NoAnalysedResultsMessage),
                components[k] + ": a datum without the GPC link gives no result and the warning");
        }
        var moving = catalog.GeneratedMovingCases();
        // R6.6 R2bN: the native Straus7 numbers, as the former GeneratedMovingCases of 0.3.0 (MovingLoadExporter.cs:40-48 of 95ab7c3; the Converter in
        // Straus7MovingNativeApi.ConvertSelectedMovingPaths): the cases added by the conversion of the paths, after the load cases of the input.
        int first = input.Model.LoadCases.Count + 1;
        Require(moving.Select(c => c.Number).SequenceEqual(Enumerable.Range(first, moving.Count)) && moving.All(c => !string.IsNullOrWhiteSpace(c.Name)) && moving.Select(c => c.Name).Distinct().Count() == moving.Count,
            $"generated moving cases numbered from {first}, after the {first - 1} load cases of the input, with distinct names: {string.Join(", ", moving.Select(c => c.Number + ": " + c.Name))}");
        // The model audit on the analysed model: its stored results add no finding (every result has its case identity), the rest is the audit
        // of the GPC input (the structural audit of the examples is in ConsumersCheck.Audit, with the expected structure).
        string Issues(BridGHe.Common.Diagnostics.ModelIssue[] issues) => string.Join("\n", issues.Select(i => i.Severity + "|" + i.Code + "|" + i.Element + "|" + i.Description));
        string withResults = Issues(BridGHe.Common.Diagnostics.BridgeModelAudit.Inspect(input, 1e-6, analysed: analysed)), inputOnly = Issues(BridGHe.Common.Diagnostics.BridgeModelAudit.Inspect(input, 1e-6));
        Require(withResults == inputOnly && !withResults.Contains("|INVALID_RESULT|"), "model audit of the analysed model equal to the audit of its input:\n" + withResults + "\n--\n" + inputOnly);
        long consumers = ConsumersCheck.Verify(analysed, label);
        var outcome = new Outcome(nodes, beams, plates, values);
        Console.WriteLine($"RESULTS GPC PASS ({label}): {nodes} nodes, {beams} beams, {plates} plates, {values} output values equal to the GPC reader rows bit for bit; " +
            $"{moving.Count} generated moving cases numbered after the load cases of the input; model audit of the analysed model equal to that of its input; result consumers: {consumers} values.");
        return outcome;
    }
}
#endif
