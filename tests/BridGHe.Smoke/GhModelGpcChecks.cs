using System;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel.Types;

/// <summary>
/// Regression gh-model-gpc (F6 phase (c), C2; R6.6 lane R0: the R6.5 contract of the GH model container, no FEM object required): a GH model
/// given by its GPC input alone (<c>GH_Model.FromGpc</c>, the output of Build Model on GPC data) is that input: its GPC model, cached, with the
/// identities of the input (C1: Element ID and super element in the bridge data); the copies of the datum share the input; an analysed GPC model
/// attached by an analysis component is the model of the datum, shared by its copies, its bridge data on the analysed beams by number (or a loud
/// error), and dropped when the datum takes a new input; Decompose Model links each element to its analysed GPC element, with the GH GUIDs of the
/// model, and results data and copies keep the link; the datum is written and read back with its input, its element GUIDs and its results.
/// </summary>
internal static class GhModelGpcChecks
{
    internal static void Run()
    {
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var type = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
        object Make(BridGHe.Common.Gpc.GpcModels.Conversion input) => type.GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { input });
        GPC.Model.Models.Model Gpc(object goo) => (GPC.Model.Models.Model)type.GetProperty("Gpc").GetValue(goo);
        bool Analysed(object goo) => (bool)type.GetProperty("HasAnalysedGpc").GetValue(goo);
        object Input(object g) => type.GetProperty("GpcInput").GetValue(g);
        void Require(bool condition, string what) { if (!condition) throw new Exception("GH MODEL GPC FAIL: " + what); }

        var cantilever = GpcFixtures.Cantilever();
        var goo = Make(cantilever);
        var gpc = Gpc(goo);
        Require(type.GetProperty("Value").GetValue(goo) == null && ((IGH_Goo)goo).IsValid, "a GH model given by its GPC input alone has no FEM value and is valid");
        Require(ReferenceEquals(gpc, cantilever.Model) && gpc.NodesElements.Count == 2 && gpc.BeamElements.Count == 1, "the GPC model is the input model: " + type.GetProperty("GpcError").GetValue(goo));
        Require(ReferenceEquals(gpc, Gpc(goo)) && ReferenceEquals(Input(goo), cantilever) && ReferenceEquals(Input(goo), Input(goo)), "the input is the datum, cached (no conversion)");
        Require((bool)type.GetProperty("HasAttachedInput").GetValue(goo), "the input is attached");
        Require(gpc.BeamElements[1].Attributes.Values.OfType<GPC.Model.Attributes.ElementLabelAttribute>().Single().Label == "B1"
            && new GPC.Checkers.CompositeBridge.BridgeModel(gpc, (GPC.Checkers.CompositeBridge.BridgeModelData)type.GetProperty("GpcBridgeData").GetValue(goo)).SuperElementNames().Contains("G1"),
            "C1 identities in the GPC model (L12: super element in the bridge data)");
        var copy = ((IGH_Goo)goo).Duplicate();
        Require(type.GetProperty("Value").GetValue(copy) == null && ReferenceEquals(Input(copy), cantilever) && ReferenceEquals(Gpc(copy), gpc),
            "a copy owns the same GPC input (immutable by convention), with the same identities");
        Require(!Analysed(goo) && !Analysed(copy), "no analysed model yet");

        // C3: the input of analysis and export is the GPC input (cached, with its report and tolerance), also after an analysis attached its model.
        GPC.Model.Models.Model InputModel(object g) => (GPC.Model.Models.Model)Input(g).GetType().GetProperty("Model").GetValue(Input(g));
        var analysed = GPC.Model.Core.ModelValues.Copy(gpc);
        type.GetMethod("AttachGpc").Invoke(goo, new object[] { analysed });
        Require(Analysed(goo) && ReferenceEquals(Gpc(goo), analysed), "analysed model attached");
        // L12 (0022c38, coordinator 5/11): the bridge data of an analysed model on its beams by number, one to one or a loud error.
        GPC.Checkers.CompositeBridge.BridgeModelData BridgeData(object g) => (GPC.Checkers.CompositeBridge.BridgeModelData)type.GetProperty("GpcBridgeData").GetValue(g);
        Require(BridgeData(goo).SuperElement(analysed.BeamElements[1].Guid) == "G1", "bridge data on the beams of the analysed model");
        var renumbered = GPC.Model.Core.ModelValues.Copy(gpc); renumbered.RemoveElement(renumbered.BeamElements[1]);
        var mismatch = Make(cantilever); type.GetMethod("AttachGpc").Invoke(mismatch, new object[] { renumbered });
        string mismatchError = null;
        try { BridgeData(mismatch); } catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { mismatchError = e.InnerException.Message; }
        Require(mismatchError != null && mismatchError.StartsWith("GpcBridgeDataNumberingMismatch", StringComparison.Ordinal) && mismatchError.Contains("first beam number on one side only: 1"),
            "a numbering mismatch between the analysed model and the input fails loudly with the first number: " + mismatchError);
        Require(ReferenceEquals(InputModel(goo), gpc), "with an analysed model attached the analysis input is still the GPC input");
        var duplicate = ((IGH_Goo)goo).Duplicate();
        Require(Analysed(duplicate) && ReferenceEquals(Gpc(duplicate), analysed), "copies share the analysed model");
        Require(!Analysed(copy), "a copy made before the analysis keeps no analysed model");

