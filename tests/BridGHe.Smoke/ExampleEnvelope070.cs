using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using static ExampleReferenceLoads;

/// <summary>0.7.0 (user decisions of 21/11/2026): the new current definitions of the catalogue with Bridge Moving Envelope between Bridge
/// Moving Combinations (Table) and Build BridGHe Model (Combinations), its Mode from a Value List «Posizioni / Inviluppo / Concomitanti» set
/// to Posizioni (the results of the source revision), its Action prefix from the same source as Bridge Moving Combinations.
/// <c>--example-envelope 50</c> writes 50 REV006 from REV005, <c>--example-envelope 99</c> 99 REV007 from REV006 (never overwritten);
/// <c>--example-envelope add &lt;in.ghx&gt; &lt;out.ghx&gt;</c> does the same on any definition with one Bridge Moving Combinations.</summary>
internal static class ExampleEnvelope070
{
    internal static int Run(string[] args)
    {
        int i = Array.IndexOf(args, "--example-envelope");
        string mode = args.Length > i + 1 ? args[i + 1] : "";
        BridgeExampleMigration.Initialize();
        if (mode == "50")
        {
            string folder = ExampleReferenceLoads50.Folder;
            string source = Path.Combine(folder, "50-ponte-quattro-modelli_REV005.ghx"), target = Path.Combine(folder, "50-ponte-quattro-modelli_REV006.ghx");
            Require(!File.Exists(target), "REV006 already exists (a revision is never overwritten).");
            string bookFrom = Path.Combine(folder, "Sezioni_conci_REV005.xlsx"), bookTo = Path.Combine(folder, "Sezioni_conci_REV006.xlsx");
            if (!File.Exists(bookTo)) File.Copy(bookFrom, bookTo); else Require(File.ReadAllBytes(bookFrom).SequenceEqual(File.ReadAllBytes(bookTo)), "Sezioni_conci_REV006.xlsx differs from REV005");
            var report = Add(source, target, "REV005", "REV006", fifty: true);
            File.WriteAllText(Path.Combine(folder, "REV006-validation.json"), JsonSerializer.Serialize(report, Options));
            Console.WriteLine("REV006 GENERATED: " + target);
            return 0;
        }
        if (mode == "99")
        {
            string folder = ExampleReferenceLoads99.Folder;
            string source = Path.Combine(folder, "REV006", "flusso-acciaio-misto_REV006.ghx"), target = Path.Combine(folder, "REV007", "flusso-acciaio-misto_REV007.ghx");
            Require(!File.Exists(target), "REV007 already exists (a revision is never overwritten).");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            foreach (var (from, to) in new[] { ("Template_BridGHe.docx", "Template_BridGHe.docx"), ("Combinazioni_REV006.xlsx", "Combinazioni_REV007.xlsx") })
            {
                string a = Path.Combine(folder, "REV006", from), b = Path.Combine(folder, "REV007", to);
                if (!File.Exists(b)) File.Copy(a, b); else Require(File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)), to + " differs from REV006");
            }
            var report = Add(source, target, "REV006", "REV007", fifty: false);
            File.WriteAllText(Path.Combine(folder, "REV007", "validation-envelope.json"), JsonSerializer.Serialize(report, Options));
            Console.WriteLine("REV007 GENERATED: " + target);
            return 0;
        }
        if (mode == "add" && args.Length > i + 3)
        {
            int selected = args.Length > i + 4 && int.TryParse(args[i + 4], out int s) ? s : 0; // optional: the mode selected in the Value List (measurements)
            var report = Add(Path.GetFullPath(args[i + 2]), Path.GetFullPath(args[i + 3]), null, null, fifty: false, selected);
            Console.WriteLine("ENVELOPE 0.7.0: " + args[i + 3] + " — " + JsonSerializer.Serialize(report, Options));
            return 0;
        }
        if (mode == "revision" && args.Length > i + 6)
        {
            // 0.7.1 (user 21/11/2026: «aggiorna anche i file del pescara e del brecciarola»): a new revision of a project definition with Bridge
            // Moving Envelope, the Value List on the given mode, the Straus output paths and the report name moved from the old revision tag to the new one
            // (only panels holding a .st7 or .docx name), and a note panel next to the Value List. The source is only read; the target is never overwritten.
            string source = Path.GetFullPath(args[i + 2]), target = Path.GetFullPath(args[i + 3]); string oldTag = args[i + 5], newTag = args[i + 6];
            Require(int.TryParse(args[i + 4], out int selected) && selected is >= 0 and <= 2, "mode 0 Posizioni, 1 Inviluppo or 2 Concomitanti");
            Require(!File.Exists(target), "the target already exists (a revision is never overwritten): " + target);
            Require(oldTag.Length > 0 && newTag.Length > 0 && oldTag != newTag, "two different revision tags");
            var moved = new List<string>();
            void Edit(GH_Document doc)
            {
                foreach (var p in doc.Objects.OfType<GH_Panel>().Where(p => p.UserText != null && !p.UserText.Contains('\n')
                    && (p.UserText.EndsWith(".st7", StringComparison.OrdinalIgnoreCase) || p.UserText.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) && p.UserText.Contains(oldTag)))
                { moved.Add(p.UserText + " -> " + p.UserText.Replace(oldTag, newTag)); p.UserText = p.UserText.Replace(oldTag, newTag); }
                Require(moved.Count > 0, $"no output path or report name holds {oldTag}");
            }
            string name = new[] { "Posizioni", "Inviluppo", "Concomitanti" }[selected];
            string note = $"MOBILE {newTag} (BridGHe 0.7.1): {newTag} = {oldTag} con Bridge Moving Envelope fra la tabella del mobile e Build BridGHe Model; Value List su {name}.\n"
                + "Concomitanti: in ogni stazione le posizioni del massimo e del minimo di N, V e M, ciascuna con tutte le sue sollecitazioni (6 combinazioni per combinazione base). Per il dimensionamento; non garantito a favore di sicurezza per l'interazione N–M.\n"
                + "Inviluppo: in ogni stazione gli 8 angoli N±, V±, M± fra le posizioni, sempre a favore di sicurezza.\n"
                + $"Posizioni: una combinazione per posizione, la verifica più precisa (i risultati di {oldTag}).\n"
                + "Per cambiare scelta: Value List, poi Run su Build BridGHe Model e sui check.";
            var report = Add(source, target, null, null, fifty: false, selected, Edit, note);
            Console.WriteLine("ENVELOPE 0.7.1: " + target + " — " + JsonSerializer.Serialize(new { Report = report, Moved = moved }, Options));
            return 0;
        }
        Console.Error.WriteLine("usage: --example-envelope 50 | 99 | add <in.ghx> <out.ghx> [mode] | revision <in.ghx> <out.ghx> <mode> <old tag> <new tag>");
        return 2;
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The Value List of the mode: Posizioni 0 (selected), Inviluppo 1, Concomitanti 2.</summary>
    internal static GH_ValueList ModeList(GH_Document doc, PointF at, int selected = 0)
    {
        var list = new GH_ValueList { ListMode = GH_ValueListMode.DropDown }; list.CreateAttributes();
        list.Name = list.NickName = "Mobile nelle verifiche — Posizioni (verifica) / Inviluppo, Concomitanti (dimensionamento)";
        list.ListItems.Clear();
        foreach (var (name, index) in new[] { ("Posizioni", 0), ("Inviluppo", 1), ("Concomitanti", 2) }) list.ListItems.Add(new GH_ValueListItem(name, index.ToString()) { Selected = index == selected });
        list.Attributes.Pivot = at; doc.AddObject(list, false); return list;
    }

    internal static object Add(string source, string target, string oldRevision, string newRevision, bool fifty, int selected = 0,
        Action<GH_Document> edit = null, string note = null)
    {
        byte[] original = File.ReadAllBytes(source);
        using var doc = GrasshopperBridgeExample.Read(source); doc.FilePath = target;
        var cv = new Canvas(doc);
        edit?.Invoke(doc);
        if (oldRevision != null)
        {
            foreach (var p in doc.Objects.OfType<GH_Panel>()) p.UserText = p.UserText.Replace("Sezioni_conci_" + oldRevision + ".xlsx", "Sezioni_conci_" + newRevision + ".xlsx").Replace("Report_50_" + oldRevision, "Report_50_" + newRevision)
                .Replace("Combinazioni_" + oldRevision + ".xlsx", "Combinazioni_" + newRevision + ".xlsx").Replace("\\" + oldRevision + "\\", "\\" + newRevision + "\\");
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis"))
                foreach (var p in c.Params.Input.Where(p => p.Name is "Output Path" or "Output path").SelectMany(p => p.Sources).OfType<GH_Panel>()) p.UserText = p.UserText.Replace(oldRevision, newRevision);
            foreach (var procedure in doc.Objects.OfType<GH_Panel>().Where(p => p.NickName == "PROCEDURA " + oldRevision + " E IPOTESI").ToArray())
            {
                procedure.NickName = procedure.Name = "PROCEDURA " + newRevision + " E IPOTESI";
                procedure.UserText = procedure.UserText.Replace(oldRevision, newRevision) + "\nMOBILE " + newRevision + " (0.7.0): Bridge Moving Envelope fra la tabella del mobile e Build BridGHe Model. "
                    + "Posizioni (scelta di partenza): una combinazione per posizione, la verifica più precisa. Inviluppo: in ogni stazione gli 8 angoli di N, V, M fra le posizioni, sempre a favore di sicurezza, per il dimensionamento. "
                    + "Concomitanti: il massimo e il minimo di N, V e M con le sollecitazioni della stessa posizione (6 casi).";
            }
        }
        var moving = doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Bridge Moving Combinations").ToArray();
        Require(moving.Length == 1, $"{moving.Length} Bridge Moving Combinations in the document: one expected");
        var mc = moving[0]; var table = Out(mc, "Table");
        var builds = doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Build BridGHe Model" && In(c, "Combinations").Sources.Contains(table)).ToArray();
        Require(builds.Length > 0, "no Build BridGHe Model reads the table of Bridge Moving Combinations");
        Require(!doc.Objects.OfType<GH_Component>().Any(c => c.Name == "Bridge Moving Envelope"), "Bridge Moving Envelope already in the definition");
        cv.At(mc);
        var envelope = cv.Bridge("BridgeMovingEnvelopeComponent", mc.NickName + " | mobile nelle verifiche");
        envelope.Attributes.Pivot = new PointF(mc.Attributes.Pivot.X + 260, mc.Attributes.Pivot.Y + 40);
        Wire(In(envelope, "Table"), table);
        var prefix = In(mc, "Action prefix").Sources.ToArray();
        if (prefix.Length > 0) Wire(In(envelope, "Action prefix"), prefix);
        var list = ModeList(doc, new PointF(envelope.Attributes.Pivot.X - 280, envelope.Attributes.Pivot.Y + 60), selected);
        Wire(In(envelope, "Mode"), list);
        foreach (var build in builds) Replace(In(build, "Combinations"), table, Out(envelope, "Table"));
        var log = new List<string> { $"Bridge Moving Envelope fra «{mc.NickName}» e {builds.Length} Build BridGHe Model; modo da Value List: {list.FirstSelectedItem.Name}" };
        if (note != null)
        {
            var panel = new GH_Panel(); panel.CreateAttributes();
            panel.Name = panel.NickName = "Mobile nelle verifiche — nota"; panel.UserText = note;
            panel.Attributes.Pivot = new PointF(list.Attributes.Pivot.X, list.Attributes.Pivot.Y + 50);
            panel.Attributes.Bounds = new RectangleF(panel.Attributes.Pivot, new SizeF(560, 150));
            doc.AddObject(panel, false); log.Add("pannello di nota accanto alla Value List");
        }
        cv.ExposeDefaults();
        var restore = Neutralize(doc, "envelope-070");
        BridgeSegmentExcelTests.Solve(doc, true);
        var errors = BridgeMovingRevisionExample.Errors(doc);
        if (errors.Length > 0) Console.WriteLine("ENVELOPE 0.7.0: offline errors (analyses not run in the generator):\n" + errors);
        GH_Document.EnableSolutions = false; restore();
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis")) BridgeMovingRevisionExample.Clear(c);
        if (fifty) { Example50HorizontalLayout.Apply(doc); ExampleTidy.Straighten(doc); }
        GH_Document.EnableSolutions = false; GrasshopperBridgeExample.Write(doc, target);
        Require(original.SequenceEqual(File.ReadAllBytes(source)), "the source definition changed");
        return new { Generator = "--example-envelope", Source = Path.GetFileName(source), SourceSHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)),
            Definition = Path.GetFileName(target), DefinitionSHA256 = Sha(target), Changes = log, NativeSolverInvoked = false };
    }
}
