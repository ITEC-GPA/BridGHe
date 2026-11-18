using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Parameters;
using GPC.Model.Loads;
using System.Text;

// Task 179: exercise the public GH reader/solution and the actual parameter menu.
// Never writes a fixture, capture or baseline. Optional F7 files are private copies.
internal static class F7Checks
{
    private static Assembly plugin;
    private static readonly List<string> failures = new();
    private static int checks;
    private static void Check(bool ok, string what)
    {
        checks++;
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + what);
        if (!ok) failures.Add(what);
    }
    private static void Start() { failures.Clear(); checks = 0; plugin = BridgeMergeTests.Initialize(); }
    private static int Finish(string name)
    {
        Console.WriteLine($"F7 {name}: {checks - failures.Count}/{checks} PASS, {failures.Count} FAIL");
        return failures.Count == 0 ? 0 : 1;
    }
    private static IGH_Param Parameter(string name) => (IGH_Param)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.Parameters." + name, true));
    private static void Add(GH_Document doc, IGH_DocumentObject obj) { obj.CreateAttributes(); doc.AddObject(obj, false); }
    private static void Solve(GH_Document doc)
    {
        GH_Document.EnableSolutions = true; doc.Enabled = true;
        doc.NewSolution(false, GH_SolutionMode.Silent);
    }
    private static string[] Warnings(IGH_ActiveObject obj) => obj.RuntimeMessages(GH_RuntimeMessageLevel.Warning).ToArray();
    private static void Menu(IGH_Param param, string text)
    {
        using var menu = new ContextMenuStrip();
        param.AppendMenuItems(menu);
        menu.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == text).PerformClick();
    }
    /// <summary>The input <paramref name="name"/> of the F7 checks: <c>artifacts\179\input</c> of the checkout when it has the file, else the
    /// versioned copy of the F7 files of 14/11 (<c>Fixtures/f7-179</c>, 0.4.2: a new checkout has no <c>artifacts\179</c>).</summary>
    private static string Input179(string name)
    {
        string local = Path.Combine("artifacts", "179", "input", name);
        return File.Exists(local) ? local : Path.Combine("tests", "BridGHe.Smoke", "Fixtures", "f7-179", name);
    }
    internal static int Readers(string[] args)
    {
        Start();
        string file = args.SkipWhile(a => a != "--f7-readers").Skip(1).FirstOrDefault()
            ?? Input179("F05b-schema2-sviluppo.ghx");
        var archive = new GH_Archive();
        using var doc = new GH_Document();
        Rhino.RhinoApp.CommandWindowCaptureEnabled = true;
        string commandBefore = Rhino.RhinoApp.CommandHistoryWindowText;
        Check(archive.ReadFromFile(Path.GetFullPath(file)) && archive.ExtractObject(doc, "Definition"), "F05b: document extracted");
        foreach (var message in archive.Messages) Console.WriteLine("READER: " + message.Message);
        string commandAfter = Rhino.RhinoApp.CommandHistoryWindowText;
        Console.WriteLine("RHINO COMMAND HISTORY DELTA: " + (commandAfter.StartsWith(commandBefore) ? commandAfter.Substring(commandBefore.Length) : commandAfter));
        Check(archive.Messages.Any(), "F05b: GH_IReader records the refusal");
        Grasshopper.Instances.DocumentServer.AddDocument(doc);
        try
        {
            var bad = doc.Objects.OfType<IGH_Param>().Single(p => p.NickName == "INC schema 2");
            var good = doc.Objects.OfType<IGH_Param>().Single(p => p.NickName.StartsWith("S355 di controllo"));
            for (int i = 0; i < 2; i++)
            {
                doc.ExpireSolution(); Solve(doc);
                Console.WriteLine("RUNTIME: " + bad.NickName + " | " + string.Join(" | ", Warnings(bad)));
                Console.WriteLine("VALUES: " + string.Join(" | ", bad.VolatileData.AllData(true).Select(g => g == null ? "null" : g.GetType().Name + ":" + g)));
                Check(bad.VolatileData.AllData(true).All(g => g == null || !g.IsValid), "F05b: refused value remains null/invalid, solution " + i);
                Check(Warnings(bad).Any(m => m.Contains(string.Format(GpcDraftArchive.NewerSchemaMessage, "schema dei record BGHGPC1 2"))), "F05b: persistent explicit warning, solution " + i);
                Check(good.VolatileData.AllData(true).Any(g => g != null && g.IsValid) && Warnings(good).Length == 0, "F05b: valid S355 unaffected, solution " + i);
            }
        }
        finally { Grasshopper.Instances.DocumentServer.RemoveDocument(doc); }
        ReaderPaths();
        GenericArchive();
        RefusalIndexChecks();
        return Finish("readers");
    }

    // Task 189: count real parameter visits, without a production counter or test-only callback.
    private sealed class ScanParameter : Param_GenericObject, IGH_InstanceDescription
    {
        internal int GuidReads, HashReads;
        Guid IGH_InstanceDescription.InstanceGuid { get { GuidReads++; return base.InstanceGuid; } }
        public override int GetHashCode() { HashReads++; return base.GetHashCode(); }
    }

    private static void RefusalIndexChecks()
    {
        const BindingFlags statics = BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
        var type = plugin.GetType("BridGHe.Grasshopper.Helpers.ReadRefusalWarnings", true);
        var pending = (System.Collections.IList)type.GetField("PendingReads", statics).GetValue(null);
        var stateType = type.GetNestedType("State", BindingFlags.NonPublic);
        var bind = type.GetMethod("Bind", statics); var show = type.GetMethod("Show", statics);
        Check(pending.Count == 0, "189: reader cases left no pending read before the empty-queue measurement");
        if (pending.Count != 0) return;
        using (var document = new GH_Document())
        {
            var parameters = Enumerable.Range(0, 100).Select(_ => new ScanParameter()).ToArray();
            foreach (var parameter in parameters) Add(document, parameter);
            var state = Activator.CreateInstance(stateType, true);
            foreach (var parameter in parameters) parameter.GuidReads = parameter.HashReads = 0;
            for (int i = 0; i < 10; i++) bind.Invoke(null, new[] { (object)document, state });
            int reads = parameters.Sum(p => p.GuidReads);
            Check(reads == 0, $"189: empty PendingReads, 10 Bind calls on 100 parameters: {reads} InstanceGuid reads (expected 0)");
            foreach (var parameter in parameters) parameter.HashReads = 0;
            for (int i = 0; i < 10; i++) show.Invoke(null, new[] { (object)document, state });
            int hashes = parameters.Sum(p => p.HashReads);
            Check(hashes == 0, $"189: empty state.Messages, 10 Show calls on 100 parameters: {hashes} hash visits (expected 0)");
        }
        using (var document = new GH_Document())
        {
            var first = new ScanParameter(); var second = new ScanParameter(); Add(document, first); Add(document, second);
            second.NewInstanceGuid(first.InstanceGuid); // Make a genuinely damaged document after AddObject's normal ID checks.
            Check(first.InstanceGuid == second.InstanceGuid, "189: duplicate parameter InstanceGuid actually present");
            var pendingType = type.GetNestedType("Pending", BindingFlags.NonPublic);
            var record = Activator.CreateInstance(pendingType, true); var state = Activator.CreateInstance(stateType, true);
            pendingType.GetField("DocumentId", fields).SetValue(record, document.DocumentID);
            const string message = "Task 189 duplicate parameter refusal";
            ((System.Collections.IDictionary)pendingType.GetField("Messages", fields).GetValue(record)).Add(first.InstanceGuid, new HashSet<string> { message });
            pending.Add(record);
            try
            {
                bool bound = true;
                try { bind.Invoke(null, new[] { (object)document, state }); }
                catch (TargetInvocationException error) { bound = false; Console.WriteLine("189 Bind exception: " + error.InnerException); }
                Check(bound, "189: duplicate InstanceGuid does not throw from Bind/SolutionStart");
                if (bound) show.Invoke(null, new[] { (object)document, state });
                Check(bound && Warnings(first).Contains(message) && !Warnings(second).Contains(message), "189: first parameter owns the refusal when InstanceGuid is duplicated");
                Check(bound && pending.Count == 0, "189: the duplicate-ID refusal is consumed once");
            }
            finally { pending.Remove(record); }
        }
    }

    private static GH_Chunk Container(GH_Chunk root, Guid id)
    {
        if (root.ItemExists("InstanceGuid") && root.GetGuid("InstanceGuid") == id) return root;
        foreach (var child in root.Chunks) { var found = Container((GH_Chunk)child, id); if (found != null) return found; }
        return null;
    }

    private static void ReaderPaths()
    {
        var cases = new List<(string Type, string Label, Action<GH_Chunk> Break)>();
        foreach (string type in new[] { "Fem.GH_Beam", "Fem.GH_BeamProperty", "Fem.GH_BeamSection", "Fem.GH_LoadCase", "Fem.GH_LoadCombination", "Fem.GH_Material", "Fem.GH_Model", "Fem.GH_Node", "Fem.GH_PlateProperty", "Fem.GH_StagedConstruction", "GH_CompositeBeamProperty" })
            cases.Add((type, "P2D damaged " + type, c => { c.SetByteArray("Data", new byte[] { 1, 2, 3 }); c.SetByteArray("BridGHeRecords", Encoding.UTF8.GetBytes("<broken")); }));
        cases.Add(("Fem.GH_Model", "P2D model inner drafts refusal", c =>
        {
            var (data, records) = GpcDraftArchive.Write(GpcFixtures.Cantilever("F7 refused drafts"));
            c.SetByteArray("Data", data); c.SetByteArray("BridGHeRecords", records);
            c.SetByteArray(GpcModelDrafts.Chunk, new byte[] { 1, 2, 3 });
        }));
        foreach (string kind in new[] { "Unknown", "BridgeStudOptions" })
            cases.Add(("BridgeDetailGoo", "MU-01 detail " + kind, c => { c.SetString("Kind", kind); c.SetString("Data", "{"); }));
        foreach (string kind in new[] { "Unknown", "BridgeSegments" })
            cases.Add(("BridgeLayoutGoo", "MU-01 layout " + kind, c => { c.SetString("Kind", kind); c.SetString("Layout", "{"); }));
        cases.Add(("MovingGoo", "MU-01 moving JSON", c => c.SetString("Data", "{")));
        cases.Add(("BridgeWorkflowGoo", "MU-01 former workflow", c => c.SetByteArray("Workflow", new byte[] { 1 })));
        cases.Add(("GH_CompositeBeam", "MU-01 composite former cache", c => { c.SetGuid("Guid", Guid.NewGuid()); c.SetByteArray("Data", new byte[] { 1 }); }));
        cases.Add(("GH_CompositeBeam", "MU-01 composite former FemmR6 cache", c => { c.SetGuid("Guid", Guid.NewGuid()); c.SetByteArray("FemmR6", new byte[] { 1 }); }));
        cases.Add(("GH_CompositeBeam", "MU-01 composite invalid GUID", c => { }));
        cases.Add(("GH_CompositeBeam", "MU-01 composite rebuildable reference", c => c.SetGuid("Guid", Guid.NewGuid())));

        foreach (var item in cases)
        foreach (bool input in new[] { false, true })
        {
            using var doc = new GH_Document();
            IGH_Param owner;
            if (input)
            {
                var component = (GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.Loads.BeamPointLoadComponent", true));
                Add(doc, component); owner = component.Params.Input[0];
            }
            else { owner = new Param_GenericObject(); Add(doc, owner); }
            var archive = new GH_Archive(); archive.AppendObject(doc, "Definition");
            archive.ClearMessages(); // Compare the refusal, not the successful "Object list written" bookkeeping message.
            var container = Container(archive.GetRootNode, owner.InstanceGuid);
            if (container == null) throw new Exception("F7 owner chunk missing: " + owner.InstanceGuid);
            var payload = (GH_Chunk)container.CreateChunk("F7RefusedPayload"); item.Break(payload);
            var goo = (IGH_Goo)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.DataTypes." + item.Type, true));
            Check(!goo.Read(payload), item.Label + ": reader refuses");
            string[] messages = archive.Messages.Select(m => m.Message).ToArray();
            Check(messages.Length > 0, item.Label + ": archive reason recorded");
            Grasshopper.Instances.DocumentServer.AddDocument(doc);
            try
            {
                Solve(doc);
                Check(messages.Length > 0 && messages.All(m => Warnings(owner).Contains(m)), item.Label + (input ? ": input" : ": floating") + " warning reproduces the exact reader reason");
                if (owner is Param_GenericObject generic)
                {
                    generic.SetPersistentData(new GH_String("recovered")); generic.ExpireSolution(false); Solve(doc);
                    Check(messages.All(m => !Warnings(owner).Contains(m)), item.Label + ": a valid replacement removes the warning");
                }
            }
            finally { Grasshopper.Instances.DocumentServer.RemoveDocument(doc); }
        }
    }

    private static IEnumerable<GH_Chunk> Chunks(GH_Chunk chunk)
    {
        yield return chunk;
        foreach (GH_Chunk child in chunk.Chunks) foreach (var nested in Chunks(child)) yield return nested;
    }

    private static void GenericArchive()
    {
        using var source = new GH_Document();
        var p = new Param_GenericObject(); Add(source, p);
        var goo = (IGH_Goo)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.DataTypes.BridgeDetailGoo", true), new object[] { new GPC.Checkers.CompositeBridge.BridgeStudOptions() });
        p.SetPersistentData(goo);
        var archive = new GH_Archive(); Check(archive.AppendObject(source, "Definition"), "MU-01 generic: real GH archive written");
        var payload = Chunks(archive.GetRootNode).Single(c => c.ItemExists("Kind"));
        payload.RemoveItem("Kind"); payload.SetString("Kind", "F7 unknown kind");
        // Two reads of the same definition, before either is put on the canvas. The instance IDs are identical.
        using var first = new GH_Document(); using var second = new GH_Document();
        Check(archive.ExtractObject(first, "Definition"), "MU-01 generic: first document read");
        var secondArchive = new GH_Archive(); secondArchive.Deserialize_Xml(archive.Serialize_Xml());
        Check(secondArchive.ExtractObject(second, "Definition"), "MU-01 generic: second document read");
        archive = null; secondArchive = null; GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        foreach (var doc in new[] { first, second })
        {
            Grasshopper.Instances.DocumentServer.AddDocument(doc);
            try
            {
                Solve(doc); var owner = doc.Objects.OfType<Param_GenericObject>().Single();
                Check(Warnings(owner).Any(m => m.Contains("F7 unknown kind")), "MU-01 generic: actual archive refusal survives GC and separate document registration");
                Check(owner.VolatileData.AllData(true).All(g => g == null || !g.IsValid), "MU-01 generic: rejected data never enters volatile data");
            }
            finally { Grasshopper.Instances.DocumentServer.RemoveDocument(doc); }
        }
    }
    internal static int Local()
    {
        Start();
        using var doc = new GH_Document();
        var cs = Parameter("CoordinateSystemParam"); Add(doc, cs);
        Menu(cs, "Global"); Menu(cs, "Local"); Solve(doc);
        Check(Warnings(cs).Length == 0, "Local menu: floating parameter has no warning: " + string.Join(" | ", Warnings(cs)));
        Check(cs.VolatileData.AllData(true).Any(g => g != null && g.IsValid && g.ToString().Contains("Local")), "Local menu: a valid explicit Local datum is visible");
        Check(cs.NickName == "Local", "Local menu: canvas label is Local");
        var archive = new GH_Archive(); Check(archive.AppendObject(doc, "Definition"), "Local document written");
        using var reopened = new GH_Document(); Check(archive.ExtractObject(reopened, "Definition"), "Local document reopened"); Solve(reopened);
        var restored = reopened.Objects.OfType<IGH_Param>().Single();
        Check(Warnings(restored).Length == 0 && restored.VolatileData.AllData(true).Any(g => g != null && g.IsValid && g.ToString().Contains("Local")), "Local survives document archive without warning");
        foreach (string extension in new[] { ".ghx", ".gh" })
        {
            string path = Path.GetFullPath(Path.Combine("artifacts", "179", "local-roundtrip" + extension));
            Check(archive.WriteToFile(path, true, false), "Local writes " + extension);
            using var disk = new GH_Document(); var a = new GH_Archive();
            Check(a.ReadFromFile(path) && a.ExtractObject(disk, "Definition"), "Local reopens " + extension); Solve(disk);
            var p = disk.Objects.OfType<IGH_Param>().Single();
            Check(Warnings(p).Length == 0 && p.NickName == "Local" && p.VolatileData.AllData(true).All(g => g != null && g.IsValid && g.ToString() == "Local"), "Local preserved on disk " + extension);
        }
        var source = Parameter("CoordinateSystemParam"); Add(doc, source); Menu(source, "Global"); cs.AddSource(source); cs.ExpireSolution(false); Solve(doc);
        Check(cs.VolatileData.AllData(true).All(g => g != null && g.IsValid && g.ToString() == "Global") && cs.NickName == "Global", "floating parameter: connected Global overrides saved Local");
        cs.RemoveAllSources(); cs.ExpireSolution(false); Solve(doc);
        Check(cs.VolatileData.AllData(true).All(g => g != null && g.IsValid && g.ToString() == "Local") && Warnings(cs).Length == 0, "floating parameter: disconnect restores saved Local");
        BeamLoads();
        ConnectedBeamFrames();
        PlateLoads();
        F09();
        return Finish("local");
    }

    private static void ConnectedBeamFrames()
    {
        GPC.Geometry.Point3d P(double x, double y, double z = 0) => new(x, y, z);
        var frames = new (string Name, GPC.Geometry.CoordinateSystem Frame, bool Global)[]
        {
            ("Global", GpcLoadFrames.Global(), true),
            ("stretched Global", new(P(0, 0), P(2, 0), P(0, 3), "Stretched"), true),
            ("off-axis plane Global", new(P(0, 0), P(5, 0), P(4, 2), "Plane"), true),
            ("translated", new(P(1, 0), P(2, 0), P(1, 1), "Translated"), false),
            ("rotated", new(P(0, 0), P(0, 1), P(-1, 0), "Rotated"), false)
        };
        var beam = GpcBeamBuilders.New(P(100, 200, 300), P(3100, 4200, 300),
            GpcFixtures.RectangleProperty(), null, 0, 0, 0, Guid.NewGuid(), "189 inclined");
        var loadCase = GpcCaseBuilders.Case("189 Q", false);
        foreach (var frame in frames)
        foreach (string type in new[] { "BeamDistributedLoadComponent", "BeamPointLoadComponent", "BeamPointMomentComponent" })
        for (int axis = 0; axis < 3; axis++)
        {
            using var document = new GH_Document();
            var component = (GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.Loads." + type, true)); Add(document, component);
            ((Param_GenericObject)component.Params.Input[0]).SetPersistentData(Goo("GH_Beam", beam));
            ((Param_GenericObject)component.Params.Input[1]).SetPersistentData(Goo("GH_LoadCase", loadCase));
            var input = component.Params.Input.Single(p => p.Name == "Coordinate System"); Menu(input, "Local");
            var source = new Param_GenericObject(); Add(document, source); source.SetPersistentData(Goo("GH_CoordinateSystem", frame.Frame)); input.AddSource(source);
            bool moment = type == "BeamPointMomentComponent", distributed = type == "BeamDistributedLoadComponent";
            if (distributed) { ((Param_Integer)component.Params.Input[4]).SetPersistentData(axis); Number(component, "PA", 10); }
            else { Number(component, (moment ? "M" : "P") + (axis + 1), 10); Number(component, "a", .4); }
            Solve(document);
            string at = $"189 {type}, connected {frame.Name}, axis {axis + 1}";
            var output = component.Params.Output[0].VolatileData.AllData(true).FirstOrDefault(g => g != null && g.IsValid);
            bool diagnostic = component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0 || Warnings(component).Length > 0;
            if (!frame.Global)
            {
                Check(output == null, at + ": unsupported absolute frame produces no load");
                Check(diagnostic, at + ": explicit refusal, same rule as distributed loads");
                continue;
            }
            Check(output != null && !diagnostic && Warnings(input).Length == 0, at + ": normalized Global accepted without a diagnostic");
            if (output == null) continue;
            var draft = (LegacyBeamDraft)output.GetType().GetProperty("Gpc").GetValue(output);
            double[] Vector(LegacyBeamDraft value)
            {
                var load = value.Beam.Assignments.Loads.Single();
                return distributed ? load.StartIntensity.GetGlobalForces() : ((PointLoad)load.Concentrated).GetGlobalForces();
            }
            bool Expected(double[] vector) => Enumerable.Range(0, 3).All(i => Math.Abs(vector[(moment ? 3 : 0) + i] - (i == axis ? 10 : 0)) < 1e-10);
            Check(Expected(Vector(draft)), at + ": exact global vector in N / N mm (N/mm for distributed load)");
            var (data, records) = GpcDraftArchive.Write(draft);
            Check(Expected(Vector(GpcDraftArchive.ReadBeam(data, records))), at + ": GPC archive retains the vector");
        }
    }

    private static void PlateLoads()
    {
        var plate = GpcPlateBuilders.Create(new[] { (0.0, 0.0, 0.0), (1000.0, 0.0, 0.0), (1000.0, 1000.0, 1000.0), (0.0, 1000.0, 1000.0) },
            GpcPropertyBuilders.Plate("F7 plate", 0, GpcFixtures.Steel(), 10, 10), null, 0, 0, "P", Guid.NewGuid());
        foreach (bool global in new[] { false, true })
        foreach (bool wire in new[] { false, true })
        {
            using var doc = new GH_Document();
            var c = (GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.Loads.PlatePressureComponent", true)); Add(doc, c);
            ((Param_GenericObject)c.Params.Input[0]).SetPersistentData(Goo("GH_Plate", plate));
            ((Param_GenericObject)c.Params.Input[1]).SetPersistentData(Goo("GH_LoadCase", GpcCaseBuilders.Case("Q", false)));
            ((Param_GenericObject)c.Params.Input[4]).SetPersistentData(new GH_Number(2));
            var input = c.Params.Input[2]; Menu(input, global ? "Local" : "Global");
            if (wire) { var p = Parameter("CoordinateSystemParam"); Add(doc, p); Menu(p, global ? "Global" : "Local"); input.AddSource(p); }
            else Menu(input, global ? "Global" : "Local");
            Solve(doc);
            string at = $"Plate Pressure {(global ? "Global" : "Local")} {(wire ? "wire" : "input")}";
            Check(Warnings(input).Length == 0 && c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, at + ": no warnings/errors");
            var goo = c.Params.Output[0].VolatileData.AllData(true).FirstOrDefault(g => g != null && g.IsValid);
            Check(goo != null, at + ": output available");
            if (goo == null) continue;
            var result = (LegacyPlateDraft)goo.GetType().GetProperty("Gpc").GetValue(goo);
            var load = (ShellFacePressure)result.Plate.Loads.Values.Single();
            Check(global ? load.NormalPressure == null && load.GlobalPressure.Z == 2 : load.NormalPressure == 2 && load.GlobalPressure == null, at + ": correct GPC pressure kind and value");
        }
    }

    private static void F09()
    {
        foreach (string name in new[] { "F09a-strain-obsoleto-cs-stirato.ghx", "F09b-legacy-0.3.0.ghx" })
        {
            string path = Input179(name);
            Check(File.Exists(path), name + ": input present (artifacts\\179\\input or Fixtures/f7-179)");
            if (!File.Exists(path)) continue;
            using var doc = new GH_Document(); var archive = new GH_Archive();
            Check(archive.ReadFromFile(Path.GetFullPath(path)) && archive.ExtractObject(doc, "Definition"), name + ": opened"); Solve(doc);
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BeamDistributedLoadComponent"))
            {
                Console.WriteLine("F09 OBSERVED " + name + " | " + c.NickName + " | " + c.Message);
                var output = c.Params.Output[0].VolatileData.AllData(true).FirstOrDefault(g => g != null && g.IsValid);
                Check(output != null && c.Message.Contains("Global") && c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, name + ": stretched/global system stays Global");
                if (output != null)
                {
                    var draft = (LegacyBeamDraft)output.GetType().GetProperty("Gpc").GetValue(output);
                    foreach (var load in draft.Beam.Assignments.Loads)
                        Console.WriteLine("F09 VECTOR " + name + " | " + string.Join(",", load.StartIntensity.GetGlobalForces()));
                }
            }
        }
    }

    private static IGH_Goo Goo(string type, object value)
    {
        var method = plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem." + type, true).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "FromGpc");
        return (IGH_Goo)method.Invoke(null, new[] { value }.Concat(Enumerable.Repeat((object)null, method.GetParameters().Length - 1)).ToArray());
    }
    private static void Number(GH_Component c, string name, double value)
    {
        var p = (Param_Number)c.Params.Input.Single(p => p.Name == name); p.PersistentData.Clear(); p.SetPersistentData(value);
    }
    internal static int Native(string[] args)
    {
        Start();
        string folder = args.SkipWhile(a => a != "--f7-native").Skip(1).FirstOrDefault()
            ?? throw new ArgumentException("--f7-native <output folder>, coordinator only under the Straus7 lock");
        Directory.CreateDirectory(Path.GetFullPath(folder));
        BeamLoads(Path.GetFullPath(folder));
        return Finish("native export");
    }

    private static GpcModels.Conversion ModelFor(LegacyBeamDraft beam) => LegacyBuildModel.Build("F7 coordinate systems", new BridgeUnits(DefinitionUnits.Nmm), .001,
        new List<(Guid, LegacyNodeDraft)>(), new List<(Guid, LegacyBeamDraft)> { (beam.Beam.Guid, beam) }, new List<(Guid, LegacyPlateDraft)>(),
        new List<LegacyCombinationDraft>(), new List<LegacyStageDraft>());

    private static void BeamLoads(string nativeFolder = null)
    {
        // 3-4-5 beam, not on a global axis. At zero section angle:
        // e1=(-4/5,3/5,0), e2=(0,0,1), e3=(3/5,4/5,0).
        // N-mm; distributed intensities N/mm, forces N, moments N mm.
        var beam = GpcBeamBuilders.New(new GPC.Geometry.Point3d(100, 200, 300), new GPC.Geometry.Point3d(3100, 4200, 300),
            GpcFixtures.RectangleProperty(), null, 0, 0, 0, Guid.NewGuid(), "F7 inclined");
        var expected = new[] { new[] { -8.0, 6.0, 0.0 }, new[] { 0.0, 0.0, 10.0 }, new[] { 6.0, 8.0, 0.0 } };
        foreach (string type in new[] { "BeamDistributedLoadComponent", "BeamPointLoadComponent", "BeamPointMomentComponent" })
        foreach (bool wire in new[] { false, true })
        foreach (bool global in new[] { false, true })
        for (int axis = 0; axis < 3; axis++)
        {
            using var doc = new GH_Document();
            var c = (GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.Loads." + type, true)); Add(doc, c);
            ((Param_GenericObject)c.Params.Input[0]).SetPersistentData(Goo("GH_Beam", beam));
            ((Param_GenericObject)c.Params.Input[1]).SetPersistentData(Goo("GH_LoadCase", GpcCaseBuilders.Case("Q", false)));
            var input = c.Params.Input.Single(p => p.Name == "Coordinate System");
            Menu(input, global ? "Local" : "Global"); // Opposite stored selection: a wire must win.
            if (wire)
            {
                var source = Parameter("CoordinateSystemParam"); Add(doc, source); Menu(source, global ? "Global" : "Local"); input.AddSource(source);
            }
            else Menu(input, global ? "Global" : "Local");
            if (type == "BeamDistributedLoadComponent")
            {
                ((Param_Integer)c.Params.Input[4]).SetPersistentData(axis); Number(c, "PA", 10);
            }
            else
            {
                Number(c, (type == "BeamPointMomentComponent" ? "M" : "P") + (axis + 1), 10); Number(c, "a", .4);
            }
            Solve(doc);
            string at = $"{type}, {(wire ? "wire" : "input")}, {(global ? "Global" : "Local")}, axis {axis + 1}";
            Check(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0 && Warnings(input).Length == 0 && Warnings(c).Length == 0, at + ": no warnings/errors");
            var output = c.Params.Output[0].VolatileData.AllData(true).FirstOrDefault(g => g != null && g.IsValid);
            Check(output != null, at + ": output available");
            if (output == null) continue;
            var draft = (LegacyBeamDraft)output.GetType().GetProperty("Gpc").GetValue(output);
            var load = draft.Beam.Assignments.Loads.Single();
            double[] actual = type == "BeamDistributedLoadComponent" ? load.StartIntensity.GetGlobalForces() : ((PointLoad)load.Concentrated).GetGlobalForces();
            int offset = type == "BeamPointMomentComponent" ? 3 : 0;
            double[] want = global ? Enumerable.Range(0, 3).Select(i => i == axis ? 10.0 : 0.0).ToArray() : expected[axis];
            Check(Enumerable.Range(0, 3).All(i => Math.Abs(actual[offset + i] - want[i]) < 1e-10), at + ": global vector = [" + string.Join(",", actual.Skip(offset).Take(3)) + "], expected [" + string.Join(",", want) + "]");
            Check(c.Message.Contains(global ? "Global" : "Local"), at + ": component message follows effective frame");
            var model = ModelFor(draft);
            var assembled = model.Model.BeamElements.Values.Single().Assignments.Loads.Single();
            var inModel = type == "BeamDistributedLoadComponent" ? assembled.StartIntensity.GetGlobalForces() : ((PointLoad)assembled.Concentrated).GetGlobalForces();
            Check(Enumerable.Range(0, 3).All(i => Math.Abs(inModel[offset + i] - want[i]) < 1e-10), at + ": assembled GPC model preserves the expected vector");
            var (data, records) = GpcDraftArchive.Write(draft); var read = GpcDraftArchive.ReadBeam(data, records);
            var saved = read.Beam.Assignments.Loads.Single();
            var reread = type == "BeamDistributedLoadComponent" ? saved.StartIntensity.GetGlobalForces() : ((PointLoad)saved.Concentrated).GetGlobalForces();
            Check(Enumerable.Range(0, 3).All(i => Math.Abs(reread[offset + i] - want[i]) < 1e-10), at + ": GPC archive preserves the expected vector");
            if (nativeFolder != null && !wire)
            {
                string name = type.Replace("Component", "") + "-" + (global ? "Global" : "Local") + "-" + (axis + 1);
                string path = Path.Combine(nativeFolder, name + ".st7");
                GpcAnalysisSeam.Export(model, path);
                File.WriteAllText(Path.Combine(nativeFolder, name + "-expected.json"), System.Text.Json.JsonSerializer.Serialize(new { UnitSystem = "N-mm", AppliedMagnitude = 10, GlobalVector = want, Start = new[] { 100, 200, 300 }, End = new[] { 3100, 4200, 300 }, Station = type == "BeamDistributedLoadComponent" ? "0..1" : "0.4", Kind = type }));
                Check(File.Exists(path), "NATIVE export " + path);
            }
        }
    }
}
