using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using R = Rhino.Geometry;

/// <summary>
/// Chain E (e3, P2K, point 4): regression <c>gpc-build-model</c> (not native). R6.6 lane R0: the Build Model component on elements, combinations and
/// stages given by their GPC data alone (the product builders, no FEM value), with the functional assertions of Build Model on GPC
/// (<see cref="BridGHe.Common.Gpc.Legacy.LegacyBuildModel.Build"/>):
/// <list type="bullet">
/// <item>assembly of the examples 01–10 (<see cref="GpcNativeExamples"/>, without their moving paths: they come after Build Model): the structure of
/// each model against its definition (explicit: nodes and positions, beams and plates with their nodes, properties and groups, restraints, cases,
/// loads, combinations, stages), the GPC input of the component equal to Build Model on the same data (name, units, combinations, stages read from
/// the GH data), the drafts of the output (identity, names, positions on the nodes);</item>
/// <item>identity: the model GUID is the owner of its element identities, the same data give the same GUIDs, the GH GUIDs of the elements are those
/// the component was given;</item>
/// <item>units: the same structures with the data in kN-m, SI and IPS against the N-mm build of the same structure (measured per class: geometry,
/// loads, section values, the same graph; at most 1E-12 relative);</item>
/// <item>synthetic merges with their expected counts and consequences (a datum twice, two branches of a datum, coincident and near nodes, a beam
/// end within the tolerance, an end without a node, a beam of zero length) and the errors and remarks of Build Model (a member merged with itself,
/// an Element ID given twice with other data, copies of the same data).</item>
/// </list>
/// The conversion of a FEM output as the oracle is retired (R6.6): the GPC catalogue is compared once with the conversion of the FEM catalogue,
/// transitionally (<see cref="GpcNativeExamples.RequireSameAsFem"/>), and <see cref="Single"/> (core-native, kN-m native section) still measures
/// against the conversion of its FEM output until P2 moves <c>Program.CheckNativeSectionAnalysis</c> to GPC.
/// </summary>
internal static class GpcBuildModelChecks
{

    /// <summary>
    /// Chain E (e3 point 5, coordinator decision 1 (b) of 5/11): the GPC input of Build Model in units other than N-mm against the conversion
    /// of its FEM output by the legacy reader: the same report and bridge data, the same graph (every element name, attribute and non-numeric
    /// text equal), and the maximum relative difference of the numbers per class (geometry, loads, section values: by the names of the
    /// elements above each number). Zero everywhere: bit for bit.
    /// </summary>
    internal static string Measure(string label, GpcModels.Conversion attached, ModelModel fem, double tolerance, out double[] maxima)
    {
        void Fail(string what) => throw new Exception("GPC BUILD MODEL UNITS FAIL (" + label + "): " + what);
        var model = attached.Model; var owner = GpcInputComparison.ElementOwner(model);
        if (owner != model.Guid) Fail("the model GUID of Build Model is not the owner of its element identities");
        var converted = GpcInputComparison.Conversion(fem, tolerance, owner, out var report);
        GpcInputComparison.RequireReport(label, attached.Report, report);
        GpcInputComparison.RequireBridge(label, model, attached.Bridge, converted, report.Bridge, false);
        var x = System.Xml.Linq.XDocument.Parse(GpcInputComparison.Document(model)).Descendants().ToArray();
        var y = System.Xml.Linq.XDocument.Parse(GpcInputComparison.Document(converted)).Descendants().ToArray();
        if (x.Length != y.Length) Fail($"{x.Length} / {y.Length} elements in the documents");
        string[] classes = { "geometry", "loads", "section values" }; maxima = new double[3]; int differing = 0;
        static int Class(System.Xml.Linq.XElement e)
        {
            string path = string.Join("/", e.AncestorsAndSelf().Select(a => a.Name.LocalName));
            if (path.Contains("Propert") || path.Contains("Section") || path.Contains("Material")) return 2;
            if (path.Contains("Load") || path.Contains("Pressure") || path.Contains("Temperature") || path.Contains("Stress") || path.Contains("Gradient") || path.Contains("InitialState")) return 1;
            return 0;
        }
        for (int i = 0; i < x.Length; i++)
        {
            string where = string.Join("/", x[i].AncestorsAndSelf().Reverse().Select(a => a.Name.LocalName));
            if (x[i].Name != y[i].Name) Fail($"element {i}: {where} / {y[i].Name.LocalName}");
            string Attributes(System.Xml.Linq.XElement e) => string.Join(" ", e.Attributes().Select(a => a.Name + "=" + a.Value));
            if (Attributes(x[i]) != Attributes(y[i])) Fail($"attributes of {where}: '{Attributes(x[i])}' / '{Attributes(y[i])}'");
            if (x[i].HasElements || y[i].HasElements || x[i].Value == y[i].Value) continue;
            const System.Globalization.NumberStyles Float = System.Globalization.NumberStyles.Float;
            if (!double.TryParse(x[i].Value, Float, System.Globalization.CultureInfo.InvariantCulture, out double p) || !double.TryParse(y[i].Value, Float, System.Globalization.CultureInfo.InvariantCulture, out double q))
                Fail($"text of {where}: '{x[i].Value}' / '{y[i].Value}'");
            double relative = Math.Abs(p - q) / Math.Max(Math.Abs(p), Math.Abs(q));
            int c = Class(x[i]); maxima[c] = Math.Max(maxima[c], relative); differing++;
            Console.WriteLine($"GPC BUILD MODEL UNITS ({label}): {classes[c]} {where} GPC {x[i].Value} / conversion {y[i].Value} (relative {relative:E2})");
        }
        return $"{x.Length} elements, {differing} numbers differ; maximum relative difference: geometry {maxima[0]:E2}, loads {maxima[1]:E2}, section values {maxima[2]:E2}";
    }

