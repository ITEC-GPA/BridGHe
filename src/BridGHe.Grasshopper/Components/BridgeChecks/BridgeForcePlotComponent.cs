using System;
using GPC.Model.Results;
namespace BridGHe.Grasshopper.Components.BridgeChecks;
public sealed class BridgeForcePlotComponent:BridgePlotComponent
{
    public BridgeForcePlotComponent():base("Bridge Force Plot","ForcePlot","Vector or PNG diagram of Straus section forces.") { }
    protected override string[] Quantities=>new[]{"N","Vx","Vy","T","Mx","My"};
    protected override string Unit(int quantity)=>quantity<3?"kN":"kNm";
    protected override double Value(object result,int quantity)
    {
        if(result is not ResultBeamForces f)throw new ArgumentException("Expected ResultBeamForces.");
        return quantity switch{0=>f.N/1000,1=>f.V1/1000,2=>f.V2/1000,3=>f.T/1e6,4=>f.M1/1e6,5=>f.M2/1e6,_=>throw new ArgumentOutOfRangeException(nameof(quantity))};
    }
    public override Guid ComponentGuid=>new Guid("77e9f533-e4e8-41de-baaf-6bfece84def9");
}
