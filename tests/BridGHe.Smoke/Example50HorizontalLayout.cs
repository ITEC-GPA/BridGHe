using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using System.Xml.Linq;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

/// <summary>Presentation only: moves the existing graph without solving it or replacing its data.</summary>
internal static class Example50HorizontalLayout
{
    private const string CaptionPrefix = "BGH50_LAYOUT_";
    private static readonly string[] Titles = {
        "01 | GEOMETRIA — tracciato, campate, travi, diaframmi e conci",
        "02 | MATERIALI E SEZIONI — acciaio, soletta e misto per concio",
        "03 | GRATICCIO COMUNE — aste, soletta, appoggi e collegamenti",
        "04 | MODELLO 0 · ACCIAIO (M01) — proprietà → carichi → FEM → fase 10",
        "05 | MODELLO 1 · n∞ (M02) — proprietà → carichi → FEM → fase 20",
        "06 | MODELLO 2 · n0 (M03) — proprietà → carichi → FEM → fase 30",
        "07 | MODELLO 3 · RITIRO (M04) — εcs → deformazioni → FEM → fase 40",
        "08 | ASSEMBLAGGIO — quattro modelli + fasi + combinazioni; preview FEM",
        "09 | VERIFICHE — sezioni, pioli, irrigiditori e riepilogo",
        "10 | GRAFICI — selezione trave, diagrammi e anteprime Grasshopper",
        "11 | REPORT — contenuti, template Word e generazione"
    };
    private static readonly Color[] Colors = {
        Color.SteelBlue, Color.SlateBlue, Color.CadetBlue, Color.DimGray,
        Color.DarkOrange, Color.SeaGreen, Color.MediumPurple, Color.Teal,
        Color.IndianRed, Color.RoyalBlue, Color.DarkGoldenrod
    };
    internal sealed record Stage(string Title, RectangleF Bounds, int Components);
    private sealed class Card
    {
        public GH_Component Component;
        public int Stage, Lane, Order;
        public readonly List<IGH_DocumentObject> Inputs = new(), Outputs = new();
        public readonly List<IGH_DocumentObject> Members = new();
        public RectangleF Bounds;
    }

    private static string Type(GH_Component c) => c.GetType().Name;
    private static int Phase(string name)
    {
        for (int i = 0; i < 4; i++) if (name.StartsWith("M0" + (i + 1), StringComparison.Ordinal)) return i;
        return -1;
    }

