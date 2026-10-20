using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSegmentsComponent : BridgeUtilityComponent
{
    public BridgeSegmentsComponent() : base("Bridge Segments", "Segments", "Girder fabrication joints in mm. Independent of supports, FE mesh and construction phases.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("190346aa-929b-443c-9a38-622dd4a497db");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Joints", "J", "Ordered joint distances in mm. Branch {0}: common joints; {1}..{N}: additional joints for each girder. Non-empty explicit joints replace automatic subdivision for that girder.", GH_ParamAccess.tree);
        p[0].Optional = true;
        p.AddNumberParameter("Maximum Length", "L", "Automatic equal segment subdivision when no explicit joints apply. Zero: a single segment. mm.", GH_ParamAccess.item, 12000);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Segments", "S", "Connect to either Simple Bridge component. A joint does not create a release.", GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {
        double maximum = 12000; da.GetData(1, ref maximum);
        if (!double.IsFinite(maximum) || maximum < 0) throw new ArgumentException("Maximum Length must be finite and non-negative.");
        var joints = new Dictionary<int, double[]>();
        if (da.GetDataTree(0, out GH_Structure<GH_Number> tree))
            for (int i = 0; i < tree.PathCount; i++)
            {
                var path = tree.Paths[i];
                if (path.Length != 1 || path[0] < 0) throw new ArgumentException("Use branches {0} for common joints or {girder number}, starting at 1.");
                var values = tree.Branches[i].Select(x => x.Value).ToArray();
                if (values.Any(x => !double.IsFinite(x) || x < 0) || values.Zip(values.Skip(1), (a, b) => b - a).Any(d => d <= 0))
                    throw new ArgumentException("Joint stations must be finite, non-negative and strictly increasing.");
                joints.Add(path[0], values);
            }
        da.SetData(0, new BridgeLayoutGoo(new BridgeSegments { MaximumLength = maximum, Joints = joints }));
    }
}

public sealed class BridgeDiaphragmsComponent : BridgeUtilityComponent
{
    public BridgeDiaphragmsComponent() : base("Bridge Diaphragms", "Diaphragms", "Locations and truss layout for Simple Bridge. All lengths in mm; creates no stiffness or releases.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("7b70afbe-36b1-4880-9d3e-852628d0dbbc");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Stations", "S", "Explicit ordered distances; if empty, use Maximum Spacing.", GH_ParamAccess.list); p[0].Optional = true;
        p.AddNumberParameter("Maximum Spacing", "L", "Automatic spacing in each span/overhang; zero: explicit/support lines only. mm.", GH_ParamAccess.item, 5000);
        p.AddBooleanParameter("At Supports", "AS", "Add support lines with their skew. False omits supports from automatic grids; explicit Stations remain authoritative.", GH_ParamAccess.item, true);
        p.AddNumberParameter("Truss Height", "H", "Total depth centred on the girder reference axes, along the diaphragm frame normal. Used only by Truss. mm.", GH_ParamAccess.item, 1500);
        p.AddIntegerParameter("Panels per Bay", "P", "Number of truss panels between adjacent girders.", GH_ParamAccess.item, 2);
        p.AddIntegerParameter("Pattern", "T", "0 X, 1 V, 2 Warren. Warren has alternating diagonals and no internal posts.", GH_ParamAccess.item, 0);
        foreach (BridgeTrussPattern pattern in Enum.GetValues<BridgeTrussPattern>()) ((Param_Integer)p[5]).AddNamedValue(pattern.ToString(), (int)pattern);
        p.AddBooleanParameter("Connect X", "X", "Split both X diagonals at their common centre. False: crossing without a node. No releases are assigned.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Diaphragms", "D", "Connect to Simple Bridge.", GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {
        var stations = new List<double>(); double spacing = 5000, height = 1500; bool supports = true, crossing = false; int panels = 2, pattern = 0;
        da.GetDataList(0, stations); da.GetData(1, ref spacing); da.GetData(2, ref supports); da.GetData(3, ref height);
        da.GetData(4, ref panels); da.GetData(5, ref pattern); da.GetData(6, ref crossing);
        if (!double.IsFinite(spacing) || spacing < 0 || !double.IsFinite(height) || height <= 0 ||
            panels < 1 || panels > 100 || !Enum.IsDefined((BridgeTrussPattern)pattern) ||
            stations.Any(s => !double.IsFinite(s) || s < 0) || stations.Zip(stations.Skip(1), (a, b) => b - a).Any(d => d <= 0))
            throw new ArgumentException("Invalid diaphragm distances, spacing, height, panel count or pattern.");
        da.SetData(0, new BridgeLayoutGoo(new BridgeDiaphragms { Stations = stations.ToArray(), MaximumSpacing = spacing,
            AtSupports = supports, TrussHeight = height, PanelsPerBay = panels, Pattern = (BridgeTrussPattern)pattern, ConnectCrossings = crossing }));
    }
}
