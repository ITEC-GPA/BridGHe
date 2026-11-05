using System;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using BridgeActionBehavior = BridGHe.Common.Checks.BridgeActionBehavior;
using BridgeWorkflowMethod = BridGHe.Common.Checks.BridgeWorkflowMethod;
using GPC.Geometry;
using GPC.Model.Materials;
using GPC.Model.Sections;

internal static class BridgeHistoryWorkflowTests
{
    internal static void Run()
    {
        var concrete = new ConcreteMaterialEN1992("C30", 30, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
        var steel = new SteelMaterialEN1992("S355", 210000, 355, 510, .1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
        var section = CompositeSectionFactory.Create(2200, 250, concrete, null, 100, 40, null, 100, new SectionH(1400, 16, 450, 30, 600, 40, "H"), steel, 40, 0, "History test");
        var checker = new BridgeCheckAdapter(section, CoordinateSystem.Global, new BridgeAnalysisOptions { Class4 = false });
        var source = BridgeWorkflowTests.Source("Reference", 100000);
        foreach (var f in source.Beams.SelectMany(b => b.Results).OfType<BeamForceResultModel>()) f.My = 0;
        BridgeActionDefinition Shrink(string key, double increment) => new() { Key = key, Behavior = BridgeActionBehavior.Shrinkage, Strain = increment, PrimaryOnly = true };
        BridgePhaseDefinition Phase(int order, BridgeSectionState state, params BridgeActionDefinition[] actions) => new() { Order = order, Name = "Step " + order, State = state, Actions = actions };
        BridgeStressResult Solve(BridgePhaseDefinition[] phases, params string[] keys)
        {
            var table = new[] { new[] { "" }.Concat(keys).ToArray(), new[] { "SLS" }.Concat(keys.Select(k => "1")).ToArray() };
            var model = BridgeWorkflowBuilder.Build(new[] { source }, phases, table, BridgeWorkflowMethod.LinearHistory, 1e-6).Model;
            var index = BridgePhaseAssembler.CreateIndex(model.Beams[0], model.Beams[0].Length, 1e-6);
            return checker.AnalyzeHistory(model.BridgeWorkflow, index, 0, model.LoadCombinations[0], 2, 0, 1, false);
        }
        void Near(double actual, double expected, string label) => BridgeWorkflowTests.Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= 1e-8 * Math.Max(1, Math.Abs(expected)), label + $": {actual:G17} != {expected:G17}");
        var one = Solve(new[] { Phase(10, BridgeSectionState.Steel), Phase(20, BridgeSectionState.Composite, Shrink("R", -200e-6)) }, "R");
        var two = Solve(new[] { Phase(10, BridgeSectionState.Steel), Phase(20, BridgeSectionState.Composite, Shrink("R1", -120e-6)), Phase(30, BridgeSectionState.Composite, Shrink("R2", -80e-6)) }, "R1", "R2");
        Near(two.Point1_ConcreteSigma, one.Point1_ConcreteSigma, "Incremental vs one-step shrinkage, concrete");
        Near(two.Point8_SteelSigma, one.Point8_SteelSigma, "Incremental vs one-step shrinkage, steel");
        // Independent rectangular-area integration: K*[epsilon0,slope] = integral(E*epsilon_cs*[1,y])dA.
        // The older ThinWall solver does not integrate the plate thickness identically; it is not the analytical oracle.
        double ea = 210000, ec = concrete.ElasticModulusCompression / 2, ac = 2200 * 250, yc = 125;
        var plates = new[] { (A: 450d * 30, Y: -15d, H: 30d), (A: 16d * 1330, Y: -695d, H: 1330d), (A: 600d * 40, Y: -1380d, H: 40d) };
        double k11 = ea * plates.Sum(p => p.A) + ec * ac;
        double k12 = ea * plates.Sum(p => p.A * p.Y) + ec * ac * yc;
        double k22 = ea * plates.Sum(p => p.A * (p.Y * p.Y + p.H * p.H / 12)) + ec * ac * (yc * yc + 250d * 250 / 12);
        double nc = ec * ac * -200e-6, mc = nc * yc, determinant = k11 * k22 - k12 * k12;
        double epsilon0 = (nc * k22 - mc * k12) / determinant, slope = (mc * k11 - nc * k12) / determinant;
        Near(one.Point1_ConcreteSigma, ec * (epsilon0 + slope * 250 + 200e-6), "History vs analytical rectangular integration, concrete");
        Near(one.Point8_SteelSigma, ea * (epsilon0 - slope * 1400), "History vs analytical rectangular integration, steel");
        var load = new BridgeActionDefinition { Key = "G", Source = source, Case = "G", Behavior = BridgeActionBehavior.Steel };
        var activation = Solve(new[] { Phase(10, BridgeSectionState.Steel, load), Phase(20, BridgeSectionState.Composite) }, "G");
        Near(activation.Point1_ConcreteSigma, 0, "Concrete starts stress-free after loaded steel");
        BridgeWorkflowTests.Require(activation.Analysis.Stages.Count == 2 && activation.Stage.GetHistory() != null, "Activation-only phase not retained in history.");
        var final = activation.Stage.GetHistory().State;
        Near(final.IntegratedN, 100000, "History sectional force equilibrium");
        Near(final.ForceResidual, 0, "History force residual");
        BridgeWorkflowTests.Require(double.IsNaN(activation.Point6_SteelTau), "History exposes unverified shear as a calculated stress.");
        foreach(var force in source.Beams.SelectMany(b=>b.Results).OfType<BeamForceResultModel>()){force.Torque=8e8;force.Mx=-4e8;force.Vx=3e6;}
        var planar=Solve(new[] { Phase(10, BridgeSectionState.Steel, load), Phase(20, BridgeSectionState.Composite) },"G");
        Near(planar.Point8_SteelSigma,activation.Point8_SteelSigma,"LinearHistory retains N-M response while excluding T, weak-axis bending and transverse shear");
        BridgeWorkflowTests.Require(planar.Assumptions.Contains(BridgeCheckAdapter.PlanarAssumption),"History result lost its planar assumption.");
#if GPC_PILOT
        Gpc(checker, one, activation, planar, ec * (epsilon0 + slope * 250 + 200e-6), ea * (epsilon0 - slope * 1400));
#endif
        var assembly = BridgeMergeTests.Initialize();
        using var doc = new Grasshopper.Kernel.GH_Document();
        var detail = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeDetails.BridgeDetailCheckComponent", true));
        detail.CreateAttributes(); doc.AddObject(detail, false);
        ((Grasshopper.Kernel.Parameters.Param_GenericObject)detail.Params.Input[0]).PersistentData.Append(new Grasshopper.Kernel.Types.GH_ObjectWrapper(activation));
        Grasshopper.Kernel.GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, Grasshopper.Kernel.GH_SolutionMode.Silent);
        BridgeWorkflowTests.Require(detail.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error).Any(s => s.Contains("LinearHistory")) && !detail.Params.Output[0].VolatileData.AllData(true).Any(g => g?.ScriptVariable() is BridgeStressResult),
            "Detail history guard: " + string.Join("; ", detail.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)) + " | warnings: "
                + string.Join("; ", detail.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Warning)) + " | outputs=" + detail.Params.Output[0].VolatileDataCount);
        Console.WriteLine("PASS history: split shrinkage, analytical primary stresses, stress-free concrete activation, equilibrium, explicit unsupported shear");
    }
