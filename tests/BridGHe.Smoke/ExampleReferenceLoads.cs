using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.MovingLoads;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

/// <summary>
/// Examples 50 REV004 and 99 REV005: the phase loads of the company reference script for composite bridges (GH04, read as data):
/// slab and casting on the steel section, G2 (pavement, curbs, safety barriers, parapets, finishes) long term n∞, traffic and the other
/// variable actions short term n0. Geometry, sections and the modular flow of the source revisions are kept; the source revisions are not
/// changed. Usage: <c>--example-reference-loads 50|99 [--check]</c>. Without <c>--check</c> the new revision is generated (refused when it
/// exists); with <c>--check</c> the saved revision is reopened and checked offline (no Straus run).
/// </summary>
internal static class ExampleReferenceLoads
{
    // Standard Grasshopper components (Math, Sets).
    private const string Multiplication = "ce46b74e-00c9-43c4-805a-193b69ea4a11", Division = "9c85271f-89fa-4e9f-9f4a-d75802120ccc",
        Subtraction = "9c007a04-d0d9-48e4-9da3-9ba142bc4d46", Addition = "a0d62394-a118-422d-abb3-6af115c75b25",
        NegativeGuid = "a3371040-e552-4bc8-b0ff-10a840258e88", Maximum = "0d1e2027-f153-460d-84c0-f9af431b08cb",
        Minimum = "57308b30-772d-4919-ac67-e86c18f3a996", PartitionList = "5a93246d-2595-4c28-bc2d-90657634f92a",
        DuplicateData = "dd8134c0-109b-4012-92be-51d843edfff7", ListItemGuid = "59daf374-bc21-4a5e-8282-5504fb7ae9ae",
        ListLengthGuid = "1817fd29-20ae-4503-b542-f0fb651e67d7", VectorXyz = "56b92eab-d121-43f7-94d3-6cd8f0ddead8",
        MergeGuid = "3cadddef-1e2b-4c09-9390-0e8f78f7609f";

