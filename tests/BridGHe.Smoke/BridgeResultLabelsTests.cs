using System;
using System.Linq;
using BridGHe.Common.Checks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static class BridgeResultLabelsTests
{
    internal static void Run()
    {
        var plugin = BridgeMergeTests.Initialize();
        var type = plugin.GetTypes().Single(t => t.Name == "BridgeResultLabelsComponent");
        using var doc = new GH_Document { Enabled = true };
        var component = (GH_Component)Activator.CreateInstance(type);
        component.CreateAttributes(); doc.AddObject(component, false);
        var input = (Param_GenericObject)component.Params.Input[0];
        var format = (Param_Integer)component.Params.Input[1];
        var plain = BridgeCheckSummaryTests.SampleResult();
        var a = plain.WithSource(new BridgeResultSource("36", 1250, "C1", "G2"));
        var b = plain.WithSource(new BridgeResultSource("37", 0, "C2", "G3"));
        var first = new GH_Path(2, 4); var second = new GH_Path(7, 0);
        input.PersistentData.Append(new GH_ObjectWrapper(a), first);
        input.PersistentData.Append(new GH_ObjectWrapper(b), first);
        input.PersistentData.Append(new GH_ObjectWrapper(plain), second);
        Solve(); Verify(0);

        var list = new GH_ValueList(); list.CreateAttributes(); doc.AddObject(list, false);
        format.AddSource(list); Solve(); Verify(0);
        Require(list.ListMode == GH_ValueListMode.DropDown && list.ListItems.Select(v => v.Name).SequenceEqual(new[] { "Completa", "Trave e stazione", "Combinazione", "Fase" }),
            "Connecting a fresh Value List must populate exactly four formats.");
        var items = list.ListItems.ToArray();
        for (int mode = 0; mode < 4; mode++)
        {
            for (int i = 0; i < list.ListItems.Count; i++) list.ListItems[i].Selected = i == mode;
            list.ExpireSolution(false); Solve(); Verify(mode);
            Solve(); Verify(mode);
            Require(list.ListItems.SequenceEqual(items), "Recomputation rebuilt the Value List or lost selection.");
        }
        format.RemoveAllSources(); format.AddSource(list); Solve(); Verify(3);

        // Save the selector and its chosen format; raw test results are supplied again after reopening.
        input.PersistentData.Clear();
        var archive = new GH_IO.Serialization.GH_Archive();
        Require(archive.AppendObject(doc, "Definition"), "Could not save the label selector.");
        using var reopened = new GH_Document { Enabled = true };
        Require(archive.ExtractObject(reopened, "Definition"), "Could not reopen the label selector.");
        var restored = reopened.Objects.OfType<GH_Component>().Single();
        var restoredList = reopened.Objects.OfType<GH_ValueList>().Single();
        ((Param_GenericObject)restored.Params.Input[0]).PersistentData.Append(new GH_ObjectWrapper(a), first);
        reopened.NewSolution(false, GH_SolutionMode.Silent);
        Require(restoredList.SelectedItems.Single().Name == "Fase" && restored.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable().Equals(a.Stage.Name),
            "Save/reopen lost the selected format or failed to solve.");

        input.PersistentData.Append(new GH_ObjectWrapper(a)); format.RemoveAllSources();
        format.PersistentData.Clear(); format.SetPersistentData(99); Solve(true);
        format.PersistentData.Clear(); format.SetPersistentData(0, 1); Solve(true);
        Console.WriteLine("RESULT LABELS PASS: four formats, automatic Value List, stable selection, tree branches, missing metadata, invalid formats and save/reopen.");

        void Solve(bool error = false)
        {
            input.ExpireSolution(false); format.ExpireSolution(false);
            GH_Document.EnableSolutions = true; doc.NewSolution(false, GH_SolutionMode.Silent);
            var errors = component.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            Require(error ? errors.Count > 0 && component.Params.Output[0].VolatileDataCount == 0 : errors.Count == 0, "Unexpected formatter errors: " + string.Join("; ", errors));
        }
        void Verify(int mode)
        {
            Require(component.Params.Output[0].VolatileData.Paths.SequenceEqual(new[] { first, second }), "Label branches do not match results.");
            string[] expected = mode switch
            {
                0 => new[] { a.Source.Label, b.Source.Label, $"Result {second}[0] / {plain.Stage.Name}" },
                1 => new[] { "G2 / Beam 36 / s=1250 mm", "G3 / Beam 37 / s=0 mm", $"Result {second}[0] / beam and station unavailable" },
                2 => new[] { "C1", "C2", $"Result {second}[0] / combination unavailable" },
                _ => new[] { a.Stage.Name, b.Stage.Name, plain.Stage.Name }
            };
            Require(component.Params.Output[0].VolatileData.AllData(true).Select(g => (string)g.ScriptVariable()).SequenceEqual(expected), "Incorrect format or label/result count.");
        }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("RESULT LABELS: " + message); }
}
