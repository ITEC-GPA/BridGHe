#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Models;
using GPC.Converter.Straus7;
using Rhino.Geometry;

/// <summary>
/// PAR (R6.5 P1, 0.3.0 parity on the GPC path, user decision of 8/11 «Sì, come proponi»): the data 0.3.0 wrote to Straus7 that the GPC path
/// refused, written now through the GPC path (the FEM model read by the reader and written by the Converter, as Straus R3 Analysis does, without
/// the solution) and read back from the .st7:
/// <list type="bullet">
/// <item>A02/A03: T, C (single) and angle sections: the native geometry (bsTSection, bsLipChannel, bsLSection with the dimensions of 0.3.0) and
/// the section data, compared with the 0.3.0 call sequence replayed on a scratch model (St7SetBeamSectionGeometry, the section data of the
/// former value, St7SetBeamMaterialData, St7UpdateElementPropertyData, St7CalculateBeamSectionProperties with the shear): equal; the
/// builder of Beam Property gives the property of the reader (same signature, same native section);</item>
/// <item>A02c: a Brep section with a hole: a BXS (type 11) with the GPC values; the values Straus7 computes from that BXS alone (the 0.3.0
/// state: St7AssignBXS without section data) are printed and bounded (listed difference: torsion and shear areas from GPC);</item>
/// <item>A01: a cutoff bar: btCutoff with the cut-off type, keep mass and the two limits of 0.3.0 (St7SetCutoffBarData);</item>
/// <item>A04: a thin shell of a material with a stress-strain table: the table of 0.3.0 (strain, stress pairs) on ptPlateStressVsStrain; LIB: a
/// beam and a shell of a Table Material material (E = 0 and the table, with GPC.Model 5.51): E = 0 and the table in Straus7, as 0.3.0 wrote them;</item>
/// <item>A02b (LIB, with GPC Converter 2.21): the double C and double angle: the single native section with the mirror option of 0.3.0 (mtLeft,
/// btFalse, gaps A and B), compared with the 0.3.0 sequence on the scratch model (type, dimensions, mirror option, section data); the beam of a
/// double angle with the Straus7 reference angle of 0.3.0, 90 + angle + 90 (<c>RotateOf90Deg</c>), and its offset as given.</item>
/// </list>
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
        var steel = new MaterialModel("S355") { Name = "S355", Kind = MaterialModel.MaterialKind.Steel, Modulus = 210000, PoissonRatio = 0.3, Density = 7.85e-9, ThermalExpansion = 1.2e-5 };
        // A02/A03: the sections of the T, C and Angle components of 0.3.0 (inputs B, T1, D, T2) and the property Beam Property made of them.
        // LIB (A02b): the double C and double angle of the same components (Double Sections, Gap: the former mirror Left and gap A).
        var standard = new (string Name, SectionModel.SectionTypes Type, BridgeSectionKind Kind, double B, double T1, double D, double T2, int Native, bool Double, double Gap)[]
        {
            ("PAR T", SectionModel.SectionTypes.T, BridgeSectionKind.T, 200, 20, 300, 10, 8, false, 0),
            ("PAR C", SectionModel.SectionTypes.C, BridgeSectionKind.C, 100, 10, 300, 8, 5, false, 0),
            ("PAR L", SectionModel.SectionTypes.Angle, BridgeSectionKind.Angle, 150, 10, 100, 12, 9, false, 0),
            ("PAR 2C", SectionModel.SectionTypes.C, BridgeSectionKind.C, 100, 10, 300, 8, 5, true, 12),
            ("PAR 2L", SectionModel.SectionTypes.Angle, BridgeSectionKind.Angle, 150, 10, 100, 12, 9, true, 8),
        };
        // calculated: the section already holds its values (the former C component computed them before setting the mirror; the Angle component after).
        BeamPropertyModel Property(string name, SectionModel section, BeamPropertyType type = BeamPropertyType.Beam, MaterialModel material = null, bool calculated = false)
        {
            if (!calculated) section.CalculateSection();
            // Former Beam Property: a copy of the dimensions, computed again, with the values of the input section.
            var copy = new SectionModel { SectionType = section.SectionType, D = section.D, B = section.B, B1 = section.B1, B2 = section.B2, L = section.L, T = section.T, T1 = section.T1,
                T2 = section.T2, T3 = section.T3, GenericShapes = section.GenericShapes, Mirror = section.Mirror, MirrorGapA = section.MirrorGapA, MirrorGapB = section.MirrorGapB };
            copy.CalculateSection();
            copy.I11 = section.I11; copy.I22 = section.I22; copy.W11 = section.W11; copy.W22 = section.W22; copy.SectionArea = section.SectionArea; copy.J = section.J; copy.Centroid = section.Centroid;
            return new BeamPropertyModel(name) { Name = name, Material = material ?? steel, Section = copy, PropertyType = type };
        }
        var properties = standard.Select(s =>
        {
            var model = new SectionModel { SectionType = s.Type, B = s.B, T1 = s.T1, D = s.D, T2 = s.T2 };
            // The order of the former components: the C computes the single section and then sets the mirror; the Angle sets the mirror and then computes.
            bool calculated = s.Double && s.Type == SectionModel.SectionTypes.C;
            if (calculated) model.CalculateSection();
            if (s.Double) { model.Mirror = SectionModel.MirrorTypes.Left; model.MirrorGapA = s.Gap; }
            return Property(s.Name, model, calculated: calculated);
        }).ToList();
        // A02c: a Brep section with a hole (the shapes of the Brep component).
        var fill = new GPC.Geometry.Polygon2d(); fill.Add(0, 0); fill.Add(200, 0); fill.Add(200, 400); fill.Add(0, 400);
        var hole = new GPC.Geometry.Polygon2d(); hole.Add(50, 50); hole.Add(150, 50); hole.Add(150, 350); hole.Add(50, 350);
        var shape = new GPC.Geometry.Shape2d(fill, new[] { hole });
        var brep = Property("PAR Brep", new SectionModel { SectionType = SectionModel.SectionTypes.GenericShapes, GenericShapes = new BridGHe.Common.Geometry.Shapes(new[] { shape }) });
        properties.Add(brep);
        // A01: a cutoff bar on an I section: ductile, keep mass, the two limits.
        var cutoff = Property("PAR Cutoff", new SectionModel { SectionType = SectionModel.SectionTypes.I, D = 400, B1 = 200, B2 = 200, T1 = 15, T2 = 15, T3 = 10 }, BeamPropertyType.CutoffBar);
        cutoff.MaxTension = 1.5e5; cutoff.MaxCompression = 2.5e5; cutoff.CutoffType = CutoffType.Ductile; cutoff.KeepMass = true;
        properties.Add(cutoff);

        // The builders give the property of the reader (signature, native section, cutoff law).
        var units = GpcBeamDrafts.DraftUnits;
        var gpcSteel = GpcMaterials.FromFemm(steel);
        for (int k = 0; k < standard.Length; k++)
        {
            var s = standard[k];
            var reader = GpcModels.BeamProperty(properties[k], units);
            var built = GpcPropertyBuilders.Beam(s.Name, 5, GpcSectionBuilders.Standard(s.Kind, s.D, b: s.B, t1: s.T1, t2: s.T2,
                mirror: s.Double ? 2 : BridGHe.Common.Gpc.Legacy.LegacySectionRecipe.NoMirror, gapA: s.Gap), gpcSteel, "Steel");
            Check(reader.Data.Signature == built.Data.Signature && reader.Data.Native?.Fingerprint() == built.Data.Native?.Fingerprint() && reader.Data.Native != null,
                $"A02 {s.Name}: builder and reader differ (signatures {reader.Data.Signature} / {built.Data.Signature})");
        }
        {
            var reader = GpcModels.BeamProperty(cutoff, units);
            var built = GpcPropertyBuilders.Beam("PAR Cutoff", 3, GpcSectionBuilders.Standard(BridgeSectionKind.I, 400, b1: 200, b2: 200, t1: 15, t2: 15, t3: 10), gpcSteel, "Steel", (1.5e5, 2.5e5, 1, true));
            var law = ((GPC.Model.ElementProperties.ElasticBeamProperty)reader.Property).CutoffLaw;
            Check(law != null && law.Failure == GPC.Model.ElementProperties.BeamCutoffFailure.Ductile && law.KeepMass && law.TensionLimit == 1.5e5 && law.CompressionLimit == 2.5e5,
                "A01: the reader gives the cutoff law of the former property");
            Check(reader.Data.Signature == built.Data.Signature, "A01: builder and reader differ");
        }
        // A04: a thin shell of a material with a stress-strain table.
        var table = new[] { (stress: 0.0, strain: 0.0), (stress: 235.0, strain: 235.0 / 210000), (stress: 360.0, strain: 0.15) };
        var tableSteel = new MaterialModel("S235 table") { Name = "S235 table", Modulus = 210000, PoissonRatio = 0.3, Density = 7.85e-9, StressStrainTable = table };
        var shell = new PlatePropertyModel("1") { Name = "PAR Shell", Material = tableSteel, PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 20, BendingThickness = 20 };
        // A04 (LIB): a material of the Table Material component: no E (0), the table; on a beam (Beam type: the table on ptBeamStressVsStrain) and on a shell.
        var tableOnly = new MaterialModel("PAR Table") { Name = "PAR Table", Modulus = 0, PoissonRatio = 0.3, StressStrainTable = table };
        var tableShell = new PlatePropertyModel("2") { Name = "PAR Table shell", Material = tableOnly, PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 20, BendingThickness = 20 };
        properties.Add(Property("PAR Table beam", new SectionModel { SectionType = SectionModel.SectionTypes.SolidRectangle, B = 100, D = 200 }, material: tableOnly));

        // The model: one cantilever per beam property, a restrained shell.
        var lc = new LoadCaseModel("P", "P");
        var input = new ModelModel(ModelModel.ModelUnits.Nmm); input.LoadCases.Add(lc);
        var elements = new List<ElementModel>();
        for (int k = 0; k < properties.Count; k++)
        {
            var a = new NodeModel("A" + k, new Point3d(0, 2000 * k, 0)); a.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
            var b = new NodeModel("B" + k, new Point3d(3000, 2000 * k, 0)); b.Loads.Add(new NodeForceLoadModel(lc, new Vector3d(1000, 0, -1000)));
            var beamModel = new BeamModel((k + 1).ToString(CultureInfo.InvariantCulture), a.Position, b.Position, properties[k]);
            if (properties[k].Name == "PAR 2L") { beamModel.AngleDeg = 30; beamModel.Offset = new Vector2d(5, 7); } // A02b: the axes of a double angle
            elements.Add(a); elements.Add(b); elements.Add(beamModel);
        }
        var corners = new[] { new Point3d(0, -3000, 0), new Point3d(1000, -3000, 0), new Point3d(1000, -2000, 0), new Point3d(0, -2000, 0) };
        for (int k = 0; k < 4; k++) { var n = new NodeModel("S" + k, corners[k]); n.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true)); elements.Add(n); }
        elements.Add(new PlateModel("1", corners, shell));
        var corners2 = new[] { new Point3d(0, -6000, 0), new Point3d(1000, -6000, 0), new Point3d(1000, -5000, 0), new Point3d(0, -5000, 0) };
        for (int k = 0; k < 4; k++) { var n = new NodeModel("T" + k, corners2[k]); n.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true)); elements.Add(n); }
        elements.Add(new PlateModel("2", corners2, tableShell));
        input.AddElements(elements.ToArray());
        string folder = Path.GetFullPath(Path.Combine("artifacts", "native", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        string st7 = Path.Combine(folder, "parity.st7");
        // The model the GPC path writes (the conversion of Straus R3 Analysis, the Converter writer), without a solution: what Straus7 receives.
        var conversion = GpcModels.Convert(input, 1e-6);
        GpcPreflight.Require(conversion.Model);
        var plan = Straus7ModelWritePlan.Prepare(conversion.Model, null, null, conversion.Report.NativeSections);
        using (var writer = new Straus7NativeWriter(st7, folder)) { plan.Write(writer, conversion.Model); writer.Save(); }

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
                        var s = standard[k]; var bp = properties[k]; int id = k + 1;
                        C(F<NewProperty>("St7NewBeamProperty")(7, id, 6, s.Name), "St7NewBeamProperty");
                        var dims = s.Type == SectionModel.SectionTypes.Angle ? new[] { bp.B, bp.D, 0, bp.T1, bp.T2, 0 } : new[] { bp.B, bp.D, bp.L, bp.T1, bp.T2, bp.T3 };
                        C(F<IntDoubles>("St7SetBeamSectionGeometry")(7, id, s.Native, dims), "St7SetBeamSectionGeometry");
                        var formerData = new[] { bp.SectionArea, bp.I11, bp.I22, bp.J, bp.ShearL1, bp.ShearL2, bp.ShearA1, bp.ShearA2, bp.Centroid.X, bp.Centroid.Y, bp.AngleX1Rad };
                        int formerCode = F<IntsDoubles>("St7SetBeamSectionPropertyData")(7, id, new[] { 0 }, formerData);
                        if (s.Double) Console.WriteLine($"PAR NATIVE A02b {s.Name}: the section data of the former value, written before the mirror: {Rs(formerData)}; St7SetBeamSectionPropertyData returned {formerCode}");
                        // A finding of LIB, not a condition: the values the former Angle component computed on the mirrored shapes may be refused by Straus7
                        // here (the former exporter would stop at this call); the calculation below overwrites whatever this call wrote.
                        if (!s.Double) C(formerCode, "St7SetBeamSectionPropertyData");
                        if (s.Double) C(F<SetMirror>("St7SetBeamMirrorOption")(7, id, 3, 0, new[] { bp.MirrorGapA, bp.MirrorGapB }), "St7SetBeamMirrorOption"); // mtLeft, btFalse, as 0.3.0
                        var m = new double[9]; m[0] = steel.Modulus; m[2] = steel.PoissonRatio; m[3] = steel.Density; m[4] = steel.ThermalExpansion;
                        C(F<Doubles>("St7SetBeamMaterialData")(7, id, m), "St7SetBeamMaterialData");
                        C(F<ThreeInts>("St7UpdateElementPropertyData")(7, 1, id), "St7UpdateElementPropertyData");
                        C(F<Calculate>("St7CalculateBeamSectionProperties")(7, id, 1), "St7CalculateBeamSectionProperties");
                        var expected = new double[20]; C(F<IntsDoubles>("St7GetBeamSectionPropertyData")(7, id, new int[4], expected), "St7GetBeamSectionPropertyData");
                        var p = byName.TryGetValue(s.Name, out var found) ? found : null;
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
                var former = table.SelectMany(t => new[] { t.strain, t.stress }).ToArray();
                Console.WriteLine($"PAR NATIVE A04 shell table: id {tableId}, {read} rows {Rs(data)} (0.3.0 {Rs(former)})");
                Check(tableId > 0 && read == table.Length && data.SequenceEqual(former), "A04: the stress-strain table of 0.3.0 on the shell");
                // A04 (LIB): the Table Material material on a beam and on a shell: E = 0 and the table, as 0.3.0 wrote them.
                foreach (var (what, entity, type, number) in new[] { ("beam", Straus7Entity.Beam, 1011, byName["PAR Table beam"].Number), ("shell", Straus7Entity.Plate, 2007, api.ReadElement(Straus7Entity.Plate, 2).Property) })
                {
                    var tp = api.ReadProperty(entity, number); int id2 = 0, n2 = 0;
                    C(F<TableId>("St7GetPropertyTable")(1, type, number, ref id2), "St7GetPropertyTable");
                    C(F<TableRows>("St7GetNumTableTypeRows")(1, 4, id2, ref n2), "St7GetNumTableTypeRows");
                    var rows2 = new double[2 * Math.Max(n2, 1)]; int r2 = 0; C(F<TableData>("St7GetTableTypeData")(1, 4, id2, n2, ref r2, rows2), "St7GetTableTypeData");
                    Console.WriteLine($"PAR NATIVE A04 Table Material on a {what}: E {R(tp.Material[0])}, table id {id2}, {r2} rows {Rs(rows2)} (0.3.0: E 0, {Rs(former)})");
                    Check(tp.Material[0] == 0, $"A04 Table Material {what}: E = {R(tp.Material[0])}, 0.3.0 wrote 0");
                    Check(id2 > 0 && r2 == table.Length && rows2.SequenceEqual(former), $"A04 Table Material {what}: the table of 0.3.0");
                }
            }
            finally { api.CloseModel(); }
        }
        foreach (var f in failures) Console.WriteLine("  PAR NATIVE FAIL: " + f);
        Console.WriteLine($"PAR NATIVE: {standard.Length} standard sections, a Brep section, a cutoff bar, a shell table; {failures.Count} failures");
        return failures.Count;
    }
}
#endif