        // Invalidation: a new GPC input (the producer solved again: a new datum or AttachInput) gives new state; the analysed model is dropped.
        var longer = GpcFixtures.Cantilever("cantilever", 2);
        type.GetMethod("AttachInput").Invoke(goo, new object[] { longer });
        var changed = Gpc(goo);
        Require(!Analysed(goo) && ReferenceEquals(changed, longer.Model) && changed.Guid != gpc.Guid && ReferenceEquals(Input(goo), longer), "a new GPC input drops the analysed model and is the model of the datum");
        Require(Analysed(duplicate) && ReferenceEquals(Input(duplicate), cantilever), "the copy keeps its input and its analysed model");
        Require(GpcFixtures.Cantilever().Model.Guid == gpc.Guid && GpcFixtures.Cantilever().Model.BeamElements[1].Guid == gpc.BeamElements[1].Guid,
            "the same inputs give the same identities (Build Model on GPC)");

        // C4: Decompose Model links each element to its analysed GPC element (the order of the drafts), with the GH GUIDs of the model; results
        // data and copies keep the link.
        var analysedGoo = Make(GpcFixtures.Cantilever()); var analysedModel = GPC.Model.Core.ModelValues.Copy(Gpc(analysedGoo));
        type.GetMethod("AttachGpc").Invoke(analysedGoo, new object[] { analysedModel });
        var guids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        type.GetMethod("AttachElementGuids").Invoke(analysedGoo, new object[] { guids });
        object Decomposed(object model, int output, int index)
        {
            using var document = new Grasshopper.Kernel.GH_Document { Enabled = true };
            Grasshopper.Kernel.GH_Document.EnableSolutions = true;
            var decompose = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(assembly.GetTypes().Single(t => t.Name == "DecomposeModelComponent"));
            decompose.CreateAttributes(); document.AddObject(decompose, false);
            var input = (Grasshopper.Kernel.Parameters.Param_GenericObject)decompose.Params.Input[0]; input.PersistentData.Append((IGH_Goo)model); input.ExpireSolution(false);
            document.NewSolution(false, Grasshopper.Kernel.GH_SolutionMode.Silent);
            Require(decompose.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error).Count == 0, "Decompose Model: " + string.Join("; ", decompose.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)));
            return decompose.Params.Output[output].VolatileData.AllData(true).ElementAt(index);
        }
        object Linked(object g) => g.GetType().GetProperty("AnalysedGpc").GetValue(g);
        Guid GuidOf(object g) => (Guid)g.GetType().GetProperty("Guid").GetValue(g);
        var beamGoo = (IGH_Goo)Decomposed(analysedGoo, 1, 0); var nodeGoo = (IGH_Goo)Decomposed(analysedGoo, 0, 0);
        Require(beamGoo.GetType().GetProperty("Value").GetValue(beamGoo) == null && ReferenceEquals(Linked(beamGoo), analysedModel.BeamElements[1]) && ReferenceEquals(Linked(nodeGoo), analysedModel.NodesElements[1]),
            "Decompose Model gives the elements by their drafts alone, linked to the analysed GPC elements");
        Require(GuidOf(nodeGoo) == guids[0] && GuidOf(beamGoo) == guids[2], "Decompose Model gives the GH GUIDs of the model");
        var results = Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_BeamResults", true), beamGoo);
        Require(ReferenceEquals(Linked(results), analysedModel.BeamElements[1]) && ReferenceEquals(Linked(((IGH_Goo)results).Duplicate()), analysedModel.BeamElements[1])
            && ReferenceEquals(Linked(beamGoo.Duplicate()), analysedModel.BeamElements[1]), "beam results and copies keep the link");

        // Persistence: the datum written with its input, its element GUIDs and (internalised) its analysed model with the results catalog, read
        // back without a FEM value.
        BridGHe.Common.Gpc.GpcResultCatalog.Attach(analysedModel, BridGHe.Common.Gpc.DefinitionUnits.Nmm, new[] { new BridGHe.Common.Gpc.GpcResultCatalog.Entry { Label = "P", Case = "Dead" } },
            new System.Collections.Generic.Dictionary<int, double> { [1] = 0 });
        var chunk = new GH_IO.Serialization.GH_LooseChunk("model");
        Require((bool)type.GetMethod("Write", new[] { typeof(GH_IO.Serialization.GH_IWriter), typeof(bool), typeof(bool) }).Invoke(analysedGoo, new object[] { chunk, true, true }), "the datum is written");
        var binary = chunk.Serialize_Binary();
        var reader = new GH_IO.Serialization.GH_LooseChunk("read"); reader.Deserialize_Binary(binary);
        var read = Activator.CreateInstance(type);
        Require((bool)type.GetMethod("Read").Invoke(read, new object[] { reader }) && type.GetProperty("Value").GetValue(read) == null, "the datum is read back without a FEM value");
        var readInput = (BridGHe.Common.Gpc.GpcModels.Conversion)Input(read);
        var readDrafts = (BridGHe.Common.Gpc.GpcModelDrafts)type.GetProperty("GpcDrafts").GetValue(read);
        Require(readInput.Model.Guid == gpc.Guid && GPC.Model.Models.ModelRevisions.AnalysisFingerprint(readInput.Model) == GPC.Model.Models.ModelRevisions.AnalysisFingerprint(gpc)
            && readInput.Tolerance == cantilever.Tolerance && readDrafts != null && readDrafts.Nodes.Count == 2 && readDrafts.Beams.Count == 1, "the GPC input is read back (identity, content, tolerance, drafts)");
        var readGuids = (Guid[])type.GetMethod("ElementGuidsOf").Invoke(read, new object[] { readDrafts });
        Require(readGuids.SequenceEqual(guids), "the element GUIDs are read back");
        Require(Analysed(read) && BridGHe.Common.Gpc.GpcResultCatalog.Of(Gpc(read))?.Entries.Single().Label == "P" && Gpc(read).Guid == analysedModel.Guid, "the internalised analysed model is read back with its results catalog");
        Console.WriteLine("GH MODEL GPC PASS: GPC-only datum, cache, identities, copies share the input and the analysed model, a new input drops it; Decompose Model links the analysed elements with the GH GUIDs; written and read back.");
    }

    /// <summary>Regression gpc-results-file: the BGHR2 results file next to the .st7 (GpcResultsFile) round trips the GPC model, refuses a
    /// changed file and recognises a results file of master (BGHR1) with the "previous version" message, never reading it. R6.6 R0: the model
    /// is the GPC cantilever of Build Model on GPC (no FEM conversion).</summary>
    internal static void ResultsFile()
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("GPC RESULTS FILE FAIL: " + what); }
        var model = GPC.Model.Core.ModelValues.Copy(GpcFixtures.Cantilever().Model);
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
    /// fck, bit for bit) and ν 0.2 with Use Poisson (the analysis derives G = E/2(1+ν)) for every grade. Prints the change from the former table.
    /// R6.6 R0: read on the GPC datum of the component output (the GPC material and its analysis material), not on the FEM value; the grades
    /// by their fck (C8/10 … C90/105).</summary>
    internal static void Materials()
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("GPC MATERIALS FAIL: " + what); }
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        double[] fck = { 8, 12, 16, 20, 25, 28, 32, 35, 40, 45, 50, 55, 60, 70, 80, 90 };
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
            var material = (GPC.Model.Materials.Material)goo.GetType().GetProperty("Gpc").GetValue(goo);
            // The GPC concretes hold the compressive strengths with the sign of compression (fck = −8 for C8/10).
            Require(material is GPC.Model.Materials.ConcreteMaterialEuropeanCommon c && c.Fck == -fck[g], $"grade {g}: a GPC concrete of fck {fck[g]} ({material?.GetType().Name} {(material as GPC.Model.Materials.ConcreteMaterialEuropeanCommon)?.Fck:R})");
            var analysis = BridGHe.Common.Gpc.GpcMaterials.Analysis(material);
            double ecm = new GPC.Model.Materials.ConcreteMaterialEN1992("C", fck[g], GPC.Model.Materials.ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle).ElasticModulusCompression;
            Require(BitConverter.DoubleToInt64Bits(analysis.ElasticModulusCompression) == BitConverter.DoubleToInt64Bits(ecm) && analysis.Ni == 0.2, $"grade {g}: E = Ecm of GPC ({analysis.ElasticModulusCompression:R} / {ecm:R}), ν 0.2");
            Require(analysis.AnalysisData?.BeamShearModulus == null, $"grade {g}: Use Poisson (no shear modulus of its own: the analysis derives G = E/2(1+ν))");
            double e = analysis.ElasticModulusCompression, shear = e / (2 * (1 + analysis.Ni));
            double dE = e / former[g] - 1, dG = shear / formerG[g] - 1;
            worstE = Math.Max(worstE, Math.Abs(dE)); worstG = Math.Max(worstG, Math.Abs(dG));
            Console.WriteLine($"E10/E11 fck {fck[g]:G}: E {former[g]:G6} -> {e:G9} MPa ({dE:+0.000%;-0.000%}), G {formerG[g]:G6} -> {shear:G9} MPa ({dG:+0.000%;-0.000%})");
        }
        Console.WriteLine($"GPC MATERIALS PASS: 16 concrete grades, E = Ecm of GPC and G = E/2(1+nu) bit for bit; change from the former tables at most {worstE:0.000%} (E), {worstG:0.000%} (G).");
    }
}
