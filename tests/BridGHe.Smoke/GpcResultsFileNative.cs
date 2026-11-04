#if GPC_PILOT
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// --gpc-results-native (F6 C4, native): the analysed GPC model of Straus R3 Analysis and Moving Static Analysis goes to the BGHR2 file
/// next to the .st7 and comes back after saving and reopening the component (archive round trip), only when the file is the one saved:
/// a changed file and a results file of master (BGHR1) give a warning and no GPC results; the FEM results stay in the document as before.
/// The ideas of master's regression analysis-results-file (d7e4092), on the GPC payload.
/// </summary>
internal static class GpcResultsFileNative
{
    private static Assembly assembly;
    private static void Require(bool condition, string what) { if (!condition) throw new Exception("GPC RESULTS NATIVE FAIL: " + what); Console.WriteLine("PASS: " + what); }

    internal static int Run()
    {
        assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        string folder = Path.Combine(Path.GetTempPath(), "bridghe-gpc-results-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            Component("BridGHe.Grasshopper.Components.Analysis.StrausR3AnalysisComponent", Cantilever(), Path.Combine(folder, "cantilever.st7"), static c =>
            { Set(c, "Solver", new GH_Integer(0)); Set(c, "Analysis Results", new GH_String("P")); Set(c, "Keep Running", new GH_Boolean(false)); }, "Output Path");
            var moving = ModelArchive.Clone(NativeExamples.Create().Single(e => e.Id == "04-moving-load").Model);
            Component("BridGHe.Grasshopper.Components.Analysis.MovingStaticAnalysisComponent", moving, Path.Combine(folder, "moving.st7"), static c =>
            { Set(c, "Run", new GH_Boolean(false)); }, "Output path");
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        Console.WriteLine("GPC RESULTS NATIVE PASS");
        return 0;
    }

