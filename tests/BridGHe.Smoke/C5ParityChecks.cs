using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Straus;
using GPC.Converter.Straus7;
using Point3d = GPC.Geometry.Point3d;
using Vector3d = GPC.Geometry.Vector3d;

/// <summary>
/// C5 (11/11/2026, user rule: what 0.3.0 accepted, 0.4.0 accepts and writes as 0.3.0 did): the end releases of a beam with a principal angle
/// (an angle, a double angle) whose kind or value differs along or about the two transverse axes. 0.3.0 (StrausR3Helper.cs:894-947 of 95ab7c3)
/// wrote St7SetBeamTRelease3 and St7SetBeamRRelease3 of end 1, then of end 2, each only when one of its three flags is set, with the flags
/// along and about the axes 1, 2, 3 of the beam as given (false → brFixed 1; true → brReleased 0, or brPartial 2 with a partial value), and
/// the partial values as given. The Converter refused them (SectionFrameBeamReleaseDiffersInPrincipalAxes); with the option of C4
/// (Straus7WriteOptions.SectionAxisDataOnNativeAxes, on in GpcStrausAnalysis.LegacyWriteOptions) it writes them as 0.3.0.
/// <list type="bullet">
/// <item><c>--regression gpc-c5</c> (<see cref="Regression"/>, not native): the write plan recorded without the native API, refusal without the
/// option, the calls of 0.3.0 with it (an angle at 20°, a double angle at 30°, and the double angle of Bridge Phase Model), every other call
/// as without the releases;</item>
/// <item><c>--c5-parity-native</c> (<see cref="Native"/>, also in <c>--core-only --native</c>): the same models written by Straus R3 Export and
/// by the analysis of a Bridge Phase Model output, read back from the .st7 against the calls of 0.3.0 replayed on the same model written
/// without the releases (reference angle 90° + angle, +90° for a double angle, +180° for a negative principal angle; St7SetBeamTRelease3 and
/// St7SetBeamRRelease3); the models solve.</item>
/// </list>
/// P2CB (13/11/2026, rule U3): the models are the GPC data of the components (the product builders and Build Model on GPC, with the geometry,
/// sections, releases and loads of the former FEM models), Bridge Phase Model is the GPC transform (<see cref="GpcModelTransforms.PhaseModel"/>),
/// and the calls of 0.3.0 are replayed from the inputs of the components (<see cref="Released"/>). The checks of the FEM output of 0.3.0 Phase
/// Model (BridgePhaseModel.Create on a FEM model) are retired by name (<see cref="P2rRetiredP2cb"/>): the GPC transform covers them.
/// </summary>
internal static class C5ParityChecks
{
    private static readonly List<string> failures = new();
    private static int checks;
    private static void Check(bool ok, string what) { checks++; if (!ok) failures.Add(what); Console.WriteLine((ok ? "PASS: " : "FAIL: ") + "C5 " + what); }
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string Rs(IEnumerable<double> v) => string.Join(",", v.Select(R));
    private const double Tolerance = 1e-6;

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Models

    /// <summary>The material of the models (Material: steel S355, E 210000, ν 0.3, density 7.85E-9, α 1.2E-5).</summary>
    private static GPC.Model.Materials.Material Steel() => R4bInputs.ParitySteel();
    private static int Clamped(GpcNativeExamples.Builder b, string id, double x, double y) { int n = b.Node(x, y, 0, id, null); b.Restrain(n, true, true, true, true, true, true); return n; }
    private const int Mirrored = 2; // Double Sections: the former MirrorTypes.Left
    /// <summary>The Angle component: L 150 × 10 / 100 × 12; with <paramref name="pair"/> Double Sections with the gap 8.</summary>
    private static LegacySectionRecipe AngleSection(bool pair) =>
        GpcSectionBuilders.Standard(BridgeSectionKind.Angle, 100, b: 150, t1: 10, t2: 12, mirror: pair ? Mirrored : LegacySectionRecipe.NoMirror, gapA: pair ? 8 : 0);
    private static LegacyBeamProperty Angle(string name, bool pair) => GpcPropertyBuilders.Beam(name, 5, AngleSection(pair), Steel(), "Steel");
    private static LegacySectionRecipe OrdinarySection() => GpcSectionBuilders.Standard(BridgeSectionKind.I, 500, b1: 200, b2: 200, t1: 20, t2: 20, t3: 10);
    private static LegacyBeamProperty Ordinary(string name) => GpcPropertyBuilders.Beam(name, 5, OrdinarySection(), Steel(), "Steel");
    private static LegacyBeamProperty Holding() => GpcPropertyBuilders.Beam("Holding", 5, GpcSectionBuilders.Generic(20000, 6.7e7, 1.7e7, 4.6e7, 0, 0, 0, 0, 0, 0, 0), Steel(), "Steel");

