using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSegmentOverrideComponent : BridgeUtilityComponent
{
    public BridgeSegmentOverrideComponent() : base("Bridge Segment Override", "SegmentOverride",
        "Override longitudinal beam properties by fabrication segment ID. Keeps steel cross-members, IDs, loads and geometry; clears results in independent copies.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("1e3d5d47-4df8-4dde-9cc9-7ec2c327ff2d");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Beams", "B", "Steel members from Bridge Member Assignment, carrying physical segment metadata.", GH_ParamAccess.list);
        p.AddTextParameter("Segment IDs", "ID", "G1-C1, G1-C2, etc., paired with Properties. Connect Bridge Segment Sections > Segment IDs.", GH_ParamAccess.list);
        p.AddGenericParameter("Properties", "P", "Exactly one property per Segment ID: steel, n0, n infinity or n shrinkage. No list broadcasting.", GH_ParamAccess.list);
        p.AddBooleanParameter("Preserve Mass", "Mass", "Preserve source mass per length: replacement density = source density * source area / replacement area. Prevents transformed concrete area being counted as steel self-weight. Elastic properties are unchanged.", GH_ParamAccess.item, true);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Girders", "G", "Longitudinal beams with replaced properties. Apply phase loads before Build Model.", GH_ParamAccess.list);
        p.AddGenericParameter("Other Steel", "S", "Unchanged properties of cross-members and bracing, copied independently.", GH_ParamAccess.list);
        p.AddTextParameter("Beam IDs", "ID", "Stable FEM IDs of the longitudinal girders, in Girders order.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Scope and reference-axis convention.", GH_ParamAccess.item);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var beams = new List<GH_Beam>(); var ids = new List<string>(); var properties = new List<GH_BeamProperty>();
        bool mass=true;
        if (!da.GetDataList(0, beams) || !da.GetDataList(1, ids) || !da.GetDataList(2, properties) || !da.GetData(3,ref mass)) return;
        var result = BridgeSegmentOverride.Apply(beams.Select(b => b?.Value).ToArray(), ids, properties.Select(p => p?.Value).ToArray(),mass);
        da.SetDataList(0, result.Girders.Select(b => new GH_Beam(b))); da.SetDataList(1, result.OtherMembers.Select(b => new GH_Beam(b)));
        da.SetDataList(2, result.Girders.Select(b => b.BeamId));
        da.SetData(3, "One keyed property per concio. Geometry, offsets and loads are preserved; all results are cleared. Homogenized properties must use the intended reference-axis convention. Preserve Mass adjusts only density to retain original mass per length.");
    }
}
