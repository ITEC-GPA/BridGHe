#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using BridGHe.Common.Straus;
using GPC.Converter.Straus7;
using Rhino.Geometry;

/// <summary>
/// C2 (11/11/2026, user decision Â«come la 0.3.0Â» on P26-01â€¦07): the Straus7 behaviour of BridGHe 0.3.0 through the GPC Converter, natively,
/// on the FEM path of Straus R3 Analysis and Moving Static Analysis (core-only --native, and --c2-parity-native):
/// <list type="bullet">
/// <item>P26-01/02/03: the section label of the eight standard kinds 0.3.0 labelled (none on a native section), the material name of the plate
/// properties, one native plate property per Plate Property named as it; no result value moves (bit for bit against the file of the Converter);</item>
/// <item>P26-04: only the requested cases and the cases of the requested combinations are solved; only the generated cases in a moving-only run;
/// the requested values equal bit for bit to a run of every case;</item>
/// <item>P26-06: the solver settings of the new file are kept (geometric and material nonlinearity, Straus7 tolerances), as 0.3.0;</item>
/// <item>P26-07: the combinations are the LSA combination cases Straus7 computes: the beam rows at the stations of the combination (a concentrated
/// load off the regular stations in A, a distributed load in B), against the superposition at the stations common to A and B of BridGHe before C2.</item>
/// </list>
/// </summary>
internal static class C2ParityNative
{
    private static int failures;
    private static void Check(bool ok, string what) { if (!ok) failures++; Console.WriteLine((ok ? "PASS: " : "FAIL: ") + "C2 " + what); }
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static MaterialModel Steel() => new("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3 };
    private static NodeModel Fixed(string id, Point3d p) { var n = new NodeModel(id, p); n.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true)); return n; }
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
        var model = new ModelModel(ModelModel.ModelUnits.Nmm); var a = new LoadCaseModel("A", "A"); model.LoadCases.Add(a);
        var kinds = new (SectionModel.SectionTypes Kind, Action<SectionModel> Set, string Label)[]
        {
            (SectionModel.SectionTypes.SolidCircle, s => s.D = 200, "SolidRound"), (SectionModel.SectionTypes.HollowCircle, s => { s.D = 200; s.T = 10; }, "HollowRound"),
            (SectionModel.SectionTypes.SolidRectangle, s => { s.B = 200; s.D = 400; }, "SolidRect"), (SectionModel.SectionTypes.HollowRectangle, s => { s.B = 200; s.D = 400; s.T1 = 10; s.T2 = 12; }, "HollowRect"),
            (SectionModel.SectionTypes.I, s => { s.B1 = 200; s.B2 = 180; s.D = 400; s.T1 = 12; s.T2 = 14; s.T3 = 8; }, "IBeam"), (SectionModel.SectionTypes.T, s => { s.B = 200; s.D = 400; s.T1 = 12; s.T2 = 14; s.T3 = 8; }, "TBeam"),
            (SectionModel.SectionTypes.C, s => { s.B = 100; s.D = 300; s.T1 = 10; s.T2 = 8; s.T3 = 8; }, "Lipped Channel"), (SectionModel.SectionTypes.Angle, s => { s.B = 150; s.D = 100; s.T1 = 10; s.T2 = 12; }, "Angle"),
        };
        var sections = kinds.Select(k => { var s = new SectionModel { SectionType = k.Kind }; k.Set(s); s.CalculateSection(); return (Section: s, k.Label); }).ToList();
        // A section of the Native Section component (no label in 0.3.0: StrausSection.Assign sets none).
        sections.Add((new StrausSection { Key = "RectangleSolid", Dimensions = new[] { 200.0, 400.0 } }.Calculate(), ""));
        var nodes = new List<NodeModel> { Fixed("N0", Point3d.Origin) };
        for (int i = 0; i < sections.Count; i++) nodes.Add(new NodeModel("N" + (i + 1), new Point3d(1000 * (i + 1), 0, 0)));
        nodes.Last().Loads.Add(new NodeForceLoadModel(a, new Vector3d(0, 0, -100)));
        model.AddElements(nodes.ToArray());
        for (int i = 0; i < sections.Count; i++)
            model.AddElements(new ElementModel[] { new BeamModel(nodes[i].Position, nodes[i + 1].Position, new BeamPropertyModel("P" + i) { Name = "Property " + i, Material = Steel(), Section = sections[i].Section, PropertyType = BeamPropertyType.Beam }) { BeamId = "B" + i } });
        // Plates: three of Â«Soletta 250Â», one of Â«Soletta 300Â», clamped along x = 0.
        var thin = new PlatePropertyModel("S250") { Name = "Soletta 250", Material = new MaterialModel("C32/40") { Name = "C32/40", Modulus = 33000, PoissonRatio = 0.2 }, PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 250, BendingThickness = 250 };
        var thick = new PlatePropertyModel("S300") { Name = "Soletta 300", Material = new MaterialModel("C35/45") { Name = "C35/45", Modulus = 34000, PoissonRatio = 0.2 }, PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 300, BendingThickness = 300 };
        // The FEM nodes of every plate corner (the reader finds them by tolerance), clamped along x = 0, loaded at (4000, 6000).
        for (int i = 0; i <= 4; i++)
            foreach (double y in new[] { 5000.0, 6000.0 })
            {
                var corner = i == 0 ? Fixed("Q" + i + "-" + y, new Point3d(0, y, 0)) : new NodeModel("Q" + i + "-" + y, new Point3d(1000 * i, y, 0));
                if (i == 4 && y == 6000) corner.Loads.Add(new NodeForceLoadModel(a, new Vector3d(0, 0, -1000)));
                model.AddElements(new ElementModel[] { corner });
            }
        for (int i = 0; i < 4; i++)
            model.AddElements(new ElementModel[] { new PlateModel("Plate " + (i + 1), new[] { new Point3d(1000 * i, 5000, 0), new Point3d(1000 * (i + 1), 5000, 0), new Point3d(1000 * (i + 1), 6000, 0), new Point3d(1000 * i, 6000, 0) }, i == 3 ? thick : thin) });
        string folder = Folder(), path = Path.Combine(folder, "names.st7");
        StrausAnalysis.Run(model, path, 0, new[] { "A" }, 1e-6, false, out _, out var analysed);
        using (var api = new Straus7NativeApi())
        {
            api.OpenModelReadOnly(path, folder);
            var labels = new Dictionary<string, string>();
            for (int b = 1; b <= api.Count(Straus7Entity.Beam); b++) { var p = api.ReadProperty(Straus7Entity.Beam, api.ReadElement(Straus7Entity.Beam, b).Property); labels[p.Name] = p.SectionName; }
            for (int i = 0; i < sections.Count; i++)
                Check(labels.TryGetValue("Property " + i, out var label) && label == sections[i].Label, $"P26-01 section label of {(i < kinds.Length ? kinds[i].Kind.ToString() : "native RectangleSolid")}: '{(labels.TryGetValue("Property " + i, out var l) ? l : "?")}', 0.3.0 '{sections[i].Label}'");
            var plates = Enumerable.Range(1, api.Count(Straus7Entity.Plate)).Select(k => api.ReadElement(Straus7Entity.Plate, k)).ToArray();
            var properties = plates.Select(p => p.Property).Distinct().Select(p => api.ReadProperty(Straus7Entity.Plate, p)).ToArray();
            Check(properties.Length == 2 && properties.Select(p => p.Name).OrderBy(n => n).SequenceEqual(new[] { "Soletta 250", "Soletta 300" }),
                "P26-03 one native property per Plate Property: " + string.Join(", ", properties.Select(p => p.Number + " '" + p.Name + "'")));
            Check(properties.All(p => p.MaterialName == (p.Name == "Soletta 250" ? "C32/40" : "C35/45")), "P26-02 material names of the plate properties: " + string.Join(", ", properties.Select(p => p.Name + " '" + p.MaterialName + "'")));
            Check(plates.Count(p => properties.Single(q => q.Number == p.Property).Name == "Soletta 250") == 3, "P26-03 three plates on Â«Soletta 250Â»");
        }
        // No value moves: the same conversion written by the Converter without the options, every result bit for bit.
        var conversion = GpcModels.Convert(model, 1e-6);
        var today = GpcStrausAnalysis.LinearStatic(conversion.Model, Path.Combine(folder, "today.st7"), folder, nativeSections: conversion.Report.NativeSections);
        var legacy = GpcStrausAnalysis.LinearStatic(conversion.Model, Path.Combine(folder, "legacy.st7"), folder, nativeSections: conversion.Report.NativeSections,
            writeOptions: GpcStrausAnalysis.LegacyWriteOptions(conversion.Model, conversion.Report));
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
    private static ModelModel TwoSpans()
    {
        var model = new ModelModel(ModelModel.ModelUnits.Nmm);
        var a = new LoadCaseModel("A", "A"); var b = new LoadCaseModel("B", "B"); var c = new LoadCaseModel("C", "C"); model.LoadCases.Add(a); model.LoadCases.Add(b); model.LoadCases.Add(c);
        var n = new[] { Fixed("1", Point3d.Origin), new NodeModel("2", new Point3d(1000, 0, 0)), Fixed("3", new Point3d(2000, 0, 0)) };
        n[1].Loads.Add(new NodeForceLoadModel(c, new Vector3d(0, 0, -500))); model.AddElements(n);
        var property = new BeamPropertyModel("P") { Name = "Rect", Material = Steel(), PropertyType = BeamPropertyType.Beam,
            Section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 20000, I11 = 66666666.7, I22 = 16666666.7, J = 45800000, ShearA1 = 0, ShearA2 = 0 } };
        var first = new BeamModel(n[0].Position, n[1].Position, property) { BeamId = "1" }; var second = new BeamModel(n[1].Position, n[2].Position, property) { BeamId = "2" };
        first.Loads.Add(new BeamPointLoadModel(a, new BeamPointLoadModel.LoadData { P = new Vector3d(0, 0, -1000), A = 0.37 }, BeamPointLoadModel.LoadType.Force));
        second.Loads.Add(new BeamDistributedLoadModel(b, new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.Uniform, CoordinateSystem = CoordinateSystemModel.Global,
            LoadDirection = BeamDistributedLoadModel.LoadDirection.Z, PA = -2, PB = -2 }, BeamDistributedLoadModel.LoadType.Force));
        model.AddElements(new ElementModel[] { first, second });
        var ab = new LoadCombinationModel("AB"); ab.Values.Add(a, 1.35); ab.Values.Add(b, 1.5);
        var ac = new LoadCombinationModel("AC"); ac.Values.Add(a, 1); ac.Values.Add(c, 1);
        model.AddLoadCombinations(new[] { ab, ac });
        return model;
    }

    private static List<BeamForceResultModel> Rows(ModelModel m, int beam, string name) => m.Beams[beam].Results.OfType<BeamForceResultModel>().Where(r => r.LoadCase.Name == name).OrderBy(r => r.Station).ToList();
    private static double[] V(BeamForceResultModel r) => new[] { r.AxialForce, r.ShearForce.X, r.ShearForce.Y, r.Torque, r.BendingMoment.X, r.BendingMoment.Y };

    /// <summary>P26-04 and P26-07 on the FEM path of Straus R3 Analysis.</summary>
    private static void Cases()
    {
        string folder = Folder(), selected = Path.Combine(folder, "selected.st7"), every = Path.Combine(folder, "every.st7");
        var some = StrausAnalysis.Run(TwoSpans(), selected, 0, new[] { "A", "AB" }, 1e-6, false, out _);
        var all = StrausAnalysis.Run(TwoSpans(), every, 0, StrausResultSelection.Catalog(TwoSpans(), 0).Select(e => e.Case.Name).ToArray(), 1e-6, false, out _);
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
            for (int k = 0; k < some.Nodes.Count; k++)
            {
                var x = some.Nodes[k].Results.OfType<NodeDisplResultModel>().Where(r => r.LoadCase.Name == name).Select(r => r.Displacement).ToArray();
                var y = all.Nodes[k].Results.OfType<NodeDisplResultModel>().Where(r => r.LoadCase.Name == name).Select(r => r.Displacement).ToArray();
                same &= x.Length > 0 && x.SequenceEqual(y); // P2R (task 39, T05): every node has its displacements of A and AB (two empty sets passed before)
            }
        }
        Check(same && rows > 0 && !some.Nodes.SelectMany(n => n.Results).Any(r => r.LoadCase.Name == "B" || r.LoadCase.Name == "C"), $"P26-04 requested values unchanged bit for bit ({rows} beam rows of A and AB), no result of the cases not requested");
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
                + string.Join(" ", rab.Select(r => R(r.Station))) + "; not common to A and B: " + string.Join("; ", extra.Select(r => "x " + R(r.Station) + " My " + R(r.My) + " Vy " + R(r.Vy))));
            Check(common.Length > 0 && compared == common.Length, $"P26-07 beam {k + 1}: every one of the {common.Length} stations common to A and B has its row of AB ({compared} compared)");
            Check(worst < 1e-12, $"P26-07 beam {k + 1}: AB equal to 1.35 A + 1.5 B at the common stations (worst {R(worst)} of the largest value)");
        }
        Check(lost > 0, $"P26-07 the Straus7 combination has {lost} stations (load sides, extrema of the combination) that the superposition at the common stations lost");
    }

    /// <summary>P26-07 on plates (as the plate-angle case of P2Y): a pressure P and a pre-stress S on a clamped strip, PS = 1.0 P + 0.5 S read
    /// from Straus7; the plate rows of PS equal the superposition of P and S to roundoff, every node of every plate.</summary>
    private static void Plates()
    {
        var model = new ModelModel(ModelModel.ModelUnits.Nmm); var p = new LoadCaseModel("P", "P"); var s = new LoadCaseModel("S", "S"); model.LoadCases.Add(p); model.LoadCases.Add(s);
        var shell = new PlatePropertyModel("Shell") { Name = "Shell 20", Material = Steel(), PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 20, BendingThickness = 20 };
        for (int i = 0; i <= 3; i++)
            foreach (double y in new[] { 0.0, 1000.0 })
                model.AddElements(new ElementModel[] { i == 0 ? Fixed("S" + i + "-" + y, new Point3d(0, y, 0)) : new NodeModel("S" + i + "-" + y, new Point3d(1000 * i, y, 0)) });
        for (int i = 0; i < 3; i++)
        {
            var plate = new PlateModel("Q" + (i + 1), new[] { new Point3d(1000 * i, 0, 0), new Point3d(1000 * (i + 1), 0, 0), new Point3d(1000 * (i + 1), 1000, 0), new Point3d(1000 * i, 1000, 0) }, shell);
            plate.Loads.Add(new PlatePressureModel(p, new() { P = new Vector3d(0, 0, -0.01) }));
            plate.Loads.Add(new PlatePreStressModel(s, new() { LocalPressure = new Vector3d(5, -2, 0) }));
            model.AddElements(new ElementModel[] { plate });
        }
        var ps = new LoadCombinationModel("PS"); ps.Values.Add(p, 1.0); ps.Values.Add(s, 0.5); model.AddLoadCombinations(new[] { ps });
        string folder = Folder();
        var result = StrausAnalysis.Run(model, Path.Combine(folder, "plates.st7"), 0, new[] { "P", "S", "PS" }, 1e-6, false, out _);
        double worst = 0, scale = 0; int rows = 0;
        foreach (var plate in result.Plates)
        {
            PlateForceResultModel[] Of(string name) => plate.Results.OfType<PlateForceResultModel>().Where(r => r.LoadCase.Name == name).ToArray();
            var rp = Of("P"); var rs = Of("S"); var rps = Of("PS");
            if (rps.Length != rp.Length || rps.Length == 0) { Check(false, "P26-07 plate " + plate.PlateId + ": " + rps.Length + " rows of PS, " + rp.Length + " of P"); continue; }
            for (int k = 0; k < rps.Length; k++)
            {
                rows++;
                double[] V(PlateForceResultModel r) => new[] { r.Force.X, r.Force.Y, r.Force.Z, r.Moment.X, r.Moment.Y, r.Moment.Z, r.TransverseShearForce.X, r.TransverseShearForce.Y };
                var a = V(rp[k]); var b = V(rs[k]); var c = V(rps[k]);
                for (int j = 0; j < c.Length; j++) { scale = Math.Max(scale, Math.Abs(c[j])); worst = Math.Max(worst, Math.Abs(c[j] - (a[j] + 0.5 * b[j]))); }
            }
        }
        Check(rows == 12 && worst <= 1e-12 * scale, $"P26-07 plates: {rows} rows of PS (Straus7 combination case) equal to 1.0 P + 0.5 S (worst {R(worst)}, largest value {R(scale)})");
    }

    /// <summary>P26-04, moving-only: only the generated cases are solved (0.3.0 disabled the ordinary ones).</summary>
    private static void Moving()
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
        m.MovingPaths.Add(new MovingPath { LoadCaseName = "Traffic", Divisions = 5, Target = 2, Coordinates = new double[] { 0, 0, 0, length, 0, 0, 0, 1000, 0 }, ConvertToStaticCases = true,
            Template = new MovingTemplate { Name = "Single axle", Vehicles = new[] { new MovingVehicle { Name = "1000 N", Loads = new MovingObject[] { new MovingPointForce { Force = new double[] { 0, 0, -1000 } } } } } } });
        string folder = Folder(), path = Path.Combine(folder, "moving.st7");
        var result = StrausAnalysis.Run(m, path, 0, Array.Empty<string>(), 1e-6, true, out _);
        using var api = new Straus7NativeApi(); api.OpenModelReadOnly(path, folder);
        var states = Enumerable.Range(1, api.LoadCaseCount).Select(i => (Name: api.ReadLoadCase(i), On: api.ReadLinearCaseEnabled(i))).ToArray();
        var generated = new HashSet<string>(result.GeneratedMovingCases.Values);
        Check(generated.Count == 5 && states.All(s => s.On == generated.Contains(s.Name)), "P26-04 moving-only: only the generated cases solved: " + string.Join(" ", states.Select(s => s.Name + "=" + (s.On ? 1 : 0))));
        var file = api.OpenResults(Path.ChangeExtension(path, "lsa")); Check(file.PrimaryCases == 5, "P26-04 moving-only: 5 result cases");
    }

    /// <summary>P26-06: the solver settings of the saved model (nonlinear increments and linear).</summary>
    private static void Solver()
    {
        foreach (int solver in new[] { 1, 0 })
        {
            var m = new ModelModel(ModelModel.ModelUnits.Nmm); var p = new LoadCaseModel("P", "P"); m.LoadCases.Add(p);
            var n0 = Fixed("1", Point3d.Origin); var n1 = new NodeModel("2", new Point3d(3000, 0, 0)); n1.Loads.Add(new NodeForceLoadModel(p, new Vector3d(0, 0, -1000))); m.AddElements(new[] { n0, n1 });
            m.AddElements(new[] { new BeamModel(n0.Position, n1.Position, new BeamPropertyModel("1") { Name = "Elastic", Material = Steel(), PropertyType = BeamPropertyType.Beam,
                Section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 60000, I11 = 450000000, I22 = 200000000, J = 100000000, ShearA1 = 0, ShearA2 = 0 } }) { BeamId = "1" } });
            var full = new LoadCombinationModel("Full"); full.Values.Add(p, 1); m.AddLoadCombinations(new[] { full });
            string folder = Folder(), path = Path.Combine(folder, "solver" + solver + ".st7");
            StrausAnalysis.Run(m, path, solver, new[] { solver == 1 ? "Full" : "P" }, 1e-6, false, out _);
            using var api = new Straus7NativeApi(); api.OpenModelReadOnly(path, folder); var s = api.ReadSolverSettings();
            Check(s.NonlinearGeometry && s.NonlinearMaterial && s.DisplacementTolerance == 1e-4 && s.ResidualTolerance == 1e-3 && s.MaximumIterations == 20,
                $"P26-06 solver {solver}: the settings of the new file, as 0.3.0: {s}");
        }
    }
}
#endif
