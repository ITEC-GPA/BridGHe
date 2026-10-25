using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSegmentCompositeComponent : BridgeUtilityComponent
{
    public BridgeSegmentCompositeComponent() : base("Bridge Segment Composite", "SegmentComposite",
        "Match composite sections to longitudinal FEM beams by physical concio ID. Retains results; aligned outputs feed Composite beam and Composite Shrinkage Actions.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("36a9ed77-8fb7-4348-a783-f1c057a921e4");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Beams", "B", "FEM members carrying Bridge Member metadata. Other member families are filtered out.", GH_ParamAccess.list);
        p.AddTextParameter("Segment IDs", "ID", "Distinct G1-C1 etc., paired with Composite Properties, from Bridge Segment Sections.", GH_ParamAccess.list);
        p.AddGenericParameter("Composite Properties", "P", "One composite section per Segment ID. No list broadcasting.", GH_ParamAccess.list);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Girders", "B", "FEM girders ordered by girder and alignment station, retaining forces. Connect to Composite beam > Beam.", GH_ParamAccess.list);
        p.AddGenericParameter("Composite Properties", "P", "One matched section per output FEM beam. Connect to Section Start or shrinkage conversion.", GH_ParamAccess.list);
        p.AddTextParameter("Segment IDs", "ID", "Concio key per output beam.", GH_ParamAccess.list);
        p.AddTextParameter("Beam IDs", "FEM", "FEM IDs in output order.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var beams = new List<GH_Beam>(); var ids = new List<string>(); var props = new List<GH_CompositeBeamProperty>();
        if (!da.GetDataList(0, beams) || !da.GetDataList(1, ids) || !da.GetDataList(2, props)) return;
        if (beams.Count == 0) { Message = "Awaiting beams"; return; }
        Message = "";
        var mapped = BridgeSegmentComposite.Map(beams.Select(b => b?.Value).ToArray(), ids, props.Select(p => p?.Value).ToArray());
        da.SetDataList(0, mapped.Girders.Select(b => new GH_Beam(b))); da.SetDataList(1, mapped.Properties.Select(p => new GH_CompositeBeamProperty(p)));
        da.SetDataList(2, mapped.SegmentIds); da.SetDataList(3, mapped.Girders.Select(b => b.BeamId));
    }
}
