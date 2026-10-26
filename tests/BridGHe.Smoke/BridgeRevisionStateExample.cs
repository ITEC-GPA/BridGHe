using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;

/// <summary>Derive REV002 from the current user-saved REV001; never regenerate the base bridge.</summary>
internal static class BridgeRevisionStateExample
{
    private static readonly string[] Tags={"M01_ACCIAIO","M02_NINF","M03_N0","M04_NSH"};
    private static IGH_Param In(GH_Component c,string n)=>c.Params.Input.Single(p=>p.Name==n);
    private static IGH_Param Out(GH_Component c,string n)=>c.Params.Output.Single(p=>p.Name==n);
    private static T[] Values<T>(IGH_Param p)=>p.VolatileData.AllData(true).Select(g=>g?.ScriptVariable()).OfType<T>().ToArray();
    private static GH_Component Find(GH_Document d,string type)=>d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name==type);
    private static GH_Component Find(GH_Document d,string type,string tag)=>d.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name==type&&c.NickName.StartsWith(tag,StringComparison.Ordinal));
    private static void Wire(IGH_Param target,params IGH_Param[] sources){target.RemoveAllSources();foreach(var s in sources)target.AddSource(s);}
    private static void Check(bool ok,string message){if(!ok)throw new Exception("REV002: "+message);}
    private static void Cache(GH_Component analysis,ModelModel model)
    {
        var f=analysis.GetType().GetField("_model",BindingFlags.NonPublic|BindingFlags.Instance);var goo=Activator.CreateInstance(f.FieldType);
        f.FieldType.GetProperty("Value").SetValue(goo,model);f.SetValue(analysis,goo);
        analysis.GetType().GetField("_just_loaded",BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(analysis,false);
    }
    private static void Press(GH_Component c)=>c.GetType().GetField("_run",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(c,true);
    private static void Initialize()
    {
        GH_Document.EnableSolutions=false;BridgeMergeTests.Initialize();
        var parse=typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Assembly),typeof(string)},null);
        foreach(var name in new[]{"MathComponents","CurveComponents","VectorComponents"})
        {var a=Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\"+name+".gha");parse.Invoke(Grasshopper.Instances.ComponentServer,new object[]{a,a.Location});}
    }
    internal static int Run(bool validateOnly=false)
    {
        try
        {
            Initialize();string folder=ExamplePaths.Case(ParametricBridgePhasesExample.Id);
            string source=Path.Combine(folder,"50-ponte-quattro-modelli_REV001.ghx"),target=Path.Combine(folder,"50-ponte-quattro-modelli_REV002.ghx");
            if(validateOnly){Validate(target);return 0;}
            if(File.Exists(target))throw new IOException("REV002 already exists; use --example50-rev002-check to validate without replacing user changes.");
            byte[] before=File.ReadAllBytes(source);string hash=Convert.ToHexString(SHA256.HashData(before));
            string sourceExcel=Path.Combine(folder,"Sezioni_conci_REV001.xlsx"),excel=Path.Combine(folder,"Sezioni_conci_REV002.xlsx");
            byte[] workbook=File.ReadAllBytes(sourceExcel);
            if(File.Exists(excel)&&!File.ReadAllBytes(excel).SequenceEqual(workbook))throw new IOException("REV002 workbook already exists with different data.");
            File.WriteAllBytes(excel,workbook);
            Directory.CreateDirectory(Path.Combine(folder,"calcoli","REV002"));
            using var doc=GrasshopperBridgeExample.Read(source);doc.FilePath=target;
            int beforeCount=doc.Objects.Count;
            var analyses=Tags.Select(t=>Find(doc,"StrausR3AnalysisComponent",t)).ToArray();
            foreach(var a in analyses)
            {
                Cache(a,null);a.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(a,false);
                foreach(var t in In(a,"Keep Running").Sources.OfType<GH_BooleanToggle>())t.Value=false;
                var p=In(a,"Output Path").Sources.OfType<GH_Panel>().Single();
                p.UserText=Path.Combine(folder,"calcoli","REV002",Tags[Array.IndexOf(analyses,a)]+".st7");
            }
            foreach(var p in doc.Objects.OfType<GH_Panel>())
            {
                if(p.UserText=="Sezioni_conci_REV001.xlsx")p.UserText="Sezioni_conci_REV002.xlsx";
                if(p.UserText=="Report_50.docx")p.UserText="Report_50_REV002.docx";
            }
            var oldOverrides=Tags.Skip(1).Take(2).Select(t=>Find(doc,"BridgeSegmentOverrideComponent",t)).ToArray();
            float start=oldOverrides[0].Attributes.Pivot.X-1400;
            foreach(var o in doc.Objects.Where(o=>o is not GH_Group&&o.Attributes.Pivot.X>=start))
            {var p=o.Attributes.Pivot;o.Attributes.Pivot=new PointF(p.X+4400,p.Y);}
            var added=new List<IGH_DocumentObject>();
            GH_Component Add(string type,string label,float x,float y){var c=BridgeSegmentExcelTests.Add(doc,type);c.NickName=label;c.Attributes.Pivot=new(x,y);added.Add(c);return c;}
            GH_Panel Panel(string label,string content,float x,float y,int width=620,int height=100)
            {
                var p=new GH_Panel{UserText=content,Name=label,NickName=label,Properties={Multiline=false}};p.CreateAttributes();p.Attributes.Pivot=new(x,y);p.Attributes.Bounds=new(x,y,width,height);doc.AddObject(p,false);added.Add(p);return p;
            }
            GH_ValueList Choice(string label,string[] options,int selected,float x,float y)
            {
                var v=new GH_ValueList{Name=label,NickName=label,ListMode=GH_ValueListMode.DropDown};v.ListItems.Clear();
                for(int i=0;i<options.Length;i++)v.ListItems.Add(new GH_ValueListItem(options[i],i.ToString()){Selected=i==selected});
                v.CreateAttributes();v.Attributes.Pivot=new(x,y);doc.AddObject(v,false);added.Add(v);return v;
            }
            var sections=Find(doc,"BridgeSegmentSectionsComponent");var composite=Find(doc,"BeamPropertyCompositeComponent");
            var decomposed=Add("DecomposeModelComponent","M0 calcolato — risultati per la mappa di rigidezza",start+250,650);
            Wire(In(decomposed,"Model"),Out(analyses[0],"Model"));
            var mapped=Add("BridgeSegmentCompositeComponent","Solo longitudinali — identità dei conci invariata",start+1000,650);
            Wire(In(mapped,"Beams"),Out(decomposed,"Beams"));Wire(In(mapped,"Segment IDs"),Out(sections,"Segment IDs"));Wire(In(mapped,"Composite Properties"),Out(composite,"Composite Section"));
            var dispatch=Add("BeamMomentDispatchComponent","Segno di BM2 sotto peso proprio — tutte le stazioni",start+2200,650);
            Wire(In(dispatch,"Beams"),Out(mapped,"Girders"));
            var lc=Panel("CASO PER LA MAPPA — peso proprio", "Dead",start+1000,1000);
            Wire(In(dispatch,"Case"),lc);In(analyses[0],"Analysis Results").AddSource(lc);
            Wire(In(dispatch,"Tolerance"),Panel("Tolleranza momento [N mm] — 1000 = 0.001 kN m","1000",start+1000,1160));
            Wire(In(dispatch,"Component"),Choice("Componente del momento",new[]{"BM1 / Mx","BM2 / My — flessione principale"},1,start+1000,1330));
            Wire(In(dispatch,"Sign"),Panel("Segno: +1 se BM2 positivo comprime la soletta","1",start+1000,1470));
            var mixed=Choice("Aste a segno variabile — approssimazione FEM",new[]{"0 — Ferma: affinare la mesh","1 — Intera asta non fessurata","2 — Intera asta fessurata"},2,start+1000,1650);
            var zero=Choice("Aste con momento entro tolleranza",new[]{"0 — Non fessurata","1 — Fessurata"},0,start+1000,1780);
            var info=Panel("MAPPA ASTE — ID / segno / estremi M [N mm]","",start+2850,480,950,650);info.AddSource(Out(dispatch,"Information"));
            var mixedBeams=new Param_GenericObject{Name="ASTE A SEGNO VARIABILE — controllare la mesh",NickName="ASTE A SEGNO VARIABILE"};mixedBeams.CreateAttributes();mixedBeams.Attributes.Pivot=new(start+2850,1300);mixedBeams.AddSource(Out(dispatch,"Mixed"));doc.AddObject(mixedBeams,false);added.Add(mixedBeams);
            const string assumptions="REV002 — mappa preliminare da Dead/BM2. Momento positivo: non fessurata; negativo: acciaio + armature.\nLa mappa non è una determinazione normativa delle zone fessurate. I conci Excel restano fisici, le proprietà cambiano per asta FEM.\nAste a segno variabile: scelta esplicita di approssimazione uniforme; non è un limite di sicurezza. Affinare Maximum Element Length e ricalcolare TUTTI i modelli per valutarne la sensibilità.\nM04 ritiro NON FESSURATO, per ipotesi esplicita: primario e secondario coerenti con la stessa deformazione. Verifica nel piano N–Mx–Vy.\n1) Read Excel. 2) Run M01 includendo Dead. 3) Controlla mappa/stati. 4) Run M02, M03, M04. 5) Run Build BridGHe Model e Super Element Check. 6) Generate Report.\nDopo ogni modifica a geometria, sezioni, carichi o politiche: ripetere le analisi. Nessun risultato FEM è incluso nel file consegnato.";
            var notes=Panel("PROCEDURA REV002 E IPOTESI",assumptions,start+100,-200,1900,550);
            for(int i=0;i<2;i++)
            {
                var old=oldOverrides[i];var replacement=Add("BridgeStateOverrideComponent",Tags[i+1]+" | proprietà per stato di ogni asta",old.Attributes.Pivot.X,old.Attributes.Pivot.Y);
                Wire(In(replacement,"Beams"),In(old,"Beams").Sources.ToArray());Wire(In(replacement,"Segment IDs"),In(old,"Segment IDs").Sources.ToArray());
                Wire(In(replacement,"Composite Sections"),Out(composite,"Composite Section"));Wire(In(replacement,"States"),Out(dispatch,"States"));
                Wire(In(replacement,"Response"),Panel("Risposta meccanica: 1=n infinito, 2=n0",(i+1).ToString(),old.Attributes.Pivot.X-700,old.Attributes.Pivot.Y+450));
                Wire(In(replacement,"Mixed Policy"),mixed);Wire(In(replacement,"Zero Policy"),zero);Wire(In(replacement,"Preserve Mass"),In(old,"Preserve Mass").Sources.ToArray());
                for(int p=0;p<old.Params.Output.Count;p++)foreach(var recipient in old.Params.Output[p].Recipients.ToArray())
                    Wire(recipient,recipient.Sources.Select(s=>ReferenceEquals(s,old.Params.Output[p])?replacement.Params.Output[p]:s).ToArray());
                foreach(var group in doc.Objects.OfType<GH_Group>().Where(g=>g.ObjectIDs.Contains(old.InstanceGuid)))
                {group.RemoveObject(old.InstanceGuid);group.AddObject(replacement.InstanceGuid);}
                doc.RemoveObject(old,false);
            }
            // Keep the user's exploratory branch, but distinguish it from the new model workflow.
            var experiment=doc.Objects.OfType<GH_Component>().SingleOrDefault(c=>c.InstanceGuid==new Guid("f1a81c47-40a1-4676-adba-c99604bfd190"));
            if(experiment!=null)experiment.NickName="PROVA REV001 — non collegata ai modelli";
            var existingIds=doc.Objects.Select(o=>o.InstanceGuid).ToHashSet();
            foreach(var g in doc.Objects.OfType<GH_Group>())foreach(var id in g.ObjectIDs.Where(id=>!existingIds.Contains(id)).ToArray())g.RemoveObject(id);
            var report=Find(doc,"BridgeReportComponent");
            foreach(var p in In(report,"Revision").Sources.OfType<GH_Panel>())p.UserText="REV002 — stati locali meccanici, ritiro non fessurato";
            var groupNew=new GH_Group{NickName="M0 → MAPPA DEL MOMENTO → STATI n∞ / n0 — RITIRO NON FESSURATO",Colour=Color.FromArgb(65,40,120,160)};groupNew.CreateAttributes();doc.AddObject(groupNew,false);
            foreach(var o in added.Where(o=>o.Attributes.Pivot.X<start+4400))groupNew.AddObject(o.InstanceGuid);
            // Refresh only explicit Read Excel components. No Create button, solver or report is run.
            Console.WriteLine("REV002: resolving geometry and refreshing the copied Excel (no FEM).");
            BridgeSegmentExcelTests.Solve(doc,true);
            foreach(var e in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeSegmentExcelComponent"))
                if(Values<int>(In(e,"Mode")).SingleOrDefault()==1){BridgeSegmentExcelTests.Press(e);e.ExpireSolution(false);}
            BridgeSegmentExcelTests.Solve(doc,true);
            var errors=doc.Objects.OfType<GH_ActiveObject>().SelectMany(o=>o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m=>o.NickName+": "+m)).ToArray();
            Check(errors.Length==0,string.Join("\n",errors));
            Check(Values<BeamPropertyModel>(Out(sections,"Properties")).Length>0,"Excel did not supply physical sections.");
            Check(doc.Objects.Count==beforeCount-2+added.Count+1,"object count changed unexpectedly");
            GH_Document.EnableSolutions=false;GrasshopperBridgeExample.Write(doc,target);
            Console.WriteLine("REV002: saved without results; starting disposable controlled-result fixture.");
            var validation=Validate(target);
            Check(File.ReadAllBytes(source).SequenceEqual(before)&&File.ReadAllBytes(sourceExcel).SequenceEqual(workbook),"source revision changed during generation");
            File.WriteAllText(Path.Combine(folder,"REV002-validation.json"),JsonSerializer.Serialize(new{Source="50-ponte-quattro-modelli_REV001.ghx",SourceSha256=hash,SourceWorkbookSha256=Convert.ToHexString(SHA256.HashData(workbook)),DefinitionSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))),SourceFilesUnmodified=true,NativeSolverInvoked=false,SyntheticResultsSaved=false,Validation=validation},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("REV002 GENERATED: "+target);return 0;
        }
        catch(Exception e){Console.Error.WriteLine("REV002 FAILED: "+e);return 1;}
    }
    private static object Validate(string path)
    {
        GH_Document.EnableSolutions=false;using var doc=GrasshopperBridgeExample.Read(path);doc.FilePath=path;
        var analyses=Tags.Select(t=>Find(doc,"StrausR3AnalysisComponent",t)).ToArray();
        BridgeSegmentExcelTests.Solve(doc,true);
        Check(analyses.All(a=>Values<ModelModel>(Out(a,"Model")).All(m=>m.Beams.Count==0)),"delivered file contains analysis caches");
        Check(analyses.All(a=>!Values<bool>(In(a,"Keep Running")).Any(v=>v)),"automatic analysis must be off");
        Check(Values<string>(In(analyses[0],"Analysis Results")).Contains("Dead"),"Dead is not requested for classification");
        var components=doc.Objects.OfType<GH_Component>().ToArray();
        for(int i=0;i<4;i++)
        {
            Console.WriteLine("REV002 fixture: "+Tags[i]);
            var build=Find(doc,"BuildModelComponent",Tags[i]);
            var input=Values<ModelModel>(Out(build,"Model")).Single();var snapshot=ModelArchive.Clone(input);
            BridGHe.Common.Helpers.ModelHelper.GetLoadCases(snapshot,out var loadCases);
            foreach(var beam in snapshot.Beams)
            {
                beam.Results.Clear();
                foreach(var lc in loadCases)foreach(double s in new[]{0d,.5,1d})
                {
                    double m=(int.Parse(beam.BeamId)%3) switch{0=>1e8,1=>-1e8,_=>(s-.5)*2e8};
                    beam.Results.Add(new BeamForceResultModel(lc,s,0,"",true){AxialForce=10*(i+1),My=m,Vy=100*(i+1)});
                }
            }
            foreach(var n in snapshot.Nodes)foreach(var lc in loadCases)n.Results.Add(new NodeReactResultModel(lc){Force=new Rhino.Geometry.Vector3d(0,0,1000*(i+1))});
            Cache(analyses[i],snapshot);analyses[i].ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc,true);
        }
        var stateOverrides=components.Where(c=>c.GetType().Name=="BridgeStateOverrideComponent").ToArray();
        Check(stateOverrides.Length==2,"two mechanical state overrides required");
        foreach(var c in stateOverrides)
        {
            var beams=Values<BeamModel>(Out(c,"Girders"));Check(beams.Length>0,"no assigned girders");
            Check(beams.Select(b=>b.Attributes.OfType<BridgeSectionStateAttributeModel>().Single().Value.ConcreteExcluded).Distinct().Count()==2,"both local states must reach each mechanical model");
        }
        var merge=Find(doc,"BuildBridgeModel");Press(merge);merge.ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc);
        var merged=Values<ModelModel>(Out(merge,"Model")).Single();
        Check(merged.BridgeWorkflow.Phases.Length==4&&merged.LoadCombinations.Count==5,"four phases / five combinations");
        Check(merged.BridgeWorkflow.Phases[1].Actions.All(a=>a.HasLocalSectionStates)&&merged.BridgeWorkflow.Phases[2].Actions.All(a=>a.HasLocalSectionStates),"states lost in mechanical actions");
        Check(merged.BridgeWorkflow.Phases[3].Actions.All(a=>!a.HasLocalSectionStates&&a.Behavior==BridgeActionBehavior.Shrinkage),"shrinkage state changed");
        Console.WriteLine("REV002 fixture: merged; checking sections, details, plots and report inputs.");
        var check=Find(doc,"SuperElementCheckComponent");Press(check);check.ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc);
        var rows=Values<BridgeStressResult>(Out(check,"Tension Check"));var main=merged.Beams.Where(b=>b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Family==BridgeMemberFamily.Girder).ToArray();
        Check(rows.Length>0&&rows.Select(r=>r.Source.BeamId).ToHashSet().SetEquals(main.Select(b=>b.BeamId)),"not all girders checked");
        Check(rows.Any(r=>r.Analysis.Input.Phases.Any(p=>p.Kind==GPC.Checkers.CompositeBridge.BridgePhaseKind.ConcreteExcluded)),"checker ignored local state");
        var details=components.Where(c=>c.GetType().Name=="BridgeDetailCheckComponent").ToArray();
        Check(details.Length==3&&details.All(c=>Out(c,"Section result").VolatileDataCount>0),"detail branch missing");
        Check(In(Find(doc,"BridgeReportComponent"),"Results").VolatileDataCount==details.Sum(c=>Out(c,"Section result").VolatileDataCount),"report missing results");
        Check(components.Where(c=>c.GetType().Name.EndsWith("PlotComponent")).All(c=>Out(c,"SVG").VolatileDataCount==1),"plot output missing");
        Console.WriteLine($"REV002 OFFLINE PASS: {main.Length} girders, {rows.Length} section results; two states, four phases, details, plots and report wiring. Controlled data discarded.");
        return new{GirderFEMBeams=main.Length,SectionResults=rows.Length,Phases=4,Combinations=5,MechanicalStates=2,ShrinkageUncracked=true,DetailBranches=3,ReportInputsChecked=true};
    }
}
