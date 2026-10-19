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
    private static readonly string Root=Path.GetFullPath("examples/19-flusso-acciaio-misto");
    private static readonly string Native=Path.GetFullPath("artifacts/bridge-workflow");
    internal static int Run()
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
        void Group(string name,Color color,params IGH_DocumentObject[] members){var g=new GH_Group{NickName=name,Colour=color};document.AddObject(g,false);foreach(var member in members)g.AddObject(member.InstanceGuid);}
        var header=Panel("COME USARE", "01 | Un unico modello acciaio assemblato.\n02 | Tre copie con Beam Override: n infinito, n0, n ritiro.\n03 | Quattro analisi Straus incrementali; non sommare modelli già cumulativi.\n04 | Merge ordinato e matching esplicito dei casi; poi assegnazione della sezione mista e verifica.\nAprire: risultati Straus già memorizzati. Premere Run su Build BridGHe Model, poi Run su Super Element Check.\nSe si modificano input: CALCOLA = true, poi false, poi i due Run.\nEsempio didattico di trave continua a due campate 12 + 12 m, N-mm-MPa. Non è un progetto completo di ponte.",20,0,1750,220);
        var steel=new MaterialModel("S355"){Name="S355",Modulus=210000,PoissonRatio=.3,MinimumYieldStress=355,MinimumTensileStress=510,ThermalExpansion=1.2e-5};
        var concrete=new MaterialModel("C30"){Name="C30",Modulus=32836.568,SpecificCompressiveStrength=30,PoissonRatio=.2};
        var rebar=new MaterialModel("B450"){Name="B450",Modulus=210000,MinimumYieldStress=450,MinimumTensileStress=540};
        var steelData=Objects("S355 - 210000 MPa",new[]{Wrap("Fem.GH_Material",steel)},20,370);
        var concreteData=Objects("C30",new[]{Wrap("Fem.GH_Material",concrete)},20,440);
        var rebarData=Objects("B450",new[]{Wrap("Fem.GH_Material",rebar)},20,510);
        var h=Component("Sections.BeamSectionIComponent",420,360);foreach(var (i,v) in new[]{(0,600.0),(1,600.0),(2,1600.0),(3,40.0),(4,40.0),(5,16.0)})Number(h,i,v);
        var slab=Component("BridgeChecks.BeamSectionConcreteSlabComponent",420,650);foreach(var (i,v) in new[]{(0,250.0),(1,3000.0),(2,3000.0),(3,45.0),(4,2000.0),(5,50.0)})Number(slab,i,v);
        var composite=Component("BridgeChecks.BeamPropertyCompositeComponent",1000,480);Text(composite,0,"H1600 + soletta 3000x250");Wire(slab.Params.Output[0],composite,1);Wire(h.Params.Output[0],composite,2);Wire(concreteData,composite,3);Wire(steelData,composite,4);Wire(rebarData,composite,5);
        var n0=Value("n0",7,700,740);var nInf=Value("n infinito",18,700,810);var nShrink=Value("n ritiro",12,700,880);var eigenstrain=Value("epsilon ritiro",-.0002,700,950);Wire(n0,composite,6);Wire(nInf,composite,7);Wire(nShrink,composite,10);
        var shrink=Component("Loads.CompositeShrinkageActionsComponent",1250,930);Wire(composite.Params.Output[0],shrink,0);Wire(eigenstrain,shrink,1);
        Group("SEZIONE UNICA - proprietà di fase",Color.FromArgb(210,225,242),h,slab,composite,shrink,n0,nInf,nShrink,eigenstrain,steelData,concreteData,rebarData);
        var curves=Add(new Param_Curve{NickName="12 elementi, 2 campate da 12000 mm"},20,1300);
        for(int i=0;i<12;i++)curves.PersistentData.Append(new GH_Curve(new LineCurve(new Point3d(i*2000,0,0),new Point3d((i+1)*2000,0,0))));
        var beam=Component("Models.BeamComponent",360,1300);Wire(curves,beam,0);Wire(composite.Params.Output[1],beam,1);
        var se=Component("Attributes.SuperElementComponent",620,1300);Wire(beam.Params.Output[0],se,0);Text(se,1,"MAIN");
        var ids=Component("Attributes.ElementIdComponent",900,1300);Wire(se.Params.Output[0],ids,0);Wire(Panel("ID stabili",string.Join("\n",Enumerable.Range(1,12)),630,1430,150,185),ids,1);
        var baseModel=Component("Models.BuildModelComponent",1350,1300);Wire(ids.Params.Output[0],baseModel,0);Text(baseModel,1,"Base - solo acciaio");Integer(baseModel,2,2);
        for(int group=0;group<3;group++)
        {
            var xs=Enumerable.Range(0,13).Where(i=>group==0?i==0:group==1?i==6||i==12:i!=0&&i!=6&&i!=12).ToArray();
            var points=Add(new Param_Point{NickName=group==0?"Appoggio fisso":group==1?"Appoggi scorrevoli":"Nodi intermedi - vincoli fuori piano"},20,1700+group*200);foreach(int i in xs)points.PersistentData.Append(new GH_Point(new Point3d(i*2000,0,0)));
            var nodes=Component("Models.NodeComponent",330,1700+group*200);Wire(points,nodes,0);
            var support=Component("Attributes.NodeRestraintsComponent",700,1700+group*200);Wire(nodes.Params.Output[0],support,0);
            Wire(Objects("Tx Ty Tz",new IGH_Goo[]{new GH_Boolean(group==0),new GH_Boolean(true),new GH_Boolean(group<2)},400,1760+group*200),support,1);
            Wire(Objects("Rx Ry Rz",new IGH_Goo[]{new GH_Boolean(true),new GH_Boolean(false),new GH_Boolean(true)},400,1810+group*200),support,2);Wire(support.Params.Output[0],baseModel,0);
        }
        var baseParts=Component("Models.DecomposeModelComponent",1600,1300);Wire(baseModel.Params.Output[0],baseParts,0);
        var toggle=Add(new GH_BooleanToggle{NickName="CALCOLA 4 MODELLI STRAUS",Value=false},1850,150);
        var analyses=new List<GH_Component>();var phaseBuilds=new List<GH_Component>();var analysedInputs=new List<IGH_Param>();
        string[] names={"G1_GETTO","G2_PERMANENTI","Q_TRAFFICO","RITIRO_FEM"};int[] propertyPorts={1,3,2,4};double[] q={8,4,12,0};
        for(int phase=0;phase<4;phase++)
        {
            float y=450+phase*700;var objects=new List<IGH_DocumentObject>();
            objects.Add(Panel("MODELLO "+(phase+1),names[phase]+" | "+new[]{"solo acciaio","n infinito = 18","n0 = 7","n ritiro = 12"}[phase],1830,y-130,1000,65));
            IGH_Param last=baseParts.Params.Output[1];
            if(phase>0){var over=Component("BeamOverrideComponent",1940,y);Wire(last,over,0);Wire(composite.Params.Output[propertyPorts[phase]],over,1);last=over.Params.Output[0];objects.Add(over);}
            var lc=new LoadCaseModel(names[phase],names[phase]);var caseParam=Objects(names[phase],new[]{Wrap("Fem.GH_LoadCase",lc)},1880,y+180);objects.Add(caseParam);
            if(phase<3)
            {
                var load=Component("Loads.BeamDistributedLoadComponent",2280,y);Wire(last,load,0);Wire(caseParam,load,1);Integer(load,2,0);Integer(load,4,1);Number(load,6,-q[phase]);last=load.Params.Output[0];objects.Add(load);
            }
            else
            {
                var pre=Component("Loads.BeamPreLoadComponent",2230,y);Wire(last,pre,0);Wire(caseParam,pre,1);Integer(pre,2,1);Wire(shrink.Params.Output[0],pre,3);
                var grad=Component("Loads.BeamGradientLoadComponent",2500,y);Wire(pre.Params.Output[0],grad,0);Wire(caseParam,grad,1);Wire(shrink.Params.Output[1],grad,3);last=grad.Params.Output[0];objects.Add(pre);objects.Add(grad);
            }
            var build=Component("Models.BuildModelComponent",2810,y);Wire(last,build,0);Wire(baseParts.Params.Output[0],build,0);Text(build,1,names[phase]);Integer(build,2,2);phaseBuilds.Add(build);objects.Add(build);
            var analysis=Component("Analysis.StrausR3AnalysisComponent",3160,y);Wire(build.Params.Output[0],analysis,0);Integer(analysis,1,0);Text(analysis,2,names[phase]);Text(analysis,3,Path.Combine(Native,names[phase]+".st7"));Wire(toggle,analysis,4);analyses.Add(analysis);objects.Add(analysis);
            var select=Component("BridgeChecks.SuperElementFilterComponent",3520,y);Wire(analysis.Params.Output[0],select,0);Text(select,1,"MAIN");last=select.Params.Output[0];objects.Add(select);
            if(phase==3)
            {
                var strain=Component("BridgeChecks.CompositeBeamSlabStrainComponent",3810,y);Wire(last,strain,0);Wire(eigenstrain,strain,2);
                Wire(Objects("RITIRO_PRIMARIO",new[]{Wrap("Fem.GH_LoadCase",new LoadCaseModel("RITIRO_PRIMARIO","RITIRO_PRIMARIO"))},3580,y+180),strain,1);last=strain.Params.Output[0];objects.Add(strain);
            }
            var forMerge=Component("Models.BuildModelComponent",4120,y);Wire(last,forMerge,0);Integer(forMerge,2,2);Text(forMerge,1,"Risultati "+(phase+1));analysedInputs.Add(forMerge.Params.Output[0]);objects.Add(forMerge);
            Group("FASE "+(phase+1),new[]{Color.FromArgb(216,230,242),Color.FromArgb(230,239,214),Color.FromArgb(252,237,208),Color.FromArgb(240,222,237)}[phase],objects.ToArray());
        }
        var merge=Component("BridgeChecks.BuildBridgeModel",4850,1100);foreach(var param in analysedInputs)Wire(param,merge,0);
        var combinations=Add(new Param_GenericObject{NickName="Header + coefficienti SLS unitari"},4450,1900);
        foreach(string s in new[]{"","1-G1_GETTO","2-G2_PERMANENTI","3-Q_TRAFFICO","4-RITIRO_FEM","4-RITIRO_PRIMARIO"})combinations.PersistentData.Append(new GH_String(s),new GH_Path(0));
        foreach(string s in new[]{"SLS_DEMO","1","1","1","1","1"})combinations.PersistentData.Append(new GH_String(s),new GH_Path(1));Wire(combinations,merge,1);
        foreach(var (port,name) in new[]{(2,"1-G1_GETTO"),(3,"2-G2_PERMANENTI"),(4,"3-Q_TRAFFICO"),(5,"4-RITIRO_PRIMARIO"),(7,"4-RITIRO_FEM")})Wire(Panel("Matching fase "+port,name,4450,2100+port*120,280,65),merge,port);
        Panel("ORIGINE E FASE","Il prefisso 1,2,3,4 viene dall'ordine dei modelli nell'input.\nLa fase è assegnata dal matching con i nomi qui sotto.\nSe si cambia l'ordine, aggiornare prefissi e combinazioni.\nRITIRO_FEM: sollecitazioni da Straus; RITIRO_PRIMARIO: autotensione gPC, una sola volta.",4420,450,650,180);
        var selectMerged=Component("BridgeChecks.SuperElementFilterComponent",5200,1100);Wire(merge.Params.Output[0],selectMerged,0);Text(selectMerged,1,"MAIN");
        var assign=Component("BridgeChecks.CompositeBeamComponent",5540,1100);Wire(selectMerged.Params.Output[0],assign,0);Wire(composite.Params.Output[0],assign,1);
        var check=Component("BridgeChecks.SuperElementCheckComponent",5890,1100);Wire(merge.Params.Output[0],check,0);Wire(assign.Params.Output[0],check,1);for(int i=2;i<=4;i++)Boolean(check,i,false);
        var results=Component("BridgeChecks.CompositeBeamTensionResultComponent",6240,1100);Wire(check.Params.Output[0],results,0);
        Panel("Sigma cls superiore [MPa]","",6550,650,420,200).AddSource(results.Params.Output[1]);
        Panel("Sigma armatura superiore [MPa]","",6550,950,420,200).AddSource(results.Params.Output[2]);
        Panel("Sigma armatura inferiore [MPa]","",6550,1250,420,200).AddSource(results.Params.Output[3]);
        Panel("Sigma acciaio inferiore [MPa]","",6550,1550,420,200).AddSource(results.Params.Output[12]);
        Panel("LIMITI DELL'ESEMPIO","Analisi lineari incrementali di una trave nel piano, soletta non fessurata.\nTre fasi di carico + ritiro FEM + autotensione.\nLe fasi usano le stesse geometrie, ID, assi e vincoli.\nJ è quello dell'acciaio, come nel componente precedente.\nCoefficienti unitari didattici: nessuna combinazione normativa è implicita.\nTau b resta NaN; non usare questo output come verifica della connessione.\nRestano da definire SLU, fatica, pioli, instabilità, soletta trasversale e azioni orizzontali.",5520,1900,1400,250);
        var settings=Component("Preview.DisplaySettingsComponent",5300,250);Boolean(settings,5,false);
        var preview=Component("Preview.PreviewModelComponent",5750,250);Wire(assign.Params.Output[0],preview,0);Wire(settings.Params.Output[0],preview,1);preview.Hidden=false;
        Component("Preview.DisplayPanelComponent",6200,250);
        string path=Path.Combine(Root,"19-flusso-acciaio-misto.ghx");GrasshopperBridgeExample.Write(document,path);
        using var restored=GrasshopperBridgeExample.Read(path);var checks=new NativeExamples.Recorder();
        GH_Component Find(GH_Component c)=>restored.Objects.OfType<GH_Component>().Single(x=>x.InstanceGuid==c.InstanceGuid);
        var run=restored.Objects.OfType<GH_BooleanToggle>().Single();run.Value=true;GH_Document.EnableSolutions=true;restored.Enabled=true;
        restored.NewSolution(false,GH_SolutionMode.Silent);
        var activeAnalyses=analyses.Select(Find).ToArray();
        foreach(var c in activeAnalyses)if(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0)throw new Exception(c.Name+": "+string.Join("; ",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        ModelModel Model(GH_Component c)=>(ModelModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        var inputModels=phaseBuilds.Select(c=>Model(Find(c))).ToArray();var solved=activeAnalyses.Select(Model).ToArray();
        var actualProperty=(CompositeBeamPropertyModel)Find(composite).Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        var actions=CompositeShrinkageActions.Calculate(actualProperty,-.0002);
        for(int phase=0;phase<4;phase++)
        {
            var nativeData=new double[11];
            lock(StrausRuntime.Gate)
            {
                int id=StrausRuntime.AcquireModelId();try
                {
                    StrausRuntime.Check(St7.St7OpenFile(id,Path.Combine(Native,names[phase]+".st7"),Native+Path.DirectorySeparatorChar),"Read exported workflow section");
                    var data=nativeData;StrausRuntime.Check(St7.St7GetBeamSectionPropertyData(id,1,new int[1],data),"Read exported property");var prop=inputModels[phase].Beams[0].BeamProperty;
                    checks.Near(names[phase]+" native A",data[St7.ipAREA],prop.SectionArea,1e-12);checks.Near(names[phase]+" native I11",data[St7.ipI11],prop.I11,1e-12);checks.Near(names[phase]+" native I22",data[St7.ipI22],prop.I22,1e-12);if(prop.J>0)checks.Near(names[phase]+" native J",data[St7.ipJ],prop.J,1e-12);else checks.Near(names[phase]+" automatic steel J positive",data[St7.ipJ]>0?1:0,1,0,0);
                }
                finally{St7.St7CloseFile(id);StrausRuntime.ReleaseModelId(id);}
            }
            var m=solved[phase];checks.Near(names[phase]+" beams",m.Beams.Count,12,0,0);
            var reactions=m.Nodes.OrderBy(n=>n.Position.X).Select(n=>BridgeGrillageExample.Reaction(n,names[phase]).Force.Z).ToArray();
            checks.Near(names[phase]+" vertical equilibrium",reactions.Sum(),q[phase]*24000,1e-8,1e-5);
            if(phase<3)
            {
                // Timoshenko compatibility for two equal spans. Straus auto-computes
                // shear area for the native steel I; generic composite phases retain
                // the existing zero-shear-area (Euler-Bernoulli) convention.
                double shearArea=nativeData[St7.ipSA2],ei=210000*nativeData[St7.ipI11],ga=210000/(2*(1+.3))*shearArea;
                double beta=shearArea>0?3*ei/(ga*12000*12000):0;
                double outer=q[phase]*12000*(.5-1/(8*(1+beta)));
                Console.WriteLine($"{names[phase]} native shear area 2={shearArea:G17}, beta={beta:G17}");
                checks.Near(names[phase]+" left reaction (including native shear flexibility)",reactions[0],outer,1e-6);
                checks.Near(names[phase]+" central reaction",reactions[6],q[phase]*24000-2*outer,1e-6);
            }
            else
            {
                checks.Near("Shrinkage free axial displacement",BridgeGrillageExample.Displ(m.Nodes.MaxBy(n=>n.Position.X),names[phase]).Displacement.X,actions.FreeStrain*24000,1e-6);
                checks.Near("Shrinkage central moment magnitude",Math.Abs(m.Beams[5].Results.OfType<BeamForceResultModel>().OrderBy(r=>r.Station).Last().My),Math.Abs(1.5*actions.EigenMoment),1e-6);
            }
            File.WriteAllBytes(Path.Combine(Native,names[phase]+"-input.bgmodel"),ModelArchive.Write(inputModels[phase]));File.WriteAllBytes(Path.Combine(Native,names[phase]+"-results.bgmodel"),ModelArchive.Write(m));

        }
        foreach(var component in new[]{Find(merge),Find(check)}){component.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(component,true);component.ExpireSolution(false);}
        restored.NewSolution(false,GH_SolutionMode.Silent);
        var errors=restored.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>c.Name+": "+e)).ToArray();if(errors.Length>0)throw new Exception(string.Join("\n",errors));
        var merged=Model(Find(merge));var checkedResults=Find(check).Params.Output[0].VolatileData.AllData(true).Select(g=>(BridgeStressResult)g.ScriptVariable()).ToArray();
        checks.Near("At least five force stations per beam",checkedResults.Length>=60?1:0,1,0,0);checks.Near("Five separate contributions",checkedResults.Min(r=>r.Analysis.Stages.Count),5,0,0);
        foreach(var r in checkedResults){checks.Near("Shrinkage FEM uses n ritiro",r.Stage.Contributions[3].HomogenizationN,12,1e-12);checks.Near("Shrinkage autostress uses n ritiro",r.Stage.Contributions[4].HomogenizationN,12,1e-12);}
        checks.Near("Top and bottom reinforcement rows",checkedResults.Count(r=>r.Analysis.Input.TopRebars.Enabled&&r.Analysis.Input.BottomRebars.Enabled),checkedResults.Length,0,0);
        checks.Near("Merged exact case origins",merged.LoadCases.Count,5,0,0);
        double maxForceDifference=0;
        for(int phase=0;phase<4;phase++)foreach(var source in solved[phase].Beams)foreach(var force in source.Results.OfType<BeamForceResultModel>())
        {
            var target=merged.Beams.Single(b=>b.BeamId==source.BeamId).Results.OfType<BeamForceResultModel>().Single(r=>r.LoadCase.Name==$"{phase+1}-{force.LoadCase.Name}" && Math.Abs(r.Station-force.Station)<=BridgeMergeValidation.StationTolerance(source.Length));
            maxForceDifference=Math.Max(maxForceDifference,new[]{Math.Abs(target.AxialForce-force.AxialForce),Math.Abs(target.Vx-force.Vx),Math.Abs(target.Vy-force.Vy),Math.Abs(target.Torque-force.Torque),Math.Abs(target.Mx-force.Mx),Math.Abs(target.My-force.My)}.Max());
        }
        checks.Near("All native forces preserved by merge",maxForceDifference,0,0,0);
        var snapshot=new{Stations=checkedResults.Length,Date=DateTimeOffset.Now,Measurements=checks.Measurements,Phases=merged.LoadCases.Select(l=>new{Name=l.Name,Phase=l.Stages.Single().BridgePhase.ToString()}),Sections=inputModels.Select(m=>new{m.Name,m.Beams[0].BeamProperty.SectionArea,m.Beams[0].BeamProperty.I11,m.Beams[0].BeamProperty.I22,m.Beams[0].BeamProperty.J}),Shrinkage=actions,Results=checkedResults.Select(r=>new{r.Point1_ConcreteSigma,r.Point2_RebarSigma,r.Point3_RebarSigma,r.Point8_SteelSigma})};
        File.WriteAllText(Path.Combine(Root,"validation.json"),JsonSerializer.Serialize(snapshot,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllBytes(Path.Combine(Root,"merged-results.bgmodel"),ModelArchive.Write(merged));
        using(var viewModel=new Rhino.FileIO.File3dm())
        {
            viewModel.Settings.ModelUnitSystem=Rhino.UnitSystem.Millimeters;
            var previewType=assembly.GetType("BridGHe.Grasshopper.Display.ModelPreview",true);
            foreach(var value in Find(assign).Params.Output[0].VolatileData.AllData(true))
            {
                var member=(CompositeBeamModel)value.ScriptVariable();
                for(int i=0;i<member.Shapes.Length;i++)viewModel.Objects.AddBrep(member.Shapes[i],new Rhino.DocObjects.ObjectAttributes{Name=member.BeamId+(i==0?" steel":" slab"),ObjectColor=i==0?Color.SteelBlue:Color.LightGray,ColorSource=Rhino.DocObjects.ObjectColorSource.ColorFromObject});
                foreach(var line in (Line[])previewType.GetMethod("ReinforcementLines").Invoke(null,new object[]{member}))viewModel.Objects.AddLine(line,new Rhino.DocObjects.ObjectAttributes{Name="Schematic reinforcement row",ObjectColor=Color.DarkOrange,ColorSource=Rhino.DocObjects.ObjectColorSource.ColorFromObject});
            }
            if(!viewModel.Write(Path.Combine(Root,"preview-sezioni-composte.3dm"),8))throw new IOException("Could not write preview geometry.");
        }
        run.Value=false;GrasshopperBridgeExample.Write(restored,path);
        using var replay=GrasshopperBridgeExample.Read(path);replay.Enabled=true;
        foreach(var id in new[]{merge.InstanceGuid,check.InstanceGuid}){var c=replay.Objects.OfType<GH_Component>().Single(c=>c.InstanceGuid==id);c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);}
        replay.NewSolution(false,GH_SolutionMode.Silent);
        var replayErrors=replay.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error)).ToArray();if(replayErrors.Length>0)throw new Exception("Cached workflow replay: "+string.Join("; ",replayErrors));
        var replayCheck=replay.Objects.OfType<GH_Component>().Single(c=>c.InstanceGuid==check.InstanceGuid);
        checks.Near("Reopened cached results",replayCheck.Params.Output[0].VolatileDataCount,checkedResults.Length,0,0);
        File.WriteAllText(Path.Combine(Root,"validation.json"),JsonSerializer.Serialize(snapshot,new JsonSerializerOptions{WriteIndented=true}));
        if(checks.Measurements.Any(c=>!c.Passed))throw new Exception("Workflow numerical checks failed; see validation.json.");
        Console.WriteLine($"WORKFLOW PASS: {checks.Measurements.Count} checks, 4 native analyses, {checkedResults.Length} gPC section checks. {path}");
        return 0;
    }
}
