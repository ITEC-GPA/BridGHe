using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using BridGHe.Common.Geometry;

internal sealed partial class ParametricBridgePhasesExample
{
    private GH_Component check, studsCheck, intermediateCheck, supportCheck, report;

    private void ValidateChecks(GH_Document document)
    {
        var checker=document.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="SuperElementCheckComponent");
        RunButton(checker);checker.ExpireSolution(false);Solve(document);
        var rows=Values<BridgeStressResult>(Out(checker,"Tension Check")).ToArray();
        Require(rows.All(r=>r.Assumptions.Contains(BridgeCheckAdapter.PlanarAssumption)),"all section results declare the planar projection");
        var assembled=(ModelModel)Value(Out(document.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BuildBridgeModel"),"Model"));
        var mainIds=assembled.Beams.Where(b=>b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Family==BridgeMemberFamily.Girder).Select(b=>b.BeamId).ToHashSet();
        Require(mainIds.SetEquals(rows.Select(r=>r.Source.BeamId)),"checks cover every longitudinal FEM beam, every girder and concio");
        Require(rows.GroupBy(r=>(r.Source.BeamId,r.Source.StationMm)).All(g=>g.Count()==5 && g.Select(r=>r.Source.Combination).Distinct().Count()==5),"every check station covers all five combinations once");
        var details=document.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeDetailCheckComponent").ToArray();
        Require(details.Length==3 && details.All(c=>Out(c,"Section result").VolatileDataCount>0),"section, stud and both stiffener branches produce results");
        var plots=document.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name.EndsWith("PlotComponent")).ToArray();
        Require(plots.Length==6 && plots.All(c=>Out(c,"SVG").VolatileDataCount==1),"six plots draw from reindexed girder data");
        var reportComponent=document.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BridgeReportComponent");
        Require(In(reportComponent,"Results").VolatileDataCount==details.Sum(c=>Out(c,"Section result").VolatileDataCount),"report receives all section/detail results");
        var selected=document.Objects.OfType<GH_NumberSlider>().Single(c=>c.NickName.StartsWith("Trave da visualizzare nei grafici"));
        foreach(int group in new[]{2,3,1})
        {
            selected.SetSliderValue(group);selected.ExpireSolution(false);Solve(document);
            foreach(var filter in document.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeGirderResultsComponent"))
            {
                var beams=Values<CompositeBeamModel>(Out(filter,"Beams")).ToArray();
                Require(beams.Length>0 && beams.All(b=>b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Group==group),"plot selection follows actual girder identity");
                Require(Values<double>(Out(filter,"Stations")).All(s=>s>=0 && s<=90+1e-6),"plot station bounds refer to one 90 m girder, never three concatenated girders");
            }
        }
        Console.WriteLine($"FOUR MODELS CHECKS PASS: {mainIds.Count} longitudinal FEM beams, {rows.Length} section results, three detail branches, six plots and complete report input; report generation remains manual.");
    }

    private GH_Component MapSections(IGH_Param beams,string name,float x,float y)
    {
        var map=Add("BridgeSegmentCompositeComponent",name,x,y);
        Wire(beams,map,"Beams");In(map,"Beams").DataMapping=GH_DataMapping.Flatten;
        Wire(Find("BridgeSegmentSectionsComponent"),"Segment IDs",map,"Segment IDs");Wire(composite,"Composite Section",map,"Composite Properties");
        return map;
    }

    private void AddShrinkage(GH_Component slab)
    {
        Group("11b | Ritiro del cls — stessa deformazione nel FEM e nella verifica");
        creep=Add("CreepAndShrinkageConcreteComponent","Ritiro soletta dal riferimento all'età finale",2200,14700);
        Scalar(creep,"Fck","Ritiro | fck [MPa] — coerente con la classe cls della soletta",32,20,90);
        Scalar(creep,"Elastic Modulus Steel","Ritiro | E acciaio [MPa]",210000,100000,250000);
        Wire(Product(In(slab,"Width").Sources.Single(),In(slab,"Height").Sources.Single(),"Ac = larghezza efficace × spessore",1400,14700),creep,"Concrete Area");
        Wire(Product(In(slab,"Width").Sources.Single(),Text("Facce della soletta esposte all'essiccamento","2"),"u = 2 × larghezza efficace",1700,14900),creep,"u");
        Scalar(creep,"RH","Ritiro | umidità relativa [%]",70,20,100,0);
        Scalar(creep,"t0","Viscosità | età al carico [giorni]",28,1,365,0);
        Scalar(creep,"t","Ritiro | età finale [giorni]",365,1,36500,0);
        Scalar(creep,"Drying start","Ritiro | fine stagionatura [giorni]",7,0,365,0);
        Scalar(creep,"Reference age","Ritiro | attivazione del vincolo misto [giorni]",7,0,365,0);
        Wire(Choice("Ritiro | tipo cemento",new[]{("R",0),("N",1),("S",2)},1),creep,"Cement Type");
        Output(Out(creep,"εcs"),"RITIRO | incremento εcs negativo — unico ingresso FEM e azione primaria",14000);
        Output(Out(creep,"Notional size"),"RITIRO | spessore fittizio h0 [mm]");
    }

    private void AddChecks()
    {
        outputX=16000;
        Group("19 | Verifiche di tutte le travi principali — opzioni e dettagli DEMO");
        Text("IPOTESI DEL VERIFICATORE — conservate anche nel report",BridgeCheckAdapter.PlanarAssumption);
        var decompose=Add("DecomposeModelComponent","Estrai aste dal modello unito con risultati",9400,16000);Wire(merge,"Model",decompose,"Model");
        var map=MapSections(Out(decompose,"Beams"),"Sezioni di verifica abbinate ai conci calcolati",9900,16000);
        var beams=Add("CompositeBeamComponent","Travi miste di verifica — tutte le travi e tutti i conci",10400,16000);
        Wire(map,"Girders",beams,"Beam");Wire(map,"Composite Properties",beams,"Section Start");
        check=Add("SuperElementCheckComponent","Verifiche di tutte le travi — Run dopo Build",10900,16000);
        Wire(merge,"Model",check,"BridGHe Model");Wire(beams,"Beam",check,"Super-Element Beam");
        var options=Add("BridgeAnalysisOptionsComponent","Opzioni delle verifiche locali — azioni già combinate",10400,16900);
        Wire(Choice("Verifiche locali | stato limite dei carichi DEMO",new[]{("Ultimate — carichi di prova, non combinazioni normative",(int)BridgeLimitState.Ultimate)},(int)BridgeLimitState.Ultimate),options,"LimitState");
        var studs=Add("BridgeStudsComponent","Pioli DEMO — tutte le sezioni",10900,17200);
        studsCheck=Add("BridgeDetailCheckComponent","Sezioni e pioli — tutte le travi",11600,16000);
        Wire(check,"Tension Check",studsCheck,"Section result");Wire(studs,"Detail",studsCheck,"Studs");Wire(options,"Detail",studsCheck,"Options");
        var simple=Find("SimpleBridgeConfigurableComponent");
        GH_Component Stations(string name,string port,float y,bool reactions)
        {
            var s=Add("BridgeDetailStationsComponent",name,11600,y);
            Wire(merge,"Model",s,"Model");Wire(studsCheck,"Section result",s,"Results");Wire(simple,port,s,"Locations");In(s,"Locations").DataMapping=GH_DataMapping.Flatten;
            if(reactions)foreach(var phase in phases)Wire(phase,"Phase",s,"Phases");
            return s;
        }
        var joints=Stations("Irrigiditori intermedi ai giunti dei conci", "Joint Points",18000,false);
        var supports=Stations("Irrigiditori agli appoggi — reazioni delle stesse fasi", "Support Points",19500,true);
        var intermediate=Add("BridgeIntermediateStiffenerComponent","Definizione irrigiditori intermedi DEMO",12100,18400);
        var support=Add("BridgeSupportStiffenerComponent","Definizione irrigiditori appoggi DEMO",12100,19900);
        // Explicit upper-bound panel length. It is a user-controlled design assumption, not an inferred local spacing.
        var panel=Slider("Irrigiditori | lunghezza di pannello assunta [mm] — controllare rispetto alle posizioni",12000,100,30000,0);
        foreach(var detail in new[]{intermediate,support}){Wire(panel,detail,"LeftPanel");Wire(panel,detail,"RightPanel");}
        Wire(supports,"Compression kN",support,"ReactionKN");Wire(supports,"End type",support,"Location");
        intermediateCheck=Add("BridgeDetailCheckComponent","Verifica irrigiditori ai giunti",12600,18000);
        Wire(joints,"Results",intermediateCheck,"Section result");Wire(intermediate,"Detail",intermediateCheck,"Intermediate stiffener");
        supportCheck=Add("BridgeDetailCheckComponent","Verifica irrigiditori agli appoggi",12600,19500);
        Wire(supports,"Results",supportCheck,"Section result");Wire(support,"Detail",supportCheck,"Support stiffener");
        var summary=Add("BridgeCheckSummaryComponent","Riepilogo di tutte le travi, sezioni e dettagli",13700,15900);
        var labels=Add("BridgeResultLabelsComponent","Etichette automatiche di tutte le verifiche",13200,16500);
        foreach(var c in new[]{studsCheck,intermediateCheck,supportCheck}){Wire(c,"Section result",summary,"Results");Wire(c,"Section result",labels,"Results");}
        In(summary,"Results").DataMapping=In(labels,"Results").DataMapping=GH_DataMapping.Flatten;
        Wire(labels,"Labels",summary,"Labels");
        Wire(Control("Formato etichette del riepilogo",new GH_ValueList()),labels,"Format");
        foreach(var p in summary.Params.Output)Output(p,"VERIFICHE PONTE | "+p.Name,outputY<23500?23500:null);
        Output(Out(supports,"Signed reaction kN"),"APPOGGI | reazioni firmate per combinazione [kN] — negativo = sollevamento");
        Output(Out(studsCheck,"Section result"),"VERIFICHE PONTE | sezioni e pioli di tutte le travi");
        Output(Out(intermediateCheck,"Section result"),"VERIFICHE PONTE | irrigiditori intermedi ai giunti");
        Output(Out(supportCheck,"Section result"),"VERIFICHE PONTE | irrigiditori di appoggio");

        Group("20 | Grafici — seleziona la trave, le verifiche restano estese a tutto il ponte");
        var girder=Slider("Trave da visualizzare nei grafici — numero G",1,1,20,0);
        var plotOutputs=new System.Collections.Generic.List<IGH_Param>();
        int ordinal=0;
        foreach(var (type,source,port,title,quantity) in new[]{
            ("BridgeForcePlotComponent",check,"Tension Forces","Momento Mx",4),
            ("BridgeResultPlotComponent",studsCheck,"Section result","Tensioni acciaio sigma 5",3),
            ("BridgeShearPlotComponent",studsCheck,"Section result","Tau massima",2),
            ("BridgeStudPlotComponent",studsCheck,"Section result","Pioli Ed e Rd",5),
            ("BridgeIntermediateStiffenerPlotComponent",intermediateCheck,"Section result","Irrigiditori intermedi WR",0),
            ("BridgeSupportStiffenerPlotComponent",supportCheck,"Section result","Irrigiditori appoggi WR",0)})
        {
            float y=21300+ordinal++*1250;
            var filter=Add("BridgeGirderResultsComponent",title+" | selezione trave",11600,y);
            Wire(beams,"Beam",filter,"Beams");Wire(source,port,filter,"Results");Wire(girder,filter,"Girder");
            var plot=Add(type,title+" | Draw",12600,y);
            Wire(merge,"Model",plot,"BridGHe Model");Wire(filter,"Beams",plot,"Super-Element Beam");Wire(filter,"Results",plot,"Results");
            if(type.Contains("Stiffener"))
            {
                Wire(Control(title+" | verifica oppure inviluppo WR",new GH_ValueList()),plot,"Check");Wire(filter,"Stations",plot,"Stations");
            }
            else
            {
                var names=type switch {
                    "BridgeForcePlotComponent"=>new[]{"N","Vx","Vy","T","Mx","My"},
                    "BridgeResultPlotComponent"=>new[]{"σ1 cls","σ2 armatura","σ4 cls","σ5 acciaio","σ6 acciaio","σ7 acciaio","σ8 acciaio","τ6","τ7","σ6 equivalente","σ7 equivalente","ε1 cls","ε8 acciaio"},
                    "BridgeShearPlotComponent"=>new[]{"V","Tau media","Tau massima","Tensione equivalente","Resistenza anima"},
                    _=>new[]{"Flusso connessione","Forza per piolo","Resistenza per piolo","Resistenza per lunghezza","WR piolo","Pioli Ed/Rd","Flusso Ed/Rd"}};
                Wire(Choice(title+" | grandezza",names.Select((n,i)=>(n,i)).ToArray(),quantity),plot,"Quantity");
            }
            Literal(plot,"Graphic Title",title+" | titolo",title+" — trave selezionata");Flag(plot,"Keep Running",title+" | aggiornamento automatico",true);
            var preview=Add("BridgePlotPreviewComponent",title+" | anteprima GH",13700,y);Wire(plot,"SVG",preview,"SVG");
            plotOutputs.Add(Out(plot,"SVG"));Output(Out(plot,"SVG"),"GRAFICO | "+title);
        }
        Group("21 | Report Word — tutte le travi e i dettagli, grafici della trave selezionata");
        report=Add("BridgeReportComponent","Report ponte completo — Generate dopo le verifiche",14900,16000);
        foreach(var c in new[]{studsCheck,intermediateCheck,supportCheck})Wire(c,"Section result",report,"Results");
        File.Copy(BridgeReportTests.Template,Path.Combine(Folder,"Template_BridGHe.docx"),true);
        Literal(report,"Template","Report | template Word","Template_BridGHe.docx");Literal(report,"File","Report | file di destinazione","Report_50.docx");
        Wire(Control("Report | capitoli da includere",new GH_ValueList()),report,"Topics");Wire(merge,"Model",report,"Model");
        Literal(report,"Title","Report | titolo","Esempio 50 — verifiche delle travi principali del ponte misto");
        Literal(report,"Project","Report | ipotesi e campo delle verifiche","DEMO: carichi incrementali di prova, coefficienti non normativi; ritiro 7–365 giorni, RH70%; pannelli irrigiditori assunti 12 m. Tutte le travi principali; grafici della trave selezionata. Non include verifiche globali, fatica, traversi e flessione locale soletta. "+BridgeCheckAdapter.PlanarAssumption);
        Literal(report,"Author","Report | autore","BridGHe");Literal(report,"Revision","Report | revisione","50 — fasi e ritiro unificato");
        foreach(var svg in plotOutputs)Wire(svg,report,"SVG");
        foreach(var p in report.Params.Output)Output(p,"REPORT | "+p.Name);
    }
}
