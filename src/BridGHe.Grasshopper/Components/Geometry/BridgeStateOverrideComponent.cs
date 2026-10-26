using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeStateOverrideComponent : BridgeUtilityComponent
{
    public BridgeStateOverrideComponent() : base("Bridge State Override","StateOverride","Assign uncracked or steel+rebar properties per FEM beam, retaining one physical section per fabrication segment. Stores the local state for the bridge checker. Shrinkage is excluded.",CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("47812468-52bb-4454-9363-4602d399473e");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Beams","B","Steel members, same reference mesh as the classified model.",GH_ParamAccess.list);
        p.AddTextParameter("Segment IDs","ID","One physical segment ID per Composite Section.",GH_ParamAccess.list);
        p.AddGenericParameter("Composite Sections","P","Full composite properties, concrete and rebar enabled, one per segment.",GH_ParamAccess.list);
        p.AddGenericParameter("States","S","Beam Moment Dispatch > States. All longitudinal beams required.",GH_ParamAccess.list);
        p.AddIntegerParameter("Response","R","1=long-term n infinity; 2=instantaneous n0. No shrinkage here.",GH_ParamAccess.item,1);
        p.AddIntegerParameter("Mixed Policy","Mix","0=stop, 1=uncracked, 2=cracked. A uniform approximation over each mixed-sign FEM beam; neither choice is a general safety bound.",GH_ParamAccess.item,0);
        p.AddIntegerParameter("Zero Policy","Zero","0=uncracked, 1=cracked for moments within tolerance.",GH_ParamAccess.item,0);
        p.AddBooleanParameter("Preserve Mass","Mass","Keep original mass per length by adjusting only transformed material density.",GH_ParamAccess.item,true);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Girders","G","Updated longitudinal beams; FEM results cleared.",GH_ParamAccess.list);
        p.AddGenericParameter("Other Steel","S","Other members with original properties; results cleared.",GH_ParamAccess.list);
        p.AddTextParameter("Beam IDs","ID","Girder IDs in output order.",GH_ParamAccess.list);
        p.AddTextParameter("Information","I","Assigned section state per beam. Recalculate before merging.",GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var beams=new List<GH_Beam>();var ids=new List<string>();var props=new List<GH_CompositeBeamProperty>();var states=new List<BeamMomentState>();int response=1,mixed=0,zero=0;bool mass=true;
        if(!da.GetDataList(0,beams)||!da.GetDataList(1,ids)||!da.GetDataList(2,props)||!da.GetDataList(3,states)||!da.GetData(4,ref response)||!da.GetData(5,ref mixed)||!da.GetData(6,ref zero)||!da.GetData(7,ref mass))return;
        if(states.Count==0 || states.Any(s=>s.Sign==BeamMomentSign.Missing)){AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,"Waiting for reference FEM results on every girder.");return;}
        var result=BridgeStateOverride.Apply(beams.Select(b=>b.Value).ToArray(),ids,props.Select(p=>p.Value).ToArray(),states,(BridgeActionBehavior)response,mixed,zero,mass);
        da.SetDataList(0,result.Girders.Select(b=>new GH_Beam(b)));da.SetDataList(1,result.OtherMembers.Select(b=>new GH_Beam(b)));da.SetDataList(2,result.Girders.Select(b=>b.BeamId));
        da.SetDataList(3,result.Girders.Select(b=>$"{b.BeamId} | {b.BeamProperty.Name} | {(b.Attributes.OfType<BridgeSectionStateAttributeModel>().Single().Value.ConcreteExcluded ? "cracked: steel + rebar" : "uncracked")}"));
        if(states.Any(s=>s.Sign==BeamMomentSign.Mixed))AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,"Mixed-sign beams assigned uniformly by explicit policy. Check sensitivity to common mesh refinement; this choice is not a general safety bound.");
    }
}
