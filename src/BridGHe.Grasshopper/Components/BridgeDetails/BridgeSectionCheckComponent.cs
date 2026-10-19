using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.BridgeDetails;

public sealed class BridgeSectionCheckComponent:GH_Component
{
    public BridgeSectionCheckComponent():base("Bridge Section Check","BridgeCheck","Native gPC staged composite section analysis. Geometry mm, materials MPa, actions kN/kNm. Actions must already include the selected combination factors.","BridGHe-Checks","Bridge details") { }
    public override Guid ComponentGuid=>new Guid("952a074e-823d-490f-87b9-79c95b5d2d1a");
    protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Geometry","G","Bridge Section Geometry",GH_ParamAccess.item);
        p.AddGenericParameter("Concrete","C","BridGHe concrete material; compressive strength MPa",GH_ParamAccess.item);
        p.AddGenericParameter("Steel","S","BridGHe structural steel material; E and strengths MPa",GH_ParamAccess.item);
        p.AddGenericParameter("Rebar material","R","Required if either reinforcement row is enabled",GH_ParamAccess.item);p[3].Optional=true;
        p.AddGenericParameter("Phases","P","Chronological Bridge Phase definitions; increments, not cumulative totals",GH_ParamAccess.list);
        p.AddGenericParameter("Top rebars","RT","Optional upper longitudinal reinforcement row",GH_ParamAccess.item);p[5].Optional=true;
        p.AddGenericParameter("Bottom rebars","RB","Optional lower longitudinal reinforcement row",GH_ParamAccess.item);p[6].Optional=true;
        p.AddGenericParameter("Details","D","Studs, stiffeners, transverse reinforcement, fatigue and box options; at most one per type",GH_ParamAccess.list);p[7].Optional=true;
        p.AddGenericParameter("Options","O","Bridge Analysis Options; default Eurocode4, rare SLS, native gPC coefficients",GH_ParamAccess.item);p[8].Optional=true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Results","R","Cumulative result per active phase, compatible with shear/stud result components",GH_ParamAccess.list);
        p.AddTextParameter("Phases","P","Active phase names in result order",GH_ParamAccess.list);
        p.AddTextParameter("Warnings","W","gPC scope and warnings; includes checks requiring separate action data",GH_ParamAccess.list);
        p.AddGenericParameter("Native analysis","A","Complete gPC HBridgeAnalysisResult",GH_ParamAccess.item);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        try
        {
            BridgeDetailGoo geometry=null,top=null,bottom=null,options=null;
            GH_Material concrete=null,steel=null,rebar=null;var phases=new List<BridgeDetailGoo>();var details=new List<BridgeDetailGoo>();
            if(!da.GetData(0,ref geometry)||!da.GetData(1,ref concrete)||!da.GetData(2,ref steel)||!da.GetDataList(4,phases))return;
            da.GetData(3,ref rebar);da.GetData(5,ref top);da.GetData(6,ref bottom);da.GetDataList(7,details);da.GetData(8,ref options);
            T Definition<T>(BridgeDetailGoo goo,T fallback) where T:class => goo==null?fallback:goo.Value as T??throw new ArgumentException("Expected "+typeof(T).Name);
            var input=new HBridgeInput
            {
                Geometry=Definition<HSectionDimensions>(geometry,null)??throw new ArgumentException("Geometry required."),
                TopRebars=Definition(top,new BridgeRebarRow()),BottomRebars=Definition(bottom,new BridgeRebarRow()),
                Options=Definition(options,new BridgeAnalysisOptions{Standard=BridgeStandard.Eurocode4,LimitState=BridgeLimitState.Rare}),
                Phases=phases.Select(p=>Definition<BridgePhase>(p,null)??throw new ArgumentException("Phase required.")).Where(p=>p.Active).ToArray(),
                Materials=BridgeGpc.Materials(concrete.Value,steel.Value,rebar?.Value)
            };
            if((input.TopRebars.Enabled||input.BottomRebars.Enabled)&&rebar?.Value==null)throw new ArgumentException("Reinforcement material required for enabled reinforcement rows.");
            input=BridgeGpc.WithDetails(input,details.Select(d=>d.Value));
            var result=HBridgeSection.Calculate(input);
            da.SetDataList(0,Enumerable.Range(0,result.Stages.Count).Select(i=>new BridgeStressResult(result,i)));
            da.SetDataList(1,result.Stages.Select(s=>s.Name));
            da.SetDataList(2,result.Stages.SelectMany(s=>s.Warnings.Select(w=>s.Name+": "+w)).Prepend(result.Scope));
            da.SetData(3,result);
        }
        catch(Exception e){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,e.Message);}
    }
}
