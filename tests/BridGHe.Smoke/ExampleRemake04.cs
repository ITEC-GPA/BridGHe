#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// P2S (7/11, coordinator decision 2 on passo 66): the examples whose result components read GH models internalised by the 0.3.0 with results
/// (24 synthetic samples, 42 synthetic displacements, 43 synthetic transit, 47/48 the archived native results of 19 REV003) remade in 0.4.0
/// form: every internalised GH model with stored results gets the analysed GPC model of the same results (<see cref="GpcSyntheticResults"/>:
/// the legacy reader and the results reader, as the synthetic results of the tests) and is saved with it (GH_Model chunk BridGHeResults);
/// a model whose beams have no node at an end (the beam-36 extracts of 47/48) gets the two end nodes first, so that it converts. The
/// document is read and written as it is: every object and its instance GUID are kept, only the internalised data change.
/// <para><c>--remake-examples-04 write</c> writes the five files of the worktree (never the main checkout); without <c>write</c> it only
/// reports. <c>--remade-examples</c> (regression <c>remade-examples</c>) opens the five files and solves them (Build BridGHe Model and Super
/// Element Check of 47/48 pressed, their checks compared with the former path, <see cref="FemPhasesReference"/>).</para>
/// </summary>
internal static class ExampleRemake04
{
    internal static readonly string[] Files =
    {
        @"24-inviluppi-concomitanti\24-inviluppi-concomitanti.ghx", @"42-frecce-controfreccia\42-frecce-controfreccia.ghx", @"43-storie-fatica\43-storie-fatica.ghx",
        @"47-pioli-irrigidimenti-verifiche\47-integrazione-modello.ghx", @"48-report-word\48-report-word.ghx",
    };

