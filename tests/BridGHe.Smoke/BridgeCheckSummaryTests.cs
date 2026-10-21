using System;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class BridgeCheckSummaryTests
{
    internal static void Run()
    {
        var plugin = BridgeMergeTests.Initialize();
        GH_Component Create(string name) => (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == name));
        var plain = SampleResult();
        var source = new BridgeResultSource("36", 1250.25, "C1", "G2");
        var a = plain.WithSource(source);
        var b = plain.WithSource(source with { Combination = "C2" });
        var c = plain.WithSource(source with { BeamId = "37", StationMm = 0 });
        Require(ReferenceEquals(a.Analysis, plain.Analysis) && ReferenceEquals(a.Stage, plain.Stage) && plain.Source == null,
            "Adding provenance must not recompute or mutate the original analysis.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("it-IT");
            Require(source.Label == "G2 / Beam 36 / s=1250.25 mm / C1", "Label units, local station or invariant decimal format.");
        }
        finally { CultureInfo.CurrentCulture = culture; }

        using var doc = new GH_Document { Enabled = true };
        var summary = Create("BridgeCheckSummaryComponent");
        summary.CreateAttributes(); doc.AddObject(summary, false);
        var input = (Param_GenericObject)summary.Params.Input[0];
        var labels = (Param_String)summary.Params.Input[1];
        var first = new GH_Path(2, 4); var second = new GH_Path(7, 0);
        Require(labels.Optional, "Labels must be optional on new components.");
        input.PersistentData.Append(new GH_ObjectWrapper(a), first);
        input.PersistentData.Append(new GH_ObjectWrapper(b), first);
        input.PersistentData.Append(new GH_ObjectWrapper(c), second);
        Solve();
        Require(summary.Params.Output[0].VolatileData.Paths.SequenceEqual(new[] { first, second }), "Automatic labels changed tree paths.");
        VerifyLabels(a, b, c);
        var expectedRatios = new[] { a, b, c }.SelectMany(r => BridgeGpc.Checks(r.Stage)).Select(v => v.Ratio ?? double.NaN);
        Require(Values<double>(3).SequenceEqual(expectedRatios), "Automatic naming changed numerical ratios or NaN values.");

        // Reordered/flattened results must retain their own identity, independently of item position.
        input.PersistentData.Clear();
        foreach (var result in new[] { c, b, a }) input.PersistentData.Append(new GH_ObjectWrapper(result));
        Solve(); VerifyLabels(c, b, a);
        labels.SetPersistentData("Custom C", "Custom B", "Custom A"); Solve();
        Require(Values<string>(0).Distinct().SequenceEqual(new[] { "Custom C", "Custom B", "Custom A" }), "Custom labels were ignored.");
        labels.PersistentData.Clear(); labels.SetPersistentData("Only one"); Solve(true);
        labels.PersistentData.Clear(); labels.SetPersistentData("C", " ", "A"); Solve(true);

        // Standalone section results have no FEM location: use distinct tree positions, never invented beam IDs.
        labels.PersistentData.Clear(); input.PersistentData.Clear();
        input.PersistentData.Append(new GH_ObjectWrapper(plain), first);
        input.PersistentData.Append(new GH_ObjectWrapper(plain), second);
        Solve();
        var fallback = Values<string>(0).Distinct().ToArray();
        Require(fallback.Length == 2 && fallback.All(s => s.Contains(plain.Stage.Name) && !s.Contains("Beam"))
            && fallback[0].Contains(first.ToString()) && fallback[1].Contains(second.ToString()), "Missing or misleading standalone result identity.");

        var detail = Create("BridgeDetailCheckComponent"); detail.CreateAttributes(); doc.AddObject(detail, false);
        ((Param_GenericObject)detail.Params.Input[0]).PersistentData.Append(new GH_ObjectWrapper(a));
        Solve();
        Require(detail.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, "Detail checker rejected the traced result.");
        var detailed = (BridgeStressResult)detail.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        Require(detailed.Source == a.Source && detailed.Analysis.Input.Phases.SequenceEqual(a.Analysis.Input.Phases), "Detail checking lost provenance or changed actions.");

        // Simulate an old definition, whose archive explicitly says Labels is required.
        input.PersistentData.Clear(); labels.Optional = false;
        var archive = new GH_IO.Serialization.GH_Archive();
        Require(archive.AppendObject(summary, "Summary"), "Could not save summary contract.");
        var restored = Create("BridgeCheckSummaryComponent");
        Require(archive.ExtractObject(restored, "Summary") && restored.Params.Input[1].Optional, "Old archives must adopt optional labels.");
        Require(restored.ComponentGuid == summary.ComponentGuid && restored.Params.Input.Count == 2 && restored.Params.Output.Count == 10,
            "Component GUID or port positions changed.");
        using var reopened = new GH_Document { Enabled = true };
        restored.CreateAttributes(); reopened.AddObject(restored, false);
        ((Param_GenericObject)restored.Params.Input[0]).PersistentData.Append(new GH_ObjectWrapper(a));
        reopened.NewSolution(false, GH_SolutionMode.Silent);
        Require(restored.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0 && restored.Params.Output[0].VolatileDataCount > 0,
            "Restored summary cannot solve without labels.");
        Console.WriteLine("CHECK SUMMARY PASS: automatic/custom labels, branches, reordered results, unchanged ratios, detail provenance and old archive migration.");

        T[] Values<T>(int port) => summary.Params.Output[port].VolatileData.AllData(true).Select(g => (T)g.ScriptVariable()).ToArray();
        void VerifyLabels(params BridgeStressResult[] results)
        {
            var expected = results.SelectMany(r => BridgeGpc.Checks(r.Stage).Select(_ => r.Source.Label));
            Require(Values<string>(0).Length > 0 && Values<string>(0).SequenceEqual(expected), "Result/label association changed.");
        }
        void Solve(bool expectError = false)
        {
            input.ExpireSolution(false); labels.ExpireSolution(false);
            GH_Document.EnableSolutions = true; doc.NewSolution(false, GH_SolutionMode.Silent);
            var errors = summary.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            Require(expectError ? errors.Any(e => e.Contains("Labels")) && summary.Params.Output[0].VolatileDataCount == 0 : errors.Count == 0,
                "Summary error handling: " + string.Join("; ", errors));
        }
    }
    internal static BridgeStressResult SampleResult()
    {
        var analysis = HBridgeSection.Calculate(new HBridgeInput
        {
            Geometry = BridgeDetailLayoutTests.Section(),
            Materials = BridgeGpc.Materials(new MaterialModel("C30") { Name = "C30", SpecificCompressiveStrength = 30 },
                new MaterialModel("S355") { Name = "S355", Modulus = 210000, MinimumYieldStress = 355, MinimumTensileStress = 510 }, null),
            TopRebars = new BridgeRebarRow { Enabled = false }, BottomRebars = new BridgeRebarRow { Enabled = false },
            Options = new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Ultimate },
            Phases = new[] { new BridgePhase { Name = "Steel", Kind = BridgePhaseKind.SteelOnly, MomentKNm = 100, ShearKN = 50 },
                new BridgePhase { Name = "Composite", MomentKNm = 200, ShearKN = 100 } }
        });
        return new BridgeStressResult(analysis, analysis.Stages.Count - 1);
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("CHECK SUMMARY: " + message); }
}