    /// <summary>The kN-m native section of core-native (<c>Program.CheckNativeSectionAnalysis</c>). <b>P2R (T3, 12/11): retired.</b> Until P2 it ran
    /// Build Model on the FEM elements of the case and measured its output against the conversion of the FEM output; Build Model has no FEM
    /// output since P2K. Its coverage stays: Build Model on GPC drafts in every unit system against explicit values (<c>gpc-build-model</c>, R0),
    /// Build Model on the drafts of the 0.3.0 models read by value (<c>gpc-build-model-legacy</c>), and in core-native the native section of this
    /// case re-resolved in mm with its analytical area b × d (the assertions before this call).</summary>
    internal static void Single(string label, ModelModel fem) =>
        Console.WriteLine($"GPC BUILD MODEL ({label}): the comparison with the FEM output retired (P2R): no FEM output since P2K; covered by gpc-build-model, gpc-build-model-legacy and the analytical area above.");
    // -----------------------------------------------------------------------------------------------------------------------------------------
    // R6.6 lane R0: Build Model on GPC data alone.

    private static System.Reflection.Assembly plugin;
    private static double pluginTolerance;

    private static Guid Id(string what) => GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r0-build-model:" + what);
    private static object Prop(object o, string name) => o.GetType().GetProperty(name).GetValue(o);

    /// <summary>A GH datum given by its GPC datum alone (<c>FromGpc</c> of the GH type), with the GH GUID given for an element.</summary>
    private static IGH_Goo Goo(string type, object datum, Guid? guid = null)
    {
        var method = plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem." + type, true).GetMethod("FromGpc", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return (IGH_Goo)method.Invoke(null, method.GetParameters().Length == 2 ? new object[] { datum, guid } : new object[] { datum });
    }

    private sealed record Run(IGH_Goo Output, string[] Errors, string[] Remarks)
    {
        internal GpcModels.Conversion Input => Output == null ? null : (GpcModels.Conversion)Prop(Output, "GpcInput");
    }

