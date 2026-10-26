using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public abstract class BridgeConnectionComponent : BridgeUtilityComponent
{
    private bool rigid => this is BridgeRigidLinksComponent;
    private readonly List<Line> lines = new();
    protected BridgeConnectionComponent(bool rigid) : base(rigid ? "Bridge Rigid Links" : "Bridge Connection Beams", rigid ? "RigidLinks" : "ConnectionBeams",
        rigid ? "Native spatial kinematic links between girder and slab nodes. No artificial penalty stiffness." : "Connection members with an explicit beam/truss property. Their stiffness and weight enter the FEM model.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    protected override void RegisterInputParams(GH_InputParamManager p)
    { p.AddLineParameter("Connection Axes","L","Connect Deck Connections from Bridge Grillage Mesh. Endpoints must coincide with FEM nodes, mm.",GH_ParamAccess.list); if(!rigid)p.AddGenericParameter("Connection Property","P","Explicit beam/truss property.",GH_ParamAccess.item); }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {p.AddGenericParameter("Elements","E","Connect to Build Model.",GH_ParamAccess.list);p.AddTextParameter("Information","I","Mechanical representation.",GH_ParamAccess.item);}
    protected override void BeforeSolveInstance(){base.BeforeSolveInstance();lines.Clear();}
    protected override void Solve(IGH_DataAccess da)
    {
        var current=new List<Line>(); GH_BeamProperty p=null;if(!da.GetDataList(0,current)||!rigid&&!da.GetData(1,ref p))return;
        var elements=BridgeConnections.Create(current,rigid,p?.Value);lines.AddRange(current);
        da.SetDataList(0,elements.Select(e=>e is NodeModel n?(IGH_Goo)new GH_Node(n):new GH_Beam((BeamModel)e)));
        da.SetData(1,rigid?"Native spatial kinematic rigid links.":"Deformable connection beams with explicit property.");
    }
    public override BoundingBox ClippingBox=>new BoundingBox(lines.SelectMany(l=>new[]{l.From,l.To}));
    public override void DrawViewportWires(IGH_PreviewArgs args){foreach(var l in lines)args.Display.DrawLine(l,Attributes.Selected?args.WireColour_Selected:Color.MediumPurple,3);}
}
public sealed class BridgeRigidLinksComponent : BridgeConnectionComponent
{public BridgeRigidLinksComponent():base(true){}public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f014");}
public sealed class BridgeConnectionBeamsComponent : BridgeConnectionComponent
{public BridgeConnectionBeamsComponent():base(false){}public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f015");}
public sealed class BridgeBeamOffsetsComponent : BridgeUtilityComponent
{
    public BridgeBeamOffsetsComponent():base("Bridge Beam Offsets","BeamOffsets","Set local section offsets on copied beams. Clears results; reference axes stay unchanged.",CategoryNameConstants.SUBCATEGORY_ATTRIBUTES){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f016");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddGenericParameter("Beams","B","Beams to copy and modify.",GH_ParamAccess.list);p.AddVectorParameter("Local Offset","O","Local section x/y offsets, mm. Z must be zero. Sets the offset, does not increment it.",GH_ParamAccess.item,Vector3d.Zero);}
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {p.AddGenericParameter("Beams","B","Modified beams with cleared results.",GH_ParamAccess.list);p.AddTextParameter("Information","I","Offset convention.",GH_ParamAccess.item);}
    protected override void Solve(IGH_DataAccess da)
    {var beams=new List<GH_Beam>();var o=Vector3d.Zero;if(!da.GetDataList(0,beams))return;da.GetData(1,ref o);if(o.Z!=0)throw new ArgumentException("Local offset Z must be zero.");da.SetDataList(0,BridgeConnections.Offset(beams.Select(b=>b.Value).ToArray(),new(o.X,o.Y)).Select(b=>new GH_Beam(b)));da.SetData(1,"Local section offsets assigned; source beams unchanged, results cleared.");}
}
