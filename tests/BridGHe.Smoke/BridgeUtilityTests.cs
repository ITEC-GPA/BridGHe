using System;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using Rhino.Geometry;

internal static class BridgeUtilityTests
{
    private static int checks;
    internal static void Assert(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
    private static void Reject(Action call, string message)
    {
        try { call(); } catch (ArgumentException) { checks++; return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }
    /// <param name="step">Step of the Q results (2 "Moving": a former moving-load step; P2T parity: 0 "", the only step GPC results have).</param>
    internal static ModelModel Source(int step = 2, string stepType = "Moving")
    {
        var model = BridgeWorkflowTests.Source("Controlled result samples N mm", 100);
        var q = new LoadCaseModel("Q", "Q"); model.LoadCases.Add(q);
        foreach (var b in model.Beams)
            foreach (var f in b.Results.OfType<BeamForceResultModel>().ToArray())
                b.Results.Add(new BeamForceResultModel(q, f.Station * b.Length, step, stepType, false) { AxialForce = -50, Vx = 13, Vy = -17, Torque = 19, Mx = -23, My = -2 * f.My });
        var node = new NodeModel("A", Point3d.Origin); node.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, false, false, false));
        node.Results.Add(new NodeReactResultModel(model.LoadCases[0]) { Force = new Vector3d(10, 20, 100), Moment = new Vector3d(1, 2, 3) });
        node.Results.Add(new NodeReactResultModel(q, step, stepType) { Force = new Vector3d(50, 60, -120), Moment = new Vector3d(4, 5, 6) });
        node.Results.Add(new NodeDisplResultModel(model.LoadCases[0]) { Displacement = new Vector3d(1, 2, -10), Rotation = new Vector3d(.1, .2, .3) });
        node.Results.Add(new NodeDisplResultModel(q, step, stepType) { Displacement = new Vector3d(3, 4, 8), Rotation = new Vector3d(.4, .5, .6) });
        model.AddElements(new[] { node }); return model;
    }
#if GPC_PILOT
    /// <summary>P2T: <see cref="Source"/> built directly in GPC (no FEM model): beams B0 and B1 and the same samples of cases G and Q; Q is a
    /// plain case (step 0), the only step GPC results have. The GPC result mapper needs the beam nodes: the bearing node A (origin, start of
    /// B0, the only node with results, as in the FEM fixture) and the nodes 2, 3, 4 at the other beam ends, without results.
    /// <paramref name="change"/> adds samples before the analysis (a duplicate or non-finite sample); <paramref name="incomplete"/> leaves
    /// out the Q sample at the start of B0 (the FEM check «Incomplete case grid hidden»).</summary>
    internal static GPC.Model.Models.Model SourceGpc(Action<GpcTestModel, GPC.Model.Elements.BeamElement[]> change = null, bool incomplete = false, bool analyse = true)
    {
        var test = new GpcTestModel("Controlled result samples N mm" + (incomplete ? ", incomplete" : "")); test.Case("G"); test.Case("Q");
        var nodes = new[] { test.Node("A", 0, 0, 0), test.Node("2", 10000, 0, 0), test.Node("3", 0, 2000, 0), test.Node("4", 10000, 2000, 0) };
        var beams = Enumerable.Range(0, 2).Select(i => test.Beam("B" + i, nodes[2 * i], nodes[2 * i + 1])).ToArray();
        for (int i = 0; i < 2; i++)
            foreach (double x in new[] { 0d, .5, 1d }) test.Forces(beams[i], "G", x * 10000, 100 + i, bm2: 100 * 1000 * (1 + x));
        for (int i = 0; i < 2; i++)
            foreach (double x in new[] { 0d, .5, 1d })
                if (!incomplete || i != 0 || x != 0) test.Forces(beams[i], "Q", x * 10000, -50, 13, -17, 19, -23, -2 * (100 * 1000 * (1 + x)));
        test.Reaction(nodes[0], "G", 10, 20, 100, 1, 2, 3).Reaction(nodes[0], "Q", 50, 60, -120, 4, 5, 6);
        test.Displacement(nodes[0], "G", 1, 2, -10, .1, .2, .3).Displacement(nodes[0], "Q", 3, 4, 8, .4, .5, .6);
        change?.Invoke(test, beams);
        return analyse ? test.Analyse() : test.Structure();
    }