    private static (int Stage, int Lane)? Classify(GH_Component c)
    {
        int phase = Phase(c.NickName);
        string type = Type(c);
        if (phase >= 0)
        {
            int lane = type switch {
                "BridgeSegmentOverrideComponent" or "BridgeStateOverrideComponent" or "LoadCaseComponent" => 0,
                "BridgeSegmentCompositeComponent" or "CompositeShrinkageActionsComponent" => 1,
                "BeamDistributedLoadComponent" => 2,
                "BeamPreLoadComponent" or "BeamGradientLoadComponent" => 2,
                "BuildModelComponent" or "StrausR3AnalysisComponent" or "BridgeAnalysisCasesComponent" => 3,
                "BridgeActionComponent" or "BridgeShrinkageActionComponent" => 4,
                "BridgeConstructionPhaseComponent" => 5,
                _ => 1 // unit conversion / tributary strip load arithmetic
            };
            return (phase + 3, lane);
        }
        return type switch {
            "BridgeSpanStationsComponent" or "BridgeSupportStationsComponent" => (0,0),
            "BridgeAlignmentCurveComponent" or "BridgeRegularGirdersComponent" => (0,1),
            "BridgeSupportLayoutComponent" or "BridgeGirderLayoutComponent" or "BridgeDeckWidthsComponent" or "BridgeCrossfallTreeComponent" => (0,2),
            "BridgeDeckLayoutComponent" or "BridgeJointSetComponent" or "BridgeJointTreeComponent" or "BridgeNormalizeJointsComponent" => (0,3),
            "BridgeFabricationSegmentsComponent" or "BridgeSolidDiaphragmsComponent" or "BridgeTrussDiaphragmsComponent" => (0,4),
            "BridgeDiaphragmChoiceComponent" or "BridgeBracingStationsComponent" => (0,5),
            "BridgeGeometryAssemblyComponent" or "BridgeMeshSettingsComponent" => (0,6),
            "BridgeGrillageMeshComponent" => (0,7),
            "BridgeBearingPatternComponent" => (2,0),
            "BridgeSectionTemplateComponent" or "BridgeSectionReadComponent" => (1,4),
            "BridgeFamilyPropertyComponent" => (2,0),
            "BridgeFemMembersComponent" => (2,1),
            "BridgeRigidLinksComponent" or "BridgeConnectionBeamsComponent" or "BridgeBeamOffsetsComponent" => (2,2),
            "BeamPropertyOverrideComponent" or "GetBeamPropertyComponent" => (2,3),
            "BeamMomentDispatchComponent" => (3,7),
            "BridgeParametricLayoutComponent" => (0, 0),
            "SimpleBridgeConfigurableComponent" => (0, 1),
            "BridgeSegmentsComponent" or "BridgeDiaphragmsComponent" or "BridgeLongitudinalBracingComponent" => (0, 2),
            "MaterialSteelComponent" or "MaterialConcreteComponent" or "MaterialRebarComponent" => (1, 0),
            "MaterialCustomComponent" => (1, 1),
            "BeamSectionIComponent" or "BeamSectionHollowRectComponent" or "BeamSectionConcreteSlabComponent" => (1, 2),
            "BeamPropertyComponent" => (1, 3),
            "BridgeSegmentSectionsComponent" => (1, 4),
            "BeamPropertyCompositeComponent" => (1, 5),
            "BridgeMemberAssignmentComponent" => (2, 0),
            "BridgeSectionScheduleComponent" => (2, 1),
            "BridgeSlabGrillageComponent" or "BridgeBearingsComponent" or "BridgeConnectionsComponent" => (2, 2),
            "CreepAndShrinkageConcreteComponent" => (6, 0),
            "BuildBridgeModel" => (7, 1),
            "BridgeModelAuditComponent" or "DisplaySettingsComponent" or "PreviewModelComponent" => (7, 3),
            "DecomposeModelComponent" => c.NickName.StartsWith("M0 calcolato") ? (3,5) : c.NickName.StartsWith("Estrai") ? (8, 0) : (7, 3),
            "BridgeSegmentCompositeComponent" => c.NickName.StartsWith("Solo longitudinali") ? (3,6) : (8, 1),
            "CompositeBeamComponent" or "BridgeAnalysisOptionsComponent" or "BridgeStudsComponent" => (8, 2),
            "SuperElementCheckComponent" => (8, 3),
            "BridgeDetailCheckComponent" => (8, c.NickName.StartsWith("Sezioni") ? 4 : 7),
            "BridgeDetailStationsComponent" => (8, 5),
            "BridgeIntermediateStiffenerComponent" or "BridgeSupportStiffenerComponent" => (8, 6),
            "BridgeResultLabelsComponent" => (8, 8),
            "BridgeCheckSummaryComponent" => (8, 9),
            "BridgeReportComponent" => (10, 0),
            _ when type.StartsWith("Bridge") && (type.Contains("Plot") || type.Contains("GirderResults")) => (9, 0),
            _ when c.NickName == "Geometria con/senza controventi" => (0, 3),
            _ when c.NickName == "Modello FEM visualizzato" => (7, 2),
            _ when c.NickName.StartsWith("Tabella combinazioni") => (7, 0),
            _ => null
        };
    }

