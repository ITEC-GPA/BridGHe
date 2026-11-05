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
/// Model, then each Results component on a datum with only the GPC link (no FEM result); every output (names, numbers, vectors) is compared
/// bit for bit, branch by branch, with the former FEM branch of the component on the FEM results of the element (F6 R7: the components have
/// no FEM fallback any more; <see cref="FemOutputs"/> keeps that branch in the test until the FeMM removal), and a datum with only FEM
/// results gives no result and the warning.
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
        var gpcSide = new GH_Component[3];
        // F6 R7: the components read only the GPC link; the FEM reference is the former component branch on the FEM results the analysis
        // wrote (FemOutputs, removed with the FeMM models), as the tokens of the goo the output parameter holds.
        var expected = new Dictionary<int, List<string>[]>[3];
        // A component without input warns that it collected no data: only the element types the model has are checked.
        var present = Enumerable.Range(0, 3).Where(k => elements[k].Length > 0).ToArray();
        foreach (int k in present)
        {
            gpcSide[k] = Make(components[k]); expected[k] = new Dictionary<int, List<string>[]>();
            var resultType = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem." + types[k], true);
            var gpcInput = (Param_GenericObject)gpcSide[k].Params.Input[0];
            for (int i = 0; i < elements[k].Length; i++)
            {
                var element = elements[k][i]; var t = element.GetType();
                object link = t.GetProperty("AnalysedGpc").GetValue(element), model = t.GetProperty("AnalysedGpcModel").GetValue(element);
                Require(link != null && model != null, types[k] + " " + i + ": linked to its analysed GPC element");
                var femResults = ((ElementModel)t.GetProperty("Value").GetValue(element)).Results;
                var gpc = (IGH_Goo)Activator.CreateInstance(resultType);
                resultType.GetProperty("Value").SetValue(gpc, new List<ResultModel>());
                resultType.GetProperty("AnalysedGpc").SetValue(gpc, link); resultType.GetProperty("AnalysedGpcModel").SetValue(gpc, model);
                gpcInput.PersistentData.Append(gpc, new GH_Path(i));
                expected[k][i] = FemOutputs(k, femResults);
            }
            gpcInput.ExpireSolution(false);
        }
        document.NewSolution(false, GH_SolutionMode.Silent);
        foreach (int k in present)
        {
            var c = gpcSide[k];
            Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0 && c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count == 0,
                components[k] + " without messages: " + string.Join("; ", c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(c.RuntimeMessages(GH_RuntimeMessageLevel.Warning))));
            long femValues = 0;
            for (int o = 0; o < c.Params.Output.Count; o++)
            {
                var a = Branches(c.Params.Output[o].VolatileData);
                string output = components[k] + " / " + c.Params.Output[o].Name;
                for (int i = 0; i < elements[k].Length; i++)
                {
                    var x = a.TryGetValue(i, out var xa) ? xa : new List<string>(); var y = expected[k][i][o];
                    Require(x.Count == y.Count, $"{output}, element {i}: {x.Count} values from GPC, {y.Count} from FEM");
                    for (int j = 0; j < x.Count; j++) Require(x[j] == y[j], $"{output}, element {i}, value {j}: GPC {x[j]} vs FEM {y[j]}");
                    values += x.Count; femValues += y.Count;
                }
                Require(a.Keys.All(i => i < elements[k].Length), output + ": no branch beyond the elements");
            }
            // Each element type with elements must have produced values (an empty comparison proves nothing).
            Require(femValues > 0, components[k] + ": FEM results present");
        }
        // R7: an element without an analysed GPC model gives no FEM result any more, only the warning.
        if (present.Length != 0)
        {
            int k = present[0];
            var probe = Make(components[k]); var resultType = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem." + types[k], true);
            var element = elements[k][0]; var femOnly = (IGH_Goo)Activator.CreateInstance(resultType);
            resultType.GetProperty("Value").SetValue(femOnly, new List<ResultModel>(((ElementModel)element.GetType().GetProperty("Value").GetValue(element)).Results));
            ((Param_GenericObject)probe.Params.Input[0]).PersistentData.Append(femOnly);
            document.NewSolution(false, GH_SolutionMode.Silent);
            Require(probe.Params.Output.All(p => p.VolatileData.DataCount == 0) && probe.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains(BridGHe.Common.Gpc.GpcResultCatalog.NoAnalysedResultsMessage),
                components[k] + ": FEM results without the GPC link give no result and the warning");
        }
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

    /// <summary>FEM path (removed with the FeMM models): the outputs of the former Node (k = 0), Beam (1) and Plate (2) Results branch on
    /// the FEM results of an element, in output order, as the tokens of the goo the output parameters hold (text, number, vector; a 2D
    /// vector as the 3D vector of the parameter, z = 0).</summary>
    private static List<string>[] FemOutputs(int k, IEnumerable<ResultModel> results)
    {
        string S(string v) => Token(new GH_String(v)); string N(double v) => Token(new GH_Number(v));
        string V(Rhino.Geometry.Vector3d v) => Token(new GH_Vector(v)); string V2(Rhino.Geometry.Vector2d v) => Token(new GH_Vector(new Rhino.Geometry.Vector3d(v.X, v.Y, 0)));
        var o = Enumerable.Range(0, k == 0 ? 8 : k == 1 ? 7 : 15).Select(_ => new List<string>()).ToArray();
        foreach (var result in results)
        {
            if (k == 0 && result is NodeDisplResultModel d) { o[0].Add(S(result.LoadCase.Name)); o[1].Add(N(result.StepNum)); o[2].Add(V(d.Displacement)); o[3].Add(V(d.Rotation)); }
            else if (k == 0 && result is NodeReactResultModel r) { o[4].Add(S(result.LoadCase.Name)); o[5].Add(N(result.StepNum)); o[6].Add(V(r.Force)); o[7].Add(V(r.Moment)); }
            else if (k == 1 && result is BeamForceResultModel b)
            { o[0].Add(S(b.LoadCase.Name)); o[1].Add(N(b.Station)); o[2].Add(N(b.StepNum)); o[3].Add(V2(b.ShearForce)); o[4].Add(V2(b.BendingMoment)); o[5].Add(N(b.AxialForce)); o[6].Add(N(b.Torque)); }
            else if (k == 2)
            {
                o[0].Add(S(result.LoadCase.Name)); o[2].Add(N(result.StepNum));
                if (result is PlateForceResultModel p)
                {
                    o[1].Add(S(p.NodeId)); o[3].Add(V(p.Force)); o[4].Add(N(p.ForceMax)); o[5].Add(N(p.ForceMin)); o[6].Add(N(p.ForceAngle)); o[7].Add(N(p.ForceVonMises));
                    o[8].Add(V(p.Moment)); o[9].Add(N(p.MomentMax)); o[10].Add(N(p.MomentMin)); o[11].Add(N(p.MomentAngle));
                    o[12].Add(V2(p.TransverseShearForce)); o[13].Add(N(p.TransverseShearForceMax)); o[14].Add(N(p.TransverseShearForceAng));
                }
            }
        }
        return o;
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
