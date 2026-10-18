using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Grasshopper.DataTypes;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.BridgeDetails
{
    /// <summary>One explicit port per scalar gPC option. Input order is fixed by Fields.</summary>
    public abstract class DetailDefinition<T> : GH_Component where T:new()
    {
        protected abstract (string Name,object Default,string Description)[] Fields { get; }
        protected DetailDefinition(string name,string nick,string description):base(name,nick,description,"BridGHe-Checks","Bridge details") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            foreach(var f in Fields)
            {
                Type t=typeof(T).GetProperty(f.Name).PropertyType;
                if(t==typeof(bool))p.AddBooleanParameter(f.Name,f.Name,f.Description,GH_ParamAccess.item,(bool)f.Default);
                else if(t.IsEnum)
                {
                    p.AddIntegerParameter(f.Name,f.Name,f.Description,GH_ParamAccess.item,(int)f.Default);
                    var q=(Param_Integer)p[p.ParamCount-1];foreach(var e in Enum.GetValues(t))q.AddNamedValue(e.ToString(),Convert.ToInt32(e));
                }
                else p.AddNumberParameter(f.Name,f.Name,f.Description,GH_ParamAccess.item,Convert.ToDouble(f.Default));
            }
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddGenericParameter("Detail","D","Immutable gPC detail definition",GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var value=new T();int i=0;
                foreach(var f in Fields)
                {
                    var property=typeof(T).GetProperty(f.Name);object v;
                    if(property.PropertyType==typeof(bool)){bool b=false;if(!da.GetData(i++,ref b))return;v=b;}
                    else if(property.PropertyType.IsEnum){int e=0;if(!da.GetData(i++,ref e))return;if(!Enum.IsDefined(property.PropertyType,e))throw new ArgumentException("Invalid "+f.Name);v=Enum.ToObject(property.PropertyType,e);}
                    else {double x=0;if(!da.GetData(i++,ref x))return;if(!double.IsFinite(x))throw new ArgumentException("Non-finite "+f.Name);v=x;}
                    property.SetValue(value,v);
                }
                if(value is BridgeStudOptions studs && studs.Enabled && (studs.CountPerRow<1||studs.CountPerRow>20||studs.CountPerRow!=Math.Truncate(studs.CountPerRow)))
                    throw new ArgumentException("CountPerRow must be an integer from 1 to 20.");
                da.SetData(0,new BridgeDetailGoo(value));
            }
            catch(Exception e){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,e.Message);}
        }
        protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
    }
    public sealed class BridgeStudsComponent:DetailDefinition<BridgeStudOptions>
    {
        public BridgeStudsComponent():base("Bridge Studs","Studs","Defines headed studs using gPC. Dimensions mm, strength MPa. Connect to Bridge Detail Check.") { }
        public override Guid ComponentGuid=>new Guid("a65b8556-6a36-4c53-b52e-434d747c576e");
        protected override (string,object,string)[] Fields=>new (string,object,string)[]{
            ("Enabled",true,"Enable studs"),("CountPerRow",2.0,"Integer studs per transverse row, 1 to 20"),("Diameter",22.0,"Shank diameter [mm]"),
            ("Height",180.0,"Total height [mm]"),("LongitudinalPitch",200.0,"Longitudinal pitch [mm]"),("TransversePitch",150.0,"Transverse pitch [mm]"),
            ("Fu",450.0,"Stud ultimate tensile strength [MPa]"),("GammaV",1.25,"Connection partial factor; select for applicable standard"),
            ("HeadDiameter",35.0,"Head diameter [mm]"),("HeadThickness",10.0,"Head thickness [mm]"),("RequiredCover",40.0,"Required cover [mm]"),
            ("RepeatedActionDetail",true,"Detailing for repeated actions; this does not enable fatigue verification")};
    }
    public sealed class BridgeIntermediateStiffenerComponent:DetailDefinition<BridgeIntermediateStiffener>
    {
        public BridgeIntermediateStiffenerComponent():base("Bridge Intermediate Stiffener","IntStiff","Intermediate web stiffener and adjacent panels, gPC. mm / kN.") { }
        public override Guid ComponentGuid=>new Guid("ea6bf9ee-ddc5-4b3e-8409-74d5674d2732");
        protected override (string,object,string)[] Fields=>new (string,object,string)[]{
            ("Enabled",true,"Enable intermediate stiffener"),("Layout",0,"Symmetric / left / right / unequal"),("Width",200.0,"Left plate outstand [mm]"),("Thickness",20.0,"Left plate thickness [mm]"),
            ("RightWidth",200.0,"Right outstand, unequal layout [mm]"),("RightThickness",20.0,"Right thickness, unequal layout [mm]"),
            ("LeftPanel",4000.0,"Left panel length [mm]"),("RightPanel",4000.0,"Right panel length [mm]"),("EqualPanels",true,"Use left length for both panels"),
            ("LengthFactor",1.0,"Buckling length factor"),("ExternalCompressionKN",0.0,"Additional compression [kN], not N"),("LoadX",0.0,"Load transverse eccentricity [mm]"),
            ("CheckWelds",true,"Check continuous welds"),("WeldThroat",6.0,"Fillet weld throat [mm]")};
    }
    public sealed class BridgeSupportStiffenerComponent:DetailDefinition<BridgeSupportStiffener>
    {
        public BridgeSupportStiffenerComponent():base("Bridge Support Stiffener","SupStiff","Bearing stiffener, footprint and end post, gPC. Assign actual design reaction in kN.") { }
        public override Guid ComponentGuid=>new Guid("bb5073af-270d-4603-b7f5-6bf65fcd6aab");
        protected override (string,object,string)[] Fields=>new (string,object,string)[]{
            ("Enabled",true,"Enable bearing stiffener"),("Layout",0,"Symmetric / left / right / unequal"),("Width",250.0,"Left plate outstand [mm]"),("Thickness",25.0,"Left plate thickness [mm]"),
            ("RightWidth",250.0,"Right outstand, unequal layout [mm]"),("RightThickness",25.0,"Right thickness, unequal layout [mm]"),
            ("Location",0,"Internal / left end / right end"),("LeftPanel",4000.0,"Left panel length [mm]"),("RightPanel",4000.0,"Right panel length [mm]"),
            ("ReactionKN",0.0,"Actual compression reaction envelope [kN], not N; zero means no external bearing compression"),
            ("EndDistance",250.0,"Distance from end [mm]"),("LoadX",0.0,"Transverse eccentricity [mm]"),("LoadZ",0.0,"Longitudinal eccentricity [mm]"),
            ("FootprintLength",400.0,"Bearing footprint longitudinal length [mm]"),("FootprintWidth",600.0,"Bearing footprint transverse width [mm]"),
            ("LengthFactor",1.0,"Buckling length factor"),("RigidEndPost",false,"Enable rigid end-post checks"),("EndPostSpacing",300.0,"Two end-post pairs spacing [mm]"),
            ("CheckWelds",true,"Check continuous welds"),("WeldThroat",8.0,"Fillet weld throat [mm]")};
    }
    public sealed class BridgeDetailCheckComponent:GH_Component
    {
        public BridgeDetailCheckComponent():base("Bridge Detail Check","DetailCheck","Recomputes a BridgeChecks section with gPC studs and stiffeners. Retains phase history, materials and limit state. No FEM stiffness change.","BridGHe-Checks","Bridge details") { }
        public override Guid ComponentGuid=>new Guid("ffad5404-7440-47e7-9824-90588810089f");
        protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Section result","R","BridgeStressResult from BridgeChecks; the selected stage is retained",GH_ParamAccess.item);
            p.AddGenericParameter("Studs","S","Bridge Studs definition",GH_ParamAccess.item);p[1].Optional=true;
            p.AddGenericParameter("Intermediate stiffener","I","Bridge Intermediate Stiffener definition",GH_ParamAccess.item);p[2].Optional=true;
            p.AddGenericParameter("Support stiffener","A","Bridge Support Stiffener definition",GH_ParamAccess.item);p[3].Optional=true;
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Section result","R","Updated gPC stress result, including Studs and Shear details",GH_ParamAccess.item);
            p.AddTextParameter("Check","C","gPC check name",GH_ParamAccess.list);p.AddNumberParameter("Demand","D","Demand, units in U",GH_ParamAccess.list);
            p.AddNumberParameter("Resistance","R","Resistance or limit, units in U",GH_ParamAccess.list);p.AddTextParameter("Units","U","gPC units",GH_ParamAccess.list);
            p.AddNumberParameter("Ratio","Eta","Demand/resistance; NaN where gPC requires a separate verification",GH_ParamAccess.list);
            p.AddTextParameter("Status","S","Status and explanation, including limit state scope",GH_ParamAccess.list);p.AddTextParameter("Warnings","W","gPC warnings",GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                GH_ObjectWrapper wrapper=null;if(!da.GetData(0,ref wrapper))return;
                if(wrapper.Value is not BridgeStressResult result)throw new ArgumentException("Expected a BridgeChecks BridgeStressResult.");
                var input=result.Analysis.Input;
                for(int i=1;i<=3;i++)
                {
                    if(Params.Input[i].VolatileDataCount==0)continue;
                    BridgeDetailGoo detail=null;if(!da.GetData(i,ref detail)||detail==null)throw new ArgumentException("Expected a gPC detail definition at input "+i);
                    input=i switch {
                        1 when detail.Value is BridgeStudOptions s=>input with{Studs=s},
                        2 when detail.Value is BridgeIntermediateStiffener s=>input with{Intermediate=s},
                        3 when detail.Value is BridgeSupportStiffener s=>input with{Support=s},
                        _=>throw new ArgumentException("Wrong gPC detail type at input "+i)};
                }
                int stage=result.Analysis.Stages.IndexOf(result.Stage);if(stage<0)throw new ArgumentException("Unknown source stage.");
                var analysis=HBridgeSection.Calculate(input);var updated=new BridgeStressResult(analysis,stage);var selected=updated.Stage;
                var checks=(selected.Shear?.Checks??new List<BridgeLocalCheck>()).Concat(selected.Studs?.Checks??new List<BridgeLocalCheck>()).ToArray();
                da.SetData(0,updated);da.SetDataList(1,checks.Select(c=>c.Name));da.SetDataList(2,checks.Select(c=>c.Demand));da.SetDataList(3,checks.Select(c=>c.Resistance));
                da.SetDataList(4,checks.Select(c=>c.Unit));da.SetDataList(5,checks.Select(c=>c.Ratio??double.NaN));da.SetDataList(6,checks.Select(c=>c.Status+": "+c.Note));da.SetDataList(7,selected.Warnings);
                if(input.Options.LimitState!=BridgeLimitState.Ultimate)AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,"Stato limite del risultato sorgente SLE: gPC lascia senza esito le verifiche che richiedono azioni SLU. Nessun fattore di carico è applicato implicitamente.");
            }
            catch(Exception e){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,e.Message);}
        }
    }
}
