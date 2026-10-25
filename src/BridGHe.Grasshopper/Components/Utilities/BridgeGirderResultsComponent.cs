using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
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
        if (beams.Any(b => b?.Value == null)) throw new ArgumentException("Valid composite beams are required.");
        var metadata = beams.Select(b => b.Value.Attributes.OfType<BridgeMemberAttributeModel>().ToArray()).ToArray();
        if (metadata.Any(m => m.Length != 1 || m[0].Value.Family != BridgeMemberFamily.Girder)) throw new ArgumentException("Supply the longitudinal Composite beam list with physical girder metadata.");
        var selected = Enumerable.Range(0, beams.Count).Where(i => metadata[i][0].Value.Group == girder)
            .OrderBy(i => metadata[i][0].Value.StationFrom).ToArray();
        if (selected.Length == 0) throw new ArgumentException("The requested girder does not exist.");
        for (int i = 1; i < selected.Length; i++)
            if (beams[selected[i - 1]].Value.PointTo.DistanceTo(beams[selected[i]].Value.PointFrom) > 1e-6)
                throw new ArgumentException("The selected girder must be a connected, forward-oriented beam chain.");
        var remap = selected.Select((old, current) => (old, current)).ToDictionary(p => p.old, p => p.current);
        var origins = new double[selected.Length];
        for (int i = 1; i < origins.Length; i++) origins[i] = origins[i - 1] + beams[selected[i - 1]].Value.Length;
        var stations = beams.Select(b => b.Value.Results.OfType<BeamForceResultModel>()
            .Select(r => Math.Round(r.NormalizedLength ? r.Station * b.Value.Length : r.Station, 15)).Distinct().OrderBy(s => s).ToArray()).ToArray();
        var output = new GH_Structure<IGH_Goo>(); var positions = new SortedSet<double>();
        for (int i = 0; i < input.PathCount; i++)
        {
            var path = input.Paths[i].Indices;
            if (path.Length != 2 || path[0] < 0 || path[0] >= beams.Count || path[1] < 0 || path[1] >= stations[path[0]].Length)
                throw new ArgumentException("Invalid checker beam/station path: " + input.Paths[i]);
            foreach (var goo in input.Branches[i])
                if (goo?.ScriptVariable() is BridgeStressResult r && r.Source != null && r.Source.BeamId != beams[path[0]].Value.BeamId)
                    throw new ArgumentException("Result provenance does not match the supplied beam order.");
            if (!remap.TryGetValue(path[0], out int index)) continue;
            output.AppendRange(input.Branches[i], new GH_Path(index, path[1]));
            positions.Add((origins[index] + stations[path[0]][path[1]]) / 1000);
        }
        da.SetDataList(0, selected.Select(i => beams[i])); da.SetDataTree(1, output); da.SetDataList(2, positions);
        da.SetDataList(3, metadata.Select(m => m[0].Value.Group).Distinct().OrderBy(g => g));
    }
}
