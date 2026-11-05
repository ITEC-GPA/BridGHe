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
        var steel = Source("Steel", 100); var shrink = Source("Shrinkage", 20);
        var original = ModelArchive.Write(steel);
        var a = new BridgeActionDefinition { Key = "G1", Source = steel, Case = "G", Behavior = BridgeActionBehavior.Steel };
        var r = new BridgeActionDefinition { Key = "RITIRO", Source = shrink, Case = "G", Behavior = BridgeActionBehavior.Shrinkage, Strain = -200e-6 };
        var p1 = new BridgePhaseDefinition { Order = 10, Name = "Getto", State = BridgeSectionState.Steel, Actions = new[] { a } };
        var p2 = new BridgePhaseDefinition { Order = 20, Name = "Misto", State = BridgeSectionState.Composite, Actions = new[] { r } };
        string[][] combinations = { new[] { "", "G1", "RITIRO" }, new[] { "SLS", "1", "0.7" } };
        BridgeWorkflowBuildResult Build(BridgePhaseDefinition[] phases = null, ModelModel[] models = null, string[][] table = null) =>
            BridgeWorkflowBuilder.Build(models ?? new[] { steel, shrink }, phases ?? new[] { p2, p1 }, table ?? combinations, BridgeWorkflowMethod.Compatible, 1e-6);
        var result = Build(); var model = result.Model;
        Require(model.BridgeWorkflow.Phases.Select(p => p.Order).SequenceEqual(new[] { 10, 20 }), "Phases not sorted explicitly.");
        Require(model.LoadCombinations.Single().Values.Where(v => v.Key.Name.StartsWith("RITIRO")).All(v => v.Value == .7), "Shrinkage branches use different coefficients.");
        var beam = model.Beams[0];
        var reference = new GPC.Geometry.CoordinateSystem(GPC.Geometry.Point3d.Origin, new GPC.Geometry.Vector3d(-1, 0, 0), new GPC.Geometry.Vector3d(0, -1, 0));
        var assembled = BridgePhaseAssembler.Assemble(beam, beam.Length, 5000, model.LoadCombinations.Single(), reference, 1e-6);
        Require(Math.Abs(assembled.StrainInfinite + 140e-6) < 1e-16, "Shrinkage strain scaled incorrectly.");
        Require(assembled.Shrinkage != null, "Shrinkage FEM lost.");
        Require(ModelArchive.Write(steel).SequenceEqual(original), "Source mutated.");
        var reordered = Build(models: new[] { shrink, steel }).Model;
        Require(reordered.LoadCases.Select(c => c.Name).SequenceEqual(model.LoadCases.Select(c => c.Name)), "Case identity depends on source order.");
        Require(reordered.Beams[0].Results.OfType<BeamForceResultModel>().Select(f => f.AxialForce).SequenceEqual(beam.Results.OfType<BeamForceResultModel>().Select(f => f.AxialForce)), "Source order changed forces.");
        var archived = ModelArchive.Clone(model);
        Require(archived.BridgeWorkflow.Phases[1].Id == p2.Id && archived.BridgeWorkflow.Phases[1].Actions[0].Strain == -200e-6, "Compiled workflow archive lost identity/strain.");
        var phaseCopy = ModelArchive.Clone(p2);
        Require(phaseCopy.Actions[0].Source.Beams.Count == 2, "Phase/action GH archive cannot retain source results.");
        var renumbered = Build(new[] { p1 with { Order = 5 }, p2 with { Order = 50 } }).Model;
        Require(renumbered.LoadCases.Select(c => c.Name).SequenceEqual(model.LoadCases.Select(c => c.Name)), "Renumbering changes action references.");
        void Reject(Action test, string contains)
        {
            try { test(); } catch (ArgumentException e) when (e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase)) { return; }
            throw new Exception("Expected validation: " + contains);
        }
        Reject(() => Build(new[] { p1, p2 with { Order = 10 } }), "numbers");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Key = "G1" } } } }), "identifiers");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Strain = double.NaN } } } }), "finite");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Case = null } } } }), "Primary Only");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { BeamIds = new[] { "unknown" } } } } }), "unknown beam");
        Reject(() => Build(new[] { p1 with { Order = 30 }, p2 }), "steel-only");
        Reject(() => Build(table: new[] { new[] { "", "G1", "MISSING" }, new[] { "SLS", "1", "1" } }), "headers");
        var duplicate = ModelArchive.Clone(shrink); duplicate.Beams[0].Results.Add(duplicate.Beams[0].Results[0]);
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Source = duplicate } } } }), "expected one");
        var primary = Build(new[] { p1, p2 with { Actions = new[] { r with { Case = null, Source = null, PrimaryOnly = true, BeamIds = new[] { "B0" } } } } });
        Require(primary.Warnings.Any(w => w.Contains("primary shrinkage only")), "Primary-only omission is hidden.");
        Require(!primary.Model.Beams[1].Loads.OfType<CompositeBeamSlabStrainModel>().Any(), "Strain applied outside selected beams.");
        var p3 = p2 with { Id = Guid.NewGuid(), Order = 30, Actions = new[] { r with { Key = "R2", Case = null, Source = null, PrimaryOnly = true, Strain = -80e-6 } } };
        var increments = Build(new[] { p1, p2 with { Actions = new[] { r with { Strain = -120e-6 } } }, p3 }, table: new[] { new[] { "", "G1", "RITIRO", "R2" }, new[] { "SLS", "1", "1", "1" } }).Model;
        var total = BridgePhaseAssembler.Assemble(increments.Beams[0], 10000, 5000, increments.LoadCombinations[0], reference, 1e-6);
        Require(Math.Abs(total.StrainInfinite + 200e-6) < 1e-16, "Incremental shrinkage accumulated incorrectly.");
        var instant = Build(new[] { p1, p2 with { Actions = new[] { r with { Source=null, Case=null, PrimaryOnly=true, Behavior=BridgeActionBehavior.ShortTerm } } } }).Model;
        var instantaneous = BridgePhaseAssembler.Assemble(instant.Beams[0],10000,5000,instant.LoadCombinations[0],reference,1e-6);
        Require(Math.Abs(instantaneous.StrainInstant + 140e-6) < 1e-16 && instantaneous.StrainInfinite == 0, "Instant imposed strain assigned to long-term behavior.");
        var withDeck = ModelArchive.Clone(shrink);
        withDeck.AddElements(new[] { new BeamModel("Deck", new Point3d(0,0,1000), new Point3d(0,2000,1000), BridgeAssignmentTests.Property()) { BeamId = "Deck" } });
        var deckPhase = p2 with { Actions = new[] { r with { Source = withDeck } } };
        Reject(() => Build(new[] { p1, deckPhase }, new[] { steel, withDeck }), "beam count");
        var mixed = BridgeWorkflowBuilder.Build(new[] { steel, withDeck }, new[] { p1, deckPhase }, combinations, BridgeWorkflowMethod.Compatible, 1e-6, true);
        Require(mixed.Model.Beams.Count == 2 && mixed.Warnings.Any(w => w.Contains("additional phase members")), "Additional deck members explicitly excluded from longitudinal checking model");
        Require(mixed.Model.Beams[0].Results.OfType<BeamForceResultModel>().Select(f => f.AxialForce).SequenceEqual(beam.Results.OfType<BeamForceResultModel>().Select(f => f.AxialForce)), "Additional members do not alter stored steel forces");
        withDeck.Beams[0].PointTo += new Vector3d(0,0,1);
        Reject(() => BridgeWorkflowBuilder.Build(new[] { steel, withDeck }, new[] { p1, deckPhase }, combinations, BridgeWorkflowMethod.Compatible, 1e-6, true), "geometry");
