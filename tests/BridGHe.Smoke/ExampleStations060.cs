using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using static ExampleReferenceLoads;

/// <summary>0.6.0 (user decisions of 20/11/2026) on a definition of the template-50 family (the 50 REV004 of the catalogue, or a user model
/// made from it):
/// <list type="bullet">
/// <item>Super Element Check Options in place of the former optional inputs of every Super Element Check (WLI, TFLI, BLI, Options, ULS
/// Combinations): wired to the same sources, read from the archive because the 0.6.0 component no longer has those inputs; analysis stations
/// per element from one slider, 2 = the two nodes;</item>
/// <item>Beam Output Stations after every Bridge FEM Members, from one slider at 5 (the Straus7 output stations of before: the user chooses);</item>
/// <item>every Bridge Detail Check fed directly by a Super Element Check reads the notable sections instead: Bridge Notable Stations (girder
/// supports, segment ends and middles, span sagging maxima, plus the points of the stiffener selectors that read that Detail Check) through
/// a new Bridge Detail Stations.</item>
/// </list>
/// <c>--example-stations 50</c> writes 50 REV005 from REV004 (never overwritten); <c>--example-stations migrate &lt;in.ghx&gt; &lt;out.ghx&gt;</c>
/// does the same on any definition.</summary>
internal static class ExampleStations060
{
    private static readonly Guid SuperElementCheck = new("94839122-32f0-4f3e-a30f-979a53c3eca8");

