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
using BridGHe.Common.MovingLoads;
using GPC.Converter.Straus7;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.5 P1 lane 3e: the analysis components, Export and the data A05, A06, A09 on the GPC path, with Straus7. R6.6 lane R4b: on GPC data alone, every
/// input built by the product builders and Build Model on GPC (<see cref="GpcNativeExamples"/>, <see cref="R4bInputs"/>) and given to the components
/// by its GPC input alone (<c>GH_Model.FromGpc</c>), no FEM model:
/// <list type="bullet">
/// <item>the examples 01–10 through Straus R3 Analysis / Moving Static Analysis: no error, no FEM value in the output; the analytic values of each
/// example (the checks of <c>--examples</c>) on the analysed model of the component; the analysed model, its results and its warnings equal to the
/// GPC-only analysis of the same input by the library (<see cref="BridGHe.Common.Straus.StrausAnalysis"/>); the Cases output the generated cases of
/// the analysed model, one per position of the path of example 04; Node, Beam and Plate Results on the output, every value equal to the GPC reader
/// rows bit for bit; the refusals with their error and no output;</item>
/// <item>Straus7 R3 Export (no solve) of example 03: the file read back with the nodes, plate, connectivity and material of the definition;</item>
/// <item>A05: the plate non-structural mass outside N-mm and A06: the moving heat source outside N-mm, written by the GPC path and read back from the
/// .st7, compared with the 0.3.0 calls (St7SetPlateNSMass5ID, St7SetLoadPathTemplateHeatSourceData with the values as given, in a file with the units
/// of 0.3.0) replayed on a scratch model and converted by Straus7 itself (St7ConvertUnits) to the N-mm file of the GPC path;</item>
/// <item>A09: the output of Bridge Phase Model with properties of the same name: each beam with the property 0.3.0 wrote (the first of each name in
/// beam order), the values written explicitly from the definition.</item>
/// </list>
/// </summary>
internal static class GpcOnlyAnalysisNative
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int NewFile(int uid, string path, string scratch);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int OneInt(int uid);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Units(int uid, int[] units);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int NodeXyz(int uid, int node, double[] xyz);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int NewPlateProperty(int uid, int prop, int type, int material, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Connection(int uid, int entity, int number, int prop, int[] connection);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int FourIntsDoubles(int uid, int plate, int loadCase, int id, [In, Out] double[] values);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int NewTemplate(int uid, int id, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int TwoInts(int uid, int a);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ThreeInts(int uid, int a, int b);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int HeatData(int uid, int template, int vehicle, int position, int[] flags, [In, Out] double[] values);

    private static T F<T>(string name) where T : Delegate
    {
        var address = GetProcAddress(GetModuleHandle("St7api.dll"), name);
        if (address == IntPtr.Zero) throw new InvalidOperationException("L3E native: no St7API function " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private static void C(int code, string what) { if (code != 0) throw new InvalidOperationException($"L3E native: {what} returned {code}"); }
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string Rs(IEnumerable<double> v) => string.Join(" ", v.Select(R));
    private static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b));
    private static bool Near(double[] a, double[] b) => a.Length == b.Length && a.Zip(b).All(p => Near(p.First, p.Second));
    private static object Prop(object o, string name) => o.GetType().GetProperty(name).GetValue(o);

    private static Assembly plugin;
    private static Type Goo => plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
    /// <summary>The GH model given by its GPC input alone (no FEM value), as the producers give it on the path of P2.</summary>
    private static IGH_Goo FromGpc(GpcModels.Conversion input)
    {
        var goo = (IGH_Goo)Goo.GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { input });
        if (Prop(goo, "Value") != null) throw new InvalidOperationException("L3E native: the GH model has a FEM value");
        return goo;
    }
    /// <summary>The GH model of an analysis by the library: the GPC input and the analysed model attached (as the analysis components give it).</summary>
    private static IGH_Goo Analysed(GpcModels.Conversion input, GPC.Model.Models.Model analysed)
    {
        var goo = FromGpc(input); Goo.GetMethod("AttachGpc").Invoke(goo, new object[] { analysed }); return goo;
    }

    /// <summary>The checks (0: none failed); one line per item on the console.</summary>
    internal static int Run()
    {
        plugin = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var failures = new List<string>();
        void Check(bool ok, string what) { if (!ok) failures.Add(what); }
        string folder = Path.Combine(Path.GetTempPath(), "bridghe-l3e-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            Examples(folder, Check);
            ExportFile(folder, Check);
            foreach (var units in new[] { DefinitionUnits.Nmm, DefinitionUnits.Knm, DefinitionUnits.SI, DefinitionUnits.IPS })
                MassAndHeat(units, folder, Check);
            Shadowed(folder, Check);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        foreach (var f in failures) Console.WriteLine("  L3E NATIVE FAIL: " + f);
        Console.WriteLine($"L3E NATIVE: examples 01-10 GPC-only, Export, A05, A06, A09 on GPC data alone; {failures.Count} failures");
        return failures.Count;
    }

    /// <summary>A component solved in a document of its own: the first output, the second output as text, the messages.</summary>
    private static (object Model, string[] Cases, string Errors, string Warnings) Solve(string type, Action<GH_Component> configure, bool press = true)
    {
        var c = (GH_Component)Activator.CreateInstance(plugin.GetType(type, true));
        configure(c);
        using var doc = new GH_Document { Enabled = true }; GH_Document.EnableSolutions = true;
        c.CreateAttributes(); doc.AddObject(c, false);
        if (press) c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true);
        c.ExpireSolution(false); doc.NewSolution(false, GH_SolutionMode.Silent);
        var model = c.Params.Output[0].VolatileData.AllData(true).SingleOrDefault();
        var cases = c.Params.Output.Count > 1 ? c.Params.Output[1].VolatileData.AllData(true).Select(g => g.ToString()).ToArray() : Array.Empty<string>();
        string errors = string.Join(" | ", c.RuntimeMessages(GH_RuntimeMessageLevel.Error)), warnings = string.Join(" | ", c.RuntimeMessages(GH_RuntimeMessageLevel.Warning));
        doc.RemoveObject(c, false);
        return (model, cases, errors, warnings);
    }

    private static void Set(GH_Component c, string input, params IGH_Goo[] values)
    {
        var param = c.Params.Input.Single(p => p.Name == input);
        foreach (var value in values)
            switch (param)
            {
                case Param_GenericObject g: g.PersistentData.Append(value); break;
                case Param_String s: s.PersistentData.Append((GH_String)value); break;
                case Param_Integer i: i.PersistentData.ClearData(); i.PersistentData.Append((GH_Integer)value); break;
                case Param_Boolean b: b.PersistentData.ClearData(); b.PersistentData.Append((GH_Boolean)value); break;
                default: throw new ArgumentException(param.GetType().Name);
            }
    }

    private static void Examples(string folder, Action<bool, string> check)
    {
        const string Analysis = "BridGHe.Grasshopper.Components.Analysis.StrausR3AnalysisComponent", Moving = "BridGHe.Grasshopper.Components.Analysis.MovingStaticAnalysisComponent";
        int replayed = 0;
        foreach (var example in GpcNativeExamples.Create())
        {
            string Target(string name) { string path = System.IO.Path.Combine(folder, example.Id + "-" + name); Directory.CreateDirectory(path); return System.IO.Path.Combine(path, "model.st7"); }
            var goo = FromGpc(example.Input);
            var gpc = example.Moving
                ? Solve(Moving, c => { Set(c, "Model", goo); Set(c, "Output path", new GH_String(Target("gpc"))); Set(c, "Run", new GH_Boolean(false)); })
                : Solve(Analysis, c =>
                {
                    Set(c, "Model", goo); Set(c, "Solver", new GH_Integer(example.Solver)); Set(c, "Output Path", new GH_String(Target("gpc")));
                    Set(c, "Analysis Results", example.Cases.Select(n => (IGH_Goo)new GH_String(n)).ToArray()); Set(c, "Keep Running", new GH_Boolean(false));
                });
            check(gpc.Errors.Length == 0, example.Id + ": errors '" + gpc.Errors + "'");
            if (gpc.Model == null) { check(false, example.Id + ": no output"); continue; }
            check(Prop(gpc.Model, "Value") == null, example.Id + ": the GPC-only output has a FEM value");
            var analysed = (GPC.Model.Models.Model)Prop(gpc.Model, "GpcResults");
            if (analysed == null) { check(false, example.Id + ": the output has no analysed GPC model"); continue; }
            // The analytic values of the example on the analysed model of the component (before: equal to the analysis of the FEM-backed model).
            var record = new NativeExamples.Recorder();
            try { example.Verify(new GpcNativeExamples.Results(analysed), record); }
            catch (Exception ex) { check(false, example.Id + ": analytic checks: " + ex.GetBaseException().Message); }
            foreach (var m in record.Measurements.Where(m => !m.Passed)) check(false, $"{example.Id}: {m.Name} = {R(m.Actual)}, analytic {R(m.Expected)}");
            // The same input analysed by the library: the component gives its analysed model, results and warnings.
            var direct = BridGHe.Common.Straus.StrausAnalysis.Run(example.Input, Target("library"), example.Solver, example.Cases, example.Moving, out var directWarnings);
            string difference = GpcOnlyReplay.ResultsDifference(Analysed(example.Input, direct), gpc.Model);
            check(difference == null, example.Id + ": the analysed model of the component differs from the analysis of the library: " + difference);
            // The component reports the warnings of the analysis (Grasshopper keeps one message per text): the distinct texts, in order.
            string componentWarnings = string.Join(" | ", gpc.Warnings.Split(" | ", StringSplitOptions.RemoveEmptyEntries).Distinct()), libraryWarnings = string.Join(" | ", directWarnings.Distinct());
            check(componentWarnings == libraryWarnings, example.Id + ": warnings of the component '" + gpc.Warnings + "', of the library '" + libraryWarnings + "'");
            // The Cases output: the generated cases of the analysed model, one per position of the moving path (none without paths).
            var generated = GpcResultCatalog.Of(analysed).GeneratedMovingCases().Select(c => c.Name).ToArray();
            int positions = example.Moving ? example.Inputs.Paths.Sum(p => p.Divisions) : 0;
            check(generated.Length == positions && gpc.Cases.SequenceEqual(generated, StringComparer.Ordinal), example.Id + ": generated cases " + gpc.Cases.Length + " (analysed model " + generated.Length + ", positions " + positions + ")");
            // Node, Beam and Plate Results on the GPC-only output: every value equal to the GPC reader rows bit for bit.
            try { if (GpcNativeExamples.ComponentsCheck(example.Input, analysed, "L3E " + example.Id) > 0) replayed++; else check(false, example.Id + ": no Results values"); }
            catch (Exception ex) { check(false, example.Id + ": " + ex.GetBaseException().Message); }
            Console.WriteLine($"L3E NATIVE {example.Id}: solver {example.Solver}, GPC-only analysis: {record.Measurements.Count} analytic checks, equal to the analysis of the library ({gpc.Cases.Length} generated cases, {analysed.Datasets.Count} datasets)");
        }
        Console.WriteLine($"L3E NATIVE results components on the real results: {replayed} GPC-only outputs, every value equal to the GPC reader rows");
        // The refusals: the error and no output.
        var only = FromGpc(GpcNativeExamples.Create().First(e => e.Id == "02-simply-supported").Input);
        var r = Solve(Analysis, c => { Set(c, "Model", only); Set(c, "Solver", new GH_Integer(0)); Set(c, "Output Path", new GH_String(System.IO.Path.Combine(folder, "refusal.st7"))); Set(c, "Keep Running", new GH_Boolean(false)); });
        check(r.Errors.Contains("Selezionare almeno un risultato") && r.Model == null, "no result selected: error and no output (" + r.Errors + ", " + r.Model + ")");
        // Example 04 with a heat source in its vehicle (built with the builders of the example, the path with the heat source appended).
        var h = GpcNativeExamples.Beam("04-moving-load-heat", 12, 6000, true);
        var path = GpcNativeExamples.Path04(6000);
        path.Template.Vehicles[0].Loads = path.Template.Vehicles[0].Loads.Append(new MovingHeatSource { X = 0, Y = 0, SizeX = 100, SizeY = 100, Heat = 5 }).ToArray();
        h.Paths.Add(path);
        var refused = Solve(Moving, c => { Set(c, "Model", FromGpc(h.Build("heat").Input)); Set(c, "Output path", new GH_String(System.IO.Path.Combine(folder, "heat.st7"))); Set(c, "Run", new GH_Boolean(false)); });
        check(refused.Errors.Contains("Le sorgenti termiche") && refused.Model == null, "Moving Static Analysis with a heat source: error and no output (" + refused.Errors + ")");
    }

    private static void ExportFile(string folder, Action<bool, string> check)
    {
        var goo = FromGpc(GpcNativeExamples.Create().First(e => e.Id == "03-plate-patch").Input);
        string file = Path.Combine(folder, "export-gpc.st7");
        var r = Solve("BridGHe.Grasshopper.Components.Export.StrausR3ExportComponent", c => { Set(c, "Model", goo); Set(c, "Output Path", new GH_String(file)); });
        check(r.Errors.Length == 0 && File.Exists(file), "Export: " + r.Errors);
        if (!File.Exists(file)) return;
        using var api = new Straus7NativeApi();
        api.OpenModelReadOnly(file, folder);
        try
        {
            var plates = Enumerable.Range(1, api.Count(Straus7Entity.Plate)).Select(k => api.ReadElement(Straus7Entity.Plate, k)).ToArray();
            var material = plates.Select(p => p.Property).Distinct().SelectMany(p => api.ReadProperty(Straus7Entity.Plate, p).Material).ToArray();
            string text = api.Count(Straus7Entity.Node) + " nodes " + plates.Length + " plates " + string.Join(";", plates.Select(p => p.Property + ":" + string.Join(",", p.Nodes))) + " " + Rs(material);
            Console.WriteLine("L3E NATIVE Export (no solve) with a GPC-only model: " + text);
            // The definition of example 03 (before: equal to the file of the FEM-backed model): 4 nodes, one plate of property 1 on the nodes 1–4 in order;
            // its material S355 E 210000, ν 0.3 and nothing else (no density, expansion, damping or thermal data).
            check(api.Count(Straus7Entity.Node) == 4 && plates.Length == 1 && plates[0].Property == 1 && plates[0].Nodes.SequenceEqual(new[] { 1, 2, 3, 4 }), "Export: topology " + text);
            check(material.Length > 2 && material[0] == 210000 && material[1] == 0.3 && material.Skip(2).All(v => v == 0), "Export: the plate material " + Rs(material) + ", the definition 210000 0.3 and zeros");
        }
        finally { api.CloseModel(); }
    }

    private static int[] NativeUnits(DefinitionUnits units) => units switch
    {
        // length, force, stress, mass, temperature, energy: the units of the files of 0.3.0 (StrausR3Helper at 95ab7c3)
        DefinitionUnits.Knm => new[] { 0, 1, 1, 1, 0, 0 },
        DefinitionUnits.SI => new[] { 0, 0, 0, 0, 0, 0 },
        DefinitionUnits.Nmm => new[] { 2, 0, 2, 1, 0, 0 },
        DefinitionUnits.IPS => new[] { 4, 4, 4, 3, 1, 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(units)),
    };

    /// <summary>A05 and A06 in the units of a definition: the two data written by the GPC path and read back, against the 0.3.0 calls.</summary>
    private static void MassAndHeat(DefinitionUnits units, string folder, Action<bool, string> check)
    {
        var goo = FromGpc(R4bInputs.MassAndHeat(units));
        string file = Path.Combine(folder, "a05-" + units + "-GPC-only.st7");
        var r = Solve("BridGHe.Grasshopper.Components.Export.StrausR3ExportComponent", c => { Set(c, "Model", goo); Set(c, "Output Path", new GH_String(file)); });
        if (r.Errors.Length != 0 || !File.Exists(file)) { Console.WriteLine($"L3E NATIVE A05/A06 {units}: ERROR {r.Errors}"); check(false, $"A05/A06 {units}: {r.Errors}"); return; }
        double[] mass, heat; string cases;
        using (var api = new Straus7NativeApi())
        {
            api.OpenModelReadOnly(file, folder);
            try
            {
                // The non-structural mass is in «Dead»: its case read by name (the order of the cases is that of Attach Moving Paths on GPC).
                var names = Enumerable.Range(1, api.LoadCaseCount).Select(api.ReadLoadCase).ToArray(); cases = string.Join(",", names);
                int dead = Array.IndexOf(names, GpcCaseBuilders.Dead) + 1; if (dead == 0) dead = 1; // by name; the first case as before when no case has the name
                mass = api.ReadPlateNonStructuralMass(1, dead, 1).Take(5).ToArray(); heat = api.ReadMovingHeatSource(1, 1, 1);
            }
            finally { api.CloseModel(); }
        }
        var (formerMass, formerHeat) = Former(units, R4bInputs.Mass, R4bInputs.Heat, file, folder);
        Console.WriteLine($"L3E NATIVE A05 {units}: GPC path (N, mm, t; cases {cases}) {Rs(mass)}; 0.3.0 calls converted by Straus7 {Rs(formerMass)}");
        Console.WriteLine($"L3E NATIVE A06 {units}: GPC path (mm, J/s) {Rs(heat)}; 0.3.0 calls converted by Straus7 {Rs(formerHeat)}");
        check(Near(mass, formerMass), $"A05 {units}: {Rs(mass)} / 0.3.0 {Rs(formerMass)}");
        check(Near(heat, formerHeat), $"A06 {units}: {Rs(heat)} / 0.3.0 {Rs(formerHeat)}");
    }

    /// <summary>The calls of 0.3.0 on a scratch model with the units of 0.3.0, converted by Straus7 itself to the units of the GPC path.</summary>
    private static (double[] Mass, double[] Heat) Former(DefinitionUnits units, double[] mass, double[] heat, string session, string folder)
    {
        using var api = new Straus7NativeApi(); // the API session
        api.OpenModelReadOnly(session, folder);
        try
        {
            C(F<NewFile>("St7NewFile")(7, Path.Combine(folder, "former-" + units + ".st7"), folder), "St7NewFile");
            try
            {
                C(F<Units>("St7SetUnits")(7, NativeUnits(units)), "St7SetUnits");
                var xyz = new[] { new double[] { 0, 0, 0 }, new double[] { 1, 0, 0 }, new double[] { 1, 1, 0 }, new double[] { 0, 1, 0 } };
                for (int n = 0; n < 4; n++) C(F<NodeXyz>("St7SetNodeXYZ")(7, n + 1, xyz[n]), "St7SetNodeXYZ");
                C(F<NewPlateProperty>("St7NewPlateProperty")(7, 1, 4, 1, "Former"), "St7NewPlateProperty");
                C(F<Connection>("St7SetElementConnection")(7, 2, 1, 1, new[] { 4, 1, 2, 3, 4 }), "St7SetElementConnection");
                C(F<FourIntsDoubles>("St7SetPlateNSMass5ID")(7, 1, 1, 1, mass.Concat(new double[] { 0 }).ToArray()), "St7SetPlateNSMass5ID");
                C(F<NewTemplate>("St7NewLoadPathTemplate")(7, 1, "Former"), "St7NewLoadPathTemplate");
                C(F<TwoInts>("St7AddLoadPathTemplateVehicle")(7, 1), "St7AddLoadPathTemplateVehicle");
                C(F<ThreeInts>("St7AddLoadPathTemplateHeatSource")(7, 1, 1), "St7AddLoadPathTemplateHeatSource");
                C(F<HeatData>("St7SetLoadPathTemplateHeatSourceData")(7, 1, 1, 1, new int[1], (double[])heat.Clone()), "St7SetLoadPathTemplateHeatSourceData");
                C(F<Units>("St7ConvertUnits")(7, NativeUnits(DefinitionUnits.Nmm)), "St7ConvertUnits");
                var m = new double[6]; C(F<FourIntsDoubles>("St7GetPlateNSMass5ID")(7, 1, 1, 1, m), "St7GetPlateNSMass5ID");
                var h = new double[5]; C(F<HeatData>("St7GetLoadPathTemplateHeatSourceData")(7, 1, 1, 1, new int[1], h), "St7GetLoadPathTemplateHeatSourceData");
                return (m.Take(5).ToArray(), h);
            }
            finally { F<OneInt>("St7CloseFile")(7); }
        }
        finally { api.CloseModel(); }
    }

    /// <summary>A09: the Phase Model of beams with properties of the same name, read back: each beam with the first property of its name in the beam
    /// order of the output (what the exporter of 0.3.0 registered), on the GPC-only path (<see cref="R4bInputs.Shadowed"/>).</summary>
    private static void Shadowed(string folder, Action<bool, string> check)
    {
        var replacement = R4bInputs.ShadowedProperty("Stiff", 420000);
        var input = R4bInputs.Shadowed();
        var output = GpcModelTransforms.PhaseModel(input, "A09", new[] { "B1" }, replacement, false, GpcIdentity.Derive(GpcIdentity.NameSpace, "test-a09-native"), replacement);
        var warnings = new List<string>();
        string gpcFile = Path.Combine(folder, "a09-gpc.st7");
        FemmAnalysisSeam.Export(null, output, gpcFile, R4bInputs.Tolerance, warnings);
        double[] gpc; string names;
        using (var api = new Straus7NativeApi())
        {
            api.OpenModelReadOnly(gpcFile, folder);
            try
            {
                var p = Enumerable.Range(1, api.Count(Straus7Entity.Beam)).Select(b => api.ReadProperty(Straus7Entity.Beam, api.ReadElement(Straus7Entity.Beam, b).Property)).ToArray();
                names = string.Join(",", p.Select(x => x.Name)); gpc = p.Select(x => x.Material[0]).ToArray();
            }
            finally { api.CloseModel(); }
        }
        // The exporter of 0.3.0 (StrausR3Helper at 95ab7c3) registered one Straus7 property per name, the first in beam order. The output beams B1
        // «Stiff» 420000, B2 and B3 «Same» 315000 (the first «Same», 210000, was the property of B1 only): 420000, 315000, 315000 (before: computed on
        // the FEM output of Bridge Phase Model).
        var expected = new[] { 420000.0, 315000, 315000 };
        Console.WriteLine($"L3E NATIVE A09: GPC-only {names} E {Rs(gpc)}; 0.3.0 rule {Rs(expected)}");
        check(names == "Stiff,Same,Same" && gpc.SequenceEqual(expected), $"A09: GPC-only {names} E {Rs(gpc)}, 0.3.0 Stiff,Same,Same {Rs(expected)}");
    }
}
