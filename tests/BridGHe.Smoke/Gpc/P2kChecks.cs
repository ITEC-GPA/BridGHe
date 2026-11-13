using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Reports;
using BridGHe.Common.Straus;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// P2K (12/11): the in-memory checks of the lane P2-componenti (regression <c>p2k-components</c>), without Straus7:
/// <list type="bullet">
/// <item>P10: Concrete Slab Section gives its GPC datum (<see cref="ConcreteSlabSection"/>), Composite Beam Property builds the same definition
/// from it as from the former slab value;</item>
/// <item>P7: the composite beam of a beam given by its draft alone has the same GUID whatever the GH GUID of the beam wrapper (two solves, two
/// wrappers of the same draft), another for another draft;</item>
/// <item>P8: a model with a cable is refused with the message before any Straus7 call (no file, no private folder) by the analysis library,
/// Straus R3 Analysis and Straus7 R3 Export; a truss and a cutoff bar are not refused;</item>
/// <item>P1: the producers, the element components and the bridge generators give their outputs by their GPC data alone (no FEM value);</item>
/// <item>P6: the data of Bridge Report are the same JSON as through one string: the data hash, the detached data, the snapshot of the DOCX.</item>
/// </list>
/// The expected values are written from the inputs (numbers, counts, messages), never from the code under test.
/// </summary>
internal static class P2kChecks
{
    private static int checks;
    private static readonly List<string> failures = new();

    private static void Check(bool ok, string what) { checks++; if (!ok) failures.Add(what); }

    internal static void Regression()
    {
        checks = 0; failures.Clear();
        var plugin = R2aDesignFixtures.Plugin;
        Slab(out var compositeProperty, out var section, out var steel);
        CompositeBeam(compositeProperty, section, steel);
        Cable(section, steel);
        Generators(section, steel);
        Report();
        foreach (var f in failures.Take(40)) Console.WriteLine("  " + f);
        if (failures.Count != 0) throw new Exception($"P2K COMPONENTS FAIL: {failures.Count} of {checks} checks (first: {failures[0]})");
        Console.WriteLine($"P2K COMPONENTS PASS: {checks} checks (slab datum, composite beam GUID, cable refusal, generators without FEM, report streams).");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // GH helpers

    private static IGH_Goo Goo(object v) => v switch
    {
        IGH_Goo g => g,
        double d => new GH_Number(d),
        int i => new GH_Integer(i),
        bool b => new GH_Boolean(b),
        string s => new GH_String(s),
        Vector3d x => new GH_Vector(x),
        Point3d p => new GH_Point(p),
        Line l => new GH_Line(l),
        Curve c => new GH_Curve(c),
        _ => new GH_ObjectWrapper(v),
    };

    private static void Set(GH_Component c, int index, object value)
    {
        var p = c.Params.Input[index]; p.RemoveAllSources();
        var data = p.GetType().GetProperty("PersistentData").GetValue(p);
        data.GetType().GetMethod("Clear").Invoke(data, null);
        var append = data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(GH_Path));
        foreach (var v in value is object[] list ? list : new[] { value }) append.Invoke(data, new object[] { Goo(v), new GH_Path(0) });
        p.ExpireSolution(false);
    }

    /// <summary>One component solved in a document of its own: the data of every output and the errors.</summary>
    private static (List<IGH_Goo>[] Outputs, string[] Errors, GH_Component Component) Solve(string type, params (int Input, object Value)[] inputs) =>
        Solve(type, null, inputs);

    private static (List<IGH_Goo>[] Outputs, string[] Errors, GH_Component Component) Solve(string type, Action<GH_Component> before, params (int Input, object Value)[] inputs)
    {
        var doc = new GH_Document();
        var c = BridgeSegmentExcelTests.Add(doc, type);
        foreach (var (input, value) in inputs) Set(c, input, value);
        before?.Invoke(c);
        BridgeSegmentExcelTests.Solve(doc, true);
        return (c.Params.Output.Select(o => o.VolatileData.AllData(true).ToList()).ToArray(), c.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToArray(), c);
    }

    private static IGH_Goo One(string type, params (int Input, object Value)[] inputs)
    {
        var (outputs, errors, _) = Solve(type, inputs);
        return outputs[0].FirstOrDefault()?.Duplicate() ?? throw new Exception("P2K: " + type + " gives no output (" + string.Join(" / ", errors) + ")");
    }

    private static bool GpcOnly(IGH_Goo g) => g != null && R2aDesignFixtures.Datum(g) != null && !R2aDesignFixtures.HasFem(g);

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // P10

