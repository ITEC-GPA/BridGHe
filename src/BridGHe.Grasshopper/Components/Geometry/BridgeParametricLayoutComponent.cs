using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeParametricLayoutComponent : BridgeUtilityComponent
{
    public BridgeParametricLayoutComponent() : base("Bridge Parametric Layout","BridgeLayout",
        "Regular or explicit spans/girders, straight/circular/custom alignment and a suggested bearing pattern. All lengths in mm; no structural sizing.",CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("14c68fb0-de0e-4eec-bc39-282e2c5f08b6");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddIntegerParameter("Span Count","N","Number of equal spans; ignored when Span Lengths is supplied.",GH_ParamAccess.item,3);
        p.AddNumberParameter("Span Length","L","Uniform span length, mm.",GH_ParamAccess.item,30000);
        p.AddNumberParameter("Span Lengths","Ls","Optional unequal span lengths in mm; overrides count and uniform length.",GH_ParamAccess.list); p[2].Optional=true;
        p.AddIntegerParameter("Girder Count","NG","Number of regularly spaced girders, minimum 2; ignored with explicit offsets.",GH_ParamAccess.item,3);
        p.AddNumberParameter("Girder Spacing","DY","Uniform girder spacing, mm.",GH_ParamAccess.item,3500);
        p.AddNumberParameter("Girder Offsets","Y","Optional increasing signed offsets in mm: right negative, left positive.",GH_ParamAccess.list); p[5].Optional=true;
        p.AddNumberParameter("Left Overhang","OL","Deck edge beyond leftmost girder, mm.",GH_ParamAccess.item,1500);
        p.AddNumberParameter("Right Overhang","OR","Deck edge beyond rightmost girder, mm.",GH_ParamAccess.item,1500);
        p.AddNumberParameter("Start Overhang","OS","Alignment extension before first support, mm.",GH_ParamAccess.item,0);
        p.AddNumberParameter("End Overhang","OE","Alignment extension after last support, mm.",GH_ParamAccess.item,0);
        p.AddNumberParameter("Radius","R","Plan radius, mm: 0 straight, positive left curve, negative right curve. Automatic arc below 180 degrees.",GH_ParamAccess.item,0);
        p.AddPointParameter("Origin","O","Start point of the generated alignment, mm.",GH_ParamAccess.item,Point3d.Origin);
        p.AddNumberParameter("Heading","A","Initial heading from global X about global Z, degrees.",GH_ParamAccess.item,0);
        p.AddCurveParameter("Custom Alignment","C","Optional open 3D curve. Length must equal total spans plus end overhangs. Overrides Radius, Origin and Heading.",GH_ParamAccess.item); p[13].Optional=true;
        p.AddNumberParameter("Bracing Spacing","BS","Maximum spacing of longitudinal bracing panels, mm. Every support is included.",GH_ParamAccess.item,6000);
        p.AddIntegerParameter("Bearing Types Override","BO","Optional one code or one per bearing, ordered by support then girder: 0 fixed, 1 longitudinal guide, 2 multidirectional, 3 elastic.",GH_ParamAccess.list); p[15].Optional=true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddCurveParameter("Alignment","A","Top-of-deck reference alignment, mm.",GH_ParamAccess.item);
        p.AddNumberParameter("Support Stations","S","Support distances along alignment, mm.",GH_ParamAccess.list);
        p.AddNumberParameter("Girder Offsets","Y","Actual signed girder offsets, mm.",GH_ParamAccess.list);
        p.AddNumberParameter("Deck Widths","W","Left and right distances from alignment, mm.",GH_ParamAccess.list);
        p.AddNumberParameter("Span Lengths","L","Actual span lengths, mm.",GH_ParamAccess.list);
        p.AddIntegerParameter("Bearing Types","BT","Suggested support-major/girder-minor order: first girder fixed at first support, guided at others; remaining bearings multidirectional. Review for curved/skew bridges.",GH_ParamAccess.list);
        p.AddNumberParameter("Length","TL","Total alignment length, mm.",GH_ParamAccess.item);
        p.AddTextParameter("Information","I","Layout and override precedence.",GH_ParamAccess.item);
        p.AddNumberParameter("Bracing Stations","BS","Regular bracing panel boundaries in each span/overhang, mm.",GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        int n=3,g=3; double l=30000,d=3500,ol=1500,or=1500,os=0,oe=0,r=0,h=0;
        var spans=new List<double>(); var offsets=new List<double>(); Point3d o=Point3d.Origin; Curve c=null;
        da.GetData(0,ref n); da.GetData(1,ref l); da.GetDataList(2,spans); da.GetData(3,ref g); da.GetData(4,ref d);
        da.GetDataList(5,offsets); da.GetData(6,ref ol); da.GetData(7,ref or); da.GetData(8,ref os); da.GetData(9,ref oe);
        da.GetData(10,ref r); da.GetData(11,ref o); da.GetData(12,ref h); da.GetData(13,ref c);
        var result=BridgeParametricLayout.Create(n,l,spans.ToArray(),g,d,offsets.ToArray(),ol,or,os,oe,r,o,h,c);
        double bracing=6000; da.GetData(14,ref bracing); var types=new List<int>(); da.GetDataList(15,types);
        double length=result.Alignment.GetLength();
        if(!double.IsFinite(bracing) || bracing<=0 || length/bracing>1000) throw new ArgumentException("Use positive Bracing Spacing and at most 1000 panels.");
        if(types.Count>0 && (types.Count!=1 && types.Count!=result.BearingTypes.Length || types.Any(t=>t<0 || t>3)))
            throw new ArgumentException("Bearing override needs one code or one per support/girder point, in 0..3.");
        var canonical=new List<double>();
        foreach(double s in new[]{0d}.Concat(result.Supports).Append(length).OrderBy(s=>s))
            if(canonical.Count==0 || s-canonical[^1]>SimpleBridgeBuilder.JoinTolerance) canonical.Add(s);
        var boundaries=canonical.ToArray();
        var bracingStations=new SortedSet<double>(boundaries);
        for(int i=0;i+1<boundaries.Length;i++)
        {
            int count=Math.Max(1,(int)Math.Ceiling((boundaries[i+1]-boundaries[i]-SimpleBridgeBuilder.JoinTolerance)/bracing));
            for(int j=1;j<count;j++) bracingStations.Add(boundaries[i]+(boundaries[i+1]-boundaries[i])*j/count);
        }
        if(bracingStations.Count>1001) throw new ArgumentException("At most 1000 longitudinal bracing panels; increase spacing.");
        da.SetData(0,result.Alignment); da.SetDataList(1,result.Supports); da.SetDataList(2,result.Offsets);
        da.SetDataList(3,result.DeckWidths); da.SetDataList(4,result.SpanLengths); da.SetDataList(5,types.Count>0?types:result.BearingTypes);
        da.SetData(6,result.Alignment.GetLength());
        da.SetData(7,$"{result.SpanLengths.Length} spans, {result.Offsets.Length} girders. Explicit lists override regular spacing; custom alignment overrides generated curve. Bearing pattern is an editable proposal.");
        da.SetDataList(8,bracingStations);
    }
}
