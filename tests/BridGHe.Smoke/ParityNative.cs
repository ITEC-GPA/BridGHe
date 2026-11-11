#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BridGHe.Common.Gpc;
using GPC.Converter.Straus7;

/// <summary>
/// PAR (R6.5 P1, 0.3.0 parity on the GPC path, user decision of 8/11 «Sì, come proponi»): the data 0.3.0 wrote to Straus7 that the GPC path
/// refused, written now through the GPC path (the model written by the Converter, as Straus R3 Analysis does, without the solution) and read back
/// from the .st7. R6.6 lane R4b: the model is GPC data alone (<see cref="R4bInputs.Parity"/>: the properties of the Beam Property, section and
/// material builders, Build Model on GPC), no FEM model; the 0.3.0 call sequences take the dimensions and materials of the definition.
/// <list type="bullet">
/// <item>A02/A03: T, C (single) and angle sections: the native geometry (bsTSection, bsLipChannel, bsLSection with the dimensions of 0.3.0) and
/// the section data, compared with the 0.3.0 call sequence replayed on a scratch model (St7SetBeamSectionGeometry, the section data written before
/// the calculation, St7SetBeamMaterialData, St7UpdateElementPropertyData, St7CalculateBeamSectionProperties with the shear): equal; the builder of
/// Beam Property gives the native section;</item>
/// <item>A02c: a Brep section with a hole: a BXS (type 11) with the GPC values; the values Straus7 computes from that BXS alone (the 0.3.0
/// state: St7AssignBXS without section data) are printed and bounded (listed difference: torsion and shear areas from GPC);</item>
/// <item>A01: a cutoff bar: btCutoff with the cut-off type, keep mass and the two limits of 0.3.0 (St7SetCutoffBarData);</item>
/// <item>A04: a thin shell of a material with a stress-strain table: the table of 0.3.0 (strain, stress pairs) on ptPlateStressVsStrain; LIB: a
/// beam and a shell of a Table Material material (E = 0 and the table, with GPC.Model 5.51): E = 0 and the table in Straus7, as 0.3.0 wrote them;</item>
/// <item>A02b (LIB, with GPC Converter 2.21): the double C and double angle: the single native section with the mirror option of 0.3.0 (mtLeft,
/// btFalse, gaps A and B), compared with the 0.3.0 sequence on the scratch model (type, dimensions, mirror option, section data); the beam of a
/// double angle with the Straus7 reference angle of 0.3.0, 90 + angle + 90 (<c>RotateOf90Deg</c>), and its offset as given.</item>
/// </list>
/// <para>Transitional (P2 retires it with the FEM models): the parity model against the conversion of the FEM model of the test before R6.6
/// (<see cref="R4bInputs.Former.ParityDifference"/>), one printed line, not a condition (the conditions are those of 0.3.0).</para>
/// </summary>
internal static class ParityNative
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int NewFile(int uid, string path, string scratch);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int OneInt(int uid);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Units(int uid, int[] units);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int NewProperty(int uid, int prop, int type, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int IntDoubles(int uid, int prop, int value, double[] doubles);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int IntsDoubles(int uid, int prop, int[] ints, double[] doubles);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Doubles(int uid, int prop, double[] doubles);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ThreeInts(int uid, int a, int b);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Calculate(int uid, int prop, byte shear);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int PathCall(int uid, int prop, string path);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TableId(int uid, int type, int prop, ref int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TableRows(int uid, int type, int id, ref int rows);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TableData(int uid, int type, int id, int max, ref int rows, double[] doubles);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SectionGeometry(int uid, int prop, ref int type, double[] doubles);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int SetMirror(int uid, int prop, int type, int twist, double[] gaps);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int GetMirror(int uid, int prop, ref int type, ref int twist, [Out] double[] gaps);

    private static T F<T>(string name) where T : Delegate
    {
        var address = GetProcAddress(GetModuleHandle("St7api.dll"), name);
        if (address == IntPtr.Zero) throw new InvalidOperationException("PAR native: no St7API function " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private static void C(int code, string what) { if (code != 0) throw new InvalidOperationException($"PAR native: {what} returned {code}"); }
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string Rs(IEnumerable<double> v) => string.Join(" ", v.Select(R));

    /// <summary>The checks (0: none failed); one line per item on the console.</summary>
    internal static int Run()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }
        // A02/A03 and A02b: the sections of the T, C and Angle components of 0.3.0 and their double C and double angle; A02c, A01, A04 (R4bInputs).
        var standard = R4bInputs.Standards;
        var properties = R4bInputs.ParityProperties();
        // The builder of Beam Property gives the native Straus7 section of the standard sections (before: the same signature and native section as
        // the reader of the former FEM property, compared now in the transitional line below).
        for (int k = 0; k < standard.Length; k++)
            Check(properties[k].Property.Name == standard[k].Name && properties[k].Data.Native != null, $"A02 {standard[k].Name}: the builder gives the native section");
        {
            var cutoff = properties.Single(p => p.Property.Name == "PAR Cutoff");
            var law = ((GPC.Model.ElementProperties.ElasticBeamProperty)cutoff.Property).CutoffLaw;
            Check(law != null && law.Failure == GPC.Model.ElementProperties.BeamCutoffFailure.Ductile && law.KeepMass && law.TensionLimit == 1.5e5 && law.CompressionLimit == 2.5e5,
                "A01: the builder gives the cutoff law of the inputs (ductile, keep mass, 150000, 250000)");
        }
        var input = R4bInputs.Parity(properties);
        Console.WriteLine("PAR NATIVE TRANSITIONAL: the parity model against the conversion of the former FEM model: " + R4bInputs.Former.ParityDifference(input));
        string folder = Path.GetFullPath(Path.Combine("artifacts", "native", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        string st7 = Path.Combine(folder, "parity.st7");
        // The model the GPC path writes (the Converter writer, as Straus R3 Analysis), without a solution: what Straus7 receives.
        GpcPreflight.Require(input.Model);
        var plan = Straus7ModelWritePlan.Prepare(input.Model, null, null, input.Report.NativeSections);
        using (var writer = new Straus7NativeWriter(st7, folder)) { plan.Write(writer, input.Model); writer.Save(); }

        using (var api = new Straus7NativeApi())
        {
            api.OpenModelReadOnly(st7, folder);
            try
            {
                var byName = new Dictionary<string, Straus7Property>(StringComparer.Ordinal); var beamOf = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int b = 1; b <= api.Count(Straus7Entity.Beam); b++) { var p = api.ReadProperty(Straus7Entity.Beam, api.ReadElement(Straus7Entity.Beam, b).Property); byName[p.Name] = p; beamOf[p.Name] = b; }
                // 0.3.0 replayed on a scratch model (file 7): the calls of the former exporter for these sections.
                string scratch = Path.Combine(folder, "parity-030.st7");
                C(F<NewFile>("St7NewFile")(7, scratch, folder), "St7NewFile");
                try
                {
                    C(F<Units>("St7SetUnits")(7, new[] { 2, 0, 2, 1, 0, 0 }), "St7SetUnits");
                    for (int k = 0; k < standard.Length; k++)
                    {
                        var s = standard[k]; int id = k + 1; var p = byName.TryGetValue(s.Name, out var found) ? found : null;
                        C(F<NewProperty>("St7NewBeamProperty")(7, id, 6, s.Name), "St7NewBeamProperty");
                        var dims = new[] { s.B, s.D, 0, s.T1, s.T2, 0 }; // the dimensions of the definition (B, D, L 0, T1, T2, T3 0), as 0.3.0 wrote them
                        C(F<IntDoubles>("St7SetBeamSectionGeometry")(7, id, s.Native, dims), "St7SetBeamSectionGeometry");
                        // R6.6 R4b: the section data of the GPC file (0.3.0 wrote the former value here; the calculation below overwrites whatever this call wrote).
                        var formerData = p == null ? new double[11] : p.SectionProperties.Take(11).ToArray();
                        int formerCode = F<IntsDoubles>("St7SetBeamSectionPropertyData")(7, id, new[] { 0 }, formerData);
                        if (s.Double) Console.WriteLine($"PAR NATIVE A02b {s.Name}: the section data written before the mirror (the values of the GPC file): {Rs(formerData)}; St7SetBeamSectionPropertyData returned {formerCode}");
                        // A finding of LIB, not a condition: the values the former Angle component computed on the mirrored shapes may be refused by Straus7
                        // here (the former exporter would stop at this call); the calculation below overwrites whatever this call wrote.
                        if (!s.Double) C(formerCode, "St7SetBeamSectionPropertyData");
                        if (s.Double) C(F<SetMirror>("St7SetBeamMirrorOption")(7, id, 3, 0, new[] { s.Gap, 0.0 }), "St7SetBeamMirrorOption"); // mtLeft, btFalse, as 0.3.0
                        var m = new double[9]; m[0] = 210000; m[2] = 0.3; m[3] = 7.85e-9; m[4] = 1.2e-5; // the material of the definition (R4bInputs.ParitySteel)
                        C(F<Doubles>("St7SetBeamMaterialData")(7, id, m), "St7SetBeamMaterialData");
                        C(F<ThreeInts>("St7UpdateElementPropertyData")(7, 1, id), "St7UpdateElementPropertyData");
                        C(F<Calculate>("St7CalculateBeamSectionProperties")(7, id, 1), "St7CalculateBeamSectionProperties");
                        var expected = new double[20]; C(F<IntsDoubles>("St7GetBeamSectionPropertyData")(7, id, new int[4], expected), "St7GetBeamSectionPropertyData");
                        Check(p != null && p.SectionType == s.Native && p.Geometry.SequenceEqual(dims), $"A02 {s.Name}: native geometry {p?.SectionType} [{Rs(p?.Geometry ?? new double[0])}], 0.3.0 {s.Native} [{Rs(dims)}]");
                        double worst = p == null ? double.NaN : Enumerable.Range(0, 11).Max(j => Math.Abs(p.SectionProperties[j] - expected[j]) / Math.Max(1, Math.Abs(expected[j])));
                        Console.WriteLine($"PAR NATIVE A02 {s.Name}: type {p?.SectionType}, section data {Rs(p?.SectionProperties ?? new double[0])}; 0.3.0 {Rs(expected.Take(11))}; worst relative difference {R(worst)}");
                        Check(worst <= 1e-12, $"A02 {s.Name}: section data differ from 0.3.0 by {R(worst)}");
                        // A02b: the mirror option, as the 0.3.0 sequence left it (none on the single sections).
                        int formerMirror = 0, formerTwist = 0; var formerGaps = new double[2];
                        C(F<GetMirror>("St7GetBeamMirrorOption")(7, id, ref formerMirror, ref formerTwist, formerGaps), "St7GetBeamMirrorOption");
                        Console.WriteLine($"PAR NATIVE A02b {s.Name}: mirror {p?.MirrorType} twist {p?.CompatibleTwist} gaps {Rs(p?.MirrorGaps ?? new double[0])}; 0.3.0 {formerMirror} {formerTwist} {Rs(formerGaps)}");
                        Check(p != null && p.MirrorType == formerMirror && p.MirrorType == (s.Double ? 3 : 0) && (p.CompatibleTwist == true ? 1 : 0) == formerTwist && p.MirrorGaps.SequenceEqual(formerGaps),
                            $"A02b {s.Name}: the mirror option differs from 0.3.0 ({p?.MirrorType}/{formerMirror})");
                    }
                    // A02b: the axes of the beam of a double angle: the reference angle of 0.3.0, 90 + angle + 90 (RotateOf90Deg), with the offset as given.
                    {
                        var angle = new double[1]; C(F<Doubles>("St7GetBeamReferenceAngle1")(1, beamOf["PAR 2L"], angle), "St7GetBeamReferenceAngle1");
                        var offset = api.ReadBeamOffset(beamOf["PAR 2L"]);
                        var plain = new double[1]; C(F<Doubles>("St7GetBeamReferenceAngle1")(1, beamOf["PAR 2C"], plain), "St7GetBeamReferenceAngle1");
                        Console.WriteLine($"PAR NATIVE A02b axes: double angle reference angle {R(angle[0])} (0.3.0: 90 + 30 + 90), offset {Rs(offset)} (0.3.0: 5 7); double C {R(plain[0])} (0.3.0: 90)");
                        // The same angle: the Converter writes it from the axes, in (-180, 180]; 0.3.0 wrote 90 + 30 + 90 = 210.
                        Check(Math.Abs(Math.IEEERemainder(angle[0] - 210, 360)) < 1e-9, $"A02b: the reference angle of the double angle is {R(angle[0])}, 0.3.0 wrote 210");
                        Check(Math.Abs(offset[0] - 5) < 1e-9 && Math.Abs(offset[1] - 7) < 1e-9, $"A02b: the offset of the double angle is {Rs(offset)}, 0.3.0 wrote 5 7");
                        Check(Math.Abs(plain[0] - 90) < 1e-9, $"A02b: the reference angle of the double C is {R(plain[0])}, 0.3.0 wrote 90");
                    }
                    // A02c: the BXS of the GPC path, and the values Straus7 computes from it alone (as 0.3.0 left them).
                    var pb = byName.TryGetValue("PAR Brep", out var fb) ? fb : null;
                    Check(pb != null && pb.SectionType == 11, "A02c: the Brep section is a BXS (type " + pb?.SectionType + ")");
                    if (pb != null)
                    {
                        string bxs = Path.Combine(folder, "parity-brep.bxs");
                        C(F<PathCall>("St7ExportBXS")(1, pb.Number, bxs), "St7ExportBXS");
                        C(F<NewProperty>("St7NewBeamProperty")(7, 10, 6, "Brep 0.3.0"), "St7NewBeamProperty");
                        C(F<PathCall>("St7AssignBXS")(7, 10, bxs), "St7AssignBXS");
                        C(F<ThreeInts>("St7UpdateElementPropertyData")(7, 1, 10), "St7UpdateElementPropertyData"); // as the former exporter, after the material
                        var own = new double[20]; C(F<IntsDoubles>("St7GetBeamSectionPropertyData")(7, 10, new int[4], own), "St7GetBeamSectionPropertyData");
                        var names = new[] { "A", "I11", "I22", "J", "SL1", "SL2", "SA1", "SA2", "XBAR", "YBAR", "ANGLE" };
                        Console.WriteLine("PAR NATIVE A02c Brep: GPC values " + Rs(pb.SectionProperties) + "; Straus7 values of the BXS (0.3.0) " + Rs(own.Take(11)) + "; relative: "
                            + string.Join(", ", Enumerable.Range(0, 11).Select(j => names[j] + " " + R((pb.SectionProperties[j] - own[j]) / Math.Max(1, Math.Abs(own[j]))))));
                        // Inertias, centroid and angle as 0.3.0; torsion, shear areas and the shear-centre offsets from GPC (listed difference). The
                        // section data of a BXS assigned alone give A = 0 (Straus7 keeps the area in the BXS); the GPC path writes the area of GPC.
                        Check(new[] { 1, 2, 8, 9, 10 }.All(j => Math.Abs(pb.SectionProperties[j] - own[j]) <= 1e-9 * Math.Max(1, Math.Abs(own[j]))),
                            "A02c: inertias, centroid and angle of the BXS equal to those of GPC (" + Rs(pb.SectionProperties) + " / " + Rs(own.Take(11)) + ")");
                    }
                }
                finally { F<OneInt>("St7CloseFile")(7); }
                // A01: the cutoff bar.
                var pc = byName.TryGetValue("PAR Cutoff", out var fc) ? fc : null;
                Check(pc != null && pc.Formulation == 4, "A01: the property is btCutoff (" + pc?.Formulation + ")");
                if (pc != null)
                {
                    var ints = new int[2]; var limits = new double[2]; C(F<IntsDoubles>("St7GetCutoffBarData")(1, pc.Number, ints, limits), "St7GetCutoffBarData");
                    Console.WriteLine($"PAR NATIVE A01 cutoff bar: type {ints[0]} keep mass {ints[1]}, tension {R(limits[0])}, compression {R(limits[1])} (0.3.0: 1 1 150000 250000)");
                    Check(ints[0] == 1 && ints[1] == 1 && limits[0] == 1.5e5 && limits[1] == 2.5e5, "A01: the cutoff data of 0.3.0");
                }
                // A04: the table of the shell.
                int plateProperty = api.ReadElement(Straus7Entity.Plate, 1).Property, tableId = 0, rows = 0;
                C(F<TableId>("St7GetPropertyTable")(1, 2007, plateProperty, ref tableId), "St7GetPropertyTable");
                C(F<TableRows>("St7GetNumTableTypeRows")(1, 4, tableId, ref rows), "St7GetNumTableTypeRows");
                var data = new double[2 * Math.Max(rows, 1)]; int read = 0; C(F<TableData>("St7GetTableTypeData")(1, 4, tableId, rows, ref read, data), "St7GetTableTypeData");
                var former = R4bInputs.Table.SelectMany(t => new[] { t.strain, t.stress }).ToArray();
                Console.WriteLine($"PAR NATIVE A04 shell table: id {tableId}, {read} rows {Rs(data)} (0.3.0 {Rs(former)})");
                Check(tableId > 0 && read == R4bInputs.Table.Length && data.SequenceEqual(former), "A04: the stress-strain table of 0.3.0 on the shell");
                // A04 (LIB): the Table Material material on a beam and on a shell: E = 0 and the table, as 0.3.0 wrote them.
                foreach (var (what, entity, type, number) in new[] { ("beam", Straus7Entity.Beam, 1011, byName["PAR Table beam"].Number), ("shell", Straus7Entity.Plate, 2007, api.ReadElement(Straus7Entity.Plate, 2).Property) })
                {
                    var tp = api.ReadProperty(entity, number); int id2 = 0, n2 = 0;
                    C(F<TableId>("St7GetPropertyTable")(1, type, number, ref id2), "St7GetPropertyTable");
                    C(F<TableRows>("St7GetNumTableTypeRows")(1, 4, id2, ref n2), "St7GetNumTableTypeRows");
                    var rows2 = new double[2 * Math.Max(n2, 1)]; int r2 = 0; C(F<TableData>("St7GetTableTypeData")(1, 4, id2, n2, ref r2, rows2), "St7GetTableTypeData");
                    Console.WriteLine($"PAR NATIVE A04 Table Material on a {what}: E {R(tp.Material[0])}, table id {id2}, {r2} rows {Rs(rows2)} (0.3.0: E 0, {Rs(former)})");
                    Check(tp.Material[0] == 0, $"A04 Table Material {what}: E = {R(tp.Material[0])}, 0.3.0 wrote 0");
                    Check(id2 > 0 && r2 == R4bInputs.Table.Length && rows2.SequenceEqual(former), $"A04 Table Material {what}: the table of 0.3.0");
                }
            }
            finally { api.CloseModel(); }
        }
        foreach (var f in failures) Console.WriteLine("  PAR NATIVE FAIL: " + f);
        Console.WriteLine($"PAR NATIVE: {standard.Length} standard sections on GPC data alone, a Brep section, a cutoff bar, a shell table; {failures.Count} failures");
        return failures.Count;
    }
}
#endif
