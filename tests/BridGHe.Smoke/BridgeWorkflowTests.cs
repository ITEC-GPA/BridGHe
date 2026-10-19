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
        Console.WriteLine("PASS workflow: numbering, source identity, joint coefficients, strain increments, selection, validation and archives");
    }
}
