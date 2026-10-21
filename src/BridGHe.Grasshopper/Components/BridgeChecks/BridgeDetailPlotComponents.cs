using System;
using System.Collections.Generic;
using BridGHe.Common.Checks;
using Grasshopper.Kernel.Data;
namespace BridGHe.Grasshopper.Components.BridgeChecks;
public sealed class BridgeShearPlotComponent:BridgePlotComponent
{
    public BridgeShearPlotComponent():base("Bridge Shear Plot","TauPlot","Vector or PNG diagrams of native gPC shear stresses and resistance.") { }
    public override Guid ComponentGuid=>new Guid("c561aa2c-a635-4a3e-8eb0-b8a166d856fb");
    protected override string[] Quantities=>new[]{"V","Average τ","Maximum τ","Equivalent stress","Web resistance"};
    protected override string Unit(int q)=>q==0||q==4?"kN":"MPa";
    protected override double Value(object result,int q)
    {
        if(result is not BridgeStressResult r||r.Stage.Shear==null)throw new ArgumentException("Expected a gPC shear result.");var s=r.Stage.Shear;
        return q switch{0=>s.V,1=>s.TauAverage,2=>s.TauMaximum,3=>s.EquivalentStressEnvelope,4=>s.Web.Resistance/1000,_=>throw new ArgumentOutOfRangeException(nameof(q))};
    }
}
public sealed class BridgeStudPlotComponent:BridgePlotComponent
{
    public BridgeStudPlotComponent():base("Bridge Stud Plot","StudPlot","Vector or PNG diagrams of native gPC connection flow and stud checks.") { }
    public override Guid ComponentGuid=>new Guid("0394f8d3-e5cc-4d03-9ee3-9ce6420b7a37");
    protected override string[] Quantities=>new[]{"Connection flow","Force per stud","Resistance per stud","Resistance per length","Stud force ratio","Stud demand / resistance","Flow demand / resistance"};
    protected override string Unit(int q)=>q==0||q==3||q==6?"N/mm":q==4?"WR":"kN";
    protected override string PrimaryLabel(int q)=>q>=5?" / Ed":"";
    protected override string LimitLabel(int q)=>q>=5?" / Rd":null;
    protected override IEnumerable<double> FixedLevels(int q)=>q==4?new[]{1.0}:Array.Empty<double>();
    protected override Reading ReadValue(object result,int q,GH_Path path,string combination)
    {
        if(q<5)return base.ReadValue(result,q,path,combination);
        if(result is not BridgeStressResult r||r.Stage.Studs==null)throw new ArgumentException("Expected a gPC stud result.");
        var s=r.Stage.Studs;if(!s.Enabled)return new(double.NaN);
        // The total longitudinal flow does not include the extra torsional demand on the most loaded box flange.
        // Use the native per-stud comparison for that case instead of displaying an unsafe flow comparison.
        if(q==6 && r.Stage.Torsion?.SlabFlow is double flow && flow!=0)
            throw new ArgumentException("For a box with torsional slab flow select Stud demand / resistance: the total flow alone is not the governing flange demand.");
        return q==5?new(s.ForcePerStud,s.ResistancePerStud):new(Math.Abs(s.Flow),s.ResistancePerLength);
    }
    protected override double Value(object result,int q)
    {
        if(result is not BridgeStressResult r||r.Stage.Studs==null)throw new ArgumentException("Expected a gPC stud result.");var s=r.Stage.Studs;
        if(q>0&&!s.Enabled)return double.NaN;
        return q switch{0=>s.Flow,1=>s.ForcePerStud,2=>s.ResistancePerStud,3=>s.ResistancePerLength,4=>s.ResistancePerStud>0?s.ForcePerStud/s.ResistancePerStud:double.NaN,_=>throw new ArgumentOutOfRangeException(nameof(q))};
    }
}