    /// <summary>The Build Model component in a document of its own on the GH data given (elements, name, the index of the definition units in the
    /// Units menu, which follows <see cref="DefinitionUnits"/>, combinations, stages).</summary>
    private static Run Solve(string name, IEnumerable<IGH_Goo> elements, IEnumerable<LegacyCombinationDraft> combinations, IEnumerable<LegacyStageDraft> stages, DefinitionUnits units = DefinitionUnits.Nmm)
    {
        using var doc = new GH_Document();
        var component = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "BuildModelComponent"));
        component.CreateAttributes(); doc.AddObject(component, false);
        var input = (Param_GenericObject)component.Params.Input[0];
        foreach (var e in elements) input.PersistentData.Append(e);
        var n = (Param_String)component.Params.Input[1]; n.PersistentData.Clear(); n.SetPersistentData(name);
        var u = (Param_Integer)component.Params.Input[2]; u.PersistentData.Clear(); u.SetPersistentData((int)units);
        foreach (var c in combinations) ((dynamic)component.Params.Input[3]).PersistentData.Append((dynamic)Goo("GH_LoadCombination", c));
        foreach (var s in stages) ((Param_GenericObject)component.Params.Input[4]).PersistentData.Append(Goo("GH_StagedConstruction", s));
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        return new Run(component.Params.Output[0].VolatileData.AllData(true).SingleOrDefault(), component.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToArray(),
            component.RuntimeMessages(GH_RuntimeMessageLevel.Remark).ToArray());
    }

    /// <summary>The GH data of the elements of Build Model inputs (nodes, beams, plates), each with its GH GUID (derived from the label and the position).</summary>
    private static List<(IGH_Goo Goo, Guid Gh)> Elements(string label, GpcNativeExamples.Inputs inputs)
    {
        var result = new List<(IGH_Goo, Guid)>();
        foreach (var (d, i) in inputs.Nodes.Select((d, i) => (d, i))) { var g = Id(label + ":gh-node:" + i); result.Add((Goo("GH_Node", d, g), g)); }
        foreach (var (d, i) in inputs.Beams.Select((d, i) => (d, i))) { var g = Id(label + ":gh-beam:" + i); result.Add((Goo("GH_Beam", d, g), g)); }
        foreach (var (d, i) in inputs.Plates.Select((d, i) => (d, i))) { var g = Id(label + ":gh-plate:" + i); result.Add((Goo("GH_Plate", d, g), g)); }
        return result;
    }

    /// <summary>
    /// The structure of a GPC model as text, for the explicit expectations: per node (number order) its name, position (mm), groups, restraints
    /// (Tx…Rz, 1 restrained) and the point loads per case (sum of the forces, count); per beam its name, end nodes, property, groups and its load
    /// assignments per case (count); per plate its name, nodes, property and groups; the cases in order; the combinations with their factors; the
    /// stages with their active groups, Morph/Rotate and increments with their factors. Numbers in the shortest round-trip form.
    /// </summary>
    internal static string Structure(GPC.Model.Models.Model m)
    {
        static string N(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static string Groups(GPC.Model.Elements.Element e) => "[" + string.Join("+", e.Groups.Keys) + "]";
        string Case(Guid g) => m.LoadCases.Values.First(c => c.Guid == g).Name;
        var s = new System.Text.StringBuilder();
        foreach (var n in m.NodesElements.Values.OrderBy(n => n.Id))
        {
            s.Append("N ").Append(n.Name).Append(' ').Append(N(n.Position.X)).Append(',').Append(N(n.Position.Y)).Append(',').Append(N(n.Position.Z)).Append(' ').Append(Groups(n));
            foreach (var r in n.Assignments.Restrains) s.Append(" R").Append(string.Concat(r.Restrain.Restrains.OrderBy(d => (int)d.Dof).Select(d => d.IsRestrained ? '1' : '0')));
            foreach (var g in n.Loads.Values.OfType<GPC.Model.Loads.PointLoad>().GroupBy(l => ((GPC.Model.LoadCases.LoadCaseBase)l.LoadCase).Name))
                s.Append(" F ").Append(g.Key).Append(':').Append(N(g.Sum(l => l.F1))).Append(',').Append(N(g.Sum(l => l.F2))).Append(',').Append(N(g.Sum(l => l.F3))).Append(" x").Append(g.Count());
            s.Append('\n');
        }
        foreach (var b in m.BeamElements.Values.OrderBy(b => b.Id))
        {
            s.Append("B ").Append(b.Name).Append(' ').Append(b.NodeI?.Name).Append('-').Append(b.NodeJ?.Name).Append(' ').Append(b.BeamProperty?.Name).Append(' ').Append(Groups(b));
            foreach (var g in b.Assignments.Loads.GroupBy(a => ((GPC.Model.LoadCases.LoadCaseBase)(a.Concentrated ?? (GPC.Model.Loads.Load)a.StartIntensity ?? a.EndIntensity).LoadCase).Name))
                s.Append(" L ").Append(g.Key).Append(':').Append(g.Count());
            s.Append('\n');
        }
        foreach (var p in m.AreaElements.Values.OrderBy(p => p.Id))
            s.Append("P ").Append(p.Name).Append(' ').Append(string.Join(",", p.Nodes.Select(n => n.Name))).Append(' ').Append(p.PlateProperty?.Name).Append(' ').Append(Groups(p)).Append('\n');
        s.Append("CASES ").Append(string.Join(",", m.LoadCases.Values.Select(c => c.Name))).Append('\n');
        foreach (var c in m.Combinations.Values)
            s.Append("C ").Append(c.Name).Append(": ").Append(string.Join(";", c.GetLoadCaseCoefficientsPair().Select(p => p.Key.Name + "=" + N(p.Value)))).Append('\n');
        foreach (var st in m.Stages.Values.OrderBy(x => x.Id))
        {
            var a = st.AnalysisState;
            s.Append("S ").Append(st.Name).Append(" [").Append(string.Join("+", a.ActiveGroupGuids.Select(g => m.Groups.Values.First(x => x.Guid == g).Name))).Append(']')
                .Append(a.BirthGeometry == GPC.Model.Stages.StageBirthGeometry.DeformedCoordinates ? " morph" : "").Append(a.RotateIncomingClusters ? " rotate" : "").Append(':');
            foreach (var i in a.Increments) s.Append(' ').Append(i.Name).Append('{').Append(string.Join(";", i.LoadFactors.Select(f => Case(f.CaseGuid) + "=" + N(f.Factor)))).Append('}');
            s.Append('\n');
        }
        return s.ToString();
    }

    private static string Nodes(int count, double length, bool supported, string loads = "")
    {
        var s = new System.Text.StringBuilder();
        for (int i = 0; i <= count; i++)
        {
            string r = supported ? (i == 0 ? "111101" : i == count ? "011101" : "010101") : i == 0 ? "111111" : null;
            s.Append($"N {i + 1} {(length * i / count).ToString("R", System.Globalization.CultureInfo.InvariantCulture)},0,0 [Permanent]{(r == null ? "" : " R" + r)}{(i == count ? loads : "")}\n");
        }
        return s.ToString();
    }

    private static string Beams(int count, string loads = "") =>
        string.Concat(Enumerable.Range(1, count).Select(k => $"B {k} {k}-{k + 1} Elastic beam [Permanent]{loads}\n"));

    /// <summary>The structure of each example of <see cref="GpcNativeExamples"/> as Build Model gives it (without the moving paths of 04), from the
    /// definitions of the examples (<see cref="NativeExamples"/>): explicit.</summary>
    private static readonly Dictionary<string, string> ExpectedStructures = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // The resolution of the assembly sums the point loads of a node per case (01: -600 and -400 in P, one load of -1000).
        ["01-cantilever"] = Nodes(1, 3000, false, " F P:0,0,-1000 x1") + Beams(1) + "CASES P,Dead\nC 1.5P: P=1.5\n",
        ["02-simply-supported"] = Nodes(4, 6000, true) + Beams(4, " L q:1") + "CASES q,Dead\n",
        ["03-plate-patch"] = "N 1 0,0,0 [] R111111\nN 2 1000,0,0 [] R001111 F Tension:1000,0,0 x1\nN 3 1000,500,0 [] R001111 F Tension:1000,0,0 x1\nN 4 0,500,0 [] R101111\n"
            + "P 1 1,2,3,4 Shell patch []\nCASES Tension,Dead\n",
        ["04-moving-load"] = Nodes(12, 6000, true) + Beams(12) + "CASES Dead\n",
        ["05-nonlinear-increments"] = Nodes(1, 3000, false, " F P:0,0,-1000 x1") + Beams(1) + "CASES P,Dead\nC Half: P=0.5\nC Full: P=1\nC Double: P=2\n",
        ["06-stage-loading"] = Nodes(1, 3000, false, " F G:0,0,-1000 x1") + Beams(1) + "CASES G,Dead\nS Steel [Permanent] morph rotate: Load{G=1}\nS Deck [Permanent] morph rotate: Load{G=1.5}\n",
        ["07-stage-unloading"] = Nodes(1, 3000, false, " F G:0,0,-1000 x1") + Beams(1) + "CASES G,Dead\nS Loading [Permanent] morph rotate: Step{G=1}\nS Unloading [Permanent] morph rotate: Step{G=0}\n"
            + "S Reloading [Permanent] morph rotate: Step{G=0.5}\n",
        ["08-stage-activation"] = Nodes(1, 3000, false, " F Axial:1000,0,0 x1") + "B 1 1-2 Elastic beam [Permanent]\nB 2 1-2 Elastic beam [Added]\nCASES Axial,Dead\n"
            + "S Original [Permanent] morph rotate: Step{Axial=1}\nS Reinforced [Permanent+Added] morph rotate: Step{Axial=2}\n",
        ["09-stage-removal"] = Nodes(1, 3000, false, " F Axial:1000,0,0 x1") + "B 1 1-2 Elastic beam [Permanent]\nB 2 1-2 Elastic beam [Added]\nCASES Axial,Dead\n"
            + "S Original [Permanent] morph rotate: Step{Axial=1}\nS Reinforced [Permanent+Added] morph rotate: Step{Axial=2}\nS Removed [Permanent] morph rotate: Step{Axial=2}\n",
        ["10-stage-selection"] = Nodes(1, 3000, false, " F G:0,0,-1000 x1 F Q:0,0,-1000 x1") + Beams(1) + "CASES G,Q,Dead\n"
            + "S Construction [Permanent] morph rotate: Half{G=0.5} Full{G=1}\nS Service [Permanent] morph rotate: SLS{G=1;Q=0.5} ULS{G=1.35;Q=1.5}\n",
    };

    internal static void Regression()
    {
        plugin = BridgeMergeTests.Initialize();
        var settings = plugin.GetType("BridGHe.Grasshopper.Settings");
        // The tolerance of the plug-in (the model tolerance of the active Rhino document, 0.001 without one): Build Model takes it.
        pluginTolerance = (double)settings.GetProperty("Tolerance").GetValue(settings.GetProperty("Instance").GetValue(null));
        // Transitional (P2 retires it with the FEM catalogue): the GPC catalogue is the conversion of the FEM catalogue, bit for bit.
        foreach (var line in GpcNativeExamples.RequireSameAsFem()) Console.WriteLine("GPC NATIVE EXAMPLES (transitional) " + line);
        int runs = 0, structures = 0, drafts = 0;
        void Fail(string label, string what) => throw new Exception("GPC BUILD MODEL FAIL (" + label + "): " + what);

        // 1. The examples 01–10 through Build Model.
        foreach (var example in GpcNativeExamples.Create())
        {
            string label = "example " + example.Id; var inputs = example.Inputs;
            var elements = Elements(label, inputs);
            var run = Solve(example.Title, elements.Select(e => e.Goo), inputs.Combinations, inputs.Stages); runs++;
            if (run.Errors.Length != 0 || run.Output == null) Fail(label, "no model: " + string.Join("; ", run.Errors));
            if (Prop(run.Output, "Value") != null || !(bool)Prop(run.Output, "HasAttachedInput")) Fail(label, "the output is not a GPC-only model");
            var input = run.Input; var model = input.Model;
            // Assembly: the structure of the definition.
            string structure = Structure(model);
            if (structure != ExpectedStructures[example.Id])
            {
                var x = structure.Split('\n'); var y = ExpectedStructures[example.Id].Split('\n');
                int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
                Fail(label, "structure differs from the definition" + (line < 0 ? $": {x.Length} / {y.Length} lines" : $", line {line + 1}: '{x[line]}', expected '{y[line]}'") + "\n" + structure);
            }
            structures++;
            if (model.Name != example.Title || input.Report.Units.Units != DefinitionUnits.Nmm || input.Tolerance != pluginTolerance) Fail(label, $"name '{model.Name}', units {input.Report.Units.Units}, tolerance {input.Tolerance}");
            // The component reads its GH data as Build Model on GPC takes them.
            var direct = LegacyBuildModel.Build(example.Title, new BridgeUnits(DefinitionUnits.Nmm), pluginTolerance, inputs.Nodes.Select((d, i) => (elements[i].Gh, d)).ToList(),
                inputs.Beams.Select((d, i) => (elements[inputs.Nodes.Count + i].Gh, d)).ToList(), inputs.Plates.Select((d, i) => (elements[inputs.Nodes.Count + inputs.Beams.Count + i].Gh, d)).ToList(),
                inputs.Combinations.ToList(), inputs.Stages.ToList());
            if (GpcInputComparison.Document(model) != GpcInputComparison.Document(direct.Model) || GpcDraftCapture.ConversionText(input) != GpcDraftCapture.ConversionText(direct))
                Fail(label, "the GPC input of the component differs from Build Model on GPC on the same data");
            // Identity: the model GUID is the owner of the element identities; the same data give the same GUIDs; the GH GUIDs are those given.
            if (GpcInputComparison.ElementOwner(model) != model.Guid) Fail(label, "the model GUID is not the owner of its element identities");
            var again = Solve(example.Title, Elements(label, inputs).Select(e => e.Goo), inputs.Combinations, inputs.Stages).Input.Model;
            if (again.Guid != model.Guid || !again.AllElements.Select(e => e.Guid).SequenceEqual(model.AllElements.Select(e => e.Guid))) Fail(label, "the same data give other GUIDs");
            var carried = input.Drafts;
            if (carried == null) Fail(label, "no drafts of the elements");
            var guids = (Guid[])run.Output.GetType().GetMethod("ElementGuidsOf").Invoke(run.Output, new object[] { carried });
            if (!guids.SequenceEqual(elements.Select(e => e.Gh))) Fail(label, "the GH GUIDs of the elements are not those given");
            // The drafts of the output: one per element, named as the element, on its positions, with the identity of the datum given.
            if (!carried.Fits(model.NodesElements.Count, model.BeamElements.Count, model.AreaElements.Count)) Fail(label, "the drafts do not fit the model");
            for (int i = 0; i < carried.Nodes.Count; i++)
            {
                var d = carried.Nodes[i].Node; var e = model.NodesElements[i + 1];
                if (d.Name != e.Name || d.Position.X != e.Position.X || d.Position.Y != e.Position.Y || d.Position.Z != e.Position.Z || d.Guid != inputs.Nodes[i].Node.Guid) Fail(label, "draft of node " + (i + 1));
            }
            for (int k = 0; k < carried.Beams.Count; k++)
            {
                var d = carried.Beams[k].Beam; var e = model.BeamElements[k + 1];
                if (d.Name != e.Name || d.StartPoint.X != e.StartPoint.X || d.StartPoint.Y != e.StartPoint.Y || d.StartPoint.Z != e.StartPoint.Z
                    || d.EndPoint.X != e.EndPoint.X || d.EndPoint.Y != e.EndPoint.Y || d.EndPoint.Z != e.EndPoint.Z || d.Guid != inputs.Beams[k].Beam.Guid) Fail(label, "draft of beam " + (k + 1));
            }
            for (int k = 0; k < carried.Plates.Count; k++)
                if (carried.Plates[k].Plate.Name != model.AreaElements[k + 1].Name || carried.Plates[k].Plate.Guid != inputs.Plates[k].Plate.Guid) Fail(label, "draft of plate " + (k + 1));
            drafts += carried.Nodes.Count + carried.Beams.Count + carried.Plates.Count;
            Console.WriteLine($"GPC BUILD MODEL PASS ({label}): structure of the definition, the GPC input of Build Model on GPC, owner GUID, same GUIDs again, GH GUIDs, drafts; {input.Report.BuildModel}");
        }

        // 2. Units: the structures of 02 and 03 and a synthetic one (restraint frame away from the origin, explicit section with shear-centre
        // offsets and a principal angle, node and beam point loads) with their data in kN-m, SI and IPS, against the N-mm build of the same structures.
        var unitMaxima = new double[3]; int measured = 0;
        foreach (var (name, make) in new (string, Func<BridgeUnits, GpcNativeExamples.Inputs>)[] { ("units 02", Units02), ("units 03", Units03), ("units synthetic", UnitsSynthetic) })
        {
            var reference = make(new BridgeUnits(DefinitionUnits.Nmm));
            var nmm = Solve(name, Elements(name, reference).Select(e => e.Goo), reference.Combinations, reference.Stages); runs++;
            if (nmm.Output == null) Fail(name, "no N-mm model: " + string.Join("; ", nmm.Errors));
            foreach (DefinitionUnits u in Enum.GetValues(typeof(DefinitionUnits)))
            {
                if (u == DefinitionUnits.Nmm) continue;
                var data = make(new BridgeUnits(u)); string label = name + " " + u;
                var run = Solve(name, Elements(label, data).Select(e => e.Goo), data.Combinations, data.Stages, u); runs++;
                if (run.Output == null) Fail(label, "no model: " + string.Join("; ", run.Errors));
                if (run.Input.Report.Units.Units != u) Fail(label, "units of the report " + run.Input.Report.Units.Units);
                string result = MeasureUnits(label, run.Input.Model, nmm.Input.Model, out var maxima);
                for (int c = 0; c < 3; c++) unitMaxima[c] = Math.Max(unitMaxima[c], maxima[c]);
                if (maxima.Any(m => m > 1e-12)) Fail(label, "relative difference above 1E-12: " + result);
                measured++;
                Console.WriteLine($"GPC BUILD MODEL UNITS PASS ({label}): {result}; {run.Input.Report.BuildModel}");
            }
        }

        // 3. Synthetic merges with their expected counts and consequences, the errors and the remarks.
        runs += Merges();

        // P2K (coordinator, item 3): BridgeModelData.Save() of two equal data differs only by the identity GUIDs (random, version 4) that GPC
        // geometry values write and never read back (here the section-up vector of the member metadata); Fingerprint() does not see them.
        byte[] Save()
        {
            var data = new GPC.Checkers.CompositeBridge.BridgeModelData();
            data.SetMember(new Guid("0f3b0c1e-6a43-4a51-9d1b-6c4e1c2b9a10"), new GPC.Checkers.CompositeBridge.Layout.BridgeMemberMetadata("Girder:1:1", GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder,
                1, 1, 1, 0, 1000, new GPC.Geometry.Vector3d(0, 0, 1), "G"));
            return data.Save();
        }
        var v4 = new System.Text.RegularExpressions.Regex("[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}");
        string xs = System.Text.Encoding.UTF8.GetString(Save()), ys = System.Text.Encoding.UTF8.GetString(Save());
        if (v4.Replace(xs, "v4") != v4.Replace(ys, "v4")) throw new Exception("GPC BUILD MODEL FAIL: BridgeModelData.Save differs beyond the GUIDs of the geometry values");
        Console.WriteLine($"GPC BUILD MODEL: BridgeModelData.Save of equal data {(xs == ys ? "identical" : "different only by the version 4 GUIDs of the geometry values")} ({v4.Matches(xs).Count} in the archive)");
        Console.WriteLine($"GPC BUILD MODEL UNITS: {measured} runs in units other than N-mm, maximum relative difference geometry {unitMaxima[0]:E2}, loads {unitMaxima[1]:E2}, section values {unitMaxima[2]:E2}");
        Console.WriteLine($"GPC BUILD MODEL PASS: {runs} Build Model runs on GPC data alone; {structures} examples with the structure of their definition, drafts of {drafts} elements; units measured against N-mm; merges, errors and remarks as expected");
    }

    /// <summary>Units: a model built from data in other units against the model of the same structure built in N-mm: the same graph (every element
    /// name, attribute and non-numeric text, the GUIDs by their order of appearance), the maximum relative difference of the numbers per class.</summary>
    private static string MeasureUnits(string label, GPC.Model.Models.Model model, GPC.Model.Models.Model reference, out double[] maxima)
    {
        void Fail(string what) => throw new Exception("GPC BUILD MODEL UNITS FAIL (" + label + "): " + what);
        GPC.Model.Models.ModelRevisions.AnalysisFingerprint(model); GPC.Model.Models.ModelRevisions.AnalysisFingerprint(reference);
        var x = System.Xml.Linq.XDocument.Parse(GpcDraftCapture.WithoutGuids(GpcInputComparison.Document(model))).Descendants().ToArray();
        var y = System.Xml.Linq.XDocument.Parse(GpcDraftCapture.WithoutGuids(GpcInputComparison.Document(reference))).Descendants().ToArray();
        if (x.Length != y.Length) Fail($"{x.Length} / {y.Length} elements in the documents");
        maxima = new double[3]; int differing = 0;
        static int Class(System.Xml.Linq.XElement e)
        {
            string path = string.Join("/", e.AncestorsAndSelf().Select(a => a.Name.LocalName));
            if (path.Contains("Propert") || path.Contains("Section") || path.Contains("Material")) return 2;
            if (path.Contains("Load") || path.Contains("Pressure") || path.Contains("Temperature") || path.Contains("Stress") || path.Contains("Gradient") || path.Contains("InitialState")) return 1;
            return 0;
        }
        for (int i = 0; i < x.Length; i++)
        {
            string where = string.Join("/", x[i].AncestorsAndSelf().Reverse().Select(a => a.Name.LocalName));
            if (x[i].Name != y[i].Name) Fail($"element {i}: {where} / {y[i].Name.LocalName}");
            string Attributes(System.Xml.Linq.XElement e) => string.Join(" ", e.Attributes().Select(a => a.Name + "=" + a.Value));
            if (Attributes(x[i]) != Attributes(y[i])) Fail($"attributes of {where}: '{Attributes(x[i])}' / '{Attributes(y[i])}'");
            if (x[i].HasElements || y[i].HasElements || x[i].Value == y[i].Value) continue;
            const System.Globalization.NumberStyles Float = System.Globalization.NumberStyles.Float;
            if (!double.TryParse(x[i].Value, Float, System.Globalization.CultureInfo.InvariantCulture, out double p) || !double.TryParse(y[i].Value, Float, System.Globalization.CultureInfo.InvariantCulture, out double q))
                Fail($"text of {where}: '{x[i].Value}' / '{y[i].Value}'");
            double relative = Math.Abs(p - q) / Math.Max(Math.Abs(p), Math.Abs(q));
            int c = Class(x[i]); maxima[c] = Math.Max(maxima[c], relative); differing++;
            if (relative > 1e-12) Console.WriteLine($"GPC BUILD MODEL UNITS ({label}): {where} {x[i].Value} / N-mm {y[i].Value} (relative {relative:E2})");
        }
        return $"{x.Length} elements, {differing} numbers differ from the N-mm build; maximum relative difference: geometry {maxima[0]:E2}, loads {maxima[1]:E2}, section values {maxima[2]:E2}";
    }

    /// <summary>Example 02 (the beam of 6000 mm on 4 beams, simply supported, q = −1 N/mm along Z) with its data in the units given.</summary>
    private static GpcNativeExamples.Inputs Units02(BridgeUnits u)
    {
        var b = new GpcNativeExamples.Builder("units-02-" + u.Units);
        double L2(double mm2) => mm2 / (u.Length * u.Length); double L4(double mm4) => mm4 / Math.Pow(u.Length, 4);
        var property = GpcPropertyBuilders.Beam("Elastic beam", 5, GpcSectionBuilders.Generic(L2(60000), L4(450000000), L4(200000000), L4(100000000), 0, 0, 0, 0, 0, 0, 0), GpcNativeExamples.Steel());
        var main = GpcCaseBuilders.Group("Permanent", null);
        for (int i = 0; i <= 4; i++) { int n = b.Node(u.FromMm(6000.0 * i / 4), 0, 0, (i + 1).ToString(), main); b.Restrain(n, i == 0, true, i == 0 || i == 4, true, false, true); }
        for (int k = 0; k < 4; k++) { b.Beam(k, k + 1, property, main, (k + 1).ToString()); b.UniformZ(k, "q", -1 * u.Length / u.Force); }
        return b.Build("units").Inputs;
    }

    /// <summary>Example 03 (the plate patch 1000 × 500 × 10 mm, 1000 N at two corners) with its data in the units given.</summary>
    private static GpcNativeExamples.Inputs Units03(BridgeUnits u)
    {
        var b = new GpcNativeExamples.Builder("units-03-" + u.Units);
        var points = new[] { (0.0, 0.0, 0.0), (1000.0, 0.0, 0.0), (1000.0, 500.0, 0.0), (0.0, 500.0, 0.0) }.Select(p => (u.FromMm(p.Item1), u.FromMm(p.Item2), u.FromMm(p.Item3))).ToArray();
        for (int i = 0; i < 4; i++)
        {
            int n = b.Node(points[i].Item1, points[i].Item2, points[i].Item3, (i + 1).ToString(), null);
            b.Restrain(n, i == 0 || i == 3, i == 0, true, true, true, true);
            if (i == 1 || i == 2) b.Force(n, "Tension", u.FromN(1000), 0, 0);
        }
        b.Plates.Add(GpcPlateBuilders.Create(points, GpcPropertyBuilders.Plate("Shell patch", 0, GpcNativeExamples.Steel(), u.FromMm(10), u.FromMm(10)), null, 0, 0, "1", b.Next("plate")));
        return b.Build("units").Inputs;
    }

    /// <summary>What the examples lack outside N-mm: a restraint frame away from the origin (its axes unitised by the normalisation), an explicit
    /// section with shear-centre offsets and a principal angle, a node force and a point load on a beam; data in the units given.</summary>
    private static GpcNativeExamples.Inputs UnitsSynthetic(BridgeUnits u)
    {
        var b = new GpcNativeExamples.Builder("units-synthetic-" + u.Units);
        double L(double mm) => u.FromMm(mm); double L2(double mm2) => mm2 / (u.Length * u.Length); double L4(double mm4) => mm4 / Math.Pow(u.Length, 4);
        var generic = GpcPropertyBuilders.Beam("PG", 5, GpcSectionBuilders.Generic(L2(12000), L4(3.1e8), L4(7.7e7), L4(2.3e6), L(13), L(-7), L2(5000), L2(4000), L(10), L(-20), 0.3), GpcNativeExamples.Steel());
        int a = b.Node(0, 0, 0, "1", null), c = b.Node(L(2500), 0, 0, "2", null), d = b.Node(L(5000), L(1200), 0, "3", null);
        b.Restrain(a, true, true, true, true, true, true);
        b.Nodes[d] = GpcNodeBuilders.WithFrame(b.Nodes[d], L(5000), L(1200), 0, 1, 2, 0, -2, 1, 0.5, b.Next("frame"));
        b.Restrain(d, false, false, true, false, false, false);
        b.Force(c, "Q", 0, u.FromN(-500), u.FromN(-1500));
        var elastic = GpcPropertyBuilders.Beam("Elastic beam", 5, GpcSectionBuilders.Generic(L2(60000), L4(450000000), L4(200000000), L4(100000000), 0, 0, 0, 0, 0, 0, 0), GpcNativeExamples.Steel());
        b.Beam(a, c, elastic, null, "1"); b.Beam(c, d, generic, null, "2");
        b.Beams[1] = GpcBeamBuilders.WithPointLoad(b.Beams[1], b.Case("Q"), false, 0.25, new GPC.Geometry.Vector3d(0, 0, u.FromN(-800)), b.Next("point"));
        return b.Build("units").Inputs;
    }

    /// <summary>
    /// Synthetic merges (N-mm, the tolerance of the plug-in, 0.001), every count and consequence from the data:
    /// - nodes n1 (0,0,0, fixed), n2 (1000,0,0), n3 (2000,0,0); A and B two branches of one GH datum at n2 (a force −1 along Z in Q each, B
    ///   restrained along Z): one merge by GH GUID (other content), and A within the tolerance of n2: a position merge; «N-near» 0.0004 from n3
    ///   (a force 1 along X in Dead): a second position merge;
    /// - beams b1 (n1–n2), b2 (n2–n3) given twice (the same goo: a self-merge); A and B two branches of one beam datum on n1–n2 (a distributed
    ///   −3 along Z in Q, a point −7 along Z in Dead and the Element ID «B-Z»): a merge; «shifted» from 0.0003 off n2 (moved onto n2) to 0.5 off n3
    ///   in every direction (a node created: «5»); «zero» from n1 to 0.0002 off n1 (moved onto n1: zero length, removed);
    /// - expected: merges nodes/beams/plates 1/2/0 (self 0/1/0), position merges 2, ends moved 2, end nodes 1, zero-length 1, reader moved 0,
    ///   re-anchored 1, no copies dropped; nodes 1, 2 (the loads of A and B, summed per case by the resolution, the restraint of B), 3 (the load of «N-near»), 5; beams 1, 2, 3 (the
    ///   loads of both branches), 4 (2–5); cases Q, Dead.
    /// Then the errors and remarks: a beam with a bridge member given twice (the former error, two members); two beams with the Element ID «B-A»
    /// and other data (the error naming the ID); the same beam datum with the Element ID «B-A» given twice under two GH GUIDs (kept once, the remark).
    /// </summary>
    private static int Merges()
    {
        const string label = "synthetic merges";
        void Fail(string what) => throw new Exception("GPC BUILD MODEL FAIL (" + label + "): " + what);
        var p = GpcFixtures.RectangleProperty();
        var q = GpcCaseBuilders.Case("Q", false); var dead = GpcCaseBuilders.Case(GpcCaseBuilders.Dead, false);
        var n1 = GpcNodeBuilders.WithRestraint(GpcNodeBuilders.Create(0, 0, 0, "", null, Id("n1")), true, true, true, true, true, true, Id("n1 restraint"));
        var n2 = GpcNodeBuilders.Create(1000, 0, 0, "", null, Id("n2")); var n3 = GpcNodeBuilders.Create(2000, 0, 0, "", null, Id("n3"));
        var nodeA = GpcNodeBuilders.WithForce(GpcNodeBuilders.Create(1000, 0, 0, "", null, Id("A")), q, 0, 0, -1, Id("A force"));
        var nodeB = GpcNodeBuilders.WithRestraint(GpcNodeBuilders.WithForce(GpcNodeBuilders.Create(1000, 0, 0, "", null, Id("B")), q, 0, 0, -1, Id("B force")), false, false, true, false, false, false, Id("B restraint"));
        var near = GpcNodeBuilders.WithForce(GpcNodeBuilders.WithLabel(GpcNodeBuilders.Create(2000.0004, 0, 0, "", null, Id("near")), "N-near", Id("near label")), dead, 1, 0, 0, Id("near force"));
        GPC.Geometry.Point3d P(double x, double y, double z) => new GPC.Geometry.Point3d(x, y, z);
        var b1 = GpcBeamBuilders.New(P(0, 0, 0), P(1000, 0, 0), p, null, 0, 0, 0, Id("b1"));
        var b2 = GpcBeamBuilders.New(P(1000, 0, 0), P(2000, 0, 0), p, null, 0, 0, 0, Id("b2"));
        var branchA = GpcBeamBuilders.WithDistributedLoad(GpcBeamBuilders.New(P(0, 0, 0), P(1000, 0, 0), p, null, 0, 0, 0, Id("branch A")), q, BridgeLoadSchema.Uniform, false, "Z", -3, -3, 0, 0, 0, 0, true, Id("branch A load"));
        var branchB = GpcElementBuilders.WithLabel(GpcBeamBuilders.WithPointLoad(GpcBeamBuilders.New(P(0, 0, 0), P(1000, 0, 0), p, null, 0, 0, 0, Id("branch B")), dead, false, 0.5, new GPC.Geometry.Vector3d(0, 0, -7), Id("branch B load")),
            "B-Z", Id("branch B label"));
        var shifted = GpcBeamBuilders.New(P(1000, 0.0003, 0), P(2000.5, 0.5, 0.5), p, null, 0, 0, 0, Id("shifted"));
        var zero = GpcBeamBuilders.New(P(0, 0, 0), P(0.0002, 0, 0), p, null, 0, 0, 0, Id("zero"));
        Guid gA = Id("gh A"), gBranch = Id("gh branch");
        var same = Goo("GH_Beam", b2, Id("gh b2"));
        var elements = new[] { Goo("GH_Node", n1, Id("gh n1")), Goo("GH_Node", n2, Id("gh n2")), Goo("GH_Node", n3, Id("gh n3")), Goo("GH_Node", nodeA, gA), Goo("GH_Node", nodeB, gA), Goo("GH_Node", near, Id("gh near")),
            Goo("GH_Beam", b1, Id("gh b1")), same, same, Goo("GH_Beam", branchA, gBranch), Goo("GH_Beam", branchB, gBranch), Goo("GH_Beam", shifted, Id("gh shifted")), Goo("GH_Beam", zero, Id("gh zero")) };
        var run = Solve(label, elements, Array.Empty<LegacyCombinationDraft>(), Array.Empty<LegacyStageDraft>());
        if (run.Output == null || run.Errors.Length != 0) Fail("no model: " + string.Join("; ", run.Errors));
        string actual = run.Input.Report.BuildModel.ToString();
        const string expectedCounts = "merges by GH GUID nodes/beams/plates 1/2/0 (self-merges 0/1/0), position merges 2, ends moved 2, end nodes 1, zero-length beams 1, corners moved 0, corner nodes 0, reader moved 0, re-anchored 1, copies of the same ID dropped beams/plates 0/0";
        if (actual != expectedCounts) Fail($"counts '{actual}', expected '{expectedCounts}'");
        string structure = Structure(run.Input.Model);
        const string expected = "N 1 0,0,0 [] R111111\nN 2 1000,0,0 [] R001000 F Q:0,0,-2 x1\nN 3 2000,0,0 [] F Dead:1,0,0 x1\nN 5 2000.5,0.5,0.5 []\n"
            + "B 1 1-2 P []\nB 2 2-3 P []\nB 3 1-2 P [] L Q:1 L Dead:1\nB 4 2-5 P []\nCASES Q,Dead\n";
        if (structure != expected) Fail("structure:\n" + structure + "expected:\n" + expected);
        Console.WriteLine($"GPC BUILD MODEL PASS ({label}): {actual}; the structure of the data");

        // The errors and remarks.
        var plan = new BridGHe.Common.Geometry.BridgeMemberPlan(new R.Point3d(0, 0, 0), new R.Point3d(1000, 0, 0), () => GpcFixtures.RectangleProperty(), "B-C", true, BridGHe.Common.Geometry.BridgeMemberFamily.Girder,
            1, 1, 1, "Girder:1:1", 0, 1000, "G", new R.Vector3d(0, 1, 0), new R.Vector3d(1, 0, 0), null, null);
        var member = Goo("GH_Beam", GpcBridgeGeometryBuilders.Assigned(plan, Id("member")), Id("gh member"));
        var self = Solve("synthetic self-merge with a member", new[] { Goo("GH_Node", n1, Id("gh n1")), Goo("GH_Node", n2, Id("gh n2")), member, member }, Array.Empty<LegacyCombinationDraft>(), Array.Empty<LegacyStageDraft>());
        if (self.Output != null || !self.Errors.Contains("LegacyDuplicateBridgeMemberAttribute: beam B-C")) Fail("a member merged with itself: '" + string.Join("; ", self.Errors) + "'");
        Console.WriteLine("GPC BUILD MODEL (synthetic self-merge with a member): no model, '" + string.Join("; ", self.Errors) + "'");
        var labelled = GpcElementBuilders.WithLabel(b1, "B-A", Id("b1 label"));
        var other = GpcElementBuilders.WithLabel(b2, "B-A", Id("b2 label"));
        var twice = Solve("synthetic ID twice", new[] { Goo("GH_Beam", labelled, Id("gh labelled")), Goo("GH_Beam", other, Id("gh other")) }, Array.Empty<LegacyCombinationDraft>(), Array.Empty<LegacyStageDraft>());
        const string idTwice = "Beam ID B-A is given by two elements with different data (LegacyElementIdGivenTwice): give each element its own Element ID, or connect it once.";
        if (twice.Output != null || !twice.Errors.Contains(idTwice)) Fail("an Element ID given twice with other data: '" + string.Join("; ", twice.Errors) + "'");
        Console.WriteLine("GPC BUILD MODEL (synthetic ID twice): no model, '" + string.Join("; ", twice.Errors) + "'");
        var copies = Solve("synthetic copies", new[] { Goo("GH_Beam", labelled, Id("gh copy 1")), Goo("GH_Beam", labelled, Id("gh copy 2")) }, Array.Empty<LegacyCombinationDraft>(), Array.Empty<LegacyStageDraft>());
        if (copies.Output == null || copies.Input.Model.BeamElements.Count != 1 || !copies.Remarks.Contains("Elements given more than once with the same data, kept once: beams B-A.")
            || copies.Input.Report.BuildModel.DroppedCopies[1].SingleOrDefault() != "B-A")
            Fail("copies of the same data: " + string.Join("; ", copies.Errors.Concat(copies.Remarks)));
        Console.WriteLine("GPC BUILD MODEL (synthetic copies): one beam, '" + string.Join("; ", copies.Remarks) + "'");
        return 4;
    }
}
