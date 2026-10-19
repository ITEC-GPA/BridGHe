using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static class BridgeMergeTests
{
    internal static Assembly Initialize()
    {
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var field = typeof(Grasshopper.Instances).GetField("m_comServer", BindingFlags.NonPublic | BindingFlags.Static);
        if (field.GetValue(null) == null)
        {
            var server = new GH_ComponentServer(); field.SetValue(null, server);
            var parse = typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Assembly), typeof(string) }, null);
            foreach (var a in new[] { typeof(GH_Document).Assembly, assembly }) parse.Invoke(server, new object[] { a, a.Location });
        }
        return assembly;
    }
    internal static void Run()
    {
        var assembly = Initialize(); using var doc = new GH_Document();
        GH_Component Add(string name)
        {
            var c = (GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks." + name, true));
            c.CreateAttributes(); doc.AddObject(c, false); return c;
        }
        IGH_Goo Model(ModelModel model)
        {
            var type = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true); var goo = (IGH_Goo)Activator.CreateInstance(type);
            type.GetProperty("Value").SetValue(goo, model); return goo;
        }
        void Generic(GH_Component c, int i, IGH_Goo value) => ((Param_GenericObject)c.Params.Input[i]).PersistentData.Append(value);
        void Text(GH_Component c, int i, string value) { var p = (Param_String)c.Params.Input[i]; p.PersistentData.Clear(); p.SetPersistentData(value); }
        void Int(GH_Component c, int i, int value) { var p = (Param_Integer)c.Params.Input[i]; p.PersistentData.Clear(); p.SetPersistentData(value); }
        void Wire(GH_Component from, GH_Component to, int port) => to.Params.Input[port].AddSource(from.Params.Output[0]);
        var steel = BridgeWorkflowTests.Source("Steel", 100); var shrink = BridgeWorkflowTests.Source("Shrinkage", 20);
        var g = Add("BridgeActionComponent"); Text(g, 0, "G1"); Generic(g, 1, Model(steel)); Generic(g, 2, new GH_String("G"));
        var r = Add("BridgeShrinkageActionComponent"); Text(r, 0, "RITIRO"); Generic(r, 1, Model(shrink)); Generic(r, 2, new GH_String("G"));
        ((Param_Number)r.Params.Input[3]).SetPersistentData(-200e-6);
        var phase1 = Add("BridgeConstructionPhaseComponent"); Int(phase1, 0, 10); Text(phase1, 1, "Getto"); Int(phase1, 2, 0); Wire(g, phase1, 3);
        var phase2 = Add("BridgeConstructionPhaseComponent"); Int(phase2, 0, 30); Text(phase2, 1, "Ritiro"); Wire(r, phase2, 3);
        var build = Add("BuildBridgeModel"); Generic(build, 0, Model(steel)); Wire(phase2, build, 1); Wire(phase1, build, 1);
        var table = (Param_GenericObject)build.Params.Input[2];
        foreach (var s in new[] { "", "G1", "RITIRO" }) table.PersistentData.Append(new GH_String(s), new GH_Path(0));
        foreach (var s in new[] { "SLS", "1", "0.7" }) table.PersistentData.Append(new GH_String(s), new GH_Path(1));
        void Solve(GH_Document document, GH_Component component)
        {
            component.GetType().GetField("_run", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(component, true);
            component.ExpireSolution(false); GH_Document.EnableSolutions = true; document.Enabled = true; document.NewSolution(false, GH_SolutionMode.Silent);
            var errors = document.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error)).ToArray();
            BridgeWorkflowTests.Require(errors.Length == 0, string.Join("; ", errors));
        }
        Solve(doc, build);
        var merged = (ModelModel)build.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        BridgeWorkflowTests.Require(merged.LoadCases.Count == 3 && merged.BridgeWorkflow.Phases[0].Order == 10, "GH workflow incorrectly assembled.");
        var list = new GH_ValueList { ListMode = GH_ValueListMode.DropDown }; list.CreateAttributes(); list.ListItems.Clear();
        list.ListItems.Add(new GH_ValueListItem("G", "\"G\"") { Selected = true }); list.ListItems.Add(new GH_ValueListItem("Obsolete strain case", "\"Obsolete\"")); doc.AddObject(list, false);
        g.Params.Input[2].AddSource(list); g.ExpireSolution(false); Solve(doc, build); Solve(doc, build);
        BridgeWorkflowTests.Require(list.ListItems.Count == 1 && list.SelectedItems.Single().Name == "G", "FEM case selector lost or mixed its selection.");
        Int(phase1, 0, 5); phase1.ExpireSolution(false); Solve(doc, build);
        var renamed = (ModelModel)build.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        BridgeWorkflowTests.Require(renamed.LoadCases.Select(c => c.Name).SequenceEqual(merged.LoadCases.Select(c => c.Name)), "GH renumbering changed combination keys.");
        string path = Path.GetFullPath("artifacts/phase-component-roundtrip.ghx"); GrasshopperBridgeExample.Write(doc, path);
        using var restored = GrasshopperBridgeExample.Read(path);
        var restoredBuild = restored.Objects.OfType<GH_Component>().Single(c => c.ComponentGuid == build.ComponentGuid); Solve(restored, restoredBuild);
        var copy = (ModelModel)restoredBuild.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        BridgeWorkflowTests.Require(copy.LoadCombinations.Single().Values.Count == 3 && copy.BridgeWorkflow.Phases[1].Id == merged.BridgeWorkflow.Phases[1].Id, "GH save/reopen changed phase identities or coefficients.");
        Console.WriteLine("PASS GH phases: real components, case selector, numbered assembly and GHX roundtrip");
    }
}
