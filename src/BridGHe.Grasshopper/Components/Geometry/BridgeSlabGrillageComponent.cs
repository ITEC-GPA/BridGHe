using System;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSlabGrillageComponent : BridgeUtilityComponent
{
    public BridgeSlabGrillageComponent() : base("Bridge Slab Grillage","SlabGrillage",
        "Assign concrete rectangular sections to transverse slab axes, including unequal station spacing and half end strips. No longitudinal composite stiffness or rigid links added.",CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("cf9a654c-6e81-4a74-ad4e-656308f92c69");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Grillage Geometry","GG","Simple Bridge Grillage Geometry: family 8 and geometric slab thickness, mm.",GH_ParamAccess.item);
        p.AddGenericParameter("Concrete Material","M","Concrete material in N-mm. Supply an effective modulus for the intended phase when needed.",GH_ParamAccess.item);
        p.AddTextParameter("Name","N","Prefix for strip properties.",GH_ParamAccess.item,"Soletta trasversale");
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Beams","B","Transverse slab FEM beams; add to Build Model with steel and explicit links.",GH_ParamAccess.list);
        p.AddGenericParameter("Properties","P","One rectangular property per beam, same order.",GH_ParamAccess.list);
        p.AddNumberParameter("Strip Widths","W","Actual tributary length per beam, mm, including reduced end strips.",GH_ParamAccess.list);
        p.AddTextParameter("Member IDs","ID","Geometric ID per beam.",GH_ParamAccess.list);
        p.AddTextParameter("Information","I","Mechanical scope of the generated strips.",GH_ParamAccess.item);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        BridgeGeometryGoo g=null; GH_Material m=null; string name="Soletta trasversale";
        if(!da.GetData(0,ref g)||!da.GetData(1,ref m)) return; da.GetData(2,ref name);
        // R6.2 (lane B): the strip properties and beams also built on GPC (the analysis material of the GPC datum of the material).
        GPC.Model.Materials.Material analysis=null;
        try{analysis=Common.Gpc.GpcMaterials.Analysis(m.Gpc);}catch(Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException){}
        var result=BridgeSlabGrillage.Create(g.Value,m.Value,name,analysis);
        // R6.5 P1 lane 1b: the beams by their GPC drafts alone (identity by content of the plan), the member IDs from the plans; the strip
        // properties as before (a property kind: lane 2).
        bool only=GpcOnlyElements.GeneratorsWithoutFem;
        var beams=result.Beams.Select(b=>new GH_Beam(b)).ToArray();
        var properties=result.Beams.Select(b=>new GH_BeamProperty(b.BeamProperty)).ToArray();
        for(int i=0;i<beams.Length;i++)
        {
            var plan=result.Plans[i];
            GpcBeamInputs.Attach(beams[i],id=>Common.Gpc.Builders.GpcBridgeGeometryBuilders.Assigned(plan,id));
            if(analysis!=null) properties[i].AttachGpc(plan.Property());
        }
        if(only) da.SetDataList(0,BridgeMemberAssignmentComponent.GpcOnly(this,0,result.Plans)); else da.SetDataList(0,beams);
        da.SetDataList(1,properties);
        da.SetDataList(2,result.Widths);
        da.SetDataList(3,only?result.Plans.Select(p=>p.GeometryId):result.Beams.Select(b=>b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.GeometryId));
        da.SetData(4,"Transverse rectangular strips only; section y follows slab normal. Widths are measured along alignment. Curved/skew grillage stiffness requires calibration. Add eccentric links separately; longitudinal composite action belongs to girder section/phase properties.");
    }
}
