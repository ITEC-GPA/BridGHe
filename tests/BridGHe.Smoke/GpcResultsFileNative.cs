#if GPC_PILOT
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// --gpc-results-native (F6 C4, native; R6.6 lane R4b: on GPC data alone): the analysed GPC model of Straus R3 Analysis and Moving Static Analysis
/// on a model given by its GPC input alone (<c>GH_Model.FromGpc</c> of the input of Build Model on GPC, no FEM value) goes to the BGHR2 file next to
/// the .st7 and comes back after saving and reopening the component (archive round trip), only when the file is the one saved: a changed file and a
/// results file of master (BGHR1) give a warning and no GPC results, a missing file a warning. After reopening, Node, Beam and Plate Results read the
/// GPC model of the file (every output equal to the GPC reader rows bit for bit) and the Cases output of Moving Static Analysis is the list of the
/// generated cases of that model, one per position of the path. The ideas of master's regression analysis-results-file (d7e4092), on the GPC
/// payload. Inputs: the cantilever of <see cref="R4bInputs.ResultsCantilever"/> and example 04 of <see cref="GpcNativeExamples"/>.
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
            // R6.6 R4b: the models given by their GPC input alone (before: the FEM-backed models, and since R6.5 P1 lane 3e their GPC-only copies).
            Component("BridGHe.Grasshopper.Components.Analysis.StrausR3AnalysisComponent", R4bInputs.ResultsCantilever(), Path.Combine(folder, "cantilever-gpc.st7"), static c =>
            { Set(c, "Solver", new GH_Integer(0)); Set(c, "Analysis Results", new GH_String("P")); Set(c, "Keep Running", new GH_Boolean(false)); }, "Output Path", 0);
            var moving = GpcNativeExamples.Create().Single(e => e.Id == "04-moving-load");
            Component("BridGHe.Grasshopper.Components.Analysis.MovingStaticAnalysisComponent", moving.Input, Path.Combine(folder, "moving-gpc.st7"), static c =>
            { Set(c, "Run", new GH_Boolean(false)); }, "Output path", moving.Inputs.Paths.Single().Divisions);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        Console.WriteLine("GPC RESULTS NATIVE PASS");
        return 0;
    }

    /// <param name="positions">The generated cases the Cases output lists: the positions of the moving path (its divisions), 0 without a path.</param>
    private static void Component(string typeName, GpcModels.Conversion input, string output, Action<GH_Component> configure, string pathInput, int positions)
    {
        string name = typeName.Split('.').Last() + " (GPC-only)";
        string[] generated = null; // C6: the "Cases" output of Moving Static Analysis at the last solution
        var type = assembly.GetType(typeName, true);
        var goo = (IGH_Goo)assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true).GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { input });
        Require(goo.GetType().GetProperty("Value").GetValue(goo) == null, name + ": the input model is given by its GPC input alone");
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
            generated = c.Params.Output.Count > 1 && c.Params.Output[1].Name == "Cases" ? c.Params.Output[1].VolatileData.AllData(true).Select(g => g.ToString()).ToArray() : null;
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
        Require(reopened.GetType().GetProperty("Value").GetValue(reopened) == null && model.GetType().GetProperty("Value").GetValue(model) == null
            && reopened.GetType().GetProperty("GpcDrafts").GetValue(reopened) != null && (bool)reopened.GetType().GetProperty("HasAttachedInput").GetValue(reopened), name + ": the output and the reopened output have no FEM value, and carry the input and its drafts");
        // C4 (R6.6 R2b/R4b, on the GPC-only output): after reopening, the Results components read the GPC model of the file, every output equal to
        // the GPC reader rows bit for bit (before: equal to the FEM values of the document).
        // ResultsComponentsCheck gives Decompose Model the GH model of the GPC input of the output: the output keeps the drafts of its elements apart
        // (GpcDrafts, as the analysis components carry them), so the input is given with them, as the reading of P2 does (R4aFixtures.GpcOnly).
        object Linked(object m)
        {
            var given = (GpcModels.Conversion)m.GetType().GetProperty("GpcInput").GetValue(m);
            var drafts = (GpcModelDrafts)m.GetType().GetProperty("GpcDrafts").GetValue(m);
            if (given.Drafts != null || drafts == null) return m;
            var modelType = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
            var linked = modelType.GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { given.WithDrafts(drafts) });
            modelType.GetMethod("AttachGpc").Invoke(linked, new object[] { Gpc(m) });
            return linked;
        }
        Require(ResultsComponentsCheck.Verify(Linked(reopened), name + " reopened").Values > 0, name + ": the Results components read the reopened GPC results");
        // C6 (before: the Cases output equal to the FEM list of generated cases): the generated cases of the reopened GPC model, one per position.
        var steps = GpcResultCatalog.Of(Gpc(reopened)).GeneratedMovingCases().Select(c => c.Name).ToArray();
        if (generated != null)
            Require(steps.Length == positions && generated.SequenceEqual(steps, StringComparer.Ordinal), name + ": the Cases output is the list of the generated cases of the reopened model, one per position of the path ("
                + generated.Length + " / " + positions + ")");
        else Require(positions == 0 && steps.Length == 0, name + ": no generated cases without a moving path");
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
}
#endif
