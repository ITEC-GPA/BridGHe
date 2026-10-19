using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using BridGHe.Common.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using static BridgeSuiteExample;

/// <summary>Editable native components, archived, reopened and solved for every bridge.</summary>
internal static class GrasshopperBridgeSuite
{
    private static Assembly assembly;
    internal static int Replay(string prefix)
    {
        var checks=new NativeExamples.Recorder();
        foreach(var original in Defaults.Where(d=>prefix=="all"||d.Id.StartsWith(prefix,StringComparison.Ordinal)))
        {
            var d=JsonSerializer.Deserialize<Definition>(File.ReadAllText(Path.Combine(Root,original.Id,"parameters.json")));
            ModelModel[] Read(string file)=>Cases.Select(name=>BridGHe.Common.Helpers.ModelArchive.Read<ModelModel>(File.ReadAllBytes(Path.Combine(Artifacts,d.Id,name,file)))).ToArray();
            using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,d.Id,"validation.json")));
            var samples=report.RootElement.GetProperty("Sections").Deserialize<List<SectionSample>>();
            Run(d,Read("input.bgmodel"),Read("results.bgmodel"),samples,checks,cached:true);
        }
        foreach(var original in BridgeSpineExamples.Definitions.Where(d=>prefix=="all"||d.Id.StartsWith(prefix,StringComparison.Ordinal)))
        {
            var setup=JsonSerializer.Deserialize<BridgeSpineExamples.Settings>(File.ReadAllText(Path.Combine(Root,original.Id,"parameters.json")));
            ModelModel[] Read(string file)=>Cases.Select(name=>ModelArchive.Read<ModelModel>(File.ReadAllBytes(Path.Combine(Artifacts,original.Id,name,file)))).ToArray();
            using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,original.Id,"validation.json")));
            Run(setup.Definition,Read("input.bgmodel"),Read("results.bgmodel"),report.RootElement.GetProperty("Sections").Deserialize<List<SectionSample>>(),checks,setup.Central,new[]{"G2"},cached:true);
        }
        Console.WriteLine("PHASE EXAMPLES: "+checks.Measurements.Count+" measurements; native solver not invoked.");
        return checks.Measurements.Count>0&&checks.Measurements.All(c=>c.Passed)?0:1;
    }
    private static void Initialize()
    {
        if(assembly!=null)return;
        assembly=Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var field=typeof(Grasshopper.Instances).GetField("m_comServer",BindingFlags.NonPublic|BindingFlags.Static);
        if(field.GetValue(null)!=null)throw new InvalidOperationException("Use a fresh test process.");
        var server=new GH_ComponentServer();field.SetValue(null,server);
        var parse=typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance,null,new[]{typeof(Assembly),typeof(string)},null);
        var math=Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\MathComponents.gha");
        foreach(var a in new[]{typeof(GH_Document).Assembly,math,assembly})parse.Invoke(server,new object[]{a,a.Location});
    }
    internal static void Run(Definition d,ModelModel[] inputs,ModelModel[] expected,List<SectionSample> samples,NativeExamples.Recorder checks,BridgeGrillageExample.Parameters sectionParameters=null,string[] selectedGroups=null,bool cached=false)
    {
        int checkStart=checks.Measurements.Count;
        Initialize();var p=sectionParameters??SectionParameters(d);var groupNames=selectedGroups??new[]{"G1","G2","G3","G4"};using var doc=new GH_Document();
        T Add<T>(T obj,float x,float y) where T:IGH_DocumentObject{obj.CreateAttributes();obj.Attributes.Pivot=new PointF(x,y);doc.AddObject(obj,false);return obj;}
        GH_Component Component(string type,float x,float y)=>Add((GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components."+type,true)),x,y);
        void Wire(IGH_Param from,GH_Component to,int i)=>to.Params.Input[i].AddSource(from);
        GH_Panel Panel(string name,string text,float x,float y,int w=230,int h=70)
        {var o=Add(new GH_Panel{NickName=name,UserText=text},x,y);o.Properties.Multiline=false;o.Attributes.Bounds=new RectangleF(x,y,w,h);return o;}
        IGH_Goo Goo(string type,object value)
        {
            var t=assembly.GetType("BridGHe.Grasshopper.DataTypes."+type,true);
            if(type!="Fem.GH_Model")return (IGH_Goo)Activator.CreateInstance(t,new[]{value});
            var goo=(IGH_Goo)Activator.CreateInstance(t);t.GetProperty("Value").SetValue(goo,value);return goo;
        }
        Param_GenericObject Objects(string name,IEnumerable<IGH_Goo> values,float x,float y)
        {var o=Add(new Param_GenericObject{NickName=name,Name=name},x,y);foreach(var v in values)o.PersistentData.Append(v);return o;}
        void Integer(GH_Component c,int i,int v){var q=(Param_Integer)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(v);}
        void Number(GH_Component c,int i,double v){var q=(Param_Number)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(v);}
        void Boolean(GH_Component c,int i,bool v){var q=(Param_Boolean)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(v);}
        void Text(GH_Component c,int i,string v){var q=(Param_String)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(v);}
        Panel("LEGGERE",d.Title+" | N-mm-MPa\n3 FASI + RITIRO LONGITUDINALE: quattro analisi incrementali con le rispettive rigidezze.\nG1 acciaio/getto; G2 misto lungo termine; Q misto istantaneo; RITIRO misto lungo termine.\nRitiro: pre-strain e gradiente per effetti globali + una sola autotensione di sezione.\nGeometrie e proprieta editabili. Per modifiche coerenti usare parameters.json e rigenerare.\nAttivare CALCOLA 4 MODELLI; poi Run Build BridGHe Model e Run Super Element Check.\nPer i ponti curvi il checker segnala la torsione non supportata: consultare tutti i risultati FEM.",20,0,1400,190);
        var toggle=Add(new GH_BooleanToggle{NickName="CALCOLA 4 MODELLI",Value=false},1600,80);
        var analyses=new List<GH_Component>();
        for(int phase=0;phase<4;phase++)
        {
            var input=inputs[phase];float y=300+phase*2200;
            Panel("CONTRIBUTO "+(phase+1),input.Name,20,y,1200,65);
            var build=Component("Models.BuildModelComponent",2100,y+550);Integer(build,2,2);Text(build,1,input.Name);
            int group=0;
            foreach(var set in input.Nodes.GroupBy(n=>{var a=n.Attributes.OfType<NodeRestraintAttributeModel>().Single().Value;return(a.Tx,a.Ty,a.Tz,a.Rx,a.Ry,a.Rz);}))
            {
                var nodes=set.ToArray();var a=set.Key;float row=y+130+group*260;
                var points=Add(new Param_Point{NickName="Nodi [mm]"},30,row);foreach(var n in nodes)points.PersistentData.Append(new GH_Point(n.Position));
                var node=Component("Models.NodeComponent",280,row);Wire(points,node,0);
                var restraint=Component("Attributes.NodeRestraintsComponent",520,row);Wire(node.Params.Output[0],restraint,0);
                Wire(Objects("Tx Ty Tz",new[]{new GH_Boolean(a.Tx),new GH_Boolean(a.Ty),new GH_Boolean(a.Tz)},290,row+60),restraint,1);
                Wire(Objects("Rx Ry Rz",new[]{new GH_Boolean(a.Rx),new GH_Boolean(a.Ry),new GH_Boolean(a.Rz)},290,row+120),restraint,2);
                var load=Component("Loads.NodeLoadComponent",900,row);Wire(restraint.Params.Output[0],load,0);
                Wire(Objects(Cases[phase],new[]{Goo("Fem.GH_LoadCase",input.LoadCases.Single())},630,row+60),load,1);
                var forces=Add(new Param_Vector{NickName="Forze nodali [N]"},630,row+120);
                foreach(var n in nodes)forces.PersistentData.Append(new GH_Vector(n.Loads.OfType<NodeForceLoadModel>().Aggregate(Vector3d.Zero,(v,l)=>v+l.Value)));
                Wire(forces,load,2);Wire(load.Params.Output[0],build,0);group++;
            }
            int beamGroup=0;
            foreach(var set in input.Beams.GroupBy(b=>b.HasAttribute(typeof(SuperElementAttributeModel))?b.BeamProperty.Name:"__cross"))
            {
                bool longitudinal=set.Key!="__cross";var members=set.ToArray();float row=y+1330+beamGroup*230;
                var curves=Add(new Param_Curve{NickName=longitudinal?"Assi longitudinali":"Assi trasversali"},30,row);
                foreach(var b in members)curves.PersistentData.Append(new GH_Curve(new LineCurve(b.PointFrom,b.PointTo)));
                var beam=Component("Models.BeamComponent",500,row);Wire(curves,beam,0);
                // GH repeats a single item over the curve list. Avoid archiving
                // hundreds of identical longitudinal section/material graphs.
                var propertyMembers=longitudinal?members.Take(1):members;
                Wire(Objects("Proprieta equivalenti A I11 I22 J",propertyMembers.Select(b=>Goo("Fem.GH_BeamProperty",b.BeamProperty)),270,row+70),beam,1);
                IGH_Param last=beam.Params.Output[0];
                if(longitudinal)
                {
                    var se=Component("Attributes.SuperElementComponent",760,row);Wire(last,se,0);
                    Wire(Panel("Gruppi longitudinali",string.Join("\n",members.Select(SuperElementAttributeModel.GetSuperElement)),520,row+70,180,80),se,1);last=se.Params.Output[0];
                    if(phase==3)
                    {
                        var preValue=members[0].Loads.OfType<BeamPreLoadModel>().Single().Value;var gradientValue=members[0].Loads.OfType<BeamGradientLoadModel>().Single().Value;var lc=Objects("Ritiro nativo",new[]{Goo("Fem.GH_LoadCase",input.LoadCases.Single())},790,row+130);
                        var pre=Component("Loads.BeamPreLoadComponent",1100,row);Wire(last,pre,0);Wire(lc,pre,1);Integer(pre,2,1);Number(pre,3,preValue);
                        var gradient=Component("Loads.BeamGradientLoadComponent",1450,row);Wire(pre.Params.Output[0],gradient,0);Wire(lc,gradient,1);Number(gradient,2,gradientValue.Axis1);Number(gradient,3,gradientValue.Axis2);last=gradient.Params.Output[0];
                    }
                }
                var ids=Component("Attributes.ElementIdComponent",1800,row);Wire(last,ids,0);
                Wire(Panel("ID elementi",string.Join("\n",members.Select(b=>b.BeamId)),1560,row+80,180,85),ids,1);Wire(ids.Params.Output[0],build,0);beamGroup++;
            }
            var analysis=Component("Analysis.StrausR3AnalysisComponent",2500,y+550);Wire(build.Params.Output[0],analysis,0);Integer(analysis,1,0);Text(analysis,2,Cases[phase]);Wire(toggle,analysis,4);
            Wire(Panel("File Straus",Path.Combine(Path.GetTempPath(),"BridGHe-"+d.Id+"-"+Cases[phase]+".st7"),2140,y+750,390,65),analysis,3);analyses.Add(analysis);
            var parts=Component("Models.DecomposeModelComponent",2780,y+550);Wire(analysis.Params.Output[0],parts,0);
            var nr=Component("Results.NodeResultsComponent",3020,y+380);Wire(parts.Params.Output[0],nr,0);
            var br=Component("Results.BeamResultsComponent",3020,y+700);Wire(parts.Params.Output[1],br,0);
            Panel("U [mm]","",3260,y+300,400,180).AddSource(nr.Params.Output[2]);
            Panel("BM1 BM2 [Nmm]","",3260,y+650,400,180).AddSource(br.Params.Output[4]);
            Panel("T [Nmm] conservata","",3260,y+900,400,180).AddSource(br.Params.Output[6]);
        }
        Panel("BridGHe", "SLS dimostrativo con coefficienti unitari: G1 + G2 + Q + ritiro primario e secondario.\nRITIRO: un unico coefficiente per secondario FEM e strain primario; n ritiro = Ninf in questi modelli storici.\nNei CSV i quattro contributi restano distinti. Soletta lorda non fessurata, armature escluse.\nCURVE: non attivare la verifica aspettandosi un esito completo. Il motore N-M-V rifiuta T/M2.\nNessuna cancellazione o proiezione delle sollecitazioni non supportate.",3900,0,1250,155);
        var merge=Component("BridgeChecks.BuildBridgeModel",4900,850);
        for(int phase=0;phase<4;phase++)
        {
            float y=280+phase*350;var filter=Component("BridgeChecks.SuperElementFilterComponent",3900,y);Wire(analyses[phase].Params.Output[0],filter,0);
            Wire(Panel("Longitudinali",string.Join("\n",groupNames),3700,y+80,150,90),filter,1);IGH_Param last=filter.Params.Output[0];
            var bm=Component("Models.BuildModelComponent",4250,y);Integer(bm,2,2);bm.Params.Input[0].DataMapping=GH_DataMapping.Flatten;Wire(last,bm,0);Wire(bm.Params.Output[0],merge,0);
            var action=Component(phase==3?"BridgeChecks.BridgeShrinkageActionComponent":"BridgeChecks.BridgeActionComponent",4500,y);
            Text(action,0,new[]{"G1","G2","Q","RITIRO"}[phase]);Wire(bm.Params.Output[0],action,1);
            Wire(Objects("Caso sorgente",new[]{Goo("Fem.GH_LoadCase",new LoadCaseModel(Cases[phase],Cases[phase]))},4300,y+150),action,2);
            if(phase==3)Number(action,3,p.ShrinkageLong);else Integer(action,3,phase);
            var stage=Component("BridgeChecks.BridgeConstructionPhaseComponent",4750,y);Integer(stage,0,10*(phase+1));Text(stage,1,Cases[phase]);Integer(stage,2,phase==0?0:1);Wire(action.Params.Output[0],stage,3);Wire(stage.Params.Output[0],merge,1);
        }
        var combos=Add(new Param_GenericObject{NickName="Combinazione SLS per ID azione"},4610,1800);
        foreach(string v in new[]{"","G1","G2","Q","RITIRO"})combos.PersistentData.Append(new GH_String(v),new GH_Path(0));
        foreach(string v in new[]{"SLS_3F_RITIRO","1","1","1","1"})combos.PersistentData.Append(new GH_String(v),new GH_Path(1));Wire(combos,merge,2);
        var select=Component("BridgeChecks.SuperElementFilterComponent",5200,850);Wire(merge.Params.Output[0],select,0);Wire(Panel("Travi",string.Join("\n",groupNames),5000,1150,150,90),select,1);
        var composite=Component("BridgeChecks.CompositeBeamComponent",5500,850);Wire(select.Params.Output[0],composite,0);
        var section=Objects($"H{p.SteelHeight} + soletta {p.SlabThickness}, b={p.GirderSlabWidth}",new[]{Goo("GH_CompositeBeamProperty",BridgeGrillageExample.Composite(p))},5250,1300);Wire(section,composite,1);Wire(section,composite,2);
        var check=Component("BridgeChecks.SuperElementCheckComponent",5800,850);Wire(merge.Params.Output[0],check,0);Wire(composite.Params.Output[0],check,1);check.Params.Input[1].DataMapping=GH_DataMapping.Flatten;
        for(int i=2;i<=4;i++)Boolean(check,i,false);
        var stresses=Component("BridgeChecks.CompositeBeamTensionResultComponent",6100,850);Wire(check.Params.Output[0],stresses,0);
        Panel("Sigma cls [MPa]","",6350,600,350,160).AddSource(stresses.Params.Output[1]);Panel("Sigma acciaio [MPa]","",6350,900,350,160).AddSource(stresses.Params.Output[12]);
        Panel("PIOLI E IRRIGIDITORI gPC","Definizioni dei dettagli collegate ai risultati di sezione.\nPrima trave selezionata: appoggio e primo diaframma interno.\nPioli: 2 x diam.22, h180, passo200 mm. Piatti intermedi 200x20, appoggio 250x25 mm.\nReazione d'appoggio dimostrativa SLS: somma dei quattro casi; sostituire con inviluppo SLU per il progetto.\nLe verifiche conservano lo stato limite del risultato sorgente. Nei curvi BridgeChecks blocca prima la torsione H.\nI dettagli non aggiungono automaticamente rigidezza o peso al graticcio equivalente.",3900,2850,1500,170);
        var studs=Component("BridgeDetails.BridgeStudsComponent",4200,3200);
        var intermediate=Component("BridgeDetails.BridgeIntermediateStiffenerComponent",4200,3550);Number(intermediate,6,d.Spans[0]/6);Number(intermediate,7,d.Spans[0]/6);
        var support=Component("BridgeDetails.BridgeSupportStiffenerComponent",4200,4000);Integer(support,6,1);Number(support,7,d.Spans[0]/6);Number(support,8,d.Spans[0]/6);
        int supportNode=(int.Parse(groupNames[0].Substring(1))-1)*(d.Panels+1);
        Number(support,9,expected.Select((m,i)=>BridgeGrillageExample.Reaction(m.Nodes[supportNode],Cases[i]).Force.Z).Sum()/1000);
        var detailChecks=new List<GH_Component>();
        for(int i=0;i<2;i++)
        {
            float y=3300+i*700;
            var item=Add(Grasshopper.Instances.ComponentServer.ObjectProxies.Where(x=>x.Desc.Name=="List Item" && x.Desc.Category=="Sets").Select(x=>x.CreateInstance()).OfType<GH_Component>().Single(x=>!x.Obsolete),4700,y);
            Wire(check.Params.Output[0],item,0);item.Params.Input[0].DataMapping=GH_DataMapping.Flatten;Integer(item,1,i==0?0:(d.PanelsPerSpan/6)*5);
            var detail=Component("BridgeDetails.BridgeDetailCheckComponent",5150,y);Wire(item.Params.Output[0],detail,0);Wire(studs.Params.Output[0],detail,1);
            Wire((i==0?support:intermediate).Params.Output[0],detail,i==0?3:2);detailChecks.Add(detail);
            Panel(i==0?"Dettagli appoggio":"Dettagli intermedio","",5500,y-100,700,330).AddSource(detail.Params.Output[6]);
        }
        string ghx=Path.Combine(Root,d.Id,d.Id+".ghx");GrasshopperBridgeExample.Write(doc,ghx);
        using var restored=GrasshopperBridgeExample.Read(ghx);
        restored.Objects.OfType<GH_BooleanToggle>().Single().Value=!cached;
        var restoredAnalyses=restored.Objects.OfType<GH_Component>().Where(c=>c.GetType()==analyses[0].GetType()).OrderBy(c=>c.Attributes.Pivot.Y).ToArray();
        for(int phase=0;phase<4;phase++)((GH_Panel)restoredAnalyses[phase].Params.Input[3].Sources.Single()).UserText=Path.Combine(Artifacts,d.Id,"gh-"+Cases[phase]+".st7");
        if(cached)for(int phase=0;phase<4;phase++)restoredAnalyses[phase].GetType().GetField("_model",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(restoredAnalyses[phase],Goo("Fem.GH_Model",expected[phase]));
        GH_Document.EnableSolutions=true;restored.Enabled=true;restored.NewSolution(false,GH_SolutionMode.Silent);
        string[] Errors()=>restored.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>c.Name+": "+e)).ToArray();
        if(Errors().Length>0)throw new Exception(string.Join("\n",Errors()));
        for(int phase=0;phase<4;phase++)
        {
            var goo=restoredAnalyses[phase].Params.Output[0].VolatileData.AllData(true).Single();var actual=(ModelModel)goo.GetType().GetProperty("Value").GetValue(goo);
            checks.Near("GH "+Cases[phase]+" beam count",actual.Beams.Count,inputs[phase].Beams.Count,0,0);
            double maxU=0,maxF=0,maxM=0;
            foreach(var node in expected[phase].Nodes)
            {
                var other=actual.Nodes.Single(n=>n.Position.DistanceTo(node.Position)<1e-6);
                maxU=Math.Max(maxU,(BridgeGrillageExample.Displ(other,Cases[phase]).Displacement-BridgeGrillageExample.Displ(node,Cases[phase]).Displacement).Length);
            }
            foreach(var b in expected[phase].Beams)
            {
                var other=actual.Beams.Single(c=>c.BeamId==b.BeamId);
                foreach(var r in b.Results.OfType<BeamForceResultModel>())
                {
                    var a=other.Results.OfType<BeamForceResultModel>().Single(f=>Math.Abs(f.Station-r.Station)<1e-5);
                    maxF=Math.Max(maxF,new[]{Math.Abs(a.AxialForce-r.AxialForce),Math.Abs(a.Vx-r.Vx),Math.Abs(a.Vy-r.Vy)}.Max());
                    maxM=Math.Max(maxM,new[]{Math.Abs(a.Mx-r.Mx),Math.Abs(a.My-r.My),Math.Abs(a.Torque-r.Torque)}.Max());
                }
            }
            checks.Near("GH "+Cases[phase]+" scarto max XYZ tutti nodi [mm]",maxU,0,0,1e-6);
            checks.Near("GH "+Cases[phase]+" scarto max N V1 V2 [N]",maxF,0,0,1e-3);
            checks.Near("GH "+Cases[phase]+" scarto max BM1 BM2 T [Nmm]",maxM,0,0,1);
        }
        var restoredMerge=restored.Objects.OfType<GH_Component>().Single(c=>c.GetType()==merge.GetType());
        var restoredCheck=restored.Objects.OfType<GH_Component>().Single(c=>c.GetType()==check.GetType());
        foreach(var c in new[]{restoredMerge,restoredCheck}){c.GetType().GetField("_run",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(c,true);c.ExpireSolution(false);}
        restored.NewSolution(false,GH_SolutionMode.Silent);
        if(d.Radius>0)
        {
            checks.Near("GH BridgeChecks segnala torsione fuori campo",Errors().Any(e=>e.Contains("torsione"))?1:0,1,0,0);
            checks.Near("GH nessuna tensione parziale presentata come verifica",restoredCheck.Params.Output[0].VolatileData.AllData(true).Count(g=>g?.ScriptVariable() is BridgeStressResult),0,0,0);
        }
        else
        {
            if(Errors().Length>0)throw new Exception(string.Join("\n",Errors()));
            var outputs=restoredCheck.Params.Output[0].VolatileData.AllData(true).Cast<GH_ObjectWrapper>().Select(g=>(BridgeStressResult)g.Value).ToArray();
            checks.Near("GH stazioni BridgeChecks",outputs.Length,groupNames.Length*d.Panels*5,0,0);checks.Near("GH cinque contributi BridgeChecks",outputs.Min(r=>r.Analysis.Stages.Count),5,0,0);
            foreach(var sample in samples.Where(s=>s.Supported))
            {
                double nearest=outputs.Min(o=>StressValues(o).Zip(sample.FinalStress,(a,b)=>Math.Abs(a-b)).Max());
                checks.Near("GH tensioni campione beam "+sample.Beam,nearest,0,0,1e-6);
            }
            File.WriteAllText(Path.Combine(Root,d.Id,"grasshopper-stresses.json"),JsonSerializer.Serialize(outputs.Select(StressValues),BridgeGrillageExample.Json));
            var detailResults=restored.Objects.OfType<GH_Component>().Where(c=>c.GetType()==detailChecks[0].GetType()).OrderBy(c=>c.Attributes.Pivot.Y)
                .Select(c=>(BridgeStressResult)((GH_ObjectWrapper)c.Params.Output[0].VolatileData.AllData(true).Single()).Value).ToArray();
            checks.Near("GH due sezioni con dettagli gPC",detailResults.Length,2,0,0);
            foreach(var result in detailResults)
            {
                var stud=result.Stage.Studs;var options=result.Analysis.Input.Studs;
                double expectedResistance=Math.Min(.8*450*Math.PI*22*22/4,.29*22*22*Math.Sqrt(30*BridgeGrillageExample.Ec))/1.25;
                checks.Near("GH resistenza piolo indipendente [N]",stud.Resistance.Resistance,expectedResistance,1e-12);
                checks.Near("GH domanda per piolo da flusso [N]",stud.ForcePerStud*1000,Math.Abs(stud.Flow)*options.LongitudinalPitch/options.CountPerRow,1e-12);
                checks.Near("GH mantiene SLE senza esito SLU implicito",result.Stage.Shear.Checks.Any(c=>c.Ratio==null)?1:0,1,0,0);
                checks.Near("GH dettagli conservano cinque contributi",result.Analysis.Stages.Count,5,0,0);
            }
            checks.Near("GH irrigiditore appoggio attivo",detailResults[0].Analysis.Input.Support.Enabled?1:0,1,0,0);
            checks.Near("GH irrigiditore intermedio attivo",detailResults[1].Analysis.Input.Intermediate.Enabled?1:0,1,0,0);
            File.WriteAllText(Path.Combine(Root,d.Id,"details.json"),JsonSerializer.Serialize(detailResults.Select(r=>new{r.Analysis.Input.Studs,r.Analysis.Input.Intermediate,r.Analysis.Input.Support,Checks=r.Stage.Shear.Checks.Concat(r.Stage.Studs.Checks),r.Stage.Warnings}),BridgeGrillageExample.Json));
        }
        restored.Objects.OfType<GH_BooleanToggle>().Single().Value=false;
        foreach(var analysis in restoredAnalyses)analysis.GetType().GetField("_model",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(analysis,Goo("Fem.GH_Model",null));
        GrasshopperBridgeExample.Write(restored,ghx);
        File.WriteAllText(Path.Combine(Root,d.Id,"phase-workflow-validation.json"),JsonSerializer.Serialize(new{Mode=cached?"cached-native-results":"native-recalculation",Checks=checks.Measurements.Skip(checkStart)},BridgeGrillageExample.Json));
    }
}
