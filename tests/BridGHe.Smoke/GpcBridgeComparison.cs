using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Converter;
using GPC.Geometry;
using GPC.Model.Analysis;
using GPC.Model.Core.Units;
using GPC.Model.Models;
using GPC.Model.Results.Locations;
using GPC.Model.Results.State;
using GPC.Model.Structure.Assignments;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using D3 = GPC.Checkers.CompositeBridge;

/// <summary>
/// P2CB (13/11/2026, rule U3): the regression <c>gpc-bridge-model</c> (below until P2CB) is retired by name with the FEM models it compared
/// (<see cref="P2rRetiredP2cb"/>); the GPC helpers of the other checks stay (<see cref="RequireGpcLinks"/>, <see cref="PartsDiffer"/>).
/// Before: F6 chain C: the GPC bridge model of Build BridGHe Model (<see cref="GpcBridgeBuilder"/>, from the GPC models only) against the former
/// seam on the merged FEM model (FemmBridgeSeam.Build, F6 C5 to step 24; its code was kept as the TESTS ONLY reference, removed with the
/// FEM models). The seam converted the merged FEM model with the identity of its own archive; here it converts it with the identity of the
/// input model of the first reference, the identity the GPC bridge model keeps (a GPC copy). With that identity everything must be equal
/// bit for bit: the merged samples (every bit, sign of zero included), the summary and the warnings of the FEM merge, the analysis
/// fingerprint and the complete GPC archive of the model (structure, dataset, source records) and the D3 recipe (fingerprint).
/// </summary>
internal static class GpcBridgeComparison
{



    private static string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
    private static readonly System.Text.RegularExpressions.Regex RandomGuid = new System.Text.RegularExpressions.Regex("[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static string Fp(params object[] values) => GPC.Model.Core.ModelValues.Fingerprint(values);


    /// <summary>Native (99, 50), finding 1 of P2T: every action with a FEM case that reaches a component (Build BridGHe Model, Bridge Detail
    /// Stations, ...) on a wire, alone or in a phase, carries its analysed GPC source and the GPC model of its source (the same model): no
    /// consumer falls back to the FEM results silently.</summary>
    internal static void RequireGpcLinks(GH_Document doc, string label)
    {
        int actions = 0, components = 0;
        foreach (var c in doc.Objects.OfType<GH_Component>())
        {
            bool any = false;
            foreach (var p in c.Params.Input)
                foreach (var goo in p.VolatileData.AllData(true).Where(g => g?.GetType().Name == "BridgeWorkflowGoo"))
                {
                    var value = goo.ScriptVariable();
                    var list = value is BridgePhaseDefinition phase ? phase.Actions : value is BridgeActionDefinition a ? new[] { a } : Array.Empty<BridgeActionDefinition>();
                    foreach (var action in list.Where(x => !string.IsNullOrWhiteSpace(x.Case)))
                    {
                        if (action.GpcSource == null || action.GpcModel == null || !ReferenceEquals(action.GpcSource, action.GpcModel))
                            throw new Exception($"GPC BRIDGE FAIL ({label}): {c.Name} '{c.NickName}', input {p.Name}: action {action.Key} without its GPC source/model");
                        actions++; any = true;
                    }
                }
            if (any) components++;
        }
        if (actions == 0) throw new Exception($"GPC BRIDGE FAIL ({label}): no action with a FEM case on the wires");
        Console.WriteLine($"GPC LINKS PASS ({label}): {actions} actions with a FEM case on the inputs of {components} components, all with their GPC source and model.");
    }



    /// <summary>The parts of two models whose content fingerprints differ (cases, combinations, model loads, freedom cases, stages, groups,
    /// members, source records, constraints, dataset metadata, every element).</summary>
    internal static List<string> PartsDiffer(Model m, Model e)
    {
        var lines = new List<string>();
            void Same(string what, object x, object y) { string fx = Fp(x), fy = Fp(y); if (fx != fy) lines.Add(what); }
            Same("name/guid", new object[] { m.Name, m.Guid }, new object[] { e.Name, e.Guid });
            Same("load cases [" + string.Join(", ", m.LoadCases.Keys) + "] / [" + string.Join(", ", e.LoadCases.Keys) + "]", m.LoadCases.Values.ToArray(), e.LoadCases.Values.ToArray());
            Same("combinations", m.Combinations.Values.ToArray(), e.Combinations.Values.ToArray());
            Same("model loads", m.ModelLoads.Values.ToArray(), e.ModelLoads.Values.ToArray());
            Same("freedom cases/stages", new object[] { m.FreedomCases.Values.ToArray(), m.Stages.Values.ToArray() }, new object[] { e.FreedomCases.Values.ToArray(), e.Stages.Values.ToArray() });
            Same("groups", m.Groups.Values.Select(g => new object[] { g.Guid, g.Name, g.Parent?.Guid }).ToArray(), e.Groups.Values.Select(g => new object[] { g.Guid, g.Name, g.Parent?.Guid }).ToArray());
            Same("physical members", m.PhysicalMembers.Values.ToArray(), e.PhysicalMembers.Values.ToArray());
            Same("preserved", m.PreservedSourceData.ToArray(), e.PreservedSourceData.ToArray());
            Same("constraints", m.Costrains.Values.ToArray(), e.Costrains.Values.ToArray());
            Same("datasets", new object[] { m.Datasets.Keys.ToArray(), m.AnalysisSource }, new object[] { e.Datasets.Keys.ToArray(), e.AnalysisSource });
            if (m.NodesElements.Count != e.NodesElements.Count || m.BeamElements.Count != e.BeamElements.Count || m.AreaElements.Count != e.AreaElements.Count) lines.Add("element counts");
            else
            {
                foreach (var n in m.NodesElements.Values) { var o = e.NodesElements[n.Id]; Same("node " + n.Id, new object[] { n.Guid, n.Position, n.Loads, n.Attributes, n.Assignments.Physical, n.Assignments.Restrains.Select(r => r.Restrain.Restrains).ToArray() }, new object[] { o.Guid, o.Position, o.Loads, o.Attributes, o.Assignments.Physical, o.Assignments.Restrains.Select(r => r.Restrain.Restrains).ToArray() }); }
                foreach (var b in m.BeamElements.Values) { var o = e.BeamElements[b.Id]; Same("beam " + b.Id, new object[] { b.Guid, b.Source, b.StartPoint, b.EndPoint, b.Loads, b.Attributes, b.Assignments.Physical, b.Assignments.Loads.ToArray(), b.Assignments.SectionAxes, b.BeamProperty }, new object[] { o.Guid, o.Source, o.StartPoint, o.EndPoint, o.Loads, o.Attributes, o.Assignments.Physical, o.Assignments.Loads.ToArray(), o.Assignments.SectionAxes, o.BeamProperty }); }
                foreach (var p in m.AreaElements.Values) { var o = e.AreaElements[p.Id]; Same("plate " + p.Id, new object[] { p.Guid, p.Loads, p.Attributes, p.Assignments.Physical }, new object[] { o.Guid, o.Loads, o.Attributes, o.Assignments.Physical }); }
            }
            return lines;
    }
}
