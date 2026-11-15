using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Utilities;

/// <summary>Subset and reindex the checker's beam/station tree; never concatenate separate girders on a plot axis.</summary>
public sealed class BridgeGirderResultsComponent : BridgeUtilityComponent
{
    public BridgeGirderResultsComponent() : base("Bridge Girder Results", "GirderResults",
        "Select one longitudinal girder from a whole-bridge check. Reindexes beam/station paths for the plot components and retains combination order.", CategoryNameConstants.SUBCATEGORY_RESULTS) { }
    public override Guid ComponentGuid => new Guid("c4b7f193-ed59-4848-9d46-f368ca56a2b5");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Beams", "B", "The exact ordered Composite beam list supplied to Super Element Check.", GH_ParamAccess.list);
        p.AddGenericParameter("Results", "R", "Section/detail/force tree {beam index; station index}. Sparse detail trees are supported.", GH_ParamAccess.tree);
        p.AddIntegerParameter("Girder", "G", "Physical girder number, 1-based, from Bridge Member metadata.", GH_ParamAccess.item, 1);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Beams", "B", "Selected composite beams in increasing alignment station order; to plot Super-Element Beam.", GH_ParamAccess.list);
        p.AddGenericParameter("Results", "R", "Reindexed {beam;station} tree; to plot Results.", GH_ParamAccess.tree);
        p.AddNumberParameter("Stations", "S", "Existing result locations along this girder [m]; to stiffener plot Stations.", GH_ParamAccess.list);
        p.AddIntegerParameter("Available Girders", "G", "Available physical girder numbers.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var beams = new List<GH_CompositeBeam>(); int girder = 1;
        if (!da.GetDataList(0, beams) || !da.GetDataTree(1, out GH_Structure<IGH_Goo> input) || !da.GetData(2, ref girder)) return;
        // R6.5 P1 lane 3a / P2K (P1): a composite beam is valid with its GPC datum; it is read on the GPC bridge model only (below).
        if (beams.Any(b => b?.Gpc == null)) throw new ArgumentException("Valid composite beams are required.");
        // R6.4 (6): with composite beams linked to the GPC bridge model (Composite Beam from Super Element Filter or Decompose Model of a Build
        // BridGHe Model output) the ID, the member metadata, the ends and the length of every beam come from it (its beams and their bridge
        // data, N-mm by construction).
        // R6.5 P1 lane 3a / P2K (P1): composite beams not linked to a GPC bridge model have no member metadata and no stations to read: an
        // error, no output.
        var members = GpcGirders(beams) ?? throw GpcOnlyModels.Refused(GpcOnlyModels.RequiresBridgeModel);
        if (members.Any(m => !m.Girder)) throw new ArgumentException("Supply the longitudinal Composite beam list with physical girder metadata.");
        var selected = Enumerable.Range(0, beams.Count).Where(i => members[i].Group == girder)
            .OrderBy(i => members[i].StationFrom).ToArray();
        if (selected.Length == 0) throw new ArgumentException("The requested girder does not exist.");
        for (int i = 1; i < selected.Length; i++)
            if (members[selected[i - 1]].To.DistanceTo(members[selected[i]].From) > 1e-6)
                throw new ArgumentException("The selected girder must be a connected, forward-oriented beam chain.");
        var remap = selected.Select((old, current) => (old, current)).ToDictionary(p => p.old, p => p.current);
        var origins = new double[selected.Length];
        for (int i = 1; i < origins.Length; i++) origins[i] = origins[i - 1] + members[selected[i - 1]].Length;
        // F6 (a)/(b), R8 / P2K (P1): the stations of Super Element Check, from the merged samples of the GPC bridge model the composite beam is
        // linked to (Composite Beam from Super Element Filter or Decompose Model of a Build BridGHe Model output); no FEM results.
        var stations = beams.Select(b => (Common.Gpc.GpcBridge.Of(b.AnalysedGpcModel)?.SamplesOf(b.BeamId)?.Select(s => Math.Round(s.Sample.Station, 15))
            ?? throw GpcOnlyModels.Refused(GpcOnlyModels.RequiresBridgeModel))
            .Distinct().OrderBy(s => s).ToArray()).ToArray();
        var output = new GH_Structure<IGH_Goo>(); var positions = new SortedSet<double>();
        for (int i = 0; i < input.PathCount; i++)
        {
            var path = input.Paths[i].Indices;
            if (path.Length != 2 || path[0] < 0 || path[0] >= beams.Count || path[1] < 0 || path[1] >= stations[path[0]].Length)
                throw new ArgumentException("Invalid checker beam/station path: " + input.Paths[i]);
            foreach (var goo in input.Branches[i])
                if (goo?.ScriptVariable() is BridgeStressResult r && r.Source != null && r.Source.BeamId != members[path[0]].Id)
                    throw new ArgumentException("Result provenance does not match the supplied beam order.");
            if (!remap.TryGetValue(path[0], out int index)) continue;
            output.AppendRange(input.Branches[i], new GH_Path(index, path[1]));
            positions.Add((origins[index] + stations[path[0]][path[1]]) / 1000);
        }
        da.SetDataList(0, selected.Select(i => beams[i])); da.SetDataTree(1, output); da.SetDataList(2, positions);
        da.SetDataList(3, members.Select(m => m.Group).Distinct().OrderBy(g => g));
    }

    /// <summary>A beam as the component reads it: its BridGHe ID, whether it has one girder member, the girder number and the start station of
    /// the member, its ends and its length.</summary>
    private sealed record Member(string Id, bool Girder, int Group, double StationFrom, Rhino.Geometry.Point3d From, Rhino.Geometry.Point3d To, double Length);

    /// <summary>R6.4 (6): the GPC reading, when every composite beam is linked to its beam of a GPC bridge model: the ID (the original ID of the
    /// source of the beam), the member metadata of its bridge data (<see cref="Common.Gpc.GpcBridge.Data"/>), its ends and their distance of
    /// Rhino (as the FEM length); null otherwise.</summary>
    private static Member[] GpcGirders(IReadOnlyList<GH_CompositeBeam> beams)
    {
        var result = new Member[beams.Count];
        for (int i = 0; i < beams.Count; i++)
        {
            var bridge = Common.Gpc.GpcBridge.Of(beams[i].AnalysedGpcModel);
            if (bridge == null || beams[i].AnalysedGpc is not GPC.Model.Elements.BeamElement beam || !bridge.Model.BeamElements.TryGetValue(beam.Id, out var own) || !ReferenceEquals(own, beam)) return null;
            var m = bridge.Data.Member(beam.Guid);
            bool girder = m != null && m.Family == GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder;
            var from = new Rhino.Geometry.Point3d(beam.StartPoint.X, beam.StartPoint.Y, beam.StartPoint.Z); var to = new Rhino.Geometry.Point3d(beam.EndPoint.X, beam.EndPoint.Y, beam.EndPoint.Z);
            result[i] = new Member(beam.Source.OriginalId, girder, girder ? m.Group : 0, girder ? m.StationFromMm : 0, from, to, from.DistanceTo(to));
        }
        return result;
    }
}
