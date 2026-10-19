using System;
using Grasshopper.Kernel;
using BridGHe.Grasshopper.Helpers;

namespace BridGHe.Grasshopper.Components.Utilities;

public abstract class BridgeUtilityComponent : GH_Component
{
    protected BridgeUtilityComponent(string name, string nick, string description, string category)
        : base(name, nick, description, CategoryNameConstants.CATEGORY_MODEL, category) { }
    protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
    protected sealed override void SolveInstance(IGH_DataAccess da)
    {
        try { Solve(da); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException)
        { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
    protected abstract void Solve(IGH_DataAccess da);
}