#if GPC_PILOT
    /// <summary>P2T: the LinearHistory checks with the GPC workflow executor (D3) of Super Element Check (<see cref="GpcBridgeChecks"/>) on the
    /// GPC bridge model of Build BridGHe Model, from an action source built in GPC (<see cref="GpcWorkflowSources"/>, the forces of the FEM
    /// source). Same oracle and meaning: split shrinkage equal to one step, analytical rectangular integration, stress-free concrete activation,
    /// equilibrium of the history, shear not verified, planar response, and the Detail Check guard on the D3 history. The FEM results of the
    /// former BridGHe phases (<paramref name="fem1"/>, …) are the parity reference until the FeMM removal (same tolerance as the test).
    /// <para>D3 reads the stations of a section from the force cases of the dataset: the two shrinkage-only workflows of the FEM test get a
    /// steel action with zero forces (no stress), which gives the dataset its stations. The checks still need the merged FEM model of Build
    /// BridGHe Model (combinations, beam angles, local states) and the actions their FEM source, as Super Element Check today (R1/R5).</para></summary>
    private static void Gpc(BridgeCheckAdapter checker, BridgeStressResult fem1, BridgeStressResult femActivation, BridgeStressResult femPlanar, double concreteOracle, double steelOracle)
    {
        void Near(double actual, double expected, string label) => BridgeWorkflowTests.Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= 1e-8 * Math.Max(1, Math.Abs(expected)), "GPC: " + label + $": {actual:G17} != {expected:G17}");
        var zero = BridgeWorkflowTests.Source("Zero", 0); foreach (var f in zero.Beams.SelectMany(b => b.Results).OfType<BeamForceResultModel>()) { f.AxialForce = 0; f.My = 0; }
        var gZero = GpcWorkflowSources.Source("Zero", 0, values: (_, _) => new double[6]);
        var source = BridgeWorkflowTests.Source("Reference", 100000); foreach (var f in source.Beams.SelectMany(b => b.Results).OfType<BeamForceResultModel>()) f.My = 0;
        var gSource = GpcWorkflowSources.Source("Reference", 100000, values: (i, _) => new[] { 100000d + i, 0, 0, 0, 0, 0 });
        int solved = 0;
        BridgeStressResult Solve(ModelModel fem, GPC.Model.Models.Model gpc, BridgePhaseDefinition[] phases, params string[] keys)
        {
            var table = new[] { new[] { "" }.Concat(keys).ToArray(), new[] { "SLS" }.Concat(keys.Select(k => "1")).ToArray() };
            var linked = phases.Select(p => p with { Actions = p.Actions.Select(a => a.Source == null ? a : a with { GpcSource = gpc }).ToArray() }).ToArray();
            var merged = BridgeWorkflowBuilder.Build(new[] { fem }, phases, table, BridgeWorkflowMethod.LinearHistory, 1e-6);
            var bridge = GpcBridgeMerge.Build(new[] { (fem, gpc) }, linked, table, "history " + solved++, BridgeWorkflowMethod.LinearHistory);
            GpcBridgeMerge.RequireSame(bridge, merged, "history " + solved);
            return new GpcBridgeChecks(bridge.Bridge, merged.Model).Calculate("B0", 0, checker.SectionInput(false), 2, 0, 1)[0];
        }
        BridgeActionDefinition Shrink(string key, double increment) => new() { Key = key, Behavior = BridgeActionBehavior.Shrinkage, Strain = increment, PrimaryOnly = true };
        BridgePhaseDefinition Phase(int order, BridgeSectionState state, params BridgeActionDefinition[] actions) => new() { Order = order, Name = "Step " + order, State = state, Actions = actions };
        var none = new BridgeActionDefinition { Key = "Z", Source = zero, Case = "G", Behavior = BridgeActionBehavior.Steel };
        var one = Solve(zero, gZero, new[] { Phase(10, BridgeSectionState.Steel, none), Phase(20, BridgeSectionState.Composite, Shrink("R", -200e-6)) }, "Z", "R");
        var two = Solve(zero, gZero, new[] { Phase(10, BridgeSectionState.Steel, none), Phase(20, BridgeSectionState.Composite, Shrink("R1", -120e-6)), Phase(30, BridgeSectionState.Composite, Shrink("R2", -80e-6)) }, "Z", "R1", "R2");
        Near(two.Point1_ConcreteSigma, one.Point1_ConcreteSigma, "Incremental vs one-step shrinkage, concrete");
        Near(two.Point8_SteelSigma, one.Point8_SteelSigma, "Incremental vs one-step shrinkage, steel");
        Near(one.Point1_ConcreteSigma, concreteOracle, "History vs analytical rectangular integration, concrete");
        Near(one.Point8_SteelSigma, steelOracle, "History vs analytical rectangular integration, steel");
        Near(one.Point1_ConcreteSigma, fem1.Point1_ConcreteSigma, "D3 history equal to the BridGHe phases, concrete");
        Near(one.Point8_SteelSigma, fem1.Point8_SteelSigma, "D3 history equal to the BridGHe phases, steel");
        var load = new BridgeActionDefinition { Key = "G", Source = source, Case = "G", Behavior = BridgeActionBehavior.Steel };
        var activation = Solve(source, gSource, new[] { Phase(10, BridgeSectionState.Steel, load), Phase(20, BridgeSectionState.Composite) }, "G");
        Near(activation.Point1_ConcreteSigma, 0, "Concrete starts stress-free after loaded steel");
        BridgeWorkflowTests.Require(activation.Analysis.Stages.Count == 2 && activation.Stage.GetHistory() != null, "GPC: Activation-only phase not retained in history.");
        var final = activation.Stage.GetHistory().State;
        Near(final.IntegratedN, 100000, "History sectional force equilibrium");
        Near(final.ForceResidual, 0, "History force residual");
        BridgeWorkflowTests.Require(double.IsNaN(activation.Point6_SteelTau), "GPC: History exposes unverified shear as a calculated stress.");
        Near(activation.Point8_SteelSigma, femActivation.Point8_SteelSigma, "D3 activation equal to the BridGHe phases, steel");
        var planarSource = GpcWorkflowSources.Source("Reference", 100000, values: (i, _) => new[] { 100000d + i, 3e6, 0, 8e8, -4e8, 0 });
        foreach (var f in source.Beams.SelectMany(b => b.Results).OfType<BeamForceResultModel>()) { f.Torque = 8e8; f.Mx = -4e8; f.Vx = 3e6; }
        var planar = Solve(source, planarSource, new[] { Phase(10, BridgeSectionState.Steel, load), Phase(20, BridgeSectionState.Composite) }, "G");
        Near(planar.Point8_SteelSigma, activation.Point8_SteelSigma, "LinearHistory retains N-M response while excluding T, weak-axis bending and transverse shear");
        Near(planar.Point8_SteelSigma, femPlanar.Point8_SteelSigma, "D3 planar equal to the BridGHe phases, steel");
        BridgeWorkflowTests.Require(planar.Assumptions.Contains(BridgeCheckAdapter.PlanarAssumption), "GPC: History result lost its planar assumption.");
        var assembly = BridgeMergeTests.Initialize();
        using var doc = new Grasshopper.Kernel.GH_Document();
        var detail = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeDetails.BridgeDetailCheckComponent", true));
        detail.CreateAttributes(); doc.AddObject(detail, false);
        ((Grasshopper.Kernel.Parameters.Param_GenericObject)detail.Params.Input[0]).PersistentData.Append(new Grasshopper.Kernel.Types.GH_ObjectWrapper(activation));
        Grasshopper.Kernel.GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, Grasshopper.Kernel.GH_SolutionMode.Silent);
        BridgeWorkflowTests.Require(detail.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error).Any(s => s.Contains("LinearHistory")) && !detail.Params.Output[0].VolatileData.AllData(true).Any(g => g?.ScriptVariable() is BridgeStressResult),
            "GPC: Detail history guard on the D3 history: " + string.Join("; ", detail.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)));
        Console.WriteLine($"PASS history on D3: {solved} GPC bridge models from sources built in GPC; split shrinkage, analytical primary stresses, activation, equilibrium, unverified shear, planar response and detail guard; equal to the BridGHe phases");
    }
#endif
}