    /// <summary>The data of a Beam Releases input: per end, the flags along and about the axes 1, 2, 3 and the partial values.</summary>
    internal sealed record Releases(bool[] TransNode1, Vector3d TransN1Partial, bool[] RotNode1, Vector3d RotN1Partial, bool[] TransNode2, Vector3d TransN2Partial, bool[] RotNode2, Vector3d RotN2Partial);

    /// <summary>A released beam as its components gave it: ID, ends, BridGHe angle, its section (mirror, principal angle in rad) and releases.</summary>
    internal sealed record Released(string BeamId, Point3d PointFrom, Point3d PointTo, double AngleDeg, bool Mirror, double AngleX1Rad, Releases Value);

    /// <summary>Releases of every kind at both ends, translations and rotations, each kind on another axis so that the two transverse axes always
    /// differ: end 1 translations (released, fixed, partial 1000 N/mm) and rotations (partial 5e7 N·mm/rad, released, fixed); end 2 translations
    /// (fixed, partial 2000 N/mm, released) and rotations (released, fixed, partial 3e7 N·mm/rad).</summary>
    private static Releases Mixed() => new(new[] { true, false, true }, new Vector3d(0, 0, 1000), new[] { true, true, false }, new Vector3d(5e7, 0, 0),
        new[] { false, true, true }, new Vector3d(0, 2000, 0), new[] { true, false, true }, new Vector3d(0, 0, 3e7));

    /// <summary>A bracing-like set: end 1 only the rotation about axis 2 released (no translation flag, so 0.3.0 writes no TRelease3 there); end 2
    /// a partial translation along axis 1 (1500 N/mm) and the rotation about axis 1 released.</summary>
    private static Releases Bracing() => new(new[] { false, false, false }, new Vector3d(0, 0, 0), new[] { false, true, false }, new Vector3d(0, 0, 0),
        new[] { true, false, false }, new Vector3d(1500, 0, 0), new[] { true, false, false }, new Vector3d(0, 0, 0));

    /// <summary>A beam of the builder (BridGHe ID and angle given) and, when given, the releases of Beam Releases on it; the released beam it is.</summary>
    private static Released Beam(GpcNativeExamples.Builder b, int from, int to, LegacyBeamProperty property, LegacySectionRecipe section, string id, double angle, Releases releases)
    {
        int k = R4bInputs.Beam(b, from, to, property, null, id, id, angleDeg: angle); // the former BeamId: BridGHe ID and Element ID (Build Model names the beam after it)
        if (releases != null)
            b.Beams[k] = GpcBeamBuilders.WithReleases(b.Beams[k], releases.TransNode1, releases.TransN1Partial, releases.RotNode1, releases.RotN1Partial,
                releases.TransNode2, releases.TransN2Partial, releases.RotNode2, releases.RotN2Partial, b.Next("releases"));
        return new Released(id, b.Nodes[from].Node.Position, b.Nodes[to].Node.Position, angle, section != null && section.Mirror != LegacySectionRecipe.NoMirror, section?.Angle ?? 0, releases);
    }

