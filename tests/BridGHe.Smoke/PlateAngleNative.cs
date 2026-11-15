using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// P2X (user decision of 8/11, «Mappalo come nella 0.3.0»): the native parity of the plate local axis angle with BridGHe 0.3.0. The model of
/// the frozen reference (<c>Fixtures/plate-angle-030</c>: Plate Results of BridGHe 0.3.0, PROBE/BridGHe-base 95ab7c3, written once by the
/// P2X host that builds this same definition with the 0.3.0 plug-in; never regenerated): a 3000 x 1000 mm steel shell strip in the XY plane
/// (three quadrangles and two triangles, fixed at x = 0), plates turned by the Angle input of Plate (30, 15, -60, 0) and one by Align Plate
/// Axes (axis 1 on (1, 1, 0)), case "Pressure" a local normal pressure, case "Prestress" a plate pre-tension (px 5, py -2 MPa, local axes);
/// Straus R3 Analysis, Decompose Model, Plate Results. Every output of Plate Results (local forces, moments, shears, principal values and
/// angles) must equal the reference within the criterion of the 0.3.0 comparisons (passo 1: max |difference| / max |value| of the quantity in
/// the case at most 1e-9; angles in degrees modulo 180 over 180), the same rows, load cases and nodes. Native (core-only --native).
/// <para>Also solved <c>warped</c>: the first quadrangle, turned by 30°, with a corner 0.005 mm out of the plane (within the planarity of GPC),
/// which must solve (its layer axes carry the Straus7 normal, <c>GpcPlateBuilders.StrausAxes</c>; with the normal of the former plate the writer
/// refused it, PlateMaterialAxesNormalMismatch, where 0.3.0 solved it). Its comparison with 0.3.0 is in passo 74 (the warping couples
/// membrane and bending at the level of rounding, whose principal angles are not comparable by this criterion).</para>
/// <para>P2Y (coordinator decision on passo 74): Force Von Mises is the value of 0.3.0 again, Straus7's combined value with the transverse
/// shears (<c>GpcResultRows.Plate</c>), and is compared everywhere, the bent plates of "Pressure" included. <c>combination</c>: the same model
/// with the combination "Both" = 1.0 Pressure + 0.5 Prestress also solved and shown (a Straus7 combination case in 0.3.0, summed by the GPC
/// rows), against its own frozen reference (<see cref="CombinationReference"/>, Plate Results of 0.3.0 written once by the P2Y host).</para>
/// </summary>
internal static class PlateAngleNative
{
    internal static readonly string Reference = Path.GetFullPath(Path.Combine("tests", "BridGHe.Smoke", "Fixtures", "plate-angle-030", "plate-results.tsv"));
    internal static readonly string CombinationReference = Path.GetFullPath(Path.Combine("tests", "BridGHe.Smoke", "Fixtures", "plate-angle-030", "plate-results-combination.tsv"));

    private static IGH_Param In(GH_Component c, string name) => c.Params.Input.Single(p => p.Name == name);
    private static IGH_Param Out(GH_Component c, string name) => c.Params.Output.Single(p => p.Name == name);
    private static void Wire(GH_Component to, string input, params IGH_Param[] from) { var p = In(to, input); foreach (var f in from) p.AddSource(f); }

