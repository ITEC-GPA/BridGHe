using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.MovingLoads;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeMovingLanesComponent : BridgeUtilityComponent
{
    public BridgeMovingLanesComponent() : base("Bridge Moving Lanes", "MovingLanes", "Create independent straight moving paths from a prototype and lateral offsets. Preserves the vehicle/template; defines no simultaneous-lane factors or normative arrangement automatically.", CategoryNameConstants.SUBCATEGORY_LOADS) { }
    public override Guid ComponentGuid => new Guid("7a067caf-afd7-466d-869d-0d3cb77ce0b1");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Prototype", "P", "Straight Moving Load Path. Template, target, divisions and static-case option are cloned.", GH_ParamAccess.item);
        p.AddNumberParameter("Offsets", "Y", "Signed offsets in model length units, towards the third prototype point projected normal to its axis.", GH_ParamAccess.list);
        p.AddTextParameter("Cases", "LC", "One distinct load-case name per path.", GH_ParamAccess.list);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) => p.AddGenericParameter("Paths", "P", "Connect the list to Attach Moving Paths. Preview follows the shifted paths.", GH_ParamAccess.list);
    protected override void Solve(IGH_DataAccess da)
    {
        MovingGoo prototype = null; var offsets = new List<double>(); var names = new List<string>();
        if (!da.GetData(0, ref prototype) || !da.GetDataList(1, offsets) || !da.GetDataList(2, names)) return;
        if (prototype.Value is not MovingPath path) throw new ArgumentException("Expected Moving Load Path.");
        da.SetDataList(0, BridgeMovingLanes.Create(path, offsets, names).Select(p => new MovingGoo(p)));
    }
}
