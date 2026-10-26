using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BeamMomentDispatchComponent : BridgeUtilityComponent
{
    public BeamMomentDispatchComponent() : base("Beam Moment Dispatch", "MomentDispatch", "Classify all sampled moments of each beam in one FEM case. This is a preliminary stiffness map, not a code cracking assessment.", CategoryNameConstants.SUBCATEGORY_TOOLS) { }
    public override Guid ComponentGuid => new Guid("2db286af-c169-4c73-b623-3ec29ca21341");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Beams","B","Analysed longitudinal beams, unique FEM IDs.",GH_ParamAccess.list);
        p.AddTextParameter("Case","C","Exact result case name. Dead is the reserved self-weight case in this example.",GH_ParamAccess.item);
        p.AddNumberParameter("Tolerance","T","Zero moment tolerance [N mm].",GH_ParamAccess.item,1000);
        p.AddIntegerParameter("Component","M","0=Mx/BM1, 1=My/BM2. Bridge main bending uses BM2 after Straus result normalization.",GH_ParamAccess.item,1);
        p.AddIntegerParameter("Sign","S","+1 or -1. With consistent section-up axes, positive BM2 compresses the slab.",GH_ParamAccess.item,1);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        foreach(var name in new[]{"Positive","Negative","Mixed","Zero","Missing"}) p.AddGenericParameter(name,name,"Original beams in this class; input order retained.",GH_ParamAccess.list);
        p.AddGenericParameter("States","S","Per-beam classification for Bridge State Override.",GH_ParamAccess.list);
        p.AddTextParameter("Information","I","ID, sampled extrema [N mm], class and scope.",GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var beams=new List<GH_Beam>(); string lc=""; double tol=1000; int component=1,sign=1;
        if(!da.GetDataList(0,beams)||!da.GetData(1,ref lc)||!da.GetData(2,ref tol)||!da.GetData(3,ref component)||!da.GetData(4,ref sign))return;
        var states=BeamMomentClassification.Classify(beams.Select(b=>b?.Value).ToArray(),lc,tol,component,sign);
        for(int k=0;k<5;k++) da.SetDataList(k,beams.Where((b,i)=>(int)states[i].Sign==k));
        da.SetDataList(5,states);da.SetDataList(6,states.Select(s=>s.ToString()).Prepend("Sampled sign only. Request adequate result stations/extrema; this does not determine code cracking zones or split the mesh."));
        if(states.Any(s=>s.Sign==BeamMomentSign.Missing))AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,"Some beams have no results for this case. Recalculate the reference model including the selected case.");
        if(states.Any(s=>s.Sign==BeamMomentSign.Mixed))AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,"Mixed-sign FEM beams: refine the common mesh or explicitly choose an approximation in Bridge State Override.");
    }
}
