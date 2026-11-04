#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// F6 C4 (native): Node, Beam and Plate Results read the analysed GPC element that Decompose Model links, and their outputs equal bit for
/// bit those of the FEM results the analysis wrote (the former component logic). For every element of an analysed GH model: Decompose
/// Model, then each Results component on two data built from the decomposed element, one with only the GPC link (no FEM result) and one
/// with only the FEM results (no link); every output (names, numbers, vectors) is compared bit for bit, branch by branch.
/// </summary>
internal static class ResultsComponentsCheck
{
    internal sealed record Outcome(int Nodes, int Beams, int Plates, long Values);

    /// <summary>The GH model an analysis component gives: the FEM results and the analysed GPC model attached.</summary>
    internal static object AnalysedGoo(ModelModel result, GPC.Model.Models.Model analysed)
    {
        var type = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly).GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
        var goo = type.GetMethod("FromModel", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { result, null });
        type.GetMethod("AttachGpc").Invoke(goo, new object[] { analysed ?? throw new ArgumentNullException(nameof(analysed)) });
        return goo;
    }

    internal static Outcome Verify(object analysedModelGoo, string label)
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("RESULTS GPC FAIL (" + label + "): " + what); }
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        Require((bool)analysedModelGoo.GetType().GetProperty("HasAnalysedGpc").GetValue(analysedModelGoo), "the GH model has its analysed GPC model");
        using var document = new GH_Document { Enabled = true };
        GH_Document.EnableSolutions = true;
        GH_Component Make(string name)
        {
            var c = (GH_Component)Activator.CreateInstance(assembly.GetTypes().Single(t => t.Name == name));
            c.CreateAttributes(); document.AddObject(c, false); return c;
        }
        var decompose = Make("DecomposeModelComponent");
        ((Param_GenericObject)decompose.Params.Input[0]).PersistentData.Append((IGH_Goo)analysedModelGoo);
        document.NewSolution(false, GH_SolutionMode.Silent);
        Require(decompose.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0 && decompose.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Count == 0,
            "Decompose Model without messages: " + string.Join("; ", decompose.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(decompose.RuntimeMessages(GH_RuntimeMessageLevel.Remark))));
        var elements = Enumerable.Range(0, 3).Select(k => decompose.Params.Output[k].VolatileData.AllData(true).ToArray()).ToArray();
        long values = 0;
        string[] components = { "NodeResultsComponent", "BeamResultsComponent", "PlateResultsComponent" };
        string[] types = { "GH_NodeResults", "GH_BeamResults", "GH_PlateResults" };
        var gpcSide = new GH_Component[3]; var femSide = new GH_Component[3];
        // A component without input warns that it collected no data: only the element types the model has are checked.
        var present = Enumerable.Range(0, 3).Where(k => elements[k].Length > 0).ToArray();
        foreach (int k in present)
        {
            gpcSide[k] = Make(components[k]); femSide[k] = Make(components[k]);
            var resultType = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem." + types[k], true);
            var gpcInput = (Param_GenericObject)gpcSide[k].Params.Input[0]; var femInput = (Param_GenericObject)femSide[k].Params.Input[0];
            for (int i = 0; i < elements[k].Length; i++)
            {
                var element = elements[k][i]; var t = element.GetType();
                object link = t.GetProperty("AnalysedGpc").GetValue(element), model = t.GetProperty("AnalysedGpcModel").GetValue(element);
                Require(link != null && model != null, types[k] + " " + i + ": linked to its analysed GPC element");
                var femResults = ((ElementModel)t.GetProperty("Value").GetValue(element)).Results;
                var gpc = (IGH_Goo)Activator.CreateInstance(resultType); var fem = (IGH_Goo)Activator.CreateInstance(resultType);
                resultType.GetProperty("Value").SetValue(gpc, new List<ResultModel>());
                resultType.GetProperty("AnalysedGpc").SetValue(gpc, link); resultType.GetProperty("AnalysedGpcModel").SetValue(gpc, model);
                resultType.GetProperty("Value").SetValue(fem, new List<ResultModel>(femResults));
                gpcInput.PersistentData.Append(gpc, new GH_Path(i)); femInput.PersistentData.Append(fem, new GH_Path(i));
            }
            gpcInput.ExpireSolution(false); femInput.ExpireSolution(false);
        }
        document.NewSolution(false, GH_SolutionMode.Silent);
        foreach (int k in present)
        {
            foreach (var c in new[] { gpcSide[k], femSide[k] })
                Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0 && c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count == 0,
                    components[k] + " without messages: " + string.Join("; ", c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(c.RuntimeMessages(GH_RuntimeMessageLevel.Warning))));
            for (int o = 0; o < gpcSide[k].Params.Output.Count; o++)
            {
                var a = Branches(gpcSide[k].Params.Output[o].VolatileData); var b = Branches(femSide[k].Params.Output[o].VolatileData);
                string output = components[k] + " / " + gpcSide[k].Params.Output[o].Name;
                for (int i = 0; i < elements[k].Length; i++)
                {
                    var x = a.TryGetValue(i, out var xa) ? xa : new List<string>(); var y = b.TryGetValue(i, out var yb) ? yb : new List<string>();
                    Require(x.Count == y.Count, $"{output}, element {i}: {x.Count} values from GPC, {y.Count} from FEM");
                    for (int j = 0; j < x.Count; j++) Require(x[j] == y[j], $"{output}, element {i}, value {j}: GPC {x[j]} vs FEM {y[j]}");
                    values += x.Count;
                }
                Require(a.Keys.All(i => i < elements[k].Length) && b.Keys.All(i => i < elements[k].Length), output + ": no branch beyond the elements");
            }
        }
        // Each element type with elements must have produced values (an empty comparison proves nothing).
        foreach (int k in present)
                Require(Enumerable.Range(0, femSide[k].Params.Output.Count).Sum(o => femSide[k].Params.Output[o].VolatileData.DataCount) > 0, components[k] + ": FEM results present");
        // C6: the generated moving cases from the GPC steps and the model audit on the analysed GPC model, equal to the FEM ones.
        var femModel = (ModelModel)analysedModelGoo.GetType().GetProperty("Value").GetValue(analysedModelGoo);
        var gpcModel = (GPC.Model.Models.Model)analysedModelGoo.GetType().GetProperty("Gpc").GetValue(analysedModelGoo);
        var catalog = BridGHe.Common.Gpc.GpcResultCatalog.Of(gpcModel);
        Require(catalog != null, "the analysed GPC model has its results catalog");
        var fromGpc = catalog.GeneratedMovingCases().Select(c => c.Number + "|" + c.Name).ToArray();
        var fromFem = femModel.GeneratedMovingCases.OrderBy(c => c.Key).Select(c => c.Key + "|" + c.Value).ToArray();
        Require(fromGpc.SequenceEqual(fromFem, StringComparer.Ordinal), $"generated moving cases: {fromGpc.Length} from the GPC steps, {fromFem.Length} FEM");
        string Issues(BridGHe.Common.Diagnostics.ModelIssue[] issues) => string.Join("\n", issues.Select(i => i.Severity + "|" + i.Code + "|" + i.Element + "|" + i.Description));
        string auditFem = Issues(BridGHe.Common.Diagnostics.BridgeModelAudit.Inspect(femModel, 1e-6)), auditGpc = Issues(BridGHe.Common.Diagnostics.BridgeModelAudit.Inspect(femModel, 1e-6, analysed: gpcModel));
        Require(auditFem == auditGpc, "model audit on the analysed GPC model equal to the FEM one:\nFEM\n" + auditFem + "\nGPC\n" + auditGpc);
        long consumers = ConsumersCheck.Verify(femModel, gpcModel, label);
        var outcome = new Outcome(elements[0].Length, elements[1].Length, elements[2].Length, values);
        Console.WriteLine($"RESULTS GPC PASS ({label}): {outcome.Nodes} nodes, {outcome.Beams} beams, {outcome.Plates} plates, {outcome.Values} output values identical bit for bit (GPC element vs FEM results); " +
            $"{fromGpc.Length} generated moving cases and the model audit equal; result consumers: {consumers} values equal.");
        return outcome;
    }

    /// <summary>Output values by element (first path index), each as an exact token: text, or the bits of every number.</summary>
    private static Dictionary<int, List<string>> Branches(IGH_Structure data)
    {
        var result = new Dictionary<int, List<string>>();
        for (int b = 0; b < data.PathCount; b++)
        {
            var path = data.get_Path(b); int element = path.Indices[0];
            if (!result.TryGetValue(element, out var list)) result.Add(element, list = new List<string>());
            foreach (var item in data.get_Branch(b)) list.Add(Token(item));
        }
        return result;
    }

    private static string Token(object item)
    {
        static string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
        return item switch
        {
            GH_String s => "s:" + s.Value,
            GH_Number n => "n:" + Bits(n.Value),
            GH_Vector v => "v:" + Bits(v.Value.X) + "," + Bits(v.Value.Y) + "," + Bits(v.Value.Z),
            null => "null",
            _ => item.GetType().Name + ":" + item,
        };
    }
}
#endif
