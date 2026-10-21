using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeDetailStationsComponent : BridgeUtilityComponent
{
    public BridgeDetailStationsComponent() : base("Bridge Detail Stations", "DetailStations",
        "Select existing section results at physical stiffener points. Optionally combine actual support reactions from the original phase sources for bearing stiffeners.", CategoryNameConstants.SUBCATEGORY_RESULTS) { }
    public override Guid ComponentGuid => new Guid("6dfef487-3bcf-456d-8de7-d28851f797f8");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Model", "M", "Assembled Build BridGHe Model, N-mm.", GH_ParamAccess.item);
        p.AddGenericParameter("Results", "R", "Final cumulative section results; original beam/station/combination provenance is required.", GH_ParamAccess.tree);
        p.AddPointParameter("Locations", "P", "Actual stiffener or support points on the FEM beam reference axes, in mm. No interpolation.", GH_ParamAccess.list);
        p.AddGenericParameter("Phases", "Ph", "Optional original Bridge Phase objects with full analysed models and node reactions. Connect only for supports; no phases means selection only.", GH_ParamAccess.list); p[3].Optional = true;
        p.AddVectorParameter("Up", "Up", "Positive compression reaction direction. Global Z by default.", GH_ParamAccess.item, Vector3d.ZAxis);
        p.AddNumberParameter("Tolerance", "Tol", "Point matching tolerance [mm].", GH_ParamAccess.item, 1e-6);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Results", "R", "Selected results, with original paths, combinations and both sides of beam joints.", GH_ParamAccess.tree);
        p.AddNumberParameter("Compression kN", "R+", "max(0, signed reaction), same tree/items as Results; to Support Stiffener > ReactionKN. Empty in selection-only mode.", GH_ParamAccess.tree);
        p.AddNumberParameter("Signed reaction kN", "Rsgn", "Positive compression, negative uplift; never absolute value.", GH_ParamAccess.tree);
        p.AddIntegerParameter("End type", "End", "0 internal, 1 start, 2 end of the selected girder chain; to Support Stiffener > Location.", GH_ParamAccess.tree);
        p.AddPointParameter("Points", "P", "Matched reference-axis points, same tree/items.", GH_ParamAccess.tree);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        GH_Model model = null; var locations = new List<Point3d>(); var phaseGoos = new List<BridgeWorkflowGoo>();
        var up = Vector3d.ZAxis; double tolerance = 1e-6;
        if (!da.GetData(0, ref model) || !da.GetDataTree(1, out GH_Structure<IGH_Goo> input) || !da.GetDataList(2, locations)
            || !da.GetData(4, ref up) || !da.GetData(5, ref tolerance)) return;
        da.GetDataList(3, phaseGoos);
        if (model?.Value == null || model.Value.Units != BridGHe.Common.Models.ModelModel.ModelUnits.Nmm) throw new ArgumentException("Model must use N-mm.");
        if (!double.IsFinite(tolerance) || tolerance <= 0 || locations.Count == 0 || locations.Any(p => !p.IsValid)) throw new ArgumentException("Supply valid detail points and positive tolerance.");
        if (phaseGoos.Any(g => g.Value is not BridgePhaseDefinition)) throw new ArgumentException("Phases requires Bridge Phase objects.");
        var phases = phaseGoos.Select(g => (BridgePhaseDefinition)g.Value).ToArray();
        var results = input.AllData(true).Select(g => g?.ScriptVariable() as BridgeStressResult ?? throw new ArgumentException("Expected section results.")).ToArray();
        if (results.Any(r => r.Source == null || r.Stage.GetHistory() != null)) throw new ArgumentException("Use final cumulative results with FEM provenance.");
        var beams = model.Value.Beams.ToDictionary(b => b.BeamId, StringComparer.Ordinal);
        if (results.Any(r => !beams.ContainsKey(r.Source.BeamId))) throw new ArgumentException("Result beam IDs do not belong to this model.");
        var selectedBeams = results.Select(r => r.Source.BeamId).Distinct().Select(id => beams[id]).ToArray();
        var output = new GH_Structure<GH_ObjectWrapper>(); var compression = new GH_Structure<GH_Number>();
        var signed = new GH_Structure<GH_Number>(); var endTypes = new GH_Structure<GH_Integer>(); var points = new GH_Structure<GH_Point>();
        var matched = new bool[locations.Count]; var cache = new Dictionary<(int Point, string Combination), double>(); bool uplift = false;
        for (int branch = 0; branch < input.PathCount; branch++)
        foreach (var goo in input.Branches[branch])
        {
            var result = (BridgeStressResult)goo.ScriptVariable(); var beam = beams[result.Source.BeamId];
            double station = result.Source.StationMm;
            if (!double.IsFinite(station) || station < -tolerance || station > beam.Length + tolerance) throw new ArgumentException("Invalid local result station.");
            var point = beam.PointFrom + (beam.PointTo - beam.PointFrom) * (station / beam.Length);
            var matches = Enumerable.Range(0, locations.Count).Where(i => point.DistanceTo(locations[i]) <= tolerance).ToArray();
            if (matches.Length == 0) continue;
            if (matches.Length != 1) throw new ArgumentException("Duplicate/overlapping detail locations.");
            int index = matches[0]; matched[index] = true; double reaction = double.NaN;
            if (phases.Length > 0 && !cache.TryGetValue((index, result.Source.Combination), out reaction))
                cache[(index, result.Source.Combination)] = reaction = BridgeSupportReactions.SignedKN(model.Value, phases, locations[index], result.Source.Combination, up, tolerance);
            int neighbours = selectedBeams.Count(b => b.PointFrom.DistanceTo(point) <= tolerance || b.PointTo.DistanceTo(point) <= tolerance);
            int end = neighbours == 1 ? station <= tolerance ? 1 : 2 : 0;
            var path = input.Paths[branch]; output.Append(new GH_ObjectWrapper(result), path);
            if (phases.Length > 0)
            { compression.Append(new GH_Number(Math.Max(0, reaction)), path); signed.Append(new GH_Number(reaction), path); }
            endTypes.Append(new GH_Integer(end), path); points.Append(new GH_Point(point), path); uplift |= reaction < -1e-9;
        }
        if (matched.Any(m => !m)) throw new ArgumentException("Some locations have no existing result station. Add the points to the FEM mesh/output stations and rerun the analyses.");
        da.SetDataTree(0, output); da.SetDataTree(1, compression); da.SetDataTree(2, signed); da.SetDataTree(3, endTypes); da.SetDataTree(4, points);
        if (uplift) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Uplift exists: compression is zero there; bearing anchorage requires a separate check. See Signed reaction.");
    }
}
