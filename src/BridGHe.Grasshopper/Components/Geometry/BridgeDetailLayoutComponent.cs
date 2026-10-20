using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeDetailLayoutComponent : BridgeUtilityComponent
{
    public BridgeDetailLayoutComponent() : base("Bridge Detail Layout", "DetailLayout",
        "Place gPC studs and web stiffeners along girders; explicit steel top datum, geometry/quantities and matching check definitions.", CategoryNameConstants.SUBCATEGORY_BRIDGE) { }
    public override Guid ComponentGuid => new Guid("1fd6e3a9-14f3-478f-a3a4-d49b5c51d12c");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Bridge Geometry", "BG", "Bridge geometry, mm.", GH_ParamAccess.item);
        p.AddGenericParameter("Section Geometry", "S", "gPC Bridge Section Geometry: H, inclined H or box.", GH_ParamAccess.item);
        p.AddIntegerParameter("Girders", "G", "Girder IDs; empty selects all.", GH_ParamAccess.list); p[2].Optional = true;
        p.AddNumberParameter("Start", "A", "Inclusive start station, mm.", GH_ParamAccess.item);
        p.AddNumberParameter("End", "B", "Exclusive end station, mm.", GH_ParamAccess.item);
        p.AddNumberParameter("Steel Top Offset", "Z", "Steel top surface above girder axis along deck normal, mm; not inferred from centroid.", GH_ParamAccess.item, 1250);
        p.AddGenericParameter("Studs", "P", "Optional gPC Bridge Studs definition.", GH_ParamAccess.item); p[6].Optional = true;
        p.AddGenericParameter("Intermediate Stiffener", "I", "Optional gPC intermediate stiffener.", GH_ParamAccess.item); p[7].Optional = true;
        p.AddGenericParameter("Support Stiffener", "R", "Optional gPC support stiffener; actual support stations.", GH_ParamAccess.item); p[8].Optional = true;
        p.AddNumberParameter("Stiffener Pitch", "L", "Automatic longitudinal pitch, mm.", GH_ParamAccess.item, 4000);
        p.AddNumberParameter("Stiffener Stations", "X", "Optional explicit stations override pitch; [Start,End).", GH_ParamAccess.list); p[10].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddLineParameter("Stud Axes", "P", "One axis per stud.", GH_ParamAccess.list);
        p.AddCircleParameter("Stud Heads", "H", "Head outlines at stud top.", GH_ParamAccess.list);
        p.AddMeshParameter("Stiffener Plates", "M", "Closed plate prisms for preview/quantities, not FEM plates.", GH_ParamAccess.list);
        p.AddGenericParameter("Details", "D", "gPC definitions paired with IDs/stations; use selected definitions in existing detail checks.", GH_ParamAccess.list);
        p.AddTextParameter("IDs", "ID", "Detail row/position identifiers.", GH_ParamAccess.list);
        p.AddNumberParameter("Stations", "S", "Detail progressive distances, mm.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Counts and limitations.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        BridgeGeometryGoo geometry = null; BridgeDetailGoo section = null, studs = null, intermediate = null, support = null;
        var girders = new List<int>(); var stations = new List<double>(); double start = 0, end = 0, top = 1250, pitch = 4000;
        if (!da.GetData(0, ref geometry) || !da.GetData(1, ref section) || !da.GetData(3, ref start) || !da.GetData(4, ref end)) return;
        da.GetDataList(2, girders); da.GetData(5, ref top); da.GetData(6, ref studs); da.GetData(7, ref intermediate);
        da.GetData(8, ref support); da.GetData(9, ref pitch); da.GetDataList(10, stations);
        if (section?.Value is not HSectionDimensions dimensions || studs != null && studs.Value is not BridgeStudOptions ||
            intermediate != null && intermediate.Value is not BridgeIntermediateStiffener || support != null && support.Value is not BridgeSupportStiffener)
            throw new ArgumentException("Connect the corresponding gPC section/stud/stiffener definitions.");
        var result = BridgeDetailLayout.Create(geometry.Value, girders, start, end, dimensions, top, studs?.Value as BridgeStudOptions,
            intermediate?.Value as BridgeIntermediateStiffener, support?.Value as BridgeSupportStiffener, pitch, stations);
        da.SetDataList(0, result.StudAxes); da.SetDataList(1, result.StudHeads); da.SetDataList(2, result.Stiffeners);
        da.SetDataList(3, result.Details.Select(d => new BridgeDetailGoo(d.Definition))); da.SetDataList(4, result.Details.Select(d => d.Id));
        da.SetDataList(5, result.Details.Select(d => d.Station)); da.SetDataList(6, result.Diagnostics);
    }
}
