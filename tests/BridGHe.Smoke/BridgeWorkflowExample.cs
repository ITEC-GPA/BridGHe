using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using St7API;

internal static class BridgeWorkflowExample
{
    private static readonly string Root=ExamplePaths.Case("19-flusso-acciaio-misto");
    private static readonly string Native=ExamplePaths.Calculations("19-flusso-acciaio-misto");
    internal static int Run(bool native=false)
    {
        Directory.CreateDirectory(Root);Directory.CreateDirectory(Native);
        var assembly=Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var field=typeof(Grasshopper.Instances).GetField("m_comServer",BindingFlags.NonPublic|BindingFlags.Static);
        if(field.GetValue(null)!=null)throw new InvalidOperationException("Use a fresh process for the workflow example.");
        var server=new GH_ComponentServer();field.SetValue(null,server);
        var parse=typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance,null,new[]{typeof(Assembly),typeof(string)},null);
        foreach(var a in new[]{typeof(GH_Document).Assembly,assembly})parse.Invoke(server,new object[]{a,a.Location});
        using var document=new GH_Document();
        T Add<T>(T item,float x,float y) where T:IGH_DocumentObject {item.CreateAttributes();item.Attributes.Pivot=new PointF(x,y);document.AddObject(item,false);if(item is IGH_PreviewObject preview)preview.Hidden=true;return item;}
        GH_Component Component(string type,float x,float y)=>Add((GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components."+type,true)),x,y);
        void Wire(IGH_Param p,GH_Component c,int port)=>c.Params.Input[port].AddSource(p);
        void Number(GH_Component c,int port,double value){var p=(Param_Number)c.Params.Input[port];p.PersistentData.Clear();p.SetPersistentData(value);}
        void Integer(GH_Component c,int port,int value){var p=(Param_Integer)c.Params.Input[port];p.PersistentData.Clear();p.SetPersistentData(value);}
        void Text(GH_Component c,int port,string value){var p=(Param_String)c.Params.Input[port];p.PersistentData.Clear();p.SetPersistentData(value);}
        void Boolean(GH_Component c,int port,bool value){var p=(Param_Boolean)c.Params.Input[port];p.PersistentData.Clear();p.SetPersistentData(value);}
        IGH_Goo Wrap(string type,object value){var t=assembly.GetType("BridGHe.Grasshopper.DataTypes."+type,true);var g=(IGH_Goo)Activator.CreateInstance(t);t.GetProperty("Value").SetValue(g,value);return g;}
        Param_GenericObject Objects(string name,IEnumerable<IGH_Goo> goos,float x,float y){var p=Add(new Param_GenericObject{Name=name,NickName=name},x,y);foreach(var goo in goos)p.PersistentData.Append(goo);return p;}
        GH_Panel Panel(string name,string text,float x,float y,int width=340,int height=95){var p=Add(new GH_Panel{NickName=name,UserText=text},x,y);p.Properties.Multiline=false;p.Attributes.Bounds=new RectangleF(x,y,width,height);return p;}
        GH_Panel Value(string name,double value,float x,float y)=>Panel(name,value.ToString("G17",CultureInfo.InvariantCulture),x,y,170,50);
        Panel("REV003 - flusso a fasi", "Ponte a graticcio 30 + 30 m, impalcato 12 m, 3 travi e traversi ogni 5 m. N-mm-MPa.\n1. Sezioni -> modello acciaio -> override n infinito / n0 / n ritiro.\n2. CALCOLA 4 MODELLI STRAUS = true, poi false.\n3. Run Build BridGHe Model, poi Run Super Element Check.\nFasi 10/20/30/40; gli ID G1/G2/Q/RITIRO non dipendono dall'ordine dei modelli.\nRITIRO unisce strain primario e sollecitazioni FEM con un unico coefficiente.\nQuesto file è preparato senza nuovi risultati Straus: eseguire le analisi dopo ogni modifica della geometria.",20,0,2000,220);
        var steel=new MaterialModel("S355"){Name="S355",Modulus=210000,PoissonRatio=.3,MinimumYieldStress=355,MinimumTensileStress=510,ThermalExpansion=1.2e-5};
        var concrete=new MaterialModel("C32/40"){Name="C32/40",Modulus=33346.36,SpecificCompressiveStrength=32,PoissonRatio=.2};
        var rebar=new MaterialModel("B450"){Name="B450",Modulus=210000,MinimumYieldStress=450,MinimumTensileStress=540};
        var steelData=Objects("S355",new[]{Wrap("Fem.GH_Material",steel)},20,350);
        var concreteData=Objects("C32/40",new[]{Wrap("Fem.GH_Material",concrete)},20,430);
        var rebarData=Objects("B450",new[]{Wrap("Fem.GH_Material",rebar)},20,510);
        var n0=Value("n0",7,30,650);var nInf=Value("n infinito",18,30,730);var nShrink=Value("n ritiro",12,30,810);var eigenstrain=Value("Delta epsilon cs",-.0002,30,890);
        GH_Component Section(string name,bool central,float y)
        {
            var h=Component("Sections.BeamSectionIComponent",400,y);
            foreach(var (i,v) in new[]{(0,central?800.0:600),(1,central?600.0:500),(2,central?2000.0:1600),(3,40.0),(4,30.0),(5,central?15.0:16)})Number(h,i,v);
            var slab=Component("BridgeChecks.BeamSectionConcreteSlabComponent",400,y+230);
            foreach(var (i,v) in new[]{(0,250.0),(1,3000.0),(2,3000.0),(3,45.0),(4,2000.0),(5,50.0)})Number(slab,i,v);
            var c=Component("BridgeChecks.BeamPropertyCompositeComponent",850,y);Text(c,0,name);Wire(slab.Params.Output[0],c,1);Wire(h.Params.Output[0],c,2);Wire(concreteData,c,3);Wire(steelData,c,4);Wire(rebarData,c,5);
            Wire(n0,c,6);Wire(nInf,c,7);Wire(nShrink,c,c.Params.Input.FindIndex(p=>p.Name=="n ritiro"));
            return c;
        }
        var central=Section("Spina H2000 + soletta efficace 3000x250",true,350);
        var side=Section("Laterali H1600 + soletta efficace 3000x250",false,1000);
        var crossH=Component("Sections.BeamSectionIComponent",400,1700);foreach(var (i,v) in new[]{(0,300.0),(1,300.0),(2,800.0),(3,20.0),(4,20.0),(5,12.0)})Number(crossH,i,v);
        // Cross members retain steel stiffness in all phases: no hidden diaphragm activation.
        var crossPropertyComponent=Component("ElementProperties.BeamPropertyComponent",800,1700);
        Text(crossPropertyComponent,0,"Traversi H800");Integer(crossPropertyComponent,1,(int)BeamPropertyType.Beam);Wire(steelData,crossPropertyComponent,3);Wire(crossH.Params.Output[0],crossPropertyComponent,2);
        var crossProperty=crossPropertyComponent.Params.Output[0];
        var baseModel=Component("Models.BuildModelComponent",1650,1600);Integer(baseModel,2,2);Text(baseModel,1,"Ponte base acciaio 30+30 m");
        const int panels=24;const double dx=2500;
        Point3d Point(int girder,int station)=>new(station*dx,(girder-1)*4000,0);
        for(int j=0;j<4;j++)
        {
            float y=2100+j*340;var curves=Add(new Param_Curve{NickName=j<3?"G"+(j+1):"Traversi"},30,y);
            if(j<3)for(int i=0;i<panels;i++)curves.PersistentData.Append(new GH_Curve(new LineCurve(Point(j,i),Point(j,i+1))));
            else for(int i=0;i<=panels;i+=2)for(int g=0;g<2;g++)curves.PersistentData.Append(new GH_Curve(new LineCurve(Point(g,i),Point(g+1,i))));
            var beam=Component("Models.BeamComponent",350,y);Wire(curves,beam,0);Wire(j<3?(j==1?central:side).Params.Output[1]:crossProperty,beam,1);
            var se=Component("Attributes.SuperElementComponent",650,y);Wire(beam.Params.Output[0],se,0);Text(se,1,j<3?"G"+(j+1):"CROSS");
            var ids=Component("Attributes.ElementIdComponent",1000,y);Wire(se.Params.Output[0],ids,0);int start=j<3?j*panels+1:3*panels+1;int count=j<3?panels:26;
            Wire(Panel("ID stabili",string.Join("\n",Enumerable.Range(start,count)),700,y+130,180,150),ids,1);Wire(ids.Params.Output[0],baseModel,0);
        }
        var allPoints=Enumerable.Range(0,3).SelectMany(j=>Enumerable.Range(0,25).Select(i=>(j,i))).ToArray();int groupIndex=0;
        foreach(var set in allPoints.GroupBy(x=>(Tx:x.j==0&&x.i==0,Ty:x.j==0&&(x.i==0||x.i==24),Tz:x.i%12==0)))
        {
            float y=3550+groupIndex++*300;var points=Add(new Param_Point{NickName="Nodi / appoggi"},30,y);foreach(var (j,i) in set)points.PersistentData.Append(new GH_Point(Point(j,i)));
            var nodes=Component("Models.NodeComponent",350,y);Wire(points,nodes,0);var support=Component("Attributes.NodeRestraintsComponent",750,y);Wire(nodes.Params.Output[0],support,0);
            Wire(Objects("Tx Ty Tz",new[]{new GH_Boolean(set.Key.Tx),new GH_Boolean(set.Key.Ty),new GH_Boolean(set.Key.Tz)},380,y+100),support,1);
            Wire(Objects("Rx Ry Rz libere",new[]{new GH_Boolean(false),new GH_Boolean(false),new GH_Boolean(false)},380,y+160),support,2);Wire(support.Params.Output[0],baseModel,0);
        }
        var decompose=Component("Models.DecomposeModelComponent",1950,1600);Wire(baseModel.Params.Output[0],decompose,0);
        var filters=new List<GH_Component>();
        foreach(var (name,index) in new[]{("G2",0),("G1\nG3",1),("CROSS",2)})
        {var f=Component("BridgeChecks.SuperElementFilterComponent",2200,1750+index*350);Wire(baseModel.Params.Output[0],f,0);Wire(Panel("Gruppi",name,1950,1830+index*350),f,1);filters.Add(f);}
        var toggle=Add(new GH_BooleanToggle{NickName="CALCOLA 4 MODELLI STRAUS",Value=false},2300,80);
        var analyses=new List<GH_Component>();var builds=new List<GH_Component>();var sourceModels=new List<IGH_Param>();
        var merge=Component("BridgeChecks.BuildBridgeModel",5700,1100);
        string[] cases={"G1_GETTO","G2_PERMANENTI","Q_TRAFFICO","RITIRO_FEM"}, keys={"G1","G2","Q","RITIRO"};int[] ports={1,3,2,4};double[] q={33,10,10,0};
        for(int phase=0;phase<4;phase++)
        {
            float y=500+phase*950;Panel("MODELLO "+(phase+1),cases[phase],2500,y-130,1600,65);
            var build=Component("Models.BuildModelComponent",4200,y);Integer(build,2,2);Text(build,1,cases[phase]);Wire(decompose.Params.Output[0],build,0);Wire(filters[2].Params.Output[0],build,0);builds.Add(build);
            var lc=Objects(cases[phase],new[]{Wrap("Fem.GH_LoadCase",new LoadCaseModel(cases[phase],cases[phase]))},2500,y+650);
            for(int family=0;family<2;family++)
            {
                float row=y+family*350;var section=family==0?central:side;IGH_Param members=filters[family].Params.Output[0];
                if(phase>0){var over=Component("BeamOverrideComponent",2700,row);Wire(members,over,0);Wire(section.Params.Output[ports[phase]],over,1);members=over.Params.Output[0];}
                if(phase<3)
                {
                    var load=Component("Loads.BeamDistributedLoadComponent",3200,row);Wire(members,load,0);Wire(lc,load,1);Integer(load,2,0);Integer(load,4,1);Number(load,6,-q[phase]);members=load.Params.Output[0];
                }
                else
                {
                    var eigen=Component("Loads.CompositeShrinkageActionsComponent",2700,row+100);Wire(section.Params.Output[0],eigen,0);Wire(eigenstrain,eigen,1);
                    var pre=Component("Loads.BeamPreLoadComponent",3200,row);Wire(members,pre,0);Wire(lc,pre,1);Integer(pre,2,1);Wire(eigen.Params.Output[0],pre,3);
                    var grad=Component("Loads.BeamGradientLoadComponent",3650,row);Wire(pre.Params.Output[0],grad,0);Wire(lc,grad,1);Wire(eigen.Params.Output[1],grad,3);members=grad.Params.Output[0];
                }
                Wire(members,build,0);
            }
            var analysis=Component("Analysis.StrausR3AnalysisComponent",4550,y);Wire(build.Params.Output[0],analysis,0);Integer(analysis,1,0);Text(analysis,2,cases[phase]);Text(analysis,3,Path.Combine(Native,"REV003-"+cases[phase]+".st7"));Wire(toggle,analysis,4);analyses.Add(analysis);
            var filter=Component("BridgeChecks.SuperElementFilterComponent",4850,y);Wire(analysis.Params.Output[0],filter,0);Text(filter,1,"G2");
            var only=Component("Models.BuildModelComponent",5100,y);Wire(filter.Params.Output[0],only,0);Integer(only,2,2);sourceModels.Add(only.Params.Output[0]);Wire(only.Params.Output[0],merge,0);
            var action=Component(phase==3?"BridgeChecks.BridgeShrinkageActionComponent":"BridgeChecks.BridgeActionComponent",5300,y+140);Text(action,0,keys[phase]);Wire(only.Params.Output[0],action,1);Wire(lc,action,2);
            if(phase==3)Wire(eigenstrain,action,3);else Integer(action,3,phase);
            var stage=Component("BridgeChecks.BridgeConstructionPhaseComponent",5550,y+230);Integer(stage,0,10*(phase+1));Text(stage,1,cases[phase]);Integer(stage,2,phase==0?0:1);Wire(action.Params.Output[0],stage,3);Wire(stage.Params.Output[0],merge,1);
        }
        var combos=Add(new Param_GenericObject{NickName="Coefficienti didattici per ID azione"},5200,4500);
        var table=new[]{new[]{"","G1","G2","Q","RITIRO"},new[]{"SLS_DEMO","1","1","1","1"},new[]{"TEST_Q20","1.3","1.5","20","1"},new[]{"TEST_G2_10","1","10","1.3","1"}};
        for(int row=0;row<table.Length;row++)foreach(string value in table[row])combos.PersistentData.Append(new GH_String(value),new GH_Path(row));Wire(combos,merge,2);
        Panel("FASE / ORIGINE","",6000,400,850,380).AddSource(merge.Params.Output[1]);
        var select=Component("BridgeChecks.SuperElementFilterComponent",6000,1100);Wire(merge.Params.Output[0],select,0);Text(select,1,"G2");
        var composite=Component("BridgeChecks.CompositeBeamComponent",6350,1100);Wire(select.Params.Output[0],composite,0);Wire(central.Params.Output[0],composite,1);
        var check=Component("BridgeChecks.SuperElementCheckComponent",6700,1100);Wire(merge.Params.Output[0],check,0);Wire(composite.Params.Output[0],check,1);for(int i=2;i<=4;i++)Boolean(check,i,false);
        foreach(var (type,y) in new[]{("BridgeForcePlotComponent",1100f),("BridgeResultPlotComponent",2300f)})
        {
            var plot=Component("BridgeChecks."+type,7100,y);Wire(merge.Params.Output[0],plot,0);Wire(composite.Params.Output[0],plot,1);Wire(check.Params.Output[type=="BridgeForcePlotComponent"?2:0],plot,2);Boolean(plot,plot.Params.Input.Count-1,true);
            var viewer=Component("Preview.BridgePlotPreviewComponent",7550,y);Wire(plot.Params.Output[0],viewer,0);
        }
        var display=Component("Preview.DisplaySettingsComponent",6000,3300);var preview=Component("Preview.PreviewModelComponent",6500,3300);Wire(baseModel.Params.Output[0],preview,0);Wire(display.Params.Output[0],preview,1);preview.Hidden=false;
        for(int family=0;family<2;family++)
        {
            var c=Component("BridgeChecks.CompositeBeamComponent",6000,3700+family*350);Wire(filters[family].Params.Output[0],c,0);Wire((family==0?central:side).Params.Output[0],c,1);
            var pv=Component("Preview.PreviewModelComponent",6500,3700+family*350);Wire(c.Params.Output[0],pv,0);Wire(display.Params.Output[0],pv,1);pv.Hidden=false;
        }
        Panel("CAMPO E IPOTESI","Modello tridimensionale: 75 nodi, 72 beam longitudinali + 26 traversi. Appoggi a x=0/30/60 m; rotazioni libere.\nTre sezioni di acciaio; soletta equivalente di larghezza efficace 3 m per trave, due file di armature.\nI traversi mantengono la rigidezza d'acciaio: soletta trasversale non modellata.\nG1=33, G2=10, Q=10 N/mm per longitudinale: valori didattici, non peso proprio automatico.\nIl checker è collegato alla sola spina G2, con carichi simmetrici. Le laterali conservano gli effetti torsionali nel FEM.\nTEST_Q20 e TEST_G2_10 riprendono i fattori maggiorati di REV002: non sono combinazioni normative.\nIl vento di REV002 resta nel file originale: richiede torsione/flessione deviata, fuori dal campo del checker H.\nCumulative conserva il metodo numerico; LinearHistory segue gli incrementi N-M e non verifica tau/pioli.\nLa verifica di un ponte richiede anche carichi mobili, combinazioni di progetto, torsione, fatica, connessioni e stabilità.",5800,4800,2200,320);
        foreach(var builder in document.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BuildModelComponent"))builder.Params.Input[0].DataMapping=GH_DataMapping.Flatten;
        GH_Document.EnableSolutions=true;document.Enabled=true;document.NewSolution(false,GH_SolutionMode.Silent);
        string[] Errors(GH_Document doc)=>doc.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>c.Name+": "+e)).ToArray();
        if(Errors(document).Length>0)throw new Exception(string.Join("\n",Errors(document)));
        ModelModel Model(GH_Component c)=>(ModelModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        var checks=new NativeExamples.Recorder();var baseValue=Model(baseModel);
        checks.Near("Bridge node count",baseValue.Nodes.Count,75,0,0);checks.Near("Bridge member count",baseValue.Beams.Count,98,0,0);
        foreach(var b in builds)
        {
            var m=Model(b);checks.Near(m.Name+" nodes",m.Nodes.Count,75,0,0);checks.Near(m.Name+" beams",m.Beams.Count,98,0,0);
            checks.Near(m.Name+" stable beam IDs",m.Beams.Select(x=>x.BeamId).Intersect(baseValue.Beams.Select(x=>x.BeamId)).Count(),98,0,0);
            File.WriteAllBytes(Path.Combine(Native,"REV003-"+m.Name+"-input.bgmodel"),ModelArchive.Write(m));
        }
        if(native)
        {
            toggle.Value=true;toggle.ExpireSolution(false);document.NewSolution(false,GH_SolutionMode.Silent);
            if(Errors(document).Length>0)throw new Exception(string.Join("\n",Errors(document)));
            for(int i=0;i<analyses.Count;i++)
            {
                var m=Model(analyses[i]);double total=m.Nodes.Sum(n=>BridgeGrillageExample.Reaction(n,cases[i]).Force.Z);
                checks.Near(cases[i]+" equilibrium [N]",total,q[i]*3*60000,1e-6,1e-3);
                File.WriteAllBytes(Path.Combine(Native,"REV003-"+cases[i]+"-results.bgmodel"),ModelArchive.Write(m));
            }
            foreach(var c in new[]{merge,check}){c.GetType().GetField("_run",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(c,true);c.ExpireSolution(false);}document.NewSolution(false,GH_SolutionMode.Silent);
            if(Errors(document).Length>0)throw new Exception(string.Join("\n",Errors(document)));
            checks.Near("Checked spine stations",check.Params.Output[0].VolatileDataCount>=120?1:0,1,0,0);toggle.Value=false;
        }
        string path=Path.Combine(Root,"19-flusso-acciaio-misto_REV003.ghx");GrasshopperBridgeExample.Write(document,path);
        using var reopened=GrasshopperBridgeExample.Read(path);reopened.Enabled=true;reopened.NewSolution(false,GH_SolutionMode.Silent);
        if(Errors(reopened).Length>0)throw new Exception("Reopened graph: "+string.Join("\n",Errors(reopened)));
        checks.Near("Reopened component count",reopened.Objects.Count,document.Objects.Count,0,0);
        File.WriteAllText(Path.Combine(Root,"validation-REV003.json"),JsonSerializer.Serialize(new{Status=native?"native-checked":"prepared-offline-checked",NativeExecuted=native,Checks=checks.Measurements},new JsonSerializerOptions{WriteIndented=true}));
        if(checks.Measurements.Any(c=>!c.Passed))throw new Exception("REV003 validation failed.");
        Console.WriteLine("WORKFLOW REV003 PASS: "+checks.Measurements.Count+" checks; native="+native+"; "+path);return 0;
    }
}
