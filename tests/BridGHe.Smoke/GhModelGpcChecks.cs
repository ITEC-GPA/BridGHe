#if GPC_PILOT
using System;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Models;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// Regression gh-model-gpc (F6 phase (c), C2): the FEM model of a GH model carries its GPC model. Converted once and cached; the
/// conversion follows the FEM model (a new FEM model gives a new conversion, the same content the same identities); an analysed GPC
/// model attached by an analysis component is shared by the copies of the datum and dropped when the FEM model changes.
/// </summary>
internal static class GhModelGpcChecks
{
    internal static void Run()
    {
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var type = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
        object Make(ModelModel model) => type.GetMethod("FromModel", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { model, null });
        GPC.Model.Models.Model Gpc(object goo) => (GPC.Model.Models.Model)type.GetProperty("Gpc").GetValue(goo);
        bool Analysed(object goo) => (bool)type.GetProperty("HasAnalysedGpc").GetValue(goo);
        void Require(bool condition, string what) { if (!condition) throw new Exception("GH MODEL GPC FAIL: " + what); }

        var femm = Cantilever();
        var goo = Make(femm);
        var gpc = Gpc(goo);
        Require(gpc != null && gpc.NodesElements.Count == 2 && gpc.BeamElements.Count == 1, "converted model: " + type.GetProperty("GpcError").GetValue(goo));
        Require(ReferenceEquals(gpc, Gpc(goo)), "conversion cached");
        Require(gpc.BeamElements[1].Attributes.Values.OfType<GPC.Model.Attributes.ElementLabelAttribute>().Single().Label == "B1"
            && gpc.PhysicalMembers.ContainsKey("SuperElement:G1"), "C1 identities in the converted model");
        var copy = ((IGH_Goo)goo).Duplicate();
        var copied = Gpc(copy);
        Require(!ReferenceEquals(copied, gpc) && copied.Guid == gpc.Guid && copied.BeamElements[1].Guid == gpc.BeamElements[1].Guid, "a copy converts its own FEM model to the same identities");
        Require(!Analysed(goo) && !Analysed(copy), "no analysed model yet");

        // C3: the input of analysis and export is the conversion (cached, with its report and tolerance), also after an analysis attached its model.
        object Input(object g) => type.GetProperty("GpcInput").GetValue(g);
        GPC.Model.Models.Model InputModel(object g) => (GPC.Model.Models.Model)Input(g).GetType().GetProperty("Model").GetValue(Input(g));
        Require(ReferenceEquals(Input(goo), Input(goo)) && ReferenceEquals(InputModel(goo), gpc), "the conversion is the analysis input, cached");
        var analysed = GPC.Model.Core.ModelValues.Copy(gpc);
        type.GetMethod("AttachGpc").Invoke(goo, new object[] { analysed });
        Require(Analysed(goo) && ReferenceEquals(Gpc(goo), analysed), "analysed model attached");
        Require(ReferenceEquals(InputModel(goo), gpc), "with an analysed model attached the analysis input is still the conversion");
        var duplicate = ((IGH_Goo)goo).Duplicate();
        Require(Analysed(duplicate) && ReferenceEquals(Gpc(duplicate), analysed), "copies share the analysed model");

        type.GetProperty("Value").SetValue(goo, Cantilever(2));
        var changed = Gpc(goo);
        Require(!Analysed(goo) && changed != null && !ReferenceEquals(changed, analysed) && changed.Guid != gpc.Guid, "a new FEM model drops the analysed model and converts again");
        Require(Analysed(duplicate), "the copy keeps its analysed model");
        // C4: Decompose Model links each element to its analysed GPC element (FEM order), and results data and copies keep the link.
        var analysedGoo = Make(Cantilever()); var analysedModel = GPC.Model.Core.ModelValues.Copy(Gpc(analysedGoo));
        type.GetMethod("AttachGpc").Invoke(analysedGoo, new object[] { analysedModel });
        using (var document = new Grasshopper.Kernel.GH_Document { Enabled = true })
        {
            Grasshopper.Kernel.GH_Document.EnableSolutions = true;
            var decompose = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.DecomposeModelComponent", false)
                ?? assembly.GetTypes().Single(t => t.Name == "DecomposeModelComponent"));
            decompose.CreateAttributes(); document.AddObject(decompose, false);
            var input = (Grasshopper.Kernel.Parameters.Param_GenericObject)decompose.Params.Input[0]; input.PersistentData.Append((IGH_Goo)analysedGoo); input.ExpireSolution(false);
            document.NewSolution(false, Grasshopper.Kernel.GH_SolutionMode.Silent);
            var beamGoo = (IGH_Goo)decompose.Params.Output[1].VolatileData.AllData(true).Single();
            var nodeGoo = (IGH_Goo)decompose.Params.Output[0].VolatileData.AllData(true).First();
            object Linked(object g) => g.GetType().GetProperty("AnalysedGpc").GetValue(g);
            Require(ReferenceEquals(Linked(beamGoo), analysedModel.BeamElements[1]) && ReferenceEquals(Linked(nodeGoo), analysedModel.NodesElements[1]), "Decompose Model links the analysed GPC elements");
            var results = Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_BeamResults", true), beamGoo);
            Require(ReferenceEquals(Linked(results), analysedModel.BeamElements[1]) && ReferenceEquals(Linked(((IGH_Goo)results).Duplicate()), analysedModel.BeamElements[1])
                && ReferenceEquals(Linked(beamGoo.Duplicate()), analysedModel.BeamElements[1]), "beam results and copies keep the link");
        }
        Console.WriteLine("GH MODEL GPC PASS: conversion, cache, identities, analysed model shared by copies and dropped when the FEM model changes; Decompose Model links the analysed elements.");
    }

    /// <summary>Regression gpc-results-file: the BGHR2 results file next to the .st7 (GpcResultsFile) round trips the GPC model, refuses a
    /// changed file and recognises a results file of master (BGHR1) with the "previous version" message, never reading it.</summary>
    internal static void ResultsFile()
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("GPC RESULTS FILE FAIL: " + what); }
        var model = BridGHe.Common.Gpc.GpcModels.FromFemm(Cantilever(), 0.001, out _);
        // C4: the results catalog of the Results components travels in the file (a BridGHe record of the analysed model).
        var entries = new[] { new BridGHe.Common.Gpc.GpcResultCatalog.Entry { Label = "Q_MOV_0001", Case = "Traffic", Step = "1: Q_MOV_0001" }, new BridGHe.Common.Gpc.GpcResultCatalog.Entry { Label = "1.5P", Case = "1.5P" } };
        BridGHe.Common.Gpc.GpcResultCatalog.Attach(model, BridGHe.Common.Gpc.DefinitionUnits.Knm, entries, new System.Collections.Generic.Dictionary<int, double> { [1] = 0.1 + 0.2 });
        Require(model.PreservedSourceData.Count(r => r.Kind == BridGHe.Common.Gpc.GpcResultCatalog.Kind) == 1, "one results catalog in the analysed model");
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bridghe-bghr2-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(folder);
        try
        {
            string path = BridGHe.Common.Gpc.GpcResultsFile.PathFor(System.IO.Path.Combine(folder, "model.st7"));
            Require(path.EndsWith("model.bghr", StringComparison.OrdinalIgnoreCase), "path next to the .st7");
            string hash = BridGHe.Common.Gpc.GpcResultsFile.Write(path, model);
            Require(!System.IO.File.Exists(path + ".tmp") && hash == BridGHe.Common.Gpc.GpcResultsFile.Hash(path), "written through a temporary file, hash of the file");
            var read = BridGHe.Common.Gpc.GpcResultsFile.Read(path, hash);
            Require(read.Guid == model.Guid && GPC.Model.Models.ModelRevisions.AnalysisFingerprint(read) == GPC.Model.Models.ModelRevisions.AnalysisFingerprint(model)
                && read.BeamElements[1].Guid == model.BeamElements[1].Guid && read.PhysicalMembers.Count == model.PhysicalMembers.Count, "round trip of the GPC model");
            var catalog = BridGHe.Common.Gpc.GpcResultCatalog.Of(read);
            Require(catalog != null && catalog.Units.Units == BridGHe.Common.Gpc.DefinitionUnits.Knm && catalog.Entries.Count == 2 && catalog.Entries[0].Step == "1: Q_MOV_0001" && catalog.Entries[1].Phase == null
                && catalog.Entries[1].Label == "1.5P" && BitConverter.DoubleToInt64Bits(catalog.BeamAngle(read.BeamElements[1])) == BitConverter.DoubleToInt64Bits(0.1 + 0.2), "the results catalog round trips (entries, units, exact beam angles)");
            BridGHe.Common.Gpc.GpcResultCatalog.Remove(read);
            Require(BridGHe.Common.Gpc.GpcResultCatalog.Of(read) == null && read.PreservedSourceData.All(r => r.Kind != BridGHe.Common.Gpc.GpcResultCatalog.Kind), "the catalog is removed before a new analysis");
            var bytes = System.IO.File.ReadAllBytes(path); bytes[bytes.Length - 1] ^= 0x5A; System.IO.File.WriteAllBytes(path, bytes);
            Require(Throws(() => BridGHe.Common.Gpc.GpcResultsFile.Read(path, hash), "cambiato"), "a changed file is refused");
            string master = System.IO.Path.Combine(folder, "master.bghr"); System.IO.File.WriteAllBytes(master, System.Text.Encoding.ASCII.GetBytes("BGHR1").Concat(new byte[64]).ToArray());
            Require(BridGHe.Common.Gpc.GpcResultsFile.IsPreviousVersion(master) && Throws(() => BridGHe.Common.Gpc.GpcResultsFile.Read(master, BridGHe.Common.Gpc.GpcResultsFile.Hash(master)), "versione precedente"),
                "a results file of master is recognised, with the previous-version message");
        }
        finally { try { System.IO.Directory.Delete(folder, true); } catch (System.IO.IOException) { } }
        Console.WriteLine("GPC RESULTS FILE PASS: BGHR2 next to the .st7, temporary file, hash, round trip (results catalog included), changed file refused, master BGHR1 recognised.");
        static bool Throws(Action action, string text) { try { action(); return false; } catch (System.IO.InvalidDataException e) { return e.Message.Contains(text); } }
    }

    private static ModelModel Cantilever(double length = 1)
    {
        var steel = new MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, UsePoisson = true, Density = 7.85e-9, ThermalExpansion = 1.2e-5 };
        var section = new SectionModel { Name = "R", SectionType = SectionModel.SectionTypes.SolidRectangle, B = 300, D = 500 };
        var property = new BeamPropertyModel("P") { Name = "P", Material = steel, Section = section, PropertyType = BeamPropertyType.Beam };
        var a = new NodeModel("1", new Point3d(0, 0, 0)); var b = new NodeModel("2", new Point3d(1000 * length, 0, 0));
        a.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
        var beam = new BeamModel(new Point3d(0, 0, 0), new Point3d(1000 * length, 0, 0), property, new GroupModel("Girder"), 0) { BeamId = "1" };
        beam.Attributes.Add(new ElementIdAttributeModel("B1")); beam.Attributes.Add(new SuperElementAttributeModel("G1"));
        var model = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = "cantilever" };
        model.AddElements(new ElementModel[] { a, b, beam });
        return model;
    }
}
#endif
