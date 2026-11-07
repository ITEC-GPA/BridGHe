#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using BridGHe.Common.Gpc;
using BridGHe.Common.Models;
using GH_IO.Serialization;

/// <summary>
/// Regression gpc-analysis-drafts (R6.4 (2), options 1 and 2 of P2Q, coordinator 7/11), not native, on the models of the examples 01–10:
/// <list type="bullet">
/// <item>option 1: the analysis components (Straus R3 Analysis, Moving Static Analysis) save the model they keep without the drafts of its
/// elements and with the fingerprint of the input; read back, the model carries no drafts until the first solution, which attaches those
/// of the live input when the fingerprint is the same (equal to the drafts of the run), none with another input (the warning), none with a
/// document without fingerprint;</item>
/// <item>option 2: the chunk BridGHeDrafts of every other GH_Model is BGHDRF2 (one Brotli stream) and reads back equal; a chunk BGHDRF1 of
/// the integration branch reads back equal; a truncated chunk is refused.</item>
/// </list>
/// </summary>
internal static class GpcAnalysisDraftsChecks
{
    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    internal static void Regression()
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("GPC ANALYSIS DRAFTS FAIL: " + what); }
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var goo = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
        var helper = assembly.GetType("BridGHe.Grasshopper.Helpers.AnalysisDrafts", true);
        object Model(ModelModel fem, GpcModelDrafts drafts)
        {
            var g = goo.GetMethod("FromModel", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { fem, null });
            if (drafts != null) goo.GetMethod("AttachDrafts").Invoke(g, new object[] { drafts });
            return g;
        }
        GpcModelDrafts Drafts(object g) => (GpcModelDrafts)goo.GetProperty("GpcDrafts").GetValue(g);
        object Call(object target, string name, params object[] args) =>
            target.GetType().GetMethods(Instance).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(target, args);
        string changedMessage = (string)helper.GetField("ChangedMessage", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        var examples = NativeExamples.Create().ToArray();
        int compared = 0; long v1 = 0, v2 = 0;
        for (int k = 0; k < examples.Length; k++)
        {
            var example = examples[k]; var fem = example.Model; string label = "example " + example.Id;
            var drafts = GpcInputComparison.FemmDrafts(fem); string text = GpcDraftArchiveChecks.DraftsText(drafts);
            var input = Model(fem, drafts);

            // Option 2: BGHDRF2 written by the GH_Model, read back equal; BGHDRF1 of the integration branch read back equal; truncated refused.
            var plain = new GH_LooseChunk("Model"); Require((bool)goo.GetMethod("Write", new[] { typeof(GH_IWriter) }).Invoke(input, new object[] { plain }), label + ": GH_Model written");
            Require(plain.ItemExists(GpcModelDrafts.Chunk), label + ": an internalized model keeps its drafts");
            byte[] chunk = plain.GetByteArray(GpcModelDrafts.Chunk);
            Require(Encoding.ASCII.GetString(chunk, 0, 7) == "BGHDRF2", label + ": drafts written as BGHDRF2");
            Require(GpcDraftArchiveChecks.DraftsText(GpcModelDrafts.Read(chunk)) == text, label + ": BGHDRF2 round trip");
            var back = Activator.CreateInstance(goo); Require((bool)goo.GetMethod("Read").Invoke(back, new object[] { plain }), label + ": GH_Model read");
            Require(Drafts(back) is GpcModelDrafts d0 && GpcDraftArchiveChecks.DraftsText(d0) == text, label + ": GH_Model round trip of the drafts");
            byte[] former = Bghdrf1(drafts);
            Require(GpcDraftArchiveChecks.DraftsText(GpcModelDrafts.Read(former)) == text, label + ": BGHDRF1 read back equal");
            Require(Refused(() => GpcModelDrafts.Read(chunk.Take(chunk.Length - 9).ToArray())), label + ": truncated BGHDRF2 refused");
            v1 += former.Length; v2 += chunk.Length;

            // Option 1, the helper: a run records the drafts of the input and its fingerprint.
            var output = Model(fem, null);
            var recorder = Activator.CreateInstance(helper, true);
            Call(recorder, "Record", input, output, null);
            Require(ReferenceEquals(Drafts(output), drafts), label + ": a run attaches the drafts of the input");
            var saved = new GH_LooseChunk("Component");
            Require((bool)goo.GetMethod("Write", new[] { typeof(GH_IWriter), typeof(bool) }).Invoke(output, new object[] { saved, false }), label + ": analysed model written");
            Call(recorder, "Write", saved);
            Require(!saved.ItemExists(GpcModelDrafts.Chunk) && saved.ItemExists("DraftsInputHash"), label + ": the analysed model saved without drafts, with the fingerprint of the input");
            string hash = saved.GetString("DraftsInputHash");
            Require(hash == GpcResultSelection.InputHash((GpcModels.Conversion)goo.GetProperty("GpcInput").GetValue(input)), label + ": the fingerprint is that of the input");

            // Read back: no drafts until the first solution; the same input attaches them again (equal), another input gives the warning.
            var reopened = Activator.CreateInstance(goo); Require((bool)goo.GetMethod("Read").Invoke(reopened, new object[] { saved }), label + ": analysed model read");
            Require(Drafts(reopened) == null, label + ": no drafts read with the analysed model");
            var restorer = Activator.CreateInstance(helper, true); Call(restorer, "Read", saved);
            Require((bool)helper.GetProperty("Pending").GetValue(restorer), label + ": pending after reading");
            Require(Call(restorer, "Restore", Model(fem, drafts), reopened, null) == null, label + ": same input, no warning");
            Require(Drafts(reopened) is GpcModelDrafts d1 && GpcDraftArchiveChecks.DraftsText(d1) == text, label + ": the drafts attached again are those of the run");
            Require(!(bool)helper.GetProperty("Pending").GetValue(restorer) && Call(restorer, "Restore", input, reopened, null) == null, label + ": restored once");

            var other = examples[(k + 1) % examples.Length].Model;
            var changed = Activator.CreateInstance(goo); goo.GetMethod("Read").Invoke(changed, new object[] { saved });
            var second = Activator.CreateInstance(helper, true); Call(second, "Read", saved);
            Require((string)Call(second, "Restore", Model(other, GpcInputComparison.FemmDrafts(other)), changed, null) == changedMessage && Drafts(changed) == null,
                label + ": another input attaches no drafts and warns");
            var rewritten = new GH_LooseChunk("Again"); Call(second, "Write", rewritten);
            Require(!rewritten.ItemExists("DraftsInputHash"), label + ": after a changed input no fingerprint is saved again");

            var none = Activator.CreateInstance(helper, true); Call(none, "Read", new GH_LooseChunk("Former"));
            var formerModel = Activator.CreateInstance(goo); goo.GetMethod("Read").Invoke(formerModel, new object[] { saved });
            Require(!(bool)helper.GetProperty("Pending").GetValue(none) && Call(none, "Restore", input, formerModel, null) == null && Drafts(formerModel) == null,
                label + ": a document without fingerprint attaches nothing");

            // Option 1, the components: Write leaves the drafts out and keeps the fingerprint; Read gives a model without drafts, pending.
            foreach (string name in new[] { "StrausR3AnalysisComponent", "MovingStaticAnalysisComponent" })
            {
                var type = assembly.GetTypes().Single(t => t.Name == name);
                var component = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(type);
                var analysed = Model(fem, null);
                var recorded = type.GetField("_drafts", Instance).GetValue(component);
                Call(recorded, "Record", input, analysed, name.StartsWith("Moving") ? hash : null);
                type.GetField("_model", Instance).SetValue(component, analysed);
                if (name.StartsWith("Moving")) type.GetField("_inputHash", Instance).SetValue(component, hash);
                var root = new GH_LooseChunk("Component");
                Require(component.Write(root), label + ": " + name + " written");
                Require(!Items(root).Contains(GpcModelDrafts.Chunk) && Items(root).Contains("DraftsInputHash"), label + ": " + name + " saved without drafts, with the fingerprint");
                var copy = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(type);
                Require(copy.Read(root), label + ": " + name + " read");
                var kept = type.GetField("_model", Instance).GetValue(copy); var pending = type.GetField("_drafts", Instance).GetValue(copy);
                Require(goo.GetProperty("Value").GetValue(kept) != null && Drafts(kept) == null && (bool)helper.GetProperty("Pending").GetValue(pending), label + ": " + name + " read back without drafts, pending");
                Require(Call(pending, "Restore", input, kept, name.StartsWith("Moving") ? hash : null) == null && Drafts(kept) is GpcModelDrafts d2 && GpcDraftArchiveChecks.DraftsText(d2) == text,
                    label + ": " + name + " attaches the drafts of the input again");
            }
            compared++;
        }
        Console.WriteLine($"GPC ANALYSIS DRAFTS PASS: {compared} models: analysis components save no drafts (fingerprint of the input; attached again equal, none and the warning with another input, none without fingerprint); BGHDRF2 round trip, BGHDRF1 read, truncated refused; drafts chunks {v1} bytes BGHDRF1, {v2} bytes BGHDRF2.");

        static bool Refused(Action action) { try { action(); return false; } catch (System.Runtime.Serialization.SerializationException) { return true; } }
    }

    /// <summary>The item names of a chunk and of all its chunks.</summary>
    private static HashSet<string> Items(GH_IChunk chunk)
    {
        var names = new HashSet<string>(chunk.Items.Select(i => i.Name));
        foreach (var child in chunk.Chunks) names.UnionWith(Items(child));
        return names;
    }

    /// <summary>The chunk BGHDRF1 of the integration branch (41d4947 to 64cee6b): header, version 1, deflated counts and the two chunks of the
    /// final writer of every draft, each with its length.</summary>
    private static byte[] Bghdrf1(GpcModelDrafts drafts)
    {
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("BGHDRF1")); output.WriteByte(1);
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
        using (var writer = new BinaryWriter(deflate, Encoding.UTF8, true))
        {
            writer.Write(drafts.Nodes.Count); writer.Write(drafts.Beams.Count); writer.Write(drafts.Plates.Count);
            void Pair((byte[] Data, byte[] Records) c) { writer.Write(c.Data.Length); writer.Write(c.Data); writer.Write(c.Records.Length); writer.Write(c.Records); }
            foreach (var d in drafts.Nodes) Pair(GpcDraftArchive.Write(d));
            foreach (var d in drafts.Beams) Pair(GpcDraftArchive.Write(d));
            foreach (var d in drafts.Plates) Pair(GpcDraftArchive.Write(d));
        }
        return output.ToArray();
    }
}
#endif
