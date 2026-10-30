using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

/// <summary>
/// Tidies a saved example without solving it: removes the objects from which no result can be reached and lays the
/// graph out from left to right. Only removals and positions change; the XML of every other object is kept, caches
/// and persistent data included. Usage: --tidy-example 50|99 [--capture] [--publish].
/// </summary>
internal static class ExampleTidy
{
    private sealed record Case(string File, string Validation, Func<GH_Document, Example50HorizontalLayout.Stage[]> Arrange, string[] Duplicates);
    private static readonly Dictionary<string, Case> Cases = new()
    {
        // REV003 has two identical Excel blocks: the reader 14b78d6a feeds the section table, the other template and
        // the second copy of the Excel note are left over (the unused reader is found by the reachability rule).
        ["50"] = new("50-ponte-quattro-modelli/50-ponte-quattro-modelli_REV003.ghx", "REV003-tidy-validation.json", Example50HorizontalLayout.Apply,
            new[] { "cdbd7c6f-5758-4bb2-81cd-c800b402df38", "60ca0140-1c40-4d96-8d6d-b1e46b0b8dd7" }),
        // The personal REV004 takes the eleven stages of example 50; its user groups are replaced by the stage groups.
        ["99"] = new("99-Ponte completo GPC/REV004/flusso-acciaio-misto_REV004.ghx", "tidy-validation.json", ArrangeModels, Array.Empty<string>())
    };
    private static readonly string[] ModelStages = {
        "01 | GEOMETRIA — appoggi, travi, soletta, conci, diaframmi e mesh",
        "02 | MATERIALI E SEZIONI — acciaio, calcestruzzo, sezioni e proprietà",
        "03 | MODELLO FEM COMUNE — famiglie, aste, link e appoggi",
        null, null, null, null,
        "08 | FASI E ASSEMBLAGGIO — azioni, fasi, combinazioni e modello BridGHe",
        "09 | VERIFICHE — sezioni, pioli, irrigiditori e riepilogo",
        "10 | GRAFICI — diagrammi e anteprime Grasshopper",
        "11 | REPORT — capitoli, template Word e generazione"
    };

