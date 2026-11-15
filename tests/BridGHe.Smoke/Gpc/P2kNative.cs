using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Checks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using static BridgeMovingRevisionExample;

/// <summary>
/// P2K (12/11): the natives of the 50 REV003 and of the 99 REV004 on the product of P2K (every component on the GPC path), without the
/// TRANSITIONAL parity checks of the other lanes that read the FEM value of the merged model (they stop the two runners now that no
/// component makes one: <c>BridgeMovingRevisionNative.cs:271</c>, <c>NativeModularBridgeTests.cs:247</c>, retired by P2-prove). The rest is
/// the GPC path of the two runners (R4c-moving), the same checks in the same order: the analyses with the vertical equilibrium against the
/// independent integration of the GPC input, the GPC-only twins equal to the analyses of the definition, the moving positions, the slab targets,
/// the merge, Super Element Check, details, plots and report inputs; the measurements written to <c>validation.json</c> and the section results
/// to the numerical fingerprint (<c>BRIDGHE_RESULT_DUMP</c>), to be compared with the gate. For the 50 also the Bridge Report of the definition
/// by the library calls of the component (P6, <see cref="R4cChainDiagnosis.Report"/>).
/// <para>Native and long (about 15 minutes): run only with <c>BRIDGHE_P2K_NATIVE=50</c>, <c>99</c> or <c>50,99</c>, under the Straus7 lock;
/// otherwise the regression prints that it is not run.</para>
/// </summary>
internal static class P2kNative
{
    internal static void Regression()
    {
        string which = Environment.GetEnvironmentVariable("BRIDGHE_P2K_NATIVE");
        if (string.IsNullOrEmpty(which)) { Console.WriteLine("P2K NATIVES: not run (native: BRIDGHE_P2K_NATIVE=50,99 under the Straus7 lock)."); return; }
        if (which.Contains("99")) Modular();
        if (which.Contains("50")) Rev003();
    }

    /// <summary>The GHA is parsed once per process: a second parse shows the modal GUID conflict prompt of Grasshopper (hidden host: a hang).</summary>
    private static bool initialized;
    private static void Initialize() { if (!initialized) { BridgeExampleMigration.Initialize(); initialized = true; } }

