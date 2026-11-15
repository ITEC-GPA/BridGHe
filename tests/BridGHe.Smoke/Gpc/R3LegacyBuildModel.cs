using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.6 lane R3 (R0 report of 11/11, «Asserzioni ritirate», point 2): regression <c>gpc-build-model-legacy</c> (not native), the coverage
/// that <c>gpc-build-model</c> had on FEM inputs, rebuilt without the FEM classes. Every 0.3.0 model of the former corpus read by value
/// (<see cref="R3LegacyCorpus"/>: the frozen node, beam and plate data in the four unit systems, the 77 captured inputs of <c>capture-1</c>,
/// the examples 01–10 captured with them; models with moving paths are left out, as before: the paths come after Build Model): its
/// elements as GH data given by their drafts alone (<see cref="LegacyModelImport.NodeDraft"/>, <see cref="LegacyModelImport.BeamDraft"/>,
/// <see cref="LegacyModelImport.PlateDraft"/> on the elements of the archive, in the order of the model), its combinations and stages by
/// value, its name and units, through the Build Model component; the GPC input it gives against the import of the reader of the same archive
/// (<see cref="LegacyModelImport.Import"/>) with the owner of Build Model: report, bridge data, analysis fingerprint, parts and the XML
/// document, bit for bit (before: the conversion of the FEM output of the same component on the FEM values). The drafts of the output are
/// those given, element by element (canonical texts of the capture). A run Build Model refuses gives its error and is listed.
/// </summary>
internal static class R3LegacyBuildModel
{
    private static System.Reflection.Assembly plugin;
    private static object Prop(object o, string name) => o.GetType().GetProperty(name).GetValue(o);

    private static IGH_Goo Goo(string type, object datum, Guid? guid = null)
    {
        var method = plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem." + type, true).GetMethod("FromGpc", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return (IGH_Goo)method.Invoke(null, method.GetParameters().Length == 2 ? new object[] { datum, guid } : new object[] { datum });
    }