    internal static Stage[] Apply(GH_Document doc)
    {
        Console.WriteLine("HORIZONTAL LAYOUT: replace annotation groups.");
        foreach (var o in doc.Objects.Where(o => o is GH_Group || o.Name.StartsWith(CaptionPrefix)).Reverse().ToArray()) doc.RemoveObject(o, false);
        Console.WriteLine("HORIZONTAL LAYOUT: classify and arrange graph.");
        var objects = doc.Objects.ToArray();
        var cards = objects.OfType<GH_Component>().Select((c, i) => new Card { Component = c, Order = i }).ToArray();
        var outputOwners = cards.SelectMany(c => c.Component.Params.Output.Select(p => (p, c))).ToDictionary(x => x.p, x => x.c);
        var assigned = new HashSet<Card>();
        foreach (var card in cards)
        {
            var position = Classify(card.Component);
            if (position.HasValue) { (card.Stage, card.Lane) = position.Value; assigned.Add(card); }
        }
        // Standard arithmetic belongs beside its first downstream consumer, including chained operators.
        for (int pass = 0; pass < cards.Length; pass++)
        {
            bool changed = false;
            foreach (var card in cards.Where(c => !assigned.Contains(c)))
            {
                var next = cards.Where(c => assigned.Contains(c) && c.Component.Params.Input.SelectMany(p => p.Sources)
                    .Any(p => outputOwners.TryGetValue(p, out var source) && source == card)).OrderBy(c => c.Stage).ThenBy(c => c.Lane).FirstOrDefault();
                if (next == null) continue;
                card.Stage = next.Stage; card.Lane = Math.Max(0, next.Lane - 1); assigned.Add(card); changed = true;
            }
            if (!changed) break;
        }
        // User-saved revisions can contain disconnected experimental branches. Keep them visible,
        // in their own lane, rather than discarding them or treating them as part of the phase chain.
        foreach(var card in cards.Except(assigned))
        {card.Stage=2;card.Lane=4;if(!card.Component.NickName.StartsWith("PROVA",StringComparison.Ordinal))card.Component.NickName="PROVA UTENTE | "+card.Component.NickName;}
        // Six complete plot chains, arranged in two horizontal triptychs.
        var plotNames = new[] { "Momento Mx", "Tensioni acciaio sigma 5", "Tau massima", "Pioli Ed e Rd", "Irrigiditori intermedi WR", "Irrigiditori appoggi WR" };
        foreach (var card in cards.Where(c => c.Stage == 9))
        {
            int i = Array.FindIndex(plotNames, p => card.Component.NickName.StartsWith(p + " |", StringComparison.Ordinal));
            if (i < 0) throw new InvalidOperationException("Unknown plot chain " + card.Component.NickName);
            card.Lane = i / 3 * 3 + (Type(card.Component).Contains("GirderResults") ? 0 : Type(card.Component).Contains("Preview") ? 2 : 1);
            card.Order = i % 3;
        }
        var owner = cards.ToDictionary(c => (IGH_DocumentObject)c.Component);
        IEnumerable<Card> SourceCards(IGH_Param p,HashSet<IGH_Param> visited)
        {
            if(!visited.Add(p))yield break;
            if(outputOwners.TryGetValue(p,out var direct)){yield return direct;yield break;}
            foreach(var source in p.Sources)foreach(var card in SourceCards(source,visited))yield return card;
        }
        foreach (var p in objects.OfType<IGH_Param>())
        {
            Card card;
            if (p.SourceCount > 0)
            {
                var sources = SourceCards(p,new()).Distinct().OrderBy(c => c.Stage).ThenBy(c => c.Lane).ToArray();
                card = sources.FirstOrDefault() ?? cards.OrderBy(c=>c.Stage).ThenBy(c=>c.Lane).First(); card.Outputs.Add(p);
            }
            else
            {
                card = cards.Where(c => c.Component.Params.Input.Any(i => i.Sources.Contains(p))).OrderBy(c => c.Stage).ThenBy(c => c.Lane).ThenBy(c => c.Order).FirstOrDefault();
                if (card == null) card = cards.Where(c => c.Stage == (p.NickName.StartsWith("IPOTESI") ? 8 : 0)).OrderBy(c => c.Lane).First();
                card.Inputs.Add(p);
            }
            owner.Add(p, card);
        }
        foreach(var note in objects.Where(o=>!owner.ContainsKey(o)))
        {var first=cards.OrderBy(c=>c.Stage).ThenBy(c=>c.Lane).First();first.Inputs.Add(note);owner.Add(note,first);}
        foreach (var card in cards)
        foreach (var input in card.Component.Params.Input)
        {
            bool distant = input.Sources.Any(p => outputOwners.TryGetValue(p, out var source) ? source.Stage != card.Stage : owner.TryGetValue(p, out var inputOwner) && inputOwner.Stage != card.Stage);
            input.WireDisplay = distant ? GH_ParamWireDisplay.hidden : GH_ParamWireDisplay.@default;
        }
        foreach (var card in cards)
        {
            card.Members.AddRange(card.Inputs); card.Members.Add(card.Component); card.Members.AddRange(card.Outputs);
            foreach (var o in card.Members) Prepare(o);
        }
        var stages = new List<Stage>();
        float x = 100;
        for (int stage = 0; stage < Titles.Length; stage++)
        {
            float start = x;
            var stageCards = cards.Where(c => c.Stage == stage).ToArray();
            var cardGroups = new List<IGH_DocumentObject>();
            // Plot rows share the same baseline and height across filter/plot/preview columns.
            float plotRowHeight = stage == 9 ? stageCards.Max(Height) + 140 : 0;
            foreach (var lane in stageCards.GroupBy(c => c.Lane).OrderBy(g => g.Key))
            {
                float y = 480, width = 0;
                foreach (var card in lane.OrderBy(c => c.Order))
                {
                    Place(card, x, stage == 9 ? 480 + card.Order * plotRowHeight : y);
                    width = Math.Max(width, card.Bounds.Width);
                    y = card.Bounds.Bottom + 150;
                    cardGroups.Add(AddGroup(doc, card.Component.NickName, Colors[stage], 18, card.Members));
                }
                x += width + 200;
            }
            var bounds = Union(stageCards.Select(c => c.Bounds));
            var title = new GH_Panel { Name = CaptionPrefix + stage, NickName = Titles[stage], UserText = Titles[stage] + "\nInput espliciti a sinistra di ciascun componente; output nominati a destra. Seguire il flusso →" };
            title.CreateAttributes(); title.Attributes.Pivot = new PointF(start, 100);
            title.Attributes.Bounds = new RectangleF(start, 100, Math.Min(2600, bounds.Width), 150);
            title.Properties.Colour = Color.FromArgb(255, (Colors[stage].R + 510) / 3, (Colors[stage].G + 510) / 3, (Colors[stage].B + 510) / 3);
            title.Properties.Font = new Font("Arial", 24, FontStyle.Bold);
            doc.AddObject(title, false);
            // The outer group contains the cards, without duplicating their memberships.
            AddGroup(doc, Titles[stage], Colors[stage], 28, cardGroups.Append(title));
            stages.Add(new Stage(Titles[stage], RectangleF.Union(bounds, title.Attributes.Bounds), stageCards.Length));
            x += 500;
        }
        ValidateBounds(objects);
        return stages.ToArray();
    }

