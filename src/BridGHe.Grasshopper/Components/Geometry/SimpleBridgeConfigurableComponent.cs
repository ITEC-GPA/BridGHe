using System;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class SimpleBridgeConfigurableComponent : SimpleBridgeComponent
{
    public SimpleBridgeConfigurableComponent() : base(false,"Simple Bridge","SimpleBridge") { }
    public override Guid ComponentGuid => new Guid("229b4f95-4bc6-43d4-8f58-e4faea8d0b34");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        base.RegisterInputParams(p);
        p.AddBooleanParameter("Truss Diaphragms","TR","False: solid-web diaphragms. True: truss diaphragms using Bridge Diaphragms layout. Geometry only.",GH_ParamAccess.item,false);
    }
    protected override bool TrussMode(IGH_DataAccess da)
    { bool truss=false; da.GetData(16,ref truss); return truss; }
}
