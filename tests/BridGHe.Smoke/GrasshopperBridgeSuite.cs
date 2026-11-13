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
    // R6.6 lane R4c-grafi: the graphs of examples 12–18 without any FEM model, drawn from the recipes of BridgeSuiteExample and
    // BridgeSpineExamples (R4cModel) as before. The data of the graph are given by their GPC datum alone (load cases, beam properties, the
    // composite section: FromGpc) and every producer runs on the GPC path of P2 (GeneratorsWithoutFem and PropertiesWithoutFem on for the whole
    // run); the models of the graph are GPC inputs alone and the checks read the analysed GPC models with the GPC result readers.

    /// <summary><c>--bridge-gh-replay</c>: the graphs solved on the archives of the last <c>--bridge-suite</c> / <c>--bridge-spine</c> (input and
    /// results of the four contribution models in <c>calcoli</c>) given to the analyses as their cached GPC-only output; the native solver is not
    /// invoked. The archived inputs are checked to be the inputs of the recipes (analysis fingerprint).</summary>
    internal static int Replay(string prefix)
    {
        var checks=new NativeExamples.Recorder();
        R4cModel.Results[] Read(string id,R4cModel[] inputs)=>Cases.Select((name,phase)=>
        {
            var (input,analysed)=R4cModel.Read(Path.Combine(ExamplePaths.Calculations(id),name));
            if(GPC.Model.Models.ModelRevisions.AnalysisFingerprint(input.Model)!=GPC.Model.Models.ModelRevisions.AnalysisFingerprint(inputs[phase].Build().Model))
                throw new Exception("GPC: the archived input "+id+"/"+name+" is not the input of the recipe");
            return new R4cModel.Results(analysed,inputs[phase]);
        }).ToArray();
        foreach(var original in Defaults.Where(d=>prefix=="all"||d.Id.StartsWith(prefix,StringComparison.Ordinal)))
        {
            var d=JsonSerializer.Deserialize<Definition>(File.ReadAllText(Path.Combine(Root,original.Id,"parameters.json")));
            var inputs=Enumerable.Range(0,4).Select(phase=>CreateGpc(d,phase)).ToArray();
            using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,d.Id,"validation.json")));
            var samples=report.RootElement.GetProperty("Sections").Deserialize<List<SectionSample>>();
            Run(d,inputs,Read(d.Id,inputs),samples,checks,cached:true);
        }
        foreach(var original in BridgeSpineExamples.Definitions.Where(d=>prefix=="all"||d.Id.StartsWith(prefix,StringComparison.Ordinal)))
        {
            var setup=JsonSerializer.Deserialize<BridgeSpineExamples.Settings>(File.ReadAllText(Path.Combine(Root,original.Id,"parameters.json")));
            var inputs=Enumerable.Range(0,4).Select(phase=>BridgeSpineExamples.CreateGpc(setup,phase)).ToArray();
            using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,original.Id,"validation.json")));
            Run(setup.Definition,inputs,Read(original.Id,inputs),report.RootElement.GetProperty("Sections").Deserialize<List<SectionSample>>(),checks,setup.Central,new[]{"G2"},cached:true);
        }
        Console.WriteLine("PHASE EXAMPLES: "+checks.Measurements.Count+" measurements; native solver not invoked (GPC data alone).");
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
    /// <summary>The graph of a suite or spine example on the recipes <paramref name="inputs"/> of its four contributions, written, reopened and
    /// solved; checked against the analysed GPC models <paramref name="expected"/> (the direct analyses of the recipes: every node, every beam
    /// station matched by position) and the section samples. Cached: the analyses are not run; each takes as output the GH model of the input of
    /// its recipe alone with its analysed GPC model attached.</summary>
    internal static void Run(Definition d,R4cModel[] inputs,R4cModel.Results[] expected,List<SectionSample> samples,NativeExamples.Recorder checks,BridgeGrillageExample.Parameters sectionParameters=null,string[] selectedGroups=null,bool cached=false)
    {
        var delivery=cached?Path.GetFullPath(Path.Combine("artifacts/example-replay",d.Id)):Path.Combine(Root,d.Id);
        Directory.CreateDirectory(delivery);
        int checkStart=checks.Measurements.Count;
        Initialize();using var gpcOnly=R2aDesignFixtures.WithoutFem();
        var modelType=assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model",true);
        var p=sectionParameters??SectionParameters(d);var groupNames=selectedGroups??new[]{"G1","G2","G3","G4"};using var doc=new GH_Document();
        T Add<T>(T obj,float x,float y) where T:IGH_DocumentObject{obj.CreateAttributes();obj.Attributes.Pivot=new PointF(x,y);doc.AddObject(obj,false);return obj;}
        GH_Component Component(string type,float x,float y)=>Add((GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components."+type,true)),x,y);
        void Wire(IGH_Param from,GH_Component to,int i)=>to.Params.Input[i].AddSource(from);
        GH_Panel Panel(string name,string text,float x,float y,int w=230,int h=70)
        {var o=Add(new GH_Panel{NickName=name,UserText=text},x,y);o.Properties.Multiline=false;o.Attributes.Bounds=new RectangleF(x,y,w,h);return o;}
        IGH_Goo Case(string name)=>R2aDesignFixtures.Case(BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case(name,false));
        Param_GenericObject Objects(string name,IEnumerable<IGH_Goo> values,float x,float y)
        {var o=Add(new Param_GenericObject{NickName=name,Name=name},x,y);foreach(var v in values)o.PersistentData.Append(v);return o;}
        void Integer(GH_Component c,int i,int v){var q=(Param_Integer)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(v);}
        void Number(GH_Component c,int i,double v){var q=(Param_Number)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(v);}
        void Boolean(GH_Component c,int i,bool v){var q=(Param_Boolean)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(v);}
        void Text(GH_Component c,int i,string v){var q=(Param_String)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(v);}
        Panel("LEGGERE",d.Title+" | N-mm-MPa\n3 FASI + RITIRO LONGITUDINALE: quattro analisi incrementali con le rispettive rigidezze.\nG1 acciaio/getto; G2 misto lungo termine; Q misto istantaneo; RITIRO misto lungo termine.\nRitiro: pre-strain e gradiente per effetti globali + una sola autotensione di sezione.\nGeometrie e proprieta editabili. Per modifiche coerenti usare parameters.json e rigenerare.\nAttivare CALCOLA 4 MODELLI; poi Run Build BridGHe Model e Run Super Element Check.\nVerifiche N-Mx-Vy: T, M2 e V1 esclusi dal checker e conservati nei risultati FEM; gli effetti esclusi non sono verificati.",20,0,1400,190);
        var toggle=Add(new GH_BooleanToggle{NickName="CALCOLA 4 MODELLI",Value=false},1600,80);
        var analyses=new List<GH_Component>();
        for(int phase=0;phase<4;phase++)
        {
            var input=inputs[phase];float y=300+phase*2200;
            Panel("CONTRIBUTO "+(phase+1),input.Name,20,y,1200,65);
            var build=Component("Models.BuildModelComponent",2100,y+550);Integer(build,2,2);Text(build,1,input.Name);
            int group=0;
            foreach(var set in input.Nodes.GroupBy(n=>(n.Restraint[0],n.Restraint[1],n.Restraint[2],n.Restraint[3],n.Restraint[4],n.Restraint[5])))
            {
                var nodes=set.ToArray();var a=set.Key;float row=y+130+group*260;
                var points=Add(new Param_Point{NickName="Nodi [mm]"},30,row);foreach(var n in nodes)points.PersistentData.Append(new GH_Point(n.Position));
                var node=Component("Models.NodeComponent",280,row);Wire(points,node,0);
                var restraint=Component("Attributes.NodeRestraintsComponent",520,row);Wire(node.Params.Output[0],restraint,0);
                Wire(Objects("Tx Ty Tz",new[]{new GH_Boolean(a.Item1),new GH_Boolean(a.Item2),new GH_Boolean(a.Item3)},290,row+60),restraint,1);
                Wire(Objects("Rx Ry Rz",new[]{new GH_Boolean(a.Item4),new GH_Boolean(a.Item5),new GH_Boolean(a.Item6)},290,row+120),restraint,2);
                var load=Component("Loads.NodeLoadComponent",900,row);Wire(restraint.Params.Output[0],load,0);
                Wire(Objects(Cases[phase],new[]{Case(input.Cases.Single())},630,row+60),load,1);
                var forces=Add(new Param_Vector{NickName="Forze nodali [N]"},630,row+120);
                foreach(var n in nodes)forces.PersistentData.Append(new GH_Vector(n.Loads.Aggregate(Vector3d.Zero,(v,l)=>v+l.Force)));
                Wire(forces,load,2);Wire(load.Params.Output[0],build,0);group++;
            }
            int beamGroup=0;
            foreach(var set in input.Beams.GroupBy(b=>b.SuperElement!=null?b.Property.Property.Name:"__cross"))
            {
                bool longitudinal=set.Key!="__cross";var members=set.ToArray();float row=y+1330+beamGroup*230;
                var curves=Add(new Param_Curve{NickName=longitudinal?"Assi longitudinali":"Assi trasversali"},30,row);
                foreach(var b in members)curves.PersistentData.Append(new GH_Curve(new LineCurve(input.Nodes[b.From].Position,input.Nodes[b.To].Position)));
                var beam=Component("Models.BeamComponent",500,row);Wire(curves,beam,0);
                // GH repeats a single item over the curve list. Avoid archiving
                // hundreds of identical longitudinal section/material graphs.
                var propertyMembers=longitudinal?members.Take(1):members;
                Wire(Objects("Proprieta equivalenti A I11 I22 J",propertyMembers.Select(b=>R2aDesignFixtures.Property(b.Property)),270,row+70),beam,1);
                IGH_Param last=beam.Params.Output[0];
                if(longitudinal)
                {
                    var se=Component("Attributes.SuperElementComponent",760,row);Wire(last,se,0);
                    Wire(Panel("Gruppi longitudinali",string.Join("\n",members.Select(b=>b.SuperElement)),520,row+70,180,80),se,1);last=se.Params.Output[0];
                    if(phase==3)
                    {
                        var shrinkage=members[0].Shrinkage.Value;var lc=Objects("Ritiro nativo",new[]{Case(input.Cases.Single())},790,row+130);
                        var pre=Component("Loads.BeamPreLoadComponent",1100,row);Wire(last,pre,0);Wire(lc,pre,1);Integer(pre,2,1);Number(pre,3,shrinkage.PreStrain);
                        var gradient=Component("Loads.BeamGradientLoadComponent",1450,row);Wire(pre.Params.Output[0],gradient,0);Wire(lc,gradient,1);Number(gradient,2,shrinkage.Axis1);Number(gradient,3,shrinkage.Axis2);last=gradient.Params.Output[0];
                    }
                }
                var ids=Component("Attributes.ElementIdComponent",1800,row);Wire(last,ids,0);
                Wire(Panel("ID elementi",string.Join("\n",members.Select(b=>b.BeamId)),1560,row+80,180,85),ids,1);Wire(ids.Params.Output[0],build,0);beamGroup++;
            }
            var analysis=Component("Analysis.StrausR3AnalysisComponent",2500,y+550);Wire(build.Params.Output[0],analysis,0);Integer(analysis,1,0);Text(analysis,2,Cases[phase]);Wire(toggle,analysis,4);
            Wire(Panel("File Straus",Path.Combine(ExamplePaths.Calculations(d.Id),"gh-"+Cases[phase]+".st7"),2140,y+750,390,65),analysis,3);analyses.Add(analysis);
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
            Wire(Objects("Caso sorgente",new[]{Case(Cases[phase])},4300,y+150),action,2);
            if(phase==3)Number(action,3,p.ShrinkageLong);else Integer(action,3,phase);
            var stage=Component("BridgeChecks.BridgeConstructionPhaseComponent",4750,y);Integer(stage,0,10*(phase+1));Text(stage,1,Cases[phase]);Integer(stage,2,phase==0?0:1);Wire(action.Params.Output[0],stage,3);Wire(stage.Params.Output[0],merge,1);
        }
        var combos=Add(new Param_GenericObject{NickName="Combinazione SLS per ID azione"},4610,1800);
        foreach(string v in new[]{"","G1","G2","Q","RITIRO"})combos.PersistentData.Append(new GH_String(v),new GH_Path(0));
        foreach(string v in new[]{"SLS_3F_RITIRO","1","1","1","1"})combos.PersistentData.Append(new GH_String(v),new GH_Path(1));Wire(combos,merge,2);
        var select=Component("BridgeChecks.SuperElementFilterComponent",5200,850);Wire(merge.Params.Output[0],select,0);Wire(Panel("Travi",string.Join("\n",groupNames),5000,1150,150,90),select,1);
        var composite=Component("BridgeChecks.CompositeBeamComponent",5500,850);Wire(select.Params.Output[0],composite,0);
        var section=Objects($"H{p.SteelHeight} + soletta {p.SlabThickness}, b={p.GirderSlabWidth}",new[]{R2aDesignFixtures.Composite(GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(p)))},5250,1300);Wire(section,composite,1);Wire(section,composite,2);
        var check=Component("BridgeChecks.SuperElementCheckComponent",5800,850);Wire(merge.Params.Output[0],check,0);Wire(composite.Params.Output[0],check,1);check.Params.Input[1].DataMapping=GH_DataMapping.Flatten;
        for(int i=2;i<=4;i++)Boolean(check,i,false);
        var stresses=Component("BridgeChecks.CompositeBeamTensionResultComponent",6100,850);Wire(check.Params.Output[0],stresses,0);
        Panel("Sigma cls [MPa]","",6350,600,350,160).AddSource(stresses.Params.Output[1]);Panel("Sigma acciaio [MPa]","",6350,900,350,160).AddSource(stresses.Params.Output[12]);
        Panel("PIOLI E IRRIGIDITORI gPC","Definizioni dei dettagli collegate ai risultati di sezione.\nPrima trave selezionata: appoggio e primo diaframma interno.\nPioli: 2 x diam.22, h180, passo200 mm. Piatti intermedi 200x20, appoggio 250x25 mm.\nReazione d'appoggio dimostrativa SLS: somma dei quattro casi; sostituire con inviluppo SLU per il progetto.\nLe verifiche conservano lo stato limite e l’ipotesi N-Mx-Vy del risultato sorgente; T, M2 e V1 restano esclusi anche nei dettagli.\nI dettagli non aggiungono automaticamente rigidezza o peso al graticcio equivalente.",3900,2850,1500,170);
        var studs=Component("BridgeDetails.BridgeStudsComponent",4200,3200);
        var intermediate=Component("BridgeDetails.BridgeIntermediateStiffenerComponent",4200,3550);Number(intermediate,6,d.Spans[0]/6);Number(intermediate,7,d.Spans[0]/6);
        var support=Component("BridgeDetails.BridgeSupportStiffenerComponent",4200,4000);Integer(support,6,1);Number(support,7,d.Spans[0]/6);Number(support,8,d.Spans[0]/6);
        int supportNode=(int.Parse(groupNames[0].Substring(1))-1)*(d.Panels+1);
        Number(support,9,expected.Select((m,i)=>m.Reaction(supportNode,Cases[i])[2]).Sum()/1000);
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
        string ghx=Path.Combine(delivery,d.Id+".ghx");GrasshopperBridgeExample.Write(doc,ghx);
        using var restored=GrasshopperBridgeExample.Read(ghx);
        restored.Objects.OfType<GH_BooleanToggle>().Single().Value=!cached;
        var restoredAnalyses=restored.Objects.OfType<GH_Component>().Where(c=>c.GetType()==analyses[0].GetType()).OrderBy(c=>c.Attributes.Pivot.Y).ToArray();
        for(int phase=0;phase<4;phase++)((GH_Panel)restoredAnalyses[phase].Params.Input[3].Sources.Single()).UserText=Path.Combine(ExamplePaths.Calculations(d.Id),"gh-"+Cases[phase]+".st7");
        if(cached)for(int phase=0;phase<4;phase++)restoredAnalyses[phase].GetType().GetField("_model",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(restoredAnalyses[phase],R2bFixtures.Model(inputs[phase].Build(),expected[phase].Model));
        GH_Document.EnableSolutions=true;restored.Enabled=true;restored.NewSolution(false,GH_SolutionMode.Silent);
        string[] Errors()=>restored.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>c.Name+": "+e)).ToArray();
        if(Errors().Length>0)throw new Exception(string.Join("\n",Errors()));
        static bool Near(GPC.Geometry.Point3d a,Point3d b,double tolerance)=>new Point3d(a.X,a.Y,a.Z).DistanceTo(b)<tolerance;
        for(int phase=0;phase<4;phase++)
        {
            var goo=restoredAnalyses[phase].Params.Output[0].VolatileData.AllData(true).Single();
            if(modelType.GetProperty("Value").GetValue(goo)!=null)throw new Exception("GPC: the analysis output of "+Cases[phase]+" has a FEM value");
            var actual=R2bFixtures.Get<GPC.Model.Models.Model>(goo,"GpcResults")??throw new Exception("GPC: no analysed GPC model of "+Cases[phase]);
            var catalog=BridGHe.Common.Gpc.GpcResultCatalog.Of(actual);string Label(int entry)=>catalog.Entries[entry].Label;
            var input=inputs[phase];
            checks.Near("GH "+Cases[phase]+" beam count",actual.BeamElements.Count,input.Beams.Count,0,0);
            double maxU=0,maxF=0,maxM=0;
            for(int k=0;k<input.Nodes.Count;k++)
            {
                var other=actual.NodesElements.Values.Single(n=>Near(n.Position,input.Nodes[k].Position,1e-6));
                var d6=BridGHe.Common.Gpc.GpcResultRows.Node(catalog,actual,other).Single(r=>r.Displacement!=null&&Label(r.Entry)==Cases[phase]).Displacement;
                maxU=Math.Max(maxU,(new Vector3d(d6[0],d6[1],d6[2])-expected[phase].Translation(k,Cases[phase])).Length);
            }
            for(int b=0;b<input.Beams.Count;b++)
            {
                var from=input.Nodes[input.Beams[b].From].Position;var to=input.Nodes[input.Beams[b].To].Position;
                var other=actual.BeamElements.Values.Single(c=>Near(c.StartPoint,from,1e-6)&&Near(c.EndPoint,to,1e-6));
                var rows=BridGHe.Common.Gpc.GpcResultRows.Beam(catalog,actual,other).Where(r=>Label(r.Entry)==Cases[phase]).ToArray();
                foreach(var r in expected[phase].BeamRows(b,Cases[phase]))
                {
                    var a=rows.Single(f=>Math.Abs(f.Station-r.Station)<1e-5);
                    maxF=Math.Max(maxF,new[]{Math.Abs(a.Axial-r.Axial),Math.Abs(a.Shear1-r.Shear1),Math.Abs(a.Shear2-r.Shear2)}.Max());
                    maxM=Math.Max(maxM,new[]{Math.Abs(a.Bending1-r.Bending1),Math.Abs(a.Bending2-r.Bending2),Math.Abs(a.Torque-r.Torque)}.Max());
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
        {
            if(Errors().Length>0)throw new Exception(string.Join("\n",Errors()));
            var outputs=restoredCheck.Params.Output[0].VolatileData.AllData(true).Cast<GH_ObjectWrapper>().Select(g=>(BridgeStressResult)g.Value).ToArray();
            checks.Near("GH stazioni BridgeChecks",outputs.Length,groupNames.Length*d.Panels*5,0,0);checks.Near("GH cinque contributi BridgeChecks",outputs.Min(r=>r.Analysis.Stages.Count),5,0,0);
            checks.Near("GH ipotesi N-Mx-Vy esplicita in tutti i risultati",outputs.All(r=>r.Assumptions.Contains(BridgeCheckAdapter.PlanarAssumption))?1:0,1,0,0);
            foreach(var sample in samples)
            {
                var baseline=sample.Supported?sample.FinalStress:ExpectedStressGpc(p,Enumerable.Range(0,4).Select(phase=>expected[phase].BeamRows(sample.Beam-1,Cases[phase]).OrderBy(r=>r.Station).First()).ToArray());
                double nearest=outputs.Min(o=>StressValues(o).Zip(baseline,(a,b)=>Math.Abs(a-b)).Max());
                checks.Near("GH tensioni campione beam "+sample.Beam,nearest,0,0,1e-6);
            }
            File.WriteAllText(Path.Combine(delivery,"grasshopper-stresses.json"),JsonSerializer.Serialize(outputs.Select(StressValues),BridgeGrillageExample.Json));
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
            File.WriteAllText(Path.Combine(delivery,"details.json"),JsonSerializer.Serialize(detailResults.Select(r=>new{r.Analysis.Input.Studs,r.Analysis.Input.Intermediate,r.Analysis.Input.Support,Checks=r.Stage.Shear.Checks.Concat(r.Stage.Studs.Checks),r.Stage.Warnings}),BridgeGrillageExample.Json));
        }
        restored.Objects.OfType<GH_BooleanToggle>().Single().Value=false;
        foreach(var analysis in restoredAnalyses)analysis.GetType().GetField("_model",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(analysis,Activator.CreateInstance(modelType));
        GrasshopperBridgeExample.Write(restored,ghx);
        File.WriteAllText(Path.Combine(delivery,"phase-workflow-validation.json"),JsonSerializer.Serialize(new{Mode=cached?"cached-native-results":"native-recalculation",Checks=checks.Measurements.Skip(checkStart)},BridgeGrillageExample.Json));
    }
}