    private static void Set(GH_Component c, string input, params IGH_Goo[] values)
    {
        var param = In(c, input); param.RemoveAllSources();
        var persistent = param.GetType().GetProperty("PersistentData").GetValue(param);
        persistent.GetType().GetMethod("Clear").Invoke(persistent, null);
        var append = persistent.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(GH_Path));
        foreach (var v in values) append.Invoke(persistent, new object[] { v, new GH_Path(0) });
    }

    private static Mesh Face(params Point3d[] p)
    {
        var mesh = new Mesh(); foreach (var q in p) mesh.Vertices.Add(q);
        if (p.Length == 3) mesh.Faces.AddFace(0, 1, 2); else mesh.Faces.AddFace(0, 1, 2, 3);
        mesh.Normals.ComputeNormals(); return mesh;
    }

    private static string Text(IGH_Goo goo) => goo switch
    {
        null => "null",
        GH_Number n => n.Value.ToString("R", CultureInfo.InvariantCulture),
        GH_Integer i => i.Value.ToString(CultureInfo.InvariantCulture),
        GH_Vector v => string.Join(" ", new[] { v.Value.X, v.Value.Y, v.Value.Z }.Select(x => x.ToString("R", CultureInfo.InvariantCulture))),
        GH_String s => s.Value,
        _ => goo.GetType().Name + " " + goo,
    };

    /// <summary>Builds and solves the definition (the analysis writes <paramref name="st7"/>); the rows of Plate Results (output, branch, index,
    /// value) and the error messages of the document.</summary>
    internal static (Dictionary<(string, string, int), string> Rows, string[] Errors) Solve(string st7, bool warped = false, bool combination = false)
    {
        using var doc = new GH_Document();
        GH_Component Add(string type) => BridgeSegmentExcelTests.Add(doc, type);
        var steel = Add("MaterialSteelComponent"); Set(steel, "Name", new GH_String("S355")); Set(steel, "Grade", new GH_Integer(2));
        var property = Add("PlatePropertyComponent");
        Set(property, "Name", new GH_String("Shell 20")); Set(property, "Type", new GH_Integer(0));
        Set(property, "Membrane Thickness", new GH_Number(20)); Set(property, "Bending Thickness", new GH_Number(20));
        Wire(property, "Material", Out(steel, "Material"));
        Point3d P(double x, double y) => new Point3d(x, y, 0);
        // warped: the first quadrangle with one corner 0.005 mm out of the plane (3.5e-6 of its diagonal: within the planarity of GPC, 1e-5).
        var meshes = new[] { Face(P(0, 0), P(1000, 0), warped ? new Point3d(1000, 1000, 0.005) : P(1000, 1000), P(0, 1000)), Face(P(2000, 0), P(3000, 0), P(3000, 1000)), Face(P(2000, 0), P(3000, 1000), P(2000, 1000)), Face(P(1000, 0), P(2000, 0), P(2000, 500), P(1000, 500)) };
        var plateA = Add("PlateComponent");
        Set(plateA, "Mesh", meshes.Select(m => (IGH_Goo)new GH_Mesh(m)).ToArray()); Wire(plateA, "Property", Out(property, "Property"));
        Set(plateA, "Angle", new[] { 30.0, 15.0, -60.0, 0.0 }.Select(a => (IGH_Goo)new GH_Number(a)).ToArray());
        var plateB = Add("PlateComponent");
        Set(plateB, "Mesh", new GH_Mesh(Face(P(1000, 500), P(2000, 500), P(2000, 1000), P(1000, 1000)))); Wire(plateB, "Property", Out(property, "Property"));
        var align = Add("AlignPlateAxesComponent");
        Wire(align, "Plate", Out(plateB, "Plate")); Set(align, "Axis", new GH_Integer(0)); Set(align, "Direction", new GH_Vector(new Vector3d(1, 1, 0)));
        var pressureCase = Add("LoadCaseComponent"); Set(pressureCase, "Name", new GH_String("Pressure"));
        var prestressCase = Add("LoadCaseComponent"); Set(prestressCase, "Name", new GH_String("Prestress"));
        var pressure = Add("PlatePressureComponent");
        Wire(pressure, "Plate", Out(plateA, "Plate"), Out(align, "Plate")); Wire(pressure, "LoadCase", Out(pressureCase, "LoadCase")); Set(pressure, "Value", new GH_Number(0.01));
        var prestress = Add("PlatePreStressComponent");
        Wire(prestress, "Plate", Out(pressure, "Plate")); Wire(prestress, "LoadCase", Out(prestressCase, "LoadCase"));
        Set(prestress, "Px", new GH_Number(5)); Set(prestress, "Py", new GH_Number(-2)); Set(prestress, "Pz", new GH_Number(0));
        var nodes = Add("NodeComponent"); Set(nodes, "Point", new GH_Point(P(0, 0)), new GH_Point(P(0, 1000)));
        var restraints = Add("NodeRestraintsComponent"); Wire(restraints, "Node", Out(nodes, "Node"));
        Set(restraints, "Transational", new GH_Boolean(true), new GH_Boolean(true), new GH_Boolean(true));
        Set(restraints, "Rotation", new GH_Boolean(true), new GH_Boolean(true), new GH_Boolean(true));
        var build = Add("BuildModelComponent");
        Wire(build, "Elements", Out(restraints, "Node"), Out(prestress, "Plate")); In(build, "Elements").DataMapping = GH_DataMapping.Flatten;
        Set(build, "Name", new GH_String("Plate angle"));
        if (combination)
        {
            var both = Add("LoadCombinationComponent"); Set(both, "Name", new GH_String("Both"));
            Wire(both, "LoadCases", Out(pressureCase, "LoadCase"), Out(prestressCase, "LoadCase")); Set(both, "Coefficients", new GH_Number(1.0), new GH_Number(0.5));
            Wire(build, "Load Combinations", Out(both, "LoadCombination"));
        }
        var analysis = Add("StrausR3AnalysisComponent");
        Wire(analysis, "Model", Out(build, "Model")); Set(analysis, "Solver", new GH_Integer(0));
        if (combination) Set(analysis, "Analysis Results", new GH_String("Pressure"), new GH_String("Prestress"), new GH_String("Both"));
        else Set(analysis, "Analysis Results", new GH_String("Pressure"), new GH_String("Prestress"));
        Set(analysis, "Output Path", new GH_String(st7)); Set(analysis, "Keep Running", new GH_Boolean(true));
        var parts = Add("DecomposeModelComponent"); Wire(parts, "Model", Out(analysis, "Model"));
        var results = Add("PlateResultsComponent"); Wire(results, "Plate", Out(parts, "Plates"));
        BridgeSegmentExcelTests.Solve(doc, true);
        var errors = doc.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => o.Name + ": " + m)).ToArray();
        var rows = new Dictionary<(string, string, int), string>();
        foreach (var p in results.Params.Output)
            for (int b = 0; b < p.VolatileData.PathCount; b++)
            {
                var branch = p.VolatileData.get_Branch(b);
                for (int i = 0; i < branch.Count; i++) rows[(p.Name, p.VolatileData.get_Path(b).ToString(), i)] = Text(branch[i] as IGH_Goo);
            }
        return (rows, errors);
    }

    /// <summary>The rows in the format of the reference (output, branch, index, value), next to the .st7 of the run (diagnosis).</summary>
    internal static void Write(string file, Dictionary<(string, string, int), string> rows) =>
        File.WriteAllLines(file, new[] { "output\tpath\tindex\tvalue" }.Concat(rows.Select(r => $"{r.Key.Item1}\t{r.Key.Item2}\t{r.Key.Item3}\t{r.Value}")), new System.Text.UTF8Encoding(false));

    internal static Dictionary<(string, string, int), string> Read(string file) =>
        File.ReadLines(file).Skip(1).Select(l => l.Split('\t', 4)).Where(f => f[0] != "plate").ToDictionary(f => (f[0], f[1], int.Parse(f[2], CultureInfo.InvariantCulture)), f => f[3]);

    /// <summary>The worst relative difference of the comparison (passo 1 criterion), or an exception on a different row, case or node.
    /// Every value is compared (P2Y: Force Von Mises too, the 0.3.0 value with the transverse shears); <paramref name="bent"/> counts the
    /// Force Von Mises values where the transverse shears are not zero (where the in-plane value of R6.4 differed from 0.3.0).</summary>
    internal static double Compare(Dictionary<(string, string, int), string> reference, Dictionary<(string, string, int), string> actual, out int values, out int bent)
    {
        if (!reference.Keys.OrderBy(k => k).SequenceEqual(actual.Keys.OrderBy(k => k))) throw new Exception($"PLATE ANGLE NATIVE: rows {actual.Count} where {reference.Count} (0.3.0)");
        var labels = new HashSet<string> { "Load cases", "Nodes", "Steps" }; var angles = new HashSet<string> { "Force Angle", "Moment Angle", "Shear Force Angle" };
        var scale = new Dictionary<(string, string), double>(); var diff = new Dictionary<(string, string), double>(); values = 0; bent = 0;
        foreach (var (k, v) in reference)
        {
            if (labels.Contains(k.Item1)) { if (actual[k] != v) throw new Exception($"PLATE ANGLE NATIVE: {k}: {actual[k]} where {v} (0.3.0)"); continue; }
            if (k.Item1 == "Force Von Mises" && reference.TryGetValue(("Shear Force", k.Item2, k.Item3), out var shear) && shear.Split(' ').Any(t => double.Parse(t, CultureInfo.InvariantCulture) != 0)) bent++;
            string @case = reference.TryGetValue(("Load cases", k.Item2, k.Item3), out var c) ? c : "?";
            var x = v.Split(' ').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray(); var y = actual[k].Split(' ').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            for (int j = 0; j < x.Length; j++)
            {
                var q = (k.Item1 + "[" + j + "]", @case); bool angle = angles.Contains(k.Item1);
                values++;
                // NaN in both (Shear Force Max and Angle: not given, in 0.3.0 too) is the same value; NaN in one only is a difference.
                if (double.IsNaN(x[j]) || double.IsNaN(y[j])) { if (!(double.IsNaN(x[j]) && double.IsNaN(y[j]))) { scale[q] = 1; diff[q] = double.PositiveInfinity; } continue; }
                double d = angle ? Math.Abs(((x[j] - y[j] + 90) % 180 + 180) % 180 - 90) : Math.Abs(x[j] - y[j]);
                scale[q] = Math.Max(scale.GetValueOrDefault(q), angle ? 180 : Math.Max(Math.Abs(x[j]), Math.Abs(y[j]))); diff[q] = Math.Max(diff.GetValueOrDefault(q), d);
            }
        }
        return scale.Keys.Select(q => scale[q] > 0 ? diff[q] / scale[q] : diff[q] == 0 ? 0 : double.PositiveInfinity).DefaultIfEmpty(0).Max();
    }
}
