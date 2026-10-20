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

public sealed class BridgeDeckMeshComponent : BridgeUtilityComponent
{
    public BridgeDeckMeshComponent() : base("Bridge Deck Mesh", "DeckMesh",
        "Slab mid-surface FEM mesh with girder projection columns, pour boundaries and explicit connection lines. N-mm.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("87971a76-5aa5-4c78-938c-6da8bdeee793");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Bridge Geometry", "BG", "Typed Simple Bridge geometry.", GH_ParamAccess.item);
        p.AddGenericParameter("Plate Property", "P", "Structural plate thicknesses must equal slab thickness. Steel-only girders are required with a structural deck.", GH_ParamAccess.item);
        p.AddNumberParameter("Longitudinal Mesh Length", "L", "Maximum reference station interval, mm; existing geometry stations are retained.", GH_ParamAccess.item, 2500);
        p.AddNumberParameter("Transverse Mesh Length", "W", "Maximum transverse interval, mm, with additional girder columns.", GH_ParamAccess.item, 1500);
        p.AddNumberParameter("Pour Boundaries", "S", "Ordered internal stations, mm. Split mesh and define separate pour groups.", GH_ParamAccess.list); p[4].Optional = true;
        p.AddBooleanParameter("Triangles", "T", "True avoids warped quads; false produces quadrilateral plates.", GH_ParamAccess.item, true);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Bridge Geometry", "BG", "Girder axes split at all deck attachment stations; use this output for Member Assignment.", GH_ParamAccess.item);
        p.AddMeshParameter("Mesh", "M", "Open slab mid-surface mesh for preview.", GH_ParamAccess.item);
        p.AddGenericParameter("Plates", "P", "Plate elements to Build Model.", GH_ParamAccess.list);
        p.AddLineParameter("Connection Lines", "C", "Beam axis to deck vertex: connect to Bridge Connections. No automatic rigidity.", GH_ParamAccess.list);
        p.AddNumberParameter("Stations", "S", "Mesh stations along alignment, mm.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Counts and modelling choices.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        BridgeGeometryGoo geometry = null; GH_PlateProperty property = null; double length = 2500, width = 1500; bool triangles = true; var pours = new List<double>();
        if (!da.GetData(0, ref geometry) || !da.GetData(1, ref property)) return;
        da.GetData(2, ref length); da.GetData(3, ref width); da.GetDataList(4, pours); da.GetData(5, ref triangles);
        var result = BridgeDeckMesh.Create(geometry.Value, property.Value, length, width, pours, triangles);
        da.SetData(0, new BridgeGeometryGoo(result.Geometry)); da.SetData(1, result.Mesh);
        da.SetDataList(2, result.Plates.Select(p => new GH_Plate(p))); da.SetDataList(3, result.Connections);
        da.SetDataList(4, result.Stations); da.SetDataList(5, result.Diagnostics);
    }
}
