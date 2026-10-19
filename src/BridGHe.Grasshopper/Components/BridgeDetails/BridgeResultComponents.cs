using System;
using System.Linq;
using BridGHe.Common.Checks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
namespace BridGHe.Grasshopper.Components.BridgeDetails;

public abstract class BridgeResultComponent:GH_Component
{
    protected BridgeResultComponent(string name,string nick,string description):base(name,nick,description,"BridGHe-Checks","Bridge results") { }
    protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
    protected override void RegisterInputParams(GH_InputParamManager p)=>p.AddGenericParameter("Section result","R","One cumulative BridgeStressResult from Section Check, Super Element Check or Detail Check",GH_ParamAccess.item);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        try
        {
            GH_ObjectWrapper wrapper=null;if(!da.GetData(0,ref wrapper))return;
            if(wrapper.Value is not BridgeStressResult result)throw new ArgumentException("Expected BridgeStressResult.");
            Write(da,result);
        }
        catch(Exception e){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,e.Message);}
    }
    protected abstract void Write(IGH_DataAccess da,BridgeStressResult result);
}
public sealed class BridgeShearResultsComponent:BridgeResultComponent
{
    public BridgeShearResultsComponent():base("Bridge Shear Results","Tau","Native gPC shear stresses and web resistance. Full shear, buckling and interaction statuses are available through Bridge Verification Results.") { }
    public override Guid ComponentGuid=>new Guid("e73670ca-0481-4716-9cc9-d42b57804614");
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("Shear","V","Cumulative shear [kN]",GH_ParamAccess.item);
        p.AddNumberParameter("Average tau","TauAvg","Native gPC average shear stress [MPa]",GH_ParamAccess.item);
        p.AddNumberParameter("Maximum tau","TauMax","Native gPC maximum shear stress [MPa]",GH_ParamAccess.item);
        p.AddNumberParameter("Equivalent stress","SigmaEq","Native gPC equivalent stress envelope [MPa]",GH_ParamAccess.item);
        p.AddNumberParameter("Web resistance","VRd","Native gPC web shear resistance [kN]; selected partial factors and buckling apply",GH_ParamAccess.item);
        p.AddTextParameter("Warnings","W","Stage warnings",GH_ParamAccess.list);
    }
    protected override void Write(IGH_DataAccess da,BridgeStressResult r)
    {
        var s=r.Stage.Shear??throw new ArgumentException("Shear result unavailable.");
        da.SetData(0,s.V);da.SetData(1,s.TauAverage);da.SetData(2,s.TauMaximum);da.SetData(3,s.EquivalentStressEnvelope);da.SetData(4,s.Web.Resistance/1000);da.SetDataList(5,r.Stage.Warnings);
    }
}
public sealed class BridgeStudResultsComponent:BridgeResultComponent
{
    public BridgeStudResultsComponent():base("Bridge Stud Results","StudResults","Native gPC connection flow, stud force and resistance. Includes phase contributions; disabled stud checks return NaN resistances.") { }
    public override Guid ComponentGuid=>new Guid("95046592-063b-4499-8191-1c8cf7ec21a1");
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("Connection flow","q","Total longitudinal connection flow [N/mm = kN/m]",GH_ParamAccess.item);
        p.AddNumberParameter("Force per stud","PEd","Demand per stud [kN]",GH_ParamAccess.item);
        p.AddNumberParameter("Resistance per stud","PRd","Resistance per stud for the selected limit state [kN]",GH_ParamAccess.item);
        p.AddNumberParameter("Resistance per length","qRd","Resistance per unit length [N/mm = kN/m]",GH_ParamAccess.item);
        p.AddNumberParameter("Force ratio","Eta","PEd/PRd; separate geometry, fatigue and slab checks remain in Verification Results",GH_ParamAccess.item);
        p.AddTextParameter("Phase","P","Names corresponding to the following flow contributions",GH_ParamAccess.list);
        p.AddNumberParameter("Phase flow","dq","Flow contribution including additional connection flow [N/mm]",GH_ParamAccess.list);
        p.AddTextParameter("Warnings","W","Stage warnings",GH_ParamAccess.list);
    }
    protected override void Write(IGH_DataAccess da,BridgeStressResult r)
    {
        var s=r.Stage.Studs??throw new ArgumentException("Connection result unavailable.");
        da.SetData(0,s.Flow);da.SetData(1,s.Enabled?s.ForcePerStud:double.NaN);da.SetData(2,s.Enabled?s.ResistancePerStud:double.NaN);
        da.SetData(3,s.Enabled?s.ResistancePerLength:double.NaN);da.SetData(4,s.Enabled&&s.ResistancePerStud>0?s.ForcePerStud/s.ResistancePerStud:double.NaN);
        da.SetDataList(5,s.Contributions.Select(c=>c.Phase));da.SetDataList(6,s.Contributions.Select(c=>c.Flow+c.AdditionalFlow));da.SetDataList(7,r.Stage.Warnings);
    }
}
public sealed class BridgeVerificationResultsComponent:BridgeResultComponent
{
    public BridgeVerificationResultsComponent():base("Bridge Verification Results","Verifications","Native gPC checks for web, stiffeners, studs, slab shear, fatigue, box torsion and diaphragms. Null ratios mean a separate verification is required.") { }
    public override Guid ComponentGuid=>new Guid("7c15b6ee-252b-4bdb-b0f2-7b765be6adf8");
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Check","C","gPC check name",GH_ParamAccess.list);p.AddNumberParameter("Demand","D","Demand, units in U",GH_ParamAccess.list);
        p.AddNumberParameter("Resistance","R","Resistance or limit, units in U",GH_ParamAccess.list);p.AddTextParameter("Units","U","gPC units",GH_ParamAccess.list);
        p.AddNumberParameter("Ratio","Eta","NaN means separate verification required",GH_ParamAccess.list);p.AddTextParameter("Status","S","gPC status",GH_ParamAccess.list);
        p.AddTextParameter("Notes","N","Scope and calculation notes from gPC",GH_ParamAccess.list);
    }
    protected override void Write(IGH_DataAccess da,BridgeStressResult r)
    {
        var checks=BridgeGpc.Checks(r.Stage).ToArray();da.SetDataList(0,checks.Select(c=>c.Name));da.SetDataList(1,checks.Select(c=>c.Demand));
        da.SetDataList(2,checks.Select(c=>c.Resistance));da.SetDataList(3,checks.Select(c=>c.Unit));da.SetDataList(4,checks.Select(c=>c.Ratio??double.NaN));da.SetDataList(5,checks.Select(c=>c.Status));da.SetDataList(6,checks.Select(c=>c.Note));
    }
}
