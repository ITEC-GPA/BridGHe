using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

/// <summary>Example 50 extends a read-only copy of 49 with four independent loaded FEM models.</summary>
internal sealed partial class ParametricBridgePhasesExample : IDisposable
{
    internal const string Id="50-ponte-quattro-modelli";
    private static readonly string Source=Path.Combine(ExamplePaths.Case("49-ponte-parametrico"),"49-ponte-parametrico.ghx");
    private static readonly string Folder=ExamplePaths.Case(Id);
    private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    private readonly GH_Document doc;
    private readonly Assembly plugin;
    private readonly List<GH_Component> added=new();
    private readonly List<GH_Component> models=new(),analyses=new(),overrides=new(),actions=new();
    private GH_Component composite,merge,creep;
    private readonly List<GH_Component> phases=new();
    private static string[] Cases(int i)=>i==3?new[]{Tags[i]+"_RITIRO"}:new[]{Tags[i]+"_SOLETTA",Tags[i]+"_TRAVI"};
    private static string[] ActionKeys=>Enumerable.Range(0,4).SelectMany(Cases).ToArray();
    private GH_BooleanToggle runAll;
    private float inputY,outputY=14300,outputX=8500;
    private readonly List<Guid> groupItems=new();
    private string groupName;
    private readonly Dictionary<string,IGH_Param> controls=new();
    private static readonly string[] Tags={"M01_ACCIAIO","M02_NINF","M03_N0","M04_NSH"};