    /// <summary>
    /// Stages of a four-model bridge whose components are not named M01–M04: a component wired, upstream or downstream,
    /// to a single Straus analysis belongs to that model. The models are named and ordered by their output file.
    /// </summary>
    private static Example50HorizontalLayout.Stage[] ArrangeModels(GH_Document doc)
    {
        var analyses = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "StrausR3AnalysisComponent").ToArray();
        if (analyses.Length != 4) throw new InvalidOperationException("Four Straus analyses expected, found " + analyses.Length + ".");
        string Name(GH_Component a)
        {
            string path = a.Params.Input.Single(p => p.Name == "Output Path").Sources.Select(s => s is GH_Panel panel ? panel.UserText
                : s is Grasshopper.Kernel.Parameters.Param_String text ? text.PersistentData.AllData(true).OfType<Grasshopper.Kernel.Types.GH_String>().FirstOrDefault()?.Value : null)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            return path == null ? a.NickName : Path.GetFileNameWithoutExtension(path.Trim());
        }
        var models = analyses.Select(a => (Analysis: a, Name: Name(a))).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
        var wires = Wires(doc.Objects.Where(o => o is IGH_Component || o is IGH_Param));
        var up = wires.ToLookup(w => w.To, w => w.From);
        var down = wires.ToLookup(w => w.From, w => w.To);
        HashSet<IGH_DocumentObject> Reach(IGH_DocumentObject start, ILookup<IGH_DocumentObject, IGH_DocumentObject> next)
        {
            var seen = new HashSet<IGH_DocumentObject> { start };
            var queue = new Queue<IGH_DocumentObject>(seen);
            while (queue.Count > 0) foreach (var o in next[queue.Dequeue()]) if (seen.Add(o)) queue.Enqueue(o);
            return seen;
        }
        var members = models.Select(m => Reach(m.Analysis, up).Concat(Reach(m.Analysis, down)).ToHashSet()).ToArray();
        int PhaseOf(GH_Component c)
        {
            var k = Enumerable.Range(0, models.Length).Where(i => members[i].Contains(c)).ToArray();
            return k.Length == 1 ? k[0] : -1;
        }
        var titles = (string[])ModelStages.Clone();
        for (int i = 0; i < models.Length; i++)
        {
            titles[3 + i] = $"{4 + i:00} | MODELLO {i} · {models[i].Name} — proprietà → carichi → FEM → analisi Straus";
            Console.WriteLine($"TIDY model {i}: {models[i].Name}, {doc.Objects.OfType<GH_Component>().Count(c => PhaseOf(c) == i)} components");
        }
        return Example50HorizontalLayout.Apply(doc, titles, PhaseOf);
    }
    // End points: what is looked at on the canvas or written to a file.
    private static readonly HashSet<string> Terminals = new() { "Preview BridGHe Model", "Bridge Plot Preview", "Bridge Report", "Bridge Result Labels",
        "Bridge Combination Excel", "Bridge Section Excel Template", "BridGHe Panel", "Bridge Check Summary", "Bridge Model Audit" };

    private static IGH_DocumentObject Owner(IGH_Param p) => p.Attributes.GetTopLevel.DocObject;
    private static IEnumerable<IGH_Param> Inputs(IGH_DocumentObject o) =>
        o is IGH_Component c ? c.Params.Input : o is IGH_Param p ? new[] { p } : Enumerable.Empty<IGH_Param>();
    private static (IGH_DocumentObject From, IGH_DocumentObject To)[] Wires(IEnumerable<IGH_DocumentObject> objects) =>
        objects.SelectMany(o => Inputs(o).SelectMany(i => i.Sources).Select(s => (From: Owner(s), To: o))).Where(w => w.From != w.To).ToArray();
    private static bool Backward((IGH_DocumentObject From, IGH_DocumentObject To) w) => w.From.Attributes.Bounds.Right >= w.To.Attributes.Bounds.Left;
    private static IGH_DocumentObject[] Functional(GH_Document doc) => doc.Objects.Where(o => o is not GH_Group && !o.Name.StartsWith("BGH50_LAYOUT_")).ToArray();
    private static void Layout(IEnumerable<IGH_DocumentObject> objects) { foreach (var o in objects) { o.Attributes.ExpireLayout(); o.Attributes.PerformLayout(); } }

    /// <summary>Objects that reach no end point through the wires: unused inputs and branches whose results go nowhere.</summary>
    private static IGH_DocumentObject[] Unreachable(GH_Document doc)
    {
        var objects = doc.Objects.Where(o => o is IGH_Component || o is IGH_Param).ToArray();
        var wires = Wires(objects);
        var targets = wires.ToLookup(w => w.From, w => w.To);
        var sources = wires.ToLookup(w => w.To, w => w.From);
        // A floating parameter fed by something shows a result; a panel without wires is a note.
        bool End(IGH_DocumentObject o) => o is IGH_Component c ? Terminals.Contains(c.Name)
            : ((IGH_Param)o).SourceCount > 0 || o is GH_Panel && !targets[o].Any();
        var reached = new HashSet<IGH_DocumentObject>(objects.Where(End));
        var queue = new Queue<IGH_DocumentObject>(reached);
        while (queue.Count > 0) foreach (var s in sources[queue.Dequeue()]) if (reached.Add(s)) queue.Enqueue(s);
        return objects.Where(o => !reached.Contains(o)).ToArray();
    }

    /// <summary>Removes objects from the XML; groups lose those members and disappear when empty.</summary>
    private static XDocument Without(XDocument original, ISet<string> ids)
    {
        var result = new XDocument(original);
        var list = Example50HorizontalLayout.ObjectList(result);
        list.Elements("chunk").Where(o => ids.Contains(Example50HorizontalLayout.Identity(o))).Remove();
        foreach (var group in list.Elements("chunk").Where(o => Example50HorizontalLayout.Item(o, "GUID") == "c552a431-af5b-46a9-a8a4-0fcbc27ef596").ToArray())
        {
            var items = Example50HorizontalLayout.Container(group).Element("items");
            var members = items.Elements("item").Where(i => (string)i.Attribute("name") == "ID").ToArray();
            var kept = members.Where(i => !ids.Contains(i.Value)).ToArray();
            if (kept.Length == members.Length) continue;
            if (kept.Length == 0) { group.Remove(); continue; }
            foreach (var member in members.Except(kept)) member.Remove();
            for (int i = 0; i < kept.Length; i++) kept[i].SetAttributeValue("index", i);
            items.Elements("item").Single(i => (string)i.Attribute("name") == "ID_Count").Value = kept.Length.ToString();
            items.SetAttributeValue("count", items.Elements("item").Count());
        }
        int index = 0;
        foreach (var o in list.Elements("chunk")) o.SetAttributeValue("index", index++);
        list.SetAttributeValue("count", index);
        list.Parent.Element("items").Elements("item").Single(i => (string)i.Attribute("name") == "ObjectCount").Value = index.ToString();
        var removedOutputs = original.Descendants("item").Where(i => (string)i.Attribute("name") == "InstanceGuid" && ids.Contains(Example50HorizontalLayout.Identity(
            i.Ancestors("chunk").First(c => (string)c.Attribute("name") == "Object")))).Select(i => i.Value).ToHashSet();
        if (result.Descendants("item").Any(i => (string)i.Attribute("name") == "Source" && removedOutputs.Contains(i.Value)))
            throw new InvalidOperationException("A kept object reads from a removed one.");
        return result;
    }

    /// <summary>
    /// Wires that still run from right to left (objects stacked in the same column): the input moves left when it has no
    /// sources, otherwise the reader moves right, only where the moved object overlaps nothing.
    /// </summary>
    private static void Straighten(GH_Document doc)
    {
        var objects = Functional(doc);
        bool Free(IGH_DocumentObject moved, RectangleF box) => objects.All(o => o == moved || !RectangleF.Inflate(box, 20, 20).IntersectsWith(o.Attributes.Bounds));
        for (int pass = 0; pass < 10; pass++)
        {
            bool changed = false;
            foreach (var w in Wires(objects).Where(Backward))
            {
                var from = w.From.Attributes.Bounds; var to = w.To.Attributes.Bounds;
                float shift = from.Right - to.Left + 80;
                var left = new RectangleF(from.X - shift, from.Y, from.Width, from.Height);
                var right = new RectangleF(to.X + shift, to.Y, to.Width, to.Height);
                bool source = !Inputs(w.From).Any(i => i.SourceCount > 0);
                if (source && left.X >= 0 && Free(w.From, left)) Example50HorizontalLayout.Move(w.From, left.X, left.Y);
                else if (Free(w.To, right) && Wires(objects).Where(x => x.From == w.To).All(x => right.Right + 40 < x.To.Attributes.Bounds.Left)) Example50HorizontalLayout.Move(w.To, right.X, right.Y);
                else continue;
                changed = true;
            }
            if (!changed) break;
        }
    }

    internal static int Run(string name, bool publish, bool capture)
    {
        if (name == null || !Cases.TryGetValue(name, out var tidy)) throw new ArgumentException("--tidy-example 50|99");
        BridgeExampleMigration.Initialize();
        string path = Path.Combine(ExamplePaths.Root, tidy.File), artifact = Path.GetFullPath(Path.Combine("artifacts", "tidy-examples", name));
        Directory.CreateDirectory(artifact);
        var original = XDocument.Load(path);
        IGH_DocumentObject[] removed; int componentsBefore, objectsBefore, wiresBefore, backwardBefore, wiresRemoved;
        using (var doc = GrasshopperBridgeExample.Read(path))
        {
            var functional = Functional(doc); Layout(functional);
            componentsBefore = doc.Objects.OfType<GH_Component>().Count(); objectsBefore = functional.Length;
            var wires = Wires(functional); wiresBefore = wires.Length; backwardBefore = wires.Count(Backward);
            var duplicates = tidy.Duplicates.Select(id => doc.FindObject(new Guid(id), true) ?? throw new InvalidOperationException("Missing duplicate " + id)).ToArray();
            removed = Unreachable(doc).Concat(duplicates).Distinct().ToArray();
            wiresRemoved = wires.Count(w => removed.Contains(w.From) || removed.Contains(w.To));
        }
        var list = removed.Select(o => new { o.Name, o.NickName, Id = o.InstanceGuid.ToString() }).OrderBy(o => o.Name).ThenBy(o => o.NickName).ToArray();
        foreach (var o in list) Console.WriteLine($"TIDY {name}: remove {o.Name} | {o.NickName} | {o.Id}");
        var pruned = Without(original, list.Select(o => o.Id).ToHashSet());
        string prunedPath = Path.Combine(artifact, "pruned.ghx"); pruned.Save(prunedPath);
        string runtime = Path.Combine(artifact, "runtime-layout.ghx");
        using (var doc = GrasshopperBridgeExample.Read(Save(Example50HorizontalLayout.WithoutAnnotations(pruned), Path.Combine(artifact, "layout-input.ghx"))))
        {
            Layout(Functional(doc));
            tidy.Arrange(doc);
            Straighten(doc);
            GrasshopperBridgeExample.Write(doc, runtime);
        }
        var candidate = Example50HorizontalLayout.PatchOnlyAttributes(pruned, XDocument.Load(runtime));
        string fingerprint = Example50HorizontalLayout.Fingerprint(pruned);
        if (Example50HorizontalLayout.Fingerprint(candidate) != fingerprint) throw new InvalidOperationException("The layout changed functional content.");
        string candidatePath = Path.Combine(artifact, "candidate.ghx"); candidate.Save(candidatePath);
        int componentsAfter, objectsAfter, wiresAfter; (IGH_DocumentObject From, IGH_DocumentObject To)[] backward; RectangleF before = default, after;
        using (var doc = GrasshopperBridgeExample.Read(candidatePath))
        {
            var functional = Functional(doc); Layout(functional);
            Example50HorizontalLayout.ValidateBounds(functional);
            componentsAfter = doc.Objects.OfType<GH_Component>().Count(); objectsAfter = functional.Length;
            var wires = Wires(functional); wiresAfter = wires.Length; backward = wires.Where(Backward).ToArray();
            after = Example50HorizontalLayout.Union(functional.Select(o => o.Attributes.Bounds));
        }
        using (var doc = GrasshopperBridgeExample.Read(path)) { var functional = Functional(doc); Layout(functional); before = Example50HorizontalLayout.Union(functional.Select(o => o.Attributes.Bounds)); }
        foreach (var w in backward) Console.WriteLine($"TIDY {name}: right to left {w.From.Name} | {w.From.NickName} -> {w.To.Name} | {w.To.NickName}");
        if (capture)
        {
            // One picture per stage group, or vertical strips of the canvas when the example has no stages.
            using var doc = GrasshopperBridgeExample.Read(candidatePath);
            var functional = Functional(doc); Layout(functional); Layout(doc.Objects.OfType<GH_Group>());
            var stages = doc.Objects.OfType<GH_Group>().Where(g => g.NickName.Length > 3 && char.IsDigit(g.NickName[0]) && g.NickName[2] == ' ' && g.NickName[3] == '|')
                .OrderBy(g => g.NickName).Select(g => new Example50HorizontalLayout.Stage(g.NickName, g.Attributes.Bounds, 0)).ToArray();
            if (stages.Length == 0)
            {
                var all = Example50HorizontalLayout.Union(functional.Select(o => o.Attributes.Bounds));
                int strips = (int)Math.Ceiling(all.Width / 8000);
                stages = Enumerable.Range(0, strips).Select(i => new Example50HorizontalLayout.Stage("strip " + i, new RectangleF(all.X + i * 8000, all.Y, 8000, all.Height), 0)).ToArray();
            }
            string pictures = Path.Combine(artifact, "canvas"); Directory.CreateDirectory(pictures);
            Example50HorizontalLayout.Capture(doc, stages, pictures, Enumerable.Range(0, stages.Length).ToArray());
            Console.WriteLine($"TIDY {name}: {stages.Length} pictures in {pictures}");
        }
        if (componentsAfter != componentsBefore - removed.Count(o => o is GH_Component) || objectsAfter != objectsBefore - removed.Length || wiresAfter != wiresBefore - wiresRemoved)
            throw new InvalidOperationException("Unexpected objects or wires after reopening.");
        Console.WriteLine($"TIDY {name}: {list.Length} objects removed; components {componentsBefore} -> {componentsAfter}, wires {wiresBefore} -> {wiresAfter}, " +
            $"right to left {backwardBefore} -> {backward.Length}; canvas {before.Width:0} x {before.Height:0} -> {after.Width:0} x {after.Height:0}; functional content preserved.");
        if (!publish) { Console.WriteLine("TIDY " + name + ": candidate " + candidatePath + " (use --publish to replace the example)."); return 0; }
        File.Copy(path, Path.Combine(artifact, "before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ghx"));
        File.Copy(candidatePath, path, true);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(path), tidy.Validation), JsonSerializer.Serialize(new {
            Mode = "tidy: unreachable objects removed, left-to-right layout", NativeSolverInvoked = false, Removed = list,
            Components = new { Before = componentsBefore, After = componentsAfter }, Objects = new { Before = objectsBefore, After = objectsAfter },
            Wires = new { Before = wiresBefore, After = wiresAfter }, RightToLeftWires = new { Before = backwardBefore, After = backward.Length },
            Canvas = new { Before = new { before.Width, before.Height }, After = new { after.Width, after.Height } },
            FunctionalContentSha256 = fingerprint, OtherObjectsUnchanged = true, NoOverlaps = true, Reopened = true
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("TIDY " + name + " PUBLISHED: " + path);
        return 0;
    }

    private static string Save(XDocument xml, string path) { xml.Save(path); return path; }
}
