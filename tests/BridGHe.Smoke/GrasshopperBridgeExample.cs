using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>Builds a real editable component graph, then reopens and executes it.</summary>
internal static class GrasshopperBridgeExample
{
    internal static void Run(BridgeGrillageExample.Parameters p,ModelModel[] inputs,ModelModel[] expected,BridgeGrillageExample.SectionReport sectionReference,NativeExamples.Recorder checks)
    {
        var assembly=Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var field=typeof(Grasshopper.Instances).GetField("m_comServer",BindingFlags.NonPublic|BindingFlags.Static);
        if(field.GetValue(null)!=null)throw new InvalidOperationException("Run this example in a fresh process.");
        var server=new GH_ComponentServer();field.SetValue(null,server);
        var parse=typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance,null,new[]{typeof(Assembly),typeof(string)},null);
        foreach(var a in new[]{typeof(GH_Document).Assembly,assembly})parse.Invoke(server,new object[]{a,a.Location});
        using var doc=new GH_Document();
        T Add<T>(T obj,float x,float y) where T:IGH_DocumentObject {obj.CreateAttributes();obj.Attributes.Pivot=new PointF(x,y);doc.AddObject(obj,false);return obj;}
        GH_Component Component(string type,float x,float y)=>Add((GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components."+type,true)),x,y);
        void Wire(IGH_Param from,GH_Component to,int input)=>to.Params.Input[input].AddSource(from);
        GH_Panel Panel(string name,string text,float x,float y,int w=230,int h=70)
        {var o=Add(new GH_Panel{NickName=name,UserText=text},x,y);o.Properties.Multiline=false;o.Attributes.Bounds=new RectangleF(x,y,w,h);return o;}
        IGH_Goo Goo(string type,object value)=> (IGH_Goo)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes."+type,true),new[]{value});
        Param_GenericObject Objects(string name,IEnumerable<IGH_Goo> values,float x,float y)
        {var obj=Add(new Param_GenericObject{NickName=name,Name=name},x,y);foreach(var v in values)obj.PersistentData.Append(v);return obj;}
        void Integer(GH_Component c,int i,int value){var q=(Param_Integer)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(value);}
        void Boolean(GH_Component c,int i,bool value){var q=(Param_Boolean)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(value);}
        void Text(GH_Component c,int i,string value){var q=(Param_String)c.Params.Input[i];q.PersistentData.Clear();q.SetPersistentData(value);}
        Panel("LEGGERE PRIMA","PONTE MISTO A GRATICCIO | N-mm-MPa\nTre modelli elastici: acciaio / misto lungo termine / misto istantaneo.\nGeometria, carichi e proprieta sono visibili e modificabili nei parametri.\nModificare parameters.json e rigenerare per cambiare coerentemente tutte le dipendenze.\nRun inizialmente false. File .st7 in TEMP, percorsi modificabili.\nMeCheck2: usare solo la combinazione simmetrica; il traffico eccentrico richiede anche torsione.\nSoletta e diaframmi sono travi equivalenti: non e una verifica normativa completa.",20,0,1300,160);
        var analyses=new List<GH_Component>();var builds=new List<GH_Component>();
        for(int phase=0;phase<3;phase++)
        {
            float y=240+phase*1050;var input=inputs[phase];
            Panel("FASE "+(phase+1),input.Name+"\nNodi "+input.Nodes.Count+" | Beam "+input.Beams.Count,y: y,x:20,w:750,h:65);
            var build=Component("Models.BuildModelComponent",1700,y+360);Text(build,1,input.Name);Integer(build,2,2);builds.Add(build);
            int nodeGroup=0;
            foreach(var nodeSet in input.Nodes.GroupBy(n=>n.Attributes.OfType<NodeRestraintAttributeModel>().Single().Value.Tz))
            {
                var nodes=nodeSet.ToArray();float row=y+140+nodeGroup*340;
                var points=Add(new Param_Point{NickName=nodeSet.Key?"Appoggi [mm]":"Nodi interni [mm]"},30,row);
                foreach(var n in nodes)points.PersistentData.Append(new GH_Point(n.Position));
                var nc=Component("Models.NodeComponent",240,row);Wire(points,nc,0);
                var restraint=Component("Attributes.NodeRestraintsComponent",440,row);Wire(nc.Params.Output[0],restraint,0);
                Wire(Objects("Tx Ty Tz",new[]{new GH_Boolean(true),new GH_Boolean(true),new GH_Boolean(nodeSet.Key)},240,row+70),restraint,1);
                Wire(Objects("Rx Ry Rz",new[]{new GH_Boolean(false),new GH_Boolean(false),new GH_Boolean(true)},240,row+120),restraint,2);
                IGH_Param last=restraint.Params.Output[0];int lcIndex=0;
                foreach(var lc in input.LoadCases)
                {
                    float x=660+lcIndex*280;
                    var load=Component("Loads.NodeLoadComponent",x,row);Wire(last,load,0);
                    Wire(Objects(lc.Name,new[]{Goo("Fem.GH_LoadCase",lc)},x-80,row+70),load,1);
                    var forces=Add(new Param_Vector{NickName="Forze "+lc.Name+" [N]"},x-80,row+120);
                    foreach(var n in nodes)forces.PersistentData.Append(new GH_Vector(new Vector3d(0,0,n.Loads.OfType<NodeForceLoadModel>().Where(l=>l.LoadCase.Name==lc.Name).Sum(l=>l.Value.Z))));
                    Wire(forces,load,2);last=load.Params.Output[0];lcIndex++;
                }
                Wire(last,build,0);nodeGroup++;
            }
            int beamGroup=0;
            foreach(var beams in input.Beams.GroupBy(b=>b.HasAttribute(typeof(SuperElementAttributeModel))))
            {
                var members=beams.ToArray();float row=y+780+beamGroup*130;
                var curves=Add(new Param_Curve{NickName=beams.Key?"Assi longitudinali [mm]":"Assi trasversali [mm]"},30,row);
                foreach(var b in members)curves.PersistentData.Append(new GH_Curve(new LineCurve(b.PointFrom,b.PointTo)));
                var bc=Component("Models.BeamComponent",460,row);Wire(curves,bc,0);
                Wire(Objects("Proprieta A I11 I22 J (N-mm)",members.Select(b=>Goo("Fem.GH_BeamProperty",b.BeamProperty)),220,row+60),bc,1);
                IGH_Param last=bc.Params.Output[0];
                if(beams.Key)
                {
                    var se=Component("Attributes.SuperElementComponent",730,row);Wire(last,se,0);
                    Wire(Panel("Super elementi",string.Join("\n",members.Select(SuperElementAttributeModel.GetSuperElement)),480,row+50,170,65),se,1);last=se.Params.Output[0];
                }
                var ids=Component("Attributes.ElementIdComponent",1020,row);Wire(last,ids,0);Wire(Panel("ID Straus",string.Join("\n",members.Select(b=>b.BeamId)),780,row+55,170,65),ids,1);
                Wire(ids.Params.Output[0],build,0);beamGroup++;
            }
            var analysis=Component("Analysis.StrausR3AnalysisComponent",2080,y+360);Wire(build.Params.Output[0],analysis,0);Integer(analysis,1,0);
            Wire(Panel("Casi da leggere",string.Join("\n",input.LoadCases.Select(c=>c.Name)),1770,y+530,260,90),analysis,2);
            Wire(Panel("File Straus",Path.Combine(Path.GetTempPath(),"BridGHe-ponte-fase-"+phase+".st7"),1770,y+650,400,60),analysis,3);
            Boolean(analysis,4,false);analyses.Add(analysis);
            var decompose=Component("Models.DecomposeModelComponent",2330,y+360);Wire(analysis.Params.Output[0],decompose,0);
            var nr=Component("Results.NodeResultsComponent",2560,y+260);Wire(decompose.Params.Output[0],nr,0);
            var br=Component("Results.BeamResultsComponent",2560,y+560);Wire(decompose.Params.Output[1],br,0);
            var disp=Panel("Spostamenti [mm]","",2770,y+170,320,150);disp.AddSource(nr.Params.Output[2]);
            var moments=Panel("BM1 BM2 [N mm]","",2770,y+450,320,150);moments.AddSource(br.Params.Output[4]);
            var torque=Panel("TORSIONE [N mm] - conservata","",2770,y+650,320,150);torque.AddSource(br.Params.Output[6]);
        }
        // Use the same instantaneous component-built model for actual Straus moving paths.
        var path=Objects("Percorso veicolo 2 assi",new[]{Goo("MovingGoo",BridgeGrillageExample.Traffic(p))},1700,3500);
        var attach=Component("Loads.AttachMovingPathsComponent",2020,3500);Wire(builds[2].Params.Output[0],attach,0);Wire(path,attach,1);
        var moving=Component("Analysis.MovingStaticAnalysisComponent",2350,3500);Wire(attach.Params.Output[0],moving,0);
        Wire(Panel("File mobile",Path.Combine(Path.GetTempPath(),"BridGHe-ponte-mobile.st7"),1800,3640,450,60),moving,1);Boolean(moving,2,false);
        var movingCases=Panel("Posizioni mobili generate","",2630,3500,450,130);movingCases.AddSource(moving.Params.Output[1]);

        // Merge only longitudinal members: transverse topology changes when the
        // slab hardens. Build MeCheck Model requires matching member order.
        Panel("MECHECK2 - CAMPO COPERTO","Combinazione SLS simmetrica, coefficienti 1.0.\nSezione lorda, soletta non fessurata, armature escluse.\n5 contributi: G1, G2, Q_UDL, ritiro lungo termine, deformazione istantanea.\nPer il traffico eccentrico conservare T e usare un verificatore dedicato.\nDopo le analisi: Run Build MeCheck Model, poi Run Super Element Check.\nNon alimentare questo merge con i risultati TOTALI delle fasi di getto.",3270,20,1100,150);
        var merge=Component("MeCheck2.BuildMeCheckModel",4300,650);
        for(int phase=0;phase<3;phase++)
        {
            float y=270+phase*370;
            var filter=Component("MeCheck2.SuperElementFilterComponent",3300,y);Wire(analyses[phase].Params.Output[0],filter,0);
            Wire(Panel("Travi principali","G1\nG2\nG3\nG4",3100,y+50,150,95),filter,1);
            IGH_Param last=filter.Params.Output[0];
            if(phase>0)
            {
                string name=phase==1?"RITIRO_INF":"DEF_IST";
                var strain=Component("MeCheck2.CompositeBeamSlabStrainComponent",3650,y);Wire(last,strain,0);
                Wire(Objects(name,new[]{Goo("Fem.GH_LoadCase",new LoadCaseModel(name,name))},3420,y+110),strain,1);
                var value=(Param_Number)strain.Params.Input[2];value.SetPersistentData(phase==1?p.ShrinkageLong:p.ShrinkageInstant);last=strain.Params.Output[0];
            }
            var onlyLong=Component("Models.BuildModelComponent",3940,y);Integer(onlyLong,2,2);Text(onlyLong,1,"Risultati longitudinali fase "+phase);
            onlyLong.Params.Input[0].DataMapping=GH_DataMapping.Flatten;Wire(last,onlyLong,0);Wire(onlyLong.Params.Output[0],merge,0);
        }
        var combinations=Add(new Param_GenericObject{NickName="SLS simmetrico - matrice combinazioni"},4000,1350);
        string[] headers={"","1-G1_GETTO","2-G2_PERMANENTI","3-Q_UDL","2-RITIRO_INF","3-DEF_IST"};
        foreach(string value in headers)combinations.PersistentData.Append(new GH_String(value),new GH_Path(0));
        foreach(string value in new[]{"SLS_SIMMETRICO","1","1","1","1","1"})combinations.PersistentData.Append(new GH_String(value),new GH_Path(1));
        Wire(combinations,merge,1);
        foreach(var entry in new[]{(2,"1-G1_GETTO"),(3,"2-G2_PERMANENTI"),(4,"3-Q_UDL\n3-Q_ECC\n3-Q_MIRROR"),(5,"2-RITIRO_INF"),(6,"3-DEF_IST")})
            Wire(Panel("Assegnazione fase "+entry.Item1,entry.Item2,4000,1450+(entry.Item1-2)*110,270,80),merge,entry.Item1);
        var select=Component("MeCheck2.SuperElementFilterComponent",4580,650);Wire(merge.Params.Output[0],select,0);
        Wire(Panel("Travi verificate","G1\nG2\nG3\nG4",4350,1050,150,95),select,1);
        var composite=Component("MeCheck2.CompositeBeamComponent",4870,650);Wire(select.Params.Output[0],composite,0);
        var section=Objects("Sezione mista H1600 + soletta 250",new[]{Goo("GH_CompositeBeamProperty",BridgeGrillageExample.Composite(p))},4620,1050);
        Wire(section,composite,1);Wire(section,composite,2);
        var check=Component("MeCheck2.SuperElementCheckComponent",5200,650);Wire(merge.Params.Output[0],check,0);Wire(composite.Params.Output[0],check,1);check.Params.Input[1].DataMapping=GH_DataMapping.Flatten;
        for(int i=2;i<=5;i++)Boolean(check,i,false);
        var stresses=Component("MeCheck2.CompositeBeamTensionResultComponent",5480,650);Wire(check.Params.Output[0],stresses,0);
        var concreteStress=Panel("Sigma cls superiore [MPa]","",5750,400,350,180);concreteStress.AddSource(stresses.Params.Output[1]);
        var steelStress=Panel("Sigma acciaio inferiore [MPa]","",5750,670,350,180);steelStress.AddSource(stresses.Params.Output[12]);
        Panel("ESITI DA NON CONFONDERE","TauB non disponibile: output NaN.\nTensioni elastiche non equivalgono a verifica SLU.\nInstabilita, connessione, fatica, soletta locale, appoggi e azioni orizzontali: da verificare a parte.\nLa deformazione istantanea imposta e un incremento separato; non ripetere lo stesso ritiro.",5450,1100,650,160);
        Write(doc,Path.Combine(BridgeGrillageExample.Delivery,"11-ponte-graticcio.ghx"));
        using var restored=Read(Path.Combine(BridgeGrillageExample.Delivery,"11-ponte-graticcio.ghx"));
        var restoredAnalyses=restored.Objects.OfType<GH_Component>().Where(c=>c.GetType()==analyses[0].GetType()).OrderBy(c=>c.Attributes.Pivot.Y).ToArray();
        for(int phase=0;phase<3;phase++)
        {
            var c=restoredAnalyses[phase];Boolean(c,4,true);
            ((GH_Panel)c.Params.Input[3].Sources.Single()).UserText=Path.Combine(BridgeGrillageExample.Output,"gh-phase-"+phase+".st7");
        }
        var restoredMoving=restored.Objects.OfType<GH_Component>().Single(c=>c.GetType()==moving.GetType());Boolean(restoredMoving,2,true);
        ((GH_Panel)restoredMoving.Params.Input[1].Sources.Single()).UserText=Path.Combine(BridgeGrillageExample.Output,"gh-moving.st7");
        GH_Document.EnableSolutions=true;restored.Enabled=true;restored.NewSolution(false,GH_SolutionMode.Silent);
        var errors=restored.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>c.Name+": "+e)).ToArray();
        if(errors.Length>0)throw new Exception(string.Join("\n",errors));
        for(int phase=0;phase<3;phase++)
        {
            var goo=restoredAnalyses[phase].Params.Output[0].VolatileData.AllData(true).Single();
            var result=(ModelModel)goo.GetType().GetProperty("Value").GetValue(goo);
            checks.Near("GH fase "+phase+" beam count",result.Beams.Count,inputs[phase].Beams.Count,0,0);
            foreach(var node in expected[phase].Nodes)
            {
                var actual=result.Nodes.Single(n=>n.Position.DistanceTo(node.Position)<1e-6);
                checks.Near("GH fase "+phase+" Uz node "+node.NodeId,BridgeGrillageExample.Displ(actual,BridgeGrillageExample.PhaseNames[phase]).Displacement.Z,BridgeGrillageExample.Displ(node,BridgeGrillageExample.PhaseNames[phase]).Displacement.Z,1e-7,1e-6);
            }
        }
        checks.Near("GH casi mobili",restoredMoving.Params.Output[1].VolatileDataCount,BridgeGrillageExample.MovingCaseCount(p),0,0);
        var restoredMerge=restored.Objects.OfType<GH_Component>().Single(c=>c.GetType()==merge.GetType());
        var restoredCheck=restored.Objects.OfType<GH_Component>().Single(c=>c.GetType()==check.GetType());
        foreach(var c in new[]{restoredMerge,restoredCheck}){c.GetType().GetField("_run",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(c,true);c.ExpireSolution(false);}
        restored.NewSolution(false,GH_SolutionMode.Silent);
        errors=restored.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>c.Name+": "+e)).ToArray();
        if(errors.Length>0)throw new Exception(string.Join("\n",errors));
        var outputs=restoredCheck.Params.Output[0].VolatileData.AllData(true).Cast<GH_ObjectWrapper>().Select(g=>(BridGHe.Common.Checks.BridgeStressResult)g.Value).ToArray();
        checks.Near("GH MeCheck2 stazioni elaborate",outputs.Length,4*p.Panels*5,0,0);
        checks.Near("GH MeCheck2 cinque contributi",outputs.Min(r=>r.Analysis.Stages.Count),5,0,0);
        var midspan=outputs.MaxBy(r=>r.Point8_SteelSigma);var reference=sectionReference.Rows.Last();
        checks.Near("GH sigma cls superiore mezzeria [MPa]",midspan.Point1_ConcreteSigma,reference.TopConcrete,1e-6,1e-6);
        checks.Near("GH sigma cls inferiore mezzeria [MPa]",midspan.Point4_ConcreteSigma,reference.BottomConcrete,1e-6,1e-6);
        checks.Near("GH sigma acciaio superiore mezzeria [MPa]",midspan.Point5_SteelSigma,reference.TopSteel,1e-6,1e-6);
        checks.Near("GH sigma acciaio inferiore mezzeria [MPa]",midspan.Point8_SteelSigma,reference.BottomSteel,1e-6,1e-6);
        var tensions=restored.Objects.OfType<GH_Component>().Single(c=>c.GetType()==stresses.GetType());
        checks.Near("GH estrazione tensioni acciaio",tensions.Params.Output[12].VolatileDataCount,outputs.Length,0,0);
        File.WriteAllText(Path.Combine(BridgeGrillageExample.Delivery,"grasshopper-stresses.json"),System.Text.Json.JsonSerializer.Serialize(outputs.Select(r=>new{r.Point1_ConcreteSigma,r.Point4_ConcreteSigma,r.Point5_SteelSigma,r.Point8_SteelSigma}),BridgeGrillageExample.Json));
    }
    internal static void Write(GH_Document doc,string path)
    {var archive=new GH_Archive();if(!archive.AppendObject(doc,"Definition")||!archive.WriteToFile(path,true,false))throw new IOException("Cannot write "+path);}
    internal static GH_Document Read(string path)
    {var archive=new GH_Archive();var doc=new GH_Document();if(!archive.ReadFromFile(path)||!archive.ExtractObject(doc,"Definition"))throw new IOException("Cannot read "+path);return doc;}
}
