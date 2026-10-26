using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.MovingLoads;
using Grasshopper.GUI.Base;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

/// <summary>Extend the user's actual REV002; never regenerate its geometry or section workbook.</summary>
internal static class BridgeMovingRevisionExample
{
    internal const string FileName="50-ponte-quattro-modelli_REV003.ghx";
    internal static IGH_Param In(GH_Component c,string name)=>c.Params.Input.Single(p=>p.Name==name);
    internal static IGH_Param Out(GH_Component c,string name)=>c.Params.Output.Single(p=>p.Name==name);
    internal static T[] Values<T>(IGH_Param p)=>p.VolatileData.AllData(true).Select(g=>g?.ScriptVariable()).OfType<T>().ToArray();
    internal static GH_Component Find(GH_Document d,string type,string prefix=null)=>d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name==type&&(prefix==null||c.NickName.StartsWith(prefix,StringComparison.Ordinal)));
    private static void Wire(IGH_Param target,params IGH_Param[] sources){target.RemoveAllSources();foreach(var s in sources)target.AddSource(s);}
    internal static void Press(GH_Component c){c.GetType().GetField("_run",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(c,true);c.ExpireSolution(false);}
    internal static void Clear(GH_Component c)
    {
        var f=c.GetType().GetField("_model",BindingFlags.NonPublic|BindingFlags.Instance);f?.SetValue(c,Activator.CreateInstance(f.FieldType));
        c.GetType().GetField("_run",BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(c,false);
        c.GetType().GetField("_inputHash",BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(c,null);
    }
    internal static string Errors(GH_Document d)=>string.Join("\n",d.Objects.OfType<GH_ActiveObject>().SelectMany(o=>o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m=>o.NickName+": "+m)));
    internal static void Require(bool ok,string message){if(!ok)throw new Exception("REV003: "+message);}
    private static void CheckBeforeSteelAnalysis(GH_Document doc)
    {
        // REV002's cracked-section dispatch needs M01/Dead. Until it exists, the downstream
        // partial models contain slab loads only; retain the exact missing-case diagnostics.
        var unexpected=doc.Objects.OfType<GH_ActiveObject>().SelectMany(o=>o.RuntimeMessages(GH_RuntimeMessageLevel.Error)
            .Where(m=>!(o.GetType().Name=="BridgeAnalysisCasesComponent"&&
                ((o.NickName.StartsWith("M02_NINF",StringComparison.Ordinal)&&m=="Risultati assenti per il solutore selezionato: M02_NINF_TRAVI")||
                 (o.NickName.StartsWith("M03_N0",StringComparison.Ordinal)&&m=="Risultati assenti per il solutore selezionato: M03_N0_TRAVI"))))
            .Select(m=>o.NickName+": "+m));
        Require(!unexpected.Any(),string.Join("\n",unexpected));
    }
    internal static int Run(string[] args)
    {
        BridgeExampleMigration.Initialize();
        string folder=Path.Combine(ExamplePaths.Root,"50-ponte-quattro-modelli"),target=Path.Combine(folder,FileName);
        if(args.Contains("--capture")){using var capture=GrasshopperBridgeExample.Read(target);Example50HorizontalLayout.CaptureMovingRevision(capture);return 0;}
        if(args.Contains("--loads")){BridgeMovingSlabTargets.Run(Path.GetFullPath("artifacts/rev003-native"));return 0;}
        if(args.Contains("--native"))return BridgeMovingRevisionNative.Run(target);
        if(args.Contains("--check")){Validate(target);return 0;}
        string source=Path.Combine(folder,"50-ponte-quattro-modelli_REV002.ghx"),sourceBook=Path.Combine(folder,"Sezioni_conci_REV002.xlsx"),book=Path.Combine(folder,"Sezioni_conci_REV003.xlsx");
        byte[] original=File.ReadAllBytes(source),originalBook=File.ReadAllBytes(sourceBook);
        if(File.Exists(target)||(File.Exists(book)&&!File.ReadAllBytes(book).SequenceEqual(originalBook)))throw new IOException("REV003 already exists; use --check or --native without overwriting user changes.");
        Directory.CreateDirectory(Path.Combine(folder,"calcoli","REV003"));File.WriteAllBytes(book,originalBook);
        using var doc=GrasshopperBridgeExample.Read(source);doc.FilePath=target;
        foreach(var c in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="StrausR3AnalysisComponent"))
        {
            Clear(c);foreach(var t in In(c,"Keep Running").Sources.OfType<GH_BooleanToggle>())t.Value=false;
            foreach(var p in In(c,"Output Path").Sources.OfType<GH_Panel>())p.UserText=p.UserText.Replace("REV002","REV003");
        }
        foreach(var p in doc.Objects.OfType<GH_Panel>())
        {
            p.UserText=p.UserText.Replace("Sezioni_conci_REV002.xlsx","Sezioni_conci_REV003.xlsx").Replace("Report_50_REV002.docx","Report_50_REV003.docx");
            if(p.NickName=="PROCEDURA REV002 E IPOTESI")
            {p.NickName=p.Name="PROCEDURA REV003 E IPOTESI";p.UserText=p.UserText.Replace("REV002","REV003")+"\nTRAFFICO: Run Moving Static Analysis dopo M01; n0 conserva la mappa da Dead. Posizioni ALTERNATIVE nella fase 30. Poi Run Build e verifiche.";}
        }
        foreach(var p in In(Find(doc,"BridgeReportComponent"),"Revision").Sources.OfType<GH_Panel>())p.UserText="REV003 — traffico mobile n0, posizioni alternative, ritiro non fessurato";
        var added=new List<GH_Component>();float cy=0;
        GH_Component Add(string type,string label){var c=BridgeSegmentExcelTests.Add(doc,type);c.NickName=label;c.Attributes.Pivot=new(2000,cy);cy+=160;added.Add(c);return c;}
        GH_Component Standard(string guid,string label)
        {
            var c=(GH_Component)Grasshopper.Instances.ComponentServer.EmitObjectProxy(new Guid(guid)).CreateInstance();c.CreateAttributes();c.NickName=label;
            c.Attributes.Pivot=new(2000,cy);cy+=160;doc.AddObject(c,false);added.Add(c);return c;
        }
        T Control<T>(string label,T p) where T:IGH_Param
        {
            p.Name=p.NickName=label;p.Description=label;p.CreateAttributes();p.Attributes.Pivot=new(900,cy);cy+=100;
            if(p is GH_Panel)p.Attributes.Bounds=new(900,cy-100,650,90);
            if(p is IGH_PreviewObject preview)preview.Hidden=true;doc.AddObject(p,false);return p;
        }
        IGH_Param Text(string label,string value)
        {if(value.Length==0){var p=new Param_String();p.SetPersistentData("");return Control(label,p);}return Control(label,new GH_Panel{UserText=value,Properties={Multiline=false}});}
        GH_NumberSlider Slider(string label,double value,double min,double max,int decimals=0)
        {
            var s=new GH_NumberSlider();s.Slider.Type=decimals==0?GH_SliderAccuracy.Integer:GH_SliderAccuracy.Float;s.Slider.DecimalPlaces=decimals;
            s.Slider.Minimum=(decimal)min;s.Slider.Maximum=(decimal)max;s.SetSliderValue((decimal)value);return Control(label,s);
        }
        IGH_Param Negative(IGH_Param input,string label)
        {var c=Standard("a3371040-e552-4bc8-b0ff-10a840258e88","M03_N0 | "+label);Wire(c.Params.Input[0],input);return c.Params.Output[0];}
        var wheel=Slider("TRAFFICO | Forza verticale per ruota [N]",150000,0,300000);
        var spacing=Slider("TRAFFICO | Interasse longitudinale dei due assi [mm]",1200,100,15000);
        var halfGauge=Slider("TRAFFICO | Semicarreggiata ruote [mm] — distanza totale = 2 volte",1000,100,2000);
        var rear=Negative(spacing,"Coordinata asse posteriore");var right=Negative(halfGauge,"Coordinata ruota destra");var fz=Negative(wheel,"Forza verso Z globale negativo");
        var vector=Standard("56b92eab-d121-43f7-94d3-6cd8f0ddead8","M03_N0 | Vettore forza per ruota [N]");
        Wire(vector.Params.Input[0],Text("TRAFFICO | Fx per ruota [N]","0"));Wire(vector.Params.Input[1],Text("TRAFFICO | Fy per ruota [N]","0"));Wire(vector.Params.Input[2],fz);
        var points=new List<GH_Component>();
        foreach(int axle in new[]{0,1})foreach(int side in new[]{0,1})
        {
            var p=Add("MovingPointComponent",$"M03_N0 | Asse {axle+1}, ruota {(side==0?"sinistra":"destra")}");
            Wire(In(p,"X"),axle==0?Text("TRAFFICO | Asse anteriore X nel veicolo [mm]","0"):rear);Wire(In(p,"Y"),side==0?halfGauge:right);Wire(In(p,"Force"),vector.Params.Output[0]);
            Wire(In(p,"Axes"),Text("TRAFFICO | Assi delle forze: 1 = globali","1"));points.Add(p);
        }
        var vehicle=Add("MovingVehicleComponent","M03_N0 | Veicolo di prova — 2 assi, 4 ruote");
        Wire(In(vehicle,"Name"),Text("TRAFFICO | Nome veicolo — nessun modello normativo automatico","TEST_2_ASSI_4_RUOTE"));
        Wire(In(vehicle,"Loads"),points.Select(p=>p.Params.Output[0]).ToArray());In(vehicle,"Loads").DataMapping=GH_DataMapping.Flatten;Wire(In(vehicle,"Lanes"),Text("TRAFFICO | Corsia del template","1"));
        var template=Add("MovingTemplateComponent","M03_N0 | Template della singola corsia");Wire(In(template,"Vehicles"),vehicle.Params.Output[0]);Wire(In(template,"Name"),Text("TRAFFICO | Nome template","REV003_TRAFFICO_TEST"));
        var path=Add("BridgeStraightMovingPathComponent","M03_N0 | Percorso alla quota delle aste di soletta");
        Wire(In(path,"Grillage Geometry"),Out(Find(doc,"BridgeGrillageMeshComponent"),"Grillage Geometry"));Wire(In(path,"Template"),template.Params.Output[0]);
        Wire(In(path,"Case"),Text("TRAFFICO | Caso sorgente del percorso","Q_MOV_N0"));
        Wire(In(path,"Lane offset"),Slider("TRAFFICO | Eccentricità corsia a sinistra del tracciato [mm]",1500,-4000,4000));Wire(In(path,"Divisions"),Slider("TRAFFICO | Divisioni del percorso — affinare per convergenza",30,5,200));
        var attach=Add("AttachMovingPathsComponent","M03_N0 | Stesso FEM n0 con percorso mobile");Wire(In(attach,"Model"),Out(Find(doc,"BuildModelComponent","M03_N0"),"Model"));Wire(In(attach,"Paths"),Out(path,"Path"));
        var moving=Add("MovingStaticAnalysisComponent","M03_N0 | ANALISI MOBILE — sole posizioni, senza peso proprio");
        Wire(In(moving,"Model"),Out(attach,"Model"));Wire(In(moving,"Output path"),Text("TRAFFICO | File di analisi mobile",Path.Combine(folder,"calcoli","REV003","M03_N0_MOBILE.st7")));
        Wire(In(moving,"Run"),Control("TRAFFICO | Analisi automatica — lasciare False, usare pulsante Run",new GH_BooleanToggle{Value=false}));
        var merge=Find(doc,"BuildBridgeModel");var oldTable=In(merge,"Combinations").Sources.ToArray();
        var combinations=Add("BridgeMovingCombinationsComponent","Tabella combinazioni mobili — una posizione alla volta");Wire(In(combinations,"Base table"),oldTable);Wire(In(combinations,"Moving cases"),Out(moving,"Cases"));
        Wire(In(combinations,"Base combination"),Text("TRAFFICO | Combinazione base cui aggiungere una posizione","DEMO_CUMULATIVA"));Wire(In(combinations,"Traffic factor"),Slider("TRAFFICO | Coefficiente esplicito del veicolo — non normativo",1,0,3,3));
        Wire(In(combinations,"Isolated positions"),Control("TRAFFICO | Aggiungere combinazioni di sola posizione mobile",new GH_BooleanToggle{Value=false}));Wire(In(merge,"Combinations"),Out(combinations,"Table"));
        var action=Add("BridgeActionComponent","M03_N0 | Azioni di traffico — una per posizione");Wire(In(action,"Action ID"),Out(combinations,"Action IDs"));Wire(In(action,"Model"),Out(moving,"Model"));Wire(In(action,"Case"),Out(combinations,"Cases"));Wire(In(action,"Behavior"),Text("TRAFFICO | Risposta: 2 = istantanea n0","2"));
        var phase=Find(doc,"BridgeConstructionPhaseComponent","M03_N0");In(phase,"Actions").AddSource(Out(action,"Action"));In(phase,"Actions").DataMapping=GH_DataMapping.Flatten;
        In(merge,"Models").AddSource(Out(moving,"Model"));In(merge,"Models").DataMapping=GH_DataMapping.Flatten;merge.NickName="ASSEMBLA QUATTRO RIGIDEZZE + ANALISI MOBILE n0";
        void Output(IGH_Param p,string label){var output=Control(label,(IGH_Param)Activator.CreateInstance(p.GetType()));output.AddSource(p);if(output is IGH_PreviewObject preview)preview.Hidden=p.Name!="Axis";}
        Output(Out(path,"Axis"),"TRAFFICO | ASSE DEL PERCORSO — quota della soletta");Output(Out(path,"Step"),"TRAFFICO | PASSO DELLE POSIZIONI [mm]");Output(Out(moving,"Model"),"TRAFFICO | MODELLO n0 CON RISULTATI MOBILI");
        Output(Out(moving,"Cases"),"TRAFFICO | NOMI ORIGINALI DEI CASI STRAUS");Output(Out(combinations,"Mapping"),"TRAFFICO | ACTION ID — POSIZIONE");Output(Out(combinations,"Table"),"TRAFFICO | COMBINAZIONI COMPLETE");
        foreach(var c in added)foreach(var p in c.Params.Input.Where(p=>p.SourceCount==0))
        {
            var data=p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure;var values=data?.AllData(true).ToArray()??Array.Empty<IGH_Goo>();IGH_Param control;string label=c.NickName+" | "+p.Name;
            if(values.Length==0){Require(p.Optional,"Unassigned new input "+label);control=Control(label+" (opzionale vuoto)",(IGH_Param)Activator.CreateInstance(p.GetType()));}
            else if(values.Length==1&&values[0] is GH_Boolean b)control=Control(label,new GH_BooleanToggle{Value=b.Value});
            else control=Text(label,string.Join("\n",values.Select(v=>v.ScriptVariable() is IFormattable n?n.ToString(null,CultureInfo.InvariantCulture):v.ScriptVariable()?.ToString())));
            data?.Clear();Wire(p,control);
        }
        var note=(GH_Panel)Text("IPOTESI TRAFFICO REV003","Veicolo di PROVA: 2 assi, 4 ruote da 150 kN; interasse 1200 mm, carreggiata 2000 mm. Forze globali -Z, posizioni statiche alternative.\nProprietà n0 dalla mappa Dead: nessun aggiornamento di fessurazione al passaggio del veicolo.\nUNA posizione per combinazione, con le sei componenti concomitanti. Nessun coefficiente normativo automatico.\nM01/M02/M03 meccanici e M04 ritiro conservati. Il ramo mobile disabilita Dead e carichi statici; aggiunge solo il veicolo alla combinazione base scelta.\n1 Read Excel. 2 Run M01. 3 Run M02, M03, M04 e Moving Static Analysis. 4 Run Build e Super Element Check. 5 Grafici e report.\nT, M2 e V1 esclusi dal checker N–Mx–Vy: traffico eccentrico richiede verifiche dedicate degli effetti esclusi. Affinare mesh e divisioni.\nHelper per tracciati rettilinei e pendenza trasversale costante; una curva viene rifiutata, non sostituita con una corda.");note.Attributes.Bounds=new(900,cy,1500,500);
        BridgeSegmentExcelTests.Solve(doc,true);
        foreach(var read in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeSectionReadComponent")){BridgeSegmentExcelTests.Press(read);read.ExpireSolution(false);}
        BridgeSegmentExcelTests.Solve(doc,true);CheckBeforeSteelAnalysis(doc);Require(Values<MovingPath>(Out(path,"Path")).Length==1,"No native path.");
        var layout=Example50HorizontalLayout.Apply(doc);GH_Document.EnableSolutions=false;GrasshopperBridgeExample.Write(doc,target);Validate(target);
        Require(original.SequenceEqual(File.ReadAllBytes(source))&&originalBook.SequenceEqual(File.ReadAllBytes(sourceBook)),"Source REV002 changed.");
        File.WriteAllText(Path.Combine(folder,"REV003-validation.json"),JsonSerializer.Serialize(new{Source=Path.GetFileName(source),SourceSHA256=Convert.ToHexString(SHA256.HashData(original)),SourceWorkbookSHA256=Convert.ToHexString(SHA256.HashData(originalBook)),DefinitionSHA256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))),SourceFilesUnmodified=true,NativeSolverInvoked=false,ResultsSaved=false,AdditionalComponents=added.Count,Stages=layout.Select(s=>s.Title).ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("REV003 GENERATED: "+target);return 0;
    }
    internal static void Validate(string path)
    {
        using var doc=GrasshopperBridgeExample.Read(path);doc.FilePath=path;BridgeSegmentExcelTests.Solve(doc,true);CheckBeforeSteelAnalysis(doc);
        var moving=Find(doc,"MovingStaticAnalysisComponent");Require(!Values<bool>(In(moving,"Run")).Any(v=>v),"Auto-run must be false.");Require(Values<BridGHe.Common.Models.ModelModel>(Out(moving,"Model")).Length==0,"Delivered traffic results must be empty.");
        var p=Values<MovingPath>(Out(Find(doc,"BridgeStraightMovingPathComponent"),"Path")).Single();Require(p.Target==2&&p.ConvertToStaticCases&&p.Divisions==30,"Path settings.");
        Require(p.Template.Vehicles.Single().Loads.Length==4&&p.Template.Vehicles[0].Loads.Cast<MovingPointForce>().All(l=>l.Axis==1&&l.Force.SequenceEqual(new[]{0d,0d,-150000})),"Four downward wheel forces.");
        Require(In(Find(doc,"BridgeConstructionPhaseComponent","M03_N0"),"Actions").Sources.Count==3,"Traffic enters phase 30.");
        Require(In(Find(doc,"BuildBridgeModel"),"Combinations").Sources.Single().Attributes.GetTopLevel.DocObject is GH_Component c&&c.GetType().Name=="BridgeMovingCombinationsComponent","Alternative combination wiring.");
        Console.WriteLine("REV003 OFFLINE PASS: explicit vehicle, slab path, phase 30, alternative combinations; no solver results saved.");
    }
}
