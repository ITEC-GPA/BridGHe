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
using St7API;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

/// <summary>Read-only replay of example 19 native files; independently asks Straus for union stations.</summary>
internal static class NativeWorkflowStationTests
{
    internal static int ReplayArchived()
    {
        string root=ExamplePaths.Calculations("19-flusso-acciaio-misto");
        var models=new[]{"G1_GETTO","G2_PERMANENTI","Q_TRAFFICO","RITIRO_FEM"}.Select(name=>
            ModelArchive.Read<ModelModel>(File.ReadAllBytes(Path.Combine(root,"REV003-"+name+"-results.bgmodel")))).ToArray();
        int count=ReplayGrasshopper(models);
        Console.WriteLine($"ARCHIVED WORKFLOW 19 PASS: {count} section results; existing bgmodel archives, no Straus API or file writes.");
        return 0;
    }
    internal sealed record Sample(double Station, double N, double V1, double V2, double T, double M1, double M2);
    internal sealed record CaseSamples(string Key, Sample[] Original, Sample[] NativeAtUnion);
    internal sealed record Fixture(double Length, string BeamId, CaseSamples[] Cases);
    internal const string FixturePath = "tests/BridGHe.Smoke/Fixtures/bridge-stations-19.json";
    private static Sample SampleOf(BeamForceResultModel f) => new(f.Station, f.AxialForce, f.Vx, f.Vy, f.Torque, f.Mx, f.My);

    private static T Open<T>(string path, Func<int, T> read)
    {
        lock (StrausRuntime.Gate)
        {
            int id = StrausRuntime.AcquireModelId(); bool file = false, results = false;
            try
            {
                StrausRuntime.Check(St7.St7OpenFile(id, Path.GetFullPath(path), Path.GetTempPath()), "Open existing example");
                file = true; int primary = 0, secondary = 0;
                StrausRuntime.Check(St7.St7OpenResultFile(id, Path.GetFullPath(Path.ChangeExtension(path, ".LSA")), null,
                    St7.btTrue, ref primary, ref secondary), "Open existing results");
                results = true;
                BridgeWorkflowTests.Require(primary == 1, "Example replay expects one incremental case per file.");
                return read(id);
            }
            finally
            {
                if (results) St7.St7CloseResultFile(id);
                if (file) St7.St7CloseFile(id);
                StrausRuntime.ReleaseModelId(id);
            }
        }
    }

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

