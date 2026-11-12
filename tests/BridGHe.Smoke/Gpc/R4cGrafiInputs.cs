#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Gpc;
using BridGHe.Common.Models;

/// <summary>
/// R6.6 lane R4c-grafi, <b>transitional</b> (P2 retires it with the FEM models): <c>--regression r4c-inputs</c>, non-native. Each GPC input of
/// the native examples 11–18 (the recipes of <see cref="R4cModel"/> built by <see cref="R4cModel.Build"/>) against the conversion of the FEM model
/// the runner built before R6.6 (<c>Create</c> of <see cref="BridgeGrillageExample"/>, <see cref="BridgeSuiteExample"/>, <see cref="BridgeSpineExamples"/>,
/// and the moving, staged and benchmark models of their former runs), by the legacy reader with the owner of the GPC input
/// (<see cref="R4bInputs.Compare"/>: report, bridge data, analysis fingerprint, parts and the canonical XML document, bit for bit; the declared form
/// of the FEM side of R0/R4b: as Build Model and Attach Moving Paths give it). So the native readbacks of the runners keep their meaning: the
/// models analysed are the former ones. Every model is compared; the differences are listed, then the regression fails.
/// </summary>
internal static class R4cGrafiInputs
{
    internal static void Regression()
    {
        var lines = new List<string>(); var failures = new List<string>(); var numbers = new List<string>();
        void Same(string label, R4cModel recipe, ModelModel fem)
        {
            try
            {
                var built = AsBuilt(fem, out string declared);
                string result = R4bInputs.Compare(label, recipe.Build(), built);
                lines.Add(label + ": " + result);
                Console.WriteLine("R4C INPUT TRANSITIONAL PASS (" + label + "): the GPC input equals the conversion of the former FEM model; " + result + declared);
            }
            catch (Exception ex) { failures.Add(label + ": " + ex.Message); Console.WriteLine("R4C INPUT TRANSITIONAL FAIL (" + label + "): " + ex.Message); }
        }
        // The section numbers of the longitudinal properties (GPC homogenisation against the FEM homogenisation of the former property), bit for bit.
        void Numbers(string label, BridgeGrillageExample.Parameters p)
        {
            for (int phase = 0; phase < 3; phase++)
            {
                var s = BridgeGrillageExample.LongProperty(p, phase).Section; var g = BridgeGrillageExample.LongNumbers(p, phase);
                var pairs = new[] { ("A", s.SectionArea, g.A), ("I11", s.I11, g.I11), ("I22", s.I22, g.I22), ("J", s.J, g.J) };
                var moved = pairs.Where(x => BitConverter.DoubleToInt64Bits(x.Item2) != BitConverter.DoubleToInt64Bits(x.Item3)).ToArray();
                string text = "R4C NUMBERS (" + label + ", phase " + phase + "): " + (moved.Length == 0 ? "A, I11, I22, J of the GPC homogenisation equal to the former FEM property bit for bit"
                    : string.Join("; ", moved.Select(x => FormattableString.Invariant($"{x.Item1} FEM {x.Item2:R} GPC {x.Item3:R} (relative {(x.Item3 - x.Item2) / x.Item2:E2})"))));
                Console.WriteLine(text); if (moved.Length != 0) numbers.Add(text);
            }
        }
        var p = JsonSerializer.Deserialize<BridgeGrillageExample.Parameters>(File.ReadAllText(Path.Combine(BridgeGrillageExample.Delivery, "parameters.json"))); p.Validate();
        Numbers("11", p); Numbers("benchmark", new BridgeGrillageExample.Parameters());
        foreach (var d in BridgeSuiteExample.Defaults) Numbers(d.Id, BridgeSuiteExample.SectionParameters(d));
        foreach (var definition in BridgeSpineExamples.Definitions)
        {
            var setup = JsonSerializer.Deserialize<BridgeSpineExamples.Settings>(File.ReadAllText(Path.Combine(BridgeSuiteExample.Root, definition.Id, "parameters.json")));
            Numbers(definition.Id + " central", setup.Central); Numbers(definition.Id + " lateral", setup.Lateral);
        }
        for (int phase = 0; phase < 3; phase++) Same("11 " + BridgeGrillageExample.PhaseNames[phase], BridgeGrillageExample.CreateGpc(p, phase), BridgeGrillageExample.Create(p, phase));
        Same("11 moving", BridgeGrillageExample.MovingGpc(p), Former.Moving(p));
        Same("11 staged", BridgeGrillageExample.StagedGpc(p), Former.Staged(p));
        foreach (int factor in new[] { 2, 4 }) { var refined = p with { Panels = p.Panels * factor }; Same("11 mesh-" + refined.Panels, BridgeGrillageExample.CreateGpc(refined, 2), BridgeGrillageExample.Create(refined, 2)); }
        foreach (double factor in new[] { .5, 2.0 }) Same("11 torsion " + factor, BridgeGrillageExample.CreateGpc(p, 2, factor), BridgeGrillageExample.Create(p, 2, factor));
        foreach (bool restrained in new[] { false, true }) Same("benchmark restrained " + restrained, BridgeSuiteExample.BenchmarkGpc(restrained), Former.Benchmark(restrained));
        foreach (var initial in BridgeSuiteExample.Defaults)
        {
            string file = Path.Combine(BridgeSuiteExample.Root, initial.Id, "parameters.json");
            var d = File.Exists(file) ? JsonSerializer.Deserialize<BridgeSuiteExample.Definition>(File.ReadAllText(file)) : initial; d.Validate();
            for (int phase = 0; phase < 4; phase++) Same(d.Id + " " + BridgeSuiteExample.Cases[phase], BridgeSuiteExample.CreateGpc(d, phase), BridgeSuiteExample.Create(d, phase));
            var refined = d with { PanelsPerSpan = d.PanelsPerSpan * 2 };
            Same(d.Id + " mesh-" + refined.PanelsPerSpan + " RITIRO", BridgeSuiteExample.CreateGpc(refined, 3), BridgeSuiteExample.Create(refined, 3));
        }
        foreach (var definition in BridgeSpineExamples.Definitions)
        {
            var setup = JsonSerializer.Deserialize<BridgeSpineExamples.Settings>(File.ReadAllText(Path.Combine(BridgeSuiteExample.Root, definition.Id, "parameters.json")));
            for (int phase = 0; phase < 4; phase++) Same(definition.Id + " " + BridgeSuiteExample.Cases[phase], BridgeSpineExamples.CreateGpc(setup, phase), BridgeSpineExamples.Create(setup, phase));
        }
        if (failures.Count > 0 || numbers.Count > 0) throw new Exception($"R4C INPUTS TRANSITIONAL FAIL: {failures.Count} of {failures.Count + lines.Count} GPC inputs differ from the conversion of the former FEM models:\n" + string.Join("\n", failures)
            + (numbers.Count > 0 ? "\nsection numbers moved:\n" + string.Join("\n", numbers) : ""));
        Console.WriteLine($"R4C INPUTS TRANSITIONAL PASS: {lines.Count} GPC inputs of the native examples 11-18 equal to the conversion of the former FEM models (declared form: no registered case records, case stages, beam numbers)");
    }

