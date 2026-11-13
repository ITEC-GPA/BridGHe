using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Diagnostics;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// U2 (13/11/2026, night; user decisions U2 of 12/11, «46 senza link rigido» of 13/11 and «rifarli stanotte su un ramo separato» of 13/11
/// 22:10): the 7 examples (9 definitions) remade as 0.4.0 files, on GPC data alone (BGHGPC1 schema 1, no FemmR6, no FEM type).
/// <list type="bullet">
/// <item>24, 42, 43, 47-integrazione-modello, 47-pioli-irrigidimenti-verifiche, 48: read as the product reads them (every internalised datum
/// by value; a development model takes the drafts and GUIDs of its elements from its FEM chunk) and written back as they are (as
/// <see cref="P2dExampleRemake"/>): every object, instance GUID, wire and value kept, only the internalised BridGHe data in 0.4.0 form.</item>
/// <item>23 and 46: their 0.3.0 models are refused by the reader (beams without end nodes; in 46 also a rigid link whose target has no node),
/// so their internalised data are rebuilt with the GPC builders of their generators (<see cref="BridgeUtilityExamples"/>,
/// <see cref="BridgeCombinationExcelExample"/>: the same samples, the end nodes added, 46 without the rigid link) and put in place of the
/// former data, in the same objects of the same file.</item>
/// <item>47-anteprima-grafici holds no BridGHe datum: it is only checked (read and written in TEMP, structurally the same) and not rewritten.</item>
/// </list>
/// <para><c>--u2-remake-examples [write] [dump]</c>: without <c>write</c> into <c>%TEMP%\bridghe-u2-remake</c>; with <c>write</c> into the
/// worktree (never the main checkout). <c>dump</c> also writes the text of every former 0.3.0 payload (by value, no FEM class) and of the
/// GPC data put in its place (for the list of the user). Nothing is solved here: <c>--u2-check-examples &lt;old root&gt; &lt;new root&gt;</c>
/// solves both copies (Build BridGHe Model, Super Element Check and the Excel read in mode 1 pressed; no analysis, no plot, no report, no
/// Excel write) and compares every output and message, component by component.</para>
/// </summary>
internal static class U2ExampleRemake
{
    internal static readonly string[] Files =
    {
        @"23-audit-modello-fase\23-audit-modello-fase.ghx", @"24-inviluppi-concomitanti\24-inviluppi-concomitanti.ghx",
        @"42-frecce-controfreccia\42-frecce-controfreccia.ghx", @"43-storie-fatica\43-storie-fatica.ghx", @"46-combinazioni-excel\46-combinazioni-excel.ghx",
        @"47-pioli-irrigidimenti-verifiche\47-integrazione-modello.ghx", @"47-pioli-irrigidimenti-verifiche\47-pioli-irrigidimenti-verifiche.ghx",
        @"47-pioli-irrigidimenti-verifiche\47-anteprima-grafici.ghx", @"48-report-word\48-report-word.ghx",
    };