    internal static int Run(string[] args)
    {
        int i = Array.IndexOf(args, "--example-stations");
        string mode = args.Length > i + 1 ? args[i + 1] : "";
        BridgeExampleMigration.Initialize();
        if (mode == "50")
        {
            string folder = ExampleReferenceLoads50.Folder;
            string source = Path.Combine(folder, "50-ponte-quattro-modelli_REV004.ghx"), target = Path.Combine(folder, "50-ponte-quattro-modelli_REV005.ghx");
            Require(!File.Exists(target), "REV005 already exists (a revision is never overwritten).");
            string bookFrom = Path.Combine(folder, "Sezioni_conci_REV004.xlsx"), bookTo = Path.Combine(folder, "Sezioni_conci_REV005.xlsx");
            if (!File.Exists(bookTo)) File.Copy(bookFrom, bookTo); else Require(File.ReadAllBytes(bookFrom).SequenceEqual(File.ReadAllBytes(bookTo)), "Sezioni_conci_REV005.xlsx differs from REV004");
            var report = Migrate(source, target, "REV004", "REV005", true);
            File.WriteAllText(Path.Combine(folder, "REV005-validation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Console.WriteLine("REV005 GENERATED: " + target);
            return 0;
        }
        if (mode == "99")
        {
            string folder = ExampleReferenceLoads99.Folder;
            string source = Path.Combine(folder, "REV005", "flusso-acciaio-misto_REV005.ghx"), target = Path.Combine(folder, "REV006", "flusso-acciaio-misto_REV006.ghx");
            Require(!File.Exists(target), "REV006 already exists (a revision is never overwritten).");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            foreach (var (from, to) in new[] { ("Template_BridGHe.docx", "Template_BridGHe.docx"), ("Combinazioni_REV005.xlsx", "Combinazioni_REV006.xlsx") })
            {
                string a = Path.Combine(folder, "REV005", from), b = Path.Combine(folder, "REV006", to);
                if (!File.Exists(b)) File.Copy(a, b); else Require(File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b)), to + " differs from REV005");
            }
            var report = Migrate(source, target, "REV005", "REV006", false, ninetyNine: true);
            File.WriteAllText(Path.Combine(folder, "REV006", "validation-stations.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Console.WriteLine("REV006 GENERATED: " + target);
            return 0;
        }
        if (mode == "errors" && args.Length > i + 2)
        {
            // The offline errors of a definition (no Straus analysis), to compare a revision with its source.
            using var doc = GrasshopperBridgeExample.Read(Path.GetFullPath(args[i + 2]));
            var restore = Neutralize(doc, "stations-060-errors"); BridgeSegmentExcelTests.Solve(doc, true);
            foreach (var read in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeSectionReadComponent")) { BridgeSegmentExcelTests.Press(read); read.ExpireSolution(false); }
            BridgeSegmentExcelTests.Solve(doc, true);
            Console.WriteLine("OFFLINE ERRORS " + Path.GetFileName(args[i + 2]) + ":\n" + BridgeMovingRevisionExample.Errors(doc));
            GH_Document.EnableSolutions = false; restore();
            return 0;
        }
        if ((mode == "migrate" || mode == "migrate-wiring") && args.Length > i + 3)
        {
            var report = Migrate(Path.GetFullPath(args[i + 2]), Path.GetFullPath(args[i + 3]), null, null, false, wiringOnly: mode == "migrate-wiring");
            Console.WriteLine("MIGRATED 0.6.0: " + args[i + 3] + " — " + JsonSerializer.Serialize(report));
            return 0;
        }
        if (mode == "catalog")
        {
            // The definitions of the catalogue with a Super Element Check, in place: only the wiring (Super Element Check Options with the
            // former sources and all the stations), so that their results stay those of 0.5.0 bit for bit.
            var root = ExamplePaths.Root; var log = new List<object>();
            using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "catalog.json")));
            foreach (var row in catalog.RootElement.EnumerateArray())
            {
                var names = new List<string>();
                if (row.TryGetProperty("definition", out var d) && d.ValueKind == JsonValueKind.String) names.Add(d.GetString());
                if (row.TryGetProperty("additionalDefinitions", out var more)) names.AddRange(more.EnumerateArray().Select(x => x.GetString()));
                foreach (var name in names)
                {
                    string path = Path.GetFullPath(Path.Combine(root, name));
                    if (!File.ReadAllText(path).Contains(SuperElementCheck.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                    string temp = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".migrazione-060.ghx");
                    if (File.Exists(temp)) File.Delete(temp);
                    var report = Migrate(path, temp, null, null, false, wiringOnly: true);
                    File.Copy(temp, path, true); File.Delete(temp);
                    log.Add(report); Console.WriteLine("MIGRATED 0.6.0 (wiring): " + name);
                }
            }
            File.WriteAllText(Path.Combine(root, "migrazione-060.json"), JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return 0;
        }
        Console.Error.WriteLine("usage: --example-stations 50 | --example-stations migrate <in.ghx> <out.ghx>");
        return 2;
    }

    /// <summary>The former inputs of a Super Element Check in the archive: by input index, the source instance GUIDs and an internal boolean.</summary>
    private sealed record OldInputs(Guid Instance, Dictionary<int, Guid[]> Sources, Dictionary<int, bool> Booleans);

    private static List<OldInputs> ReadOldInputs(string path)
    {
        var archive = new GH_Archive(); Require(archive.ReadFromFile(path), "cannot read " + path);
        var objects = archive.GetRootNode.FindChunk("Definition").FindChunk("DefinitionObjects");
        var found = new List<OldInputs>();
        foreach (GH_IReader o in objects.Chunks)
        {
            if (!o.ItemExists("GUID") || o.GetGuid("GUID") != SuperElementCheck) continue;
            var c = o.FindChunk("Container");
            var sources = new Dictionary<int, Guid[]>(); var booleans = new Dictionary<int, bool>();
            foreach (GH_IReader p in c.Chunks)
            {
                if (p.Name != "param_input") continue;
                int index = p.Index;
                sources[index] = p.Items.Where(it => it.Name == "Source").Select(it => it._guid).ToArray();
                var data = Descendants(p).FirstOrDefault(ch => ch.Name == "PersistentData");
                if (data != null) { var b = Descendants(data).SelectMany(ch => ch.Items).FirstOrDefault(it => it.Type == GH_IO.Types.GH_Types.gh_bool); if (b != null) booleans[index] = b._bool; }
            }
            found.Add(new OldInputs(c.GetGuid("InstanceGuid"), sources, booleans));
        }
        return found;
    }

    private static IEnumerable<GH_IReader> Descendants(GH_IReader chunk)
    {
        foreach (GH_IReader c in chunk.Chunks) { yield return c; foreach (var d in Descendants(c)) yield return d; }
    }

    internal static object Migrate(string source, string target, string oldRevision, string newRevision, bool catalogue, bool ninetyNine = false, bool wiringOnly = false)
    {
        byte[] original = File.ReadAllBytes(source);
        var old = ReadOldInputs(source);
        using var doc = GrasshopperBridgeExample.Read(source); doc.FilePath = target;
        var cv = new Canvas(doc);
        if (oldRevision != null)
        {
            foreach (var p in doc.Objects.OfType<GH_Panel>()) p.UserText = p.UserText.Replace("Sezioni_conci_" + oldRevision + ".xlsx", "Sezioni_conci_" + newRevision + ".xlsx").Replace("Report_50_" + oldRevision, "Report_50_" + newRevision)
                .Replace("Combinazioni_" + oldRevision + ".xlsx", "Combinazioni_" + newRevision + ".xlsx").Replace("\\" + oldRevision + "\\", "\\" + newRevision + "\\");
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis"))
                foreach (var p in c.Params.Input.Where(p => p.Name is "Output Path" or "Output path").SelectMany(p => p.Sources).OfType<GH_Panel>()) p.UserText = p.UserText.Replace(oldRevision, newRevision);
            foreach (var procedure in doc.Objects.OfType<GH_Panel>().Where(p => p.NickName == "PROCEDURA " + oldRevision + " E IPOTESI").ToArray())
            {
                procedure.NickName = procedure.Name = "PROCEDURA " + newRevision + " E IPOTESI";
                procedure.UserText = procedure.UserText.Replace(oldRevision, newRevision) + "\nSTAZIONI " + newRevision + " (0.6.0): Super Element Check su tutta la trave ai 2 nodi di ogni asta (Super Element Check Options); "
                    + "Detail Check solo nelle sezioni notevoli (estremi e metà dei tratti, appoggi, massimi di campata: Bridge Notable Stations); stazioni Straus7 per asta da Beam Output Stations (5 come prima).";
            }
        }
        var log = new List<string>();
        // 1. Super Element Check Options for every Super Element Check, wired to its former sources.
        var checks = doc.Objects.OfType<GH_Component>().Where(c => c.ComponentGuid == SuperElementCheck).ToArray();
        Require(checks.Length > 0 && checks.Length == old.Count, $"{checks.Length} Super Element Check in the document, {old.Count} in the archive");
        // A component read from an older archive keeps the names of its former inputs (Grasshopper reads them back): replace it with a fresh
        // instance, same instance GUID, place and nickname, with its two inputs and all its recipients.
        // Already migrated (0.6.0 inputs, Options from Super Element Check Options): kept, only the stations follow the mode below.
        bool Migrated(GH_Component c) => c.Params.Input.Count == 3 && c.Params.Input[2].Name == "Options";
        var already = checks.Where(Migrated).ToDictionary(c => c.InstanceGuid, c => c.Params.Input[2].Sources.Select(x => x.Attributes?.GetTopLevel?.DocObject).OfType<GH_Component>()
            .FirstOrDefault(o => o.GetType().Name == "SuperElementCheckOptionsComponent"));
        checks = checks.Select(c => Migrated(c) ? c : Fresh(c)).ToArray();
        GH_Component Fresh(GH_Component was)
        {
            var fresh = (GH_Component)Activator.CreateInstance(was.GetType()); fresh.CreateAttributes();
            fresh.NickName = was.NickName; fresh.Attributes.Pivot = was.Attributes.Pivot;
            var inputs = was.Params.Input.Take(2).Select(p => (Sources: p.Sources.ToArray(), p.DataMapping)).ToArray();
            var outputs = was.Params.Output.Select(p => p.Recipients.ToArray()).ToArray();
            var guid = was.InstanceGuid;
            for (int k = 0; k < outputs.Length; k++) foreach (var r in outputs[k]) r.RemoveSource(was.Params.Output[k]);
            doc.RemoveObject(was, false);
            fresh.NewInstanceGuid(guid); doc.AddObject(fresh, false);
            for (int k = 0; k < 2; k++) { Wire(fresh.Params.Input[k], inputs[k].Sources); fresh.Params.Input[k].DataMapping = inputs[k].DataMapping; }
            for (int k = 0; k < outputs.Length && k < fresh.Params.Output.Count; k++) foreach (var r in outputs[k]) r.AddSource(fresh.Params.Output[k]);
            return fresh;
        }
        GH_NumberSlider stations = null;
        foreach (var check in checks)
        {
            if (already.TryGetValue(check.InstanceGuid, out var existing))
            {
                if (existing == null) { log.Add($"{check.NickName}: già 0.6.0, Options non da Super Element Check Options: lasciato com'è"); continue; }
                var slider = existing.Params.Input[5].Sources.OfType<GH_NumberSlider>().FirstOrDefault();
                if (slider != null) slider.SetSliderValue(wiringOnly ? slider.CurrentValue : 2m);
                else { stations ??= cv.Slider("Super Element Check | stazioni di analisi per asta — 2 = i due nodi, 0 = tutte", wiringOnly ? 0 : 2, 0, 10); Wire(existing.Params.Input[5], stations); }
                log.Add($"{check.NickName}: già 0.6.0, stazioni {(wiringOnly ? "invariate" : "a 2 nodi")}");
                continue;
            }
            var was = old.Single(o => o.Instance == check.InstanceGuid);
            cv.At(check);
            var options = cv.Bridge("SuperElementCheckOptionsComponent", check.NickName + " | opzioni");
            stations ??= cv.Slider("Super Element Check | stazioni di analisi per asta — 2 = i due nodi, 0 = tutte", wiringOnly ? 0 : 2, 0, 10);
            // former inputs: 2 WLI, 3 TFLI, 4 BLI, 5 Options (Bridge Analysis Options), 6 ULS Combinations
            foreach (var (from, to) in new[] { (2, 0), (3, 1), (4, 2), (5, 3), (6, 4) })
            {
                var input = options.Params.Input[to];
                var sourceParams = (was.Sources.TryGetValue(from, out var guids) ? guids : Array.Empty<Guid>()).Select(g => doc.FindObject(g, true) as IGH_Param).Where(p => p != null).ToArray();
                if (sourceParams.Length > 0) Wire(input, sourceParams);
                else if (from <= 4 && was.Booleans.TryGetValue(from, out bool b)) { var t = (Grasshopper.Kernel.Parameters.Param_Boolean)input; t.PersistentData.Clear(); t.SetPersistentData(b); }
            }
            Wire(options.Params.Input[5], stations);
            Wire(In(check, "Options"), Out(options, "Options"));
            log.Add($"{check.NickName}: Super Element Check Options (sorgenti di WLI, TFLI, BLI, Options, ULS ricollegate)");
        }
        if (wiringOnly)
        {
            // Only the wiring: no new analysis or check component, no solution, analysis caches and layout untouched.
            GH_Document.EnableSolutions = false; GrasshopperBridgeExample.Write(doc, target);
            Require(original.SequenceEqual(File.ReadAllBytes(source)), "the source definition changed");
            return new { Generator = "--example-stations (wiring)", Source = Path.GetFileName(source), SourceSHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)),
                Definition = Path.GetFileName(source), DefinitionSHA256 = Sha(target), Changes = log };
        }
        // 2. Beam Output Stations after every Bridge FEM Members.
        GH_NumberSlider strausStations = null;
        foreach (var members in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeFemMembersComponent" || c.Name == "Bridge FEM Members").ToArray())
        {
            var beams = Out(members, "Beams");
            var users = doc.Objects.OfType<IGH_DocumentObject>().SelectMany(o => o is GH_Component gc ? gc.Params.Input.Cast<IGH_Param>() : o is IGH_Param ip ? new[] { ip } : Array.Empty<IGH_Param>())
                .Where(p => p.Sources.Contains(beams)).ToArray();
            if (users.Length == 0) continue;
            if (users.All(u => u.Attributes?.GetTopLevel?.DocObject is GH_Component gc && gc.GetType().Name == "BeamOutputStationsComponent")) { log.Add($"{members.NickName}: Beam Output Stations già presente"); continue; }
            cv.At(members);
            var bos = cv.Bridge("BeamOutputStationsComponent", members.NickName + " | stazioni Straus7 per asta");
            strausStations ??= cv.Slider("Beam Output Stations | stazioni Straus7 per asta — 5 come prima", 5, 2, 20);
            Wire(In(bos, "Beam"), beams); Wire(In(bos, "OutputStations"), strausStations);
            foreach (var u in users) Replace(u, beams, Out(bos, "Beam"));
            log.Add($"{members.NickName}: Beam Output Stations su {users.Length} ingressi");
        }
        // 3. Detail Checks fed by a Super Element Check: through the notable sections.
        foreach (var detail in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Bridge Detail Check").ToArray())
        {
            var input = In(detail, "Section result");
            var check = input.Sources.Select(s => s.Attributes?.GetTopLevel?.DocObject).OfType<GH_Component>().FirstOrDefault(c => c.ComponentGuid == SuperElementCheck);
            if (check == null) continue;
            var results = Out(check, "Tension Check");
            var model = In(check, "BridGHe Model").Sources.Single();
            // the points of the stiffener selectors that read this Detail Check, kept as they are
            var selectors = doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Bridge Detail Stations" && In(c, "Results").Sources.Any(s => s.Attributes?.GetTopLevel?.DocObject == detail)).ToArray();
            var extra = selectors.SelectMany(s => In(s, "Locations").Sources).Distinct().ToArray();
            var supports = doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Bridge Grillage Mesh").Select(c => c.Params.Output.FirstOrDefault(p => p.Name == "Support Points")).Where(p => p != null).ToArray();
            cv.At(detail);
            var notable = cv.Bridge("BridgeNotableStationsComponent", detail.NickName + " | sezioni notevoli");
            Wire(In(notable, "Model"), model); Wire(In(notable, "Results"), results);
            if (supports.Length > 0) Wire(In(notable, "Supports"), supports);
            else if (extra.Length > 0) Wire(In(notable, "Supports"), extra);
            if (extra.Length > 0) Wire(In(notable, "Additional"), extra);
            var pick = cv.Bridge("BridgeDetailStationsComponent", detail.NickName + " | stazioni notevoli");
            Wire(In(pick, "Model"), model); Wire(In(pick, "Results"), results); Wire(In(pick, "Locations"), Out(notable, "Points"));
            Wire(In(pick, "Tolerance"), cv.Slider(detail.NickName + " | stazioni notevoli | tolleranza [mm]", 1, 0, 10, 3));
            Wire(input, Out(pick, "Results"));
            // The diagrams of stresses and shear along the girder read the whole girder: from the Super Element Check, not from the reduced Detail.
            int plots = 0;
            foreach (var girder in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Bridge Girder Results" && In(c, "Results").Sources.Contains(Out(detail, "Section result"))).ToArray())
            {
                bool alongGirder = girder.Params.Output.SelectMany(o => o.Recipients).Select(r => r.Attributes?.GetTopLevel?.DocObject).OfType<GH_Component>()
                    .Any(r => r.Name is "Bridge Stress Plot" or "Bridge Shear Plot");
                if (!alongGirder) continue;
                Replace(In(girder, "Results"), Out(detail, "Section result"), results); plots++;
            }
            log.Add($"{detail.NickName}: Detail Check sulle sezioni notevoli ({selectors.Length} selettori di irrigiditori, {extra.Length} gruppi di punti aggiunti; {plots} grafici di tensioni e taglio dal Super Element Check)");
        }
        cv.ExposeDefaults();
        var restore = Neutralize(doc, "stations-060");
        BridgeSegmentExcelTests.Solve(doc, true);
        var errors = BridgeMovingRevisionExample.Errors(doc);
        if (errors.Length > 0) Console.WriteLine("STATIONS 0.6.0: offline errors (analyses not run in the generator):\n" + errors);
        GH_Document.EnableSolutions = false; restore();
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis")) BridgeMovingRevisionExample.Clear(c);
        if (catalogue) { Example50HorizontalLayout.Apply(doc); ExampleTidy.Straighten(doc); }
        if (ninetyNine) { ExampleTidy.ArrangeModels(doc); ExampleTidy.Straighten(doc); }
        GH_Document.EnableSolutions = false; GrasshopperBridgeExample.Write(doc, target);
        Require(original.SequenceEqual(File.ReadAllBytes(source)), "the source definition changed");
        return new { Generator = "--example-stations", Source = Path.GetFileName(source), SourceSHA256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)),
            Definition = Path.GetFileName(target), DefinitionSHA256 = Sha(target), Changes = log, NativeSolverInvoked = false };
    }
}