    private ParametricBridgePhasesExample()
    {
        plugin=BridgeMergeTests.Initialize();
        var math=Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\MathComponents.gha");
        typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Assembly),typeof(string)},null)
            .Invoke(Grasshopper.Instances.ComponentServer,new object[]{math,math.Location});
        doc=GrasshopperBridgeExample.Read(Source);
        inputY=doc.Objects.OfType<IGH_Param>().Where(p=>p.SourceCount==0).Max(p=>p.Attributes.Bounds.Bottom)+200;
    }
    public void Dispose()=>doc.Dispose();
    private static IGH_Structure Persistent(IGH_Param p)=>p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure;
    private GH_Component Find(string type)=>doc.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name==type);
    private static IGH_Param In(GH_Component c,string name)=>c.Params.Input.Single(p=>p.Name==name);
    private static IGH_Param Out(GH_Component c,string name)=>c.Params.Output.Single(p=>p.Name==name);
    private static void Wire(IGH_Param source,GH_Component target,string port,bool replace=false)
    { var p=In(target,port); if(replace)p.RemoveAllSources(); Persistent(p)?.Clear(); p.AddSource(source); }
    private static void Wire(GH_Component source,string port,GH_Component target,string input,bool replace=false)=>Wire(Out(source,port),target,input,replace);
    private GH_Component Add(string type,string name,float x,float y)
    {
        var c=(GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name==type));
        c.NickName=name; c.CreateAttributes(); c.Attributes.Pivot=new PointF(x,y); c.Hidden=true; doc.AddObject(c,false); added.Add(c); return c;
    }
    private GH_Component Standard(string name,float x,float y)
    {
        var server=Grasshopper.Instances.ComponentServer;
        var c=server.ObjectProxies.Where(p=>p.Desc.Name==name).Select(p=>server.EmitObject(p.Guid)).OfType<GH_Component>()
            .First(c=>!c.GetType().Name.Contains("OBSOLETE"));
        c.CreateAttributes(); c.Attributes.Pivot=new PointF(x,y); c.Hidden=true; doc.AddObject(c,false); added.Add(c); return c;
    }
    private void Group(string name)
    {
        if(groupItems.Count>0)
        {
            var g=new GH_Group{NickName=groupName,Colour=Color.FromArgb(45,70,120,160)}; g.CreateAttributes(); doc.AddObject(g,false);
            foreach(var id in groupItems)g.AddObject(id); groupItems.Clear(); inputY+=90;
        }
        groupName=name;
    }
    private T Control<T>(string label,T p) where T:IGH_Param
    {
        p.Name=p.NickName=label; p.Description=label; p.CreateAttributes(); p.Attributes.Pivot=new PointF(60,inputY);
        if(p is GH_NumberSlider)p.Attributes.Bounds=new RectangleF(60,inputY,620,24);
        if(p is GH_Panel)p.Attributes.Bounds=new RectangleF(60,inputY,840,95);
        if(p is IGH_PreviewObject preview)preview.Hidden=true;
        doc.AddObject(p,false); controls.Add(label,p); groupItems.Add(p.InstanceGuid); inputY+=p is GH_Panel?120:75; return p;
    }
    private IGH_Param Text(string label,string value)=>Control(label,new GH_Panel{UserText=value,Properties={Multiline=false}});
    private GH_NumberSlider Slider(string label,double value,double min,double max,int decimals=3)
    {
        var s=new GH_NumberSlider(); s.Slider.Type=decimals==0?GH_SliderAccuracy.Integer:GH_SliderAccuracy.Float;
        s.Slider.DecimalPlaces=decimals; s.Slider.Minimum=(decimal)min;s.Slider.Maximum=(decimal)max;s.SetSliderValue((decimal)value);return Control(label,s);
    }
    private IGH_Param Choice(string label,(string Name,int Value)[] choices,int value)
    {
        var p=new GH_ValueList{ListMode=GH_ValueListMode.DropDown}; p.ListItems.Clear();
        foreach(var item in choices)p.ListItems.Add(new GH_ValueListItem(item.Name,item.Value.ToString(Inv)){Selected=item.Value==value});return Control(label,p);
    }
    private void Scalar(GH_Component c,string port,string label,double value,double min,double max,int decimals=3)=>Wire(Slider(label,value,min,max,decimals),c,port,true);
    private void Literal(GH_Component c,string port,string label,string text)=>Wire(Text(label,text),c,port,true);
    private void Flag(GH_Component c,string port,string label,bool value)=>Wire(Control(label,new GH_BooleanToggle{Value=value}),c,port,true);
    private IGH_Param Product(IGH_Param a,IGH_Param b,string name,float x,float y)
    {
        var c=Standard("Multiplication",x,y);c.NickName=name;c.Params.Input[0].AddSource(a);c.Params.Input[1].AddSource(b);return c.Params.Output[0];
    }
    private void Output(IGH_Param source,string label,float? y=null)
    {
        if(y.HasValue)outputY=y.Value;
        var p=(IGH_Param)Activator.CreateInstance(source.GetType());Persistent(p)?.Clear();p.Name=p.NickName=label;p.Description=source.Description;
        p.CreateAttributes();p.Attributes.Pivot=new PointF(outputX,outputY);p.AddSource(source);if(p is IGH_PreviewObject preview)preview.Hidden=true;doc.AddObject(p,false);
        var g=new GH_Group{Name=label,NickName=label,Colour=Color.FromArgb(45,70,120,160)};g.CreateAttributes();doc.AddObject(g,false);g.AddObject(p.InstanceGuid);outputY+=100;
    }
    private void RenameExistingModel(GH_Component c,string name,float y)
    {
        c.NickName=name;c.Attributes.Pivot=new PointF(4500,y);
        var text=(GH_Panel)In(c,"Name").Sources.Single();text.UserText=name;text.Name=text.NickName=name+" | Nome modello";
        foreach(var p in doc.Objects.OfType<IGH_Param>().Where(p=>p.Sources.Contains(Out(c,"Model"))))
        {
            p.Name=p.NickName=name+" — modello FEM con carichi di test";
            foreach(var g in doc.Objects.OfType<GH_Group>().Where(g=>g.ObjectIDs.Contains(p.InstanceGuid)))g.Name=g.NickName=p.NickName;
        }
    }
    private void ExternalDefaults()
    {
        Group("18 | Parametri espliciti dei nuovi componenti");
        foreach(var c in added)
        foreach(var p in c.Params.Input)
        {
            if(p.SourceCount>0){Persistent(p)?.Clear();continue;}
            string label=c.NickName+" | "+p.Name;
            var values=Persistent(p)?.AllData(true).Cast<IGH_Goo>().ToArray()??Array.Empty<IGH_Goo>();
            IGH_Param source;
            if(values.Length==0)
            {
                if(!p.Optional)throw new Exception("Missing example input "+label);
                source=(IGH_Param)Activator.CreateInstance(p.GetType());Persistent(source)?.Clear();Control(label+" (opzionale vuoto)",source);
            }
            else if(values.Length==1 && values[0] is GH_Boolean b)source=Control(label,new GH_BooleanToggle{Value=b.Value});
            else if(values.Length==1 && values[0] is GH_Integer i)source=Slider(label,i.Value,Math.Min(0,i.Value),Math.Max(10,i.Value*2),0);
            else if(values.Length==1 && values[0] is GH_Number n)
            {
                if(double.IsFinite(n.Value) && Math.Abs(n.Value)<=1e20 && decimal.TryParse(n.Value.ToString("R",Inv),NumberStyles.Float,Inv,out var exact) && (double)exact==n.Value)
                {
                    int precision=Math.Max(3,(decimal.GetBits(exact)[3]>>16)&0xff);
                    var slider=Slider(label,n.Value,Math.Min(0,n.Value),Math.Max(1,n.Value*2),precision);
                    if((double)slider.CurrentValue!=n.Value)throw new Exception("Slider changed the default value of "+label);
                    source=slider;
                }
                else source=Text(label,n.Value.ToString("R",Inv));
            }
            else source=Text(label,string.Join("\n",values.Select(g=>g.ScriptVariable() is IFormattable v?v.ToString(null,Inv):g.ToString())));
            Wire(source,c,p.Name);source.Description=p.Description;
        }
        Group(null);
    }
    private void Build()
    {
        var segment=Find("BridgeSegmentSectionsComponent");var steel=Find("MaterialSteelComponent");var concrete=Find("MaterialConcreteComponent");
        var rebar=Find("MaterialRebarComponent");var slabCheck=Find("BeamSectionConcreteSlabComponent");var assignment=Find("BridgeMemberAssignmentComponent");
        var slab=Find("BridgeSlabGrillageComponent");var links=Find("BridgeConnectionsComponent");var bearings=Find("BridgeBearingsComponent");
        // The base example uses a single effective width. Use 3 m here, below both edge tributary widths (3.25 m).
        ((GH_NumberSlider)slabCheck.Params.Input[1].Sources.Single()).SetSliderValue(3000);
        Group("10 | Omogeneizzazione per ogni concio — coefficienti dimostrativi");
        composite=Add("BeamPropertyCompositeComponent","Sezioni miste di tutti i conci",2200,13000);
        Wire(segment,"Segment IDs",composite,"Name");Wire(segment,"Sections",composite,"Steel Section");Wire(slabCheck,"Concrete Slab",composite,"Concrete Slab Section");
        Wire(steel,"Material",composite,"Steel Material");Wire(concrete,"Material",composite,"Concrete Material");Wire(rebar,"Material",composite,"Reinforcement Material");
        Scalar(composite,"n0","n0 — coefficiente istantaneo",7,1,40);Scalar(composite,"n ∞","n infinito — coefficiente a lungo termine",18,1,80);
        Scalar(composite,"n ritiro","n ritiro — coefficiente per deformazioni da ritiro",12,1,80);
        Flag(composite,"Consider Concrete","Considera calcestruzzo nella sezione mista",true);Flag(composite,"Consider Rebars","Considera armature nella sezione mista",true);
        Output(Out(composite,"Composite Section"),"Sezioni miste per concio — stesso ordine degli ID",13000);
        foreach(string port in new[]{"n ∞ Beam Section","n0 Beam Section","n ritiro Beam Section"})Output(Out(composite,port),"Proprietà per concio | "+port);

        Group("11 | Materiale fittizio delle fasce soletta — comune ai quattro modelli");
        var fictitious=Add("MaterialCustomComponent","Cls soletta senza peso",2200,13900);
        Literal(fictitious,"Name","Nome materiale fittizio soletta","Soletta fittizia — densità zero");
        Wire(Choice("Tipo materiale soletta",new[]{("Calcestruzzo",(int)MaterialModel.MaterialKind.Concrete)},(int)MaterialModel.MaterialKind.Concrete),fictitious,"Kind");
        Wire(Choice("Unità materiale soletta",new[]{("N mm MPa",2)},2),fictitious,"Units");
        var elastic=Slider("Modulo elastico fasce soletta E [MPa]",30000,1000,50000);Wire(elastic,fictitious,"Modulus");
        var nu=Slider("Poisson fasce soletta",.2,0,.49);Wire(nu,fictitious,"Poisson Ratio");
        var plus=Standard("Addition",1400,14100);plus.Params.Input[0].AddSource(nu);plus.Params.Input[1].AddSource(Text("Costante 1 per 1 + nu","1"));
        var denom=Product(plus.Params.Output[0],Text("Costante 2 per G = E / 2(1 + nu)","2"),"2(1+nu)",1700,14100);
        var shear=Standard("Division",1900,14100);shear.Params.Input[0].AddSource(elastic);shear.Params.Input[1].AddSource(denom);Wire(shear.Params.Output[0],fictitious,"Shear Modulus");
        Scalar(fictitious,"Density","Densità materiale fittizio — ZERO [t/mm³]",0,0,1e-8,12);
        Scalar(fictitious,"Thermal Expansion","Dilatazione termica cls [1/K]",1e-5,0,3e-5,7);
        Flag(fictitious,"Use Poisson","Materiale soletta: usa Poisson",true);
        Scalar(fictitious,"Yielding","Resistenza cls fittizio [MPa]",32,1,80);Scalar(fictitious,"Strength for Steel","Resistenza acciaio non usata dal materiale cls",0,0,1);
        Wire(fictitious,"Material",slab,"Concrete Material",true);
        Output(Out(fictitious,"Material"),"Materiale delle fasce trasversali — cls senza peso",13400);
        AddShrinkage(slabCheck);

        Group("12 | Carichi e analisi — impostazioni comuni");
        var unit=Choice("Unità dei quattro modelli",new[]{("N mm MPa",2)},2);
        var schema=Choice("Schema carichi di test",new[]{("Uniforme",0)},0);
        var direction=Choice("Direzione carichi di test",new[]{("Z globale",2)},2);
        var cs=(IGH_Param)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name=="CoordinateSystemParam"));
        var csGoo=(IGH_Goo)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name=="GH_CoordinateSystem"));
        // This is the same explicit Global setting exposed by the coordinate-system parameter menu.
        cs.GetType().GetProperty("PersistentData").GetValue(cs).GetType().GetMethod("Append",new[]{csGoo.GetType()})?.Invoke(Persistent(cs),new object[]{csGoo});
        if(Persistent(cs).IsEmpty)cs.GetType().GetMethods().First(m=>m.Name=="SetPersistentData" && m.GetParameters().Length==1 && m.GetParameters()[0].ParameterType==csGoo.GetType()).Invoke(cs,new object[]{csGoo});
        Control("Sistema di riferimento dei carichi — Global",cs);
        var pressureFactor=Text("Conversione pressione kN/m² → N/mm²","0.001");
        runAll=Control("CALCOLA I QUATTRO MODELLI STRAUS — poi riportare False",new GH_BooleanToggle{Value=false});
        var oldModels=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BuildModelComponent").ToArray();
        var casesByModel=new List<GH_Component[]>();
        for(int i=0;i<4;i++)
        {
            string tag=Tags[i];float y=15100+i*1600;Group($"{13+i:00} | {tag} — carichi indipendenti modificabili");
            var over=Add("BridgeSegmentOverrideComponent",tag+" | proprietà longitudinali",2500,y);overrides.Add(over);
            Wire(assignment,"Beams",over,"Beams");Wire(segment,"Segment IDs",over,"Segment IDs");
            Wire(i==0?Out(segment,"Properties"):Out(composite,new[]{"","n ∞ Beam Section","n0 Beam Section","n ritiro Beam Section"}[i]),over,"Properties");
            var lcs=new List<GH_Component>();
            foreach(string family in i==3?new[]{"RITIRO"}:new[]{"SOLETTA","TRAVI"})
            {
                var lc=Add("LoadCaseComponent",tag+" | caso "+family,1500,y+(family=="SOLETTA"?0:550));
                Literal(lc,"Name",tag+" | nome caso "+family,tag+"_"+family);Flag(lc,"Gravity NSM",tag+" | gravità masse non strutturali "+family,false);lcs.Add(lc);
            }
            casesByModel.Add(lcs.ToArray());
            IGH_Param loadedGirders,loadedSlab;
            if(i==3)
            {
                var map=MapSections(Out(over,"Girders"),"M04 | sezione mista di ogni asta",2800,y+250);
                var equivalent=Add("CompositeShrinkageActionsComponent","M04 | deformazioni equivalenti per concio",3100,y+600);
                Wire(map,"Composite Properties",equivalent,"Composite property");Wire(creep,"εcs",equivalent,"Concrete strain");
                var pre=Add("BeamPreLoadComponent","M04 | contrazione equivalente Straus",3500,y);
                Wire(map,"Girders",pre,"Beam");Wire(lcs[0],"LoadCase",pre,"LoadCase");
                Wire(Choice("M04 | tipo predeformazione",new[]{("Strain — deformazione imposta",1)},1),pre,"Type");
                Wire(equivalent,"Straus pre-strain",pre,"Value");
                var gradient=Add("BeamGradientLoadComponent","M04 | curvatura equivalente del ritiro",3900,y+350);
                Wire(pre,"Beam",gradient,"Beam");Wire(lcs[0],"LoadCase",gradient,"LoadCase");Wire(equivalent,"Axis 2 thermal gradient",gradient,"Axis 2");
                loadedGirders=Out(gradient,"Beam");loadedSlab=Out(slab,"Beams");
                Output(Out(equivalent,"Straus pre-strain"),"Ritiro | predeformazione assiale equivalente per asta",y+1200);
                Output(Out(equivalent,"Axis 2 thermal gradient"),"Ritiro | gradiente equivalente per curvatura [K/mm]");
            }
            else
            {
            var pressure=Slider(tag+" | pressione verticale soletta [kN/m²]",-1,-20,20);
            var stripPressure=Product(pressure,pressureFactor,tag+" | pressione in N/mm²",1700,y+950);
            var stripLineLoad=Product(stripPressure,Out(slab,"Strip Widths"),tag+" | q = pressione × fascia tributaria",2600,y+950);
            var q=Slider(tag+" | carico verticale travi principali [N/mm = kN/m]",-1,-30,30);
            GH_Component Load(IGH_Param beam,IGH_Param amount,GH_Component lc,string family,float dy)
            {
                var load=Add("BeamDistributedLoadComponent",tag+" | carico "+family,3500,y+dy);
                Wire(beam,load,"Beam");Wire(lc,"LoadCase",load,"LoadCase");Wire(schema,load,"Type");Wire(cs,load,"Coordinate System");Wire(direction,load,"Direction");Wire(amount,load,"PA");return load;
            }
            var slabLoad=Load(Out(slab,"Beams"),stripLineLoad,lcs[0],"soletta",300);
            var girderLoad=Load(Out(over,"Girders"),q,lcs[1],"travi",0);
            loadedGirders=Out(girderLoad,"Beam");loadedSlab=Out(slabLoad,"Beam");
            }
            var model=i<2?oldModels[i]:Add("BuildModelComponent",tag,4500,y);
            if(i<2)RenameExistingModel(model,tag,y);else Literal(model,"Name",tag+" | nome modello",tag);
            In(model,"Elements").RemoveAllSources();In(model,"Elements").DataMapping=GH_DataMapping.Flatten;
            Wire(loadedGirders,model,"Elements");Wire(over,"Other Steel",model,"Elements");Wire(loadedSlab,model,"Elements");
            Wire(bearings,"Nodes",model,"Elements");Wire(links,"Elements",model,"Elements");Wire(unit,model,"Units",true);models.Add(model);
            var analysis=Add("StrausR3AnalysisComponent",tag+" | analisi lineare",5100,y);analyses.Add(analysis);
            Wire(model,"Model",analysis,"Model");Wire(runAll,analysis,"Keep Running");
            Literal(analysis,"Output Path",tag+" | file Straus",Path.Combine(Folder,"calcoli",tag+".st7"));
            Literal(analysis,"Analysis Results",tag+" | casi restituiti",string.Join("\n",Cases(i))+(i<3?"\nDead":""));
            Output(Out(model,"Model"),tag+" | MODELLO FEM CARICATO",y);
            Output(Out(analysis,"Model"),tag+" | MODELLO CON RISULTATI STRAUS");
            Output(Out(over,"Beam IDs"),tag+" | ID travi longitudinali");
            Output(loadedGirders,tag+" | travi principali con proprietà e "+(i==3?"ritiro":"carichi"));
            Output(loadedSlab,tag+" | fasce soletta "+(i==3?"senza carichi":"con carico tributario"));
            foreach(var lc in lcs)Output(Out(lc,"LoadCase"),tag+" | "+lc.NickName);
        }

        Group("17 | Assemblaggio delle quattro risposte — combinazioni solo di prova");
        merge=Add("BuildBridgeModel","ASSEMBLA QUATTRO MODELLI E FASI",7500,17700);
        var rows=new List<string>{"Combinazione;"+string.Join(";",ActionKeys)};
        for(int i=0;i<4;i++)rows.Add("TEST_"+Tags[i]+";"+string.Join(";",ActionKeys.Select(k=>Cases(i).Contains(k)?"1":"0")));
        rows.Add("DEMO_CUMULATIVA;"+string.Join(";",ActionKeys.Select(_=>"1")));
        var combinations=Text("COMBINAZIONI TEST — una riga per risposta, coefficienti modificabili",string.Join("\n",rows));
        combinations.Attributes.Bounds=new RectangleF(60,combinations.Attributes.Pivot.Y,840,200);inputY+=105;
        var split=Standard("Text Split",6700,18400);split.NickName="Tabella combinazioni da righe separate da ;";
        split.Params.Input[0].AddSource(combinations);split.Params.Input[1].AddSource(Text("Separatore della tabella combinazioni",";"));
        Wire(split.Params.Output[0],merge,"Combinations");
        for(int i=0;i<4;i++)
        {
            string tag=Tags[i];float y=15100+i*1600;Wire(analyses[i],"Model",merge,"Models");
            var phase=Add("BridgeConstructionPhaseComponent",tag+" | fase di test",6900,y);
            phases.Add(phase);
            Scalar(phase,"Number",tag+" | numero fase",10*(i+1),0,100,0);Literal(phase,"Name",tag+" | nome fase",tag);
            Wire(Choice(tag+" | stato sezione",new[]{("Acciaio",0),("Misto attivo",1)},i==0?0:1),phase,"Section State");
            for(int family=0;family<Cases(i).Length;family++)
            {
                string key=Cases(i)[family];
                var action=Add(i==3?"BridgeShrinkageActionComponent":"BridgeActionComponent",key+" | azione",6050,y+family*500);actions.Add(action);
                Literal(action,"Action ID",key+" | identificativo azione",key);Wire(analyses[i],"Model",action,"Model");Wire(casesByModel[i][family],"LoadCase",action,"Case");
                // Whole-model actions preserve global support reactions. The checker selects the longitudinal beams downstream.
                if(i<3)Wire(Choice(key+" | comportamento",new[]{("Acciaio",0),("Lungo termine n infinito",1),("Istantaneo n0",2)},i),action,"Behavior");
                else
                {
                    Wire(creep,"εcs",action,"Strain increment");
                    Flag(action,"Primary Only",key+" | solo effetto primario",false);
                    Wire(Choice(key+" | risposta",new[]{("n ritiro",0)},0),action,"Response");
                }
                Wire(action,"Action",phase,"Actions");
            }
            Wire(phase,"Phase",merge,"Phases");Output(Out(phase,"Phase"),tag+" | definizione fase",y+850);
        }
        Wire(Choice("Metodo Build BridGHe Model",new[]{("Cumulativo",0),("Storia lineare",1)},0),merge,"Calculation");Flag(merge,"Additional Phase Members","Consenti elementi aggiuntivi nelle fasi — geometrie identiche",false);
        Output(Out(merge,"Model"),"MODELLO BRIDGHE ASSEMBLATO — dopo analisi e Run",22000);
        Output(Out(merge,"Phases"),"Riepilogo delle quattro fasi e delle sette azioni");Output(Out(merge,"Diagnostics"),"Diagnostica assemblaggio delle risposte");
        // Keep the existing preview/audit useful, but select explicitly which loaded FEM model is displayed.
        var selector=Standard("List Item",5000,23200);selector.NickName="Modello FEM visualizzato";
        foreach(var m in models)selector.Params.Input[0].AddSource(Out(m,"Model"));selector.Params.Input[0].DataMapping=GH_DataMapping.Flatten;
        selector.Params.Input[1].AddSource(Choice("Modello FEM da visualizzare",Tags.Select((s,i)=>(s,i)).ToArray(),0));
        Wire(selector.Params.Output[0],Find("PreviewModelComponent"),"Model / elements",true);
        Wire(selector.Params.Output[0],Find("BridgeModelAuditComponent"),"Model",true);
        Wire(selector.Params.Output[0],Find("DecomposeModelComponent"),"Model",true);
        AddChecks();
        ExternalDefaults();
        // Remove only orphaned input controls produced by replacing old sources in this in-memory copy.
        var recipients=doc.Objects.OfType<GH_Component>().SelectMany(c=>c.Params.Input).Concat(doc.Objects.OfType<IGH_Param>()).SelectMany(p=>p.Sources).ToHashSet();
        foreach(var p in doc.Objects.OfType<IGH_Param>().Where(p=>p.SourceCount==0 && p.Attributes.Pivot.X<700 && !recipients.Contains(p) && p is not GH_Panel).ToArray())doc.RemoveObject(p,false);
        var title=doc.Objects.OfType<GH_Panel>().Single(p=>p.NickName=="COME USARE IL PONTE PARAMETRICO");
        title.NickName="50 | QUATTRO MODELLI DI CALCOLO";
        title.UserText="50 | STESSA GEOMETRIA, QUATTRO RISPOSTE: ACCIAIO / n infinito / n0 / n ritiro\n"+
            "Conci e input geometrici come nel 49, che resta invariato. Sezioni a I per concio → sezioni miste → override per ID.\n"+
            "Fasce trasversali della soletta con materiale cls di densità ZERO in tutti i modelli; E = 30000 MPa comune e modificabile.\n"+
            "M01/M02/M03: pressione soletta e carico lineare travi. M04: SOLO ritiro, stessa epsilon per FEM e azione primaria.\n"+
            "Ritiro calcolato da Creep And Shrinkage Concrete, quindi convertito in pre-strain + curvatura equivalente di ogni concio.\n"+
            "CALCOLA I QUATTRO MODELLI = True, attendere le analisi, poi False. Run sul Build BridGHe Model per assemblare le quattro risposte.\n"+
            "Run su Verifiche di tutte le travi → pioli / irrigiditori / riepilogo. Draw sui grafici; Generate sul report Word.\n"+
            "Le TEST isolano le risposte; DEMO_CUMULATIVA somma tre incrementi meccanici distinti e un solo ritiro. Coefficienti dimostrativi.\n"+
            BridgeCheckAdapter.PlanarAssumption+"\n"+
            "Le fasce cls sono attive anche nel ramo acciaio per questo confronto: non rappresenta automaticamente la fase fisica di getto fresco.\n"+
            "Quote FEM, eccentricità e convenzione delle sezioni omogeneizzate seguono il flusso esistente. Vedere README dell'esempio 50.";
        Example50HorizontalLayout.Apply(doc);
    }

    private static object Value(IGH_Param p)=>p.VolatileData.AllData(true).Single().ScriptVariable();
    private static IEnumerable<T> Values<T>(IGH_Param p)=>p.VolatileData.AllData(true).Where(g=>g?.ScriptVariable() is T).Select(g=>(T)g.ScriptVariable());
    private static void Solve(GH_Document d)
    {
        GH_Document.EnableSolutions=true;d.Enabled=true;d.NewSolution(false,GH_SolutionMode.Silent);
        var errors=d.Objects.OfType<GH_ActiveObject>().SelectMany(o=>o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>o.NickName+": "+e)).ToArray();
        if(errors.Length>0)throw new Exception(string.Join("\n",errors));
    }
    private static void Require(bool test,string message){if(!test)throw new Exception("FOUR MODELS: "+message);}
    private object Validate(GH_Document d)
    {
        var ms=Tags.Select(tag=>(ModelModel)Value(Out(d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BuildModelComponent"&&c.NickName==tag),"Model"))).ToArray();
        var baseById=ms[0].Beams.ToDictionary(b=>b.BeamId);var rows=new List<object>();
        for(int i=0;i<4;i++)
        {
            var m=ms[i];Require(m.Name==Tags[i] && m.Beams.Count==ms[0].Beams.Count && m.Nodes.Count==ms[0].Nodes.Count,"identical topology across models");
            ModelHelper.GetLoadCases(m,out var cases);
            Require(cases.Where(l=>l.Name!="Dead").Select(l=>l.Name).Order().SequenceEqual(Cases(i).Order()),"correct phase cases: "+string.Join(",",cases.Select(l=>l.Name)));
            Require(cases.Where(l=>l.Name!="Dead").All(l=>l.GlobalInertiaLoad==LoadCaseModel.GlobalInertiaLoads.None),"no implicit gravity in test cases");
            var audit=BridgeModelAudit.Inspect(m,1e-3);Require(!audit.Any(a=>a.Severity=="Error"||a.Code=="DISCONNECTED"),"model audit: "+string.Join(";",audit.Select(a=>a.ToString())));
            double deckForce=0,girderForce=0;
            foreach(var b in m.Beams)
            {
                var meta=b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value;var original=baseById[b.BeamId];
                Require(b.PointFrom==original.PointFrom && b.PointTo==original.PointTo && b.Offset==original.Offset && b.AngleDeg==original.AngleDeg,"stable axes, offsets and FEM identities");
                if(meta.Family==BridgeMemberFamily.DeckTransverse)Require(b.BeamProperty.Material.Kind==MaterialModel.MaterialKind.Concrete && b.BeamProperty.Material.Density==0,"zero density on every transverse slab strip");
                else Require(b.BeamProperty.Material.Kind==MaterialModel.MaterialKind.Steel,"all steel members retain steel material");
                if(meta.Family==BridgeMemberFamily.Girder)Require(b.BeamProperty.SectionType==(i==0?SectionModel.SectionTypes.I:SectionModel.SectionTypes.Generic),"correct girder section response");
                else Require(b.BeamProperty.SectionArea==original.BeamProperty.SectionArea && b.BeamProperty.I11==original.BeamProperty.I11,"other members retain stiffness");
                Require(Math.Abs(b.BeamProperty.Material.Density*b.BeamProperty.SectionArea-original.BeamProperty.Material.Density*original.BeamProperty.SectionArea)<1e-15,"physical steel mass unchanged by homogenization");
                var loads=b.Loads.OfType<BeamDistributedLoadModel>().ToArray();
                Require(loads.Length==(i<3 && meta.Family is BridgeMemberFamily.Girder or BridgeMemberFamily.DeckTransverse?1:0),"mechanical loads only on intended member families and phases");
                if(i==3)
                {
                    Require(b.Loads.Count==(meta.Family==BridgeMemberFamily.Girder?2:0),"shrinkage model contains only two equivalent strain actions per longitudinal beam");
                    if(meta.Family==BridgeMemberFamily.Girder)
                    {
                        var c=d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BeamPropertyCompositeComponent");
                        var section=Values<CompositeBeamPropertyModel>(Out(c,"Composite Section")).Single(s=>s.Name==$"G{meta.Group}-C{meta.Segment}");
                        double epsilon=(double)Value(Out(d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="CreepAndShrinkageConcreteComponent"),"εcs"));
                        var expected=CompositeShrinkageActions.Calculate(section,epsilon);
                        var pre=b.Loads.OfType<BeamPreLoadModel>().Single();var grad=b.Loads.OfType<BeamGradientLoadModel>().Single();
                        Require(epsilon<0 && pre.PreLoadType==BeamPreLoadModel.LoadType.Strain && Math.Abs(pre.Value-expected.PreStrain)<1e-14 && grad.Value.Axis1==0 && Math.Abs(grad.Value.Axis2-expected.ThermalGradient)<1e-14,"correct concio shrinkage axial strain and curvature");
                    }
                }
                foreach(var load in loads)
                {
                    Require(load.Value.CoordinateSystem!=null && load.Value.LoadDirection==BeamDistributedLoadModel.LoadDirection.Z,"global vertical loading");
                    double force=load.Value.PA*b.PointFrom.DistanceTo(b.PointTo);
                    if(meta.Family==BridgeMemberFamily.DeckTransverse)deckForce+=force;else girderForce+=force;
                }
            }
            Require(Math.Abs(deckForce+(i==3?0:900000))<1e-5 && Math.Abs(girderForce+(i==3?0:270000))<1e-5,"analytical load totals; zero mechanical loads in shrinkage model");
            rows.Add(new{m.Name,beams=m.Beams.Count,nodes=m.Nodes.Count,deckForceN=deckForce,girderForceN=girderForce,zeroSlabDensity=true});
        }
        var comp=d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BeamPropertyCompositeComponent");
        var sections=Values<CompositeBeamPropertyModel>(Out(comp,"Composite Section")).ToArray();Require(sections.Length==24,"24 composite properties, one per fabrication segment");
        for(int i=1;i<4;i++)
        foreach(var b in ms[i].Beams.Where(b=>b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Family==BridgeMemberFamily.Girder))
        {
            var meta=b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value;var c=sections.Single(c=>c.Name==$"G{meta.Group}-C{meta.Segment}");
            var expected=c.CreateHomogenizedProperty(i==1?c.NInfinite:i==2?c.NInstant:c.NShrinkage,"expected");
            Require(Math.Abs(b.BeamProperty.SectionArea/expected.SectionArea-1)<1e-12 && Math.Abs(b.BeamProperty.I11/expected.I11-1)<1e-12,"each model uses its own modular ratio for each concio");
        }
        Require(!ReferenceEquals(ms[0].Beams[0],ms[1].Beams[0]),"independent model objects");
        var merger=d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BuildBridgeModel");
        var data=In(merger,"Combinations").VolatileData;
        var table=data.Paths.Select(path=>data.get_Branch(path).Cast<IGH_Goo>().Select(g=>g.ScriptVariable()?.ToString()).ToArray()).ToArray();
        Require(table.Length==6 && table.All(r=>r.Length==8),"header and five combinations with seven action columns");
        Require(table[0].Skip(1).SequenceEqual(ActionKeys),"combination headers exactly match action IDs");
        for(int i=0;i<4;i++)Require(table[i+1].Skip(1).SequenceEqual(ActionKeys.Select(k=>Cases(i).Contains(k)?"1":"0")),"comparison isolates one model");
        Require(table[5].Skip(1).All(s=>s=="1"),"cumulative demo combines three distinct mechanical increments and shrinkage once");
        var shrink=d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BridgeShrinkageActionComponent");
        var strain=d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="CreepAndShrinkageConcreteComponent");
        Require(In(shrink,"Strain increment").Sources.Single()==Out(strain,"εcs") && In(d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="CompositeShrinkageActionsComponent"),"Concrete strain").Sources.Single()==Out(strain,"εcs"),"one strain source for primary and secondary shrinkage");
        Require(d.Objects.OfType<GH_Component>().SelectMany(c=>c.Params.Input).All(p=>p.SourceCount>0 && Persistent(p)?.IsEmpty!=false),"all inputs remain explicitly wired");
        return rows;
    }
    private static void RunButton(GH_Component c)=>c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);
    private void ValidateWorkflowWiring(string savedDefinition)
    {
        // Controlled test data lives only in this disposable document. It is never written to the delivered GHX.
        using var fixture=GrasshopperBridgeExample.Read(savedDefinition);Solve(fixture);
        for(int i=0;i<4;i++)
        {
            var build=fixture.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BuildModelComponent"&&c.NickName==Tags[i]);
            var snapshot=ModelArchive.Clone((ModelModel)Value(Out(build,"Model")));
            ModelHelper.GetLoadCases(snapshot,out var cases);
            foreach(var b in snapshot.Beams)
            foreach(var lc in cases.Where(c=>c.Name!="Dead"))
            foreach(double station in new[]{0d,b.PointFrom.DistanceTo(b.PointTo)})
                b.Results.Add(new BeamForceResultModel(lc,station,0,""){AxialForce=10*(i+1)+(lc.Name.EndsWith("TRAVI")?1:0),Vy=2000*(i+1),My=1e7*(i+1),Torque=5e6,Mx=-2e6,Vx=3e4});
            foreach(var node in snapshot.Nodes)
            foreach(var lc in cases.Where(c=>c.Name!="Dead"))
                node.Results.Add(new NodeReactResultModel(lc){Force=new Rhino.Geometry.Vector3d(0,0,1000*(i+1))});
            var analysis=fixture.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="StrausR3AnalysisComponent"&&c.NickName.StartsWith(Tags[i]));
            var field=analysis.GetType().GetField("_model",BindingFlags.Instance|BindingFlags.NonPublic);
            var goo=Activator.CreateInstance(field.FieldType);field.FieldType.GetProperty("Value").SetValue(goo,snapshot);field.SetValue(analysis,goo);
            analysis.ExpireSolution(false);
        }
        var merger=fixture.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BuildBridgeModel");RunButton(merger);merger.ExpireSolution(false);Solve(fixture);
        var result=(ModelModel)Value(Out(merger,"Model"));Require(result.BridgeWorkflow?.Phases.Length==4 && result.LoadCombinations.Count==5,"four wired phases and five demo combinations in fixture");
        Require(result.BridgeWorkflow.Phases.SelectMany(p=>p.Actions).Count()==7,"all seven distinct wired actions reach the builder");
        foreach(var b in result.Beams.Where(b=>b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Family==BridgeMemberFamily.Girder))
        for(int i=0;i<4;i++)
        foreach(var key in Cases(i))
        {
            var values=b.Results.OfType<BeamForceResultModel>().Where(r=>r.LoadCase.Name==key+" [FEM]").ToArray();
            Require(values.Length>=2 && values.All(v=>v.AxialForce==10*(i+1)+(key.EndsWith("TRAVI")?1:0)),"fixture force remains associated with its exact action and source model");
        }
        ValidateChecks(fixture);
        var checker=fixture.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="SuperElementCheckComponent");
        var fullForces=Values<GPC.Model.Results.ResultBeamForces>(Out(checker,"Tension Forces")).ToArray();
        Require(fullForces.Any(f=>Math.Abs(f.T)>1 && Math.Abs(f.M2)>1 && Math.Abs(f.V1)>1),
            "checker force output and force plots retain T, M2 and V1 excluded from section checks");
        Console.WriteLine("FOUR MODELS WIRING FIXTURE PASS: seven actions, four phases, five combinations and whole-bridge checks; synthetic test data discarded, no FEM result claimed.");
    }
    private object Native()
    {
        var results=new List<object>();Directory.CreateDirectory(Path.Combine(Folder,"calcoli"));
        for(int i=0;i<analyses.Count;i++)
        {
            RunButton(analyses[i]);analyses[i].ExpireSolution(false);Solve(doc);
            var model=(ModelModel)Value(Out(analyses[i],"Model"));
            var lines=new List<object>();
            ModelHelper.GetLoadCases(model,out var cases);
            foreach(var lc in cases.Where(c=>i<3||c.Name!="Dead"))
            {
                var reactions=model.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Where(r=>r.LoadCase.Name==lc.Name).ToArray();
                Require(reactions.Length>0,"native support reactions for "+lc.Name);
                double rz=reactions.Sum(r=>r.Force.Z),expected=lc.Name=="Dead"?model.Beams.Sum(b=>b.BeamProperty.Material.Density*b.BeamProperty.SectionArea*b.PointFrom.DistanceTo(b.PointTo))*ModelModel.GRAVITY_mmSuS2:
                    lc.Name.EndsWith("RITIRO")?0:lc.Name.EndsWith("SOLETTA")?900000:270000;
                Require(Math.Abs(rz-expected)<=Math.Max(1e-3,expected*1e-7),"vertical native equilibrium for "+lc.Name);
                lines.Add(new{lc.Name,reactionZ=rz,expectedZ=expected});
            }
            string logfile=Path.ChangeExtension(Path.Combine(Folder,"calcoli",Tags[i]+".st7"),".LSL");
            if(File.Exists(logfile)){string log=File.ReadAllText(logfile);Require(!log.Contains("*WARNING[")&&!log.Contains("*ERROR["),"no unresolved Straus solver warnings in "+Tags[i]);}
            results.Add(new{model.Name,cases=lines});Console.WriteLine("FOUR MODELS STRAUS PASS: "+Tags[i]);
        }
        RunButton(merge);merge.ExpireSolution(false);Solve(doc);
        var merged=(ModelModel)Value(Out(merge,"Model"));Require(merged.BridgeWorkflow?.Phases.Length==4,"four phases assembled after real analyses");
        Require(merged.LoadCombinations.Count==5,"five demo combinations assembled");
        ValidateChecks(doc);
        return new{models=results,mergedPhases=4,combinations=5,wholeBridgeChecksExecuted=true};
    }
    internal static int Run(bool native)
    {
        string sourceHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Source)));
        try
        {
            using var example=new ParametricBridgePhasesExample();example.Build();Solve(example.doc);var checks=example.Validate(example.doc);
            Directory.CreateDirectory(Folder);string path=Path.Combine(Folder,Id+".ghx"),temporary=path+".tmp.ghx";
            GrasshopperBridgeExample.Write(example.doc,temporary);
            using(var reopened=GrasshopperBridgeExample.Read(temporary)){Solve(reopened);example.Validate(reopened);}
            example.ValidateWorkflowWiring(temporary);
            File.Move(temporary,path,true);
            File.WriteAllText(Path.Combine(Folder,"validation.json"),JsonSerializer.Serialize(new{source49Sha256=sourceHash,sourceUnchanged=true,reopened=true,
                workflowWiringTestedWithDiscardedSyntheticFixture=true,solverInvoked=false,models=checks},new JsonSerializerOptions{WriteIndented=true}));
            if(!native)File.WriteAllText(Path.Combine(Folder,"native-validation.json"),JsonSerializer.Serialize(
                new{status="not-run",solverResultsVerified=false,reason="Analyses deferred. Run the four Straus components, then Build BridGHe Model, or regenerate with -Native when the licence is available."},
                new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("FOUR MODELS EXAMPLE PASS: four independently loaded models, 24 sections, zero slab density, unchanged example 49, saved/reopened.");
            if(native)
            {
                var result=example.Native();
                File.WriteAllText(Path.Combine(Folder,"native-validation.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
                // Persist real results; the automatic-run toggle stays false, so reopening never launches analysis.
                GrasshopperBridgeExample.Write(example.doc,temporary);File.Move(temporary,path,true);
                Console.WriteLine("FOUR MODELS NATIVE PASS: six mechanical cases, one shrinkage case, three Dead result cases, reaction equilibrium and whole-bridge checks.");
            }
            return 0;
        }
        catch(Exception e)
        {
            if(native && Directory.Exists(Folder))File.WriteAllText(Path.Combine(Folder,"native-validation.json"),JsonSerializer.Serialize(
                new{status="not-completed",solverResultsVerified=false,error=e.Message},new JsonSerializerOptions{WriteIndented=true}));
            Console.Error.WriteLine("FOUR MODELS EXAMPLE FAILED: "+e);return 1;
        }
        finally {Require(sourceHash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Source))),"example 49 file unchanged");}
    }
}
