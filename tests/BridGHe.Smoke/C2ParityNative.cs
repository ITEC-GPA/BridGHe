﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.MovingLoads;
using BridGHe.Common.Straus;
using GPC.Converter.Straus7;

/// <summary>
/// C2 (11/11/2026, user decision «come la 0.3.0» on P26-01…07): the Straus7 behaviour of BridGHe 0.3.0 through the GPC Converter, natively,
/// on the analysis of Straus R3 Analysis and Moving Static Analysis (core-only --native, and --c2-parity-native):
/// <list type="bullet">
/// <item>P26-01/02/03: the section label of the eight standard kinds 0.3.0 labelled (none on a native section), the material name of the plate
/// properties, one native plate property per Plate Property named as it; no result value moves (bit for bit against the file of the Converter);</item>
/// <item>P26-04: only the requested cases and the cases of the requested combinations are solved; only the generated cases in a moving-only run;
/// the requested values equal bit for bit to a run of every case;</item>
/// <item>P26-06: the solver settings of the new file are kept (geometric and material nonlinearity, Straus7 tolerances), as 0.3.0;</item>
/// <item>P26-07: the combinations are the LSA combination cases Straus7 computes: the beam rows at the stations of the combination (a concentrated
/// load off the regular stations in A, a distributed load in B), against the superposition at the stations common to A and B of BridGHe before C2.</item>
/// </list>
/// P2CB (13/11/2026, rule U3): the models are the GPC data of the components (the product builders, Build Model on GPC, Attach Moving Paths on
/// GPC: <see cref="GpcNativeExamples.Builder"/>, <see cref="R4bInputs.Build"/>) with the geometry, properties, loads, cases and combinations of the
/// former FEM models, analysed on the GPC-only path (<see cref="StrausAnalysis.Run(GpcModels.Conversion, string, int, IReadOnlyCollection{string}, bool, out List{string})"/>)
/// and read with the GPC result readers (<see cref="GpcNativeExamples.Results"/>, the rows the FEM results copied bit for bit): the same oracles
/// and the same printed values as on the FEM models.
/// </summary>
internal static class C2ParityNative
{
    private static int failures;
    private static void Check(bool ok, string what) { if (!ok) failures++; Console.WriteLine((ok ? "PASS: " : "FAIL: ") + "C2 " + what); }
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static GPC.Model.Materials.Material Steel() => GpcNativeExamples.Steel();
    private static int Fixed(GpcNativeExamples.Builder b, string id, double x, double y) { int n = b.Node(x, y, 0, id, null); b.Restrain(n, true, true, true, true, true, true); return n; }
    private static LegacyBeamProperty Generic(string name, double area, double i11, double i22, double j) =>
        GpcPropertyBuilders.Beam(name, 5, GpcSectionBuilders.Generic(area, i11, i22, j, 0, 0, 0, 0, 0, 0, 0), Steel());
    private static GPC.Model.Materials.Material Concrete(string name, double modulus) => GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Name = name, Modulus = modulus, PoissonRatio = 0.2 });
    private static (double, double, double)[] Quad(double x0, double y0, double x1, double y1) => new[] { (x0, y0, 0.0), (x1, y0, 0.0), (x1, y1, 0.0), (x0, y1, 0.0) };
    private static string Folder() { string f = Path.GetFullPath(Path.Combine("artifacts", "native", "c2-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(f); return f; }

    internal static int Run()
    {
        failures = 0;
        Names();
        Cases();
        Plates();
        Moving();
        Solver();
        Console.WriteLine($"C2 PARITY NATIVE {(failures == 0 ? "PASS" : "FAIL")}: {failures} failures");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>P26-01/02/03.</summary>
    private static void Names()
    {
        var b = new GpcNativeExamples.Builder("c2-names");
        var kinds = new (BridgeSectionKind Kind, LegacySectionRecipe Section, string Label)[]
        {
            (BridgeSectionKind.SolidCircle, GpcSectionBuilders.Standard(BridgeSectionKind.SolidCircle, 200), "SolidRound"),
            (BridgeSectionKind.HollowCircle, GpcSectionBuilders.Standard(BridgeSectionKind.HollowCircle, 200, t: 10), "HollowRound"),
            (BridgeSectionKind.SolidRectangle, GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, 400, b: 200), "SolidRect"),
            (BridgeSectionKind.HollowRectangle, GpcSectionBuilders.Standard(BridgeSectionKind.HollowRectangle, 400, b: 200, t1: 10, t2: 12), "HollowRect"),
            (BridgeSectionKind.I, GpcSectionBuilders.Standard(BridgeSectionKind.I, 400, b1: 200, b2: 180, t1: 12, t2: 14, t3: 8), "IBeam"),
            (BridgeSectionKind.T, GpcSectionBuilders.Standard(BridgeSectionKind.T, 400, b: 200, t1: 12, t2: 14, t3: 8), "TBeam"),
            (BridgeSectionKind.C, GpcSectionBuilders.Standard(BridgeSectionKind.C, 300, b: 100, t1: 10, t2: 8, t3: 8), "Lipped Channel"),
            (BridgeSectionKind.Angle, GpcSectionBuilders.Standard(BridgeSectionKind.Angle, 100, b: 150, t1: 10, t2: 12), "Angle"),
        };
        var sections = kinds.Select(k => (k.Section, k.Label)).ToList();
        // A section of the Native Section component (no label in 0.3.0: StrausSection.Assign sets none).
        sections.Add((GpcSectionBuilders.Native("RectangleSolid", new[] { 200.0, 400.0 }, null, null, null, 0), ""));
        var nodes = new List<int> { Fixed(b, "N0", 0, 0) };
        for (int i = 0; i < sections.Count; i++) nodes.Add(b.Node(1000 * (i + 1), 0, 0, "N" + (i + 1), null));
        b.Force(nodes.Last(), "A", 0, 0, -100);
        // Plates: three of «Soletta 250», one of «Soletta 300», clamped along x = 0; the nodes of every plate corner (Build Model finds them by
        // tolerance), loaded at (4000, 6000).
        var thin = GpcPropertyBuilders.Plate("Soletta 250", 0, Concrete("C32/40", 33000), 250, 250);
        var thick = GpcPropertyBuilders.Plate("Soletta 300", 0, Concrete("C35/45", 34000), 300, 300);
        for (int i = 0; i <= 4; i++)
            foreach (double y in new[] { 5000.0, 6000.0 })
            {
                int corner = i == 0 ? Fixed(b, "Q" + i + "-" + y, 0, y) : b.Node(1000 * i, y, 0, "Q" + i + "-" + y, null);
                if (i == 4 && y == 6000) b.Force(corner, "A", 0, 0, -1000);
            }
        for (int i = 0; i < sections.Count; i++)
            R4bInputs.Beam(b, nodes[i], nodes[i + 1], GpcPropertyBuilders.Beam("Property " + i, 5, sections[i].Section, Steel()), null, "B" + i);
        for (int i = 0; i < 4; i++)
            b.Plates.Add(GpcPlateBuilders.Create(Quad(1000 * i, 5000, 1000 * (i + 1), 6000), i == 3 ? thick : thin, null, 0, 0, "Plate " + (i + 1), b.Next("plate")));
        var input = R4bInputs.Build(b, "names");
        string folder = Folder(), path = Path.Combine(folder, "names.st7");
        StrausAnalysis.Run(input, path, 0, new[] { "A" }, false, out _);
        using (var api = new Straus7NativeApi())
        {
            api.OpenModelReadOnly(path, folder);
            var labels = new Dictionary<string, string>();
            for (int k = 1; k <= api.Count(Straus7Entity.Beam); k++) { var p = api.ReadProperty(Straus7Entity.Beam, api.ReadElement(Straus7Entity.Beam, k).Property); labels[p.Name] = p.SectionName; }
            for (int i = 0; i < sections.Count; i++)
                Check(labels.TryGetValue("Property " + i, out var label) && label == sections[i].Label, $"P26-01 section label of {(i < kinds.Length ? kinds[i].Kind.ToString() : "native RectangleSolid")}: '{(labels.TryGetValue("Property " + i, out var l) ? l : "?")}', 0.3.0 '{sections[i].Label}'");
            var plates = Enumerable.Range(1, api.Count(Straus7Entity.Plate)).Select(k => api.ReadElement(Straus7Entity.Plate, k)).ToArray();
            var properties = plates.Select(p => p.Property).Distinct().Select(p => api.ReadProperty(Straus7Entity.Plate, p)).ToArray();
            Check(properties.Length == 2 && properties.Select(p => p.Name).OrderBy(n => n).SequenceEqual(new[] { "Soletta 250", "Soletta 300" }),
                "P26-03 one native property per Plate Property: " + string.Join(", ", properties.Select(p => p.Number + " '" + p.Name + "'")));
            Check(properties.All(p => p.MaterialName == (p.Name == "Soletta 250" ? "C32/40" : "C35/45")), "P26-02 material names of the plate properties: " + string.Join(", ", properties.Select(p => p.Name + " '" + p.MaterialName + "'")));
            Check(plates.Count(p => properties.Single(q => q.Number == p.Property).Name == "Soletta 250") == 3, "P26-03 three plates on «Soletta 250»");
        }
        // No value moves: the same GPC input written by the Converter without the options, every result bit for bit.
        var today = GpcStrausAnalysis.LinearStatic(input.Model, Path.Combine(folder, "today.st7"), folder, nativeSections: input.Report.NativeSections);
        var legacy = GpcStrausAnalysis.LinearStatic(input.Model, Path.Combine(folder, "legacy.st7"), folder, nativeSections: input.Report.NativeSections,
            writeOptions: GpcStrausAnalysis.LegacyWriteOptions(input.Model, input.Report));
        int compared = 0, differ = 0;
        foreach (var element in today.Model.AllElements)
        {
            var other = legacy.Model.AllElements.Single(e => e.Guid == element.Guid);
            var x = element.Results.SelectMany(r => r.Results).Select(Text).ToArray(); var y = other.Results.SelectMany(r => r.Results).Select(Text).ToArray();
            compared += x.Length; if (!x.SequenceEqual(y)) differ++;
        }
        Check(compared > 0 && differ == 0, $"P26-01/02/03 move no value: {compared} result locations equal bit for bit ({differ} elements differ)");
    }

    private static string Text(GPC.Model.Results.Locations.ResultLocation l) => l switch
    {
        GPC.Model.Results.Locations.NodeResultDisplacement d => "D " + l.Case.Name + " " + string.Join(",", new[] { d.ResultDisplacement.D1, d.ResultDisplacement.D2, d.ResultDisplacement.D3, d.ResultDisplacement.R1, d.ResultDisplacement.R2, d.ResultDisplacement.R3 }.Select(R)),
        GPC.Model.Results.Locations.StationResultBeamForces f => "B " + l.Case.Name + " " + R(f.ParametricDistance) + " " + string.Join(",", new[] { f.ResultBeamForces.N, f.ResultBeamForces.V1, f.ResultBeamForces.V2, f.ResultBeamForces.T, f.ResultBeamForces.M1, f.ResultBeamForces.M2 }.Select(R)),
        GPC.Model.Results.Locations.PointResultPlateForces p => "P " + l.Case.Name + " " + p.SourceNodeId + " " + string.Join(",", new[] { p.Forces.Fxx, p.Forces.Fyy, p.Forces.Fxy, p.Forces.Fxz, p.Forces.Fyz, p.Forces.Mxx, p.Forces.Myy, p.Forces.Mxy }.Select(R)),
        GPC.Model.Results.Locations.NodeResultForces r => "R " + l.Case.Name + " " + r.Kind + " " + string.Join(",", new[] { r.ResultBeamForces.N, r.ResultBeamForces.V1, r.ResultBeamForces.V2, r.ResultBeamForces.T, r.ResultBeamForces.M1, r.ResultBeamForces.M2 }.Select(R)),
        _ => l.GetType().Name + " " + l.Case?.Name,
    };

    /// <summary>Two clamped spans of 1000 mm: A a concentrated force at 0.37 of the first span, B a distributed force on the second, C a nodal
    /// force at the middle support; AB = 1.35 A + 1.5 B, AC = A + C.</summary>
    private static GpcModels.Conversion TwoSpans()
    {
        var b = new GpcNativeExamples.Builder("c2-two-spans");
        var n = new[] { Fixed(b, "1", 0, 0), b.Node(1000, 0, 0, "2", null), Fixed(b, "3", 2000, 0) };
        b.Force(n[1], "C", 0, 0, -500);
        var property = Generic("Rect", 20000, 66666666.7, 16666666.7, 45800000);
        int first = R4bInputs.Beam(b, n[0], n[1], property, null, "1"), second = R4bInputs.Beam(b, n[1], n[2], property, null, "2");
        b.Beams[first] = GpcBeamBuilders.WithPointLoad(b.Beams[first], b.Case("A"), false, 0.37, new GPC.Geometry.Vector3d(0, 0, -1000), b.Next("point"));
        b.Beams[second] = GpcBeamBuilders.WithDistributedLoad(b.Beams[second], b.Case("B"), BridgeLoadSchema.Uniform, false, "Z", -2, -2, 0, 0, 0, 0, true, b.Next("distributed"));
        b.Combinations.Add(b.Combination("AB", ("A", 1.35), ("B", 1.5)));
        b.Combinations.Add(b.Combination("AC", ("A", 1), ("C", 1)));
        return R4bInputs.Build(b, "two-spans");
    }

    private static string Label(GpcNativeExamples.Results m, int entry) => m.Catalog.Entries[entry].Label;
    private static List<GpcResultRows.BeamRow> Rows(GpcNativeExamples.Results m, int beam, string name) => m.BeamRows(beam, name).OrderBy(r => r.Station).ToList();
    private static double[] V(GpcResultRows.BeamRow r) => new[] { r.Axial, r.Shear1, r.Shear2, r.Torque, r.Bending1, r.Bending2 };

    /// <summary>P26-04 and P26-07 on the analysis of Straus R3 Analysis.</summary>
    private static void Cases()
    {
        string folder = Folder(), selected = Path.Combine(folder, "selected.st7"), every = Path.Combine(folder, "every.st7");
        var input = TwoSpans();
        var some = new GpcNativeExamples.Results(StrausAnalysis.Run(input, selected, 0, new[] { "A", "AB" }, false, out _));
        var all = new GpcNativeExamples.Results(StrausAnalysis.Run(input, every, 0, GpcResultSelection.Catalog(input.Model, 0).Select(e => e.Name).ToArray(), false, out _));
        using (var api = new Straus7NativeApi())
        {
            api.OpenModelReadOnly(selected, folder);
            var states = Enumerable.Range(1, api.LoadCaseCount).Select(i => api.ReadLoadCase(i) + "=" + (api.ReadLinearCaseEnabled(i) ? 1 : 0)).ToArray();
            Check(states.SequenceEqual(new[] { "A=1", "B=1", "C=0", "Dead=0" }), "P26-04 cases solved for A and AB (A, B; not C, Dead), as 0.3.0: " + string.Join(" ", states));
        }
        // The requested values do not move: A and AB equal bit for bit to the run of every case (where AB is read from Straus7 too).
        bool same = true; int rows = 0;
        foreach (string name in new[] { "A", "AB" })
        {
            for (int k = 0; k < 2; k++) { var x = Rows(some, k, name); var y = Rows(all, k, name); rows += x.Count; same &= x.Count == y.Count && x.Zip(y, (p, q) => p.Station == q.Station && V(p).SequenceEqual(V(q))).All(t => t); }
            for (int k = 0; k < some.NodeCount; k++)
            {
                var x = some.NodeRows(k).Where(r => r.Displacement != null && Label(some, r.Entry) == name).SelectMany(r => r.Displacement.Take(3)).ToArray();
                var y = all.NodeRows(k).Where(r => r.Displacement != null && Label(all, r.Entry) == name).SelectMany(r => r.Displacement.Take(3)).ToArray();
                same &= x.Length > 0 && x.SequenceEqual(y); // P2R (task 39, T05): every node has its displacements of A and AB (two empty sets passed before)
            }
        }
        Check(same && rows > 0 && !some.AllNodeRows().Any(r => Label(some, r.Entry) == "B" || Label(some, r.Entry) == "C"),
            $"P26-04 requested values unchanged bit for bit ({rows} beam rows of A and AB), no result of the cases not requested");
        // P26-07: AB at the stations of the Straus7 combination, against the superposition of A and B at their common stations (BridGHe until C2).
        int lost = 0;
        for (int k = 0; k < 2; k++)
        {
            var ra = Rows(all, k, "A"); var rb = Rows(all, k, "B"); var rab = Rows(all, k, "AB");
            var common = ra.Select(r => r.Station).Where(s => rb.Any(r => Math.Abs(r.Station - s) < 1e-9)).ToArray();
            double scale = rab.SelectMany(V).Max(Math.Abs), worst = 0; int compared = 0;
            foreach (var s in common)
            {
                // P2R (task 39, T04): a common station of A and B must have its row of AB (before: skipped, and no station compared passed).
                var ab = rab.FirstOrDefault(r => Math.Abs(r.Station - s) < 1e-9); if (ab == null) continue; compared++;
                var va = V(ra.First(r => Math.Abs(r.Station - s) < 1e-9)); var vb = V(rb.First(r => Math.Abs(r.Station - s) < 1e-9)); var vab = V(ab);
                for (int c = 0; c < 6; c++) worst = Math.Max(worst, Math.Abs(vab[c] - (1.35 * va[c] + 1.5 * vb[c])) / scale);
            }
            var extra = rab.Where(r => !common.Any(s => Math.Abs(s - r.Station) < 1e-9)).ToArray(); lost += extra.Length;
            Console.WriteLine($"C2 P26-07 beam {k + 1}: A {ra.Count} stations, B {rb.Count}, common {common.Length} (the rows of AB before C2); Straus7 combination AB {rab.Count}: "
                + string.Join(" ", rab.Select(r => R(r.Station))) + "; not common to A and B: " + string.Join("; ", extra.Select(r => "x " + R(r.Station) + " My " + R(r.Bending2) + " Vy " + R(r.Shear2))));
            Check(common.Length > 0 && compared == common.Length, $"P26-07 beam {k + 1}: every one of the {common.Length} stations common to A and B has its row of AB ({compared} compared)");
            Check(worst < 1e-12, $"P26-07 beam {k + 1}: AB equal to 1.35 A + 1.5 B at the common stations (worst {R(worst)} of the largest value)");
        }
        Check(lost > 0, $"P26-07 the Straus7 combination has {lost} stations (load sides, extrema of the combination) that the superposition at the common stations lost");
    }

    /// <summary>P26-07 on plates (as the plate-angle case of P2Y): a pressure P and a pre-stress S on a clamped strip, PS = 1.0 P + 0.5 S read
    /// from Straus7; the plate rows of PS equal the superposition of P and S to roundoff, every node of every plate.</summary>
    private static void Plates()
    {
        var b = new GpcNativeExamples.Builder("c2-plates");
        var shell = GpcPropertyBuilders.Plate("Shell 20", 0, Steel(), 20, 20);
        for (int i = 0; i <= 3; i++)
            foreach (double y in new[] { 0.0, 1000.0 })
                if (i == 0) Fixed(b, "S" + i + "-" + y, 0, y); else b.Node(1000 * i, y, 0, "S" + i + "-" + y, null);
        for (int i = 0; i < 3; i++)
        {
            var plate = GpcPlateBuilders.Create(Quad(1000 * i, 0, 1000 * (i + 1), 1000), shell, null, 0, 0, "Q" + (i + 1), b.Next("plate"));
            plate = GpcPlateBuilders.WithPressure(plate, b.Case("P"), false, null, 0, 0, -0.01, false, b.Next("pressure"));
            plate = GpcPlateBuilders.WithPrestress(plate, b.Case("S"), 5, -2, 0, b.Next("prestress"));
            b.Plates.Add(plate);
        }
        b.Combinations.Add(b.Combination("PS", ("P", 1.0), ("S", 0.5)));
        var input = R4bInputs.Build(b, "plates");
        string folder = Folder();
        var result = new GpcNativeExamples.Results(StrausAnalysis.Run(input, Path.Combine(folder, "plates.st7"), 0, new[] { "P", "S", "PS" }, false, out _));
        double worst = 0, scale = 0; int rows = 0;
        for (int k = 0; k < result.Model.AreaElements.Count; k++)
        {
            GpcResultRows.PlateRow[] Of(string name) => result.PlateRows(k).Where(r => Label(result, r.Entry) == name).ToArray();
            var rp = Of("P"); var rs = Of("S"); var rps = Of("PS");
            if (rps.Length != rp.Length || rps.Length == 0) { Check(false, "P26-07 plate Q" + (k + 1) + ": " + rps.Length + " rows of PS, " + rp.Length + " of P"); continue; }
            for (int j = 0; j < rps.Length; j++)
            {
                rows++;
                var pa = rp[j].Values; var pb = rs[j].Values; var pc = rps[j].Values;
                for (int c = 0; c < pc.Length; c++) { scale = Math.Max(scale, Math.Abs(pc[c])); worst = Math.Max(worst, Math.Abs(pc[c] - (pa[c] + 0.5 * pb[c]))); }
            }
        }
        Check(rows == 12 && worst <= 1e-12 * scale, $"P26-07 plates: {rows} rows of PS (Straus7 combination case) equal to 1.0 P + 0.5 S (worst {R(worst)}, largest value {R(scale)})");
    }

    /// <summary>P26-04, moving-only: only the generated cases are solved (0.3.0 disabled the ordinary ones).</summary>
    private static void Moving()
    {
        const double length = 6000; var b = new GpcNativeExamples.Builder("c2-moving");
        var property = Generic("Elastic beam", 60000, 450000000, 200000000, 100000000);
        for (int i = 0; i <= 12; i++)
        {
            int n = b.Node(length * i / 12, 0, 0, (i + 1).ToString(CultureInfo.InvariantCulture), null); b.Restrain(n, i == 0, true, i == 0 || i == 12, true, false, true);
            if (i == 6) b.Force(n, "G", 0, 0, -10);
        }
        for (int i = 0; i < 12; i++) R4bInputs.Beam(b, i, i + 1, property, null, (i + 1).ToString(CultureInfo.InvariantCulture));
        b.Paths.Add(new MovingPath { LoadCaseName = "Traffic", Divisions = 5, Target = 2, Coordinates = new double[] { 0, 0, 0, length, 0, 0, 0, 1000, 0 }, ConvertToStaticCases = true,
            Template = new MovingTemplate { Name = "Single axle", Vehicles = new[] { new MovingVehicle { Name = "1000 N", Loads = new MovingObject[] { new MovingPointForce { Force = new double[] { 0, 0, -1000 } } } } } } });
        var input = b.Build("moving").Input;
        string folder = Folder(), path = Path.Combine(folder, "moving.st7");
        var result = new GpcNativeExamples.Results(StrausAnalysis.Run(input, path, 0, Array.Empty<string>(), true, out _));
        using var api = new Straus7NativeApi(); api.OpenModelReadOnly(path, folder);
        var states = Enumerable.Range(1, api.LoadCaseCount).Select(i => (Name: api.ReadLoadCase(i), On: api.ReadLinearCaseEnabled(i))).ToArray();
        var generated = new HashSet<string>(result.GeneratedMovingCases.Select(c => c.Name));
        Check(generated.Count == 5 && states.All(s => s.On == generated.Contains(s.Name)), "P26-04 moving-only: only the generated cases solved: " + string.Join(" ", states.Select(s => s.Name + "=" + (s.On ? 1 : 0))));
        var file = api.OpenResults(Path.ChangeExtension(path, "lsa")); Check(file.PrimaryCases == 5, "P26-04 moving-only: 5 result cases");
    }

    /// <summary>P26-06: the solver settings of the saved model (nonlinear increments and linear).</summary>
    private static void Solver()
    {
        foreach (int solver in new[] { 1, 0 })
        {
            var b = new GpcNativeExamples.Builder("c2-solver-" + solver);
            int n0 = Fixed(b, "1", 0, 0), n1 = b.Node(3000, 0, 0, "2", null); b.Force(n1, "P", 0, 0, -1000);
            R4bInputs.Beam(b, n0, n1, Generic("Elastic", 60000, 450000000, 200000000, 100000000), null, "1");
            b.Combinations.Add(b.Combination("Full", ("P", 1)));
            var input = R4bInputs.Build(b, "solver" + solver);
            string folder = Folder(), path = Path.Combine(folder, "solver" + solver + ".st7");
            StrausAnalysis.Run(input, path, solver, new[] { solver == 1 ? "Full" : "P" }, false, out _);
            using var api = new Straus7NativeApi(); api.OpenModelReadOnly(path, folder); var s = api.ReadSolverSettings();
            Check(s.NonlinearGeometry && s.NonlinearMaterial && s.DisplacementTolerance == 1e-4 && s.ResidualTolerance == 1e-3 && s.MaximumIterations == 20,
                $"P26-06 solver {solver}: the settings of the new file, as 0.3.0: {s}");
        }
    }
}
