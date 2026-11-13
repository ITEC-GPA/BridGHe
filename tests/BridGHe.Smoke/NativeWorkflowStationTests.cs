using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

/// <summary>Read-only replay of example 19 native files; independently asks Straus for union stations.
/// <para>R6.6 lane R4b: <c>--workflow-archived-replay</c> stays <b>transitional</b> (FEM): it reads the 0.3.0 FEM result archives of 19 REV003
/// (<c>calcoli/REV003-*-results.bgmodel</c>, ignored by git: absent from the worktrees) into the analysis components; no gate runs it. On GPC data
/// the same archives read by value (<c>LegacyArchive.Read</c>, <c>LegacyModelImport.Import</c>, <c>LegacyResultsReader.Import</c>) would seed the
/// components with <c>GH_Model.FromGpc</c> and the analysed model (see the report of R4b). The fixture records stay for
/// <see cref="BridgeStationTests"/> (its GPC replay, R2b); <see cref="Merge"/> (FEM) for its FEM replay.</para></summary>
internal static class NativeWorkflowStationTests
{
    internal sealed record Sample(double Station, double N, double V1, double V2, double T, double M1, double M2);
    internal sealed record CaseSamples(string Key, Sample[] Original, Sample[] NativeAtUnion);
    internal sealed record Fixture(double Length, string BeamId, CaseSamples[] Cases);
    internal const string FixturePath = "tests/BridGHe.Smoke/Fixtures/bridge-stations-19.json";
    private static Sample SampleOf(BeamForceResultModel f) => new(f.Station, f.AxialForce, f.Vx, f.Vy, f.Torque, f.Mx, f.My);


    internal static BridgeWorkflowBuildResult Merge(ModelModel[] models)
    {
        string[] keys = { "G1", "G2", "Q", "RITIRO" };
        var phases = models.Select((m, i) => new BridgePhaseDefinition
        {
            Order = 10 * (i + 1), Name = keys[i], State = i == 0 ? BridgeSectionState.Steel : BridgeSectionState.Composite,
            Actions = new[] { new BridgeActionDefinition
            {
                Key = keys[i], Source = m, Case = m.LoadCases.Single().Name,
                Behavior = (BridgeActionBehavior)i, Strain = i == 3 ? -200e-6 : null
            } }
        }).ToArray();
        return BridgeWorkflowBuilder.Build(models, phases,
            new[] { new[] { "", "G1", "G2", "Q", "RITIRO" }, new[] { "SLS", "1", "1", "1", "1" } },
            BridgeWorkflowMethod.Compatible, 1e-6);
    }


    private static int ReplayGrasshopper(ModelModel[] models)
    {
        var assembly = BridgeMergeTests.Initialize();
        string path = Path.Combine(ExamplePaths.Case("19-flusso-acciaio-misto"), "19-flusso-acciaio-misto_REV003.ghx");
        using var document = GrasshopperBridgeExample.Read(path);
        foreach (var toggle in document.Objects.OfType<GH_BooleanToggle>()) toggle.Value = false;
        var components = document.Objects.OfType<GH_Component>().ToArray();
        // Resolve input names before seeding cached results; never run or overwrite the native files.
        GH_Document.EnableSolutions = true; document.Enabled = true; document.NewSolution(false, GH_SolutionMode.Silent);
        foreach (var component in components.Where(c => c.GetType().Name == "StrausR3AnalysisComponent"))
        {
            var input = (ModelModel)component.Params.Input[0].VolatileData.AllData(true).Single().ScriptVariable();
            var model = models.Single(m => m.Name == input.Name);
            var gooType = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
            var goo = Activator.CreateInstance(gooType);
            gooType.GetProperty("Value").SetValue(goo, model);
            component.GetType().GetField("_model", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(component, goo);
            component.ExpireSolution(false);
        }
        foreach (var component in components.Where(c => c.GetType().Name is "BuildBridgeModel" or "SuperElementCheckComponent"))
        {
            component.GetType().GetField("_run", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(component, true);
            component.ExpireSolution(false);
        }
        document.NewSolution(false, GH_SolutionMode.Silent);
        var errors = document.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => c.Name + ": " + e)).ToArray();
        BridgeWorkflowTests.Require(errors.Length == 0, string.Join("\n", errors));
        var check = components.Single(c => c.GetType().Name == "SuperElementCheckComponent");
        BridgeWorkflowTests.Require(check.Params.Output[0].VolatileDataCount >= 120, "No checked spine stations.");
        Console.WriteLine($"GH EXAMPLE 19 PASS: {check.Params.Output[0].VolatileDataCount} section results; no red components");
        return check.Params.Output[0].VolatileDataCount;
    }
}