    internal static void Regression()
    {
        plugin = BridgeMergeTests.Initialize();
        var settings = plugin.GetType("BridGHe.Grasshopper.Settings");
        // The tolerance of the plug-in (the model tolerance of the active Rhino document, 0.001 without one): Build Model and the import take it.
        double tolerance = (double)settings.GetProperty("Tolerance").GetValue(settings.GetProperty("Instance").GetValue(null));
        int runs = 0, compared = 0, moving = 0, nodes = 0, beams = 0, plates = 0, stale = 0, renumbered = 0, declaredCases = 0; var refused = new List<string>(); var failures = new List<string>(); var units = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in R3LegacyCorpus.Fixtures(duplicates: false).Concat(R3LegacyCorpus.Captures()).Concat(R3LegacyCorpus.Examples()))
        {
            if (item.HasMovingPaths) { moving++; continue; }
            runs++;
            Result r;
            try { r = Run(item, tolerance); }
            catch (Exception x) when (Environment.GetEnvironmentVariable("BRIDGHE_REGRESSION_CONTINUE") == "1") { failures.Add(x.Message.Length > 900 ? x.Message.Substring(0, 900) : x.Message); continue; }
            if (r.Error != null) { refused.Add(item.Id + ": " + r.Error); Console.WriteLine($"GPC BUILD MODEL LEGACY ({item.Id}): no model, '{r.Error}'"); continue; }
            compared++; nodes += r.Nodes; beams += r.Beams; plates += r.Plates; stale += r.Stale; renumbered += r.Renumbered; declaredCases += r.DeclaredCases;
            units.TryGetValue(r.Units, out int u); units[r.Units] = u + 1;
            if (compared % 20 == 0 || item.Fixture != null) Console.WriteLine($"GPC BUILD MODEL LEGACY PASS ({item.Id}, {r.Units}): {r.Text}");
        }
        if (failures.Count > 0) throw new Exception($"GPC BUILD MODEL LEGACY FAIL: {failures.Count} models, {compared} equal\n" + string.Join("\n", failures));
        if (compared < 80) throw new Exception($"GPC BUILD MODEL LEGACY FAIL: only {compared} models compared");
        GpcInputComparison.PrintShadowed();
        if (Declared.Count > 0) Console.WriteLine($"DECLARED registered cases: {Declared.Values.Sum()} cases in {Declared.Count} models x the list of cases saved with the model, registered by the import, not made by Build Model (same cases, same origins but «Registered»); in {Reordered.Count} of them the import has the cases in the order of that list ({string.Join(", ", Reordered)}), compared in the order of Build Model");
        Console.WriteLine($"GPC BUILD MODEL LEGACY PASS: {runs} Build Model runs on the drafts of 0.3.0 models read by value ({moving} with moving paths left out), {compared} GPC inputs equal to the import of the reader bit for bit "
            + $"(report, bridge data, fingerprint, parts, XML; {renumbered} renumbered and renamed by the rule of Build Model; {declaredCases} registered cases declared; units {string.Join(", ", units.Select(p => p.Key + " " + p.Value))}; "
            + $"{stale} stale section states), drafts of {nodes} nodes, {beams} beams, {plates} plates equal to those given; {refused.Count} refused by Build Model{(refused.Count == 0 ? "" : ": " + string.Join(" | ", refused))}");
    }

    private sealed record Result(string Error, string Text = null, string Units = null, int Nodes = 0, int Beams = 0, int Plates = 0, int Stale = 0, int Renumbered = 0, int DeclaredCases = 0);

    private static Result Run(R3LegacyCorpus.Item item, double tolerance)
    {
        void Fail(string what) => throw new Exception("GPC BUILD MODEL LEGACY FAIL (" + item.Id + "): " + what);
        var root = item.Root;
        var definitionUnits = BridgeUnits.Parse(root.Enum("Units"));
        string name = root.String("Name") ?? "";
        var signatures = item.Signatures;
        // The elements by value, in the order of the model, each with a GH GUID of its own (from the id and the position).
        Guid Id(string what) => GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r3-build-model:" + item.Id + ":" + what);
        var nodeDrafts = new List<LegacyNodeDraft>(); var beamDrafts = new List<LegacyBeamDraft>(); var plateDrafts = new List<LegacyPlateDraft>();
        var elements = new List<IGH_Goo>();
        foreach (var (e, i) in root.Field("Elements").Items.Select((e, i) => (e, i)))
        {
            if (e.Is("NodeModel")) { var d = LegacyModelImport.NodeDraft(e, GpcBeamDrafts.DraftUnits, Id("node:" + i)); nodeDrafts.Add(d); elements.Add(Goo("GH_Node", d, Id("gh:" + i))); }
            else if (e.Is("BeamModel")) { var d = LegacyModelImport.BeamDraft(e, GpcBeamDrafts.DraftUnits, Id("beam:" + i), signatures?.Invoke(beamDrafts.Count)); beamDrafts.Add(d); elements.Add(Goo("GH_Beam", d, Id("gh:" + i))); }
            else if (e.Is("PlateModel")) { var d = LegacyModelImport.PlateDraft(e, GpcBeamDrafts.DraftUnits, Id("plate:" + i)); plateDrafts.Add(d); elements.Add(Goo("GH_Plate", d, Id("gh:" + i))); }
            else Fail("element " + e.ShortType);
        }
        var combinations = root.Field("LoadCombinations").Items.Select(LegacyModelImport.CombinationDraft).ToList();
        var stages = (root.Has("StagedConstruction") ? root.Field("StagedConstruction").Items : Array.Empty<LegacyValue>()).Select(LegacyModelImport.StageDraft).ToList();

        // Build Model in a document of its own.
        using var doc = new GH_Document();
        var component = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "BuildModelComponent"));
        component.CreateAttributes(); doc.AddObject(component, false);
        var input = (Param_GenericObject)component.Params.Input[0];
        foreach (var e in elements) input.PersistentData.Append(e);
        var n = (Param_String)component.Params.Input[1]; n.PersistentData.Clear(); n.SetPersistentData(name);
        var u = (Param_Integer)component.Params.Input[2]; u.PersistentData.Clear(); u.SetPersistentData((int)definitionUnits);
        foreach (var c in combinations) ((dynamic)component.Params.Input[3]).PersistentData.Append((dynamic)Goo("GH_LoadCombination", c));
        foreach (var s in stages) ((Param_GenericObject)component.Params.Input[4]).PersistentData.Append(Goo("GH_StagedConstruction", s));
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        var errors = component.RuntimeMessages(GH_RuntimeMessageLevel.Error);
        var output = component.Params.Output[0].VolatileData.AllData(true).SingleOrDefault();
        if (output == null) return new Result(errors.Count == 0 ? "no output and no error" : string.Join("; ", errors));
        if (Prop(output, "Value") != null || !(bool)Prop(output, "HasAttachedInput")) Fail("the output is not a GPC-only model");
        var attached = (GpcModels.Conversion)Prop(output, "GpcInput"); var model = attached.Model;

        // The import of the reader with the owner of Build Model (the model GUID of Build Model is the owner of its element identities).
        var owner = GpcInputComparison.ElementOwner(model);
        if (owner != model.Guid) Fail("the model GUID of Build Model is not the owner of its element identities");
        var report = new LegacyImportReport();
        var imported = LegacyModelImport.Import(root, owner, tolerance, report, signatures);
        if (model.Name != name || attached.Report.Units.Units != definitionUnits || attached.Tolerance != tolerance) Fail($"name '{model.Name}', units {attached.Report.Units.Units}, tolerance {attached.Tolerance}");

        // The correspondence of the elements: every element of the output comes from the GH datum given for one element of the archive (its GH
        // GUID), whose draft it carries; the import numbers the elements of each family in the order of the archive.
        var carried = attached.Drafts ?? throw new Exception($"GPC BUILD MODEL LEGACY FAIL ({item.Id}): the GPC input carries no drafts of its elements");
        if (carried.Nodes.Count != nodeDrafts.Count || carried.Beams.Count != beamDrafts.Count || carried.Plates.Count != plateDrafts.Count)
            Fail($"drafts of the output {carried.Nodes.Count}/{carried.Beams.Count}/{carried.Plates.Count}, given {nodeDrafts.Count}/{beamDrafts.Count}/{plateDrafts.Count}");
        var guids = (Guid[])output.GetType().GetMethod("ElementGuidsOf").Invoke(output, new object[] { carried });
        var givenGuids = elements.Select(e => (Guid)Prop(e, "Guid")).ToList();
        if (guids.Length != givenGuids.Count || guids.Distinct().Count() != guids.Length || guids.Except(givenGuids).Any()) Fail("the GH GUIDs of the elements are not those given");
        var archiveIndex = givenGuids.Select((g, i) => (g, i)).ToDictionary(x => x.g, x => x.i);
        var kinds = root.Field("Elements").Items.Select(e => e.ShortType).ToArray();
        int Rank(int index) => kinds.Take(index + 1).Count(k => k == kinds[index]);
        var maps = new Dictionary<int, int>[3]; // per family: number of the import -> number of the output
        bool identity = true;
        int draftsRenamed = 0;
        void Correspond(int family, string kind, int count, int offset, Func<int, object> outputDraft, Func<int, object> givenDraft, Func<object, GPC.Model.Elements.Element> element, Func<object, object> copy)
        {
            maps[family] = new Dictionary<int, int>();
            for (int k = 0; k < count; k++)
            {
                int index = archiveIndex[guids[offset + k]];
                if (kinds[index] != kind) Fail($"output {kind} {k + 1} comes from a {kinds[index]}");
                int number = Rank(index); maps[family][number] = k + 1; identity &= number == k + 1;
                // The drafts of the output: those given (the same identity and data), named and sourced by Build Model (DraftDiffers).
                var given = givenDraft(number - 1); var carriedDraft = outputDraft(k);
                if (element(carriedDraft).Name != element(given).Name) draftsRenamed++;
                string differs = DraftDiffers(carriedDraft, given, model, k + 1);
                if (differs != null) Fail($"the draft of {kind} {k + 1} of the output differs from the one given: {differs}");
            }
        }
        Correspond(0, "NodeModel", carried.Nodes.Count, 0, k => carried.Nodes[k], i => nodeDrafts[i], d => ((LegacyNodeDraft)d).Node, d => GpcDrafts.Copy((LegacyNodeDraft)d));
        Correspond(1, "BeamModel", carried.Beams.Count, carried.Nodes.Count, k => carried.Beams[k], i => beamDrafts[i], d => ((LegacyBeamDraft)d).Beam, d => GpcDrafts.Copy((LegacyBeamDraft)d));
        Correspond(2, "PlateModel", carried.Plates.Count, carried.Nodes.Count + carried.Beams.Count, k => carried.Plates[k], i => plateDrafts[i], d => ((LegacyPlateDraft)d).Plate, d => GpcDrafts.Copy((LegacyPlateDraft)d));

        // The registered cases of a captured model (the list of its cases, which Build Model does not make: it takes the cases of the elements,
        // combinations and stages) are declared: the same cases with the same origins but «registered». The import puts the cases of that list
        // first, in its order; Build Model in the order it meets them: the cases of the import are then compared in the order of Build Model.
        int declaredCases = DeclareRegistered(item.Id, attached.Report, report);
        bool casesReordered = false;
        if (declaredCases > 0 && !imported.LoadCases.Keys.SequenceEqual(model.LoadCases.Keys) && imported.LoadCases.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(model.LoadCases.Keys.OrderBy(k => k, StringComparer.Ordinal)))
        {
            var order = model.LoadCases.Keys.Select(k => imported.LoadCases[k]).ToList();
            imported.LoadCases.Clear(); foreach (var c in order) imported.LoadCases.Add(c);
            casesReordered = true; lock (Reordered) Reordered.Add(item.Id);
        }

        // Build Model names the elements by their first Element ID (else a number no ID uses) and puts the numbered beams and plates first (the
        // rule of 0.3.0, ModelHelper.BuildModel): a model not saved by Build Model (the frozen data) is renumbered and renamed. The import is
        // then numbered and named as the output (ModelValues.Renumber, same identities), its element GUIDs replaced by those of the output.
        var expected = imported; Dictionary<Guid, Guid> replace = null; int renamed = 0;
        var names = new[] { model.NodesElements.Values.Select(e => e.Name), model.BeamElements.Values.Select(e => e.Name), model.AreaElements.Values.Select(e => e.Name) };
        var importNames = new[] { imported.NodesElements.Values.Select(e => e.Name), imported.BeamElements.Values.Select(e => e.Name), imported.AreaElements.Values.Select(e => e.Name) };
        if (!identity || Enumerable.Range(0, 3).Any(f => !names[f].SequenceEqual(importNames[f])))
        {
            RequireNamingRule(item.Id, model, carried);
            int Of(GPC.Model.Elements.Element e) => maps[e is GPC.Model.Elements.NodeElement ? 0 : e is GPC.Model.Elements.BeamElement ? 1 : 2][e.Id];
            expected = GPC.Model.Core.ModelValues.Renumber(imported, e => Of(e));
            replace = new Dictionary<Guid, Guid>();
            var pairs = new List<(GPC.Model.Elements.Element X, GPC.Model.Elements.Element M)>();
            foreach (var e in expected.NodesElements.Values) pairs.Add((e, model.NodesElements[e.Id]));
            foreach (var e in expected.BeamElements.Values) pairs.Add((e, model.BeamElements[e.Id]));
            foreach (var e in expected.AreaElements.Values) pairs.Add((e, model.AreaElements[e.Id]));
            foreach (var p in pairs) p.X.Name = "r3-renaming:" + p.X.Guid;
            // A renamed element gets the source of its name, as Build Model gives it (the ID, the same string as the name; 1 for an empty one).
            foreach (var p in pairs)
            {
                if (p.X.Name != p.M.Name || p.X.Source?.OriginalId != p.M.Source?.OriginalId)
                    p.X.Source = new GPC.Model.Core.Identity.SourceIdentity(p.X.Source?.Program ?? LegacyModelImport.Program, p.X.Source?.ModelRevision ?? owner.ToString("D"),
                        GPC.Model.Models.Model.FamilyOf(p.X), p.M.Name.Length != 0 ? p.M.Name : 1.ToString());
                p.X.Name = p.M.Name; replace[p.X.Guid] = p.M.Guid;
            }
            renamed = Enumerable.Range(0, 3).Sum(f => names[f].Zip(importNames[f], (p, q) => p != q).Count(d => d));
            RequireMappedReport(item.Id, attached.Report, report, maps, model, imported);
        }
        else GpcInputComparison.RequireReport(item.Id, attached.Report, report);
        GpcInputComparison.RequireBridge(item.Id, model, attached.Bridge, expected, report.Bridge, false);
        if (replace == null)
        {
            var parts = GpcBridgeComparison.PartsDiffer(model, imported);
            if (model.AnalysisFingerprint() != imported.AnalysisFingerprint()) Fail("analysis fingerprint differs: " + string.Join("; ", parts.Take(12)));
            if (parts.Count != 0) Fail("parts differ: " + string.Join("; ", parts.Take(12)) + (parts.Count > 12 ? $"; ... {parts.Count} in all" : ""));
        }
        else { model.AnalysisFingerprint(); expected.AnalysisFingerprint(); } // the lazy values the document writes
        string a = GpcInputComparison.Document(model), b = GpcInputComparison.Document(expected, replace);
        if (replace != null && a != null && b != null)
        {
            // The renaming, in the two places of the document that name an element apart from its id: the source record of a beam load
            // and of its pieces (BeamLoad/<beam number>/<load>[/segment n], the number of the beam in its model) mapped to the number of the output; and the namespace
            // prefixes of the type annotations (dNpM, numbered by the serializer where a shared string is written first: the strings an element
            // shares with its name move with the renaming), on both sides.
            b = System.Text.RegularExpressions.Regex.Replace(b, "((?:OriginalAssignmentId|SourceRecord)_x003E_k__BackingField>BeamLoad/)([0-9]+)/", m => m.Groups[1].Value + maps[1][int.Parse(m.Groups[2].Value)] + "/");
            static string Prefixes(string d) => System.Text.RegularExpressions.Regex.Replace(d, "\"d[0-9]+p[0-9]+:", "\"dp:");
            a = Prefixes(a); b = Prefixes(b);
        }
        if (a == null || a != b)
        {
            var x = (a ?? "").Split('\n'); var y = (b ?? "").Split('\n');
            int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            Fail("XML document differs" + (replace == null ? "" : " (import renumbered and renamed as the output)") + ": "
                + (line < 0 ? $"{x.Length} / {y.Length} lines" : $"line {line + 1}: Build Model '{x[line].Trim()}' / import '{y[line].Trim()}'"));
        }
        int staleStates = model.BeamElements.Values.Count(x => attached.Bridge.SectionState(x.Guid) is var s && s != null && s.PropertyFingerprint.StartsWith(LegacyModelImport.StaleSectionStatePrefix, StringComparison.Ordinal));
        return new Result(null, $"{model.NodesElements.Count} nodes, {model.BeamElements.Count} beams, {model.AreaElements.Count} plates, {model.LoadCases.Count} cases, {model.Combinations.Count} combinations, "
            + $"{model.Stages?.Count ?? 0} stages, XML document equal ({a?.Length ?? 0} characters){(replace == null ? "" : $", the import renumbered and renamed as the output ({renamed} names)")}"
            + $"{(declaredCases == 0 ? "" : $", {declaredCases} registered cases declared{(casesReordered ? " (cases of the import in the order of Build Model)" : "")}")}{(draftsRenamed == 0 ? "" : $", {draftsRenamed} drafts named by Build Model")}; {attached.Report.BuildModel}", definitionUnits.ToString(), nodeDrafts.Count, beamDrafts.Count, plateDrafts.Count, staleStates,
            replace == null ? 0 : 1, declaredCases);
    }

    /// <summary>
    /// The declared difference of a model saved with its list of cases (the captured inputs: the former model registered them): the import
    /// registers those cases (count, origin «Registered»), Build Model on the drafts has no such list and takes the same cases from the elements,
    /// combinations and stages. Declared when Build Model registers none, every case has the same origins but «Registered», and no case of
    /// the import is registered only (the case sets are the same); the import report is then compared as Build Model makes it. Returns the
    /// number of registered cases declared (0: none, the reports compared as they are).
    /// </summary>
    private static int DeclareRegistered(string label, LegacyImportReport actual, LegacyImportReport expected)
    {
        if (expected.RegisteredCases == 0 || actual.RegisteredCases != 0) return 0;
        var names = expected.CaseOrigins.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        if (!names.SequenceEqual(actual.CaseOrigins.Keys.OrderBy(k => k, StringComparer.Ordinal))) return 0;
        if (names.Any(n => (expected.CaseOrigins[n] & ~LegacyCaseOrigins.Registered) != actual.CaseOrigins[n])) return 0;
        int count = expected.RegisteredCases;
        foreach (var n in names) expected.CaseOrigins[n] &= ~LegacyCaseOrigins.Registered;
        typeof(LegacyImportReport).GetProperty("RegisteredCases").GetSetMethod(true).Invoke(expected, new object[] { 0 });
        lock (Declared) { Declared.TryGetValue(label, out int c); Declared[label] = c + count; }
        return count;
    }

    private static string Fp(params object[] values) => GPC.Model.Core.ModelValues.Fingerprint(values);

    /// <summary>
    /// Null when the draft Build Model carries for an element of its output is the draft given for it: the same identity, the name of the element
    /// of the output, and the same data by their fingerprints (position, ends or corners, loads, restraints, assignments, attributes, groups,
    /// axes, offsets, formulation, property and its data, BridGHe angle, bridge data, the records at list level). Build Model gives the draft
    /// its name and a source of that name, and copies of its cases and property (their fingerprints are those of the values): not compared as
    /// text. Otherwise the first difference.
    /// </summary>
    private static string DraftDiffers(object output, object given, Model model, int number)
    {
        static string Same(string what, object a, object b) => Fp(a) == Fp(b) ? null : what;
        static object[] Groups(GPC.Model.Elements.Element e) => e.Groups.Values.Select(g => new object[] { g.Guid, g.Name, g.Parent?.Guid }).ToArray();
        static object[] Restraints(GPC.Model.Elements.NodeElement n) => n.Assignments.Restrains.Select(r => new object[] { r.Case, r.FreedomCase, r.Phase, r.SourceRecord, r.Restrain?.Name, r.Restrain?.CoordinateSystem, r.Restrain?.Restrains }).ToArray();
        static object Load(GPC.Model.Loads.Assignments.BeamLoadAssignment l) => new object[] { l.Start, l.End, l.Concentrated, l.StartIntensity, l.EndIntensity, l.Eccentricity,
            l.EccentricityCoordinateSystem, (int)l.LengthConvention, l.ProjectionPlaneNormal, l.StationDomain };
        static string Bridge(GPC.Checkers.CompositeBridge.BridgeBeamData d) => d == null ? "none" : string.Join("|", d.BeamGuid, d.Member == null ? "-" : Fp(d.Member), d.SuperElement ?? "-",
            d.SectionState == null ? "-" : $"{d.SectionState.Behavior} {d.SectionState.ConcreteExcluded} {d.SectionState.ModularRatio:R} {d.SectionState.ClassificationCase ?? "-"} {d.SectionState.PropertyFingerprint}",
            d.CompositeSection == null ? "-" : "composite");
        string Common(GPC.Model.Elements.Element a, GPC.Model.Elements.Element b, GPC.Model.Elements.Element x, LegacyElementRecords ra, LegacyElementRecords rb)
        {
            if (a.Guid != b.Guid) return "identity";
            if (a.Name != x.Name) return $"name '{a.Name}', the element of the output '{x.Name}'";
            if (a.Source?.OriginalId != (a.Name.Length != 0 ? a.Name : "1")) return $"source '{a.Source?.OriginalId}' for the name '{a.Name}'";
            return Same("attributes", a.Attributes.Values.ToArray(), b.Attributes.Values.ToArray()) ?? Same("groups", Groups(a), Groups(b))
                ?? (GpcDraftArchiveChecks.Records(ra) == GpcDraftArchiveChecks.Records(rb) ? null : "records\n" + GpcDraftArchiveChecks.FirstDifference(GpcDraftArchiveChecks.Records(rb), GpcDraftArchiveChecks.Records(ra)));
        }
        switch (output)
        {
            case LegacyNodeDraft o:
            {
                var g = (LegacyNodeDraft)given; var a = o.Node; var b = g.Node;
                return Common(a, b, model.NodesElements[number], o.Records, g.Records) ?? Same("position", a.Position, b.Position) ?? Same("loads", a.Loads.Values.ToArray(), b.Loads.Values.ToArray())
                    ?? Same("restraints", Restraints(a), Restraints(b)) ?? Same("assignments", new object[] { a.Assignments.Physical, a.Assignments.Dofs, a.Assignments.Mass, a.Assignments.GroundSprings },
                        new object[] { b.Assignments.Physical, b.Assignments.Dofs, b.Assignments.Mass, b.Assignments.GroundSprings });
            }
            case LegacyBeamDraft o:
            {
                var g = (LegacyBeamDraft)given; var a = o.Beam; var b = g.Beam;
                return Common(a, b, model.BeamElements[number], o.Records, g.Records) ?? Same("ends", new object[] { a.StartPoint, a.EndPoint }, new object[] { b.StartPoint, b.EndPoint })
                    ?? Same("loads", new object[] { a.Loads.Values.ToArray(), a.Assignments.Loads.Select(Load).ToArray() }, new object[] { b.Loads.Values.ToArray(), b.Assignments.Loads.Select(Load).ToArray() })
                    ?? Same("physical data", a.Assignments.Physical, b.Assignments.Physical)
                    ?? Same("axes and offsets", new object[] { a.Assignments.SectionAxes, a.Assignments.OffsetAxes, a.Assignments.OffsetI, a.Assignments.OffsetJ, (int)a.Assignments.Formulation },
                        new object[] { b.Assignments.SectionAxes, b.Assignments.OffsetAxes, b.Assignments.OffsetI, b.Assignments.OffsetJ, (int)b.Assignments.Formulation })
                    ?? Same("property", a.BeamProperty, b.BeamProperty) ?? (GpcDraftArchiveChecks.Property(o.Property) == GpcDraftArchiveChecks.Property(g.Property) ? null : "property data")
                    ?? (o.AngleDeg.Equals(g.AngleDeg) ? null : "BridGHe angle") ?? (Bridge(o.Bridge) == Bridge(g.Bridge) ? null : $"bridge data '{Bridge(o.Bridge)}' / '{Bridge(g.Bridge)}'");
            }
            case LegacyPlateDraft o:
            {
                var g = (LegacyPlateDraft)given; var a = o.Plate; var b = g.Plate;
                return Common(a, b, model.AreaElements[number], o.Records, g.Records) ?? Same("corners", o.Corners.ToArray(), g.Corners.ToArray()) ?? Same("loads", a.Loads.Values.ToArray(), b.Loads.Values.ToArray())
                    ?? Same("assignments", new object[] { a.Assignments.Offset, a.Assignments.Physical }, new object[] { b.Assignments.Offset, b.Assignments.Physical })
                    ?? Same("property", a.PlateProperty, b.PlateProperty) ?? (o.AngleDeg.Equals(g.AngleDeg) ? null : "local axis angle");
            }
            default: return "not a draft: " + output?.GetType().Name;
        }
    }

    private static readonly Dictionary<string, int> Declared = new Dictionary<string, int>(StringComparer.Ordinal);
    private static readonly List<string> Reordered = new List<string>();

    /// <summary>The naming rule of Build Model (0.3.0, <c>ModelHelper.BuildModel</c>) on its output: an element with an Element ID is named by
    /// its first one, the others by numbers no Element ID uses, all names of a family distinct; the beams and plates with a number come first,
    /// by number, then the others in the order given.</summary>
    private static void RequireNamingRule(string label, Model model, GpcModelDrafts drafts)
    {
        void Fail(string what) => throw new Exception("GPC BUILD MODEL LEGACY FAIL (" + label + "): naming rule of Build Model: " + what);
        void Family(string kind, IReadOnlyList<string> names, IReadOnlyList<string> labels, bool numberedFirst)
        {
            var used = labels.Where(l => l != null).ToHashSet(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
                if (labels[i] != null ? names[i] != labels[i] : !int.TryParse(names[i], out _) || used.Contains(names[i])) Fail($"{kind} {i + 1} '{names[i]}' (first Element ID '{labels[i] ?? "none"}')");
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Count) Fail(kind + " names not distinct");
            if (numberedFirst)
            {
                var numbers = names.Select(n => int.TryParse(n, out int v) ? v : int.MaxValue).ToArray();
                for (int i = 1; i < numbers.Length; i++) if (numbers[i] < numbers[i - 1]) Fail($"{kind} {i + 1} '{names[i]}' after '{names[i - 1]}'");
            }
        }
        Family("node", model.NodesElements.Values.OrderBy(e => e.Id).Select(e => e.Name).ToList(), drafts.Nodes.Select(d => d.Records.FirstLabel).ToList(), false);
        Family("beam", model.BeamElements.Values.OrderBy(e => e.Id).Select(e => e.Name).ToList(), drafts.Beams.Select(d => d.Records.FirstLabel).ToList(), true);
        Family("plate", model.AreaElements.Values.OrderBy(e => e.Id).Select(e => e.Name).ToList(), drafts.Plates.Select(d => d.Records.FirstLabel).ToList(), true);
    }

    /// <summary>
    /// The report of Build Model against the report of the import, the elements of the import numbered as the output (<paramref name="maps"/>:
    /// per family, number of the import → number of the output): element cases and element warnings (the name of the element in a warning
    /// is the name it has in each model), BridGHe angles and shadowed beams by number; units, owner, registered cases, case origins, native
    /// sections (by kind), beam property data and warnings as <see cref="GpcInputComparison.RequireReport"/>.
    /// </summary>
    private static void RequireMappedReport(string label, LegacyImportReport actual, LegacyImportReport expected, Dictionary<int, int>[] maps, Model model, Model imported)
    {
        void Fail(string what) => throw new Exception("GPC BUILD MODEL LEGACY FAIL (" + label + "): report (import numbered as the output) " + what);
        static int F(GPC.Model.Core.Identity.EntityFamily f) => f == GPC.Model.Core.Identity.EntityFamily.Node ? 0 : f == GPC.Model.Core.Identity.EntityFamily.Beam ? 1 : 2;
        string Name(Model m, int family, int number) => family == 0 ? m.NodesElements[number].Name : family == 1 ? m.BeamElements[number].Name : m.AreaElements[number].Name;
        string Cases(LegacyImportReport r, bool map) => string.Join(" | ", r.ElementLoadCases.Select(c => (F(c.Family), map ? maps[F(c.Family)][c.Number] : c.Number, c))
            .OrderBy(x => x.Item1).ThenBy(x => x.Item2).Select(x => x.c.Family + ":" + x.Item2 + ":" + string.Join(",", x.c.Cases)));
        if (Cases(actual, false) != Cases(expected, true)) Fail($"element cases '{Cases(actual, false)}' / '{Cases(expected, true)}'");
        string Warnings(LegacyImportReport r, Model m, bool map) => string.Join(" | ", r.ElementWarnings.Select(w =>
        {
            int f = F(w.Family), n = map ? maps[f][w.Number] : w.Number; string own = Name(m, f, w.Number);
            return w.Family + ":" + n + ":" + w.Restraint + ":" + w.Text(n).Replace(" " + own + ":", " <name>:");
        }).OrderBy(s => s, StringComparer.Ordinal));
        if (Warnings(actual, model, false) != Warnings(expected, imported, true)) Fail($"element warnings '{Warnings(actual, model, false)}' / '{Warnings(expected, imported, true)}'");
        if (actual.Units.Units != expected.Units.Units || actual.RegisteredCases != expected.RegisteredCases || actual.Owner != expected.Owner)
            Fail($"units {actual.Units.Units}/{expected.Units.Units}, registered cases {actual.RegisteredCases}/{expected.RegisteredCases}, owner {actual.Owner}/{expected.Owner}");
        string Map<T>(IEnumerable<KeyValuePair<string, T>> m, Func<T, string> text) => string.Join("; ", m.Select(p => p.Key + "=" + text(p.Value)).OrderBy(s => s, StringComparer.Ordinal));
        if (Map(actual.CaseOrigins, o => o.ToString()) != Map(expected.CaseOrigins, o => o.ToString())) Fail($"case origins '{Map(actual.CaseOrigins, o => o.ToString())}' / '{Map(expected.CaseOrigins, o => o.ToString())}'");
        static string D(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");
        string Angles(LegacyImportReport r, bool map) => string.Join(",", r.BeamAngles.Select(p => (map ? maps[1][p.Key] : p.Key) + "=" + D(p.Value)).OrderBy(s => s, StringComparer.Ordinal));
        if (Angles(actual, false) != Angles(expected, true)) Fail("BridGHe angles differ");
        string Natives(LegacyImportReport r) => string.Join(",", r.NativeSections.Values.Select(s => s == null ? "null" : s.GetType().Name).OrderBy(s => s, StringComparer.Ordinal));
        if (Natives(actual) != Natives(expected)) Fail($"native sections '{Natives(actual)}' / '{Natives(expected)}'");
        string Property(LegacyBeamPropertyData d) => d.Formulation + "/" + D(d.Angle) + "/" + (d.Native == null ? "-" : "native") + "/" + d.Signature;
        if (Map(actual.BeamProperties, Property) != Map(expected.BeamProperties, Property)) Fail($"beam properties '{Map(actual.BeamProperties, Property)}' / '{Map(expected.BeamProperties, Property)}'");
        // The warnings: those of the elements (compared above, with the names of each model) taken out once each, the others compared as a
        // multiset, with the declared shadowing of R6.3 (GpcInputComparison.ShadowRule) as RequireReport.
        List<string> Others(LegacyImportReport r)
        {
            var left = r.Warnings.ToList();
            foreach (var w in r.ElementWarnings) left.Remove(w.Text(w.Number));
            return left;
        }
        var actualOthers = Others(actual); var expectedOthers = Others(expected);
        if (actual.Warnings.Count - actualOthers.Count != expected.Warnings.Count - expectedOthers.Count) Fail($"{actual.Warnings.Count - actualOthers.Count} / {expected.Warnings.Count - expectedOthers.Count} warnings of the elements");
        var mappedShadowed = expected.ShadowedPropertyBeams.Select(k => maps[1][k]).ToHashSet();
        string Shadowed(IEnumerable<int> beams) => string.Join(",", beams.OrderBy(k => k));
        if (Shadowed(actual.ShadowedPropertyBeams) != Shadowed(mappedShadowed))
        {
            var extra = mappedShadowed.Except(actual.ShadowedPropertyBeams).ToList();
            if (actual.ShadowedPropertyBeams.Except(mappedShadowed).Any() || extra.Count == 0) Fail($"shadowed beams '{Shadowed(actual.ShadowedPropertyBeams)}' / '{Shadowed(mappedShadowed)}'");
            var remaining = expectedOthers.ToList();
            foreach (var w in actualOthers) remaining.Remove(w);
            if (remaining.Count == 0 || remaining.Count > extra.Count || remaining.Any(w => !System.Text.RegularExpressions.Regex.IsMatch(w, "^The beam property .* already exists$")))
                Fail($"shadowed beams '{Shadowed(actual.ShadowedPropertyBeams)}' / '{Shadowed(mappedShadowed)}' (warnings not those of the property registry)");
            expectedOthers = expectedOthers.Where(w => actualOthers.Contains(w)).ToList();
            lock (GpcInputComparison.Shadowed) { GpcInputComparison.Shadowed.TryGetValue(label, out int s); GpcInputComparison.Shadowed[label] = s + extra.Count; }
        }
        string All(IEnumerable<string> w) => string.Join(" | ", w.OrderBy(s => s, StringComparer.Ordinal));
        if (All(actualOthers) != All(expectedOthers)) Fail($"warnings '{All(actualOthers)}' / '{All(expectedOthers)}'");
    }
}