    private static IEnumerable<IGH_Param> Params(GH_Document doc) => doc.Objects.OfType<IGH_Param>()
        .Concat(doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input));

    internal static int Run(bool write)
    {
        BridgeMergeTests.Initialize();
        if (write && Path.GetFullPath(ExamplePaths.Root).StartsWith(@"C:\Users\g.pacini\Documents\Repository\BridGHe\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Never writes into the main checkout.");
        foreach (var relative in Files)
        {
            string path = Path.Combine(ExamplePaths.Root, relative); long before = new FileInfo(path).Length;
            using var doc = GrasshopperBridgeExample.Read(path);
            int remade = 0, nodes = 0;
            foreach (var param in Params(doc))
            {
                var data = param.GetType().GetProperty("PersistentData")?.GetValue(param) as IGH_Structure;
                if (data == null || data.DataCount == 0) continue;
                for (int p = 0; p < data.PathCount; p++)
                {
                    var branch = data.get_Branch(p);
                    for (int i = 0; i < branch.Count; i++)
                    {
                        if (!(branch[i] is IGH_Goo goo) || goo.GetType().Name != "GH_Model") continue;
                        var fem = (ModelModel)goo.GetType().GetProperty("Value").GetValue(goo);
                        if (fem == null || !fem.Elements.Any(e => e.Results.Count != 0) || goo.GetType().GetProperty("GpcResults").GetValue(goo) != null) continue;
                        var model = WithEndNodes(fem, out int added); nodes += added;
                        var analysed = GpcSyntheticResults.Analysed(model);
                        var created = (IGH_Goo)Activator.CreateInstance(goo.GetType());
                        created.GetType().GetProperty("Value").SetValue(created, model);
                        created.GetType().GetMethod("AttachGpc").Invoke(created, new object[] { analysed });
                        branch[i] = created; remade++;
                        Console.WriteLine($"REMAKE {relative} [{param.NickName}]: {model.Nodes.Count} nodes ({added} added at beam ends), {model.Beams.Count} beams, {model.Plates.Count} plates; "
                            + $"{analysed.Datasets.Count} GPC datasets, {BridGHe.Common.Gpc.GpcResultCatalog.Of(analysed).Entries.Count} catalog entries");
                    }
                }
                if (remade > 0) param.ExpireSolution(false);
            }
            // 24: the model now has the beam end nodes (without results): the reaction and displacement envelopes select the support node A, as
            // their description asks when a model also contains free nodes.
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name is "BridgeBearingEnvelopeComponent" or "BridgeDisplacementEnvelopeComponent"))
            {
                var ids = (Grasshopper.Kernel.Parameters.Param_String)c.Params.Input[1];
                if (ids.SourceCount == 0 && ids.PersistentData.IsEmpty) { ids.SetPersistentData("A"); Console.WriteLine($"REMAKE {relative} [{c.NickName}]: Element IDs = A"); }
            }
            if (remade == 0) throw new Exception("REMAKE: no internalised model with results in " + relative);
            if (write)
            {
                GrasshopperBridgeExample.Write(doc, path);
                Console.WriteLine($"REMAKE WRITTEN {relative}: {remade} models, {nodes} end nodes; {before} -> {new FileInfo(path).Length} bytes");
            }
        }
        return 0;
    }

    /// <summary>The model with a node at every beam end that has none (a beam extract without its nodes does not convert); the same model when
    /// every end has one.</summary>
    internal static ModelModel WithEndNodes(ModelModel fem, out int added)
    {
        var copy = BridGHe.Common.Helpers.ModelArchive.Clone(fem);
        var missing = new List<Point3d>();
        bool Has(Point3d p) => copy.Nodes.Any(n => n.Position.DistanceTo(p) <= 1e-6) || missing.Any(m => m.DistanceTo(p) <= 1e-6);
        foreach (var b in copy.Beams) foreach (var end in new[] { b.PointFrom, b.PointTo }) if (!Has(end)) missing.Add(end);
        added = missing.Count;
        if (added == 0) return copy;
        int next = 1; var ids = copy.Nodes.Select(n => n.NodeId).ToHashSet(StringComparer.Ordinal);
        var created = new List<ElementModel>();
        foreach (var p in missing) { string id; do id = "N" + next++; while (!ids.Add(id)); created.Add(new NodeModel(id, p)); }
        copy.AddElements(created);
        return copy;
    }

    /// <summary>Regression remade-examples: the five files solve in 0.4.0 form without errors; 47/48 build the GPC bridge model and check on D3,
    /// compared with the former path.</summary>
    internal static void Replay()
    {
        BridgeMergeTests.Initialize();
        foreach (var relative in Files)
        {
            string path = Path.Combine(ExamplePaths.Root, relative);
            using var doc = GrasshopperBridgeExample.Read(path);
            var gooModels = Params(doc).SelectMany(p => (p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure)?.AllData(true) ?? Enumerable.Empty<IGH_Goo>())
                .Where(g => g.GetType().Name == "GH_Model").ToArray();
            if (gooModels.Length == 0 || gooModels.Any(g => g.GetType().GetProperty("GpcResults").GetValue(g) == null)) throw new Exception(relative + ": an internalised model without its GPC results");
            BridgeSegmentExcelTests.Solve(doc, true);
            foreach (var name in new[] { "BuildBridgeModel", "SuperElementCheckComponent" })
                foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == name)) { c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true); c.ExpireSolution(false); BridgeSegmentExcelTests.Solve(doc, true); }
            var errors = doc.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => o.NickName + ": " + m)).ToArray();
            if (errors.Length > 0) throw new Exception(relative + ": " + string.Join("; ", errors));
            var warnings = doc.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Where(m => m.Contains("Nessun risultato di un'analisi") || m.Contains("versione precedente")).Select(m => o.NickName + ": " + m)).ToArray();
            if (warnings.Length > 0) throw new Exception(relative + ": " + string.Join("; ", warnings));
            foreach (var check in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "SuperElementCheckComponent")) FemPhasesReference.Compare(check, relative);
            var (compared, steps) = CompareUtilities(doc, relative);
            Console.WriteLine($"REMADE EXAMPLE PASS {relative}: {gooModels.Length} internalised models with GPC results; no errors; {compared} result-utility outputs equal to the former FEM path"
                + (steps > 0 ? $" except {steps} step/step-type values (GPC results have step 0 and no step type)" : ""));
        }
    }

    private static T[] Out<T>(GH_Component c, int i) => c.Params.Output[i].VolatileData.AllData(true).Select(g => g.ScriptVariable()).OfType<T>().ToArray();
    private static T[] In<T>(GH_Component c, int i) => c.Params.Input[i].VolatileData.AllData(true).Select(g => g.ScriptVariable()).OfType<T>().ToArray();
    private static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

    /// <summary>Until R6.5: the outputs of the result utilities of a remade example (GPC results) against the former FEM path on the FEM value of
    /// the same internalised model (its stored results; the end nodes added by the remake have none). Every output equal bit for bit, except
    /// the step and the step type of results the former FEM samples stored with a moving step (counted).</summary>
    private static (int Compared, int Steps) CompareUtilities(GH_Document doc, string relative)
    {
        int compared = 0, steps = 0;
        void Same(string what, IEnumerable<string> gpc, IEnumerable<string> fem)
        {
            var a = gpc.ToArray(); var b = fem.ToArray();
            if (!a.SequenceEqual(b)) throw new Exception($"{relative} {what}: GPC [{string.Join(", ", a.Take(6))}] former [{string.Join(", ", b.Take(6))}] ({a.Length}/{b.Length})");
            compared += a.Length;
        }
        void Steps(IEnumerable<string> gpc, IEnumerable<string> fem) { var a = gpc.ToArray(); var b = fem.ToArray(); if (a.Length != b.Length) throw new Exception(relative + ": step rows differ"); steps += a.Zip(b).Count(p => p.First != p.Second); }
        foreach (var c in doc.Objects.OfType<GH_Component>())
        {
            var name = c.GetType().Name;
            if (name is not ("BridgeBeamEnvelopeComponent" or "BridgeBearingEnvelopeComponent" or "BridgeDisplacementEnvelopeComponent" or "BridgeResultCasesComponent" or "BridgeDeflectionCamberComponent" or "BridgeFatigueHistoryComponent")) continue;
            var fem = In<ModelModel>(c, name == "BridgeFatigueHistoryComponent" ? 1 : 0).SingleOrDefault();
            if (fem == null) continue;
            switch (name)
            {
                case "BridgeResultCasesComponent":
                {
                    var rows = fem.Elements.SelectMany(e => e.Results).Select(r => (Case: r.LoadCase?.Name, Step: r.StepNum, Type: r.StepType, Result: r.GetType().Name))
                        .GroupBy(r => (r.Case, r.Result)).OrderBy(g => g.Key.Case, StringComparer.Ordinal).ThenBy(g => g.Key.Result, StringComparer.Ordinal).ToArray();
                    Same("Result Cases cases", Out<string>(c, 0), rows.Select(g => g.Key.Case).Distinct());
                    Same("Result Cases rows", Out<string>(c, 1).Zip(Out<string>(c, 4), (a, b) => a + " " + b), rows.Select(g => g.Key.Case + " " + g.Key.Result));
                    Same("Result Cases counts", Out<int>(c, 5).Select(n => n.ToString()), rows.Select(g => g.Count().ToString()));
                    Steps(Out<double>(c, 2).Select(B).Concat(Out<string>(c, 3)), rows.Select(g => B(g.First().Step)).Concat(rows.Select(g => g.First().Type ?? "")));
                    break;
                }
                case "BridgeDeflectionCamberComponent":
                {
                    var r = BridGHe.Common.Diagnostics.BridgeDeflectionCamber.Evaluate(fem, ((BridGHe.Common.Geometry.SimpleBridgeGeometry)In<object>(c, 1).Single()), In<int>(c, 2).Single(),
                        In<string>(c, 3).Single(), In<double>(c, 4).Single(), In<string>(c, 5).SingleOrDefault() ?? "", In<Vector3d>(c, 6).Single(), In<double>(c, 7).Single(), In<bool>(c, 8).Single(), In<double>(c, 9).Single());
                    Same("Deflection stations", Out<double>(c, 0).Select(B), r.Stations.Select(B)); Same("Deflection absolute", Out<double>(c, 1).Select(B), r.Absolute.Select(B));
                    Same("Deflection relative", Out<double>(c, 2).Select(B), r.Relative.Select(B)); Same("Deflection camber", Out<double>(c, 3).Select(B), r.Camber.Select(B));
                    break;
                }
                case "BridgeFatigueHistoryComponent":
                {
                    var s = BridGHe.Common.Diagnostics.BridgeFatigueHistory.Extract(fem, In<string>(c, 2).Single(), In<double>(c, 3).Single(), In<string>(c, 4), In<int>(c, 5).Single(), In<double>(c, 6));
                    Same("Fatigue values", Out<double>(c, 0).Select(B), s.Values.Select(B)); compared += Out<string>(c, 1).Length;
                    break;
                }
                default:
                {
                    int kind = name == "BridgeBeamEnvelopeComponent" ? 0 : name == "BridgeBearingEnvelopeComponent" ? 1 : 2;
                    var rows = BridGHe.Common.Diagnostics.BridgeResultEnvelope.Evaluate(fem, kind, In<string>(c, 1), In<string>(c, 2), In<int>(c, 3).Single(), In<int>(c, 4).Single());
                    Same("Envelope elements", Out<string>(c, 0), rows.Select(r => r.Element)); Same("Envelope stations", Out<double>(c, 1).Select(B), rows.Select(r => B(r.Station)));
                    Same("Envelope values", Out<double>(c, 2).Select(B), rows.Select(r => B(r.Value))); Same("Envelope cases", Out<string>(c, 3), rows.Select(r => r.Case));
                    Same("Envelope components", Out<double>(c, 6).Select(B), rows.SelectMany(r => r.Components.Select(B))); Same("Envelope coverage", Out<string>(c, 8), rows.Select(r => r.Coverage));
                    Steps(Out<double>(c, 4).Select(B).Concat(Out<string>(c, 5)), rows.Select(r => B(r.Step)).Concat(rows.Select(r => r.StepType ?? "")));
                    break;
                }
            }
        }
        return (compared, steps);
    }
}
#endif
