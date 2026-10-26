using System;
using BridGHe.Common.Helpers;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components;

public sealed class GetBeamPropertyComponent : BridgeUtilityComponent
{
    public GetBeamPropertyComponent() : base("Get Beam Property", "GetProperty",
        "Extract an independent copy of the complete Beam Property assigned to a beam. Lists/trees are processed item by item; name, material, section and composite preview data are retained.",
        CategoryNameConstants.SUBCATEGORY_TOOLS) { }
    public override Guid ComponentGuid => new Guid("5b890d9d-1629-4591-a02c-07c507f88252");
    protected override void RegisterInputParams(GH_InputParamManager p) =>
        p.AddGenericParameter("Beam", "B", "Beam whose assigned property is required.", GH_ParamAccess.item);
    protected override void RegisterOutputParams(GH_OutputParamManager p) =>
        p.AddGenericParameter("Property", "P", "Independent copy of the complete Beam Property. Connect to Beam Property Override or other property inputs.", GH_ParamAccess.item);
    protected override void Solve(IGH_DataAccess da)
    {
        GH_Beam beam = null;
        if (!da.GetData(0, ref beam)) return;
        if (beam?.Value?.BeamProperty?.IsValid != true) throw new ArgumentException("The beam must have a valid assigned Beam Property.");
        da.SetData(0, new GH_BeamProperty(ModelArchive.Clone(beam.Value.BeamProperty)));
    }
}