    internal static int Run(string[] args)
    {
        string which = args.SkipWhile(a => a != "--example-reference-loads").Skip(1).FirstOrDefault(a => !a.StartsWith("--"));
        BridgeExampleMigration.Initialize();
        bool check = args.Contains("--check");
        switch (which)
        {
            case "50": if (check) ExampleReferenceLoads50.Check(ExampleReferenceLoads50.Target); else ExampleReferenceLoads50.Generate(); return 0;
            case "99": if (check) ExampleReferenceLoads99.Check(ExampleReferenceLoads99.Target); else ExampleReferenceLoads99.Generate(); return 0;
            case "native":
                return Native(args.SkipWhile(a => a != "native").Skip(1).First(), args.SkipWhile(a => a != "--input").Skip(1).First());
            case "baseline":
                // The offline errors of the source revisions, for comparison with the new ones (no file written).
                foreach (var source in new[] { ExampleReferenceLoads50.Source, ExampleReferenceLoads99.Source })
                {
                    using var doc = GrasshopperBridgeExample.Read(source); doc.FilePath = source;
                    Neutralize(doc, "baseline-" + Path.GetFileNameWithoutExtension(source));
                    BridgeSegmentExcelTests.Solve(doc, true);
                    foreach (var read in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeSectionReadComponent")) { BridgeSegmentExcelTests.Press(read); read.ExpireSolution(false); }
                    BridgeSegmentExcelTests.Solve(doc, true);
                    foreach (var o in doc.Objects.OfType<GH_ActiveObject>()) foreach (var e in o.RuntimeMessages(GH_RuntimeMessageLevel.Error))
                        Console.WriteLine($"BASELINE {Path.GetFileName(source)}: {o.Name} | {o.NickName}: {e}");
                }
                return 0;
            default: throw new ArgumentException("--example-reference-loads 50|99 [--check]");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // Native run (Straus7 licence, coordinator's locks): a COPY of the new revision, analyses redirected into artifacts/reference-loads.

    /// <summary>
    /// <c>--example-reference-loads native 50|99 --input &lt;copy&gt;</c>: every Straus analysis pressed, the reactions of every result case against
    /// the independent integration of the loads of its GPC input (Z with self-weight; X and Y of the beam and node forces), the moving positions
    /// against the vehicle, the merge and the section checks. The copy must sit outside the example folder, with its workbooks beside it.
    /// </summary>
    internal static int Native(string which, string input)
    {
        input = Path.GetFullPath(input);
        Require(!input.StartsWith(ExamplePaths.Root, StringComparison.OrdinalIgnoreCase), "run natives on a copy outside the example folder");
        byte[] bytes = File.ReadAllBytes(input);
        var report = new List<object>();
        using var doc = GrasshopperBridgeExample.Read(input); doc.FilePath = input;
        Neutralize(doc, "native-" + which);
        BridgeSegmentExcelTests.Solve(doc, true);
        foreach (var read in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeSectionReadComponent")) { BridgeSegmentExcelTests.Press(read); read.ExpireSolution(false); }
        BridgeSegmentExcelTests.Solve(doc, true);
        static object One(IGH_Param p) => p.VolatileData.AllData(true).SingleOrDefault();
        var analyses = doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis")
            .OrderBy(c => Path.GetFileName(c.Params.Input.Single(p => p.Name == "Output Path").Sources.OfType<GH_Panel>().Single().UserText), StringComparer.Ordinal).ToArray();
        if (which == "50") analyses = analyses.OrderBy(c => c.NickName, StringComparer.Ordinal).ToArray();
        else analyses = analyses.OrderBy(c => Array.IndexOf(new[] { "G1.st7", "G2.st7", "Q.st7", "RITIRO.st7" },
            Path.GetFileName(c.Params.Input.Single(p => p.Name == "Output Path").Sources.OfType<GH_Panel>().Single().UserText))).ToArray();
        foreach (var a in analyses)
        {
            var gpc = R4cMoving.Input(One(In(a, "Model")));
            Require(gpc != null && gpc.Model.BeamElements.Count > 0, "no GPC input: " + a.NickName + "\n" + string.Join("\n", ErrorLines(doc)));
            Console.WriteLine($"REFERENCE LOADS NATIVE: {a.NickName}, {gpc.Model.BeamElements.Count} beams");
            BridgeMovingRevisionExample.Press(a); BridgeSegmentExcelTests.Solve(doc, true);
            var result = R4cMoving.Results(One(Out(a, "Model")));
            Require(result != null && result.HasBeamRows(), "no native results: " + a.NickName + "\n" + string.Join("\n", ErrorLines(doc)));
            foreach (string label in result.ReactionLabels())
            {
                double z = R4cMoving.VerticalLoad(gpc.Model, label, result.Catalog.Units);
                var h = Horizontal(gpc.Model, label);
                var rows = result.NodeRows.SelectMany(r => r.Where(x => x.Reaction != null && result.Label(x.Entry) == label)).ToArray();
                double rx = rows.Sum(r => r.Reaction[0]), ry = rows.Sum(r => r.Reaction[1]), rz = rows.Sum(r => r.Reaction[2]);
                double ex = -result.Catalog.Units.FromN(h.X), ey = -result.Catalog.Units.FromN(h.Y);
                double tol = Math.Max(result.Catalog.Units.FromN(1), 1e-6 * (Math.Abs(z) + Math.Abs(ex) + Math.Abs(ey)));
                Near(report, $"{a.NickName} | {label} | Rz", rz, z, tol / Math.Max(1, Math.Abs(z)));
                Near(report, $"{a.NickName} | {label} | Rx", rx, ex, tol / Math.Max(1, Math.Abs(ex)));
                Near(report, $"{a.NickName} | {label} | Ry", ry, ey, tol / Math.Max(1, Math.Abs(ey)));
            }
        }
        var moving = doc.Objects.OfType<GH_Component>().SingleOrDefault(c => c.Name == "Moving Static Analysis");
        if (moving != null)
        {
            BridgeMovingRevisionExample.Press(moving); BridgeSegmentExcelTests.Solve(doc, true);
            var traffic = R4cMoving.Results(One(Out(moving, "Model")));
            Require(traffic != null, "moving analysis failed:\n" + string.Join("\n", ErrorLines(doc)));
            var path = R4cMoving.Input(One(In(moving, "Model"))).Model.MovingLoads.Paths.Single();
            double vehicle = -path.Template.Vehicles.Single().PointForces.Sum(p => p.Force.Z);
            var cases = traffic.Catalog.GeneratedMovingCases().Select(g => g.Name).ToArray();
            var totals = cases.Select(traffic.ReactionZ).ToArray();
            report.Add(new { Check = "posizioni mobili", Count = cases.Length, Pass = cases.Length > 0 });
            Near(report, "reazione verticale massima delle posizioni = peso del veicolo [N]", totals.Max(), traffic.Catalog.Units.FromN(vehicle), 1e-6);
            True(report, "nessuna posizione oltre il peso del veicolo", totals.All(t => t <= traffic.Catalog.Units.FromN(vehicle) * (1 + 1e-6) && t >= -1e-6));
        }
        foreach (var excel in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Bridge Combination Excel" && Values(In(c, "Mode")).Select(Convert.ToInt32).FirstOrDefault() == 1))
        { BridgeMovingRevisionExample.Press(excel); BridgeSegmentExcelTests.Solve(doc, true); }
        var merge = doc.Objects.OfType<GH_Component>().Single(c => c.Name == "Build BridGHe Model");
        BridgeMovingRevisionExample.Press(merge); BridgeSegmentExcelTests.Solve(doc, true);
        var bridge = R4cMoving.Bridge(One(Out(merge, "Model")));
        var mergeMessages = new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark }
            .SelectMany(l => merge.RuntimeMessages(l).Select(m => l + ": " + m)).ToArray();
        foreach (var m in mergeMessages) Console.WriteLine("REFERENCE LOADS NATIVE merge " + m);
        Console.WriteLine($"REFERENCE LOADS NATIVE merge outputs: {Out(merge, "Model").VolatileDataCount}");
        Require(bridge != null, "merge failed:\n" + string.Join("\n", ErrorLines(doc)));
        report.Add(new { Check = "assemblaggio", Phases = bridge.Records.Phases.Length, Combinations = bridge.Combinations.Count, Pass = true });
        int sections = 0;
        foreach (var checker in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Super Element Check"))
        { BridgeMovingRevisionExample.Press(checker); BridgeSegmentExcelTests.Solve(doc, true); sections += Out(checker, "Tension Check").VolatileDataCount; }
        True(report, $"verifiche di sezione: {sections} righe", sections > 0);
        var errors = ErrorLines(doc);
        report.Add(new { Check = "errori dopo il calcolo", Errors = errors, Pass = errors.Length == 0 });
        Require(bytes.SequenceEqual(File.ReadAllBytes(input)), "the native run modified the definition");
        string folder = Path.GetFullPath(Path.Combine("artifacts", "reference-loads", "native-" + which));
        File.WriteAllText(Path.Combine(folder, "native-validation.json"), JsonSerializer.Serialize(new { Native = true, Definition = Path.GetFileName(input),
            DefinitionSHA256 = Convert.ToHexString(SHA256.HashData(bytes)), Checks = report }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"REFERENCE LOADS NATIVE {which}: {(errors.Length == 0 ? "PASS" : "ERRORS " + errors.Length)}, {sections} section rows");
        return errors.Length == 0 ? 0 : 1;
    }

    internal static string[] ErrorLines(GH_Document doc) => doc.Objects.OfType<GH_ActiveObject>()
        .SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => o.Name + " | " + o.NickName + ": " + e)).ToArray();

    /// <summary>The X and Y resultants (N) of the beam and node forces of a case or combination of a GPC model.</summary>
    internal static (double X, double Y) Horizontal(GPC.Model.Models.Model model, string name)
    {
        if (model.Combinations.TryGetValue(name, out var combination))
        {
            double x = 0, y = 0;
            foreach (var p in combination.GetLoadCaseCoefficientsPair()) { var h = Horizontal(model, p.Key.Name); x += p.Value * h.X; y += p.Value * h.Y; }
            return (x, y);
        }
        double sx = 0, sy = 0;
        foreach (var b in model.BeamElements.Values)
        foreach (var a in b.Assignments.Loads)
        {
            if (a.Concentrated is GPC.Model.Loads.PointLoad c) { if (c.LoadCase?.Name == name) { var f = c.GetGlobalLoadVector().force; sx += f.X; sy += f.Y; } continue; }
            if (a.StartIntensity == null || a.StartIntensity.LoadCase?.Name != name) continue;
            Require(a.LengthConvention != GPC.Model.Loads.Assignments.BeamLoadLengthConvention.ProjectedLength, "projected loads are not integrated here");
            double[] f0 = a.StartIntensity.GetGlobalForces(), f1 = (a.EndIntensity ?? a.StartIntensity).GetGlobalForces(); double w = (a.End - a.Start) * b.Length / 2;
            sx += (f0[0] + f1[0]) * w; sy += (f0[1] + f1[1]) * w;
        }
        foreach (var n in model.NodesElements.Values)
        foreach (var l in n.Loads.Values.Where(l => l.LoadCase?.Name == name).OfType<GPC.Model.Loads.PointLoad>()) { var f = l.GetGlobalLoadVector().force; sx += f.X; sy += f.Y; }
        return (sx, sy);
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // Shared helpers

    /// <summary>
    /// Offline solve only: every Keep Running / Run of the analyses false and every Output Path redirected, in memory, into
    /// artifacts/reference-loads (the saved definitions name absolute paths under Repository\BridGHe: never read or write there).
    /// </summary>
    internal static Action Neutralize(GH_Document doc, string label)
    {
        var saved = new List<(GH_Panel Panel, string Text)>();
        string folder = Path.GetFullPath(Path.Combine("artifacts", "reference-loads", label)); Directory.CreateDirectory(folder);
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.Name == "Straus R3 Analysis" || c.Name == "Moving Static Analysis"))
        {
            BridgeMovingRevisionExample.Clear(c);
            foreach (var p in c.Params.Input.Where(p => p.Name is "Keep Running" or "Run"))
            {
                foreach (var t in p.Sources.OfType<GH_BooleanToggle>()) t.Value = false;
                if (p is Param_Boolean b) { b.PersistentData.Clear(); b.SetPersistentData(false); }
                Require(p.Sources.All(s => s is GH_BooleanToggle), c.NickName + ": " + p.Name + " not driven by a toggle");
            }
            foreach (var p in c.Params.Input.Where(p => p.Name is "Output Path" or "Output path"))
            {
                Require(p.Sources.All(s => s is GH_Panel), c.NickName + ": output path not in a panel");
                foreach (var panel in p.Sources.OfType<GH_Panel>()) { saved.Add((panel, panel.UserText)); panel.UserText = Path.Combine(folder, Path.GetFileName(panel.UserText.Trim())); }
            }
        }
        return () => { foreach (var (panel, text) in saved) panel.UserText = text; };
    }

    internal static void Require(bool ok, string message) { if (!ok) throw new Exception("REFERENCE LOADS: " + message); }
    internal static IGH_Param In(GH_Component c, string name) => c.Params.Input.Single(p => p.Name == name);
    internal static IGH_Param Out(GH_Component c, string name) => c.Params.Output.Single(p => p.Name == name);
    internal static void Wire(IGH_Param target, params IGH_Param[] sources) { target.RemoveAllSources(); foreach (var s in sources) target.AddSource(s); }
    /// <summary>Replaces one source keeping the order of the others.</summary>
    /// <summary>Every floating parameter that shows <paramref name="old"/> shows <paramref name="replacement"/> instead.</summary>
    internal static void Redirect(GH_Document doc, IGH_Param old, IGH_Param replacement)
    {
        foreach (var p in doc.Objects.OfType<IGH_Param>().Where(p => p.Sources.Contains(old)).ToArray()) Replace(p, old, replacement);
    }
    internal static void Replace(IGH_Param target, IGH_Param old, IGH_Param replacement)
    {
        var sources = target.Sources.ToList(); int i = sources.IndexOf(old);
        Require(i >= 0, $"{target.Name}: source to replace not found");
        sources[i] = replacement; Wire(target, sources.ToArray());
    }
    internal static GH_Component Component(GH_Document doc, string name, string nick) =>
        doc.Objects.OfType<GH_Component>().Single(c => c.Name == name && c.NickName == nick);
    internal static GH_Component ComponentPrefix(GH_Document doc, string name, string prefix) =>
        doc.Objects.OfType<GH_Component>().Single(c => c.Name == name && c.NickName.StartsWith(prefix, StringComparison.Ordinal));
    internal static T Param<T>(GH_Document doc, string nick) where T : class, IGH_DocumentObject =>
        doc.Objects.OfType<T>().Single(o => o.NickName == nick);
    internal static IGH_Param Float(GH_Document doc, string nick) => doc.Objects.OfType<IGH_Param>().Single(o => o.NickName == nick);
    internal static double Number(IGH_Param p)
    {
        if (p is GH_NumberSlider s) return (double)s.CurrentValue;
        var v = p.VolatileData.AllData(true).FirstOrDefault()?.ScriptVariable();
        if (v is IConvertible c) return Convert.ToDouble(c, CultureInfo.InvariantCulture);
        if (p is GH_Panel panel) return double.Parse(panel.UserText, CultureInfo.InvariantCulture);
        throw new InvalidOperationException("Not a number: " + p.NickName);
    }
    internal static string Invariant(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
    internal static object[] Values(IGH_Param p) => p.VolatileData.AllData(true).Select(g => g?.ScriptVariable()).ToArray();
    /// <summary>The goo items themselves (GH_Beam: the GPC draft is on the goo, its script value is empty).</summary>
    internal static object[] Goos(IGH_Param p) => p.VolatileData.AllData(true).Cast<object>().ToArray();
    internal static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>Adds components and explicit controls; every input of an added component ends up wired to a control.</summary>
    internal sealed class Canvas
    {
        internal readonly GH_Document Doc; internal readonly List<GH_Component> Added = new();
        private PointF anchor; private float dy;
        internal Canvas(GH_Document doc) { Doc = doc; }
        /// <summary>New objects start beside <paramref name="near"/>: the layout keeps notes beside the nearest component.</summary>
        internal void At(IGH_DocumentObject near) { var p = near.Attributes.Pivot; anchor = new PointF(p.X + 60, p.Y + 40); dy = 0; }
        private PointF Next(float h) { var p = new PointF(anchor.X, anchor.Y + dy); dy += h; return p; }
        internal GH_Component Bridge(string type, string nick)
        {
            var c = BridgeSegmentExcelTests.Add(Doc, type); c.NickName = nick; c.Attributes.Pivot = Next(160); Added.Add(c); return c;
        }
        internal GH_Component Std(string guid, string nick)
        {
            var c = (GH_Component)Grasshopper.Instances.ComponentServer.EmitObjectProxy(new Guid(guid)).CreateInstance(); c.CreateAttributes();
            c.NickName = nick; c.Attributes.Pivot = Next(120); Doc.AddObject(c, false); Added.Add(c); return c;
        }
        internal T Control<T>(string label, T p) where T : IGH_Param
        {
            p.Name = p.NickName = label; p.Description = label; p.CreateAttributes(); p.Attributes.Pivot = Next(90);
            if (p is GH_Panel) p.Attributes.Bounds = new RectangleF(p.Attributes.Pivot.X, p.Attributes.Pivot.Y, 650, 90);
            if (p is IGH_PreviewObject preview) preview.Hidden = true; Doc.AddObject(p, false); return p;
        }
        internal IGH_Param Text(string label, string value)
        {
            if (value.Length == 0) { var p = new Param_String(); p.SetPersistentData(""); return Control(label, p); }
            return Control(label, new GH_Panel { UserText = value, Properties = { Multiline = false } });
        }
        internal IGH_Param List(string label, IEnumerable<double> values) => Text(label, string.Join("\n", values.Select(Invariant)));
        internal GH_NumberSlider Slider(string label, double value, double min, double max, int decimals = 0)
        {
            var s = new GH_NumberSlider(); s.Slider.Type = decimals == 0 ? GH_SliderAccuracy.Integer : GH_SliderAccuracy.Float; s.Slider.DecimalPlaces = decimals;
            s.Slider.Minimum = (decimal)min; s.Slider.Maximum = (decimal)max; s.SetSliderValue((decimal)value); return Control(label, s);
        }
        internal GH_BooleanToggle Toggle(string label, bool value) => Control(label, new GH_BooleanToggle { Value = value });
        private IGH_Param Binary(string guid, string nick, IGH_Param a, IGH_Param b)
        { var c = Std(guid, nick); Wire(c.Params.Input[0], a); Wire(c.Params.Input[1], b); return c.Params.Output[0]; }
        internal IGH_Param Mul(string nick, IGH_Param a, IGH_Param b) => Binary(Multiplication, nick, a, b);
        internal IGH_Param Div(string nick, IGH_Param a, IGH_Param b) => Binary(Division, nick, a, b);
        internal IGH_Param Sub(string nick, IGH_Param a, IGH_Param b) => Binary(Subtraction, nick, a, b);
        internal IGH_Param Sum(string nick, IGH_Param a, IGH_Param b) => Binary(Addition, nick, a, b);
        internal IGH_Param Max(string nick, IGH_Param a, IGH_Param b) => Binary(Maximum, nick, a, b);
        internal IGH_Param Min(string nick, IGH_Param a, IGH_Param b) => Binary(Minimum, nick, a, b);
        internal IGH_Param Neg(string nick, IGH_Param a) { var c = Std(NegativeGuid, nick); Wire(c.Params.Input[0], a); return c.Params.Output[0]; }
        internal IGH_Param Partition(string nick, IGH_Param list, IGH_Param size) => Binary(PartitionList, nick, list, size);
        internal IGH_Param Duplicate(string nick, IGH_Param data, IGH_Param count, IGH_Param order)
        { var c = Std(DuplicateData, nick); Wire(c.Params.Input[0], data); Wire(c.Params.Input[1], count); Wire(c.Params.Input[2], order); return c.Params.Output[0]; }
        internal IGH_Param Length(string nick, IGH_Param list) { var c = Std(ListLengthGuid, nick); Wire(c.Params.Input[0], list); return c.Params.Output[0]; }
        internal IGH_Param Item(string nick, IGH_Param list, IGH_Param index, IGH_Param wrap)
        { var c = Std(ListItemGuid, nick); Wire(c.Params.Input[0], list); Wire(c.Params.Input[1], index); Wire(c.Params.Input[2], wrap); return c.Params.Output[0]; }
        internal IGH_Param Vector(string nick, IGH_Param x, IGH_Param y, IGH_Param z)
        { var c = Std(VectorXyz, nick); Wire(c.Params.Input[0], x); Wire(c.Params.Input[1], y); Wire(c.Params.Input[2], z); return c.Params.Output[0]; }
        /// <summary>Merge of the parts in order (head, interior values, tail of a per-strip pattern).</summary>
        internal IGH_Param Merge(string nick, params IGH_Param[] parts)
        {
            var c = Std(MergeGuid, nick); var variable = (IGH_VariableParameterComponent)c;
            while (c.Params.Input.Count < parts.Length) c.Params.RegisterInputParam(variable.CreateParameter(GH_ParameterSide.Input, c.Params.Input.Count));
            variable.VariableParameterMaintenance(); c.Params.OnParametersChanged();
            // Flattened inputs: one list in input order (the parts come from components with different output paths).
            for (int i = 0; i < parts.Length; i++) { Wire(c.Params.Input[i], parts[i]); c.Params.Input[i].DataMapping = GH_DataMapping.Flatten; }
            return c.Params.Output[0];
        }
        /// <summary>Every input still unwired gets an explicit control with its default value (REV003 rule: no hidden defaults).</summary>
        internal void ExposeDefaults()
        {
            foreach (var c in Added)
            foreach (var p in c.Params.Input.Where(p => p.SourceCount == 0).ToArray())
            {
                At(c);
                var data = p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure;
                var values = data?.AllData(true).ToArray() ?? Array.Empty<IGH_Goo>(); IGH_Param control; string label = c.NickName + " | " + p.Name;
                if (values.Length == 0) { Require(p.Optional, "Unassigned new input " + label); control = Control(label + " (opzionale vuoto)", (IGH_Param)Activator.CreateInstance(p.GetType())); }
                else if (values.Length == 1 && values[0] is GH_Boolean b) control = Toggle(label, b.Value);
                else control = Text(label, string.Join("\n", values.Select(v => v.ScriptVariable() is IFormattable n ? n.ToString(null, CultureInfo.InvariantCulture) : v.ScriptVariable()?.ToString())));
                data?.Clear(); Wire(p, control);
            }
        }
    }

    /// <summary>One load component of a phase: load case, action (ID, model, case, behaviour) wired to the phase.</summary>
    internal static (GH_Component Case, GH_Component Action) CaseAndAction(Canvas cv, string model, string id, string label, GH_Component straus, int behavior)
    {
        var lc = cv.Bridge("LoadCaseComponent", $"{model} | caso {label}");
        Wire(In(lc, "Name"), cv.Text($"{model} | nome caso {label}", id));
        var action = cv.Bridge("BridgeActionComponent", $"{id} | azione");
        Wire(In(action, "Action ID"), cv.Text($"{id} | identificativo azione", id));
        Wire(In(action, "Model"), Out(straus, "Model")); Wire(In(action, "Case"), Out(lc, "LoadCase"));
        string meaning = behavior switch { 0 => "0 = solo acciaio", 1 => "1 = lungo termine n∞", _ => "2 = breve termine n0" };
        Wire(In(action, "Behavior"), cv.Text($"{id} | comportamento: {meaning}", behavior.ToString(CultureInfo.InvariantCulture)));
        return (lc, action);
    }

    /// <summary>Beam Distributed Load: global direction (0 X, 1 Y, 2 Z), uniform schema on [a, 1 − b] of each beam.</summary>
    internal static GH_Component Distributed(Canvas cv, string nick, IGH_Param beams, GH_Component loadCase, IGH_Param schema, IGH_Param global,
        IGH_Param direction, IGH_Param pa, IGH_Param[] a, IGH_Param[] b)
    {
        var c = cv.Bridge("BeamDistributedLoadComponent", nick);
        Wire(In(c, "Beam"), beams); Wire(In(c, "LoadCase"), Out(loadCase, "LoadCase")); Wire(In(c, "Type"), schema);
        Wire(In(c, "Coordinate System"), global); Wire(In(c, "Direction"), direction); Wire(In(c, "PA"), pa);
        Wire(In(c, "a"), a); Wire(In(c, "b"), b);
        return c;
    }

    /// <summary>Beam Point Load in global axes at station a of each beam.</summary>
    internal static GH_Component Point(Canvas cv, string nick, IGH_Param beams, GH_Component loadCase, IGH_Param global, IGH_Param zero, IGH_Param pz, IGH_Param[] a)
    {
        var c = cv.Bridge("BeamPointLoadComponent", nick);
        Wire(In(c, "Beam"), beams); Wire(In(c, "LoadCase"), Out(loadCase, "LoadCase")); Wire(In(c, "Coordinate System"), global);
        Wire(In(c, "P1"), zero); Wire(In(c, "P2"), zero); Wire(In(c, "P3"), pz); Wire(In(c, "a"), a);
        return c;
    }

    /// <summary>Beam PreLoad as an imposed strain (uniform temperature α ΔT).</summary>
    internal static GH_Component Strain(Canvas cv, string nick, IGH_Param beams, GH_Component loadCase, IGH_Param type, IGH_Param value)
    {
        var c = cv.Bridge("BeamPreLoadComponent", nick);
        Wire(In(c, "Beam"), beams); Wire(In(c, "LoadCase"), Out(loadCase, "LoadCase")); Wire(In(c, "Type"), type); Wire(In(c, "Value"), value);
        return c;
    }

    /// <summary>
    /// Removes the objects that reach no end point any more (the replaced demonstration loads, their cases, actions and exclusive controls).
    /// Every removed object must be one of the expected nicknames or a control read only by them.
    /// </summary>
    internal static string[] RemoveDetached(GH_Document doc, ISet<string> expectedComponents)
    {
        bool Expected(GH_Component c, string key) => c.NickName == key || c.InstanceGuid.ToString() == key;
        // Floating output parameters that only show a replaced component (e.g. «caso SOLETTA») are disconnected and removed with it.
        foreach (var p in doc.Objects.OfType<IGH_Param>().Where(p => p.SourceCount > 0 && p.Sources.All(x => x.Attributes.GetTopLevel.DocObject is GH_Component c
            && expectedComponents.Any(k => Expected(c, k)))).ToArray())
        { Console.WriteLine("REFERENCE LOADS: output of a replaced component disconnected: " + p.NickName); p.RemoveAllSources(); }
        var removed = ExampleTidy.Unreachable(doc);
        var unexpected = removed.OfType<GH_Component>().Where(c => !expectedComponents.Any(k => Expected(c, k))).Select(c => c.Name + " | " + c.NickName + " | " + c.InstanceGuid).ToArray();
        Require(unexpected.Length == 0, "unexpected removals: " + string.Join("; ", unexpected));
        var missing = expectedComponents.Where(k => !removed.OfType<GH_Component>().Any(c => Expected(c, k))).ToArray();
        Require(missing.Length == 0, "replaced components still reachable: " + string.Join("; ", missing));
        var names = removed.Select(o => o.Name + " | " + o.NickName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        foreach (var o in removed) doc.RemoveObject(o, false);
        foreach (var n in names) Console.WriteLine("REFERENCE LOADS: removed " + n);
        return names;
    }

    /// <summary>Wires that still run from right to left.</summary>
    internal static int BackwardWires(GH_Document doc)
    {
        var objects = doc.Objects.Where(o => o is not GH_Group && !o.Name.StartsWith("BGH50_LAYOUT_")).ToArray();
        foreach (var o in objects) { o.Attributes.ExpireLayout(); o.Attributes.PerformLayout(); }
        IGH_DocumentObject Owner(IGH_Param p) => p.Attributes.GetTopLevel.DocObject;
        IEnumerable<IGH_Param> Inputs(IGH_DocumentObject o) => o is IGH_Component c ? c.Params.Input : o is IGH_Param p ? new[] { p } : Enumerable.Empty<IGH_Param>();
        int count = 0;
        foreach (var o in objects)
        foreach (var s in Inputs(o).SelectMany(i => i.Sources))
        {
            var from = Owner(s); if (from == o) continue;
            if (from.Attributes.Bounds.Right >= o.Attributes.Bounds.Left) { count++; Console.WriteLine($"REFERENCE LOADS: right to left {from.NickName} -> {o.NickName}"); }
        }
        Example50HorizontalLayout.ValidateBounds(objects);
        return count;
    }

    // ---------------------------------------------------------------------------------------------------------------------------------
    // Load resultants read back from the GPC drafts of the loaded beams (offline equilibrium of the applied loads).

    internal sealed class Totals
    {
        internal readonly Dictionary<string, double[]> Force = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> Pieces = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> Strains = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, double> StrainValues = new(StringComparer.Ordinal);
        internal double[] Of(string name) => Force.TryGetValue(name, out var f) ? f : new double[3];
    }

    private static object Prop(object o, string name)
    {
        if (o == null) return null;
        var t = o.GetType();
        return t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is PropertyInfo p ? p.GetValue(o)
            : t.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);
    }

    /// <summary>Sum of the line and point loads of the beams (global components, N) per load case name.</summary>
    internal static Totals LoadTotals(IEnumerable<object> beams)
    {
        var totals = new Totals();
        foreach (var goo in beams)
        {
            var draft = Prop(goo, "Gpc"); var beam = Prop(draft, "Beam");
            Require(beam != null, "beam without GPC draft: " + goo?.GetType().Name);
            double length = Convert.ToDouble(Prop(beam, "Length"), CultureInfo.InvariantCulture);
            var loads = Prop(Prop(beam, "Assignments"), "Loads") as System.Collections.IEnumerable;
            foreach (var l in loads ?? Array.Empty<object>())
            {
                if (l is GPC.Model.Loads.Assignments.BeamLoadAssignment a)
                {
                    string name; double[] f;
                    if (a.Concentrated != null)
                    {
                        var c = a.Concentrated.GetGlobalLoadVector().force;
                        name = a.Concentrated.LoadCaseName ?? a.Concentrated.LoadCase?.Name; f = new[] { c.X, c.Y, c.Z };
                    }
                    else
                    {
                        Require(a.LengthConvention != GPC.Model.Loads.Assignments.BeamLoadLengthConvention.ProjectedLength, "projected loads are not integrated here");
                        name = a.StartIntensity.LoadCaseName ?? a.StartIntensity.LoadCase?.Name; double w = (a.End - a.Start) * length / 2;
                        double[] f0 = a.StartIntensity.GetGlobalForces(), f1 = (a.EndIntensity ?? a.StartIntensity).GetGlobalForces();
                        f = new[] { (f0[0] + f1[0]) * w, (f0[1] + f1[1]) * w, (f0[2] + f1[2]) * w };
                    }
                    var sum = totals.Force.TryGetValue(name, out var s) ? s : totals.Force[name] = new double[3];
                    for (int k = 0; k < 3; k++) sum[k] += f[k];
                    totals.Pieces[name] = totals.Pieces.GetValueOrDefault(name) + 1;
                }
            }
            // Beam PreLoad: the initial states of the physical data (imposed axial strain per case).
            if (Prop(Prop(Prop(beam, "Assignments"), "Physical"), "InitialStates") is System.Collections.IEnumerable states)
                foreach (var st in states)
                {
                    string name = Prop(Prop(st, "Case"), "Name") as string ?? "?";
                    totals.Strains[name] = totals.Strains.GetValueOrDefault(name) + 1;
                    totals.StrainValues[name] = Convert.ToDouble(Prop(st, "Value"), CultureInfo.InvariantCulture);
                }
        }
        return totals;
    }

    internal static void Near(List<object> report, string label, double actual, double expected, double relative = 1e-9)
    {
        bool ok = Math.Abs(actual - expected) <= relative * Math.Max(1, Math.Abs(expected));
        report.Add(new { Check = label, Actual = actual, Expected = expected, Pass = ok });
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}: {actual.ToString("G10", CultureInfo.InvariantCulture)} (atteso {expected.ToString("G10", CultureInfo.InvariantCulture)})");
        Require(ok, label);
    }

    internal static void True(List<object> report, string label, bool ok)
    {
        report.Add(new { Check = label, Pass = ok }); Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {label}"); Require(ok, label);
    }

    /// <summary>
    /// Pattern of a uniform band [y1, y2] (transverse coordinate, left positive) over the strip segments [o(j), o(j+1)] of each station:
    /// mask 1 where they overlap, a and b the unloaded fractions from the start (right) and end (left) node of the segment.
    /// </summary>
    internal static (double[] Mask, double[] A, double[] B) Band(double[] offsets, double y1, double y2)
    {
        int n = offsets.Length - 1; var m = new double[n]; var a = new double[n]; var b = new double[n];
        for (int j = 0; j < n; j++)
        {
            double s = offsets[j], e = offsets[j + 1], lo = Math.Max(y1, s), hi = Math.Min(y2, e);
            if (hi - lo <= 1e-6) continue;
            m[j] = 1; a[j] = Math.Round((lo - s) / (e - s), 6); b[j] = Math.Round((e - hi) / (e - s), 6);
        }
        return (m, a, b);
    }
}
