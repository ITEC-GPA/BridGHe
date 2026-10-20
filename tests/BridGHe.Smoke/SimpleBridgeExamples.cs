using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BridGHe.Common.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class SimpleBridgeExamples
{
    private static Assembly plugin;
    private static GH_Component Add(GH_Document doc, string type, float x, float y)
    {
        var c = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == type));
        c.CreateAttributes(); c.Attributes.Pivot = new PointF(x, y); doc.AddObject(c, false); return c;
    }
    private static GH_Panel Panel(GH_Document doc, string name, string text, float x, float y, int width = 250, int height = 110)
    {
        var p = new GH_Panel { NickName = name, UserText = text }; p.CreateAttributes();
        p.Properties.Multiline = false; p.Attributes.Pivot = new PointF(x, y); p.Attributes.Bounds = new RectangleF(x, y, width, height);
        doc.AddObject(p, false); return p;
    }
    private static void Numbers(GH_Document doc, GH_Component c, int port, string name, IEnumerable<double> values, float x, float y) =>
        c.Params.Input[port].AddSource(Panel(doc, name, string.Join("\n", values.Select(v => v.ToString("G17", CultureInfo.InvariantCulture))), x, y));

    private static void Solve(GH_Document doc)
    {
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        var errors = doc.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => c.Name + ": " + e)).ToArray();
        SimpleBridgeTests.Require(errors.Length == 0, string.Join("\n", errors));
    }

    internal static void Run(bool generate = false)
    {
        plugin = BridgeMergeTests.Initialize();
        var settingsType = plugin.GetType("BridGHe.Grasshopper.DataTypes.BridgeLayoutGoo", true);
        var settings = new BridgeSegments { Joints = new() { [2] = new[] { 18000d, 36000 } } };
        var originalGoo = (IGH_Goo)Activator.CreateInstance(settingsType, settings);
        var duplicateGoo = originalGoo.Duplicate();
        settings.Joints[2][0] = 17000;
        SimpleBridgeTests.Require(((BridgeSegments)settingsType.GetProperty("Value").GetValue(duplicateGoo)).Joints[2][0] == 18000, "Configuration deep-copy independence");
        foreach (object value in new object[] { settings, new BridgeDiaphragms { Pattern = BridgeTrussPattern.V, ConnectCrossings = true, Stations = new[] { 12000d } } })
        {
            var goo = (IGH_Goo)Activator.CreateInstance(settingsType, value);
            var archive = new GH_IO.Serialization.GH_Archive();
            SimpleBridgeTests.Require(archive.AppendObject(goo, "layout"), "Configuration archive write");
            var restored = (IGH_Goo)Activator.CreateInstance(settingsType);
            SimpleBridgeTests.Require(archive.ExtractObject(restored, "layout") && restored.IsValid &&
                JsonSerializer.Serialize(settingsType.GetProperty("Value").GetValue(restored), value.GetType()) == JsonSerializer.Serialize(value, value.GetType()),
                "Configuration archive preserves all settings");
        }
        foreach (var (number, truss, curved) in new[] { (29, false, false), (30, false, true), (31, true, false), (32, true, true) })
        {
            string id = $"{number}-simple-bridge-{(truss ? "reticolare" : "pieno")}{(curved ? "-curvo" : "")}";
            var input = SimpleBridgeTests.Input(truss, curved) with
            {
                Segments = new BridgeSegments { Joints = new() { [0] = new[] { 12000d, 24000, 36000, 48000, 60000, 72000, 84000 }, [2] = new[] { 18000d } } },
                SupportSkewDegrees = new[] { 0d, 12, -8, 0 },
                CrossfallStations = new[] { 0d, 50000, 100000 }, CrossfallPercent = new[] { -2d, 2, 3 },
                Diaphragms = new BridgeDiaphragms { Pattern = curved ? BridgeTrussPattern.Warren : BridgeTrussPattern.X }
            };
            using var doc = new GH_Document();
            Panel(doc, "PROCEDURA", $"{number} | SIMPLE BRIDGE {(truss ? "RETICOLARE" : "ANIMA PIENA")} | {(curved ? "CURVO R=250 m" : "RETTILINEO")}\n" +
                "Geometria in mm: campate 30+40+30 m, tre travi a -4/0/+4 m, soletta 12 m x 250 mm.\n" +
                "Modifica i pannelli: appoggi, interassi, quote, conci, traversi. Nessun carico, materiale, vincolo o analisi.\n" +
                "Il concio 24-36 m attraversa la pila a 30 m. Il giunto aggiuntivo a 18 m interessa solo G2.\n" +
                "Members + Member IDs hanno gli stessi percorsi. Famiglie: 0 travi, 1 traversi pieni, 2/3 correnti, 4 montanti, 5 diagonali.\n" +
                "Apri README.md per le prove guidate. Le quote sono assi di riferimento: non baricentri dedotti da sezioni.",
                0, -250, 1700, 190);
            var component = Add(doc, truss ? "SimpleBridgeTrussComponent" : "SimpleBridgeSolidComponent", 1120, 300);
            var alignment = new Param_Curve { NickName = "Tracciato 3D — mm" }; alignment.CreateAttributes();
            alignment.Attributes.Pivot = new PointF(820, 30); alignment.Hidden = true; alignment.PersistentData.Append(new GH_Curve(input.Alignment)); doc.AddObject(alignment, false);
            component.Params.Input[0].AddSource(alignment);
            Numbers(doc, component, 1, "Progressive appoggi [mm]", input.Supports, 500, 10);
            Numbers(doc, component, 2, "Offsets travi [mm]", input.GirderOffsets, 500, 150);
            Numbers(doc, component, 3, "Quote assi [mm]", input.GirderLevels, 500, 290);
            Numbers(doc, component, 4, "Larghezze SX / DX [mm]", input.DeckWidths, 500, 430);
            Numbers(doc, component, 5, "Spessore soletta [mm]", new[] { input.DeckThickness }, 500, 570);
            var slopes = new Param_Number { NickName = "Coppie progressiva / pendenza %" }; slopes.CreateAttributes();
            slopes.Attributes.Pivot = new PointF(820, 200); doc.AddObject(slopes, false);
            for (int i = 0; i < input.CrossfallStations.Length; i++) slopes.PersistentData.AppendRange(
                new[] { new GH_Number(input.CrossfallStations[i]), new GH_Number(input.CrossfallPercent[i]) }, new GH_Path(i));
            component.Params.Input[6].AddSource(slopes);
            var joints = new Param_Number { NickName = "Giunti mm: {0} comuni; {2} solo G2" }; joints.CreateAttributes();
            joints.Attributes.Pivot = new PointF(40, 210); doc.AddObject(joints, false);
            foreach (var branch in input.Segments.Joints) joints.PersistentData.AppendRange(branch.Value.Select(v => new GH_Number(v)), new GH_Path(branch.Key));
            var segments = Add(doc, "BridgeSegmentsComponent", 300, 210); segments.Params.Input[0].AddSource(joints); component.Params.Input[7].AddSource(segments.Params.Output[0]);
            var diaphragm = Add(doc, "BridgeDiaphragmsComponent", 300, 850);
            ((Param_Integer)diaphragm.Params.Input[5]).PersistentData.Clear(); ((Param_Integer)diaphragm.Params.Input[5]).SetPersistentData((int)input.Diaphragms.Pattern);
            component.Params.Input[8].AddSource(diaphragm.Params.Output[0]);
            Numbers(doc, diaphragm, 1, "Passo traversi [mm]", new[] { input.Diaphragms.MaximumSpacing }, 0, 650);
            Numbers(doc, diaphragm, 3, "Altezza reticolo [mm]", new[] { input.Diaphragms.TrussHeight }, 0, 790);
            Numbers(doc, component, 9, "Lunghezza massima [mm]", new[] { input.MaximumElementLength }, 800, 650);
            Numbers(doc, component, 10, "Errore di corda [mm]", new[] { input.ChordTolerance }, 800, 790);
            Numbers(doc, component, 11, "Sghembi appoggi [deg]", input.SupportSkewDegrees, 800, 930);
            foreach (var (port, label, y) in new[] { (13, "Informazioni", 20), (12, "Conci: girder;segment;start;end", 280), (11, "Topologia degli assi", 550) })
            {
                var panel = Panel(doc, label, "", 1460, y, 620, 230); panel.AddSource(component.Params.Output[port]);
            }
            var expected = SimpleBridgeBuilder.Build(input);
            void Validate(GH_Document document)
            {
                var c = document.Objects.OfType<GH_Component>().Single(o => o.GetType() == component.GetType());
                var axes = c.Params.Output[0].VolatileData.AllData(true).Select(g => (Curve)g.ScriptVariable()).ToArray();
                var ids = c.Params.Output[1].VolatileData.AllData(true).Select(g => (string)g.ScriptVariable()).ToArray();
                SimpleBridgeTests.Require(axes.Length == expected.Members.Count && ids.Length == axes.Length, "GH member count and IDs");
                var byId = expected.Members.ToDictionary(m => m.Id);
                for (int i = 0; i < axes.Length; i++)
                    SimpleBridgeTests.Require(axes[i].PointAtStart.DistanceTo(byId[ids[i]].Axis.From) < 1e-7 &&
                        axes[i].PointAtEnd.DistanceTo(byId[ids[i]].Axis.To) < 1e-7, "GH curve/ID pairing and numerical geometry");
                SimpleBridgeTests.Require(c.ClippingBox.IsValid && c.ClippingBox.Contains(expected.Nodes[0]), "Live preview bounding box");
                var deck = (Mesh)c.Params.Output[7].VolatileData.AllData(true).Single().ScriptVariable();
                SimpleBridgeTests.Require(deck.IsClosed && deck.IsValid, "GH slab geometry");
                SimpleBridgeTests.Require(c.Params.Output[5].VolatileDataCount == 12 && c.Params.Output[6].VolatileDataCount == expected.Joints.Count, "Support/joint output branches");
            }
            Solve(doc); Validate(doc);
            string folder = generate ? ExamplePaths.Case(id) : Path.GetFullPath(Path.Combine("artifacts", "simple-bridge-tests", id));
            Directory.CreateDirectory(folder); string path = Path.Combine(folder, id + ".ghx");
            GrasshopperBridgeExample.Write(doc, path);
            using (var reopened = GrasshopperBridgeExample.Read(path)) { Solve(reopened); Validate(reopened); }
            // Exercise the alternate V configuration after saving the example definition.
            if (truss)
            {
                var pattern = (Param_Integer)diaphragm.Params.Input[5];
                pattern.PersistentData.Clear(); pattern.SetPersistentData(1); diaphragm.ExpireSolution(false); Solve(doc);
                SimpleBridgeTests.Require(component.Params.Output[0].VolatileDataCount > 0, "V pattern solves in GH");
            }
            File.WriteAllText(Path.Combine(folder, "validation.json"), JsonSerializer.Serialize(new
            {
                example = id, geometryOnly = true, solverInvoked = false, solved = true, reopened = true,
                members = expected.Members.Count, nodes = expected.Nodes.Count, segments = expected.Segments.Count,
                supports = expected.Supports.Count, joints = expected.Joints.Count, truss, curved, pattern = input.Diaphragms.Pattern.ToString()
            }, new JsonSerializerOptions { WriteIndented = true }));
            Preview(expected, Path.Combine(folder, "preview.svg"), id);
            Console.WriteLine($"SIMPLE BRIDGE GH PASS: {id}, {expected.Members.Count} axes, solved and reopened");
        }
        MinimalInputs();
        Console.WriteLine("SIMPLE BRIDGE EXAMPLES PASS: 4 definitions, all three truss patterns, defaults and invalid-input preview reset; no solver invoked.");
    }

    private static void MinimalInputs()
    {
        using var doc = new GH_Document();
        var c = Add(doc, "SimpleBridgeSolidComponent", 0, 0);
        ((Param_Curve)c.Params.Input[0]).PersistentData.Append(new GH_Curve(SimpleBridgeTests.Input().Alignment));
        ((Param_Number)c.Params.Input[1]).SetPersistentData(new[] { 0d, 30000, 70000, 100000 });
        ((Param_Number)c.Params.Input[2]).SetPersistentData(new[] { -4000d, 0, 4000 });
        Solve(doc);
        SimpleBridgeTests.Require(c.Params.Output[0].VolatileDataCount > 0, "Only alignment, supports and offsets are required");
        var bad = (Param_Number)c.Params.Input[9]; bad.PersistentData.Clear(); bad.SetPersistentData(-1); c.ExpireSolution(false);
        doc.NewSolution(false, GH_SolutionMode.Silent);
        SimpleBridgeTests.Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0 && !c.ClippingBox.IsValid && c.Params.Output[0].VolatileDataCount == 0,
            "Invalid input clears old preview and outputs");
    }

    private static void Preview(SimpleBridgeGeometry geometry, string path, string title)
    {
        // Orthographic illustration from the actual generated geometry, not a design mock-up.
        (double X, double Y) Project(Point3d p) => (.8 * p.X - .6 * p.Y, .25 * (.6 * p.X + .8 * p.Y) - p.Z);
        var points = geometry.Nodes.Concat(geometry.LeftEdge).Concat(geometry.RightEdge).Select(Project).ToArray();
        double minX = points.Min(p => p.X), minY = points.Min(p => p.Y);
        double sx = 1120 / (points.Max(p => p.X) - minX), sy = 500 / (points.Max(p => p.Y) - minY), scale = Math.Min(sx, sy);
        string P(Point3d p) { var q = Project(p); return FormattableString.Invariant($"{40 + (q.X - minX) * scale:F2},{90 + (q.Y - minY) * scale:F2}"); }
        var svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"660\" viewBox=\"0 0 1200 660\"><rect width=\"1200\" height=\"660\" fill=\"#f3f6f8\"/>");
        svg.Append($"<text x=\"40\" y=\"35\" font-family=\"Segoe UI,Arial\" font-size=\"22\" fill=\"#203b50\">{title}</text>");
        svg.Append("<text x=\"40\" y=\"60\" font-family=\"Segoe UI,Arial\" font-size=\"14\" fill=\"#506070\">30 + 40 + 30 m · geometria senza carichi o vincoli · conci indipendenti dalle campate</text>");
        var deck = geometry.LeftEdge.Concat(geometry.RightEdge.AsEnumerable().Reverse()).Select(P);
        svg.Append($"<polygon points=\"{string.Join(" ", deck)}\" fill=\"#b9c5cd\" fill-opacity=\".24\" stroke=\"#8596a0\"/>");
        string[] colors = { "#137f86", "#bf641b", "#356cb5", "#8058a0", "#687784", "#c34a48" };
        foreach (var m in geometry.Members)
            svg.Append($"<polyline points=\"{P(m.Axis.From)} {P(m.Axis.To)}\" fill=\"none\" stroke=\"{colors[m.Family == BridgeMemberFamily.Girder ? (m.Segment - 1) % 3 : (int)m.Family]}\" stroke-width=\"{(m.Family == BridgeMemberFamily.Girder ? "2" : "1")}\"/>");
        foreach (var s in geometry.Supports)
        {
            var xy = P(s.Point).Split(',');
            svg.Append($"<circle cx=\"{xy[0]}\" cy=\"{xy[1]}\" r=\"3.5\" fill=\"#b72d3d\"/>");
        }
        svg.Append($"<text x=\"40\" y=\"635\" font-family=\"Segoe UI,Arial\" font-size=\"14\" fill=\"#506070\">{geometry.Members.Count} assi · {geometry.Nodes.Count} nodi · {geometry.Segments.Count} conci di trave · rosso: punti di appoggio geometrici</text></svg>");
        File.WriteAllText(path, svg.ToString());
    }
}