    private static void Prepare(IGH_DocumentObject o)
    {
        o.Attributes.PerformLayout();
        var b = o.Attributes.Bounds;
        if (o is GH_NumberSlider || o is GH_Panel)
        {
            float width = Math.Max(620, TextRenderer.MeasureText(o.NickName, SystemFonts.MessageBoxFont).Width + 140);
            float height = o is GH_Panel ? Math.Max(110, b.Height) : 26;
            if (o.NickName == "50 | QUATTRO MODELLI DI CALCOLO") { width = 1400; height = 470; }
            if (o.NickName.StartsWith("IPOTESI")) { width = 1000; height = 190; }
            o.Attributes.Bounds = new RectangleF(b.X, b.Y, width, height);
        }
        o.Attributes.ExpireLayout(); o.Attributes.PerformLayout();
    }
    private static float StackHeight(IEnumerable<IGH_DocumentObject> objects) => objects.Sum(o => Math.Max(30, o.Attributes.Bounds.Height) + 35);
    private static float Height(Card c) => Math.Max(c.Component.Attributes.Bounds.Height, Math.Max(StackHeight(c.Inputs), StackHeight(c.Outputs))) + 40;
    private static void Move(IGH_DocumentObject o, float x, float y)
    {
        var b = o.Attributes.Bounds; var pivot = o.Attributes.Pivot;
        o.Attributes.Pivot = new PointF(pivot.X + x - b.Left, pivot.Y + y - b.Top);
        o.Attributes.ExpireLayout(); o.Attributes.PerformLayout();
    }
    private static void Place(Card c, float x, float y)
    {
        float inputWidth = c.Inputs.Select(o => o.Attributes.Bounds.Width).DefaultIfEmpty(0).Max();
        float iy = y;
        foreach (var o in c.Inputs) { Move(o, x, iy); iy += Math.Max(30, o.Attributes.Bounds.Height) + 35; }
        Move(c.Component, x + inputWidth + 100, y + 20);
        float ox = c.Component.Attributes.Bounds.Right + 120, oy = y;
        foreach (var o in c.Outputs) { Move(o, ox, oy); oy += Math.Max(30, o.Attributes.Bounds.Height) + 35; }
        c.Bounds = Union(c.Members.Select(o => o.Attributes.Bounds));
    }
    private static RectangleF Union(IEnumerable<RectangleF> bounds) => bounds.Aggregate(RectangleF.Union);
    private static GH_Group AddGroup(GH_Document doc, string title, Color color, int alpha, IEnumerable<IGH_DocumentObject> objects)
    {
        var g = new GH_Group { NickName = title, Colour = Color.FromArgb(alpha, color), Border = GH_GroupBorder.Box };
        g.CreateAttributes(); doc.AddObject(g, false);
        foreach (var o in objects) g.AddObject(o.InstanceGuid);
        return g;
    }
    private static void ValidateBounds(IGH_DocumentObject[] objects)
    {
        for (int i = 0; i < objects.Length; i++)
        for (int j = i + 1; j < objects.Length; j++)
        {
            var overlap = RectangleF.Intersect(objects[i].Attributes.Bounds, objects[j].Attributes.Bounds);
            if (overlap.Width > 1 && overlap.Height > 1) throw new InvalidOperationException("Overlapping objects: " + objects[i].NickName + " / " + objects[j].NickName);
        }
    }