    /// <summary>The FEM fixture of the parity: <see cref="Source"/> with Q at step 0 and the same nodes as <see cref="SourceGpc"/>.</summary>
    private static ModelModel ParitySource()
    {
        var model = Source(0, "");
        model.AddElements(new[] { new NodeModel("2", new Point3d(10000, 0, 0)), new NodeModel("3", new Point3d(0, 2000, 0)), new NodeModel("4", new Point3d(10000, 2000, 0)) });
        return model;
    }

    /// <summary>P2T: the envelope and stored-result checks on the analysed GPC model built in GPC, with the meaning of the FEM checks of
    /// <see cref="Run"/> (Q at step 0, the only GPC step); then the GPC path equal bit for bit to the FEM path on the same samples.</summary>
    private static void Gpc(ModelModel model)
    {
        var gpc = SourceGpc();
        var env = BridgeResultEnvelope.Evaluate(gpc, 0, null, null, 5, 2);
        Assert(env.Length == 6 && env.All(r => r.Case == "Q" && r.Step == 0 && r.StepType == "" && r.Value < 0), "GPC: Signed absolute beam envelope incorrect.");
        Assert(env[1].Station == 5000 && env.All(r => r.Components.SequenceEqual(new[] { -50d, 13, -17, 19, -23, r.Value })), "GPC: Normalized stations/concomitants lost.");
        Assert(BridgeResultEnvelope.Evaluate(gpc, 0, null, null, 5, 1).All(r => r.Case == "G"), "GPC: Maximum envelope incorrect.");
        Assert(BridgeResultEnvelope.Evaluate(gpc, 0, new[] { "B0" }, new[] { "G" }, 0, 0).Length == 3, "GPC: Selection failed.");
        var reaction = BridgeResultEnvelope.Evaluate(gpc, 1, new[] { "A" }, null, 2, 0).Single();
        Assert(reaction.Value == -120 && reaction.Components[0] == 50 && reaction.Case == "Q", "GPC: Bearing reactions are not concomitant.");
        Assert(BridgeResultEnvelope.Evaluate(gpc, 2, new[] { "A" }, null, 2, 2).Single().Value == -10, "GPC: Displacement sign lost.");
        Reject(() => BridgeResultEnvelope.Evaluate(gpc, 2, null, null, 2, 2), "GPC: nodes without results (the beam end nodes) not reported");
        Reject(() => BridgeResultEnvelope.Evaluate(gpc, 0, null, new[] { "missing" }, 0, 0), "GPC: unknown case");
        Reject(() => BridgeResultEnvelope.Evaluate(gpc, 0, new[] { "missing" }, null, 0, 0), "GPC: unknown element");
        Reject(() => BridgeResultEnvelope.Evaluate(gpc, 0, null, null, 6, 0), "GPC: invalid component");
        // The FEM fixture removes the Q sample at the start of B0: here it is never stored.
        var incomplete = SourceGpc(incomplete: true);
        Assert(BridgeResultEnvelope.Evaluate(incomplete, 0, null, null, 5, 2).Any(r => r.Coverage.StartsWith("Missing")), "GPC: Incomplete case grid hidden.");
        // A duplicate or non-finite sample never reaches the envelope: rejected by the result mapper or by the envelope.
        RejectGpc(() => BridgeResultEnvelope.Evaluate(SourceGpc((t, b) => t.Forces(b[0], "G", 0, 100, bm2: 100000)), 0, null, null, 0, 0), "GPC: duplicate sample");
        RejectGpc(() => BridgeResultEnvelope.Evaluate(SourceGpc((t, b) => t.Forces(b[0], "Q", 2500, double.NaN, 13, -17, 19, -23, -250000)), 0, null, null, 5, 2), "GPC: NaN concomitant");
        // Model audit, stored-result part: the GPC model without findings; a result without case identity is an error.
        string Issues(ModelIssue[] issues) => string.Join("\n", issues.Select(i => i.Severity + "|" + i.Code + "|" + i.Element + "|" + i.Description));
        Assert(Issues(BridgeModelAudit.Inspect(model, 1e-6, analysed: gpc)) == Issues(BridgeModelAudit.Inspect(model, 1e-6)), "GPC: audit of the stored results differs.");
        var catalog = BridGHe.Common.Gpc.GpcResultCatalog.Of(gpc);
        BridGHe.Common.Gpc.GpcResultCatalog.Attach(gpc, catalog.Units.Units, catalog.Entries.Append(new BridGHe.Common.Gpc.GpcResultCatalog.Entry { Label = "", Case = "G" }).ToArray(),
            gpc.BeamElements.Values.ToDictionary(b => b.Id, catalog.BeamAngle));
        Assert(BridgeModelAudit.Inspect(model, 1e-6, analysed: gpc).Any(i => i.Code == "INVALID_RESULT"), "GPC: result without case identity not reported.");
        // Parity (removed with the FeMM models): the GPC path on the model built in GPC equals the FEM path on the FEM fixture with the
        // same samples (Q at step 0), bit for bit: envelopes of every kind, component and mode, result cases and fatigue histories.
        // The node envelopes of the bearing node (ConsumersCheck selects every node: the beam end nodes give the same error on both paths).
        var fem = ParitySource(); var analysed = SourceGpc();
        long values = ConsumersCheck.Verify(fem, analysed, "bridge utilities, model built in GPC");
        static string Bits(double v) => BitConverter.DoubleToInt64Bits(v == 0 ? 0.0 : v).ToString("X16");
        static string Row(EnvelopeRow r) => string.Join("|", new[] { r.Element, Bits(r.Station), r.Case, Bits(r.Step), r.StepType, Bits(r.Value), r.Coverage }.Concat(r.Components.Select(Bits)));
        foreach (var incompleteGrid in new[] { false, true })
        {
            var f = ParitySource(); var g = SourceGpc(incomplete: incompleteGrid);
            if (incompleteGrid) f.Beams[0].Results.RemoveAt(3);
            for (int kind = 0; kind < 3; kind++)
                for (int component = 0; component < 6; component++)
                    for (int mode = 0; mode < 3; mode++)
                    {
                        var ids = kind == 0 ? null : new[] { "A" };
                        Assert(BridgeResultEnvelope.Evaluate(f, kind, ids, null, component, mode).Select(Row).SequenceEqual(BridgeResultEnvelope.Evaluate(g, kind, ids, null, component, mode).Select(Row)),
                            $"GPC envelope equal to the FEM one bit for bit (kind {kind}, component {component}, mode {mode}, incomplete {incompleteGrid})");
                        values++;
                    }
        }
        Assert(values > 0, "GPC: parity compared no value.");
    }

