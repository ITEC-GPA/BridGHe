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
            && new GPC.Checkers.CompositeBridge.BridgeModel(gpc, (GPC.Checkers.CompositeBridge.BridgeModelData)type.GetProperty("GpcBridgeData").GetValue(goo)).SuperElementNames().Contains("G1"),
            "C1 identities in the converted model (L12: super element in the bridge data)");
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
        // L12 (0022c38, coordinator 5/11): the bridge data of an analysed model on its beams by number, one to one or a loud error.
        GPC.Checkers.CompositeBridge.BridgeModelData BridgeData(object g) => (GPC.Checkers.CompositeBridge.BridgeModelData)type.GetProperty("GpcBridgeData").GetValue(g);
        Require(BridgeData(goo).SuperElement(analysed.BeamElements[1].Guid) == "G1", "bridge data on the beams of the analysed model");
        var renumbered = GPC.Model.Core.ModelValues.Copy(gpc); renumbered.RemoveElement(renumbered.BeamElements[1]);
        var mismatch = Make(femm); Gpc(mismatch); type.GetMethod("AttachGpc").Invoke(mismatch, new object[] { renumbered });
        string mismatchError = null;
        try { BridgeData(mismatch); } catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { mismatchError = e.InnerException.Message; }
        Require(mismatchError != null && mismatchError.StartsWith("GpcBridgeDataNumberingMismatch", StringComparison.Ordinal) && mismatchError.Contains("first beam number on one side only: 1"),
            "a numbering mismatch between the analysed model and the input fails loudly with the first number: " + mismatchError);
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

    /// <summary>Regression gpc-materials (F6 E10/E11, user decisions of 4/11): the concrete component gives E = Ecm of GPC (ConcreteMaterialEN1992 from
    /// fck, bit for bit) and G = E/2(1+0.2) for every grade, with UsePoisson (the analysis derives G from it). Prints the change from the former table.</summary>
    internal static void Materials()
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("GPC MATERIALS FAIL: " + what); }
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        double[] former = { 2.533E+4, 2.709E+4, 2.861E+4, 2.996E+4, 3.148E+4, 3.231E+4, 3.335E+4, 3.408E+4, 3.522E+4, 3.628E+4, 3.728E+4, 3.821E+4, 3.910E+4, 4.074E+4, 4.224E+4, 4.363E+4 };
        double[] formerG = { 1.055E+4, 1.129E+4, 1.192E+4, 1.248E+4, 1.312E+4, 1.346E+4, 1.389E+4, 1.420E+4, 1.468E+4, 1.512E+4, 1.553E+4, 1.592E+4, 1.629E+4, 1.698E+4, 1.760E+4, 1.818E+4 };
        using var document = new Grasshopper.Kernel.GH_Document { Enabled = true };
        Grasshopper.Kernel.GH_Document.EnableSolutions = true;
        var component = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(assembly.GetTypes().Single(t => t.Name == "MaterialConcreteComponent"));
        component.CreateAttributes(); document.AddObject(component, false);
        ((Grasshopper.Kernel.Parameters.Param_String)component.Params.Input[0]).PersistentData.Append(new GH_String("C"));
        var grade = (Grasshopper.Kernel.Parameters.Param_Integer)component.Params.Input[1];
        double worstE = 0, worstG = 0;
        for (int g = 0; g < former.Length; g++)
        {
            grade.PersistentData.Clear(); grade.PersistentData.Append(new GH_Integer(g)); grade.ExpireSolution(false);
            document.NewSolution(false, Grasshopper.Kernel.GH_SolutionMode.Silent);
            var goo = component.Params.Output[0].VolatileData.AllData(true).Single();
            var m = (MaterialModel)goo.GetType().GetProperty("Value").GetValue(goo);
            double ecm = new GPC.Model.Materials.ConcreteMaterialEN1992("C", m.SpecificCompressiveStrength, GPC.Model.Materials.ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle).ElasticModulusCompression;
            Require(BitConverter.DoubleToInt64Bits(m.Modulus) == BitConverter.DoubleToInt64Bits(ecm) && m.UsePoisson && m.PoissonRatio == 0.2, $"grade {g}: E = Ecm of GPC ({m.Modulus:R} / {ecm:R})");
            Require(BitConverter.DoubleToInt64Bits(m.ShearModulus) == BitConverter.DoubleToInt64Bits(ecm / (2 * 1.2)), $"grade {g}: G = E/2(1+0.2)");
            double dE = m.Modulus / former[g] - 1, dG = m.ShearModulus / formerG[g] - 1;
            worstE = Math.Max(worstE, Math.Abs(dE)); worstG = Math.Max(worstG, Math.Abs(dG));
            Console.WriteLine($"E10/E11 fck {m.SpecificCompressiveStrength:G}: E {former[g]:G6} -> {m.Modulus:G9} MPa ({dE:+0.000%;-0.000%}), G {formerG[g]:G6} -> {m.ShearModulus:G9} MPa ({dG:+0.000%;-0.000%})");
        }
        Console.WriteLine($"GPC MATERIALS PASS: 16 concrete grades, E = Ecm of GPC and G = E/2(1+nu) bit for bit; change from the former tables at most {worstE:0.000%} (E), {worstG:0.000%} (G).");
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
