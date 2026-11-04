using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// 4/11/2026: the analysis components keep their results in a file next to the .st7 and the Grasshopper document only the file
/// and its SHA-256 (example 50 REV003 had grown from 4.9 to 887 MB, and every autosave rewrote it). No native analysis runs here:
/// results are seeded in the components as the archived-replay tests do.
/// </summary>
internal static class AnalysisResultsFileTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }

    internal static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "bridghe-risultati-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            FileRoundTrip(folder);
            StrausComponent(folder);
            MovingComponent(folder);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }

    private static ModelModel Model(bool results)
    {
        var section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 300 };
        var property = new BeamPropertyModel("P") { Section = section, Material = new MaterialModel("S"), PropertyType = BeamPropertyType.Beam };
        var model = new ModelModel(ModelModel.ModelUnits.Nmm);
        var beam = new BeamModel(Point3d.Origin, new Point3d(1000, 0, 0), property);
        model.AddElements(new ElementModel[] { new NodeModel("1", Point3d.Origin), new NodeModel("2", new Point3d(1000, 0, 0)), beam });
        var load = new LoadCaseModel("G1", "G1"); model.LoadCases.Add(load);
        if (results)
        {
            model.Nodes[1].Results.Add(new NodeDisplResultModel(load));
            beam.Results.Add(new BeamForceResultModel(load, 0.5, 0, "", true) { AxialForce = 12.5 });
        }
        return model;
    }

    private static string Archive(ModelModel model) => Convert.ToHexString(SHA256.HashData(ModelArchive.Write(model)));

    private static void FileRoundTrip(string folder)
    {
        var model = Model(true);
        var guids = model.Elements.Select(_ => Guid.NewGuid()).ToArray();
        string path = AnalysisResultsFile.PathFor(Path.Combine(folder, "file", "M01.st7"));
        Require(path.EndsWith("M01.bghr", StringComparison.Ordinal), "results file next to the .st7, with its own extension");
        string hash = AnalysisResultsFile.Write(path, model, guids);
        Require(File.Exists(path) && !File.Exists(path + ".tmp") && hash == AnalysisResultsFile.Hash(path), "results file written whole, with its SHA-256");
        var read = AnalysisResultsFile.Read(path, hash, out var readGuids);
        Require(Archive(read) == Archive(model) && readGuids.SequenceEqual(guids), "results file reads back the same model and element GUIDs");
        Require(AnalysisResultsFile.HasResults(model) && !AnalysisResultsFile.HasResults(Model(false)) && !AnalysisResultsFile.HasResults(null), "models without results are recognized");
        File.AppendAllText(path, "x");
        bool refused = false;
        try { AnalysisResultsFile.Read(path, hash, out _); } catch (InvalidDataException) { refused = true; }
        Require(refused, "a results file changed after saving is refused");
    }

    private static Assembly Plugin() => BridgeMergeTests.Initialize();

    private static IGH_Goo Goo(Assembly assembly, ModelModel model)
    {
        var goo = (IGH_Goo)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true));
        goo.GetType().GetProperty("Value").SetValue(goo, model);
        return goo;
    }

    private static GH_Component Component(Assembly assembly, GH_Document doc, string type, IGH_Goo input, string output)
    {
        var component = (GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.Analysis." + type, true));
        component.CreateAttributes();
        doc.AddObject(component, false);
        Inputs(component, input, output);
        return component;
    }

    private static void Inputs(GH_Component component, IGH_Goo input, string output)
    {
        var model = (Param_GenericObject)component.Params.Input[0];
        model.PersistentData.Clear(); model.SetPersistentData(input);
        var path = (Param_String)component.Params.Input.Single(p => p.Name.Equals("Output Path", StringComparison.OrdinalIgnoreCase));
        path.PersistentData.Clear(); path.SetPersistentData(output);
    }

    private static void Solve(GH_Document doc) { GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(true, GH_SolutionMode.Silent); }

    private static ModelModel Output(GH_Component component) =>
        component.Params.Output[0].VolatileData.AllData(true).Select(g => g.ScriptVariable()).OfType<ModelModel>().SingleOrDefault();

    private static GH_LooseChunk Saved(GH_Component component)
    {
        var chunk = new GH_LooseChunk("component");
        Require(component.Write(chunk), component.Name + ": component written");
        var copy = new GH_LooseChunk("component");
        copy.Deserialize_Binary(chunk.Serialize_Binary());
        return copy;
    }

    private static GH_Component Reopened(Assembly assembly, string type, GH_LooseChunk chunk, IGH_Goo input, string output, out GH_Document doc)
    {
        doc = new GH_Document();
        var component = Component(assembly, doc, type, input, output);
        Require(component.Read(chunk), type + ": component read");
        // Read replaces the persistent inputs with the saved ones: restore those of this test.
        Inputs(component, input, output);
        return component;
    }

    private static void Seed(GH_Component component, object goo) => component.GetType().GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, goo);

    private static void StrausComponent(string folder)
    {
        var assembly = Plugin();
        string output = Path.Combine(folder, "straus", "M01.st7");
        string file = AnalysisResultsFile.PathFor(output);
        var input = Goo(assembly, Model(false));
        var analysed = Goo(assembly, Model(true));

        // A document saved before the change: the model with its results inside the component.
        using var before = new GH_Document();
        var fresh = Component(assembly, before, "StrausR3AnalysisComponent", input, output);
        Seed(fresh, analysed);
        var legacy = Saved(fresh);
        Require(legacy.ItemExists("Data") && !legacy.ItemExists("ResultsHash") && !File.Exists(file), "a component never solved still embeds its results (no output path yet)");

        var reopened = Reopened(assembly, "StrausR3AnalysisComponent", legacy, input, output, out var doc);
        Solve(doc);
        Require(Output(reopened) is { } fromLegacy && Archive(fromLegacy) == Archive((ModelModel)analysed.ScriptVariable()), "a document saved before opens with its embedded results");
        var migrated = Saved(reopened);
        Require(!migrated.ItemExists("Data") && migrated.ItemExists("ResultsHash") && File.Exists(file), "at the next save the results move next to the .st7");
        Require(migrated.Serialize_Binary().Length < legacy.Serialize_Binary().Length, "the saved component no longer carries the results");
        var again = Saved(reopened);
        Require(again.GetString("ResultsHash") == migrated.GetString("ResultsHash"), "later saves only repeat the reference");

        var loaded = Reopened(assembly, "StrausR3AnalysisComponent", migrated, input, output, out var doc2);
        Require(!loaded.Params.Output[0].VolatileData.AllData(true).Any(), "the file is not read while opening");
        var unsolvedSave = Saved(loaded);
        Require(unsolvedSave.GetString("ResultsHash") == migrated.GetString("ResultsHash"), "saving before the first solution keeps the reference");
        Solve(doc2);
        var restored = Output(loaded);
        Require(restored != null && Archive(restored) == Archive((ModelModel)analysed.ScriptVariable()), "the first solution reads the results from their file");
        // As with embedded results, the GUIDs of the elements follow those of the input model.
        Guid[] Guids(object model) => (Guid[])model.GetType().GetMethod("ElementGuids").Invoke(model, null);
        var goo = loaded.Params.Output[0].VolatileData.AllData(true).Single();
        var legacyGoo = reopened.Params.Output[0].VolatileData.AllData(true).Single();
        Require(Guids(goo).SequenceEqual(Guids(legacyGoo)) && Guids(goo).Take(2).SequenceEqual(Guids(input).Take(2)), "element GUIDs are restored as for embedded results");

        File.AppendAllText(file, "x");
        var stale = Reopened(assembly, "StrausR3AnalysisComponent", migrated, input, output, out var doc3);
        Solve(doc3);
        Require(Output(stale)?.Elements.Count is null or 0 && stale.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("Run")),
            "a results file changed after saving is not used, and the component asks for Run");
        Seed(stale, Goo(assembly, Model(false)));
        var noResults = Saved(stale);
        Require(noResults.ItemExists("Data") && !noResults.ItemExists("ResultsHash"), "a model without results is written in the document, as before");
    }

    private static void MovingComponent(string folder)
    {
        var assembly = Plugin();
        string output = Path.Combine(folder, "mobile", "M03_MOBILE.st7");
        string file = AnalysisResultsFile.PathFor(output);
        var inputModel = Model(false);
        var input = Goo(assembly, inputModel);
        var analysed = Goo(assembly, Model(true));

        using var doc = new GH_Document();
        var component = Component(assembly, doc, "MovingStaticAnalysisComponent", input, output);
        Solve(doc);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var hash = (string)component.GetType().GetField("_hashedValue", flags).GetValue(component);
        Require(hash != null && component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.StartsWith("Run required", StringComparison.Ordinal)),
            "without results the moving analysis asks for Run");
        var inputHash = component.GetType().GetMethod("InputHash", flags);
        var probe = Model(false);
        string first = (string)inputHash.Invoke(component, new object[] { probe });
        var cached = component.GetType().GetField("_hashedInput", flags).GetValue(component);
        Require(first == Archive(probe) && ReferenceEquals(cached, probe) && (string)inputHash.Invoke(component, new object[] { probe }) == first,
            "the input is hashed once per input object");
        Seed(component, analysed);
        component.GetType().GetField("_inputHash", flags).SetValue(component, hash);
        component.ExpireSolution(false); Solve(doc);
        Require(Output(component) != null, "seeded moving results are delivered for the same input");
        var saved = Saved(component);
        Require(saved.ItemExists("InputHash") && saved.ItemExists("ResultsHash") && !saved.ChunkExists("MovingResults") && File.Exists(file),
            "moving results are saved next to the .st7, not in MovingResults");

        var reopened = Reopened(assembly, "MovingStaticAnalysisComponent", saved, input, output, out var doc2);
        Solve(doc2);
        Require(Output(reopened) is { } restored && Archive(restored) == Archive((ModelModel)analysed.ScriptVariable()), "reopened moving results come from their file");

        var changed = Model(false); changed.LoadCases.Add(new LoadCaseModel("G2", "G2"));
        var other = Reopened(assembly, "MovingStaticAnalysisComponent", saved, Goo(assembly, changed), output, out var doc3);
        Solve(doc3);
        Require(Output(other) == null && other.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.StartsWith("Run required", StringComparison.Ordinal)),
            "a changed input asks for Run instead of using the saved results");
    }
}
