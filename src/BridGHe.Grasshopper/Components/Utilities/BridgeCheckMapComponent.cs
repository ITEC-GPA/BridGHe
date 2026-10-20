using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeCheckMapComponent : BridgeUtilityComponent
{
    private readonly List<(BridgeMappedMember Member, bool Label)> preview = new();
    public BridgeCheckMapComponent() : base("Bridge Check Map", "CheckMap",
        "Map supplied gPC section/detail checks to FEM beam IDs and stations. Missing checks remain visible; colours do not certify a complete bridge verification.", CategoryNameConstants.SUBCATEGORY_RESULTS) { }
    public override Guid ComponentGuid => new Guid("f9a95b40-c77d-4bc6-a08b-771f70905653");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Model", "M", "Model whose beam axes are coloured.", GH_ParamAccess.item);
        p.AddGenericParameter("Results", "R", "BridgeStressResult from existing section/detail checks, one per mapping entry.", GH_ParamAccess.list);
        p.AddTextParameter("Beam IDs", "B", "One exact FEM beam ID per result.", GH_ParamAccess.list);
        p.AddNumberParameter("Stations", "X", "One physical station from beam start per result, in model length units.", GH_ParamAccess.list);
        p.AddTextParameter("Cases", "LC", "One explicit case/combination label per result.", GH_ParamAccess.list);
        p.AddBooleanParameter("Labels", "L", "Draw maximum-ratio labels in the viewport.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddLineParameter("Axes", "A", "Axes paired with IDs and colours.", GH_ParamAccess.list);
        p.AddColourParameter("Colours", "C", "Red: supplied ratio above 1. Amber: some checks unavailable. Grey: no numeric checks.", GH_ParamAccess.list);
        p.AddTextParameter("Beam IDs", "B", "Output model beam IDs.", GH_ParamAccess.list);
        p.AddNumberParameter("Maximum Ratios", "R", "Largest supplied ratio per beam, NaN if unavailable.", GH_ParamAccess.list);
        p.AddIntegerParameter("Unavailable", "U", "Number of supplied checks with no numerical ratio.", GH_ParamAccess.list);
        p.AddTextParameter("Governing", "G", "Check, phase, case and station of every tied maximum.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Meaning and coverage of the map.", GH_ParamAccess.item);
    }
    protected override void BeforeSolveInstance() { base.BeforeSolveInstance(); preview.Clear(); }
    protected override void Solve(IGH_DataAccess da)
    {
        GH_Model model = null; var results = new List<GH_ObjectWrapper>(); var ids = new List<string>(); var stations = new List<double>(); var cases = new List<string>(); bool labels = false;
        if (!da.GetData(0, ref model) || !da.GetDataList(1, results) || !da.GetDataList(2, ids) ||
            !da.GetDataList(3, stations) || !da.GetDataList(4, cases)) return;
        da.GetData(5, ref labels);
        if (results.Count != ids.Count || ids.Count != stations.Count || ids.Count != cases.Count ||
            results.Any(r => r?.Value is not BridgeStressResult) || cases.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Provide one beam ID, station and case label for each BridgeStressResult.");
        var rows = new List<BridgeMappedCheck>();
        for (int i = 0; i < results.Count; i++)
        {
            var result = (BridgeStressResult)results[i].Value;
            var checks = BridgeGpc.Checks(result.Stage).ToArray();
            if (checks.Length == 0) rows.Add(new(ids[i], stations[i], result.Stage.Name, cases[i], "No available checks", null, "Unavailable", ""));
            else rows.AddRange(checks.Select(c => new BridgeMappedCheck(ids[i], stations[i], result.Stage.Name, cases[i], c.Name, c.Ratio, c.Status, c.Note)));
        }
        var mapped = BridgeCheckMap.Evaluate(model.Value, rows); preview.AddRange(mapped.Select(m => (m, labels)));
        da.SetDataList(0, mapped.Select(m => m.Axis)); da.SetDataList(1, mapped.Select(m => m.Color));
        da.SetDataList(2, mapped.Select(m => m.BeamId)); da.SetDataList(3, mapped.Select(m => m.Maximum));
        da.SetDataList(4, mapped.Select(m => m.Unavailable)); da.SetDataList(5, mapped.Select(m => m.Governing));
        da.SetData(6, "Colours summarize supplied local checks only. Unmapped beams are grey; unsampled stations, other limit states and global stability are outside this map.");
    }
    public override BoundingBox ClippingBox => preview.Count == 0 ? BoundingBox.Empty : new BoundingBox(preview.SelectMany(p => new[] { p.Member.Axis.From, p.Member.Axis.To }));
    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        foreach (var p in preview)
        {
            args.Display.DrawLine(p.Member.Axis, p.Member.Color, 5);
            if (p.Label) args.Display.DrawDot(p.Member.Axis.PointAt(.5), p.Member.Label, p.Member.Color, Color.White);
        }
    }
}
