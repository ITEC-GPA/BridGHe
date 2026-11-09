#if GPC_PILOT
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
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using GPC.Converter.Straus7;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// R6.5 P1 lane 3e: the analysis components, Export and the data A05, A06, A09 on the GPC path, with Straus7:
/// <list type="bullet">
/// <item>the examples 01–10 through Straus R3 Analysis / Moving Static Analysis with a model given by its GPC input alone: the analysed model, its
/// results and the generated cases equal to the FEM-backed path; Node, Beam and Plate Results replayed on the real results;</item>
/// <item>Straus7 R3 Export (no solve) with a GPC-only model: the file read back equals the file of the FEM-backed path;</item>
/// <item>A05: the plate non-structural mass outside N-mm and A06: the moving heat source outside N-mm, written by the GPC path and read back from the
/// .st7, compared with the 0.3.0 calls (St7SetPlateNSMass5ID, St7SetLoadPathTemplateHeatSourceData with the values as given, in a file with the units
/// of 0.3.0) replayed on a scratch model and converted by Straus7 itself (St7ConvertUnits) to the N-mm file of the GPC path;</item>
/// <item>A09: the output of Bridge Phase Model with properties of the same name: each beam with the property 0.3.0 wrote (the first of each name in
/// beam order).</item>
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
    private static IGH_Goo FromModel(ModelModel m) => (IGH_Goo)Goo.GetMethod("FromModel", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { m, null });

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
            foreach (var units in new[] { ModelModel.ModelUnits.Nmm, ModelModel.ModelUnits.Knm, ModelModel.ModelUnits.SI, ModelModel.ModelUnits.IPS })
                MassAndHeat(units, folder, Check);
            Shadowed(folder, Check);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        foreach (var f in failures) Console.WriteLine("  L3E NATIVE FAIL: " + f);
        Console.WriteLine($"L3E NATIVE: examples 01-10 GPC-only, Export, A05, A06, A09; {failures.Count} failures");
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
        foreach (var example in NativeExamples.Create())
        {
            example.Model.Name = example.Title;
            (object Model, string[] Cases, string Errors, string Warnings) Run(IGH_Goo model, string file)
            {
                string path = Path.Combine(folder, file); Directory.CreateDirectory(path); path = Path.Combine(path, "model.st7");
                if (example.Moving) return Solve(Moving, c => { Set(c, "Model", model); Set(c, "Output path", new GH_String(path)); Set(c, "Run", new GH_Boolean(false)); });
                return Solve(Analysis, c =>
                {
                    Set(c, "Model", model); Set(c, "Solver", new GH_Integer(example.Solver)); Set(c, "Output Path", new GH_String(path));
                    Set(c, "Analysis Results", example.Cases.Select(n => (IGH_Goo)new GH_String(n)).ToArray()); Set(c, "Keep Running", new GH_Boolean(false));
                });
            }
            var goo = FromModel(ModelArchive.Clone(example.Model));
            var fem = Run(goo, example.Id + "-fem"); var gpc = Run(GpcOnlyReplay.ModelOnly(goo), example.Id + "-gpc");
            check(fem.Errors.Length == 0 && gpc.Errors.Length == 0, example.Id + ": errors FEM '" + fem.Errors + "', GPC-only '" + gpc.Errors + "'");
            if (fem.Model == null || gpc.Model == null) { check(false, example.Id + ": no output"); continue; }
            check(fem.Warnings == gpc.Warnings, example.Id + ": warnings FEM '" + fem.Warnings + "', GPC-only '" + gpc.Warnings + "'");
            check(Prop(gpc.Model, "Value") == null, example.Id + ": the GPC-only output has a FEM value");
            string difference = GpcOnlyReplay.ResultsDifference(fem.Model, gpc.Model);
            check(difference == null, example.Id + ": analysed models differ: " + difference);
            check(fem.Cases.SequenceEqual(gpc.Cases, StringComparer.Ordinal), example.Id + ": generated cases " + fem.Cases.Length + " -> " + gpc.Cases.Length);
            GpcOnlyReplay.ResultsNative((IGH_Goo)fem.Model, example.Id, check, ref replayed);
            Console.WriteLine($"L3E NATIVE {example.Id}: solver {example.Solver}, GPC-only analysis equal to the FEM-backed one ({fem.Cases.Length} generated cases, {((GPC.Model.Models.Model)Prop(gpc.Model, "Gpc")).Datasets.Count} datasets)");
        }
        Console.WriteLine($"L3E NATIVE results components on the real results: {replayed} instances replayed");
        // The refusals: the same error and no output.
        var m = NativeExamples.Create().First(e => e.Id == "02-simply-supported").Model; m.Name = "refusal";
        var only = GpcOnlyReplay.ModelOnly(FromModel(m));
        var r = Solve(Analysis, c => { Set(c, "Model", only); Set(c, "Solver", new GH_Integer(0)); Set(c, "Output Path", new GH_String(Path.Combine(folder, "refusal.st7"))); Set(c, "Keep Running", new GH_Boolean(false)); });
        check(r.Errors.Contains("Selezionare almeno un risultato") && r.Model == null, "no result selected: error and no output (" + r.Errors + ", " + r.Model + ")");
        var h = NativeExamples.Create().Single(e => e.Id == "04-moving-load").Model; h.Name = "heat";
        h.MovingPaths[0].Template.Vehicles[0].Loads = h.MovingPaths[0].Template.Vehicles[0].Loads.Append(new MovingHeatSource { X = 0, Y = 0, SizeX = 100, SizeY = 100, Heat = 5 }).ToArray();
        var refused = Solve(Moving, c => { Set(c, "Model", GpcOnlyReplay.ModelOnly(FromModel(h))); Set(c, "Output path", new GH_String(Path.Combine(folder, "heat.st7"))); Set(c, "Run", new GH_Boolean(false)); });
        check(refused.Errors.Contains("Le sorgenti termiche") && refused.Model == null, "Moving Static Analysis with a heat source: error and no output (" + refused.Errors + ")");
    }

    private static void ExportFile(string folder, Action<bool, string> check)
    {
        var m = NativeExamples.Create().First(e => e.Id == "03-plate-patch").Model; m.Name = "export";
        var goo = FromModel(m);
        string Write(IGH_Goo model, string name)
        {
            string file = Path.Combine(folder, name + ".st7");
            var r = Solve("BridGHe.Grasshopper.Components.Export.StrausR3ExportComponent", c => { Set(c, "Model", model); Set(c, "Output Path", new GH_String(file)); });
            check(r.Errors.Length == 0 && File.Exists(file), "Export " + name + ": " + r.Errors);
            return file;
        }
        string Text(string file)
        {
            using var api = new Straus7NativeApi();
            api.OpenModelReadOnly(file, folder);
            try
            {
                var plates = Enumerable.Range(1, api.Count(Straus7Entity.Plate)).Select(k => api.ReadElement(Straus7Entity.Plate, k)).ToArray();
                return api.Count(Straus7Entity.Node) + " nodes " + plates.Length + " plates " + string.Join(";", plates.Select(p => p.Property + ":" + string.Join(",", p.Nodes)))
                    + " " + Rs(plates.Select(p => p.Property).Distinct().SelectMany(p => api.ReadProperty(Straus7Entity.Plate, p).Material));
            }
            finally { api.CloseModel(); }
        }
        string fem = Text(Write(goo, "export-fem")), gpc = Text(Write(GpcOnlyReplay.ModelOnly(goo), "export-gpc"));
        check(fem == gpc, "Export: the file read back differs:\n  " + fem + "\n  " + gpc);
        Console.WriteLine("L3E NATIVE Export (no solve) with a GPC-only model: " + gpc);
    }

    private static int[] NativeUnits(ModelModel.ModelUnits units) => units switch
    {
        // length, force, stress, mass, temperature, energy: the units of the files of 0.3.0 (StrausR3Helper at 95ab7c3)
        ModelModel.ModelUnits.Knm => new[] { 0, 1, 1, 1, 0, 0 },
        ModelModel.ModelUnits.SI => new[] { 0, 0, 0, 0, 0, 0 },
        ModelModel.ModelUnits.Nmm => new[] { 2, 0, 2, 1, 0, 0 },
        ModelModel.ModelUnits.IPS => new[] { 4, 4, 4, 3, 1, 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(units)),
    };

    /// <summary>A05 and A06 in the units of a definition: the two data written by the two paths and read back, against the 0.3.0 calls.</summary>
    private static void MassAndHeat(ModelModel.ModelUnits units, string folder, Action<bool, string> check)
    {
        var m = ImportDump.PlateData(units); m.Name = "a05-a06-" + units;
        double[] mass = { 0.2, 1.1, 0.01, -0.02, 0.03 }, heat = { 0.1, 0.2, 0.3, 0.4, 5 };
        var dead = new LoadCaseModel(LoadCaseModel.Dead);
        m.Plates[0].Loads.Add(new PlateNSMassModel(dead, new PlateNSMassModel.LoadData { Mass = mass[0], DynFactor = mass[1], Offset = new Vector3d(mass[2], mass[3], mass[4]) }));
        m.MovingPaths.Add(new MovingPath
        {
            LoadCaseName = "Traffic", Divisions = 5, Target = 2, Coordinates = new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
            Template = new MovingTemplate { Name = "Heat", Vehicles = new[] { new MovingVehicle { Name = "H", Loads = new MovingObject[] { new MovingPointForce { Force = new double[] { 0, 0, -1 } }, new MovingHeatSource { X = heat[0], Y = heat[1], SizeX = heat[2], SizeY = heat[3], Heat = heat[4] } } } } },
        });
        var goo = FromModel(m);
        var read = new Dictionary<string, (double[] Mass, double[] Heat)>();
        foreach (var (name, model) in new[] { ("FEM-backed", goo), ("GPC-only", GpcOnlyReplay.ModelOnly(goo)) })
        {
            string file = Path.Combine(folder, "a05-" + units + "-" + name + ".st7");
            var r = Solve("BridGHe.Grasshopper.Components.Export.StrausR3ExportComponent", c => { Set(c, "Model", model); Set(c, "Output Path", new GH_String(file)); });
            if (r.Errors.Length != 0 || !File.Exists(file)) { check(false, $"A05/A06 {units} {name}: {r.Errors}"); return; }
            using var api = new Straus7NativeApi();
            api.OpenModelReadOnly(file, folder);
            try { read[name] = (api.ReadPlateNonStructuralMass(1, 1, 1).Take(5).ToArray(), api.ReadMovingHeatSource(1, 1, 1)); }
            finally { api.CloseModel(); }
        }
        var (formerMass, formerHeat) = Former(units, mass, heat, folder);
        var fem = read["FEM-backed"]; var gpc = read["GPC-only"];
        Console.WriteLine($"L3E NATIVE A05 {units}: GPC path (N, mm, t) {Rs(gpc.Mass)}; 0.3.0 calls converted by Straus7 {Rs(formerMass)}");
        Console.WriteLine($"L3E NATIVE A06 {units}: GPC path (mm, J/s) {Rs(gpc.Heat)}; 0.3.0 calls converted by Straus7 {Rs(formerHeat)}");
        check(fem.Mass.SequenceEqual(gpc.Mass) && fem.Heat.SequenceEqual(gpc.Heat), $"A05/A06 {units}: the two paths differ");
        check(Near(gpc.Mass, formerMass), $"A05 {units}: {Rs(gpc.Mass)} / 0.3.0 {Rs(formerMass)}");
        check(Near(gpc.Heat, formerHeat), $"A06 {units}: {Rs(gpc.Heat)} / 0.3.0 {Rs(formerHeat)}");
    }

    /// <summary>The calls of 0.3.0 on a scratch model with the units of 0.3.0, converted by Straus7 itself to the units of the GPC path.</summary>
    private static (double[] Mass, double[] Heat) Former(ModelModel.ModelUnits units, double[] mass, double[] heat, string folder)
    {
        using var api = new Straus7NativeApi(); // the API session
        api.OpenModelReadOnly(Path.Combine(folder, "a05-" + units + "-GPC-only.st7"), folder);
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
                C(F<Units>("St7ConvertUnits")(7, NativeUnits(ModelModel.ModelUnits.Nmm)), "St7ConvertUnits");
                var m = new double[6]; C(F<FourIntsDoubles>("St7GetPlateNSMass5ID")(7, 1, 1, 1, m), "St7GetPlateNSMass5ID");
                var h = new double[5]; C(F<HeatData>("St7GetLoadPathTemplateHeatSourceData")(7, 1, 1, 1, new int[1], h), "St7GetLoadPathTemplateHeatSourceData");
                return (m.Take(5).ToArray(), h);
            }
            finally { F<OneInt>("St7CloseFile")(7); }
        }
        finally { api.CloseModel(); }
    }

    /// <summary>A09: the Phase Model of beams with properties of the same name, read back: each beam with the first property of its name in the beam
    /// order of the output (what the exporter of 0.3.0 registered), on the GPC-only path and on the FEM output.</summary>
    private static void Shadowed(string folder, Action<bool, string> check)
    {
        BeamPropertyModel Property(string name, double modulus) => new BeamPropertyModel(name) { Name = name, PropertyType = BeamPropertyType.Beam,
            Material = new MaterialModel("S355") { Name = "S355", Modulus = modulus, PoissonRatio = 0.3 },
            Section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 60000, I11 = 450000000, I22 = 200000000, J = 100000000, ShearA1 = 0, ShearA2 = 0 } };
        var points = Enumerable.Range(0, 4).Select(i => new Point3d(5000 * i, 0, 0)).ToArray();
        var fem = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = "a09" };
        var nodes = points.Select((p, i) => new NodeModel("N" + i, p)).ToArray();
        nodes[0].Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
        var first = Property("Same", 210000); var second = Property("Same", 315000);
        fem.AddElements(nodes);
        fem.AddElements(new ElementModel[] { new BeamModel("B1", points[0], points[1], first), new BeamModel("B2", points[1], points[2], second), new BeamModel("B3", points[2], points[3], second) });
        var replacement = Property("Stiff", 420000);
        const double tolerance = 1e-6;
        var names = new[] { fem.Beams[0].BeamId };
        var input = GpcInputComparison.ConvertWithDrafts(fem, tolerance);
        var output = GpcModelTransforms.PhaseModel(input, "A09", names, GpcModels.BeamProperty(replacement, input.Report.Units), false,
            GpcIdentity.Derive(GpcIdentity.NameSpace, "test-a09-native"), GpcModels.BeamProperty(replacement, GpcBeamDrafts.DraftUnits));
        var femOutput = BridGHe.Common.Diagnostics.BridgePhaseModel.Create(fem, "A09", names, replacement, false);
        var warnings = new List<string>();
        string gpcFile = Path.Combine(folder, "a09-gpc.st7"), femFile = Path.Combine(folder, "a09-fem.st7");
        FemmAnalysisSeam.Export(null, output, gpcFile, tolerance, warnings);
        FemmAnalysisSeam.Export(femOutput, null, femFile, tolerance, warnings);
        double[] Read(string file, out string names2)
        {
            using var api = new Straus7NativeApi();
            api.OpenModelReadOnly(file, folder);
            try
            {
                var p = Enumerable.Range(1, api.Count(Straus7Entity.Beam)).Select(b => api.ReadProperty(Straus7Entity.Beam, api.ReadElement(Straus7Entity.Beam, b).Property)).ToArray();
                names2 = string.Join(",", p.Select(x => x.Name)); return p.Select(x => x.Material[0]).ToArray();
            }
            finally { api.CloseModel(); }
        }
        var gpc = Read(gpcFile, out string gpcNames); var former = Read(femFile, out string femNames);
        // The exporter of 0.3.0 (StrausR3Helper at 95ab7c3) registered one Straus7 property per name, the first in beam order.
        var expected = femOutput.Beams.Select(b => femOutput.Beams.First(o => o.BeamProperty.Name == b.BeamProperty.Name).BeamProperty.Material.Modulus).ToArray();
        Console.WriteLine($"L3E NATIVE A09: GPC-only {gpcNames} E {Rs(gpc)}; FEM output {femNames} E {Rs(former)}; 0.3.0 rule {Rs(expected)}");
        check(gpc.SequenceEqual(expected), $"A09: GPC-only E {Rs(gpc)}, 0.3.0 {Rs(expected)}");
        check(former.SequenceEqual(expected), $"A09: FEM output E {Rs(former)}, 0.3.0 {Rs(expected)}");
    }
}
#endif