    /// <summary>The errors of the document but the known defect of P2K found on the 50: a GH Brep parameter fed with beams given by their
    /// GPC draft alone (no FEM value, no FEM shape) cannot cast them (<c>GH_Beam.CastTo</c>, lane P2-dati), printed as a defect.</summary>
    private static string Unexpected(GH_Document doc)
    {
        var lines = Errors(doc).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var l in lines.Where(Known)) Console.WriteLine("P2K DEFECT (DataTypes, P2-dati): " + l);
        return string.Join("\n", lines.Where(l => !Known(l)));
        static bool Known(string l) => l.Contains("Data conversion failed from Beam to Brep") || l.Contains("Data conversion failed from Plate to Brep");
    }

    private static object One(IGH_Param p) => p.VolatileData.AllData(true).SingleOrDefault();
    private static void SetText(GH_Component c, string name, string text) { var p = (Param_String)In(c, name); p.RemoveAllSources(); p.SetPersistentData(text); }

    /// <summary>The 50 REV003 (the frozen capture input), the GPC path of <c>BridgeMovingRevisionNative.RunGpc</c> without the transitional checks.</summary>
    private static void Rev003()
    {
        Initialize();
        string source = CaptureInput;
        byte[] bytes = File.ReadAllBytes(source);
        string folder = Path.GetFullPath("artifacts/p2k-native-50"); Directory.CreateDirectory(folder);
        using var doc = GrasshopperBridgeExample.Read(source); doc.FilePath = source;
        var analyses = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "StrausR3AnalysisComponent").OrderBy(c => c.NickName).ToArray();
        void OutputPath(GH_Component c, string port, string file) { var p = (Param_String)In(c, port); p.RemoveAllSources(); p.SetPersistentData(Path.Combine(folder, file)); }
        foreach (var c in analyses) { Clear(c); OutputPath(c, "Output Path", c.NickName.Split(' ')[0] + ".st7"); }
        var moving = Find(doc, "MovingStaticAnalysisComponent"); Clear(moving); OutputPath(moving, "Output path", "M03_N0_MOBILE.st7");
        BridgeSegmentExcelTests.Solve(doc, true);
        foreach (var read in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeSectionReadComponent")) { BridgeSegmentExcelTests.Press(read); read.ExpireSolution(false); }
        BridgeSegmentExcelTests.Solve(doc, true); ResultFingerprint.Mark("REV003 inputs solved");
        var measurements = new List<object>();
        void Near(string label, double actual, double expected, double tolerance)
        { Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{label}: {actual:G17} vs {expected:G17}, tol={tolerance}"); measurements.Add(new { Label = label, Actual = actual, Expected = expected, Tolerance = tolerance }); }
        foreach (var c in analyses)
        {
            string name = c.NickName.Split(' ')[0];
            var input = R4cMoving.Input(One(In(c, "Model"))); Require(input != null, "No GPC input: " + c.NickName + "\n" + Errors(doc)); Console.WriteLine("P2K REV003 NATIVE: " + c.NickName);
            Press(c); BridgeSegmentExcelTests.Solve(doc, true);
            var result = R4cMoving.Results(One(Out(c, "Model"))); Require(result != null && result.HasBeamRows(), Errors(doc));
            foreach (string label in result.ReactionLabels())
            {
                double expected = R4cMoving.VerticalLoad(input.Model, label, result.Catalog.Units);
                Near(label + " Rz [N]", result.ReactionZ(label), expected, Math.Max(.01, Math.Abs(expected) * 1e-7));
            }
            Require(File.Exists(Path.Combine(folder, name + ".bghr")), "The analysis did not keep its results next to its output: " + name);
        }
        var n0 = R4cMoving.Input(One(In(analyses[2], "Model"))); var trafficInput = R4cMoving.Input(One(In(moving, "Model")));
        Require(n0 != null && trafficInput != null, "No GPC input of n0 or of the traffic model.");
        Require(trafficInput.Model.BeamElements.Count == n0.Model.BeamElements.Count && trafficInput.Model.NodesElements.Count == n0.Model.NodesElements.Count, "Traffic topology differs from n0.");
        static bool Same(GPC.Geometry.Point3d a, GPC.Geometry.Point3d b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        var fingerprints = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        string Property(GPC.Model.ElementProperties.BeamProperty p) => fingerprints.TryGetValue(p, out var f) ? f : fingerprints[p] = GPC.Model.Core.ModelValues.Fingerprint(new object[] { p });
        var n0Beams = n0.Model.BeamElements.Values.ToDictionary(b => b.Name, StringComparer.Ordinal);
        foreach (var b in trafficInput.Model.BeamElements.Values)
        { var reference = n0Beams[b.Name]; Require(Same(b.StartPoint, reference.StartPoint) && Same(b.EndPoint, reference.EndPoint) && Property(b.BeamProperty) == Property(reference.BeamProperty), "Traffic changed n0 geometry/rigidity."); }
        fingerprints.Clear(); n0Beams = null;
        Press(moving); BridgeSegmentExcelTests.Solve(doc, true);
        var definitionTraffic = One(Out(moving, "Model")); Require(R4cMoving.Results(definitionTraffic) != null, "Moving analysis failed: " + Errors(doc));
        string gpcOnlyFolder = Path.Combine(folder, "gpc-only"); Directory.CreateDirectory(gpcOnlyFolder);
        var (trafficGoo, trafficMessages) = R4cMoving.GpcOnlyAnalysis(moving, Path.Combine(gpcOnlyFolder, "M03_N0_MOBILE.st7"));
        var traffic = R4cMoving.Results(trafficGoo); Require(traffic != null, "Moving analysis on its GPC input alone failed: " + string.Join(" / ", trafficMessages));
        string difference = GpcOnlyReplay.ResultsDifference(definitionTraffic, trafficGoo); Require(difference == null, "Moving analysis on its GPC input alone differs from the analysis of the definition: " + difference);
        Console.WriteLine("P2K REV003 NATIVE: moving analysis on its GPC input alone equal to the analysis of the definition.");
        int slabAssignments = BridgeMovingSlabTargets.Run(gpcOnlyFolder);
        var path = trafficInput.Model.MovingLoads.Paths.Single();
        var start = new Point3d(path.Start.X, path.Start.Y, path.Start.Z); var end = new Point3d(path.End.X, path.End.Y, path.End.Z);
        var axis = end - start; double length = axis.Length; axis.Unitize(); var lateral = new Vector3d(path.Reference.X - path.Start.X, path.Reference.Y - path.Start.Y, path.Reference.Z - path.Start.Z); lateral.Unitize();
        var wheels = path.Template.Vehicles.Single().PointForces; double step = length / path.Divisions, maxX = wheels.Max(p => p.X), minX = wheels.Min(p => p.X);
        string[] cases = traffic.Catalog.GeneratedMovingCases().Select(g => g.Name).ToArray(); int expectedCount = (int)Math.Ceiling((length + maxX - minX) / step - .5);
        Require(cases.Length == expectedCount, "Unexpected position count.");
        foreach (var b in traffic.Model.BeamElements.Values)
            Require(BridGHe.Common.Gpc.GpcResultRows.Beam(traffic.Catalog, traffic.Model, b).Select(r => r.Entry).Distinct().Count() == cases.Length, "Missing moving beam results.");
        var nodes = traffic.Nodes; var nodeRows = traffic.NodeRows;
        for (int i = 0; i < cases.Length; i++)
        {
            Vector3d force = Vector3d.Zero, moment = Vector3d.Zero; double front = (i + .5) * step;
            foreach (var w in wheels)
            {
                double s = front + w.X - maxX; if (s < 0 || s > length) continue;
                var point = start + s * axis + w.Y * lateral; var f = new Vector3d(w.Force.X, w.Force.Y, w.Force.Z); force -= f; moment -= Vector3d.CrossProduct((Vector3d)point, f);
            }
            Vector3d actualForce = Vector3d.Zero, actualMoment = Vector3d.Zero;
            for (int k = 0; k < nodes.Count; k++)
            {
                var r = nodeRows[k].Single(x => x.Reaction != null && traffic.Label(x.Entry) == cases[i]).Reaction; var position = nodes[k].Position;
                var rf = new Vector3d(r[0], r[1], r[2]); actualForce += rf; actualMoment += Vector3d.CrossProduct(new Vector3d(position.X, position.Y, position.Z), rf) + new Vector3d(r[3], r[4], r[5]);
            }
            for (int d = 0; d < 3; d++)
            { Near(cases[i] + " F" + d + " [N]", actualForce[d], force[d], Math.Max(.02, Math.Abs(force.Z) * 1e-7)); Near(cases[i] + " M" + d + " [Nmm]", actualMoment[d], moment[d], Math.Max(100, Math.Abs(force.Z) * length * 1e-7)); }
        }
        traffic = null; trafficGoo = null; nodes = null; nodeRows = null;
        string resultFile = Path.Combine(folder, "M03_N0_MOBILE.lsa"); var written = File.GetLastWriteTimeUtc(resultFile);
        moving.ExpireSolution(false); BridgeSegmentExcelTests.Solve(doc, true);
        Require(R4cMoving.Results(One(Out(moving, "Model")))?.Catalog.GeneratedMovingCases().Count == cases.Length && File.GetLastWriteTimeUtc(resultFile) == written, "Moving result cache lost or solver reran while Run=False.");
        var combo = Find(doc, "BridgeMovingCombinationsComponent"); var ids = Values<string>(Out(combo, "Action IDs"));
        ResultFingerprint.Mark("REV003 merge start"); var merge = Find(doc, "BuildBridgeModel"); Press(merge); BridgeSegmentExcelTests.Solve(doc, true);
        var mergedGoo = One(Out(merge, "Model")); var bridge = R4cMoving.Bridge(mergedGoo); Require(bridge?.Records?.Phases.Length == 4, "Four-phase merge failed: " + Errors(doc));
        Require(!R2aDesignFixtures.HasFem(mergedGoo), "P2K: the merged model has no FEM value.");
        var actions = bridge.Records.Phases.Single(p => p.Order == 30).Actions.Where(a => ids.Contains(a.Key)).ToArray();
        Require(actions.Length == cases.Length && actions.All(a => a.Behavior == BridgeActionBehavior.ShortTerm && cases.Contains(a.SourceCase)), "Moving positions lost their n0 source association.");
        int active = 0;
        foreach (var c in bridge.Combinations)
        { int count = c.Factors.Count(v => ids.Any(id => v.Case == id + " [FEM]") && v.Factor != 0); Require(count <= 1, "Different traffic positions were summed."); active += count; }
        Require(active == cases.Length, "Each moving position must enter exactly one cumulative combination.");
        int combinations = bridge.Combinations.Count;
        ResultFingerprint.Mark("REV003 checks start"); var checker = Find(doc, "SuperElementCheckComponent"); Press(checker); BridgeSegmentExcelTests.Solve(doc, true);
        var rows = Values<BridgeStressResult>(Out(checker, "Tension Check")); Require(rows.Length > 0, "No section checks: " + Errors(doc)); int sectionChecks = rows.Length; rows = null;
        // P2K: the TRANSITIONAL parity checks of the other lanes (FEM value of the merged model) are not called (BridgeMovingRevisionNative.cs:270-278).
        bridge = null; mergedGoo = null;
        var details = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeDetailCheckComponent").ToArray();
        Require(details.Length == 3 && details.All(c => Out(c, "Section result").VolatileDataCount > 0), "Missing detail checks: " + Errors(doc));
        var plots = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name.EndsWith("PlotComponent")).ToArray(); Require(plots.Length == 6 && plots.All(c => Out(c, "SVG").VolatileDataCount > 0), "Missing SVG plots.");
        Require(In(Find(doc, "BridgeReportComponent"), "Results").VolatileDataCount == details.Sum(c => Out(c, "Section result").VolatileDataCount), "Report input coverage.");
        Require(Unexpected(doc).Length == 0, Unexpected(doc));
        ResultFingerprint.Mark("REV003 checks, details and plots solved"); ResultFingerprint.Write("example50-rev003-native", doc);
        // P6: the Bridge Report of the definition by the library calls of the component (R4c-moving: OutOfMemoryException before P2K).
        Environment.SetEnvironmentVariable("BRIDGHE_R4CM_REPORT", "1");
        R4cChainDiagnosis.Report(doc, "50");
        var pathComponent = Find(doc, "BridgeStraightMovingPathComponent"); var divisions = (Param_Integer)In(pathComponent, "Divisions"); divisions.RemoveAllSources(); divisions.SetPersistentData(path.Divisions + 1); pathComponent.ExpireSolution(false); BridgeSegmentExcelTests.Solve(doc, true);
        Require(!Out(moving, "Model").VolatileData.AllData(true).Any(g => R4cMoving.Results(g) != null) && Values<string>(Out(moving, "Cases")).Length == 0, "Stale moving results survived changed input.");
        Require(bytes.SequenceEqual(File.ReadAllBytes(source)), "Native replay modified the delivered GHX.");
        var report = new { Native = true, Definition = Path.GetFileName(source), DefinitionSHA256 = Convert.ToHexString(SHA256.HashData(bytes)), Analyses = 5, PhaseCount = 4, Beams = n0.Model.BeamElements.Count, Nodes = n0.Model.NodesElements.Count, MovingPositions = cases.Length, SlabForceAssignments = slabAssignments, Combinations = combinations, SectionChecks = sectionChecks, DetailBranches = details.Length, Plots = plots.Length, ReportInputs = true, CachedRunFalse = true, ChangedPathInvalidatesResults = true, ResultsSavedInGHX = false, Measurements = measurements };
        File.WriteAllText(Path.Combine(folder, "validation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"P2K REV003 NATIVE PASS: 5 analyses, {cases.Length} moving positions, {combinations} combinations, {sectionChecks} section checks, 3 details, 6 plots, {measurements.Count} measurements (GPC data alone, no transitional check).");
    }

    /// <summary>The 99 REV004 (the migrated candidate), the GPC path of <c>NativeModularBridgeTests.RunGpc</c> without the transitional checks.</summary>
    private static void Modular()
    {
        Initialize();
        string relative = "99-Ponte completo GPC/REV004/flusso-acciaio-misto_REV004.ghx";
        string source = Path.Combine(ExamplePaths.Root, relative), candidate = Path.GetFullPath(Path.Combine("artifacts/modular-bridge/candidates", relative));
        if (!File.Exists(candidate)) throw new FileNotFoundException("Prepare the migrated candidate first.", candidate);
        string folder = Path.GetFullPath("artifacts/modular-bridge/p2k-native-" + Path.GetFileNameWithoutExtension(source)); Directory.CreateDirectory(folder);
        byte[] original = File.ReadAllBytes(source), migrated = File.ReadAllBytes(candidate);
        using var doc = GrasshopperBridgeExample.Read(candidate); doc.FilePath = source;
        var analyses = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "StrausR3AnalysisComponent").OrderBy(c => c.NickName).ToArray();
        if (analyses.Length != 4) throw new Exception("Expected four Straus phase models.");
        for (int i = 0; i < analyses.Length; i++)
        {
            var a = analyses[i]; Clear(a); var k = (Param_Boolean)In(a, "Keep Running"); k.RemoveAllSources(); k.SetPersistentData(false);
            SetText(a, "Output Path", Path.Combine(folder, $"{i + 1:00}-" + a.NickName.Split(' ')[0] + ".st7"));
        }
        foreach (var report in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeReportComponent")) report.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(report, false);
        BridgeSegmentExcelTests.Solve(doc, true);
        foreach (var read in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeSectionReadComponent")) { BridgeSegmentExcelTests.Press(read); read.ExpireSolution(false); }
        BridgeSegmentExcelTests.Solve(doc, true); ResultFingerprint.Mark("modular inputs solved");
        var summary = new List<object>();
        for (int i = 0; i < analyses.Length; i++)
        {
            var a = analyses[i]; var input = R4cMoving.Input(One(In(a, "Model")));
            if (input == null || input.Model.BeamElements.Count == 0) throw new Exception("Phase model missing before analysis: " + a.NickName + "\n" + Errors(doc));
            int beams = input.Model.BeamElements.Count, nodes = input.Model.NodesElements.Count;
            Console.WriteLine("P2K MODULAR NATIVE: " + a.NickName + "; " + beams + " beams, " + nodes + " nodes.");
            string before = GPC.Model.Models.ModelRevisions.AnalysisFingerprint(input.Model);
            Press(a); BridgeSegmentExcelTests.Solve(doc, true);
            if (GPC.Model.Models.ModelRevisions.AnalysisFingerprint(input.Model) != before || input.Model.Datasets.Count != 0) throw new Exception("C3: the analysis changed the conversion of its input: " + a.NickName);
            var goo = One(Out(a, "Model")); var result = R4cMoving.Results(goo);
            if (result == null || !result.HasBeamRows()) throw new Exception("No native results: " + a.NickName + "\n" + Errors(doc));
            var equilibrium = new List<object>();
            var labels = result.ReactionLabels(); if (labels.Length == 0) throw new Exception("Native reaction results missing.");
            foreach (string lc in labels)
            {
                double actual = result.ReactionZ(lc), expected = R4cMoving.VerticalLoad(input.Model, lc, result.Catalog.Units), tolerance = Math.Max(.01, Math.Abs(expected) * 1e-7);
                if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance) throw new Exception($"Vertical equilibrium {lc}: {actual:G17} vs {expected:G17} N; tolerance {tolerance}.");
                equilibrium.Add(new { Case = lc, ReactionZ_N = actual, Expected_N = expected, Tolerance_N = tolerance });
            }
            string outputPath = Values<string>(In(a, "Output Path")).Single();
            string gpcOnly = Path.Combine(folder, "gpc-only"); Directory.CreateDirectory(gpcOnly);
            var (twin, messages) = R4cMoving.GpcOnlyAnalysis(a, Path.Combine(gpcOnly, Path.GetFileName(outputPath)));
            if (twin == null || R4cMoving.Results(twin) == null) throw new Exception("Analysis on its GPC input alone failed: " + a.NickName + ": " + string.Join(" / ", messages));
            string difference = GpcOnlyReplay.ResultsDifference(goo, twin);
            if (difference != null) throw new Exception("Analysis on its GPC input alone differs from the analysis of the definition: " + a.NickName + ": " + difference);
            ResultsComponentsCheck.Verify(twin, "99 " + a.NickName + " (GPC-only output)");
            string log = Path.ChangeExtension(outputPath, ".LSL");
            if (File.Exists(log)) { string text = File.ReadAllText(log); if (text.Contains("*WARNING[") || text.Contains("*ERROR[")) throw new Exception("Straus solver warnings: " + log); }
            summary.Add(new { Phase = i, Beams = beams, Nodes = nodes, Equilibrium = equilibrium });
            File.WriteAllText(Path.Combine(folder, "progress.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        }
        ResultFingerprint.Mark("modular merge start");
        foreach (var excel in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeCombinationExcelComponent"))
            if (Values<int>(In(excel, "Mode")).SingleOrDefault() == 1) { Press(excel); BridgeSegmentExcelTests.Solve(doc, true); }
        var merge = doc.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == "BuildBridgeModel"); Press(merge); BridgeSegmentExcelTests.Solve(doc, true);
        var mergedGoo = One(Out(merge, "Model")); var bridge = R4cMoving.Bridge(mergedGoo);
        if (bridge?.Records?.Phases.Length != 4) throw new Exception("Four-phase merge failed.\n" + Errors(doc));
        if (R2aDesignFixtures.HasFem(mergedGoo)) throw new Exception("P2K: the merged model has a FEM value.");
        int phases = bridge.Records.Phases.Length, combinations = bridge.Combinations.Count;
        ResultFingerprint.Mark("modular checks start");
        var checks = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "SuperElementCheckComponent").ToArray();
        foreach (var check in checks) { Press(check); BridgeSegmentExcelTests.Solve(doc, true); }
        var rows = checks.SelectMany(check => Values<BridgeStressResult>(Out(check, "Tension Check"))).ToArray();
        if (checks.Length == 0 || checks.Any(check => Out(check, "Tension Check").VolatileDataCount == 0)) throw new Exception("Missing section check results.\n" + Errors(doc));
        var details = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeDetailCheckComponent").ToArray();
        if (details.Any(c => Out(c, "Section result").VolatileDataCount == 0)) throw new Exception("A detail check branch has no output.\n" + Errors(doc));
        var plots = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name.EndsWith("PlotComponent")).ToArray();
        if (plots.Any(c => Out(c, "SVG").VolatileDataCount == 0)) throw new Exception("A plot has no SVG.\n" + Errors(doc));
        var reportComponent = doc.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == "BridgeReportComponent");
        if (In(reportComponent, "Results").VolatileDataCount != details.Sum(c => Out(c, "Section result").VolatileDataCount)) throw new Exception("Incomplete report result input.");
        string errors = Errors(doc); if (errors.Length > 0) throw new Exception("Saved graph still has errors:\n" + errors);
        // P2K: the TRANSITIONAL parity checks of the other lanes (FEM value of the merged model) are not called (NativeModularBridgeTests.cs:246-251).
        ResultFingerprint.Mark("modular checks, details and plots solved"); ResultFingerprint.Write("modular-" + Path.GetFileNameWithoutExtension(source), doc);
        R3ConcreteExclusionCheck.Run(doc, checks[0], mergedGoo, "99");
        if (!original.SequenceEqual(File.ReadAllBytes(source)) || !migrated.SequenceEqual(File.ReadAllBytes(candidate))) throw new Exception("Native test modified a GHX source.");
        File.WriteAllText(Path.Combine(folder, "validation.json"), JsonSerializer.Serialize(new { Definition = relative, Native = true, Models = summary, Phases = phases, Combinations = combinations, SectionChecks = rows.Length, Details = details.Length, Plots = plots.Length }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"P2K MODULAR NATIVE PASS: 4 models, {combinations} combinations, {rows.Length} section results, {details.Length} details, {plots.Length} plots (GPC data alone, no transitional check).");
    }
}
