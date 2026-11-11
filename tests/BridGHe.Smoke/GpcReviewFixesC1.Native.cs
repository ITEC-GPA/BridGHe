#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using BridGHe.Common.Gpc;
using BridGHe.Common.Models;
using GPC.Converter.Straus7;

/// <summary>
/// C1 (user decision of 11/11, Table Material in 0.4.0): the material of the Table Material component with α and ρ written to Straus7 through
/// the GPC path (the FEM value of the component read by the reader and written by the Converter, as Straus R3 Analysis does, without the
/// solution) and read back from the .st7: on a beam and on a thin shell, E = 0 (as 0.3.0 wrote a table material), the stress-strain table of
/// the component (strain, stress pairs), α and ρ in the material data of the property at the places Straus7 gives them for a plain material
/// with the same α and ρ (written beside it). Under <c>--core-only --native</c> and <c>--c1 --native</c>.
/// </summary>
internal static class GpcReviewFixesC1Native
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TableId(int uid, int type, int prop, ref int id);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TableRows(int uid, int type, int id, ref int rows);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TableData(int uid, int type, int id, int max, ref int rows, double[] doubles);

    private static T F<T>(string name) where T : Delegate
    {
        var address = GetProcAddress(GetModuleHandle("St7api.dll"), name);
        if (address == IntPtr.Zero) throw new InvalidOperationException("C1 native: no St7API function " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private static void C(int code, string what) { if (code != 0) throw new InvalidOperationException($"C1 native: {what} returned {code}"); }
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string Rs(IEnumerable<double> v) => string.Join(" ", v.Select(R));

    internal static int Run()
    {
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }
        var goo = GpcReviewFixesC1.TableMaterialOutput(GpcReviewFixesC1.Alpha, GpcReviewFixesC1.Rho, out var errors);
        if (goo == null) { Console.WriteLine("  C1 NATIVE FAIL: Table Material with α and ρ gives no material: " + string.Join(" / ", errors)); return 1; }
        var table = (MaterialModel)goo.GetType().GetProperty("Value").GetValue(goo);
        // The model of the checks with the table material, and beside it a plain material with the same α and ρ (where Straus7 puts them).
        var input = GpcReviewFixesC1.TableModel(table);
        var plain = new MaterialModel("C1 plain") { Name = "C1 plain", Modulus = 210000, PoissonRatio = 0.3, Density = GpcReviewFixesC1.Rho, ThermalExpansion = GpcReviewFixesC1.Alpha };
        var reference = GpcReviewFixesC1.TableModel(plain);
        foreach (var e in reference.Elements) { if (e is BeamModel b) b.BeamProperty.Name = "C1 plain beam"; if (e is PlateModel p) p.PlateProperty.Name = "C1 plain shell"; }
        string folder = Path.GetFullPath(Path.Combine("artifacts", "native", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        var arrays = new Dictionary<string, (double[] Beam, double[] Shell, double[] BeamTable, double[] ShellTable)>();
        foreach (var (label, model) in new[] { ("table", input), ("plain", reference) })
        {
            string st7 = Path.Combine(folder, "c1-" + label + ".st7");
            var conversion = GpcModels.Convert(model, 1e-6);
            GpcPreflight.Require(conversion.Model);
            var plan = Straus7ModelWritePlan.Prepare(conversion.Model, null, null, conversion.Report.NativeSections);
            using (var writer = new Straus7NativeWriter(st7, folder)) { plan.Write(writer, conversion.Model); writer.Save(); }
            using var api = new Straus7NativeApi();
            api.OpenModelReadOnly(st7, folder);
            try
            {
                int beamProperty = api.ReadElement(Straus7Entity.Beam, 1).Property, plateProperty = api.ReadElement(Straus7Entity.Plate, 1).Property;
                double[] Table(int type, int number)
                {
                    int id = 0, rows = 0;
                    C(F<TableId>("St7GetPropertyTable")(1, type, number, ref id), "St7GetPropertyTable");
                    if (id == 0) return Array.Empty<double>();
                    C(F<TableRows>("St7GetNumTableTypeRows")(1, 4, id, ref rows), "St7GetNumTableTypeRows");
                    var data = new double[2 * Math.Max(rows, 1)]; int read = 0;
                    C(F<TableData>("St7GetTableTypeData")(1, 4, id, rows, ref read, data), "St7GetTableTypeData");
                    return data.Take(2 * read).ToArray();
                }
                arrays[label] = (api.ReadProperty(Straus7Entity.Beam, beamProperty).Material, api.ReadProperty(Straus7Entity.Plate, plateProperty).Material, Table(1011, beamProperty), Table(2007, plateProperty));
            }
            finally { api.CloseModel(); }
        }
        var expectedTable = GpcReviewFixesC1.Stresses.SelectMany((s, i) => new[] { GpcReviewFixesC1.Strains[i], s }).ToArray();
        var t = arrays["table"]; var p0 = arrays["plain"];
        foreach (var (what, material, reference0, rows) in new[] { ("beam", t.Beam, p0.Beam, t.BeamTable), ("shell", t.Shell, p0.Shell, t.ShellTable) })
        {
            int rho = Array.IndexOf(reference0, GpcReviewFixesC1.Rho), alpha = Array.IndexOf(reference0, GpcReviewFixesC1.Alpha);
            Console.WriteLine($"C1 NATIVE Table Material on a {what}: material data {Rs(material)} (plain material: {Rs(reference0)}); table {Rs(rows)} (component: {Rs(expectedTable)})");
            Check(material[0] == 0, $"Table Material {what}: E = {R(material[0])}, 0.3.0 wrote 0");
            Check(rho > 0 && alpha > 0 && material[rho] == GpcReviewFixesC1.Rho && material[alpha] == GpcReviewFixesC1.Alpha, $"Table Material {what}: ρ and α in the material data (indices {rho}, {alpha})");
            Check(rows.SequenceEqual(expectedTable), $"Table Material {what}: the stress-strain table of the component");
        }
        foreach (var f in failures) Console.WriteLine("  C1 NATIVE FAIL: " + f);
        Console.WriteLine($"C1 NATIVE: Table Material with α and ρ on a beam and a shell, read back from Straus7; {failures.Count} failures");
        return failures.Count;
    }
}
#endif