    /// <summary>P2T: Bridge Phase Model on GPC (<c>GpcModelTransforms.PhaseModel</c>, chain D) on the input model built in GPC
    /// (<see cref="SourceGpc"/> without results, through the public input <c>GpcModels.Conversion.FromGpc</c>) with a replacement property built
    /// in GPC (<c>GpcModels.BeamProperty</c>): the same checks as the FEM ones (selection and property, no stale data, IDs and geometry, input
    /// isolated, loads retained on request, unknown and empty selections rejected), then the parity with the FEM path: the beams that change
    /// property, the geometry and the cases equal to those of the conversion of the FEM output (the default «Dead» of the legacy reader apart;
    /// no section state in this test, so the stricter GPC rule on stale states does not apply).</summary>
    private static void PhaseGpc(ModelModel model, BeamPropertyModel femProperty, ModelModel femPhase)
    {
        var input = SourceGpc(analyse: false);
        var units = new BridGHe.Common.Gpc.BridgeUnits(BridGHe.Common.Gpc.DefinitionUnits.Nmm);
        var angles = input.BeamElements.Values.ToDictionary(b => b.Id, _ => 0d);
        BridGHe.Common.Gpc.GpcModels.Conversion Input() => BridGHe.Common.Gpc.GpcModels.Conversion.FromGpc(input, units, angles, 1e-6);
        var original = (GPC.Model.ElementProperties.ElasticBeamProperty)input.BeamElements.Values.Single(b => b.Name == "B0").BeamProperty;
        var replacement = new GpcTestModel("Phase properties").Property("Long term", area: 2 * original.Section.Area);
        var data = BridGHe.Common.Gpc.GpcModels.BeamProperty(replacement, GPC.Model.Structure.Assignments.BeamFormulation.StraightTwoNode);
        GPC.Model.Models.Model Phase(string name, string[] ids, bool clear) =>
            BridGHe.Common.Gpc.GpcModelTransforms.PhaseModel(Input(), name, ids, data, clear, BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace, "p2t-phase:" + name)).Model;
        static double Area(GPC.Model.Elements.BeamElement b) => ((GPC.Model.ElementProperties.ElasticBeamProperty)b.BeamProperty).Section.Area;
        var phase = Phase("Long term", new[] { "B0" }, true);
        var beams = phase.BeamElements.Values.OrderBy(b => b.Id).ToArray(); var inputBeams = input.BeamElements.Values.OrderBy(b => b.Id).ToArray();
        Assert(Area(beams[0]) == replacement.Section.Area && Area(beams[1]) == original.Section.Area && beams[0].BeamProperty.Name == "Long term", "GPC: Phase selection/property failed.");
        Assert(phase.AllElements.All(e => e.Results.Count == 0) && phase.Datasets.Count == 0 && beams.All(b => b.Assignments.Loads.Count == 0) && phase.LoadCases.Keys.All(c => c == "Dead"), "GPC: Phase retained stale data.");
        Assert(beams.Select(b => b.Name).SequenceEqual(inputBeams.Select(b => b.Name)) && beams[0].StartPoint.X == inputBeams[0].StartPoint.X && beams[0].StartPoint.Y == inputBeams[0].StartPoint.Y
            && beams[0].EndPoint.X == inputBeams[0].EndPoint.X, "GPC: Phase changed IDs/geometry.");
        Assert(!ReferenceEquals(phase, input) && ReferenceEquals(inputBeams[0].BeamProperty, original) && Area(inputBeams[0]) == original.Section.Area, "GPC: Phase output aliases its input.");
        var retained = Phase("Q", new[] { "B0" }, false);
        Assert(retained.LoadCases.ContainsKey("G") && retained.LoadCases.ContainsKey("Q"), "GPC: Retain-loads option lost cases.");
        Reject(() => Phase("P", new[] { "missing" }, true), "GPC: unknown beam");
        Reject(() => Phase("P", Array.Empty<string>(), true), "GPC: empty selection");
        // Parity (removed with the FeMM models): the same transformation on the FEM path (Bridge Phase Model on the FEM fixture; its beams
        // have no nodes, which the legacy reader refuses, so the FEM output is compared as it is, not converted).
        Assert(femPhase.Beams.Select(b => b.BeamId).SequenceEqual(beams.Select(b => b.Name)), "GPC phase: beam IDs equal to the FEM path");
        for (int k = 0; k < beams.Length; k++)
        {
            bool gpcChanged = !ReferenceEquals(beams[k].BeamProperty, inputBeams[k].BeamProperty) && beams[k].BeamProperty.Name != inputBeams[k].BeamProperty.Name;
            bool femChanged = femPhase.Beams[k].BeamProperty.SectionArea != model.Beams[k].BeamProperty.SectionArea;
            Assert(gpcChanged == femChanged, $"GPC phase: property change of beam {beams[k].Name} as on the FEM path");
            var (from, to) = (femPhase.Beams[k].PointFrom, femPhase.Beams[k].PointTo);
            Assert(beams[k].StartPoint.X == from.X && beams[k].StartPoint.Y == from.Y && beams[k].StartPoint.Z == from.Z
                && beams[k].EndPoint.X == to.X && beams[k].EndPoint.Y == to.Y && beams[k].EndPoint.Z == to.Z, $"GPC phase: geometry of beam {beams[k].Name} as on the FEM path");
        }
        var femRetained = BridgePhaseModel.Create(model, "Q", new[] { "B0" }, femProperty, false);
        Assert(retained.LoadCases.Keys.Where(c => c != "Dead").SequenceEqual(femRetained.LoadCases.Select(c => c.Name)) && phase.LoadCases.Keys.Where(c => c != "Dead").SequenceEqual(femPhase.LoadCases.Select(c => c.Name)),
            "GPC phase: cases as on the FEM path");
    }

    private static void RejectGpc(Action call, string message)
    {
        try { call(); }
        catch (ArgumentException) { checks++; return; }
        catch (InvalidOperationException e) when (e.Message.StartsWith("GpcTestModel results not imported")) { checks++; return; }
        throw new InvalidOperationException("Expected rejection: " + message);
    }