    /// <summary>The declared form of a former FEM model (<see cref="R4bInputs.AsBuilt"/>: as Build Model and Attach Moving Paths give it, the node IDs
    /// as Build Model names them), and two more declared differences, neither of which enters a Straus7 model:
    /// <list type="bullet">
    /// <item>the former cases had one stage, carrying their BridgeChecks phase (<c>LoadCaseModel.Stage.BridgePhase</c>); the case of Load Case on
    /// GPC has none (<see cref="BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case"/>): the phase of a case is a datum of the bridge recipe
    /// (Bridge Action, Construction Phase), read from it by the checks (as R4b for the bridge phases). The FEM cases are compared without stages;</item>
    /// <item>a former beam named by its BridGHe ID (<c>BeamId</c>, the transverse members of the steel-only model skip the rows without a
    /// diaphragm), the beam of Build Model on GPC without an Element ID named by its number (the rule of R3): the FEM beams are compared with
    /// the names of Build Model. The runners keep the IDs of the recipe for their outputs (forces.csv, geometry.json) and the graphs give them as
    /// Element IDs, as before.</item>
    /// </list></summary>
    private static ModelModel AsBuilt(ModelModel fem, out string declared)
    {
        var built = R4bInputs.AsBuilt(fem, out int renamed);
        var cases = built.Nodes.SelectMany(n => n.Loads).Concat(built.Beams.SelectMany(b => b.Loads)).Select(l => l.LoadCase).Concat(built.LoadCases)
            .Where(c => c?.Stages != null && c.Stages.Count > 0).Distinct().ToArray();
        foreach (var c in cases) c.Stages.Clear();
        int beams = 0;
        for (int i = 0; i < built.Beams.Count; i++)
        {
            string number = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (built.Beams[i].BeamId != number) { built.Beams[i].BeamId = number; beams++; }
        }
        declared = (renamed > 0 ? "; declared: " + renamed + " node IDs as Build Model names them" : "") + (cases.Length > 0 ? "; declared: " + cases.Length + " former cases without their stage" : "")
            + (beams > 0 ? "; declared: " + beams + " beams named by their number (former BridGHe IDs)" : "");
        return built;
    }

