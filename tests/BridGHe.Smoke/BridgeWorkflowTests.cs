using System;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class BridgeWorkflowTests
{
    internal static ModelModel Source(string name, double force)
    {
        var m = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = name };
        var lc = new LoadCaseModel("G", "G"); m.LoadCases.Add(lc);
        for (int i = 0; i < 2; i++)
        {
            var b = new BeamModel("B" + i, new Point3d(0, i * 2000, 0), new Point3d(10000, i * 2000, 0),
                BridgeGrillageExample.LongProperty(new(), 0)) { BeamId = "B" + i };
            foreach (double x in new[] { 0d, .5, 1d }) b.Results.Add(new BeamForceResultModel(lc, x, 0, "", true) { AxialForce = force + i, My = force * 1000 * (1 + x) });
            m.AddElements(new[] { b });
        }
        return m;
    }
    internal static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Run()
    {
        Gpc();
    }
    /// <summary>R6.6 R2b: the workflow test on Build BridGHe Model on GPC with sources built in GPC only (<see cref="GpcWorkflowSources"/>), no
    /// FEM model: the D3 recipe (phase order and identity, strains, beam selection, primary-only actions, behaviors), the combinations of the
    /// bridge model, case identity, the shared validation (same messages), additional members and geometry. The merged samples are written
    /// explicitly from the analytic diagrams of the sources (N = force + i, BM2 = force·1000·(1 + x/L) at x = 0, L/2, L, no resampling), and the
    /// sources keep their stored results bit for bit: they replace the former parity with the FEM merge. The checks of the phase assembly on
    /// the merged FEM beams (<c>BridgePhaseAssembler.Assemble</c>, the former BridGHe phases) are expressed on the recipe and the combination
    /// factors; the GH archive of a phase with its FEM source has no GPC counterpart (the GPC source is not archived with the phase).</summary>
    private static void Gpc()
    {
        var pSteel = GpcWorkflowSources.Pair("Steel", 100); var pShrink = GpcWorkflowSources.Pair("Shrinkage", 20); var gSteel = pSteel.Analysed; var gShrink = pShrink.Analysed;
        var steelBefore = R2bFixtures.ResultTokens(gSteel); var shrinkBefore = R2bFixtures.ResultTokens(gShrink);
        var a = new BridgeActionDefinition { Key = "G1", GpcSource = gSteel, Case = "G", Behavior = BridgeActionBehavior.Steel };
        var r = new BridgeActionDefinition { Key = "RITIRO", GpcSource = gShrink, Case = "G", Behavior = BridgeActionBehavior.Shrinkage, Strain = -200e-6 };
        var p1 = new BridgePhaseDefinition { Order = 10, Name = "Getto", State = BridgeSectionState.Steel, Actions = new[] { a } };
        var p2 = new BridgePhaseDefinition { Order = 20, Name = "Misto", State = BridgeSectionState.Composite, Actions = new[] { r } };
        string[][] combinations = { new[] { "", "G1", "RITIRO" }, new[] { "SLS", "1", "0.7" } };
        int merges = 0;
        BridGHe.Common.Gpc.GpcBridgeBuild Build(BridgePhaseDefinition[] phases = null, (GPC.Model.Models.Model Input, GPC.Model.Models.Model Analysed)[] models = null, string[][] table = null, bool additional = false)
        {
            var gpc = GpcBridgeMerge.Build(models ?? new[] { pSteel, pShrink }, phases ?? new[] { p2, p1 }, table ?? combinations, "workflow " + merges, additional: additional);
            merges++; return gpc;
        }
        static double[] Diagram(double force, int beam, double x) => new[] { force + beam, 0, 0, 0, 0, force * 1000 * (1 + x / 10000) };
        double[] grid = { 0, 5000, 10000 };
        void Samples(BridGHe.Common.Gpc.GpcBridgeBuild build, string label, params (string Case, double Force)[] cases)
        {
            for (int k = 0; k < 2; k++)
            {
                var present = build.Bridge.Samples[k].Select(s => s.Case).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
                Require(present.SequenceEqual(cases.Select(c => c.Case).OrderBy(c => c, StringComparer.Ordinal)), $"GPC {label}: merged cases of beam {k}: {string.Join(", ", present)}");
                foreach (var (name, force) in cases) R2bFixtures.RequireSamples(build, k, name, grid, x => Diagram(force, k, x), "GPC " + label);
            }
        }
        static double[] Factors(BridGHe.Common.Gpc.GpcBridgeBuild b, string prefix) => b.Bridge.Model.Combinations.Values.Single().GetLoadCaseCoefficientsPair()
            .Where(v => v.Key.Name.StartsWith(prefix, StringComparison.Ordinal)).Select(v => v.Value).ToArray();
        var result = Build(); var recipe = result.Bridge.Workflow;
        Samples(result, "merge", ("G1 [FEM]", 100), ("RITIRO [FEM]", 20));
        Require(recipe.Phases.Select(p => p.Order).SequenceEqual(new[] { 10, 20 }), "GPC: Phases not sorted explicitly.");
        string summary = string.Join("\n", result.Summary);
        Require(summary.IndexOf("Getto", StringComparison.Ordinal) >= 0 && summary.IndexOf("Getto", StringComparison.Ordinal) < summary.IndexOf("Misto", StringComparison.Ordinal), "GPC: summary in phase order: " + summary);
        var shrinkFactors = Factors(result, "RITIRO");
        Require(shrinkFactors.Length == 2 && shrinkFactors.All(v => v == .7) && Factors(result, "G1").SequenceEqual(new[] { 1d }), "GPC: Shrinkage branches use different coefficients.");
        var ritiro = recipe.Phases[1].Actions.Single();
        Require(ritiro.Strain == -200e-6 && ritiro.ForceCaseGuid.HasValue && ritiro.StrainCaseGuid.HasValue && Math.Abs(ritiro.Strain.Value * shrinkFactors[0] + 140e-6) < 1e-16,
            "GPC: Shrinkage strain scaled incorrectly.");
        Require(GpcBridgeMerge.Samples(result, 0).Any(s => s.Case == "RITIRO [FEM]"), "GPC: Shrinkage FEM lost.");
        var reordered = Build(models: new[] { pShrink, pSteel });
        Require(reordered.Bridge.Model.LoadCases.Keys.SequenceEqual(result.Bridge.Model.LoadCases.Keys), "GPC: Case identity depends on source order.");
        Require(GpcBridgeMerge.Tokens(reordered.Bridge.Samples).SequenceEqual(GpcBridgeMerge.Tokens(result.Bridge.Samples)), "GPC: Source order changed forces.");
        var archived = GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.Read(recipe.Save());
        Require(archived.Phases[1].Id == p2.Id && archived.Phases[1].Actions[0].Strain == -200e-6, "GPC: Compiled workflow archive lost identity/strain.");
        var renumbered = Build(new[] { p1 with { Order = 5 }, p2 with { Order = 50 } });
        Require(renumbered.Bridge.Model.LoadCases.Keys.SequenceEqual(result.Bridge.Model.LoadCases.Keys), "GPC: Renumbering changes action references.");
        void Reject(Action test, string contains)
        {
            try { test(); } catch (ArgumentException e) when (e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase)) { return; }
            throw new Exception("GPC: Expected validation: " + contains);
        }
        Reject(() => Build(new[] { p1, p2 with { Order = 10 } }), "numbers");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Key = "G1" } } } }), "identifiers");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Strain = double.NaN } } } }), "finite");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Case = null } } } }), "Primary Only");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { BeamIds = new[] { "unknown" } } } } }), "unknown beam");
        Reject(() => Build(new[] { p1 with { Order = 30 }, p2 }), "steel-only");
        Reject(() => Build(table: new[] { new[] { "", "G1", "MISSING" }, new[] { "SLS", "1", "1" } }), "headers");
        // A duplicate sample never reaches the merge (rejected by the result mapper or by the merge).
        GPC.Model.Models.Model duplicate = null;
        try { duplicate = GpcWorkflowSources.Source("Shrinkage", 20, change: (t, beams, _) => t.Forces(beams[0], "G", 5000, 21)); }
        catch (InvalidOperationException e) when (e.Message.StartsWith("GpcTestModel results not imported")) { }
        if (duplicate != null) Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { GpcSource = duplicate } } } }), "expected one");
        // Primary-only shrinkage on B0: reported, and the recipe applies it to B0 only; the steel samples as given.
        var primary = Build(new[] { p1, p2 with { Actions = new[] { r with { Case = null, GpcSource = null, PrimaryOnly = true, BeamIds = new[] { "B0" } } } } });
        Require(primary.Warnings.Any(w => w.Contains("primary shrinkage only")), "GPC: Primary-only omission is hidden.");
        Samples(primary, "primary-only merge", ("G1 [FEM]", 100));
        var b0 = primary.Bridge.Model.BeamElements.Values.Single(b => b.Name == "B0").Guid;
        var only = primary.Bridge.Workflow.Phases[1].Actions.Single();
        Require(only.PrimaryOnly && !only.ForceCaseGuid.HasValue && only.BeamGuids.SequenceEqual(new[] { b0 }), "GPC: Strain applied outside selected beams.");
        // Incremental shrinkage: two increments in two phases, -120e-6 and -80e-6 with factor 1 (total -200e-6).
        var p3 = p2 with { Id = Guid.NewGuid(), Order = 30, Actions = new[] { r with { Key = "R2", Case = null, GpcSource = null, PrimaryOnly = true, Strain = -80e-6 } } };
        var increments = Build(new[] { p1, p2 with { Actions = new[] { r with { Strain = -120e-6 } } }, p3 }, table: new[] { new[] { "", "G1", "RITIRO", "R2" }, new[] { "SLS", "1", "1", "1" } });
        var strains = increments.Bridge.Workflow.Phases.SelectMany(p => p.Actions).Where(x => x.Strain.HasValue).ToArray();
        Require(strains.Length == 2 && Math.Abs(strains.Sum(x => x.Strain.Value * Factors(increments, x.Key + " [strain]").Single()) + 200e-6) < 1e-16, "GPC: Incremental shrinkage accumulated incorrectly.");
        var instant = Build(new[] { p1, p2 with { Actions = new[] { r with { GpcSource = null, Case = null, PrimaryOnly = true, Behavior = BridgeActionBehavior.ShortTerm } } } });
        var instantAction = instant.Bridge.Workflow.Phases[1].Actions.Single();
        Require(instantAction.Behavior == GPC.Checkers.CompositeBridge.BridgeActionBehavior.ShortTerm && instantAction.Strain == -200e-6, "GPC: Instant imposed strain assigned to long-term behavior.");
        // Additional deck member: rejected, or excluded with a warning; a changed geometry is rejected.
        void AddDeck(GpcTestModel t, GPC.Model.Elements.BeamElement[] beams, GPC.Model.Elements.NodeElement[] nodes) => t.Beam("Deck", t.Node("5", 0, 0, 1000), t.Node("6", 0, 2000, 1000));
        var withDeck = GpcWorkflowSources.Pair("Shrinkage", 20, change: AddDeck);
        var deckPhase = p2 with { Actions = new[] { r with { GpcSource = withDeck.Analysed } } };
        Reject(() => Build(new[] { p1, deckPhase }, new[] { pSteel, withDeck }), "beam count");
        var mixed = Build(new[] { p1, deckPhase }, new[] { pSteel, withDeck }, additional: true);
        Require(mixed.Bridge.Model.BeamElements.Count == 2 && mixed.Warnings.Any(w => w.Contains("additional phase members")), "GPC: Additional deck members explicitly excluded from longitudinal checking model");
        Samples(mixed, "merge with additional members", ("G1 [FEM]", 100), ("RITIRO [FEM]", 20));
        var moved = GpcWorkflowSources.Pair("Shrinkage", 20, change: AddDeck, firstEndZ: 1);
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { GpcSource = moved.Analysed } } } }, new[] { pSteel, moved }, additional: true), "geometry");
        Require(R2bFixtures.ResultTokens(gSteel).SequenceEqual(steelBefore) && R2bFixtures.ResultTokens(gShrink).SequenceEqual(shrinkBefore), "GPC: Source mutated.");
        Console.WriteLine($"PASS workflow on GPC data alone: {merges} GPC merges of sources built in GPC (recipe, combinations, case identity, increments, selection, validation, additional members); merged samples equal to the analytic source diagrams bit for bit, sources unchanged");
    }
}