    private static IEnumerable<IGH_Param> Params(GH_Document doc) => doc.Objects.OfType<IGH_Param>().Concat(doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input));

    private static IGH_Structure Persistent(IGH_Param p) => p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure;

    private static bool Bridghe(IGH_Goo g) => g?.GetType().Namespace?.StartsWith("BridGHe.", StringComparison.Ordinal) == true;

    /// <summary>The internalised BridGHe data of a document, one line each: owner object (instance GUID, nickname), parameter, path, index,
    /// type and the state of a model (GPC input, drafts, results).</summary>
    private static List<string> Inventory(GH_Document doc)
    {
        var lines = new List<string>();
        foreach (var p in Params(doc))
        {
            var tree = Persistent(p); if (tree == null) continue;
            var owner = p.Attributes?.GetTopLevel?.DocObject ?? p;
            for (int b = 0; b < tree.PathCount; b++)
            {
                var branch = tree.get_Branch(b);
                for (int i = 0; i < branch.Count; i++)
                {
                    if (!(branch[i] is IGH_Goo g) || !Bridghe(g)) continue;
                    string state = "";
                    if (g.GetType().Name == "GH_Model")
                        state = $" input {R2bFixtures.Get<GpcModels.Conversion>(g, "GpcInput") != null} drafts {R2bFixtures.Get<GpcModelDrafts>(g, "GpcDrafts") != null} results {R2bFixtures.Get<GPC.Model.Models.Model>(g, "GpcResults") != null}";
                    lines.Add($"{owner.InstanceGuid} {owner.NickName} / {p.Name} {tree.get_Path(b)}[{i}] {g.GetType().Name} valid {g.IsValid}{state}");
                }
            }
        }
        return lines;
    }

    /// <summary>The objects of a document: instance GUID, type, nickname, the sources of every input (instance GUIDs), in document order.</summary>
    private static List<string> Objects(GH_Document doc) => doc.Objects.Select(o =>
    {
        string wires = o is GH_Component c ? string.Join(";", c.Params.Input.Select(p => p.Name + "<" + string.Join(",", p.Sources.Select(s => s.InstanceGuid))))
            : o is IGH_Param q ? "<" + string.Join(",", q.Sources.Select(s => s.InstanceGuid)) : "";
        return $"{o.InstanceGuid} {o.GetType().Name} {o.NickName} {wires}";
    }).ToList();

    // ------------------------------------------------------------------------------------------------------------------------------------
    // 23 and 46: the GPC data of the generators put in place of the refused 0.3.0 data

    private static void SetPersistent(IGH_Param p, IGH_Goo goo)
    {
        // The persistent tree of the parameter (GH_Structure<T>): cleared, then the datum appended at {0}, the path of the former datum.
        var m = p.GetType().GetProperty("PersistentData")?.GetValue(p) ?? throw new InvalidOperationException("U2: no persistent data on " + p.Name);
        m.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(m, null);
        var append = m.GetType().GetMethods().First(x => x.Name == "Append" && x.GetParameters().Length == 2 && x.GetParameters()[1].ParameterType == typeof(GH_Path)
            && x.GetParameters()[0].ParameterType.IsAssignableFrom(goo.GetType()));
        append.Invoke(m, new object[] { goo, new GH_Path(0) });
        if (Persistent(p).DataCount != 1) throw new InvalidOperationException("U2: the datum was not set on " + p.Name);
        p.ExpireSolution(false);
    }

    private static GH_Component[] Of(GH_Document doc, string type) => doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == type).ToArray();

    private static IGH_Param Input(GH_Component c, string name) => c.Params.Input.Single(p => p.Name == name);

    private static string[] Texts(IGH_Param p) => Persistent(p)?.AllData(true).OfType<GH_String>().Select(s => s.Value).ToArray() ?? Array.Empty<string>();

    /// <summary>23: the audit's model (the two disconnected girders of BridgeUtilityTests.SourceGpc with their stored samples attached, as
    /// BridgeUtilityExamples.AuditPhase); the Phase Model property is kept as read by value.</summary>
    private static List<string> Remake23(GH_Document doc)
    {
        var done = new List<string>();
        var audits = Of(doc, "BridgeModelAuditComponent").Where(c => c.Params.Input[0].SourceCount == 0).ToArray();
        if (audits.Length != 1) throw new InvalidOperationException("U2 23: expected one audit with an internalised model, found " + audits.Length);
        var model = R2bFixtures.Model(R2bFixtures.Input(BridgeUtilityTests.SourceGpc(analyse: false)), BridgeUtilityTests.SourceGpc());
        SetPersistent(audits[0].Params.Input[0], model); done.Add($"{audits[0].InstanceGuid} {audits[0].NickName} / {audits[0].Params.Input[0].Name}: GH_Model of SourceGpc (2 beams, nodes A 2 3 4, cases G Q) with its analysed model");
        // The Phase Model property of 0.3.0 is read by value (a GH_BeamProperty on its GPC datum): kept as saved, written in BGHGPC1. Only a
        // property that cannot be read would take the GPC twin of the generator (LongPropertyGpc of phase 1).
        var phase = Of(doc, "BridgePhaseModelComponent").Single();
        var saved = Persistent(phase.Params.Input[3])?.AllData(true).OfType<IGH_Goo>().ToArray() ?? Array.Empty<IGH_Goo>();
        if (saved.Length != 1 || !saved[0].IsValid)
        {
            SetPersistent(phase.Params.Input[3], R2aDesignFixtures.Property(BridgeGrillageExample.LongPropertyGpc(new(), 1)));
            done.Add($"{phase.InstanceGuid} {phase.NickName} / {phase.Params.Input[3].Name}: GH_BeamProperty of LongPropertyGpc(phase 1)");
        }
        return done;
    }

    /// <summary>46: the four sources of BridgeCombinationExcelTests.RecipeGpc (two parallel beams with end nodes, no rigid link), each Bridge
    /// Action by its Action ID (G1, G2, Q, RITIRO of the catalogue), the two Excel components and Build BridGHe Model the first source, as
    /// BridgeCombinationExcelExample.</summary>
    private static List<string> Remake46(GH_Document doc)
    {
        var done = new List<string>();
        var (references, phases) = BridgeCombinationExcelTests.RecipeGpc();
        var catalog = BridgeCombinationCatalog.FromGpc(references.Select(r => R2bFixtures.Input(r.Input)).ToArray(), phases);
        var actions = Of(doc, "BridgeActionComponent").Concat(Of(doc, "BridgeShrinkageActionComponent")).ToArray();
        if (actions.Length != 4) throw new InvalidOperationException("U2 46: expected 4 actions, found " + actions.Length);
        foreach (var a in actions)
        {
            string id = Texts(a.Params.Input[0]).Single();
            int i = Array.IndexOf(catalog.ActionIds.ToArray(), id);
            if (i < 0) throw new InvalidOperationException("U2 46: an action of unknown ID " + id);
            SetPersistent(a.Params.Input[1], R2bFixtures.Model(references[i])); done.Add($"{a.InstanceGuid} {a.NickName} ({id}) / {a.Params.Input[1].Name}: GH_Model of RecipeGpc source {i}");
        }
        foreach (var c in Of(doc, "BridgeCombinationExcelComponent").Concat(Of(doc, "BuildBridgeModel")))
        {
            if (c.Params.Input[0].SourceCount != 0) throw new InvalidOperationException("U2 46: a wired model input on " + c.NickName);
            SetPersistent(c.Params.Input[0], R2bFixtures.Model(references[0])); done.Add($"{c.InstanceGuid} {c.NickName} / {c.Params.Input[0].Name}: GH_Model of RecipeGpc source 0");
        }
        if (done.Count != 7) throw new InvalidOperationException("U2 46: 7 internalised models expected, " + done.Count + " set");
        return done;
    }

    // ------------------------------------------------------------------------------------------------------------------------------------
    // The text of the former 0.3.0 payloads (by value, no FEM class), for the list

    private static string Dump(LegacyValue v, int depth = 0)
    {
        var sb = new StringBuilder(); string pad = new string(' ', depth * 2);
        if (v.IsNull) return "null";
        if (v.IsCollection) { sb.Append(v.Type?.Split(',')[0] + "[" + v.Items.Count + "]"); foreach (var i in v.Items) sb.Append("\n" + pad + "  - " + Dump(i, depth + 2)); return sb.ToString(); }
        if (v.Text != null) return v.Text;
        sb.Append(v.ShortType);
        foreach (var f in v.Fields) sb.Append("\n" + pad + "  " + f.Key + " = " + Dump(f.Value, depth + 1));
        return sb.ToString();
    }

    private static void DumpLegacy(string file, string folder)
    {
        var archive = new GH_IO.Serialization.GH_Archive(); if (!archive.ReadFromFile(file)) throw new IOException("U2: cannot read " + file);
        string label = Path.GetFileNameWithoutExtension(file); int n = 0;
        void Walk(GH_IO.Serialization.GH_IChunk chunk, string path)
        {
            foreach (var item in chunk.Items)
            {
                if (item.Type != GH_IO.Types.GH_Types.gh_bytearray || !LegacyArchive.IsLegacy(item._bytearray)) continue;
                n++; var root = LegacyArchive.Read(item._bytearray); string refusal = "";
                if (root.ShortType == "ModelModel") { try { GpcLegacyPayloads.Model(item._bytearray, 1e-3, out _); refusal = "read by value"; } catch (Exception e) { refusal = "refused by value: " + e.GetType().Name + ": " + e.Message; } }
                File.WriteAllText(Path.Combine(folder, $"{label}-legacy-{n:00}.txt"), path + "/" + item.Name + "\n" + item._bytearray.Length + " bytes; " + refusal + "\n" + Dump(root));
            }
            foreach (var c in chunk.Chunks) Walk(c, path + "/" + c.Name + (c.Index > 0 ? "[" + c.Index + "]" : ""));
        }
        Walk(archive.GetRootNode, "");
    }

    internal static int Run(string[] args)
    {
        BridgeExampleMigration.Initialize(); // the Grasshopper component libraries of the examples (no «missing objects» dialog)
        bool write = args.Contains("write"), dump = args.Contains("dump");
        string root = Path.GetFullPath(ExamplePaths.Root);
        if (write && root.StartsWith(@"C:\Users\g.pacini\Documents\Repository\BridGHe\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Never writes into the main checkout.");
        string output = write ? root : Path.Combine(Path.GetTempPath(), "bridghe-u2-remake");
        string dumps = Path.Combine(Path.GetTempPath(), "bridghe-u2-dump"); if (dump) Directory.CreateDirectory(dumps);
        int failures = 0;
        foreach (var relative in Files)
        {
            string path = Path.Combine(root, relative), target = Path.Combine(output, relative);
            string id = relative.Substring(0, 2), name = Path.GetFileNameWithoutExtension(relative);
            long before = new FileInfo(path).Length; byte[] original = File.ReadAllBytes(path);
            if (dump) DumpLegacy(path, dumps);
            List<string> objects, inventory, set = new List<string>(), again, objectsAgain;
            using (var doc = GrasshopperBridgeExample.Read(path))
            {
                doc.FilePath = path;
                objects = Objects(doc);
                var read = Inventory(doc);
                if (id == "23") set = Remake23(doc); else if (id == "46") set = Remake46(doc);
                inventory = Inventory(doc);
                if (dump) File.WriteAllLines(Path.Combine(dumps, name + "-inventory.txt"), new[] { "read:" }.Concat(read).Concat(new[] { "set:" }).Concat(set).Concat(new[] { "written:" }).Concat(inventory));
                if (name == "47-anteprima-grafici")
                {
                    // No BridGHe datum: checked in TEMP only, never rewritten.
                    if (inventory.Count != 0) throw new InvalidOperationException("U2: 47-anteprima-grafici holds BridGHe data");
                    target = Path.Combine(Path.GetTempPath(), "bridghe-u2-remake", relative);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                GrasshopperBridgeExample.Write(doc, target);
            }
            using (var doc = GrasshopperBridgeExample.Read(target)) { again = Inventory(doc); objectsAgain = Objects(doc); }
            string text = File.ReadAllText(target);
            int femm = text.Split("\"FemmR6\"").Length - 1, legacy = 0;
            {
                var archive = new GH_IO.Serialization.GH_Archive(); archive.ReadFromFile(target);
                void Walk(GH_IO.Serialization.GH_IChunk chunk) { foreach (var item in chunk.Items) if (item.Type == GH_IO.Types.GH_Types.gh_bytearray && LegacyArchive.IsLegacy(item._bytearray) && item.Name == "Data") legacy++; foreach (var c in chunk.Chunks) Walk(c); }
                Walk(archive.GetRootNode);
            }
            bool sameObjects = objects.SequenceEqual(objectsAgain), sameData = inventory.SequenceEqual(again);
            bool unchanged = write || name == "47-anteprima-grafici" ? File.ReadAllBytes(path).SequenceEqual(original) || write : true;
            bool ok = femm == 0 && sameObjects && sameData && inventory.All(l => l.Contains(" valid True")) && (!write || name == "47-anteprima-grafici" ? File.ReadAllBytes(path).SequenceEqual(original) : true);
            // A former 0.3.0 Data chunk that stays BGHA1 is allowed only for the non-FEM application payloads (bridge geometry): listed.
            if (!ok) failures++;
            Console.WriteLine($"U2 REMAKE {(ok ? "OK" : "FAIL")} {relative}: {before} -> {new FileInfo(target).Length} bytes, FemmR6 {femm}, BGHA1 Data chunks {legacy}; {objects.Count} objects "
                + $"({(sameObjects ? "the same GUIDs, types, nicknames and wires" : "DIFFERENT")}), {inventory.Count} internalised BridGHe data ({set.Count} rebuilt with the GPC builders), read again: {(sameData ? "the same" : "DIFFERENT")}"
                + (name == "47-anteprima-grafici" ? "; no BridGHe datum: not rewritten" : write ? "" : "; written to " + target));
            foreach (var l in set) Console.WriteLine("  rebuilt: " + l);
            if (!sameData) foreach (var l in inventory.Except(again).Concat(again.Except(inventory))) Console.WriteLine("  data: " + l);
        }
        Console.WriteLine(failures == 0 ? $"U2 REMAKE PASS: {Files.Length} definitions{(write ? " (written in the worktree)" : " (dry run, " + output + ")")}" : $"U2 REMAKE FAIL: {failures} definitions");
        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------------------------------------------------------------------------
    // --u2-check-examples <old root> <new root>: both copies solved, every output and message compared

    private static void Press(GH_Component c)
    {
        var f = c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic) ?? c.GetType().GetField("run", BindingFlags.Instance | BindingFlags.NonPublic);
        f.SetValue(c, true); c.ExpireSolution(false);
    }

    /// <summary>Solves a definition as the regression of the remade examples: the document, then Excel read (mode 1), Build BridGHe Model and
    /// Super Element Check pressed in turn. Never an analysis, a plot, Bridge Report or the Excel write.</summary>
    private static void Solve(GH_Document doc)
    {
        BridgeSegmentExcelTests.Solve(doc, true);
        foreach (var c in Of(doc, "BridgeCombinationExcelComponent"))
            if (c.Params.Input.FirstOrDefault(p => p.Name == "Mode")?.VolatileData.AllData(true).OfType<GH_Integer>().FirstOrDefault()?.Value == 1) { Press(c); BridgeSegmentExcelTests.Solve(doc, true); }
        foreach (var name in new[] { "BuildBridgeModel", "SuperElementCheckComponent" })
            foreach (var c in Of(doc, name)) { Press(c); BridgeSegmentExcelTests.Solve(doc, true); }
    }

    private static string Value(IGH_Goo g)
    {
        if (g == null) return "null";
        if (Bridghe(g) || g is GH_ObjectWrapper) { try { return GpcOnlyReplay.Text(g); } catch (Exception e) { return g.GetType().Name + " text error " + e.GetType().Name; } }
        return g switch
        {
            GH_Number n => "num " + n.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            GH_Integer i => "int " + i.Value,
            GH_String s => "str " + s.Value,
            GH_Boolean b => "bool " + b.Value,
            _ => g.GetType().Name + " " + g.ToString(),
        };
    }

    /// <summary>The outputs and messages of every component, by instance GUID: «guid nickname | port | count» then the value lines.</summary>
    private static SortedDictionary<string, string[]> Outputs(GH_Document doc)
    {
        var d = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var c in doc.Objects.OfType<GH_Component>())
        {
            string head = c.InstanceGuid + " " + c.GetType().Name + " " + c.NickName;
            var levels = new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark };
            d[head + " | messages"] = levels.SelectMany(l => c.RuntimeMessages(l).Select(m => l + ": " + m)).ToArray();
            for (int o = 0; o < c.Params.Output.Count; o++)
            {
                var tree = c.Params.Output[o].VolatileData;
                var lines = new List<string>();
                for (int b = 0; b < tree.PathCount; b++) foreach (var x in tree.get_Branch(b)) lines.Add(tree.get_Path(b) + " " + Value(x as IGH_Goo));
                d[head + " | " + o + " " + c.Params.Output[o].Name] = lines.ToArray();
            }
        }
        return d;
    }

    /// <summary>The files next to 46 and 48, made again from a solved copy into a folder of the check (never next to the definition): the
    /// Excel template of «1 Crea template» (mode 0) and the Word reports of Bridge Report, each with its File redirected and Overwrite true.
    /// Only for the comparison with the files the examples ship (Template.xlsx, Report_esempio.docx, Report_sintetico.docx).</summary>
    private static void Documents(GH_Document doc, string label, string folder)
    {
        void Set(GH_Component c, string name, IGH_Goo value)
        {
            var p = c.Params.Input.First(x => x.Name == name); p.RemoveAllSources();
            var m = p.GetType().GetProperty("PersistentData").GetValue(p); m.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(m, null);
            m.GetType().GetMethods().First(x => x.Name == "Append" && x.GetParameters().Length == 2 && x.GetParameters()[1].ParameterType == typeof(GH_Path)
                && x.GetParameters()[0].ParameterType.IsAssignableFrom(value.GetType())).Invoke(m, new object[] { value, new GH_Path(0) });
            p.ExpireSolution(false);
        }
        int n = 0;
        foreach (var c in Of(doc, "BridgeCombinationExcelComponent").Where(c => c.Params.Input.First(p => p.Name == "Mode").VolatileData.AllData(true).OfType<GH_Integer>().FirstOrDefault()?.Value != 1))
        { Set(c, "File", new GH_String(Path.Combine(folder, $"{label}-template-{++n}.xlsx"))); Set(c, "Overwrite", new GH_Boolean(true)); Press(c); BridgeSegmentExcelTests.Solve(doc, true); }
        n = 0;
        foreach (var c in Of(doc, "BridgeReportComponent").OrderBy(c => c.NickName, StringComparer.Ordinal).ThenBy(c => c.InstanceGuid))
        { Set(c, "File", new GH_String(Path.Combine(folder, $"{label}-report-{++n}-{c.InstanceGuid:N}.docx"))); Set(c, "Overwrite", new GH_Boolean(true)); Press(c); BridgeSegmentExcelTests.Solve(doc, true);
          foreach (var m in c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(c.RuntimeMessages(GH_RuntimeMessageLevel.Warning))) Console.WriteLine($"U2 DOCUMENTS {label} {c.NickName} {c.InstanceGuid}: {m}"); }
    }

    /// <summary>The internalised BridGHe data of a document by their text (<see cref="GpcOnlyReplay.Text"/>: a model by its GPC input and state,
    /// any other datum by its GPC datum), keyed by owner, parameter and place.</summary>
    private static SortedDictionary<string, string> DataTexts(GH_Document doc)
    {
        var d = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in Params(doc))
        {
            var tree = Persistent(p); if (tree == null) continue;
            var owner = p.Attributes?.GetTopLevel?.DocObject ?? p;
            for (int b = 0; b < tree.PathCount; b++)
            {
                var branch = tree.get_Branch(b);
                for (int i = 0; i < branch.Count; i++)
                    if (branch[i] is IGH_Goo g && Bridghe(g))
                    {
                        string t; try { t = GpcOnlyReplay.Text(g); } catch (Exception e) { t = g.GetType().Name + " text error " + e.GetType().Name + ": " + e.Message; }
                        d[$"{owner.InstanceGuid} {owner.NickName} / {p.Name} {tree.get_Path(b)}[{i}] {g.GetType().Name}"] = t;
                    }
            }
        }
        return d;
    }

    internal static int Check(string[] args)
    {
        BridgeExampleMigration.Initialize(); // the Grasshopper component libraries of the examples (no «missing objects» dialog)
        var r = args.SkipWhile(a => a != "--u2-check-examples").Skip(1).ToArray();
        string oldRoot = Path.GetFullPath(r[0]), newRoot = Path.GetFullPath(r[1]), report = r.Length > 2 && !r[2].StartsWith("--") ? Path.GetFullPath(r[2]) : null;
        string examples = Path.GetFullPath(ExamplePaths.Root);
        var sb = new StringBuilder(); int differentFiles = 0;
        foreach (var relative in Files)
        {
            var results = new List<SortedDictionary<string, string[]>>(); var data = new List<SortedDictionary<string, string>>();
            foreach (var rootPath in new[] { oldRoot, newRoot })
            {
                string path = Path.Combine(rootPath, relative);
                if (!File.Exists(path)) path = Path.Combine(examples, relative); // 47-anteprima-grafici is not rewritten
                using var doc = GrasshopperBridgeExample.Read(path);
                doc.FilePath = Path.Combine(examples, relative); // relative inputs (Excel, templates) resolve in the worktree examples, read only
                data.Add(DataTexts(doc));
                Solve(doc);
                results.Add(Outputs(doc));
                if (report != null) Documents(doc, Path.GetFileNameWithoutExtension(relative) + (rootPath == oldRoot ? "-old" : "-new"), Path.GetDirectoryName(report));
            }
            var (a, b) = (results[0], results[1]);
            if (report != null)
                for (int v = 0; v < 2; v++)
                    File.WriteAllLines(report + "." + Path.GetFileNameWithoutExtension(relative) + (v == 0 ? ".old.txt" : ".new.txt"),
                        results[v].SelectMany(p => new[] { "## " + p.Key }.Concat(p.Value)));
            int equal = 0, guidsOnly = 0; var changed = new List<string>();
            foreach (var k in a.Keys.Union(b.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                a.TryGetValue(k, out var x); b.TryGetValue(k, out var y); x ??= new[] { "(absent)" }; y ??= new[] { "(absent)" };
                if (x.SequenceEqual(y)) { equal++; continue; }
                if (x.Select(GpcDraftCapture.WithoutGuids).SequenceEqual(y.Select(GpcDraftCapture.WithoutGuids))) { guidsOnly++; continue; }
                int first = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => GpcDraftCapture.WithoutGuids(x[i]) != GpcDraftCapture.WithoutGuids(y[i]), Math.Min(x.Length, y.Length));
                string Cut(string[] s) => first < s.Length ? s[first].Replace("\r", "").Replace("\n", " ⏎ ").Substring(0, Math.Min(400, s[first].Replace("\n", " ⏎ ").Length)) : "(end)";
                changed.Add($"  CHANGED {k}: {x.Length} -> {y.Length} values; first difference at {first}: {Cut(x)}  =>  {Cut(y)}");
            }
            if (changed.Count > 0) differentFiles++;
            string line = $"U2 CHECK {relative}: {a.Count} outputs and message lists before, {b.Count} after; {equal} equal, {guidsOnly} equal but for GUIDs, {changed.Count} different";
            Console.WriteLine(line); sb.AppendLine(line); foreach (var c in changed) { Console.WriteLine(c); sb.AppendLine(c); }
            // The internalised data themselves: the same text, the same but for GUIDs, or different (23 and 46: rebuilt, the former refused).
            foreach (var k in data[0].Keys.Union(data[1].Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                data[0].TryGetValue(k, out var x); data[1].TryGetValue(k, out var y);
                string state = x == null ? "absent before (refused on reading)" : y == null ? "absent after" : x == y ? "the same text" : GpcDraftCapture.WithoutGuids(x) == GpcDraftCapture.WithoutGuids(y) ? "the same text but for GUIDs" : "DIFFERENT text";
                string d = $"  DATUM {k}: {state}" + (x != null && y != null && x != y && GpcDraftCapture.WithoutGuids(x) != GpcDraftCapture.WithoutGuids(y) ? "; first difference: " + GpcDraftArchiveChecks.FirstDifference(GpcDraftCapture.WithoutGuids(x), GpcDraftCapture.WithoutGuids(y)).Replace("\n", " ⏎ ") : "");
                Console.WriteLine(d.Length > 600 ? d.Substring(0, 600) : d); sb.AppendLine(d);
            }
        }
        if (report != null) File.WriteAllText(report, sb.ToString());
        Console.WriteLine($"U2 CHECK DONE: {Files.Length} definitions, {differentFiles} with differences (listed above)");
        return 0;
    }
}