    /// <summary>Two propped lines along x: A0 (0,0,0) clamped – A1 (3000,0,0) loaded – A2 (6000,0,0) clamped, the angle L 150 × 10 / 100 × 12 at
    /// 20° (L1, releases <see cref="Mixed"/>) from A0 to A1 and a holding beam from A1 to A2; the same at y = 2000 with the double angle (gap 8)
    /// at 30° (L2, releases <see cref="Bracing"/>). <paramref name="released"/>: the beams with releases.</summary>
    private static GpcModels.Conversion RotatedModel(bool releases, out Released[] released)
    {
        var b = new GpcNativeExamples.Builder("c5-rotated-" + releases);
        int a0 = Clamped(b, "A0", 0, 0), a1 = b.Node(3000, 0, 0, "A1", null), a2 = Clamped(b, "A2", 6000, 0);
        int b0 = Clamped(b, "B0", 0, 2000), b1 = b.Node(3000, 2000, 0, "B1", null), b2 = Clamped(b, "B2", 6000, 2000);
        b.Force(a1, "G", 0, -200, -500); b.Force(b1, "G", 0, 300, -400);
        var holding = Holding();
        var l1 = Beam(b, a0, a1, Angle("L 150x100", false), AngleSection(false), "L1", 20, releases ? Mixed() : null);
        Beam(b, a1, a2, holding, null, "H1", 0, null);
        var l2 = Beam(b, b0, b1, Angle("2L 150x100", true), AngleSection(true), "L2", 30, releases ? Bracing() : null);
        Beam(b, b1, b2, holding, null, "H2", 0, null);
        released = releases ? new[] { l1, l2 } : Array.Empty<Released>();
        return R4bInputs.Build(b, "C5 rotated releases");
    }

    /// <summary>The propped line at y = 4000 with an ordinary I beam at 30° (P1, releases <see cref="Mixed"/>) and a holding beam: the input of
    /// a Bridge Phase Model that puts P1 on the double angle.</summary>
    private static GpcModels.Conversion PhaseSource(bool releases)
    {
        var b = new GpcNativeExamples.Builder("c5-phase-source-" + releases);
        int c0 = Clamped(b, "C0", 0, 4000), c1 = b.Node(3000, 4000, 0, "C1", null), c2 = Clamped(b, "C2", 6000, 4000);
        b.Force(c1, "G", 0, -250, -600);
        Beam(b, c0, c1, Ordinary("PO"), OrdinarySection(), "P1", 30, releases ? Mixed() : null);
        Beam(b, c1, c2, Holding(), null, "H3", 0, null);
        return R4bInputs.Build(b, "C5 phase source");
    }

    /// <summary>Bridge Phase Model (the GPC transform) of <see cref="PhaseSource"/>: P1 on the double angle.</summary>
    private static GpcModels.Conversion PhaseGpc(bool releases) =>
        GpcModelTransforms.PhaseModel(PhaseSource(releases), "To 2L", new[] { "P1" }, Angle("2L 150x100", true), false, GpcIdentity.Derive(GpcIdentity.NameSpace, "c5-phase-to"));

    /// <summary>P1 of the phase model as its components give it: the beam of <see cref="PhaseSource"/> (angle 30°, releases <see cref="Mixed"/>) on
    /// the double angle.</summary>
    private static Released PhaseP1() => new("P1", new Point3d(0, 4000, 0), new Point3d(3000, 4000, 0), 30, true, AngleSection(true).Angle, Mixed());

    /// <summary>The calls of 0.3.0 for the releases of a beam (StrausR3Helper.cs:904-947): per end (1, 2) and kind (translations, rotations) in this
    /// order, only when a flag is set; status false → 1, true → 0 with a zero partial value, 2 otherwise; values the partial vector.</summary>
    private static List<(int End, bool Rotation, int[] Status, double[] Values)> Former(Releases rel)
    {
        var calls = new List<(int, bool, int[], double[])>();
        foreach (var (flags, partial, end, rotation) in new[] { (rel.TransNode1, rel.TransN1Partial, 1, false), (rel.RotNode1, rel.RotN1Partial, 1, true),
            (rel.TransNode2, rel.TransN2Partial, 2, false), (rel.RotNode2, rel.RotN2Partial, 2, true) })
        {
            if (!flags.Any(f => f)) continue;
            var v = new[] { partial.X, partial.Y, partial.Z };
            calls.Add((end, rotation, Enumerable.Range(0, 3).Select(k => !flags[k] ? 1 : v[k] == 0 ? 0 : 2).ToArray(), v));
        }
        return calls;
    }