    private static XElement ObjectList(XDocument xml) => xml.Descendants("chunk").Single(c => (string)c.Attribute("name") == "DefinitionObjects").Element("chunks");
    private static XElement Container(XElement o) => o.Element("chunks").Elements("chunk").Single(c => (string)c.Attribute("name") == "Container");
    private static string Item(XElement c, string name) => c.Element("items")?.Elements("item").SingleOrDefault(i => (string)i.Attribute("name") == name)?.Value;
    private static string Identity(XElement o) => Item(Container(o), "InstanceGuid");
    private static bool Annotation(XElement o) => Item(o, "GUID") == "c552a431-af5b-46a9-a8a4-0fcbc27ef596" || (Item(Container(o), "Name") ?? "").StartsWith(CaptionPrefix);
    private static string Fingerprint(XDocument xml)
    {
        var functional = ObjectList(xml).Elements("chunk").Where(o => !Annotation(o)).Select(o => new XElement(o)).ToArray();
        foreach (var o in functional)
        {
            o.Attribute("index")?.Remove();
            o.Descendants("chunk").Where(c => (string)c.Attribute("name") == "Attributes").Remove();
            o.Descendants("item").Where(i => (string)i.Attribute("name") == "WireDisplay").Remove();
            // Item counts include the optional WireDisplay presentation setting.
            foreach (var items in o.Descendants("items")) items.SetAttributeValue("count", items.Elements("item").Count());
        }
        string canonical = string.Concat(functional.OrderBy(Identity).Select(o => o.ToString(SaveOptions.DisableFormatting)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
    // Retain the original XML for ALL non-layout data, including opaque caches and persistent inputs.
    private static XDocument PatchOnlyAttributes(XDocument original, XDocument laidOut)
    {
        var result = new XDocument(original);
        var target = ObjectList(result);
        var positions = ObjectList(laidOut).Elements("chunk").ToDictionary(Identity);
        foreach (var o in target.Elements("chunk").ToArray())
        {
            if (Annotation(o)) { o.Remove(); continue; }
            if (!positions.TryGetValue(Identity(o), out var updated)) throw new InvalidOperationException("Lost object " + Identity(o));
            var oldAttrs = o.Descendants("chunk").Where(c => (string)c.Attribute("name") == "Attributes").ToArray();
            var newAttrs = updated.Descendants("chunk").Where(c => (string)c.Attribute("name") == "Attributes").ToArray();
            if (oldAttrs.Length != newAttrs.Length) throw new InvalidOperationException("Attribute structure changed: " + Identity(o));
            for (int i = 0; i < oldAttrs.Length; i++) oldAttrs[i].ReplaceWith(new XElement(newAttrs[i]));
            var display = Container(updated).Element("chunks").Elements("chunk").Where(c => (string)c.Attribute("name") == "param_input")
                .ToDictionary(c => Item(c, "InstanceGuid"), c => c.Element("items").Elements("item").SingleOrDefault(i => (string)i.Attribute("name") == "WireDisplay"));
            foreach (var chunk in Container(o).Element("chunks").Elements("chunk").Where(c => (string)c.Attribute("name") == "param_input"))
            {
                if (!display.TryGetValue(Item(chunk, "InstanceGuid"), out var wire)) continue;
                var items = chunk.Element("items");
                items.Elements("item").Where(i => (string)i.Attribute("name") == "WireDisplay").Remove();
                if (wire != null) items.Add(new XElement(wire));
                items.SetAttributeValue("count", items.Elements("item").Count());
            }
        }
        target.Add(positions.Values.Where(Annotation).Select(o => new XElement(o)));
        int index = 0;
        foreach (var o in target.Elements("chunk")) o.SetAttributeValue("index", index++);
        target.SetAttributeValue("count", index);
        target.Parent.Element("items").Elements("item").Single(i => (string)i.Attribute("name") == "ObjectCount").Value = index.ToString();
        return result;
    }

    private static XDocument WithoutAnnotations(XDocument original)
    {
        // Strip presentation groups before deserialization: removing and rebuilding many live
        // groups can retain stale attribute dependencies in the Grasshopper runtime.
        var result = new XDocument(original);
        var list = ObjectList(result);
        list.Elements("chunk").Where(Annotation).Remove();
        int index = 0;
        foreach (var o in list.Elements("chunk")) o.SetAttributeValue("index", index++);
        list.SetAttributeValue("count", index);
        list.Parent.Element("items").Elements("item").Single(i => (string)i.Attribute("name") == "ObjectCount").Value = index.ToString();
        return result;
    }

    internal static int Run()
    {
        Console.WriteLine("HORIZONTAL LAYOUT: register Grasshopper libraries; solutions disabled.");
        GH_Document.EnableSolutions = false;
        BridgeMergeTests.Initialize();
        var math = Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\MathComponents.gha");
        typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Assembly), typeof(string) }, null)
            .Invoke(Grasshopper.Instances.ComponentServer, new object[] { math, math.Location });
        string folder = ExamplePaths.Case(ParametricBridgePhasesExample.Id);
        string path = Path.Combine(folder, ParametricBridgePhasesExample.Id + ".ghx");
        string artifact = Path.Combine("artifacts", "example50-horizontal"); Directory.CreateDirectory(artifact);
        var before = XDocument.Load(path);
        if (Environment.GetCommandLineArgs().Contains("--capture-layout"))
        {
            using var display = GrasshopperBridgeExample.Read(path);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "layout-validation.json")));
            var zones = manifest.RootElement.GetProperty("Stages").EnumerateArray().Select(s => new Stage(s.GetProperty("Title").GetString(),
                new RectangleF(s.GetProperty("X").GetSingle(), 100, s.GetProperty("Width").GetSingle(), s.GetProperty("Height").GetSingle()), s.GetProperty("Components").GetInt32())).ToArray();
            Capture(display, zones, artifact);
            Console.WriteLine("HORIZONTAL CANVAS CAPTURE PASS: no definition written.");
            return 0;
        }
        Console.WriteLine("HORIZONTAL LAYOUT: read existing definition.");
        string input = Path.Combine(artifact, "layout-input.ghx"); WithoutAnnotations(before).Save(input);
        using var doc = GrasshopperBridgeExample.Read(input);
        int components = doc.Objects.OfType<GH_Component>().Count();
        var oldBounds = Union(doc.Objects.Where(o => o is not GH_Group && !o.Name.StartsWith(CaptionPrefix)).Select(o => o.Attributes.Bounds));
        var stages = Apply(doc);
        Console.WriteLine("HORIZONTAL LAYOUT: positioned all objects; no intersections.");
        string runtime = Path.Combine(artifact, "runtime-layout.ghx"); GrasshopperBridgeExample.Write(doc, runtime);
        var patched = PatchOnlyAttributes(before, XDocument.Load(runtime));
        string fingerprint = Fingerprint(before);
        if (Fingerprint(patched) != fingerprint) throw new InvalidOperationException("Layout changed functional content.");
        string candidate = Path.Combine(artifact, "candidate.ghx"); patched.Save(candidate);
        using var reopened = GrasshopperBridgeExample.Read(candidate);
        Console.WriteLine("HORIZONTAL LAYOUT: reopened candidate; functional XML preserved.");
        if (reopened.Objects.OfType<GH_Component>().Count() != components) throw new InvalidOperationException("Lost components on reopen.");
        var functional = reopened.Objects.Where(o => o is not GH_Group && !o.Name.StartsWith(CaptionPrefix)).ToArray();
        Console.WriteLine("HORIZONTAL LAYOUT: lay out reopened functional objects.");
        foreach (var o in functional) o.Attributes.PerformLayout();
        ValidateBounds(functional);
        Console.WriteLine("HORIZONTAL LAYOUT: reopened bounds checked.");
        int wires = reopened.Objects.OfType<IGH_Param>().Concat(reopened.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input)).Sum(p => p.SourceCount);
        var afterBounds = Union(functional.Select(o => o.Attributes.Bounds));
        if (afterBounds.Width < afterBounds.Height * 3) throw new InvalidOperationException("The layout must be predominantly horizontal.");
        string backup = Path.Combine(artifact, "before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ghx"); File.Copy(path, backup);
        File.Copy(candidate, path, true);
        File.WriteAllText(Path.Combine(folder, "layout-validation.json"), JsonSerializer.Serialize(new {
            Mode = "horizontal-layout-only", NativeSolverInvoked = false, Components = components, FunctionalObjects = functional.Length,
            Connections = wires, FunctionalContentSha256 = fingerprint, AllInputsAndCachesPreserved = true,
            NoOverlaps = true, Reopened = true,
            Before = new { Width = oldBounds.Width, Height = oldBounds.Height }, After = new { Width = afterBounds.Width, Height = afterBounds.Height },
            Stages = stages.Select(s => new { s.Title, s.Components, X = s.Bounds.X, Width = s.Bounds.Width, Height = s.Bounds.Height })
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"HORIZONTAL LAYOUT PASS: {components} components, {functional.Length} functional objects, {wires} connections; exact input/cache preservation; no overlaps; reopened; no solver invoked.");
        return 0;
    }
    internal static int CaptureModular()
    {
        BridgeExampleMigration.Initialize();
        string path=Path.Combine(ExamplePaths.Root,"50-ponte-quattro-modelli","50-ponte-quattro-modelli_REV002.ghx");
        string folder=Path.GetFullPath("artifacts/modular-bridge/canvas");Directory.CreateDirectory(folder);
        using var doc=GrasshopperBridgeExample.Read(path);
        var stages=Apply(doc);
        Capture(doc,stages,folder,new[]{0,1,3,8});
        File.WriteAllText(Path.Combine(folder,"stages.json"),JsonSerializer.Serialize(stages.Select(s=>new{s.Title,s.Components,s.Bounds.X,s.Bounds.Y,s.Bounds.Width,s.Bounds.Height}),new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("MODULAR CANVAS CAPTURE PASS: horizontal stages; no GHX written.");return 0;
    }
    private static void Capture(GH_Document doc, Stage[] stages, string directory, int[] selected=null)
    {
        GH_Skin.LoadSkin();
        using var canvas = new GH_Canvas();
        canvas.Size = new Size(2200, 1200); canvas.Document = doc;
        foreach (var panel in doc.Objects.OfType<GH_Panel>()) { panel.CollectData(); panel.ComputeData(); }
        foreach (int index in selected??new[] { 3 })
        {
            var bounds = stages[index].Bounds;
            float zoom = Math.Min(2100 / bounds.Width, 1100 / bounds.Height);
            canvas.Viewport.Zoom = zoom;
            canvas.Viewport.Target = new Point((int)(50 - bounds.X * zoom), (int)(50 - bounds.Y * zoom));
            using var bitmap = canvas.GetCanvasScreenBuffer(GH_CanvasMode.Export);
            bitmap.Save(Path.Combine(directory, $"stage-{index + 1:00}.png"));
        }
        canvas.Document = null;
    }
}
