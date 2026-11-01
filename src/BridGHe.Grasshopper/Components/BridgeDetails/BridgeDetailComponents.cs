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
        private (System.Reflection.PropertyInfo Property,object Default,string Description)[] metadata;
        private (System.Reflection.PropertyInfo Property,object Default,string Description)[] Metadata => metadata ??= Fields.Select(f =>
            (typeof(T).GetProperty(f.Name) ?? throw new InvalidOperationException("Missing gPC property: "+f.Name),f.Default,f.Description)).ToArray();
        protected DetailDefinition(string name,string nick,string description):base(name,nick,description,Helpers.CategoryNameConstants.CATEGORY_MODEL, Helpers.CategoryNameConstants.SUBCATEGORY_BRIDGE) { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            foreach(var f in Metadata)
            {
                Type t=f.Property.PropertyType;
                if(t==typeof(bool))p.AddBooleanParameter(f.Property.Name,f.Property.Name,f.Description,GH_ParamAccess.item,(bool)f.Default);
                else if(t==typeof(string))p.AddTextParameter(f.Property.Name,f.Property.Name,f.Description,GH_ParamAccess.item,(string)f.Default);
                else if(t.IsEnum)
                {
                    p.AddIntegerParameter(f.Property.Name,f.Property.Name,f.Description,GH_ParamAccess.item,(int)f.Default);
                    var q=(Param_Integer)p[p.ParamCount-1];foreach(var e in Enum.GetValues(t))q.AddNamedValue(e.ToString(),Convert.ToInt32(e));
                }
                else p.AddNumberParameter(f.Property.Name,f.Property.Name,f.Description,GH_ParamAccess.item,Convert.ToDouble(f.Default));
                p[p.ParamCount-1].Optional=true;
            }
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddGenericParameter("Detail","D","Immutable gPC detail definition",GH_ParamAccess.item);
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var value=new T();int i=0;
                foreach(var f in Metadata)
                {
                    var property=f.Property;object v;
                    if(property.PropertyType==typeof(bool)){bool b=(bool)f.Default;da.GetData(i++,ref b);v=b;}
                    else if(property.PropertyType.IsEnum){int e=Convert.ToInt32(f.Default);da.GetData(i++,ref e);if(!Enum.IsDefined(property.PropertyType,e))throw new ArgumentException("Invalid "+f.Property.Name);v=Enum.ToObject(property.PropertyType,e);}
                    else if(property.PropertyType==typeof(string)){string text=(string)f.Default;da.GetData(i++,ref text);v=text;}
                    else {double x=Convert.ToDouble(f.Default);da.GetData(i++,ref x);if(!double.IsFinite(x))throw new ArgumentException("Non-finite "+f.Property.Name);v=x;}
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
    /// <summary>
    /// Task capable: the first pass reads the inputs of every section and starts its gPC calculation in a task (the calculations are
    /// independent and thread-safe, as in Super Element Check), the second pass sets the outputs and the messages in the order of the
    /// sections. Without tasks the second pass calculates each section, as before.
    /// </summary>
    public sealed class BridgeDetailCheckComponent:GH_TaskCapableComponent<BridgeDetailCheckComponent.Solution>
    {
        /// <summary>The inputs of one section: the source result, the gPC input with the details and the stage to keep</summary>
        private sealed class Request
        {
            internal BridgeStressResult Result; internal HBridgeInput Input; internal int Stage;
        }
        /// <summary>The outputs of one section, or the message of its error</summary>
        public sealed class Solution
        {
            internal BridgeStressResult Updated; internal BridgeLocalCheck[] Checks; internal bool ServiceLimitState; internal string Error;
        }
        public BridgeDetailCheckComponent():base("Bridge Detail Check","DetailCheck","Recomputes a BridgeChecks section with gPC studs and stiffeners. Retains phase history, materials and limit state. No FEM stiffness change.",Helpers.CategoryNameConstants.CATEGORY_MODEL, Helpers.CategoryNameConstants.SUBCATEGORY_DESIGN) { }
        public override Guid ComponentGuid=>new Guid("ffad5404-7440-47e7-9824-90588810089f");
        protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Section result","R","BridgeStressResult from BridgeChecks; the selected stage is retained",GH_ParamAccess.item);
            p.AddGenericParameter("Studs","S","Bridge Studs definition",GH_ParamAccess.item);p[1].Optional=true;
            p.AddGenericParameter("Intermediate stiffener","I","Bridge Intermediate Stiffener definition",GH_ParamAccess.item);p[2].Optional=true;
            p.AddGenericParameter("Support stiffener","A","Bridge Support Stiffener definition",GH_ParamAccess.item);p[3].Optional=true;
            p.AddGenericParameter("Options","O","Optional native gPC analysis options; actions remain unchanged",GH_ParamAccess.item);p[4].Optional=true;
            p.AddGenericParameter("Additional details","D","Optional transverse slab reinforcement, fatigue or box options",GH_ParamAccess.list);p[5].Optional=true;
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
            if(InPreSolve)
            {
                // one task for every section, also without data or with an input error, so that the results keep the order of the sections;
                // the consecutive sections of a station (the combinations of a beam station) are calculated together in one gPC batch
                Request request=null;Solution failed=null;
                try{request=Read(da);}
                catch(Exception e){failed=new Solution{Error=e.Message};}
                if(request==null){TaskList.Add(System.Threading.Tasks.Task.FromResult(failed));return;}
                string station=request.Result.Source is { } source?source.BeamId+"|"+source.StationMm.ToString("R",System.Globalization.CultureInfo.InvariantCulture):null;
                if(pending.Count>0&&(station==null||station!=pendingStation))StartPending();
                var completion=new System.Threading.Tasks.TaskCompletionSource<Solution>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                pending.Add((request,completion));pendingStation=station;
                if(station==null)StartPending();
                TaskList.Add(completion.Task);
                return;
            }
            StartPending();
            if(!GetSolveResults(da,out var solution))
            {
                try{var request=Read(da);solution=request==null?null:Calculate(request);}
                catch(Exception e){solution=new Solution{Error=e.Message};}
            }
            if(solution==null)return;
            if(solution.Error!=null){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,solution.Error);return;}
            try
            {
                var updated=solution.Updated;var selected=updated.Stage;var checks=solution.Checks;
                da.SetData(0,updated);da.SetDataList(1,checks.Select(c=>c.Name));da.SetDataList(2,checks.Select(c=>c.Demand));da.SetDataList(3,checks.Select(c=>c.Resistance));
                da.SetDataList(4,checks.Select(c=>c.Unit));da.SetDataList(5,checks.Select(c=>c.Ratio??double.NaN));da.SetDataList(6,checks.Select(c=>c.Status+": "+c.Note));da.SetDataList(7,selected.Warnings.Concat(updated.Assumptions).Distinct());
                if(solution.ServiceLimitState)AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,"Stato limite del risultato sorgente SLE: gPC lascia senza esito le verifiche che richiedono azioni SLU. Nessun fattore di carico è applicato implicitamente.");
            }
            catch(Exception e){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,e.Message);}
        }
        /// <summary>The inputs of the current section (null: no section result); exceptions are the input errors</summary>
        private Request Read(IGH_DataAccess da)
        {
            GH_ObjectWrapper wrapper=null;if(!da.GetData(0,ref wrapper))return null;
            if(wrapper.Value is not BridgeStressResult result)throw new ArgumentException("Expected a BridgeChecks BridgeStressResult.");
            if(result.Stage.GetHistory()!=null)throw new NotSupportedException("Bridge Detail Check does not support LinearHistory results. Recomputing them with the cumulative solver would change their history; use a separately validated detail calculation.");
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
            BridgeDetailGoo options=null;
            if(da.GetData(4,ref options))input=options.Value is BridgeAnalysisOptions o?input with{Options=o}:throw new ArgumentException("Expected Bridge Analysis Options.");
            var extra=new List<BridgeDetailGoo>();da.GetDataList(5,extra);
            if(extra.Any(d=>d.Value is not BridgeTransverseReinforcement && d.Value is not BridgeFatigueOptions && d.Value is not BridgeBoxOptions))
                throw new ArgumentException("Additional details accepts transverse reinforcement, fatigue and box options only.");
            input=BridgeGpc.WithDetails(input,extra.Select(d=>d.Value));
            int stage=result.Analysis.Stages.IndexOf(result.Stage);if(stage<0)throw new ArgumentException("Unknown source stage.");
            return new Request{Result=result,Input=input,Stage=stage};
        }
        /// <summary>The gPC calculation of a section, without Grasshopper data access; exceptions are the errors of the result. When only details,
        /// limit state or unused common elevation change, gPC keeps the converged phase situations of the source analysis and recomputes the
        /// stress points, shear, studs and details, with the numbers of a full calculation</summary>
        private static Solution Calculate(Request request)
        {
            try{return Finish(request,HBridgeSection.Recalculate(request.Result.Analysis,request.Input));}
            catch(Exception e){return new Solution{Error=e.Message};}
        }
        /// <summary>The sections of a station in one gPC batch (it runs in a task): each gets the outputs or the error of its own calculation</summary>
        private static Solution[] Calculate(Request[] requests)
        {
            IReadOnlyList<HBridgeBatchResult> analyses;
            try{analyses=HBridgeSection.Recalculate(requests.Select(r=>r.Result.Analysis).ToArray(),requests.Select(r=>r.Input).ToArray());}
            catch(Exception e){return requests.Select(_=>new Solution{Error=e.Message}).ToArray();}
            return requests.Select((request,k)=>
            {
                if(analyses[k].Error!=null)return new Solution{Error=analyses[k].Error.Message};
                try{return Finish(request,analyses[k].Analysis);}
                catch(Exception e){return new Solution{Error=e.Message};}
            }).ToArray();
        }
        private static Solution Finish(Request request,HBridgeAnalysisResult analysis)
        {
            var updated=new BridgeStressResult(analysis,request.Stage,request.Result.Source,request.Result.Assumptions);
            return new Solution{Updated=updated,Checks=BridgeGpc.Checks(updated.Stage).ToArray(),ServiceLimitState=request.Input.Options.LimitState!=BridgeLimitState.Ultimate};
        }
        /// <summary>The sections of the current station, waiting for the first one of another station (or for the second pass)</summary>
        private readonly List<(Request Request,System.Threading.Tasks.TaskCompletionSource<Solution> Completion)> pending=new();
        private string pendingStation;
        protected override void BeforeSolveInstance(){base.BeforeSolveInstance();pending.Clear();pendingStation=null;}
        /// <summary>Starts the batch of the pending sections in a task that completes the task of each of them</summary>
        private void StartPending()
        {
            if(pending.Count==0)return;
            var batch=pending.ToArray();pending.Clear();pendingStation=null;
            var cancel=CancelToken;
            System.Threading.Tasks.Task.Run(()=>
            {
                if(cancel.IsCancellationRequested){foreach(var b in batch)b.Completion.TrySetCanceled();return;}
                Solution[] solutions;
                try{solutions=Calculate(batch.Select(b=>b.Request).ToArray());}
                catch(Exception e){solutions=batch.Select(_=>new Solution{Error=e.Message}).ToArray();}
                for(int k=0;k<batch.Length;k++)batch[k].Completion.TrySetResult(solutions[k]);
            });
        }
    }
}
