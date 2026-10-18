using System;
using BridGHe.Common.Checks;
namespace BridGHe.Grasshopper.Components.BridgeChecks;
public sealed class BridgeResultPlotComponent:BridgePlotComponent
{
    public BridgeResultPlotComponent():base("Bridge Stress Plot","StressPlot","Vector or PNG diagram of gPC stresses and strains.") { }
    protected override string[] Quantities=>new[]{"σ1 concrete","σ2 rebar","σ4 concrete","σ5 steel","σ6 steel","σ7 steel","σ8 steel","τ6","τ7","σ6 equivalent","σ7 equivalent","ε1 concrete","ε8 steel"};
    protected override bool ReferenceLines=>true;
    protected override string Unit(int quantity)=>quantity>=11?"strain":"MPa";
    protected override double Value(object result,int quantity)
    {
        if(result is not BridgeStressResult r)throw new ArgumentException("Expected BridgeStressResult.");
        return quantity switch{0=>r.Point1_ConcreteSigma,1=>r.Point2_RebarSigma,2=>r.Point4_ConcreteSigma,3=>r.Point5_SteelSigma,4=>r.Point6_SteelSigma,5=>r.Point7_SteelSigma,6=>r.Point8_SteelSigma,7=>r.Point6_SteelTau,8=>r.Point7_SteelTau,9=>r.Point6_SteelSigmaId,10=>r.Point7_SteelSigmaId,11=>r.Point1_ConcreteEpsilon,12=>r.Point8_SteelEpsilon,_=>throw new ArgumentOutOfRangeException(nameof(quantity))};
    }
    public override Guid ComponentGuid=>new Guid("8f53f404-9446-4b47-90e3-d514f9c547c9");
}