    /// <summary>The FEM models of the former runs not made by a <c>Create</c> (code of c4b19ff), for the transitional comparison only.</summary>
    private static class Former
    {
        internal static ModelModel Moving(BridgeGrillageExample.Parameters p)
        {
            var m = BridgeGrillageExample.Create(p, 2); m.MovingPaths.Add(BridgeGrillageExample.Traffic(p)); return m;
        }

        internal static ModelModel Staged(BridgeGrillageExample.Parameters p)
        {
            var staged = BridgeGrillageExample.Create(p, 0);
            foreach (var node in staged.Nodes)
            {
                bool end = node.Position.X == 0 || node.Position.X == p.Span;
                node.Attributes.Clear();
                node.Attributes.Add(new NodeRestraintAttributeModel(node.Position.X == 0 && node.Position.Y == BridgeGrillageExample.GirderY(p, 0), end, end, false, false, false));
            }
            string[] stageNames = Enumerable.Range(1, 5).Select(i => "GETTO_" + i).ToArray();
            for (int i = 0; i < 5; i++)
            {
                var combo = new LoadCombinationModel(stageNames[i]); combo.Values.Add(staged.LoadCases.Single(), (i + 1) / 5.0);
                var stage = new StagedConstructionModel(stageNames[i]) { Marph = true, Rotate = true }; stage.AddGroups(new[] { BridgeGrillageExample.Main, BridgeGrillageExample.Cross }); stage.AddLoadCombinations(new[] { combo }); staged.AddStagedConstructions(new[] { stage });
            }
            return staged;
        }

        internal static ModelModel Benchmark(bool restrained)
        {
            var p = new BridgeGrillageExample.Parameters(); var sh = BridgeSuiteExample.Shrinkage(p); double l = 20000;
            var m = new ModelModel(ModelModel.ModelUnits.Nmm); var lc = BridgeGrillageExample.Case("RITIRO", 1); m.LoadCases.Add(lc);
            var prop = BridgeSuiteExample.SuiteProperty(p, 1); prop.Material.ThermalExpansion = 1e-5;
            for (int i = 0; i < 3; i++)
            {
                var node = new NodeModel((i + 1).ToString(), new(l * i / 2, 0, 0));
                node.Attributes.Add(new NodeRestraintAttributeModel(i == 0 || restrained && i == 2, true, i == 0 || i == 2, true, restrained && (i == 0 || i == 2), true)); m.AddElements(new[] { node });
            }
            for (int i = 0; i < 2; i++)
            {
                var b = new BeamModel(m.Nodes[i].Position, m.Nodes[i + 1].Position, prop, BridgeGrillageExample.Main) { BeamId = (i + 1).ToString() };
                b.Loads.Add(new BeamPreLoadModel(lc, -sh.Epsilon, BeamPreLoadModel.LoadType.Strain));
                b.Loads.Add(new BeamGradientLoadModel(lc, new() { Axis2 = sh.Gradient / prop.Material.ThermalExpansion })); m.AddElements(new[] { b });
            }
            return m;
        }
    }
}
#endif
