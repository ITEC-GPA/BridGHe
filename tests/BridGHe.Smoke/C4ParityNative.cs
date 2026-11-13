using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using BridGHe.Common.Straus;
using GPC.Converter.Straus7;
using GPC.Model.Elements;
using Rhino.Geometry;

/// <summary>
/// C4 (11/11/2026, user rule: what 0.3.0 accepted, 0.4.0 accepts and writes as 0.3.0 did), natively (<c>--c4-parity-native</c>, also in
/// <c>--core-only --native</c>):
/// <list type="bullet">
/// <item>point 1: an angle and a double angle (principal angle) with distributed supports, temperature gradients and a taper, refused by the
/// Converter before C4, written by Straus R3 Export; read back from the .st7 against the calls of 0.3.0 replayed on the same model written without
/// these data (reference angle 90° + angle, +90° for a double angle; St7SetBeamSupport2, St7SetBeamTempGradient2, St7SetBeamTaper2 with the
/// 0.3.0 rules of BeamSupportExport and StrausR3Helper); the model solves;</item>
/// <item>point 2: an ordinary run (linear, and nonlinear increments) with a moving path «ConvertToStaticCases»: the generated static cases are
/// in the file and are not solved, as 0.3.0; the solved values equal bit for bit the run without generated cases (before C4);</item>
/// <item>point 3: solver 1 as 0.3.0 (NLA without stages, staged flag 0) on example 05: the values of the new run, of the run before C4 (one
/// explicit stage, staged flag 1, replayed here) and of 0.3.0 solving its frozen file (P2B/results/examples-1), every value printed.</item>
/// </list>
/// </summary>
internal static class C4ParityNative
{
    private static int failures;
    private static void Check(bool ok, string what) { if (!ok) failures++; Console.WriteLine((ok ? "PASS: " : "FAIL: ") + "C4 " + what); }
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string Rs(IEnumerable<double> v) => string.Join(" ", v.Select(R));
    private static MaterialModel Steel() => new("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, Density = 7.85e-9, ThermalExpansion = 1.2e-5 };
    private static NodeModel Fixed(string id, Point3d p) { var n = new NodeModel(id, p); n.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true)); return n; }
    private static string Folder() { string f = Path.GetFullPath(Path.Combine("artifacts", "native", "c4-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(f); return f; }

    internal static int Run()
    {
        failures = 0;
        Rotated();
        MovingOrdinary();
        Solver1();
        Cable();
        Console.WriteLine($"C4 PARITY NATIVE {(failures == 0 ? "PASS" : "FAIL")}: {failures} failures");
        return failures == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Raw Straus7 calls on a second file (7) next to the one the Converter API keeps open (1), as C3.

    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int OpenFile(int uid, string path, string scratch);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int OneInt(int uid);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int FourInts(int uid, int a, int b, int c);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int IntDoubles(int uid, int a, double[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TwoIntsDoubles(int uid, int a, int b, double[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int FourIntsDoubles(int uid, int a, int b, int c, int d, double[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ThreeIntsDoubles(int uid, int a, int b, int c, double[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SupportRead(int uid, int beam, int direction, int freedomCase, ref int status, double[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TaperRead(int uid, int beam, int axis, ref int type, double[] values);
    private static T F<T>(string name) where T : Delegate
    {
        var address = GetProcAddress(GetModuleHandle("St7api.dll"), name);
        if (address == IntPtr.Zero) throw new InvalidOperationException("C4 native: no St7API function " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private static void C(int code, string what) { if (code != 0) throw new InvalidOperationException($"C4 native: {what} returned {code}"); }

    /// <summary>What the .st7 file <paramref name="uid"/> holds for a beam: reference angle, initial axes, supports on the four directions of
    /// freedom case 1, gradients of a case, taper of the two axes.</summary>
    private static string BeamData(int uid, int beam, int loadCase)
    {
        var angle = new double[1]; C(F<IntDoubles>("St7GetBeamReferenceAngle1")(uid, beam, angle), "St7GetBeamReferenceAngle1");
        var axes = new double[9]; C(F<IntDoubles>("St7GetBeamAxisSystemInitial")(uid, beam, axes), "St7GetBeamAxisSystemInitial");
        var parts = new List<string> { "axes " + string.Join(" ", axes.Select(v => Math.Round(v, 6).ToString("R", CultureInfo.InvariantCulture))) };
        for (int d = 0; d < 4; d++)
        {
            int status = 0; var v = new double[2]; int code = F<SupportRead>("St7GetBeamSupport2")(uid, beam, d, 1, ref status, v);
            parts.Add($"support {d} " + (code == 0 ? status + " " + Rs(v) : "none (" + code + ")"));
        }
        var g = new double[2]; int gc = F<TwoIntsDoubles>("St7GetBeamTempGradient2")(uid, beam, loadCase, g); parts.Add("gradient " + (gc == 0 ? Rs(g) : "none (" + gc + ")"));
        for (int axis = 1; axis <= 2; axis++) { int type = 0; var t = new double[2]; int tc = F<TaperRead>("St7GetBeamTaper2")(uid, beam, axis, ref type, t); parts.Add($"taper {axis} " + (tc == 0 ? type + " " + Rs(t) : "none (" + tc + ")")); }
        Console.WriteLine($"C4 point 1 file {uid} beam {beam}: reference angle {R(angle[0])}; " + string.Join("; ", parts));
        return string.Join("; ", parts);
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Point 1

    private static BeamSupportAttributeModel Support(BeamSupportAttributeModel.LocalAxisDirection direction, BeamSupportAttributeModel.SimpleSpringTypes type, double stiffness, double gap) =>
        new(new BeamSupportAttributeModel.AttributeData { SpringType = BeamSupportAttributeModel.SpringType.Simple, SpringTensionOrientation = BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis,
            LocalAxisDirection = direction, SimpleSpringType = type, Stiffness = stiffness, Gap = gap });

    /// <summary>The 0.3.0 rule of a support (BeamSupportExport.Resolve of 95ab7c3): −1 → adMinus1 (0), +1 → adPlus1 (1), −2 → adMinus2 (2),
    /// +2 → adPlus2 (3); compression only for compression-only and tension-only springs; a tension-only spring flips the direction (^1).</summary>
    private static (int Direction, int Status, double[] Values) Former(BeamSupportAttributeModel.AttributeData a)
    {
        int d = a.LocalAxisDirection switch
        {
            BeamSupportAttributeModel.LocalAxisDirection.negative1Axis => 0, BeamSupportAttributeModel.LocalAxisDirection.positive1Axis => 1,
            BeamSupportAttributeModel.LocalAxisDirection.negative2Axis => 2, BeamSupportAttributeModel.LocalAxisDirection.positive2Axis => 3, _ => throw new ArgumentException()
        };
        bool compression = a.SimpleSpringType != BeamSupportAttributeModel.SimpleSpringTypes.TensionCompression;
        if (a.SimpleSpringType == BeamSupportAttributeModel.SimpleSpringTypes.TensionOnly) d ^= 1;
        return (d, compression ? 1 : 0, new[] { a.Stiffness, a.Gap });
    }

    /// <summary>An angle L 150 × 10 / 100 × 12 at 20° (supports +2 compression only with a gap and −1 tension only, a gradient, a taper of
    /// axis 1 to 0.8 aligned on top) and a double angle (gap 8) at 30° (a support +1 and a gradient), clamped at the start, loaded at the end.</summary>
    private static ModelModel RotatedModel(bool data)
    {
        var model = new ModelModel(ModelModel.ModelUnits.Nmm); var g = new LoadCaseModel("G", "G"); model.LoadCases.Add(g);
        var single = new SectionModel { SectionType = SectionModel.SectionTypes.Angle, B = 150, T1 = 10, D = 100, T2 = 12 }; single.CalculateSection();
        var pair = new SectionModel { SectionType = SectionModel.SectionTypes.Angle, B = 150, T1 = 10, D = 100, T2 = 12, Mirror = SectionModel.MirrorTypes.Left, MirrorGapA = 8 }; pair.CalculateSection();
        var nodes = new[] { Fixed("A0", Point3d.Origin), new NodeModel("A1", new Point3d(3000, 0, 0)), Fixed("B0", new Point3d(0, 2000, 0)), new NodeModel("B1", new Point3d(3000, 2000, 0)) };
        nodes[1].Loads.Add(new NodeForceLoadModel(g, new Vector3d(0, -200, -500))); nodes[3].Loads.Add(new NodeForceLoadModel(g, new Vector3d(0, 300, -400)));
        model.AddElements(nodes);
        var l1 = new BeamModel(nodes[0].Position, nodes[1].Position, new BeamPropertyModel("L") { Name = "L 150x100", Material = Steel(), Section = single, PropertyType = BeamPropertyType.Beam }) { BeamId = "L1", AngleDeg = 20 };
        var l2 = new BeamModel(nodes[2].Position, nodes[3].Position, new BeamPropertyModel("2L") { Name = "2L 150x100", Material = Steel(), Section = pair, PropertyType = BeamPropertyType.Beam }) { BeamId = "L2", AngleDeg = 30 };
        if (data)
        {
            l1.Attributes.Add(Support(BeamSupportAttributeModel.LocalAxisDirection.positive2Axis, BeamSupportAttributeModel.SimpleSpringTypes.CompressionOnly, 3, 0.5));
            l1.Attributes.Add(Support(BeamSupportAttributeModel.LocalAxisDirection.negative1Axis, BeamSupportAttributeModel.SimpleSpringTypes.TensionOnly, 2, 0));
            l1.Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData { NFromX = 1, NToX = 0.8, AlignmentX = BeamTaperAttributeModel.AlignmentCodes.Top }));
            l1.Loads.Add(new BeamGradientLoadModel(g, new BeamGradientLoadModel.LoadData { Axis1 = 0.01, Axis2 = -0.02 }));
            l2.Attributes.Add(Support(BeamSupportAttributeModel.LocalAxisDirection.positive1Axis, BeamSupportAttributeModel.SimpleSpringTypes.TensionCompression, 4, 0));
            l2.Loads.Add(new BeamGradientLoadModel(g, new BeamGradientLoadModel.LoadData { Axis1 = 0.005, Axis2 = 0 }));
        }
        model.AddElements(new ElementModel[] { l1, l2 });
        return model;
    }

    private static void Rotated()
    {
        string folder = Folder(), written = Path.Combine(folder, "rotated.st7"), bare = Path.Combine(folder, "bare.st7"), former = Path.Combine(folder, "former030.st7");
        var model = RotatedModel(true);
        // Before C4 the Converter refused it (PrincipalAngleWithTaperGradientOrSupportRequiresExplicitAxisConvention): the same plan without the option.
        var conversion = GpcModels.Convert(model, 1e-6);
        string refusal = null;
        try { GPC.Converter.Straus7.Straus7ModelWritePlan.Prepare(conversion.Model, null, null, conversion.Report.NativeSections, new Straus7WriteOptions()); } catch (NotSupportedException e) { refusal = e.Message; }
        Check(refusal != null && refusal.Contains("PrincipalAngleWithTaperGradientOrSupportRequiresExplicitAxisConvention"), "point 1 without the option the Converter refuses the angles with axis data (before C4): " + refusal);
        var warnings = new List<string>();
        FemmAnalysisSeam.Export(model, written, 1e-6, warnings);
        FemmAnalysisSeam.Export(RotatedModel(false), bare, 1e-6, new List<string>());
        File.Copy(bare, former);
        var beams = new[] { RotatedModel(true).Beams[0], RotatedModel(true).Beams[1] };
        using (var api = new Straus7NativeApi())
        {
            api.OpenModelReadOnly(written, folder);
            int lc = Enumerable.Range(1, api.LoadCaseCount).Single(i => api.ReadLoadCase(i) == "G");
            var actual = new[] { BeamData(1, 1, lc), BeamData(1, 2, lc) };
            // 0.3.0 replayed (file 7) on the same model written without the data: StrausR3Helper.cs:872-880 (reference angle 90° + angle, +90° for a
            // double angle), BeamSupportExport (supports), :970-1020 (taper of the axes with a scale other than 1), :1225-1230 (gradients).
            C(F<OpenFile>("St7OpenFile")(7, former, folder), "St7OpenFile");
            try
            {
                for (int k = 0; k < 2; k++)
                {
                    int n = k + 1; var b = beams[k];
                    double angle = 90 + b.AngleDeg + (b.BeamProperty.Section.Mirror != SectionModel.MirrorTypes.None ? 90 : 0);
                    C(F<IntDoubles>("St7SetBeamReferenceAngle1")(7, n, new[] { angle }), "St7SetBeamReferenceAngle1");
                    foreach (var s in b.Attributes.OfType<BeamSupportAttributeModel>()) { var f = Former(s.Value); C(F<FourIntsDoubles>("St7SetBeamSupport2")(7, n, f.Direction, 1, f.Status, f.Values), "St7SetBeamSupport2"); }
                    foreach (var t in b.Attributes.OfType<BeamTaperAttributeModel>())
                    {
                        int Code(BeamTaperAttributeModel.AlignmentCodes a) => a == BeamTaperAttributeModel.AlignmentCodes.Bottom ? 2 : a == BeamTaperAttributeModel.AlignmentCodes.Center ? 0 : 1;
                        if (t.Value.NFromX != 1 || t.Value.NToX != 1) C(F<ThreeIntsDoubles>("St7SetBeamTaper2")(7, n, 1, Code(t.Value.AlignmentX), new[] { t.Value.NFromX, t.Value.NToX }), "St7SetBeamTaper2 x");
                        if (t.Value.NFromY != 1 || t.Value.NToY != 1) C(F<ThreeIntsDoubles>("St7SetBeamTaper2")(7, n, 2, Code(t.Value.AlignmentY), new[] { t.Value.NFromY, t.Value.NToY }), "St7SetBeamTaper2 y");
                    }
                    foreach (var gl in b.Loads.OfType<BeamGradientLoadModel>()) C(F<TwoIntsDoubles>("St7SetBeamTempGradient2")(7, n, lc, new[] { gl.Value.Axis1, gl.Value.Axis2 }), "St7SetBeamTempGradient2");
                    string expected = BeamData(7, n, lc);
                    Check(actual[k] == expected, $"point 1 beam {b.BeamId} ({(k == 0 ? "angle at 20°" : "double angle at 30°")}): axes, supports, gradient and taper of the .st7 as 0.3.0 wrote them\n    0.4.0 {actual[k]}\n    0.3.0 {expected}");
                }
            }
            finally { F<OneInt>("St7CloseFile")(7); }
        }
        // 0.3.0 analysed such a model: it solves.
        var result = StrausAnalysis.Run(RotatedModel(true), Path.Combine(folder, "rotated-run.st7"), 0, new[] { "G" }, 1e-6, false, out _);
        var tip = result.Nodes.Select(n => n.Results.OfType<NodeDisplResultModel>().SingleOrDefault(r => r.LoadCase.Name == "G")).Where(r => r != null).Select(r => r.Displacement).ToArray();
        Check(tip.Length == 4 && tip.All(d => double.IsFinite(d.X) && double.IsFinite(d.Z)) && tip.Any(d => d.Length > 0), "point 1 the model with the axis data solves (Straus R3 Analysis): node displacements " + string.Join(" | ", tip.Select(d => R(d.X) + "," + R(d.Y) + "," + R(d.Z))));
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Point 2

    /// <summary>The beam of example 04 (12 elements, 6000 mm) with a node load in G and a moving axle on a path «ConvertToStaticCases»
    /// (<paramref name="convert"/>), and the combination «1.0G».</summary>
    private static ModelModel MovingModel(bool convert)
    {
        const double length = 6000; var m = new ModelModel(ModelModel.ModelUnits.Nmm); var dead = new LoadCaseModel("G", "G"); m.LoadCases.Add(dead);
        var property = new BeamPropertyModel("1") { Name = "Elastic beam", Material = Steel(), PropertyType = BeamPropertyType.Beam,
            Section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 60000, I11 = 450000000, I22 = 200000000, J = 100000000, ShearA1 = 0, ShearA2 = 0 } };
        for (int i = 0; i <= 12; i++)
        {
            var n = new NodeModel((i + 1).ToString(), new Point3d(length * i / 12, 0, 0)); n.Attributes.Add(new NodeRestraintAttributeModel(i == 0, true, i == 0 || i == 12, true, false, true));
            if (i == 6) n.Loads.Add(new NodeForceLoadModel(dead, new Vector3d(0, 0, -10))); m.AddElements(new[] { n });
        }
        for (int i = 0; i < 12; i++) m.AddElements(new[] { new BeamModel(m.Nodes[i].Position, m.Nodes[i + 1].Position, property) { BeamId = (i + 1).ToString() } });
        m.MovingPaths.Add(new MovingPath { LoadCaseName = "Traffic", Divisions = 5, Target = 2, Coordinates = new double[] { 0, 0, 0, length, 0, 0, 0, 1000, 0 }, ConvertToStaticCases = convert,
            Template = new MovingTemplate { Name = "Single axle", Vehicles = new[] { new MovingVehicle { Name = "1000 N", Loads = new MovingObject[] { new MovingPointForce { Force = new double[] { 0, 0, -1000 } } } } } } });
        var c = new LoadCombinationModel("1.0G"); c.Values.Add(dead, 1.0); m.AddLoadCombinations(new[] { c });
        return m;
    }

    private static string Rows(ModelModel m, string name) => string.Join(" | ",
        m.Nodes.Select(n => n.Results.OfType<NodeDisplResultModel>().SingleOrDefault(r => r.LoadCase.Name == name)).Where(r => r != null).Select(r => R(r.Displacement.Z) + "," + R(r.Rotation.Y))
        .Concat(m.Beams.SelectMany(b => b.Results.OfType<BeamForceResultModel>().Where(r => r.LoadCase.Name == name).OrderBy(r => r.Station)).Select(r => R(r.Station) + ":" + R(r.My) + "," + R(r.Vx) + "," + R(r.Vy))));

    private static void MovingOrdinary()
    {
        foreach (int solver in new[] { 0, 1 })
        {
            string folder = Folder(), path = Path.Combine(folder, "moving-ordinary.st7"), before = Path.Combine(folder, "no-generated.st7");
            string name = solver == 0 ? "G" : "1.0G";
            var result = StrausAnalysis.Run(MovingModel(true), path, solver, new[] { name }, 1e-6, false, out _);
            // Before C4: the path written without its static cases (the same model with ConvertToStaticCases off).
            var reference = StrausAnalysis.Run(MovingModel(false), before, solver, new[] { name }, 1e-6, false, out _);
            using (var api = new Straus7NativeApi())
            {
                api.OpenModelReadOnly(path, folder);
                var cases = Enumerable.Range(1, api.LoadCaseCount).Select(i => (Name: api.ReadLoadCase(i), On: solver == 0 && api.ReadLinearCaseEnabled(i))).ToArray();
                var ordinary = new HashSet<string>(StringComparer.Ordinal) { "G", "Traffic", "Dead", "BridGHe NLA results" };
                var generated = cases.Where(c => !ordinary.Contains(c.Name)).ToArray();
                Check(generated.Length == 5, $"point 2 solver {solver}: the 5 static cases of the path are in the file, as 0.3.0: " + string.Join(" ", cases.Select(c => c.Name + (solver == 0 ? "=" + (c.On ? 1 : 0) : ""))));
                if (solver == 0)
                {
                    Check(generated.All(c => !c.On) && cases.Single(c => c.Name == "G").On, "point 2 solver 0: the generated cases are disabled (not solved), G enabled");
                    var file = api.OpenResults(Path.ChangeExtension(path, "lsa")); Check(file.PrimaryCases == 1, "point 2 solver 0: one result case (G): " + file.PrimaryCases);
                }
                else { var file = api.OpenResults(Path.ChangeExtension(path, "nla")); Check(file.PrimaryCases == 1, "point 2 solver 1: one increment (1.0G): " + file.PrimaryCases); }
            }
            Check(result.GeneratedMovingCases.Count == 0, $"point 2 solver {solver}: no generated case is read in an ordinary run");
            string a = Rows(result, name), b = Rows(reference, name);
            Check(a.Length > 0 && a == b, $"point 2 solver {solver}: the values of {name} equal bit for bit to the run without generated cases (before C4)" + (a == b ? "" : "\n    C4 " + a + "\n    before " + b));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Point 3

    /// <summary>The run of solver 1 before C4 (FemmAnalysisSeam.Unstaged until c23263a): one explicit stage with every element active and the
    /// combinations as its increments (staged flag 1), a group of the stage for the ungrouped beams and plates.</summary>
    private static void OneStage(GPC.Model.Models.Model gpc, string name)
    {
        if (gpc.FreedomCases.Count == 0)
        { var fc = new GPC.Model.FreedomCases.FreedomCase(GpcIdentity.FreedomCase("Default restraints"), "Default restraints"); gpc.FreedomCases.Add(fc.Name, fc); }
        var ungrouped = gpc.BeamElements.Values.Cast<Element>().Concat(gpc.AreaElements.Values).Where(e => e.Groups.Count == 0).ToArray();
        if (ungrouped.Length != 0)
        {
            var group = GPC.Model.Attributes.Group.Create(GpcIdentity.Group(name), name); gpc.Groups.Add(group.Name, group);
            foreach (var e in ungrouped) e.AddGroup(group);
        }
        var stage = new GPC.Model.Stages.Stage(GpcIdentity.Stage(name), 1, name) { Definition = new GPC.Model.Stages.StageDefinition(1, 0) };
        stage.AnalysisState = new GPC.Model.Stages.StageAnalysisState
        {
            BirthGeometry = GPC.Model.Stages.StageBirthGeometry.OriginalCoordinates,
            ActiveGroupGuids = gpc.Groups.Values.Select(g => g.Guid).ToArray(), ActiveFreedomCaseGuids = gpc.FreedomCases.Values.Select(f => f.Guid).ToArray(),
            Increments = gpc.Combinations.Values.Select(c => new GPC.Model.Stages.StageLoadIncrement
            {
                Name = c.Name,
                LoadFactors = c.GetLoadCaseCoefficientsPair().Select(p => new GPC.Model.Stages.StageCaseFactor { CaseGuid = p.Key.Guid, Factor = p.Value }).ToArray(),
            }).ToArray(),
        };
        gpc.Stages.Add(stage);
        gpc.Combinations.Clear();
    }

    /// <summary>Every result of a solved file, read natively: per result case, per node (by its coordinates), displacement and reaction; per
    /// beam (by its end coordinates), the force table at its stations.</summary>
    private static Dictionary<string, double[]> NativeValues(string model, string results, string folder, List<string> info)
    {
        var values = new Dictionary<string, double[]>(StringComparer.Ordinal);
        using var api = new Straus7NativeApi();
        api.OpenModelReadOnly(model, folder);
        info.Add($"staged flag {(api.ReadStagedAnalysis() ? 1 : 0)}, stages {api.StageCount}, increments of stage 0 {api.ReadStageIncrementCount(0)}");
        string Key(double[] xyz) => string.Join(",", xyz.Select(v => Math.Round(v, 6).ToString(CultureInfo.InvariantCulture)));
        var nodes = Enumerable.Range(1, api.Count(Straus7Entity.Node)).ToDictionary(n => n, n => Key(api.ReadNode(n).Coordinates));
        var beams = Enumerable.Range(1, api.Count(Straus7Entity.Beam)).ToDictionary(b => b, b => { var e = api.ReadElement(Straus7Entity.Beam, b); return nodes[e.Nodes[0]] + "-" + nodes[e.Nodes[1]]; });
        var file = api.OpenResults(results);
        for (int c = 1; c <= file.PrimaryCases; c++)
        {
            var rc = api.ReadResultCase(c); info.Add($"case {c} '{rc.Name}' stage {rc.Stage} converged {(api.ReadResultConverged(c) ? 1 : 0)}");
            foreach (var n in nodes) { values[$"{c} node {n.Value} D"] = api.ReadNodeResult(n.Key, c, false).Values; values[$"{c} node {n.Value} R"] = api.ReadNodeResult(n.Key, c, true).Values; }
            foreach (var b in beams) { var t = api.ReadBeamForces(b.Key, c, 5); values[$"{c} beam {b.Value} stations"] = t.Positions; values[$"{c} beam {b.Value} F"] = t.Values; }
        }
        return values;
    }

    private static (double Absolute, double Relative, string Where) Worst(Dictionary<string, double[]> a, Dictionary<string, double[]> b)
    {
        double absolute = 0, relative = 0; string where = "";
        foreach (var p in a)
        {
            if (!b.TryGetValue(p.Key, out var q) || q.Length != p.Value.Length) return (double.NaN, double.NaN, "missing " + p.Key);
            double scale = Math.Max(1e-300, p.Value.Concat(q).Max(Math.Abs));
            for (int i = 0; i < q.Length; i++)
            {
                double d = Math.Abs(p.Value[i] - q[i]);
                if (d > absolute) { absolute = d; }
                if (d / scale > relative) { relative = d / scale; where = p.Key + "[" + i + "] " + R(p.Value[i]) + " / " + R(q[i]); }
            }
        }
        return (absolute, relative, where);
    }

    private static void Solver1()
    {
        string folder = Folder();
        var example = GpcNativeExamples.Create().Single(e => e.Id == "05-nonlinear-increments");
        var requested = new[] { "Half", "Full", "Double" };
        // C4: the product (GPC-only path of Straus R3 Analysis, solver 1).
        string fresh = Path.Combine(folder, "c4.st7");
        var analysed = StrausAnalysis.Run(example.Input, fresh, 1, requested, false, out _);
        // Before C4: one explicit stage (replayed: the same input, the same write options, the settings of the file).
        var gpc = GPC.Model.Core.ModelValues.Copy(example.Input.Model);
        OneStage(gpc, "BridGHe NLA"); gpc.LoadCases.Add("BridGHe NLA results", new GPC.Model.Stages.StageResultCase("BridGHe NLA results", "NLA"));
        string before = Path.Combine(folder, "before.st7");
        GpcStrausAnalysis.Staged(gpc, "BridGHe NLA results", before, folder, nativeSections: example.Input.Report.NativeSections,
            writeOptions: GpcStrausAnalysis.LegacyWriteOptions(gpc, example.Input.Report), nonlinear: Straus7NonlinearRunOptions.OfFile());
        // 0.3.0 solving its frozen file of example 05 (P2B/results/examples-1, copied): as StrausAnalysis.cs:64-77 of 0.3.0 (the settings of the
        // file, no staged flag for solver 1, St7RunSolver stNonlinearStatic).
        string frozen = Path.GetFullPath(Path.Combine(Environment.GetEnvironmentVariable("BRIDGHE_C4_EXAMPLES") ?? @"..\..\P2B\results\examples-1", "05-nonlinear-increments", "legacy.st7"));
        string former = Path.Combine(folder, "former030.st7");
        bool haveFormer = File.Exists(frozen);
        if (haveFormer)
        {
            File.Copy(frozen, former);
            using var api = new Straus7NativeApi();
            C(F<OpenFile>("St7OpenFile")(7, former, folder), "St7OpenFile");
            try { C(F<OneInt>("St7SaveFile")(7), "St7SaveFile"); C(F<FourInts>("St7RunSolver")(7, 3, 3, 1), "St7RunSolver"); }
            finally { F<OneInt>("St7CloseFile")(7); }
        }
        else Console.WriteLine("C4 point 3: the frozen 0.3.0 file is not there: " + frozen);
        var infoC4 = new List<string>(); var infoBefore = new List<string>(); var infoFormer = new List<string>();
        var c4 = NativeValues(fresh, Path.ChangeExtension(fresh, "nla"), folder, infoC4);
        var old = NativeValues(before, Path.ChangeExtension(before, "nla"), folder, infoBefore);
        var f030 = haveFormer ? NativeValues(former, Path.ChangeExtension(former, "NLA"), folder, infoFormer) : null;
        Console.WriteLine("C4 point 3 file C4: " + string.Join("; ", infoC4));
        Console.WriteLine("C4 point 3 file before C4: " + string.Join("; ", infoBefore));
        if (haveFormer) Console.WriteLine("C4 point 3 file 0.3.0: " + string.Join("; ", infoFormer));
        Check(infoC4[0].StartsWith("staged flag 0, stages 0, increments of stage 0 3", StringComparison.Ordinal) && infoC4.Skip(1).Select(s => s.Substring(0, s.IndexOf(" converged", StringComparison.Ordinal)))
            .SequenceEqual(new[] { "case 1 '1: Half' stage 0", "case 2 '2: Full' stage 0", "case 3 '3: Double' stage 0" }), "point 3: solver 1 as 0.3.0, NLA without stages, the combinations as the increments of stage 0 in their order");
        foreach (var key in c4.Keys.OrderBy(k => k, StringComparer.Ordinal))
            Console.WriteLine($"C4 point 3 {key}: C4 {Rs(c4[key])} | before {(old.TryGetValue(key, out var o) ? Rs(o) : "-")} | 0.3.0 {(f030 != null && f030.TryGetValue(key, out var f) ? Rs(f) : "-")}");
        var cb = Worst(c4, old); Console.WriteLine($"C4 point 3 worst, C4 against before C4: absolute {R(cb.Absolute)}, relative {R(cb.Relative)} at {cb.Where}");
        if (f030 != null)
        {
            var cf = Worst(c4, f030); var bf = Worst(old, f030);
            Console.WriteLine($"C4 point 3 worst, C4 against 0.3.0: absolute {R(cf.Absolute)}, relative {R(cf.Relative)} at {cf.Where}");
            Console.WriteLine($"C4 point 3 worst, before C4 against 0.3.0: absolute {R(bf.Absolute)}, relative {R(bf.Relative)} at {bf.Where}");
            Check(!double.IsNaN(cf.Relative), "point 3: every value of the 0.3.0 file is in the C4 file (same nodes, beams, stations and cases)");
        }
        // The rows the components show (the GPC readers) for the requested increments.
        Check(analysed.Stages.Count == 0 && analysed.Groups.Values.All(g => g.Name != "BridGHe NLA"), "point 3: no stage and no group of the stage in the analysed model (before C4: one stage «BridGHe NLA» and its group)");
        var rows = new GpcNativeExamples.Results(analysed);
        foreach (var name in requested)
            Console.WriteLine($"C4 point 3 rows {name}: tip Uz {R(rows.Displacement(rows.NodeCount - 1, name)[2])}, Rz {R(rows.Reaction(0, name)[2])}, My reaction {R(rows.Reaction(0, name)[4])}, beam My {R(rows.Force(0, name).Bending2)}");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Point 4

    /// <summary>A beam (steel) and a cable from a clamped node to its free end, on the material <paramref name="cable"/>.</summary>
    private static ModelModel CableModel(MaterialModel cable)
    {
        var model = new ModelModel(ModelModel.ModelUnits.Nmm); var g = new LoadCaseModel("G", "G"); model.LoadCases.Add(g);
        var nodes = new[] { Fixed("1", Point3d.Origin), new NodeModel("2", new Point3d(1000, 0, 0)), Fixed("3", new Point3d(1000, 0, 1000)) };
        nodes[1].Loads.Add(new NodeForceLoadModel(g, new Vector3d(0, 0, -100))); model.AddElements(nodes);
        SectionModel Generic(double area) => new() { SectionType = SectionModel.SectionTypes.Generic, SectionArea = area, I11 = 1e6, I22 = 1e6, J = 1e5, ShearA1 = 0, ShearA2 = 0 };
        model.AddElements(new ElementModel[]
        {
            new BeamModel(nodes[0].Position, nodes[1].Position, new BeamPropertyModel("B") { Name = "Beam", Material = Steel(), Section = Generic(1000), PropertyType = BeamPropertyType.Beam }) { BeamId = "1" },
            new BeamModel(nodes[2].Position, nodes[1].Position, new BeamPropertyModel("C") { Name = "Cable", Material = cable, Section = Generic(100), PropertyType = BeamPropertyType.Cable }) { BeamId = "2" },
        });
        return model;
    }

    /// <summary>Point 4: 0.3.0 refused every cable (StrausR3Helper.cs:338-341 calls St7SetBeamShearModulusMode on every beam property and
    /// StrausRuntime.Check throws on its error; on R31 a cable and a cutoff bar give 41, «The specified beam type is not valid», probe C4 of
    /// 11/11): the refusal of a cable on a Table Material (E 0, ZeroElasticModulus, lane C3) is kept. The run of a cable on a steel prints
    /// what 0.4.0 does (not checked: a finding for the coordinator).</summary>
    private static void Cable()
    {
        var table = new MaterialModel("TM") { Name = "Table", PoissonRatio = 0.3, UsePoisson = true, StressStrainTable = new[] { (0.0, 0.0), (200.0, 0.001), (250.0, 0.01) } };
        string refusal = null;
        try { GpcModels.Convert(CableModel(table), 1e-6); } catch (Exception e) { refusal = e.GetType().Name + ": " + e.Message; }
        Check(refusal != null && refusal.Contains("ZeroElasticModulus"), "point 4: a cable on a Table Material (E 0) is refused, as 0.3.0 refused every cable: " + (refusal ?? "accepted"));
        string folder = Folder(), outcome;
        try { FemmAnalysisSeam.Export(CableModel(Steel()), Path.Combine(folder, "cable.st7"), 1e-6, new List<string>()); outcome = "written"; }
        catch (Exception e) { outcome = e.GetType().Name + ": " + e.Message; }
        Console.WriteLine("C4 point 4 (not checked): a cable on a steel material through Straus R3 Export: " + outcome);
    }
}