    /// <summary>The release calls the Converter writes for a beam with a release set (always the four, end 1 and 2, translations and rotations):
    /// the calls of 0.3.0, and for an end and kind 0.3.0 did not write the default of the file (fixed, no value).</summary>
    private static string[] FormerWithDefaults(int beam, Releases rel)
    {
        var former = Former(rel);
        return new[] { (1, false), (1, true), (2, false), (2, true) }.Select(k =>
        {
            var c = former.FirstOrDefault(f => f.End == k.Item1 && f.Rotation == k.Item2);
            var status = c.Status ?? new[] { 1, 1, 1 }; var values = c.Values ?? new double[3];
            return $"SetBeamRelease({beam}, {k.Item1}, {k.Item2}, [{string.Join(" ", status)}], [{string.Join(" ", values.Select(R))}])";
        }).ToArray();
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Recorded write plan

    /// <summary>The write plan of a GPC input with <paramref name="options"/>, recorded without the native API; null when the plan refuses it
    /// (the message in <paramref name="refusal"/>).</summary>
    private static C5Recorder Record(GpcModels.Conversion input, Straus7WriteOptions options, out string refusal)
    {
        refusal = null;
        try
        {
            var plan = Straus7ModelWritePlan.Prepare(input.Model, null, null, input.Report.NativeSections, options);
            var api = DispatchProxy.Create<IC5Straus7Recording, C5Recorder>(); plan.Write(api, input.Model);
            return (C5Recorder)(object)api;
        }
        catch (NotSupportedException e) { refusal = e.Message; return null; }
    }

    /// <summary>The native number of the beam from <paramref name="from"/> to <paramref name="to"/> in a recorded plan (SetNode, SetElement).</summary>
    private static int NativeBeam(C5Recorder log, Point3d from, Point3d to)
    {
        var nodes = log.Calls.Where(c => c.Name == nameof(IStraus7ModelWriteApi.SetNode)).ToDictionary(c => (int)c.Args[0], c => (double[])c.Args[1]);
        bool At(int node, Point3d p) => Math.Abs(nodes[node][0] - p.X) < 1e-6 && Math.Abs(nodes[node][1] - p.Y) < 1e-6 && Math.Abs(nodes[node][2] - p.Z) < 1e-6;
        return log.Calls.Where(c => c.Name == nameof(IStraus7ModelWriteApi.SetElement) && (int)c.Args[0] == 1).Select(c => (Id: (int)c.Args[1], Nodes: (int[])c.Args[3]))
            .Single(e => At(e.Nodes[0], from) && At(e.Nodes[1], to)).Id;
    }

    private static void RecordedReleases(string label, GpcModels.Conversion input, GpcModels.Conversion bare, IEnumerable<Released> released)
    {
        Record(input, new Straus7WriteOptions(), out var refusal);
        Check(refusal != null && refusal.Contains("SectionFrameBeamReleaseDiffersInPrincipalAxes"), $"{label}: without the option the Converter refuses the releases (before C5): {refusal ?? "accepted"}");
        var legacy = Record(input, GpcStrausAnalysis.LegacyWriteOptions(input.Model, input.Report), out refusal);
        Check(legacy != null, $"{label}: with LegacyWriteOptions the model is written: {refusal ?? "written"}");
        if (legacy == null) return;
        foreach (var beam in released)
        {
            int id = NativeBeam(legacy, beam.PointFrom, beam.PointTo);
            var rel = beam.Value;
            string[] expected = FormerWithDefaults(id, rel), actual = legacy.Calls.Where(c => c.Name == nameof(IStraus7ModelWriteApi.SetBeamRelease) && (int)c.Args[0] == id).Select(C5Recorder.Text).ToArray();
            Check(expected.SequenceEqual(actual), $"{label} beam {beam.BeamId} (native {id}): the release calls of 0.3.0 on the native axes 1, 2, 3 as given ({Former(rel).Count} calls of 0.3.0, the others the default)\n    0.4.0 {string.Join(" ", actual)}\n    0.3.0 {string.Join(" ", expected)}");
        }
        // Nothing else changes: every other call is the one of the same model without the releases.
        var without = Record(bare, GpcStrausAnalysis.LegacyWriteOptions(bare.Model, bare.Report), out refusal);
        string[] others = legacy.Calls.Where(c => c.Name != nameof(IStraus7ModelWriteApi.SetBeamRelease)).Select(C5Recorder.Text).ToArray(), plain = without?.Calls.Select(C5Recorder.Text).ToArray() ?? Array.Empty<string>();
        int first = Enumerable.Range(0, Math.Min(others.Length, plain.Length)).FirstOrDefault(i => others[i] != plain[i]);
        Check(without != null && others.SequenceEqual(plain), $"{label}: every other call ({others.Length}) as the same model without the releases ({plain.Length})"
            + (others.SequenceEqual(plain) ? "" : $"; first difference at {first}: {(first < others.Length ? others[first] : "-")} | {(first < plain.Length ? plain[first] : "-")}"));
    }

    internal static void Regression()
    {
        failures.Clear(); checks = 0;
        foreach (var (name, p) in new[] { ("angle L 150x100", AngleSection(false)), ("double angle 2L 150x100 gap 8", AngleSection(true)), ("I 500", OrdinarySection()) })
            Console.WriteLine($"C5 section {name}: principal angle AngleX1Rad {R(p.Angle)} (0.3.0 reference angle 90 + angle{(p.Mirror != LegacySectionRecipe.NoMirror ? " + 90" : p.Angle < -0.000001 ? " + 180" : "")})");
        // An angle at 20° and a double angle at 30° with releases that differ about the transverse axes.
        var model = RotatedModel(true, out var released);
        RecordedReleases("rotated", model, RotatedModel(false, out _), released);
        // Bridge Phase Model: the ordinary I at 30° with the same releases is written with or without the option (no principal angle); on the double
        // angle the phase model is refused without the option and written as 0.3.0 with it (the GPC transform). P2CB: the two checks of the FEM output
        // of 0.3.0 Phase Model are retired by name (P2rRetiredP2cb).
        var source = PhaseSource(true);
        Record(source, new Straus7WriteOptions(), out var before);
        Check(before == null, "phase source: the ordinary I at 30° with the releases is written without the option: " + (before ?? "written"));
        RecordedReleases("phase model (GPC transform)", PhaseGpc(true), PhaseGpc(false), new[] { PhaseP1() });
        foreach (var f in failures) Console.WriteLine("  C5 FAIL: " + f.Split('\n')[0]);
        Console.WriteLine($"C5 CHECKS: {checks} checks, {failures.Count} failures");
        if (failures.Count != 0) throw new Exception($"C5 CHECKS FAIL: {failures.Count} of {checks} checks");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Native: raw Straus7 calls on a second file (7) next to the one the Converter API keeps open (1), as C3 and C4.

    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int OpenFile(int uid, string path, string scratch);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int OneInt(int uid);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int IntDoubles(int uid, int a, double[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Release(int uid, int beam, int end, int[] status, double[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Connection(int uid, int entity, int number, int[] nodes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Total(int uid, int entity, ref int total);
    private static T F<T>(string name) where T : Delegate
    {
        var address = GetProcAddress(GetModuleHandle("St7api.dll"), name);
        if (address == IntPtr.Zero) throw new InvalidOperationException("C5 native: no St7API function " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private static void C(int code, string what) { if (code != 0) throw new InvalidOperationException($"C5 native: {what} returned {code}"); }
    private static string Folder() { string f = Path.GetFullPath(Path.Combine("artifacts", "native", "c5-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(f); return f; }

    /// <summary>The native number of the beam from <paramref name="from"/> to <paramref name="to"/> in the .st7 file <paramref name="uid"/>.</summary>
    private static int NativeBeam(int uid, Point3d from, Point3d to)
    {
        int total = 0; C(F<Total>("St7GetTotal")(uid, 1, ref total), "St7GetTotal");
        bool At(int node, Point3d p) { var xyz = new double[3]; C(F<IntDoubles>("St7GetNodeXYZ")(uid, node, xyz), "St7GetNodeXYZ"); return Math.Abs(xyz[0] - p.X) < 1e-6 && Math.Abs(xyz[1] - p.Y) < 1e-6 && Math.Abs(xyz[2] - p.Z) < 1e-6; }
        for (int k = 1; k <= total; k++) { var c = new int[30]; C(F<Connection>("St7GetElementConnection")(uid, 1, k, c), "St7GetElementConnection"); if (At(c[1], from) && At(c[2], to)) return k; }
        throw new InvalidOperationException($"C5 native: no beam from {from} to {to} in file {uid}");
    }

    private const int DataNotFound = 10; // ERR7_DataNotFound: no release attribute on that end and kind (0.3.0 wrote none without a flag)
    private const string NoRelease = "none";

    /// <summary>The releases of a beam of the file <paramref name="uid"/> (end 1 and 2, translations and rotations: status and values, or
    /// «none» when the file holds no release attribute there), the same with «none» read as the native default (fixed, no value: the
    /// mechanics of the file), and its initial axes (rounded to 1e-6, as C4).</summary>
    private static (string Releases, string Mechanics, string Axes, double Reference) BeamData(int uid, int beam)
    {
        var parts = new List<string>(); var mechanics = new List<string>();
        foreach (int end in new[] { 1, 2 })
            foreach (var (name, kind) in new[] { ("St7GetBeamTRelease3", "T"), ("St7GetBeamRRelease3", "R") })
            {
                var s = new int[3]; var v = new double[3]; int code = F<Release>(name)(uid, beam, end, s, v);
                if (code == DataNotFound) { parts.Add($"{kind}{end} {NoRelease}"); mechanics.Add($"{kind}{end} 1 1 1 [0,0,0]"); continue; }
                C(code, name); string text = $"{kind}{end} {string.Join(" ", s)} [{Rs(v)}]"; parts.Add(text); mechanics.Add(text);
            }
        var axes = new double[9]; C(F<IntDoubles>("St7GetBeamAxisSystemInitial")(uid, beam, axes), "St7GetBeamAxisSystemInitial");
        var angle = new double[1]; C(F<IntDoubles>("St7GetBeamReferenceAngle1")(uid, beam, angle), "St7GetBeamReferenceAngle1");
        return (string.Join("; ", parts), string.Join("; ", mechanics), string.Join(" ", axes.Select(a => R(Math.Round(a, 6)))), angle[0]);
    }

    /// <summary>0.3.0 replayed on the file 7 for one beam: StrausR3Helper.cs:872-880 (reference angle 90° + angle, +90° for a double angle, else
    /// +180° for a principal angle below −1e-6 rad) and :904-947 (the release calls).</summary>
    private static void Replay(int beam, Released b)
    {
        double angle = 90 + b.AngleDeg + (b.Mirror ? 90 : b.AngleX1Rad < -0.000001 ? 180 : 0);
        C(F<IntDoubles>("St7SetBeamReferenceAngle1")(7, beam, new[] { angle }), "St7SetBeamReferenceAngle1");
        foreach (var c in Former(b.Value))
        {
            string name = c.Rotation ? "St7SetBeamRRelease3" : "St7SetBeamTRelease3";
            C(F<Release>(name)(7, beam, c.End, (int[])c.Status.Clone(), (double[])c.Values.Clone()), name);
        }
    }

    /// <summary>Reads the released beams of <paramref name="written"/> (0.4.0) and of <paramref name="bare"/> (0.4.0 without the releases, copied)
    /// with the calls of 0.3.0 replayed on it, and compares releases and axes.</summary>
    private static void Compare(string label, string folder, string written, string bare, IReadOnlyList<Released> released)
    {
        string former = Path.Combine(folder, Path.GetFileNameWithoutExtension(bare) + "-former030.st7"); File.Copy(bare, former);
        using var api = new Straus7NativeApi();
        api.OpenModelReadOnly(written, folder);
        C(F<OpenFile>("St7OpenFile")(7, former, folder), "St7OpenFile");
        try
        {
            foreach (var b in released)
            {
                int n1 = NativeBeam(1, b.PointFrom, b.PointTo), n7 = NativeBeam(7, b.PointFrom, b.PointTo);
                Replay(n7, b);
                var actual = BeamData(1, n1); var expected = BeamData(7, n7);
                Console.WriteLine($"C5 {label} beam {b.BeamId}: 0.4.0 (native {n1}) reference angle {R(actual.Reference)} axes {actual.Axes}; {actual.Releases}");
                Console.WriteLine($"C5 {label} beam {b.BeamId}: 0.3.0 (native {n7}) reference angle {R(expected.Reference)} axes {expected.Axes}; {expected.Releases}");
                Check(actual.Mechanics == expected.Mechanics, $"native {label} beam {b.BeamId}: the releases of the .st7 at end 1 and 2, translations and rotations, as 0.3.0 wrote them (no attribute = fixed)\n    0.4.0 {actual.Mechanics}\n    0.3.0 {expected.Mechanics}");
                // 0.3.0 wrote a release attribute only for an end and kind with a flag set; the Converter writes the four (NodesBeams.cs, the loop
                // of SetBeamRelease), those without a flag as «fixed, no value». R31 keeps no attribute for that call (read back: DataNotFound, as
                // the 0.3.0 file; measured on 11/11: 0 differences); should it keep one, it is the same mechanics, declared and counted here.
                var differences = actual.Releases.Split("; ").Zip(expected.Releases.Split("; ")).Where(p => p.First != p.Second).ToArray();
                Check(differences.All(p => p.Second.EndsWith(" " + NoRelease, StringComparison.Ordinal) && p.First.EndsWith(" 1 1 1 [0,0,0]", StringComparison.Ordinal)),
                    $"native {label} beam {b.BeamId}: the only file differences are the all-fixed attributes where 0.3.0 wrote none\n    0.4.0 {actual.Releases}\n    0.3.0 {expected.Releases}");
                if (differences.Length != 0)
                {
                    declaredAttributes += differences.Length;
                    Console.WriteLine($"C5 {label} beam {b.BeamId}: declared file difference (as for every beam with releases): {string.Join(", ", differences.Select(p => p.First.Substring(0, 2)))} hold «fixed, no value» in 0.4.0, no attribute in 0.3.0");
                }
                Check(actual.Axes == expected.Axes, $"native {label} beam {b.BeamId}: the beam axes of the .st7 as 0.3.0 wrote them (the releases act on them)\n    0.4.0 {actual.Axes}\n    0.3.0 {expected.Axes}");
                if (b.Value.TransN1Partial.Z == 1000)
                    Check(actual.Releases == "T1 0 1 2 [0,0,1000]; R1 2 0 1 [50000000,0,0]; T2 1 2 0 [0,2000,0]; R2 0 1 2 [0,0,30000000]",
                        $"native {label} beam {b.BeamId}: released, fixed and partial at end 1 and end 2, translations and rotations, read back: {actual.Releases}");
            }
        }
        finally { F<OneInt>("St7CloseFile")(7); }
    }

    /// <summary>The displacements (translations) of «G» of every node of an analysed GPC model, read with the GPC result readers.</summary>
    private static double[][] OfG(GPC.Model.Models.Model analysed)
    {
        var m = new GpcNativeExamples.Results(analysed);
        return Enumerable.Range(0, m.NodeCount).Select(k => m.NodeRows(k).SingleOrDefault(r => r.Displacement != null && m.Catalog.Entries[r.Entry].Label == "G")).Where(r => r != null)
            .Select(r => r.Displacement.Take(3).ToArray()).ToArray();
    }
    private static string Displacements(GPC.Model.Models.Model solved) => string.Join(" | ", OfG(solved).Select(v => R(v[0]) + "," + R(v[1]) + "," + R(v[2])));
    private static bool Solved(GPC.Model.Models.Model solved, int nodes)
    {
        var d = OfG(solved);
        return d.Length == nodes && d.All(v => v.All(double.IsFinite)) && d.Any(v => Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]) > 0);
    }

    private static int declaredAttributes;

    internal static int Native()
    {
        failures.Clear(); checks = 0; declaredAttributes = 0;
        // The angle at 20° and the double angle at 30° through Straus R3 Export.
        string folder = Folder(), written = Path.Combine(folder, "rotated.st7"), bare = Path.Combine(folder, "rotated-bare.st7");
        var model = RotatedModel(true, out var released);
        BridGHe.Common.Gpc.Legacy.GpcAnalysisSeam.Export(model, written);
        BridGHe.Common.Gpc.Legacy.GpcAnalysisSeam.Export(RotatedModel(false, out _), bare);
        Compare("rotated", folder, written, bare, released);
        // 0.3.0 analysed such a model: it solves (Straus R3 Analysis, linear).
        var solved = StrausAnalysis.Run(model, Path.Combine(folder, "rotated-run.st7"), 0, new[] { "G" }, false, out _);
        Check(Solved(solved, 6), "native rotated: the model with the releases solves (Straus R3 Analysis): node displacements " + Displacements(solved));

        // Bridge Phase Model on the double angle: the GPC transform written as the analysis writes it, against 0.3.0 replayed on the same transform
        // written without the releases (before P2CB: on the FEM output of 0.3.0 Phase Model).
        string phase = Path.Combine(folder, "phase.st7"), phaseBare = Path.Combine(folder, "phase-bare.st7");
        var gpc = PhaseGpc(true);
        BridGHe.Common.Gpc.Legacy.GpcAnalysisSeam.Export(gpc, phase);
        BridGHe.Common.Gpc.Legacy.GpcAnalysisSeam.Export(PhaseGpc(false), phaseBare);
        Compare("phase model", folder, phase, phaseBare, new[] { PhaseP1() });
        var phaseSolved = StrausAnalysis.Run(gpc, Path.Combine(folder, "phase-run.st7"), 0, new[] { "G" }, false, out _);
        Check(Solved(phaseSolved, 3), "native phase model: the double angle with the releases solves (Straus R3 Analysis of the phase model output): node displacements " + Displacements(phaseSolved));
        Console.WriteLine($"C5 native: {declaredAttributes} declared all-fixed release attributes where 0.3.0 wrote none; folder: " + folder);
        foreach (var f in failures) Console.WriteLine("  C5 FAIL: " + f.Split('\n')[0]);
        Console.WriteLine($"C5 PARITY NATIVE {(failures.Count == 0 ? "PASS" : "FAIL")}: {checks} checks, {failures.Count} failures");
        return failures.Count == 0 ? 0 : 1;
    }
}

/// <summary>C5: the write API of the Converter with the name API of the 0.3.0 labels (LegacyWriteOptions), recorded without the native API.</summary>
public interface IC5Straus7Recording : IC1Straus7Recording, IStraus7PropertyNameWriteApi { }

/// <summary>C5: every call of a write plan with its arguments (arrays copied).</summary>
public class C5Recorder : DispatchProxy
{
    internal readonly List<(string Name, object[] Args)> Calls = new();
    internal static string Text((string Name, object[] Args) call) => call.Name + "(" + string.Join(", ", call.Args.Select(F)) + ")";
    private static string F(object a) => a switch
    {
        null => "null", bool b => b ? "True" : "False", double d => d.ToString("R", CultureInfo.InvariantCulture), float f => ((double)f).ToString("R", CultureInfo.InvariantCulture),
        string s => "'" + s + "'", Straus7SectionDefinition n => "native:" + n.Fingerprint(), Array x => "[" + string.Join(" ", x.Cast<object>().Select(F)) + "]",
        Enum e => e.GetType().Name + "." + e, IFormattable v => v.ToString(null, CultureInfo.InvariantCulture), _ => a.GetType().Name + ":" + a,
    };
    protected override object Invoke(MethodInfo method, object[] args)
    {
        Calls.Add((method.Name, (args ?? Array.Empty<object>()).Select(a => a is Array x ? x.Clone() : a).ToArray()));
        return method.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
}
