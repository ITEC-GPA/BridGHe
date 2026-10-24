using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

/// <summary>Reproducible GH definition: every control on the left, ordinary components in the centre, named outputs on the right.</summary>
internal sealed class ParametricBridgeExample
{
    private readonly GH_Document doc=new();
    private readonly Assembly plugin;
    private readonly Dictionary<string,IGH_Param> controls=new();
    private float inputY=0,outputY=0;
    private readonly List<Guid> currentGroup=new();
    private string groupName;
    private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    private const string Id="49-ponte-parametrico";
    private ParametricBridgeExample()
    {
        plugin=BridgeMergeTests.Initialize();
        var standard=Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\MathComponents.gha");
        typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,
            null,new[]{typeof(Assembly),typeof(string)},null).Invoke(Grasshopper.Instances.ComponentServer,new object[]{standard,standard.Location});
    }
    private static IGH_Structure Persistent(IGH_Param p)=>p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure;
    private GH_Component Add(string type,string name,float x,float y)
    {
        var c=(GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name==type));
        c.NickName=name; c.CreateAttributes(); c.Attributes.Pivot=new PointF(x,y); c.Hidden=true;
        doc.AddObject(c,false); return c;
    }
    private GH_Component Standard(string name,float x,float y)
    {
        var server=Grasshopper.Instances.ComponentServer;
        var c=server.ObjectProxies.Where(p=>p.Desc.Name==name).Select(p=>server.EmitObject(p.Guid)).OfType<GH_Component>()
            .First(c=>!c.GetType().Name.Contains("OBSOLETE"));
        c.CreateAttributes(); c.Attributes.Pivot=new PointF(x,y); doc.AddObject(c,false); return c;
    }
    private static IGH_Param In(GH_Component c,string name)=>c.Params.Input.Single(p=>p.Name==name);
    private static IGH_Param Out(GH_Component c,string name)=>c.Params.Output.Single(p=>p.Name==name);
    private static void Wire(IGH_Param source,GH_Component target,string input)
    { var p=In(target,input); Persistent(p)?.Clear(); p.AddSource(source); }
    private static void Wire(GH_Component source,string output,GH_Component target,string input)=>Wire(Out(source,output),target,input);
    private void Group(string name)
    {
        if(currentGroup.Count>0)
        {
            var g=new GH_Group{NickName=groupName,Colour=Color.FromArgb(35,30,130,145)}; g.CreateAttributes(); doc.AddObject(g,false);
            foreach(var id in currentGroup) g.AddObject(id); currentGroup.Clear(); inputY+=90;
        }
        groupName=name;
    }
    private T Control<T>(string key,string label,T control) where T:IGH_Param
    {
        control.NickName=label; control.Description=label; control.CreateAttributes(); control.Attributes.Pivot=new PointF(60,inputY);
        if(control is GH_NumberSlider) control.Attributes.Bounds=new RectangleF(60,inputY,540,24);
        else if(control is GH_Panel) control.Attributes.Bounds=new RectangleF(60,inputY,540,85);
        if(control is IGH_PreviewObject preview) preview.Hidden=true;
        doc.AddObject(control,false); currentGroup.Add(control.InstanceGuid); controls.Add(key,control);
        inputY+=control is GH_Panel?110:75; return control;
    }
    private GH_NumberSlider Slider(string key,string label,double value,double min,double max,bool integer=false)
    {
        var s=new GH_NumberSlider(); s.Slider.Type=integer?GH_SliderAccuracy.Integer:GH_SliderAccuracy.Float;
        s.Slider.DecimalPlaces=integer?0:3; s.Slider.Minimum=(decimal)min; s.Slider.Maximum=(decimal)max; s.SetSliderValue((decimal)value);
        return Control(key,label,s);
    }
    private IGH_Param Text(string key,string label,string value)
    {
        if(value.Length==0) { var p=new Param_String(); p.SetPersistentData(""); return Control(key,label+" (vuoto)",p); }
        var panel=new GH_Panel{UserText=value}; panel.Properties.Multiline=false; return Control(key,label,panel);
    }
    private IGH_Param Toggle(string key,string label,bool value)=>Control(key,label,new GH_BooleanToggle{Value=value});
    private IGH_Param Choice(string key,string label,IEnumerable<(string Name,int Value)> choices,int selected)
    {
        var list=new GH_ValueList{ListMode=GH_ValueListMode.DropDown}; list.ListItems.Clear();
        foreach(var (name,value) in choices) list.ListItems.Add(new GH_ValueListItem(name,value.ToString(Inv)){Selected=value==selected});
        return Control(key,label,list);
    }
    private IGH_Param Optional(GH_Component c,string port,string label)
    {
        var p=(IGH_Param)Activator.CreateInstance(In(c,port).GetType()); Persistent(p)?.Clear();
        p.Description=In(c,port).Description; Control(c.InstanceGuid+port,label+" (opzionale)",p); Wire(p,c,port); return p;
    }
    private void Set(GH_Component c,string port,IGH_Param source)
    { source.Description=source.NickName+"\n"+In(c,port).Description; Wire(source,c,port); }
    private void Scalar(GH_Component c,string port,string label,double value,double min,double max,bool integer=false)=>
        Set(c,port,Slider(c.InstanceGuid+port,label,value,min,max,integer));
    private void Panel(GH_Component c,string port,string label,string value)=>Set(c,port,Text(c.InstanceGuid+port,label,value));
    private void Output(IGH_Param source,string label,string description=null)
    {
        // Use the source parameter type to preserve trees and FEM goo, with no internal data.
        var p=(IGH_Param)Activator.CreateInstance(source.GetType()); Persistent(p)?.Clear();
        p.Name=label; p.NickName=label; p.Description=description??source.Description; p.CreateAttributes();
        p.Attributes.Pivot=new PointF(6200,outputY); p.AddSource(source); if(p is IGH_PreviewObject preview) preview.Hidden=true;
        doc.AddObject(p,false);
        // Group captions stay visible even when GH displays parameter icons instead of nicknames.
        var caption=new GH_Group {Name=label,NickName=label,Colour=Color.FromArgb(45,30,130,145)};
        caption.CreateAttributes(); doc.AddObject(caption,false); caption.AddObject(p.InstanceGuid);
        outputY+=95;
    }
    private void Output(GH_Component c,string port,string label)=>Output(Out(c,port),label);
    private void Defaults()
    {
        Group("09 | Altri parametri e ingressi opzionali");
        foreach(var c in doc.Objects.OfType<GH_Component>().ToArray())
        foreach(var p in c.Params.Input)
        {
            if(p.SourceCount>0) { Persistent(p)?.Clear(); continue; }
            var values=Persistent(p)?.AllData(true).Cast<IGH_Goo>().ToArray()??Array.Empty<IGH_Goo>();
            string key=c.InstanceGuid+p.Name,label=c.NickName+" | "+p.Name;
            IGH_Param source;
            if(values.Length==0)
            {
                if(!p.Optional) throw new Exception("Missing required example input: "+label);
                Optional(c,p.Name,label); continue;
            }
            if(values.Length==1 && values[0] is GH_Boolean b) source=Toggle(key,label,b.Value);
            else if(values.Length==1 && values[0] is GH_Integer n && p is Param_Integer pi && pi.HasNamedValues)
            {
                var f=typeof(Param_Integer).GetField("m_namedValues",BindingFlags.Instance|BindingFlags.NonPublic);
                var choices=((IEnumerable)f.GetValue(pi)).Cast<object>().Select(o=>(
                    (string)o.GetType().GetField("Name").GetValue(o),(int)o.GetType().GetField("Value").GetValue(o))).ToArray();
                source=Choice(key,label,choices,n.Value);
            }
            else if(values.Length==1 && values[0] is GH_Integer i) source=Slider(key,label,i.Value,Math.Min(0,i.Value),Math.Max(10,i.Value*2),true);
            else if(values.Length==1 && values[0] is GH_Number real) source=Slider(key,label,real.Value,Math.Min(0,real.Value*2),Math.Max(1,real.Value*2));
            else source=Text(key,label,string.Join("\n",values.Select(g=>g.ScriptVariable() is IFormattable v?v.ToString(null,Inv):g.ToString())));
            source.Description=p.Description; Set(c,p.Name,source);
        }
        Group(null);
    }
    private void Build()
    {
        Group("01 | Campate, tracciato e travi");
        var layout=Add("BridgeParametricLayoutComponent","Campate e tracciato",1100,500);
        Set(layout,"Span Count",Slider("spans","Numero campate",3,1,12,true));
        Set(layout,"Span Length",Slider("length","Lunghezza campata regolare [mm]",30000,5000,80000));
        Optional(layout,"Span Lengths","Lunghezze campate diverse [mm] — sostituiscono numero e luce");
        Set(layout,"Girder Count",Slider("girders","Numero travi longitudinali",3,2,10,true));
        Set(layout,"Girder Spacing",Slider("spacing","Interasse regolare travi [mm]",3500,1000,10000));
        Optional(layout,"Girder Offsets","Offset trasversali non uniformi [mm] — destra negativi");
        Scalar(layout,"Left Overhang","Sbalzo laterale sinistro soletta [mm]",1500,0,4000);
        Scalar(layout,"Right Overhang","Sbalzo laterale destro soletta [mm]",1500,0,4000);
        Scalar(layout,"Start Overhang","Prolungamento prima del primo appoggio [mm]",0,0,15000);
        Scalar(layout,"End Overhang","Prolungamento dopo ultimo appoggio [mm]",0,0,15000);
        Set(layout,"Radius",Slider("radius","Raggio in pianta [mm] — 0 rettilineo; segno = lato curva",0,-1000000,1000000));
        Panel(layout,"Origin","Origine del tracciato [mm]","0,0,0");
        Scalar(layout,"Heading","Rotazione iniziale tracciato [deg]",0,-180,180);
        Optional(layout,"Custom Alignment","Tracciato Rhino 3D — lunghezza = campate + prolungamenti");
        Scalar(layout,"Bracing Spacing","Passo massimo controventi longitudinali [mm]",6000,1000,15000);
        Optional(layout,"Bearing Types Override","Tipi appoggi personalizzati — ordine appoggio poi trave");

        Group("02 | Soletta, quote, mesh e sghembi");
        var bridge=Add("SimpleBridgeConfigurableComponent","Ponte geometrico",2200,1300);
        Wire(layout,"Alignment",bridge,"Alignment"); Wire(layout,"Support Stations",bridge,"Support Stations");
        Wire(layout,"Girder Offsets",bridge,"Girder Offsets"); Wire(layout,"Deck Widths",bridge,"Deck Widths");
        Scalar(bridge,"Girder Levels","Quota assi travi rispetto all'estradosso [mm]",-1550,-5000,-300);
        Set(bridge,"Deck Thickness",Slider("thickness","Spessore soletta [mm]",300,150,500));
        Scalar(bridge,"Crossfall","Pendenza trasversale soletta [%]",0,-8,8);
        Scalar(bridge,"Maximum Element Length","Lunghezza massima elementi longitudinali [mm]",3000,500,6000);
        Scalar(bridge,"Chord Tolerance","Tolleranza discretizzazione in curva [mm]",5,0.1,20);
        Scalar(bridge,"Support Skew","Sghembo appoggi [deg] — prevedere prolungamenti agli estremi",0,-45,45);
        Set(bridge,"Labels",Toggle("labels","Mostra etichette geometriche",false));
        Set(bridge,"Deck Beam Spacing",Slider("deck-spacing","Passo massimo aste trasversali soletta [mm]",3000,500,6000));
        Optional(bridge,"Deck Beam Stations","Progressive soletta personalizzate [mm] — sostituiscono il passo");
        Optional(bridge,"Deck Beam Level","Quota asse soletta [mm] — vuoto = metà spessore");
        Set(bridge,"Truss Diaphragms",Toggle("truss","Diaframmi reticolari — False = anima piena",false));

        Group("03 | Conci e diaframmi");
        var segments=Add("BridgeSegmentsComponent","Conci di officina",1100,2600);
        Optional(segments,"Joints","Giunti [mm] — rami {0} comuni, {n} trave n");
        Scalar(segments,"Maximum Length","Lunghezza massima concio [mm]",12000,0,24000);
        Wire(segments,"Segments",bridge,"Segments");
        var dia=Add("BridgeDiaphragmsComponent","Disposizione diaframmi",1100,3400);
        Optional(dia,"Stations","Progressive diaframmi personalizzate [mm]");
        Scalar(dia,"Maximum Spacing","Passo massimo diaframmi [mm]",6000,0,15000);
        Set(dia,"At Supports",Toggle("at-supports","Diaframmi sulle linee di appoggio",true));
        Scalar(dia,"Truss Height","Altezza reticolo dei diaframmi [mm]",1800,500,4000);
        Scalar(dia,"Panels per Bay","Numero pannelli reticolari fra due travi",2,1,8,true);
        Set(dia,"Pattern",Choice("pattern","Schema diaframma reticolare",new[]{("X",0),("V",1),("Warren",2)},0));
        Set(dia,"Connect X",Toggle("truss-cross","Nodo all'incrocio delle diagonali X",false));
        Wire(dia,"Diaphragms",bridge,"Diaphragms");

        Group("04 | Controventi longitudinali");
        var bracing=Add("BridgeLongitudinalBracingComponent","Controventi longitudinali",3100,2300);
        Wire(bridge,"Bridge Geometry",bracing,"Bridge Geometry"); Wire(layout,"Bracing Stations",bracing,"Stations");
        Optional(bracing,"Bays","Campi tra travi da controventare — vuoto = tutti");
        Set(bracing,"Pattern",Choice("bracing-pattern","Schema controventi longitudinali",new[]{("X",0),("V",1),("Alternati",2)},0));
        Scalar(bracing,"Normal Offset","Offset normale controventi dagli assi travi [mm]",0,-3000,3000);
        Set(bracing,"Connect X",Toggle("bracing-cross","Nodo all'incrocio controventi X",false));
        Optional(bracing,"Omitted Panels","Numeri campi longitudinali da omettere");
        var selected=Standard("List Item",3800,2200); selected.NickName="Geometria con/senza controventi";
        selected.Params.Input[0].AddSource(Out(bridge,"Bridge Geometry")); selected.Params.Input[0].AddSource(Out(bracing,"Bridge Geometry"));
        selected.Params.Input[0].DataMapping=GH_DataMapping.Flatten;
        selected.Params.Input[1].AddSource(Toggle("bracing-enabled","Aggiungi controventi longitudinali",true));

        Group("05 | Materiali e sezioni FEM");
        var steel=Add("MaterialSteelComponent","Materiale carpenteria",1100,4600);
        Panel(steel,"Name","Nome materiale acciaio","Acciaio carpenteria");
        Set(steel,"Grade",Choice("steel-grade","Classe acciaio",new[]{("S235",0),("S275",1),("S355",2),("S450",3)},2));
        var concrete=Add("MaterialConcreteComponent","Materiale soletta",1100,5000);
        Panel(concrete,"Name","Nome materiale calcestruzzo","Calcestruzzo soletta");
        Set(concrete,"Grade",Choice("concrete-grade","Classe calcestruzzo",new[]{("C25/30",4),("C28/35",5),("C32/40",6),("C35/45",7),("C40/50",8),("C45/55",9),("C50/60",10)},6));
        GH_Component ISection(string name,double[] values,float y)
        {
            var c=Add("BeamSectionIComponent",name,1800,y);
            string[] labels={"Larghezza ala inferiore","Larghezza ala superiore","Altezza totale","Spessore ala inferiore","Spessore ala superiore","Spessore anima","Raggio raccordo"};
            for(int i=0;i<values.Length;i++) Scalar(c,c.Params.Input[i].Name,name+" | "+labels[i]+" [mm]",values[i],i==6?0:1,Math.Max(50,values[i]*2));
            return c;
        }
        var mainSection=ISection("Sezione travi principali",new[]{600d,600,2500,30,30,16,0},5600);
        var diaSection=ISection("Sezione diaframma pieno",new[]{300d,300,900,20,20,12,0},6400);
        GH_Component Tube(string name,double size,double t,float y)
        {
            var c=Add("BeamSectionHollowRectComponent",name,1800,y);
            Scalar(c,"Height",name+" | Altezza [mm]",size,50,600); Scalar(c,"Base",name+" | Larghezza [mm]",size,50,600);
            Scalar(c,"Horizontal Thickness",name+" | Spessore orizzontale [mm]",t,2,30);
            Scalar(c,"Vertical Thickness",name+" | Spessore verticale [mm]",t,2,30); return c;
        }
        var chord=Tube("Correnti reticolari",250,10,7200); var post=Tube("Montanti reticolari",200,8,7800);
        var diagonal=Tube("Diagonali e controventi",180,8,8400);
        GH_Component Property(GH_Component section,string name,float y)
        {
            var c=Add("BeamPropertyComponent",name,2800,y); Panel(c,"Name",name+" | Nome proprietà",name);
            Set(c,"Type",Choice(c.InstanceGuid+"Type",name+" | Formulazione FEM",new[]{("Beam con flessione e torsione",5),("Truss solo assiale",2)},5));
            Wire(section,"Section",c,"Section"); Wire(steel,"Material",c,"Material"); return c;
        }
        var mainProperty=Property(mainSection,"Travi principali",5600); var diaProperty=Property(diaSection,"Traversi pieni",6400);
        var chordProperty=Property(chord,"Correnti",7200); var postProperty=Property(post,"Montanti",7800);
        var diagonalProperty=Property(diagonal,"Diagonali",8400);
        Group("05b | Sezioni per concio — modifica una riga per ogni Gx-Cy");
        var segmentSections=Add("BridgeSegmentSectionsComponent","Sezione di ogni concio",3500,4400);
        Wire(selected.Params.Output[0],segmentSections,"Bridge Geometry"); Wire(mainProperty,"Property",segmentSections,"Base Property");
        var sectionTable=Control("segment-sections","SEZIONI CONCI [mm] — * = slider sezione base",new GH_Panel {UserText=BridgeSegmentSections.Header});
        sectionTable.Properties.Multiline=false;
        sectionTable.Attributes.Bounds=new RectangleF(60,sectionTable.Attributes.Pivot.Y,840,600); inputY+=530;
        Set(segmentSections,"Section Table",sectionTable);
        var assign=Add("BridgeMemberAssignmentComponent","Carpenteria FEM per famiglia",4200,5000);
        Wire(selected.Params.Output[0],assign,"Bridge Geometry");
        var properties=new[]{mainProperty,diaProperty,chordProperty,chordProperty,postProperty,diagonalProperty,diagonalProperty,postProperty};
        // Repeated sources are deduplicated by GH; use explicit family rules for the shared chord/diagonal properties.
        Set(assign,"Families",Text("families","Famiglia proprietà di base — -1 tutte le famiglie acciaio","-1"));
        Wire(mainProperty,"Property",assign,"Default Properties");
        Wire(segmentSections,"Schedule",assign,"Schedule");
        for(int family=1;family<8;family++)
        {
            var rule=Add("BridgeSectionScheduleComponent","Proprietà famiglia "+family,3500,5700+(family-1)*360);
            Wire(properties[family],"Property",rule,"Property"); Wire(layout,"Length",rule,"End");
            Scalar(rule,"Start","Famiglia "+family+" | Inizio assegnazione [mm]",0,0,200000);
            Scalar(rule,"Family","Codice famiglia: "+family,family,0,8,true);
            Wire(rule,"Schedule",assign,"Schedule");
        }
        In(assign,"Schedule").DataMapping=GH_DataMapping.Flatten;
        var slab=Add("BridgeSlabGrillageComponent","Fasce soletta FEM",4200,9000);
        Wire(bridge,"Grillage Geometry",slab,"Grillage Geometry"); Wire(concrete,"Material",slab,"Concrete Material");

        Group("06 | Appoggi, eccentricità e assemblaggio");
        var bearings=Add("BridgeBearingsComponent","Appoggi orientati",3100,9700);
        Wire(selected.Params.Output[0],bearings,"Bridge Geometry"); Wire(layout,"Bearing Types",bearings,"Types");
        Optional(bearings,"Supports","Selezione appoggi — vuoto = tutti"); Optional(bearings,"Girders","Selezione travi agli appoggi — vuoto = tutte");
        Scalar(bearings,"Guide Angle","Rotazione guide degli appoggi [deg]",0,-180,180);
        Panel(bearings,"Translation Stiffness","Rigidezze elastiche Kx,Ky,Kz [N/mm]","0,0,0");
        Panel(bearings,"Rotation Stiffness","Rigidezze elastiche Krx,Kry,Krz [N mm/rad]","0,0,0");
        Optional(bearings,"Orientation","Piano degli appoggi — vuoto = tangente orizzontale");
        var links=Add("BridgeConnectionsComponent","Collegamenti eccentrici trave-soletta",3100,10300);
        Wire(bridge,"Deck Connections",links,"Connections"); In(links,"Connections").DataMapping=GH_DataMapping.Flatten;
        Set(links,"Mode",Choice("connection-mode","Collegamento trave-soletta",new[]{("Link cinematico rigido",0),("Asta deformabile con proprietà esplicita",1)},0));
        Optional(links,"Property","Proprietà aste di collegamento — necessaria solo nel modo 1");
        GH_Component Model(string name,float y,bool withDeck)
        {
            var c=Add("BuildModelComponent",name,4900,y); Panel(c,"Name",name+" | Nome modello",name);
            Set(c,"Units",Choice(c.InstanceGuid+"Units",name+" | Unità del modello",new[]{("N, mm, MPa",(int)ModelModel.ModelUnits.Nmm)},(int)ModelModel.ModelUnits.Nmm));
            Wire(assign,"Beams",c,"Elements"); Wire(bearings,"Nodes",c,"Elements");
            if(withDeck){Wire(slab,"Beams",c,"Elements"); Wire(links,"Elements",c,"Elements");}
            In(c,"Elements").DataMapping=GH_DataMapping.Flatten; return c;
        }
        var steelModel=Model("Modello sola carpenteria",4800,false); var model=Model("Modello graticcio con soletta",9000,true);
        var decompose=Add("DecomposeModelComponent","Entità FEM assemblate",5500,9200); Wire(model,"Model",decompose,"Model");
        var audit=Add("BridgeModelAuditComponent","Controllo connessioni e proprietà",5500,9900); Wire(model,"Model",audit,"Model");
        Scalar(audit,"Tolerance","Tolleranza audit del modello [mm]",.001,.001,1);

        Group("07 | Soletta e armature per il successivo verificatore");
        var rebar=Add("MaterialRebarComponent","Materiale armature B450C",1800,11000);
        var slabCheck=Add("BeamSectionConcreteSlabComponent","Sezione soletta per il misto",2800,11000);
        Set(slabCheck,"Height",controls["thickness"]);
        Scalar(slabCheck,"Width","Larghezza efficace soletta per singola trave [mm]",3500,500,10000);
        Scalar(slabCheck,"Rebar area","Area totale armatura superiore sulla larghezza efficace [mm²]",3500,0,15000);
        Scalar(slabCheck,"Top concrete cover","Distanza asse armatura superiore dall'estradosso [mm]",50,10,150);
        Scalar(slabCheck,"Bottom rebar area","Area totale armatura inferiore sulla larghezza efficace [mm²]",3500,0,15000);
        Scalar(slabCheck,"Bottom concrete cover","Distanza asse armatura inferiore dall'intradosso [mm]",50,10,150);

        Group("08 | Visualizzazione");
        var display=Add("DisplaySettingsComponent","Opzioni preview",4200,11500);
        var preview=Add("PreviewModelComponent","Preview del graticcio",4900,11200); preview.Hidden=false;
        Wire(model,"Model",preview,"Model / elements"); Wire(display,"Settings",preview,"Settings");
        Defaults();

        string[] labels={"Assi carpenteria per famiglia","ID degli assi","Travi longitudinali per concio","Diaframmi per famiglia",
            "Nodi geometrici carpenteria","Punti di appoggio","Punti dei giunti di concio","Volume geometrico soletta",
            "Bordi soletta","Riferimenti locali","Progressive mesh","Tabella topologia","Tabella conci","Informazioni geometria",
            "Geometria carpenteria di base","Assi trasversali soletta","Assi eccentricità trave-soletta","Progressive fasce soletta",
            "Larghezze tributarie fasce","Geometria acciaio + fasce soletta","ID fasce soletta"};
        for(int i=0;i<labels.Length;i++) Output(bridge.Params.Output[i],labels[i]);
        Output(selected.Params.Output[0],"Geometria carpenteria con opzione controventi");
        Output(bracing,"Added Members","Assi controventi generati — secondo selettore per il modello");
        Output(layout,"Alignment","Tracciato effettivo"); Output(layout,"Support Stations","Progressive appoggi");
        Output(layout,"Girder Offsets","Offset effettivi travi"); Output(layout,"Span Lengths","Luci effettive campate");
        Output(layout,"Bracing Stations","Progressive campi controventi");
        outputY=4800;
        Output(assign,"Beams","Elementi FEM carpenteria"); Output(assign,"Physical IDs","ID fisici carpenteria");
        Output(assign,"Unassigned","Assi privi di proprietà — deve essere vuoto");
        foreach(var p in new[]{mainProperty,diaProperty,chordProperty,postProperty,diagonalProperty}) Output(p,"Property","Proprietà FEM | "+p.NickName);
        Output(steelModel,"Model","MODELLO ACCIAIO — base per fasi di getto");
        Output(segmentSections,"Sections","Sezione acciaio di ciascun concio — per sezioni miste e override");
        Output(segmentSections,"Properties","Proprietà FEM di ciascun concio");
        Output(segmentSections,"Segment IDs","ID ordinati delle sezioni — G = trave, C = concio");
        Output(segmentSections,"Schedule","Regole effettive di assegnazione delle sezioni ai conci");
        Output(segmentSections,"Resolved Table","Dimensioni effettive di ciascuna sezione [mm]");
        Output(segmentSections,"Top Offsets","Quota ala superiore rispetto al baricentro, per concio [mm]");
        Output(segmentSections,"Information","Diagnostica sezioni e aggiornamento tabella conci");
        outputY=9000;
        Output(slab,"Beams","Elementi FEM fasce soletta"); Output(slab,"Properties","Proprietà individuali fasce soletta");
        Output(slab,"Strip Widths","Larghezze individuali fasce FEM [mm]"); Output(slab,"Member IDs","ID fasce FEM");
        Output(bearings,"Nodes","Nodi con vincoli e molle"); Output(bearings,"Frames","Piani locali dei vincoli"); Output(bearings,"Labels","ID appoggio e tipo vincolo");
        Output(links,"Elements","Link rigidi oppure aste di collegamento");
        Output(model,"Model","MODELLO GRATICCIO — ingresso per Straus");
        Output(decompose,"Nodes","Tutti i nodi FEM assemblati"); Output(decompose,"Beams","Tutti gli elementi beam assemblati");
        Output(audit,"No input errors","Audit: assenza errori negli input"); Output(audit,"Code","Codici diagnostici"); Output(audit,"Description","Descrizione diagnostica del modello");
        Output(steel,"Material","Materiale acciaio"); Output(concrete,"Material","Materiale calcestruzzo");
        Output(rebar,"Material","Materiale armature B450C"); Output(slabCheck,"Concrete Slab","Soletta con due file di armature per sezione mista");
        Output(mainSection,"Section","Sezione acciaio BASE — le sezioni effettive sono distinte per concio");
        var note=new GH_Panel {NickName="COME USARE IL PONTE PARAMETRICO",UserText=
            "49 | PONTE PARAMETRICO — INPUT A SINISTRA / COMPONENTI AL CENTRO / OUTPUT A DESTRA\n"+
            "Unità: N, mm, MPa. Slider numero campate/travi, luce/interasse; liste opzionali per geometrie non regolari.\n"+
            "Diaframmi reticolari: True = X/V/Warren; False = anima piena. La preview del modello è in basso al centro.\n"+
            "SEZIONI CONCI: tabella nel gruppo 05b. Una riga per Gx-Cy; * segue gli slider base. Pulsante Tabella conci per aggiornare i giunti.\n"+
            "La soletta è a graticcio: assi trasversali alla quota reale, link eccentrici e proprietà per ogni fascia.\n"+
            "La quota degli assi travi è un input indipendente: aggiornarla se cambia il baricentro della sezione.\n"+
            "Modelli senza carichi e senza analisi. La rigidezza longitudinale mista va assegnata nelle fasi tramite override delle sezioni.\n"+
            "Il modello è predisposto per Straus. Per luci/interassi/sezioni modificati: verificare appoggi, proprietà e calibrazione del graticcio.\n"+
            "README e script di rigenerazione nella cartella 49-ponte-parametrico."};
        note.CreateAttributes(); note.Attributes.Pivot=new PointF(60,-900); note.Attributes.Bounds=new RectangleF(60,-900,6500,350); doc.AddObject(note,false);
    }

    private static void Solve(GH_Document d)
    {
        GH_Document.EnableSolutions=true; d.Enabled=true; d.NewSolution(false,GH_SolutionMode.Silent);
        var errors=d.Objects.OfType<GH_ActiveObject>().SelectMany(o=>o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>o.Name+": "+e)).ToArray();
        if(errors.Length>0) throw new Exception(string.Join("\n",errors));
    }
    private static GH_Component Find(GH_Document d,string type)=>d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name==type);
    private static object Value(IGH_Param p)=>p.VolatileData.AllData(true).Single().ScriptVariable();
    private static object Validate(GH_Document d)
    {
        var geo=(SimpleBridgeGeometry)Value(Out(Find(d,"SimpleBridgeConfigurableComponent"),"Grillage Geometry"));
        var model=(ModelModel)Value(Out(d.Objects.OfType<GH_Component>().Single(c=>c.NickName=="Modello graticcio con soletta"),"Model"));
        var steel=Find(d,"BridgeMemberAssignmentComponent"); var slab=Find(d,"BridgeSlabGrillageComponent");
        var missing=Out(steel,"Unassigned").VolatileData.AllData(true).Select(g=>g?.ScriptVariable()?.ToString()).Where(s=>!string.IsNullOrEmpty(s)).ToArray();
        ParametricBridgeTests.Require(missing.Length==0,"Every steel member has a property: "+string.Join(", ",missing.Take(8)));
        ParametricBridgeTests.Require(model.Beams.Count==Out(steel,"Beams").VolatileDataCount+Out(slab,"Beams").VolatileDataCount,
            $"All steel and concrete beams assembled: model={model.Beams.Count}, steel={Out(steel,"Beams").VolatileDataCount}, slab={Out(slab,"Beams").VolatileDataCount}");
        var issues=BridgeModelAudit.Inspect(model,1e-3);
        ParametricBridgeTests.Require(!issues.Any(i=>i.Severity=="Error"||i.Code=="DISCONNECTED"),string.Join("; ",issues.Select(i=>i.ToString())));
        ParametricBridgeTests.Require(model.Nodes.Count(n=>n.Attributes.OfType<NodeRestraintAttributeModel>().Any())==geo.Supports.Count,"All bearings assembled");
        ParametricBridgeTests.Require(model.Elements.All(e=>e.Results.Count==0),"No invented analysis results");
        var segmentSections=Find(d,"BridgeSegmentSectionsComponent");
        var sectionIds=Out(segmentSections,"Segment IDs").VolatileData.AllData(true).Select(g=>g.ScriptVariable().ToString()).ToArray();
        ParametricBridgeTests.Require(sectionIds.SequenceEqual(geo.Segments.OrderBy(s=>s.Girder).ThenBy(s=>s.Segment).Select(BridgeSegmentSections.Id)),
            "Exactly one keyed steel section for every concio, independent of FEM mesh");
        foreach(var beam in Out(steel,"Beams").VolatileData.AllData(true).Select(g=>(BeamModel)g.ScriptVariable()))
        {
            var meta=beam.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value;
            if(meta.Family==BridgeMemberFamily.Girder)
                ParametricBridgeTests.Require(beam.BeamProperty.Id==$"Concio G{meta.Group}-C{meta.Segment}","Concio properties reach assigned FEM beams");
        }
        var controls=d.Objects.OfType<IGH_Param>().Where(p=>p.SourceCount==0).ToArray();
        ParametricBridgeTests.Require(controls.All(p=>p.Attributes.Pivot.X<700),"All input controls lie on the left");
        var outputs=d.Objects.OfType<IGH_Param>().Where(p=>p.Attributes.Pivot.X>6000).ToArray();
        ParametricBridgeTests.Require(outputs.Length>40 && outputs.All(p=>p.SourceCount==1 && p.Name==p.NickName &&
            d.Objects.OfType<GH_Group>().Any(g=>g.NickName==p.Name && g.ObjectIDs.Contains(p.InstanceGuid))),
            "Every right-hand output has an explicit name and a visible group caption, also after reopening");
        var hidden=d.Objects.OfType<GH_Component>().SelectMany(c=>c.Params.Input.Where(p=>p.SourceCount==0 || Persistent(p)?.IsEmpty==false).Select(p=>c.NickName+" / "+p.Name)).ToArray();
        ParametricBridgeTests.Require(hidden.Length==0,"Every input is explicitly connected with no embedded values: "+string.Join(", ",hidden));
        return new {beams=model.Beams.Count,nodes=model.Nodes.Count,slabBeams=Out(slab,"Beams").VolatileDataCount,
            supports=geo.Supports.Count,segmentSections=sectionIds.Length,objects=d.Objects.Count,sliders=controls.OfType<GH_NumberSlider>().Count(),warnings=issues.Select(i=>i.ToString()).ToArray()};
    }
    private static void RefreshSectionTable(GH_Document d)
    {
        // A changed fabrication layout deliberately invalidates the old table. Solve geometry first,
        // then invoke exactly the explicit button action used by the operator and revalidate everything.
        GH_Document.EnableSolutions=true; d.Enabled=true; d.NewSolution(false,GH_SolutionMode.Silent);
        var component=Find(d,"BridgeSegmentSectionsComponent");
        component.GetType().GetMethod("UpdateTablePanel").Invoke(component,null);
        Solve(d);
    }
    internal static int Run()
    {
        try
        {
            ParametricBridgeTests.Run();
            BridgeSegmentSectionsTests.Run();
            var example=new ParametricBridgeExample(); using var document=example.doc; example.Build(); Solve(document);
            RefreshSectionTable(document);
            var baseline=Validate(document); string folder=ExamplePaths.Case(Id); string path=Path.Combine(folder,Id+".ghx");
            string temporary=path+".tmp.ghx";
            Directory.CreateDirectory(folder); GrasshopperBridgeExample.Write(document,temporary);
            using(var reopened=GrasshopperBridgeExample.Read(temporary)) { Solve(reopened); Validate(reopened); }
            var sectionPanel=(GH_Panel)example.controls["segment-sections"];
            string originalTable=sectionPanel.UserText;
            var edit=originalTable.Split(new[]{"\r\n","\n"},StringSplitOptions.None);
            edit[1]=BridgeSegmentSectionsTests.Edit(edit[1],6,"50");
            sectionPanel.UserText=string.Join(Environment.NewLine,edit); sectionPanel.ExpireSolution(false); Solve(document); Validate(document);
            void CheckEditedModel(GH_Document d)
            {
                var assembled=(ModelModel)Value(Out(d.Objects.OfType<GH_Component>().Single(c=>c.NickName=="Modello graticcio con soletta"),"Model"));
                int editedCount=0;
                foreach(var b in assembled.Beams)
                {
                    var m=b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value;
                    if(m.Family!=BridgeMemberFamily.Girder) continue;
                    bool edited=m.Group==1 && m.Segment==1;
                    ParametricBridgeTests.Require(b.BeamProperty.T1==(edited?50:30),"External panel edit affects exactly G1-C1 in the assembled FEM model");
                    if(edited) editedCount++;
                }
                ParametricBridgeTests.Require(editedCount>0,"Edited concio is present in the assembled model");
            }
            CheckEditedModel(document);
            string editedPath=Path.GetFullPath(Path.Combine(folder,"..","..","artifacts","parametric-bridge-edited.ghx"));
            GrasshopperBridgeExample.Write(document,editedPath);
            using(var reopenedEdit=GrasshopperBridgeExample.Read(editedPath)) { Solve(reopenedEdit); Validate(reopenedEdit); CheckEditedModel(reopenedEdit); }
            RefreshSectionTable(document);
            ParametricBridgeTests.Require(sectionPanel.UserText.Contains("; 50;"),"Refresh button preserves an edited row on unchanged geometry");
            sectionPanel.UserText=originalTable; sectionPanel.ExpireSolution(false); Solve(document);
            Console.WriteLine("SEGMENT SECTIONS COMPONENT PASS: external panel populated, edited and refreshed; individual properties reach the assembled FEM model.");
            var scenarios=new List<object>();
            void Change(string name,double value) { var s=(GH_NumberSlider)example.controls[name]; s.SetSliderValue((decimal)value); s.ExpireSolution(false); }
            foreach(var s in new[]{(1,2,false,0d),(2,4,true,0d),(3,3,true,250000d),(4,5,false,-400000d)})
            {
                Change("spans",s.Item1); Change("girders",s.Item2); Change("radius",s.Item4);
                var toggle=(GH_BooleanToggle)example.controls["truss"]; toggle.Value=s.Item3; toggle.ExpireSolution(false);
                RefreshSectionTable(document); var data=Validate(document); scenarios.Add(new{spans=s.Item1,girders=s.Item2,truss=s.Item3,radius=s.Item4,data});
                Console.WriteLine($"PARAMETRIC SCENARIO PASS: {s.Item1} spans, {s.Item2} girders, truss={s.Item3}, R={s.Item4}.");
            }
            void Explicit(string type,string port,double[] values)
            {
                var p=(Param_Number)In(Find(document,type),port).Sources.Single(); p.PersistentData.Clear();
                p.SetPersistentData(values); p.ExpireSolution(false);
            }
            Explicit("BridgeParametricLayoutComponent","Span Lengths",new[]{12000d,18000,24000});
            Explicit("BridgeParametricLayoutComponent","Girder Offsets",new[]{-4000d,-500,4000});
            Explicit("SimpleBridgeConfigurableComponent","Deck Beam Stations",new[]{0d,2100,5000,18000,31000,54000});
            var braceToggle=(GH_BooleanToggle)example.controls["bracing-enabled"]; braceToggle.Value=false; braceToggle.ExpireSolution(false);
            RefreshSectionTable(document); scenarios.Add(new{name="Campate, interassi e fasce non uniformi; controventi disattivati",data=Validate(document)});
            var actualSteel=Out(Find(document,"BridgeMemberAssignmentComponent"),"Beams").VolatileData.AllData(true).Select(g=>(BeamModel)g.ScriptVariable());
            ParametricBridgeTests.Require(actualSteel.All(b=>b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Family is not (BridgeMemberFamily.LongitudinalBracing or BridgeMemberFamily.BracingPost)),"Bracing toggle actually removes the bracing from the model");
            ParametricBridgeTests.Require(Out(Find(document,"BridgeSlabGrillageComponent"),"Beams").VolatileDataCount==24,"Explicit slab stations replace regular spacing");
            Console.WriteLine("PARAMETRIC SCENARIO PASS: explicit unequal spans/offsets/slab stations override sliders; bracing disabled.");
            File.Move(temporary,path,true);
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonSerializer.Serialize(new{baseline,reopened=true,allInputsExternal=true,allOutputsCaptioned=true,
                segmentTablePopulated=true,editedSectionCheckedInAssembledModel=true,editedSectionReopened=true,solverInvoked=false,scenarios},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("PARAMETRIC BRIDGE EXAMPLE PASS: saved/reopened, five live variants, external named controls and assembled FEM models; no solver invoked."); return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("PARAMETRIC BRIDGE EXAMPLE FAILED: "+ex); return 1;}
    }
}