    private static void Slab(out IGH_Goo composite, out IGH_Goo section, out IGH_Goo steel)
    {
        steel = One("MaterialSteelComponent", (0, "S355"), (1, 2));
        var concrete = One("MaterialConcreteComponent", (0, "C35/45"), (1, 7));
        var rebar = One("MaterialRebarComponent", (0, "B450C"));
        section = One("BeamSectionIComponent", (0, 300.0), (1, 250.0), (2, 800.0), (3, 20.0), (4, 16.0), (5, 12.0));
        Check(GpcOnly(steel) && GpcOnly(concrete) && GpcOnly(rebar) && GpcOnly(section), "P1: materials and I section by their GPC data alone");
        var slab = One("BeamSectionConcreteSlabComponent", (0, 250.0), (1, 2500.0), (2, 1000.0), (3, 50.0), (4, 800.0), (5, 50.0));
        var datum = (slab as GH_ObjectWrapper)?.Value as ConcreteSlabSection;
        Check(datum == new ConcreteSlabSection(250, 2500, 1000, 50, 800, 50) && datum.TotalRebarArea == 1800 && datum.IsValid,
            "P10: Concrete Slab Section gives its GPC datum with its numbers: " + (slab?.ToString() ?? "null"));
        var (badOutputs, badErrors, _) = Solve("BeamSectionConcreteSlabComponent", (0, 250.0), (1, 2500.0), (2, 1000.0), (3, 260.0));
        Check(badOutputs[0].Count == 0 && badErrors.Any(e => e.Contains("Active row axes must lie inside the slab")), "P10: a row outside the slab is refused, no output");
        var inputs = new List<(int, object)> { (0, "CB"), (1, slab), (2, section), (3, concrete), (4, steel), (5, rebar), (6, 6.0), (7, 18.0) };
        var (outputs, errors, _) = Solve("BeamPropertyCompositeComponent", inputs.ToArray());
        composite = outputs[0].FirstOrDefault();
        Check(errors.Length == 0 && outputs.All(o => o.Count == 1 && GpcOnly(o[0])), "P10/P1: Composite Beam Property gives its five outputs by their GPC data alone: " + string.Join(" / ", errors));
        // P2CA (13/11/2026, U3): the definition from the former slab value (SectionConcreteSlabModel, transitional) is retired by name with the FEM
        // types (P2rRetired.Checks); the slab datum above is the input of every path.
        var a = R2aDesignFixtures.Datum<GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition>(composite);
        // By content (the GPC fingerprint of the definition: the archive holds the GUIDs the materials take at construction, other at every build).
        var again = R2aDesignFixtures.Datum<GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition>(One("BeamPropertyCompositeComponent",
            new (int, object)[] { (0, "CB"), (1, slab), (2, section), (3, concrete), (4, steel), (5, rebar), (6, 6.0), (7, 18.0) }));
        Check(a != null && again != null && a.Fingerprint() == again.Fingerprint(), "P10: the definition of the same inputs, twice: the same fingerprint (control)");
        Check(a != null && a.Name == "CB", "P10: the definition keeps the name");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // P7

    private static void CompositeBeam(IGH_Goo composite, IGH_Goo section, IGH_Goo steel)
    {
        var girder = One("BeamPropertyComponent", (0, "Girder"), (1, 5), (2, section), (3, steel));
        Check(GpcOnly(girder), "P1: Beam Property by its GPC datum alone");
        IGH_Goo Beam(double y) => One("BeamComponent", (0, new LineCurve(new Point3d(0, y, 0), new Point3d(10000, y, 0))), (1, girder));
        var beam = Beam(0); var other = Beam(3000);
        Check(GpcOnly(beam) && GpcOnly(other), "P1: Beam by its GPC draft alone");
        var draft = R2aDesignFixtures.Datum<LegacyBeamDraft>(beam);
        Guid Composite(IGH_Goo b)
        {
            var output = One("CompositeBeamComponent", (0, b), (1, composite));
            Check(!R2aDesignFixtures.HasFem(output) && R2aDesignFixtures.Datum(output) != null, "P7/P1: the composite beam by its GPC datum alone");
            return R2aDesignFixtures.Get<Guid>(output, "Guid");
        }
        // Two wrappers of the same draft (FromGpc without a GUID: a new GH GUID each), each solved in a document of its own.
        var w1 = R2aDesignFixtures.Beam(draft); var w2 = R2aDesignFixtures.Beam(draft);
        Check(R2aDesignFixtures.Get<Guid>(w1, "Guid") != R2aDesignFixtures.Get<Guid>(w2, "Guid"), "P7: two wrappers of the same draft have two GH GUIDs (fixture)");
        Guid g1 = Composite(w1), g2 = Composite(w2), g3 = Composite(w1);
        Check(g1 == g2 && g1 == g3 && g1 != Guid.Empty, $"P7: the composite beam of the same draft has the same GUID whatever the beam wrapper and the solve: {g1} / {g2} / {g3}");
        Check(Composite(other) != g1, "P7: another beam gives another composite beam GUID");
        Check(Composite(R2aDesignFixtures.Beam(draft, R2aDesignFixtures.Id("p2k-wrapper"))) == g1, "P7: a wrapper with an explicit GH GUID gives the same composite beam GUID");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // P8

    private static void Cable(IGH_Goo section, IGH_Goo steel)
    {
        var circle = One("BeamSectionSolidCircleComponent", (0, 20.0));
        IGH_Goo Model(int type, string name)
        {
            // A cutoff bar shows its four inputs (the component registers them for type 3): tension 1e9, compression 0, brittle, mass kept.
            var (properties, propertyErrors, _) = Solve("BeamPropertyComponent", type != 3 ? null : c => { c.ExpireSolution(false); Set(c, 4, 1e9); Set(c, 5, 0.0); Set(c, 6, 0); Set(c, 7, true); },
                (0, name), (1, type), (2, type == 1 ? circle : section), (3, steel));
            var property = properties[0].FirstOrDefault() ?? throw new Exception("P2K: Beam Property " + name + " gives no output (" + string.Join(" / ", propertyErrors) + ")");
            var beam = One("BeamComponent", (0, new LineCurve(new Point3d(0, 0, 0), new Point3d(5000, 0, 0))), (1, property));
            var model = One("BuildModelComponent", (0, new object[] { beam }));
            Check(model.GetType().Name == "GH_Model" && !R2aDesignFixtures.HasFem(model) && R2aDesignFixtures.Get<GpcModels.Conversion>(model, "GpcInput") != null,
                "P1: Build Model gives a model by its GPC input alone (" + name + ")");
            return model;
        }
        var cable = Model(1, "Fune");
        var input = R2aDesignFixtures.Get<GpcModels.Conversion>(cable, "GpcInput");
        Check(input.Model.BeamElements.Values.Single().Assignments.Formulation == GPC.Model.Structure.Assignments.BeamFormulation.Cable, "P8: the model has a cable (fixture)");
        string folder = Path.Combine(Path.GetTempPath(), "bridghe-p2k-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string output = Path.Combine(folder, "cable.st7");
        int Private() => Directory.GetDirectories(Path.GetTempPath(), "bridghe-gpc-*").Length;
        int before = Private();
        bool Refusal(string message) => message != null && message.StartsWith("StrausCableNotSupported: ", StringComparison.Ordinal)
            && message.Contains("'Fune'") && message.Contains("0.4.0") && message.Contains("Cutoff Bar [3]") && message.Contains("Truss [2]");
        string library = null;
        try { StrausAnalysis.Run(input, output, 0, Array.Empty<string>(), false, out _); }
        catch (NotSupportedException x) { library = x.Message; }
        Check(Refusal(library), "P8: the analysis library refuses the cable with the message: " + (library ?? "accepted"));
        string moving = null;
        try { StrausAnalysis.Run(input, output, 0, Array.Empty<string>(), true, out _); }
        catch (NotSupportedException x) { moving = x.Message; }
        Check(Refusal(moving), "P8: the moving analysis refuses the cable before its other checks: " + (moving ?? "accepted"));
        void Press(GH_Component c)
        {
            for (var t = c.GetType(); t != null; t = t.BaseType)
                if (t.GetField("_run", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly) is { } f) { f.SetValue(c, true); return; }
            throw new Exception("P2K: no _run field on " + c.Name);
        }
        var (analysis, analysisErrors, _) = Solve("StrausR3AnalysisComponent", Press, (0, cable), (3, output));
        Check(analysis[0].Count == 0 && analysisErrors.Any(Refusal), "P8: Straus R3 Analysis refuses the cable with the message, no output: " + string.Join(" / ", analysisErrors));
        var (export, exportErrors, _) = Solve("StrausR3ExportComponent", Press, (0, cable), (1, output));
        Check(export[0].Count == 0 && exportErrors.Any(Refusal), "P8: Straus7 R3 Export refuses the cable with the message, no output: " + string.Join(" / ", exportErrors));
        Check(!File.Exists(output) && Directory.GetFiles(folder).Length == 0 && Private() == before, "P8: nothing written (no Straus7 file, no private folder of the Converter)");
        // A truss and a cutoff bar are not refused (the cutoff bar is written since 0.4.0, user decision of 11/11).
        foreach (var (type, name) in new[] { (2, "Biella"), (3, "Cutoff") })
        {
            var model = R2aDesignFixtures.Get<GpcModels.Conversion>(Model(type, name), "GpcInput").Model;
            string refused = null;
            try { StrausAnalysis.RefuseCables(model); } catch (NotSupportedException x) { refused = x.Message; }
            Check(refused == null, "P8: " + name + " is not refused: " + refused);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // P1, generators

    private static void Generators(IGH_Goo section, IGH_Goo steel)
    {
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        var goo = (IGH_Goo)Activator.CreateInstance(R2aDesignFixtures.Plugin.GetTypes().Single(t => t.Name == "BridgeGeometryGoo"), geometry);
        int points = geometry.Supports.Count;
        var layout = BridgeBearings.Create(geometry, null, null, new[] { 0 }, 0, Vector3d.Zero, Vector3d.Zero);
        Check(layout.Drafts.Length == points, $"P1: Bridge Bearings on the GPC path makes {points} drafts ({layout.Drafts.Length}; P2CE: the layout has no FEM nodes)");
        var (bearings, bearingErrors, _) = Solve("BridgeBearingsComponent", (0, goo), (3, 0));
        Check(bearingErrors.Length == 0 && bearings[0].Count == points && bearings[0].All(GpcOnly), $"P1: Bridge Bearings gives {points} nodes by their drafts alone: {bearings[0].Count} " + string.Join(" / ", bearingErrors));
        var again = Solve("BridgeBearingsComponent", (0, goo), (3, 0)).Outputs[0];
        Check(again.Select(n => R2aDesignFixtures.Datum<LegacyNodeDraft>(n).Node.Guid).SequenceEqual(bearings[0].Select(n => R2aDesignFixtures.Datum<LegacyNodeDraft>(n).Node.Guid)),
            "P1: Bridge Bearings identities by content (the same twice)");
        var (deck, deckErrors, _) = Solve("BridgeDeckMeshComponent", (0, goo), (1, R2aDesignFixtures.PlateProperty(R2aDesignFixtures.DeckProperty())));
        Check(deckErrors.Length == 0 && deck[2].Count > 0 && deck[2].All(GpcOnly) && deck[3].Count > 0, $"P1: Bridge Deck Mesh gives {deck[2].Count} plates by their drafts alone and {deck[3].Count} connection lines " + string.Join(" / ", deckErrors));
        var (thin, thinErrors, _) = Solve("BridgeDeckMeshComponent", (0, goo), (1, R2aDesignFixtures.PlateProperty(R2aDesignFixtures.DeckProperty(200))));
        Check(thin[2].Count == 0 && thinErrors.Any(e => e.Contains("must match the geometric slab")), "P1: Bridge Deck Mesh refuses a membrane thinner than the slab on the GPC datum: " + string.Join(" / ", thinErrors));
        var lines = deck[3].Select(g => ((GH_Line)g).Value).Take(4).ToArray();
        var (rigid, rigidErrors, _) = Solve("BridgeRigidLinksComponent", (0, lines.Cast<object>().ToArray()));
        Check(rigidErrors.Length == 0 && rigid[0].Count == 2 * lines.Length && rigid[0].All(GpcOnly), $"P1: Bridge Rigid Links gives {2 * lines.Length} nodes by their drafts alone: {rigid[0].Count} " + string.Join(" / ", rigidErrors));
        var girder = One("BeamPropertyComponent", (0, "Connection"), (1, 5), (2, section), (3, steel));
        var (beams, beamErrors, _) = Solve("BridgeConnectionBeamsComponent", (0, lines.Cast<object>().ToArray()), (1, girder));
        Check(beamErrors.Length == 0 && beams[0].Count == lines.Length && beams[0].All(GpcOnly), $"P1: Bridge Connection Beams gives {lines.Length} beams by their drafts alone: {beams[0].Count} " + string.Join(" / ", beamErrors));
        var (dup, dupErrors, _) = Solve("BridgeRigidLinksComponent", (0, new object[] { lines[0], lines[0] }));
        Check(dup[0].Count == 0 && dupErrors.Any(e => e.Contains("Duplicate connection line")), "P1: a duplicate connection line is refused: " + string.Join(" / ", dupErrors));
        var circle = One("BeamSectionSolidCircleComponent", (0, 20.0));
        var cable = One("BeamPropertyComponent", (0, "Fune"), (1, 1), (2, circle), (3, steel));
        var (wrong, wrongErrors, _) = Solve("BridgeConnectionBeamsComponent", (0, lines.Cast<object>().ToArray()), (1, cable));
        Check(wrong[0].Count == 0 && wrongErrors.Any(e => e.Contains("require an explicit valid Beam or Truss property")), "P1: a cable is not a connection property: " + string.Join(" / ", wrongErrors));
        var (offsets, offsetErrors, _) = Solve("BridgeBeamOffsetsComponent", (0, new object[] { beams[0][0] }), (1, new Vector3d(0, 100, 0)));
        Check(offsetErrors.Length == 0 && offsets[0].Count == 1 && GpcOnly(offsets[0][0]), "P1: Bridge Beam Offsets gives the beam by its draft alone: " + string.Join(" / ", offsetErrors));
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // P6

    private static void Report()
    {
        var result = BridgeDetailPlotTests.Result();
        var entries = Enumerable.Range(0, 5).Select(i => new BridgeReportEntry(result.WithSource(new BridgeResultSource("B1", 200, "C" + i, "G1")), "{0}[" + i + "]")).ToArray();
        var data = BridgeReportData.Create(entries, new BridgeReportMetadata("P2K «report»", "Progetto à", "Tecnico", "01"));
        // The former computations through one string, written here: the hash of the UTF-8 of the JSON, and the round trip through a string.
        string former = BridgeReportData.Hash(Encoding.UTF8.GetBytes(BridgeReportData.Serialize(new { data.Model, data.Sections, data.Samples })));
        Check(data.DataHash == former, $"P6: the data hash is the hash of the UTF-8 of the JSON: {data.DataHash} / {former}");
        Check(BridgeReportData.HashOf(new { data.Model, data.Sections, data.Samples }) == former, "P6: HashOf equals the hash through the string");
        string json = BridgeReportData.Serialize(data);
        Check(BridgeReportData.Serialize(JsonSerializer.Deserialize<BridgeReportData>(json, BridgeReportData.JsonOptions)) == json, "P6: the detached data are a fixed point of the JSON round trip");
        using (var stream = new MemoryStream()) { BridgeReportData.Serialize(stream, data); Check(stream.ToArray().SequenceEqual(Encoding.UTF8.GetBytes(json)), "P6: the JSON on a stream has the bytes of the string"); }
        Check(data.Samples.Length == 5 && data.Samples.Select(s => s.Source.Combination).SequenceEqual(Enumerable.Range(0, 5).Select(i => "C" + i)), "P6: the five samples in order");
        string folder = Path.Combine(Path.GetTempPath(), "bridghe-p2k-report-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "report.docx");
        var written = BridgeWordReport.Write(BridgeReportTests.Template, file, data, new[] { BridgeReportTopic.Sections, BridgeReportTopic.Checks }, overwrite: true);
        using (var zip = ZipFile.OpenRead(file))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            var entry = zip.GetEntry("bridghe/report.json");
            Check(entry != null && names.Count(n => n == "bridghe/report.json") == 1, "P6: one snapshot entry in the DOCX");
            byte[] bytes; using (var s = entry.Open()) using (var m = new MemoryStream()) { s.CopyTo(m); bytes = m.ToArray(); }
            string text = Encoding.UTF8.GetString(bytes);
            Check(text.StartsWith("{" + Environment.NewLine + "  \"Schema\": 1,", StringComparison.Ordinal), "P6: the snapshot is the indented JSON of the report");
            using var doc = JsonDocument.Parse(bytes);
            var snapshot = JsonSerializer.Deserialize<BridgeReportData>(doc.RootElement.GetProperty("Data").GetRawText(), BridgeReportData.JsonOptions);
            Check(BridgeReportData.Serialize(snapshot) == json, "P6: the data of the snapshot are the data of the report");
            // The parts of the template first, the snapshot after the document parts, as before (the order of the dictionary of the parts).
            Check(names.IndexOf("bridghe/report.json") > names.IndexOf("word/document.xml") && names.IndexOf("bridghe/report.json") > names.IndexOf("[Content_Types].xml"),
                "P6: the snapshot entry after the document and the content types: " + string.Join(",", names));
        }
        Check(written.Results == 5, "P6: the report has the five results");
    }
}