#endif
    internal static MovingPath Path() => new()
    {
        LoadCaseName = "Prototype", Shape = 0, Coordinates = new[] { 0d, 0, 0, 20000, 0, 0, 0, 1000, 0 },
        Template = new MovingTemplate { Vehicles = new[] { new MovingVehicle { Loads = new MovingObject[] { new MovingPointForce { Force = new[] { 0d, 0, -100000 } } } } } }
    };
    internal static void Run()
    {
        checks = 0; var model = Source(); var before = ModelArchive.Write(model);
        var issues = BridgeModelAudit.Inspect(model, 1e-6);
        Assert(!issues.Any(i => i.Severity == "Error"), "Audit rejected valid input.");
        Assert(issues.Any(i => i.Code == "DISCONNECTED"), "Audit missed disconnected girders.");
        Assert(!issues.Any(i => i.Code == "ISOLATED_NODE"), "Audit missed connected support.");
        var bad = ModelArchive.Clone(model); bad.AddElements(new[] { new NodeModel("Floating", new Point3d(700, 900, 800)) });
        Assert(BridgeModelAudit.Inspect(bad, 1e-6).Any(i => i.Code == "ISOLATED_NODE" && i.Element == "Floating"), "Isolated node not detected.");
        bad.Beams[0].PointTo = bad.Beams[0].PointFrom;
        Assert(BridgeModelAudit.Inspect(bad, 1e-6).Any(i => i.Code == "INVALID_BEAM"), "Zero length missed.");
        Reject(() => BridgeModelAudit.Inspect(model, 0), "zero tolerance");
#if GPC_PILOT
        // P2T: envelopes and the stored-result audit on the analysed GPC model built in GPC (they stay after the FeMM removal), and their
        // parity with the FEM path. The envelope checks on the FEM fixture below remain until the FeMM removal.
        Gpc(model);
#endif
        var property = ModelArchive.Clone(model.Beams[0].BeamProperty); property.Section.SectionArea *= 2;
        var phase = BridgePhaseModel.Create(model, "Long term", new[] { "B0" }, property, true);
        Assert(phase.Beams[0].SectionAreaForTest() == property.SectionArea && phase.Beams[1].BeamProperty.SectionArea == model.Beams[1].BeamProperty.SectionArea, "Phase selection/property failed.");
        Assert(phase.Elements.All(e => e.Results.Count == 0 && e.Loads.Count == 0) && phase.LoadCases.Count == 0, "Phase retained stale data.");
        Assert(phase.Beams.Select(b => b.BeamId).SequenceEqual(model.Beams.Select(b => b.BeamId)) && phase.Beams[0].PointFrom == model.Beams[0].PointFrom, "Phase changed IDs/geometry.");
        phase.Beams[0].BeamProperty.Section.SectionArea += 1;
        Assert(property.SectionArea != phase.Beams[0].BeamProperty.SectionArea, "Phase property aliases input.");
        Assert(BridgePhaseModel.Create(model, "Q", new[] { "B0" }, property, false).LoadCases.Count == 2, "Retain-loads option lost cases.");
        Reject(() => BridgePhaseModel.Create(model, "P", new[] { "missing" }, property, true), "unknown beam");
        Reject(() => BridgePhaseModel.Create(model, "P", Array.Empty<string>(), property, true), "empty selection");
#if GPC_PILOT
        PhaseGpc(model, property, phase);
#endif
        var env = BridgeResultEnvelope.Evaluate(model, 0, null, null, 5, 2);
        Assert(env.Length == 6 && env.All(r => r.Case == "Q" && r.Step == 2 && r.StepType == "Moving" && r.Value < 0), "Signed absolute beam envelope incorrect.");
        Assert(env[1].Station == 5000 && env.All(r => r.Components.SequenceEqual(new[] { -50d, 13, -17, 19, -23, r.Value })), "Normalized stations/concomitants lost.");
        Assert(BridgeResultEnvelope.Evaluate(model, 0, null, null, 5, 1).All(r => r.Case == "G"), "Maximum envelope incorrect.");
        Assert(BridgeResultEnvelope.Evaluate(model, 0, new[] { "B0" }, new[] { "G" }, 0, 0).Length == 3, "Selection failed.");
        var reaction = BridgeResultEnvelope.Evaluate(model, 1, new[] { "A" }, null, 2, 0).Single();
        Assert(reaction.Value == -120 && reaction.Components[0] == 50 && reaction.Case == "Q", "Bearing reactions are not concomitant.");
        Assert(BridgeResultEnvelope.Evaluate(model, 2, null, null, 2, 2).Single().Value == -10, "Displacement sign lost.");
        Reject(() => BridgeResultEnvelope.Evaluate(model, 0, null, new[] { "missing" }, 0, 0), "unknown case");
        Reject(() => BridgeResultEnvelope.Evaluate(model, 0, new[] { "missing" }, null, 0, 0), "unknown element");
        Reject(() => BridgeResultEnvelope.Evaluate(model, 0, null, null, 6, 0), "invalid component");
        bad = ModelArchive.Clone(model); bad.Beams[0].Results.RemoveAt(3);
        Assert(BridgeResultEnvelope.Evaluate(bad, 0, null, null, 5, 2).Any(r => r.Coverage.StartsWith("Missing")), "Incomplete case grid hidden.");
        bad = ModelArchive.Clone(model); bad.Beams[0].Results.Add(bad.Beams[0].Results[0]);
        Reject(() => BridgeResultEnvelope.Evaluate(bad, 0, null, null, 0, 0), "duplicate sample");
        bad = ModelArchive.Clone(model); ((BeamForceResultModel)bad.Beams[0].Results[0]).AxialForce = double.NaN;
        Reject(() => BridgeResultEnvelope.Evaluate(bad, 0, null, null, 5, 2), "NaN concomitant");
        var action = new BridgeActionDefinition { Key = "G1", Source = model, Case = "G" };
        var shrink = new BridgeActionDefinition { Key = "CS", PrimaryOnly = true, Strain = -200e-6, Behavior = BridgeActionBehavior.Shrinkage };
        var p1 = new BridgePhaseDefinition { Order = 10, Name = "Steel", State = BridgeSectionState.Steel, Day = 1, Actions = new[] { action } };
        var p2 = new BridgePhaseDefinition { Order = 20, Name = "Shrinkage", State = BridgeSectionState.Composite, Day = 28, Actions = new[] { shrink } };
        var table = BridgeWorkflowTools.CombinationTable(new[] { p2, p1 }, new[] { "CS", "G1" }, new[] { "SLS" }, new[] { new[] { .7, 1d } });
        Assert(table[0].SequenceEqual(new[] { "Combination", "G1", "CS" }) && table[1].SequenceEqual(new[] { "SLS", "1", "0.7" }), "Combination keys reordered incorrectly.");
        var built = BridgeWorkflowBuilder.Build(new[] { model }, new[] { p2, p1 }, table, BridgeWorkflowMethod.Compatible, 1e-6);
        Assert(built.Model.LoadCombinations.Single().Values.Any(v => Math.Abs(v.Value - .7) < 1e-15), "Generated table not accepted by builder.");
        Assert(BridgeWorkflowTools.CombinationTable(new[] { p1, p2 }, new[] { "G1" }, new[] { "SLS" }, new[] { new[] { 1d } })[1][2] == "0", "Omitted action not zero.");
        Reject(() => BridgeWorkflowTools.Ordered(new[] { p1, p2 with { Order = 10 } }), "duplicate phase");
        Reject(() => BridgeWorkflowTools.Ordered(new[] { p1, p2 with { Day = 0 } }), "backward day");
        Reject(() => BridgeWorkflowTools.CombinationTable(new[] { p1 }, new[] { "bad" }, new[] { "S" }, new[] { new[] { 1d } }), "unknown action");
        Reject(() => BridgeWorkflowTools.CombinationTable(new[] { p1 }, new[] { "G1" }, new[] { "S" }, new[] { new[] { double.NaN } }), "NaN coefficient");
        var increments = BridgeWorkflowTools.ShrinkageIncrements(new[] { 7d, 28, 100 }, new[] { -100e-6, -200e-6, -250e-6 }, -50e-6);
        Assert(Math.Abs(increments.Sum() + 200e-6) < 1e-16 && Math.Abs(increments[0] + 50e-6) < 1e-16, "Shrinkage increment balance incorrect.");
        Reject(() => BridgeWorkflowTools.ShrinkageIncrements(new[] { 1d, 1 }, new[] { -1e-6, -2e-6 }, 0), "duplicate day");
        Reject(() => BridgeWorkflowTools.ShrinkageIncrements(new[] { 1d, 2 }, new[] { -2e-6, -1e-6 }, 0), "non-monotonic shrinkage");
        var compare = BridgeResultComparison.Compare(new[] { "a", "b", "c" }, new[] { 100d, 0, -100 }, new[] { "c", "a", "b" }, new[] { -100d, 100.5, 1e-8 }, 1e-6, .01);
        Assert(compare.All(r => r.WithinTolerance) && double.IsNaN(compare[1].RelativeDifference) && compare[0].Difference == .5, "Keyed numerical comparison failed.");
        Assert(!BridgeResultComparison.Compare(new[] { "a" }, new[] { 0d }, new[] { "a" }, new[] { 1d }, 1e-6, .01)[0].WithinTolerance, "Near-zero mismatch passed.");
        Reject(() => BridgeResultComparison.Compare(new[] { "a" }, new[] { 1d }, new[] { "b" }, new[] { 1d }, 0, 0), "different physical keys");
        Reject(() => BridgeResultComparison.Compare(new[] { "a" }, new[] { 1d }, new[] { "a" }, new[] { double.NaN }, 0, 0), "NaN candidate");
        var prototype = Path(); var lanes = BridgeMovingLanes.Create(prototype, new[] { -3000d, 0, 3000 }, new[] { "L1", "L2", "L3" });
        Assert(lanes[0].Coordinates[1] == -3000 && lanes[2].Coordinates[7] == 4000, "Lane offset coordinates incorrect.");
        lanes[0].Template.Vehicles[0].Loads.ClearForTest();
        Assert(prototype.Template.Vehicles[0].Loads.Length == 1 && lanes[1].Template.Vehicles[0].Loads[0] is MovingPointForce, "Lane templates alias.");
        prototype.Shape = 1; Reject(() => BridgeMovingLanes.Create(prototype, new[] { 0d }, new[] { "C" }), "curved prototype");
        prototype.Shape = 0; Reject(() => BridgeMovingLanes.Create(prototype, new[] { 0d, 0 }, new[] { "C", "D" }), "duplicate lane");
        Assert(ModelArchive.Write(model).SequenceEqual(before), "Utilities mutated source model.");
        Console.WriteLine($"BRIDGE UTILITIES PASS: {checks} checks; no Straus solver invoked.");
    }
    private static double SectionAreaForTest(this BeamModel beam) => beam.BeamProperty.SectionArea;
    private static void ClearForTest(this MovingObject[] loads) { loads[0] = new MovingHeatSource { SizeX = 1, SizeY = 1, Heat = 10 }; }
}