    internal static int Run(bool captureFixture)
    {
        string root = ExamplePaths.Calculations("19-flusso-acciaio-misto");
        string[] names = { "G1_GETTO", "G2_PERMANENTI", "Q_TRAFFICO", "RITIRO_FEM" };
        string[] keys = { "G1", "G2", "Q", "RITIRO" };
        var files = names.SelectMany(n => new[] { "-input.bgmodel", ".st7", ".LSA" }.Select(ext => Path.Combine(root, "REV003-" + n + ext))).ToArray();
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var before = files.ToDictionary(p => p, Hash);
        var models = names.Select(name =>
        {
            var m = ModelArchive.Read<ModelModel>(File.ReadAllBytes(Path.Combine(root, "REV003-" + name + "-input.bgmodel")));
            foreach (var e in m.Elements) e.Results.Clear();
            ModelHelper.GetLoadCases(m, out var cases);
            var loadCase = cases.Single(c => c.Name == name);
            m.LoadCases.Clear(); m.LoadCases.Add(loadCase);
            Open(Path.Combine(root, "REV003-" + name + ".st7"), id =>
            {
                StrausResultsReader.Read(id, 1, loadCase, m);
                return true;
            });
            return m;
        }).ToArray();
        foreach (var m in models)
            Console.WriteLine(m.Name + " beam 29: " + string.Join(", ", m.Beams.Single(b => b.BeamId == "29")
                .Results.OfType<BeamForceResultModel>().Select(f => f.Station.ToString("G12", System.Globalization.CultureInfo.InvariantCulture))));
        var merged = Merge(models);
        var fixtures = new List<CaseSamples>(); int compared = 0; double maxForceError = 0, maxMomentError = 0, maxTorqueError = 0;
        var report = new List<object>();
        for (int phase = 0; phase < models.Length; phase++)
        {
            var model = models[phase];
            Open(Path.Combine(root, "REV003-" + names[phase] + ".st7"), id =>
            {
                for (int i = 0; i < model.Beams.Count; i++)
                {
                    var beam = model.Beams[i];
                    var actual = merged.Model.Beams.Single(b => b.BeamId == beam.BeamId).Results.OfType<BeamForceResultModel>()
                        .Where(f => f.LoadCase.Name == keys[phase] + " [FEM]").OrderBy(f => f.Station).ToArray();
                    var stations = actual.Select(f => f.Station).ToArray();
                    int columns = 0; var raw = new double[St7.kMaxBeamResult];
                    StrausRuntime.Check(St7.St7GetBeamResultArrayPos(id, St7.rtBeamForce, St7.stBeamPrincipal, i + 1, 1,
                        stations.Length, stations, ref columns, raw), "Independent native sampling");
                    var native = new List<Sample>();
                    for (int s = 0; s < stations.Length; s++)
                    {
                        double v1 = raw[s * columns + St7.ipBeamSF1], v2 = raw[s * columns + St7.ipBeamSF2];
                        double m1 = raw[s * columns + St7.ipBeamBM1], m2 = raw[s * columns + St7.ipBeamBM2];
                        if (StrausR3Helper.RotateOf90Deg(beam)) { (v1, v2) = (-v2, v1); (m1, m2) = (-m2, m1); }
                        var expected = new Sample(stations[s], raw[s * columns + St7.ipBeamAxialF], v1, v2, raw[s * columns + St7.ipBeamTorque], m1, m2);
                        native.Add(expected);
                        double forceError = new[] { Math.Abs(actual[s].AxialForce - expected.N), Math.Abs(actual[s].Vx - v1),
                            Math.Abs(actual[s].Vy - v2) }.Max();
                        double torqueError = Math.Abs(actual[s].Torque - expected.T);
                        double momentError = Math.Max(Math.Abs(actual[s].Mx - m1), Math.Abs(actual[s].My - m2));
                        maxForceError = Math.Max(maxForceError, forceError); maxMomentError = Math.Max(maxMomentError, momentError);
                        maxTorqueError = Math.Max(maxTorqueError, torqueError);
                        // N-mm model; absolute tolerances cover cancellation near zero. Uniform loads give exact quadratics.
                        BridgeWorkflowTests.Require(forceError < 1e-5 && momentError < .01 && torqueError < .01,
                            $"{keys[phase]}, beam {beam.BeamId}, x={stations[s]}: native errors {forceError} N / {momentError} Nmm");
                        compared += 6;
                    }
                    if (beam.BeamId == "29") fixtures.Add(new(keys[phase], beam.Results.OfType<BeamForceResultModel>().Select(SampleOf).ToArray(), native.ToArray()));
                }
                return true;
            });
            report.Add(new { Case = names[phase], Beams = model.Beams.Count });
        }
        int sectionResults = ReplayGrasshopper(models);
        BridgeWorkflowTests.Require(files.All(p => Hash(p) == before[p]), "Read-only replay changed a native input/result file.");
        Directory.CreateDirectory("artifacts/workflow-stations");
        string validation = JsonSerializer.Serialize(
            new { Mode = "Read-only replay of existing Straus LSA results; no new solver run", Compared = compared,
                MaxForceErrorN = maxForceError, MaxMomentErrorNmm = maxMomentError, MaxTorqueErrorNmm = maxTorqueError,
                GrasshopperSectionResults = sectionResults, GrasshopperErrors = 0, NativeFilesUnchanged = true,
                Cases = report, Sources = before.Select(p => new { File = Path.GetFileName(p.Key), Sha256 = p.Value }), merged.Warnings },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("artifacts/workflow-stations/native-validation.json", validation);
        if (captureFixture)
        {
            File.WriteAllText(FixturePath, JsonSerializer.Serialize(new Fixture(models[0].Beams.Single(b => b.BeamId == "29").Length, "29", fixtures.ToArray()),
                new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(ExamplePaths.Case("19-flusso-acciaio-misto"), "validation-stations.json"), validation);
        }
        Console.WriteLine($"NATIVE STATION REPLAY PASS: {compared} components; max errors {maxForceError:G9} N, {maxMomentError:G9} Nmm (M), {maxTorqueError:G9} Nmm (T)");
        return 0;
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