    private static void Component(string typeName, ModelModel input, string output, Action<GH_Component> configure, string pathInput)
    {
        string name = typeName.Split('.').Last();
        var type = assembly.GetType(typeName, true);
        var goo = (IGH_Goo)assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true).GetMethod("FromModel", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { input, null });
        GH_Component Make(GH_Archive archive = null)
        {
            var c = (GH_Component)Activator.CreateInstance(type);
            if (archive != null && !archive.ExtractObject(c, "component")) throw new Exception("archive extract");
            Set(c, "Model", goo); Set(c, pathInput, new GH_String(output)); configure(c);
            return c;
        }
        (object Model, string Warnings) Solve(GH_Component c, bool run)
        {
            using var doc = new GH_Document { Enabled = true }; GH_Document.EnableSolutions = true;
            c.CreateAttributes(); doc.AddObject(c, false);
            if (run) type.GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true);
            c.ExpireSolution(false); doc.NewSolution(false, GH_SolutionMode.Silent);
            var errors = c.RuntimeMessages(GH_RuntimeMessageLevel.Error); if (errors.Count > 0) throw new Exception(name + ": " + string.Join("; ", errors));
            var model = c.Params.Output[0].VolatileData.AllData(true).SingleOrDefault();
            string warnings = string.Join(" | ", c.RuntimeMessages(GH_RuntimeMessageLevel.Warning)); // before the removal, which clears them
            doc.RemoveObject(c, false);
            return (model, warnings);
        }
        bool Analysed(object m) => m != null && (bool)m.GetType().GetProperty("HasAnalysedGpc").GetValue(m);
        GPC.Model.Models.Model Gpc(object m) => (GPC.Model.Models.Model)m.GetType().GetProperty("Gpc").GetValue(m);
        GH_Archive Save(GH_Component c) { var a = new GH_Archive(); if (!a.AppendObject(c, "component")) throw new Exception("archive append"); return Reopen(a); }
        GH_Archive Reopen(GH_Archive a) { var copy = new GH_Archive(); if (!copy.Deserialize_Binary(a.Serialize_Binary())) throw new Exception("archive reopen"); return copy; }

        var first = Make(); var (model, _) = Solve(first, true);
        string file = GpcResultsFile.PathFor(output);
        Require(Analysed(model) && Gpc(model).Datasets.Count > 0 && File.Exists(file), name + ": analysed GPC model attached and written next to the .st7");
        var archive = Save(first);
        var (reopened, warnings) = Solve(Make(archive), false);
        Require(Analysed(reopened) && warnings.Length == 0, name + ": after reopening the GPC results come back from the file" + (Analysed(reopened) && warnings.Length == 0 ? "" : " [analysed " + Analysed(reopened) + ", warnings: " + warnings + "]"));
        Require(GPC.Model.Models.ModelRevisions.AnalysisFingerprint(Gpc(reopened)) == GPC.Model.Models.ModelRevisions.AnalysisFingerprint(Gpc(model))
            && Gpc(reopened).Datasets.Keys.SequenceEqual(Gpc(model).Datasets.Keys), name + ": the same analysed model (fingerprint, datasets)");
        // C4: after reopening, the Results components read the GPC model of the file and show the FEM values of the document bit for bit.
        Require(ResultsComponentsCheck.Verify(reopened, name + " reopened").Values > 0, name + ": the Results components read the reopened GPC results");
        var bytes = File.ReadAllBytes(file); var changed = (byte[])bytes.Clone(); changed[changed.Length - 1] ^= 0x5A; File.WriteAllBytes(file, changed);
        (reopened, warnings) = Solve(Make(archive), false);
        Require(!Analysed(reopened) && warnings.Contains("cambiato") && warnings.Contains("Run"), name + ": a changed file gives a warning and asks for Run" + " [analysed " + Analysed(reopened) + ", warnings: " + warnings + "]");
        File.WriteAllBytes(file, System.Text.Encoding.ASCII.GetBytes("BGHR1").Concat(new byte[128]).ToArray());
        (reopened, warnings) = Solve(Make(archive), false);
        Require(!Analysed(reopened) && warnings.Contains("versione precedente"), name + ": a results file of master gives the previous-version message" + " [analysed " + Analysed(reopened) + ", warnings: " + warnings + "]");
        File.WriteAllBytes(file, bytes);
        (reopened, warnings) = Solve(Make(archive), false);
        Require(Analysed(reopened), name + ": the saved file restored is accepted again" + " [analysed " + Analysed(reopened) + ", warnings: " + warnings + "]");
        File.Delete(file);
        (reopened, warnings) = Solve(Make(archive), false);
        Require(!Analysed(reopened) && warnings.Contains("non trovato"), name + ": a missing file gives a warning" + " [analysed " + Analysed(reopened) + ", warnings: " + warnings + "]");
    }

    private static void Set(GH_Component c, string input, IGH_Goo value)
    {
        var param = c.Params.Input.Single(p => p.Name == input);
        param.RemoveAllSources();
        switch (param)
        {
            case Param_GenericObject g: g.PersistentData.Clear(); g.PersistentData.Append(value); break;
            case Param_String s: s.PersistentData.Clear(); s.PersistentData.Append((GH_String)value); break;
            case Param_Integer i: i.PersistentData.Clear(); i.PersistentData.Append((GH_Integer)value); break;
            case Param_Boolean b: b.PersistentData.Clear(); b.PersistentData.Append((GH_Boolean)value); break;
            default: throw new ArgumentException(param.GetType().Name);
        }
        param.ExpireSolution(false);
    }

    private static ModelModel Cantilever()
    {
        var loadCase = new LoadCaseModel("P", "P");
        var material = new MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3 };
        var section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 60000, I11 = 450000000, I22 = 200000000, J = 100000000, ShearA1 = 0, ShearA2 = 0 };
        var property = new BeamPropertyModel("P") { Name = "P", Material = material, Section = section, PropertyType = BeamPropertyType.Beam };
        var first = new NodeModel("N1", Point3d.Origin); first.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
        var end = new NodeModel("N2", new Point3d(3000, 0, 0)); end.Loads.Add(new NodeForceLoadModel(loadCase, new Vector3d(0, 0, -1000)));
        var beam = new BeamModel("1", first.Position, end.Position, property);
        var input = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = "cantilever" };
        input.LoadCases.Add(loadCase); input.AddElements(new ElementModel[] { first, end, beam });
        return input;
    }
}
#endif
