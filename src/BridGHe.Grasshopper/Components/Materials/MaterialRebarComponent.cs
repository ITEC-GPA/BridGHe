using System;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Materials;

public sealed class MaterialRebarComponent : BridgeUtilityComponent
{
    public MaterialRebarComponent() : base("Reinforcement Steel B450C", "B450C",
        "Reinforcing steel using GPC SteelMaterialEN1992. Characteristic properties, N-mm-MPa; connect Material to the composite property's reinforcement input.",
        CategoryNameConstants.SUBCATEGORY_MATERIALS) { }
    protected override void RegisterInputParams(GH_InputParamManager p) =>
        p.AddTextParameter("Name", "N", "Reinforcement material name.", GH_ParamAccess.item, "B450C");
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Material", "M", "B450C reinforcing steel, compatible with composite properties and Straus beam properties.", GH_ParamAccess.item);
        p.AddTextParameter("Information", "I", "Characteristic properties and modelling conventions.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        string name = "B450C"; if (!da.GetData(0, ref name)) return;
        da.SetData(0, new GH_Material(ReinforcementMaterials.B450C(name)));
        da.SetDataList(1, new[] { "B450C | E=200000 MPa | fyk=450 MPa | ftk=540 MPa | epsilon_uk=0.075",
            "nu=0.30 | density=7.85e-9 t/mm3 | alpha=1.2e-5 /K",
            "GPC elastic-perfectly-plastic reinforcement law. Characteristic strengths: safety factors belong to checks, not the FEM material.",
            "Expected strengths equal characteristic values; no statistical overstrength assumed. Reinforcement area and position are defined separately in the slab." });
        Message = "B450C";
    }
    public override Guid ComponentGuid => new Guid("91f4df3c-8079-4324-a368-4ea1ea3dd13f");
}
