using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeFamilyPropertyComponent : BridgeDefinitionComponent
{
    public BridgeFamilyPropertyComponent():base("Bridge Family Property","FamilyProperty","Associate one explicitly named geometric family with a beam property. Sections assigned per concio take precedence."){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f027");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Property","P","Beam or truss property.",GH_ParamAccess.item);
        p.AddIntegerParameter("Family","F","Select the member family from named values. All steel excludes the slab.",GH_ParamAccess.item,-1);
        var family=(Param_Integer)p[1];family.AddNamedValue("All steel families",-1);foreach(BridgeMemberFamily f in Enum.GetValues<BridgeMemberFamily>())family.AddNamedValue(f.ToString(),(int)f);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddGenericParameter("Family Property","P","Connect a list of these definitions to Bridge FEM Members.",GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {GH_BeamProperty p=null;int f=-1;if(!da.GetData(0,ref p))return;da.GetData(1,ref f);if(f< -1||f>8||p?.Value?.IsValid!=true)throw new ArgumentException("A valid beam property and member family are required.");da.SetData(0,new BridgeDesignGoo(new BridgeFamilyProperty(f,p.Value)));}
}

public sealed class BridgeFemMembersComponent : BridgeMemberAssignmentComponent
{
    public BridgeFemMembersComponent(){Name="Bridge FEM Members";NickName="FEMMembers";Description="Assign explicit family properties and optional per-concio section rules to the geometric wireframe. Creates no loads or restraints.";}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f028");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddGenericParameter("Bridge Geometry","G","From Grillage Mesh or Longitudinal Bracing.",GH_ParamAccess.item);p.AddGenericParameter("Family Properties","P","Optional list of Bridge Family Property objects. No parallel code/property lists.",GH_ParamAccess.list);p[1].Optional=true;p.AddGenericParameter("Section Rules","S","Optional rules from Bridge Segment Sections or Bridge Section Schedule.",GH_ParamAccess.list);p[2].Optional=true;}
    protected override void Solve(IGH_DataAccess da)
    {
        BridgeGeometryGoo g=null;if(!da.GetData(0,ref g))return;var properties=new List<BridgeDesignGoo>();var rules=new List<BridgeDesignGoo>();da.GetDataList(1,properties);da.GetDataList(2,rules);
        if(properties.Any(p=>p.Value is not BridgeFamilyProperty)||rules.Any(r=>r.Value is not BridgeSectionRule))throw new ArgumentException("Connect Family Property objects and Section Rules to their corresponding inputs.");
        var values=properties.Select(p=>(BridgeFamilyProperty)p.Value).ToArray();if(values.Select(v=>v.Family).Distinct().Count()!=values.Length)throw new ArgumentException("Each family can have only one default property.");
        Publish(da,g.Value,values.ToDictionary(v=>v.Family,v=>v.Property),rules.Select(r=>(BridgeSectionRule)r.Value).ToArray());
    }
}