#if GPC_PILOT
        Gpc();
#endif
        Console.WriteLine("PASS workflow: numbering, source identity, joint coefficients, strain increments, selection, validation and archives");
    }
#if GPC_PILOT
    /// <summary>P2T: the workflow test on Build BridGHe Model on GPC, with action sources built in GPC (<see cref="GpcWorkflowSources"/>): the
    /// D3 recipe (phase order and identity, strains, beam selection, primary-only actions, behaviors), the combinations of the bridge model,
    /// case identity, the shared validation (same messages), additional members and geometry; every merge equal to the FEM one bit for bit.
    /// The checks of the phase assembly on the merged FEM beams (<c>BridgePhaseAssembler.Assemble</c>, the former BridGHe phases: R1) are
    /// expressed on the recipe and the combination factors; the GH archive of a phase with its FEM source has no GPC counterpart.</summary>
    private static void Gpc()
    {
        var steel = Source("Steel", 100); var shrink = Source("Shrinkage", 20);
        var gSteel = GpcWorkflowSources.Source("Steel", 100); var gShrink = GpcWorkflowSources.Source("Shrinkage", 20);
        var gpcOf = new System.Collections.Generic.Dictionary<ModelModel, GPC.Model.Models.Model> { [steel] = gSteel, [shrink] = gShrink };
        var a = new BridgeActionDefinition { Key = "G1", Source = steel, GpcSource = gSteel, Case = "G", Behavior = BridgeActionBehavior.Steel };
        var r = new BridgeActionDefinition { Key = "RITIRO", Source = shrink, GpcSource = gShrink, Case = "G", Behavior = BridgeActionBehavior.Shrinkage, Strain = -200e-6 };
        var p1 = new BridgePhaseDefinition { Order = 10, Name = "Getto", State = BridgeSectionState.Steel, Actions = new[] { a } };
        var p2 = new BridgePhaseDefinition { Order = 20, Name = "Misto", State = BridgeSectionState.Composite, Actions = new[] { r } };
        string[][] combinations = { new[] { "", "G1", "RITIRO" }, new[] { "SLS", "1", "0.7" } };
        int merges = 0;
        BridGHe.Common.Gpc.GpcBridgeBuild Build(BridgePhaseDefinition[] phases = null, ModelModel[] models = null, string[][] table = null, bool additional = false, bool parity = true)
        {
            models ??= new[] { steel, shrink }; phases ??= new[] { p2, p1 }; table ??= combinations;
            var gpc = GpcBridgeMerge.Build(models.Select(m => (m, gpcOf[m])).ToArray(), phases, table, "workflow " + merges, additional: additional);
            if (parity)
            {
                // The FEM merge of the same recipe without the GPC sources (the former path), bit for bit.
                var femPhases = phases.Select(p => p with { Actions = p.Actions.Select(x => x with { GpcSource = null }).ToArray() }).ToArray();
                GpcBridgeMerge.RequireSame(gpc, BridgeWorkflowBuilder.Build(models, femPhases, table, BridgeWorkflowMethod.Compatible, 1e-6, additional), "workflow " + merges);
            }
            merges++; return gpc;
        }
        static double[] Factors(BridGHe.Common.Gpc.GpcBridgeBuild b, string prefix) => b.Bridge.Model.Combinations.Values.Single().GetLoadCaseCoefficientsPair()
            .Where(v => v.Key.Name.StartsWith(prefix, StringComparison.Ordinal)).Select(v => v.Value).ToArray();
        var result = Build(); var recipe = result.Bridge.Workflow;
        Require(recipe.Phases.Select(p => p.Order).SequenceEqual(new[] { 10, 20 }), "GPC: Phases not sorted explicitly.");
        var shrinkFactors = Factors(result, "RITIRO");
        Require(shrinkFactors.Length == 2 && shrinkFactors.All(v => v == .7), "GPC: Shrinkage branches use different coefficients.");
        var ritiro = recipe.Phases[1].Actions.Single();
        Require(ritiro.Strain == -200e-6 && ritiro.ForceCaseGuid.HasValue && ritiro.StrainCaseGuid.HasValue && Math.Abs(ritiro.Strain.Value * shrinkFactors[0] + 140e-6) < 1e-16,
            "GPC: Shrinkage strain scaled incorrectly.");
        Require(GpcBridgeMerge.Samples(result, 0).Any(s => s.Case == "RITIRO [FEM]"), "GPC: Shrinkage FEM lost.");
        var reordered = Build(models: new[] { shrink, steel });
        Require(reordered.Bridge.Model.LoadCases.Keys.SequenceEqual(result.Bridge.Model.LoadCases.Keys), "GPC: Case identity depends on source order.");
        Require(GpcBridgeMerge.Samples(reordered, 0).Select(s => s.Sample.Axial).SequenceEqual(GpcBridgeMerge.Samples(result, 0).Select(s => s.Sample.Axial)), "GPC: Source order changed forces.");
        var archived = GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.Read(recipe.Save());
        Require(archived.Phases[1].Id == p2.Id && archived.Phases[1].Actions[0].Strain == -200e-6, "GPC: Compiled workflow archive lost identity/strain.");
        var renumbered = Build(new[] { p1 with { Order = 5 }, p2 with { Order = 50 } });
        Require(renumbered.Bridge.Model.LoadCases.Keys.SequenceEqual(result.Bridge.Model.LoadCases.Keys), "GPC: Renumbering changes action references.");
        void Reject(Action test, string contains)
        {
            try { test(); } catch (ArgumentException e) when (e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase)) { return; }
            throw new Exception("GPC: Expected validation: " + contains);
        }
        Reject(() => Build(new[] { p1, p2 with { Order = 10 } }, parity: false), "numbers");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Key = "G1" } } } }, parity: false), "identifiers");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Strain = double.NaN } } } }, parity: false), "finite");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Case = null } } } }, parity: false), "Primary Only");
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { BeamIds = new[] { "unknown" } } } } }, parity: false), "unknown beam");
        Reject(() => Build(new[] { p1 with { Order = 30 }, p2 }, parity: false), "steel-only");
        Reject(() => Build(table: new[] { new[] { "", "G1", "MISSING" }, new[] { "SLS", "1", "1" } }, parity: false), "headers");
        // A duplicate sample never reaches the merge (rejected by the result mapper or by the merge).
        GPC.Model.Models.Model duplicate = null;
        try { duplicate = GpcWorkflowSources.Source("Shrinkage", 20, change: (t, beams, _) => t.Forces(beams[0], "G", 5000, 21)); }
        catch (InvalidOperationException e) when (e.Message.StartsWith("GpcTestModel results not imported")) { }
        if (duplicate != null) Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { GpcSource = duplicate } } } }, parity: false), "expected one");
        // Primary-only shrinkage on B0: reported, and the recipe applies it to B0 only.
        var primary = Build(new[] { p1, p2 with { Actions = new[] { r with { Case = null, Source = null, GpcSource = null, PrimaryOnly = true, BeamIds = new[] { "B0" } } } } });
        Require(primary.Warnings.Any(w => w.Contains("primary shrinkage only")), "GPC: Primary-only omission is hidden.");
        var b0 = primary.Bridge.Model.BeamElements.Values.Single(b => b.Name == "B0").Guid;
        var only = primary.Bridge.Workflow.Phases[1].Actions.Single();
        Require(only.PrimaryOnly && !only.ForceCaseGuid.HasValue && only.BeamGuids.SequenceEqual(new[] { b0 }), "GPC: Strain applied outside selected beams.");
        // Incremental shrinkage: two increments in two phases, -120e-6 and -80e-6 with factor 1 (total -200e-6).
        var p3 = p2 with { Id = Guid.NewGuid(), Order = 30, Actions = new[] { r with { Key = "R2", Case = null, Source = null, GpcSource = null, PrimaryOnly = true, Strain = -80e-6 } } };
        var increments = Build(new[] { p1, p2 with { Actions = new[] { r with { Strain = -120e-6 } } }, p3 }, table: new[] { new[] { "", "G1", "RITIRO", "R2" }, new[] { "SLS", "1", "1", "1" } });
        var strains = increments.Bridge.Workflow.Phases.SelectMany(p => p.Actions).Where(x => x.Strain.HasValue).ToArray();
        Require(strains.Length == 2 && Math.Abs(strains.Sum(x => x.Strain.Value * Factors(increments, x.Key + " [strain]").Single()) + 200e-6) < 1e-16, "GPC: Incremental shrinkage accumulated incorrectly.");
        var instant = Build(new[] { p1, p2 with { Actions = new[] { r with { Source = null, GpcSource = null, Case = null, PrimaryOnly = true, Behavior = BridgeActionBehavior.ShortTerm } } } });
        var instantAction = instant.Bridge.Workflow.Phases[1].Actions.Single();
        Require(instantAction.Behavior == GPC.Checkers.CompositeBridge.BridgeActionBehavior.ShortTerm && instantAction.Strain == -200e-6, "GPC: Instant imposed strain assigned to long-term behavior.");
        // Additional deck member: rejected, or excluded with a warning; a changed geometry is rejected.
        var withDeck = ModelArchive.Clone(shrink);
        withDeck.AddElements(new[] { new BeamModel("Deck", new Point3d(0, 0, 1000), new Point3d(0, 2000, 1000), BridgeAssignmentTests.Property()) { BeamId = "Deck" } });
        void AddDeck(GpcTestModel t, GPC.Model.Elements.BeamElement[] beams, GPC.Model.Elements.NodeElement[] nodes) => t.Beam("Deck", t.Node("5", 0, 0, 1000), t.Node("6", 0, 2000, 1000));
        gpcOf[withDeck] = GpcWorkflowSources.Source("Shrinkage", 20, change: AddDeck);
        var deckPhase = p2 with { Actions = new[] { r with { Source = withDeck, GpcSource = gpcOf[withDeck] } } };
        Reject(() => Build(new[] { p1, deckPhase }, new[] { steel, withDeck }, parity: false), "beam count");
        var mixed = Build(new[] { p1, deckPhase }, new[] { steel, withDeck }, additional: true);
        Require(mixed.Bridge.Model.BeamElements.Count == 2 && mixed.Warnings.Any(w => w.Contains("additional phase members")), "GPC: Additional deck members explicitly excluded from longitudinal checking model");
        Require(GpcBridgeMerge.Samples(mixed, 0).Where(s => s.Case == "G1 [FEM]").Select(s => s.Sample.Axial).SequenceEqual(GpcBridgeMerge.Samples(result, 0).Where(s => s.Case == "G1 [FEM]").Select(s => s.Sample.Axial)),
            "GPC: Additional members do not alter stored steel forces");
        var moved = ModelArchive.Clone(withDeck); moved.Beams[0].PointTo += new Vector3d(0, 0, 1);
        gpcOf[moved] = GpcWorkflowSources.Source("Shrinkage", 20, change: AddDeck, firstEndZ: 1);
        Reject(() => Build(new[] { p1, p2 with { Actions = new[] { r with { Source = moved, GpcSource = gpcOf[moved] } } } }, new[] { steel, moved }, additional: true, parity: false), "geometry");
        Console.WriteLine($"PASS workflow on GPC: {merges} GPC merges of sources built in GPC (recipe, combinations, case identity, increments, selection, validation, additional members); equal to the FEM merges bit for bit");
    }
#endif
}
