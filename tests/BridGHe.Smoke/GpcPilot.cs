#if GPC_PILOT
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using GPC.Model.Core;
using GPC.Model.Models;
using GPC.Model.Persistence;
using GPC.Persistence;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class GpcPilot
{
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static void Near(double a,double b,string name)=>Check(double.IsFinite(a)&&Math.Abs(a-b)<=Math.Max(1e-7,Math.Abs(b)*2e-8),name+" "+a.ToString("G12"));
    internal static int Run()
    {
        StrausRuntime.EnsureInitialized();
        string reopen=Environment.GetEnvironmentVariable("GPC_PILOT_REOPEN"); if(!string.IsNullOrEmpty(reopen)) { BridgeMergeTests.Initialize(); return ReopenFolder(reopen); }
        string folder=Path.GetFullPath(Path.Combine("artifacts","f4","bridge-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));Directory.CreateDirectory(folder);
        var first=RunCase(folder,0); var reversed=RunCase(folder,1); Compare(first,reversed,false);
        Console.WriteLine("PASS I/J reversal preserves global nodal response");
        Console.WriteLine("F4 BRIDGE PASS "+folder);return 0;
    }
    private static int ReopenFolder(string folder)
    {
        int count=0;
        foreach(string document in Directory.GetFiles(folder,"*.ghx"))
        {
            var reader=new GH_Archive(); Check(reader.ReadFromFile(document),"fresh process GH read");
            using(var doc=new GH_Document())
            {
                Check(reader.ExtractObject(doc,"Definition"),"fresh process GH reopen");
                string text=doc.Objects.OfType<GH_Panel>().Single().UserText;
                ExternalModelReference reference;
                using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(text))) reference=ExternalModelReference.Read(stream);
                var run=ExternalModelArchive.Open(document,reference);
                Check(Numeric(run.Model)==File.ReadAllText(document+".numeric.sha256"),"fresh process numeric bytes");
                Check(run.Model.AnalysisFingerprint()==File.ReadAllText(document+".input.sha256"),"fresh process input fingerprint");
                Check(ModelValues.Fingerprint(new object[]{run.SolverMap})==File.ReadAllText(document+".map.sha256"),"fresh process solver identities");
                count++;
            }
        }
        Check(count>0,"saved pilot documents exist"); Console.WriteLine("F4 REOPEN PASS "+count); return 0;
    }
    static string Numeric(Model model)
    {
        using var stream=new MemoryStream(); ModelArchive.Save(model,stream); stream.Position=0;
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read); using var input=zip.GetEntry("results.bin").Open(); using var bytes=new MemoryStream(); input.CopyTo(bytes); return ArchiveBlock.Digest(bytes.ToArray());
    }
    static ModelModel RunCase(string folder,int variant)
    {
        var timings=new List<string>(); var timer=Stopwatch.StartNew();
        void Mark(string name){timings.Add(name+","+timer.Elapsed.TotalSeconds.ToString("R",CultureInfo.InvariantCulture));timer.Restart();}
        var assembly=BridgeMergeTests.Initialize(); using var doc=new GH_Document();
        var build=(GH_Component)Activator.CreateInstance(assembly.GetTypes().Single(t=>t.Name=="BuildModelComponent"));build.CreateAttributes();doc.AddObject(build,false);
        var lc=new LoadCaseModel("P","P");
        var points=new[]{new Point3d(0,0,0),new Point3d(3000,600,100),new Point3d(6000,1200,200)};
        var nodes=points.Select((p,i)=>new NodeModel(i.ToString(),p)).ToArray();
        // Short two-element bridge, pin/roller-type supports; torsion held at both ends.
        nodes[0].Attributes.Add(new NodeRestraintAttributeModel(true,true,true,true,false,false));
        nodes[2].Attributes.Add(new NodeRestraintAttributeModel(false,true,true,true,false,false));
        nodes[1].Loads.Add(new NodeForceLoadModel(lc,new Vector3d(120,80,-1000)));
        nodes[1].Loads.Add(new NodeMomentLoadModel(lc,new Vector3d(5000,7000,9000)));
        var property=new BeamPropertyModel("1"){Name="Elastic reference",PropertyType=BeamPropertyType.Beam,Material=new MaterialModel("Steel"){Modulus=210000,PoissonRatio=.3,Density=0},Section=new SectionModel{SectionType=SectionModel.SectionTypes.Generic,SectionArea=60000,I11=450000000,I22=200000000,J=100000000,ShearA1=0,ShearA2=0}};
        var beams=new[]{new BeamModel(points[0],points[1],property,angleDeg:23){BeamId="left",Offset=new Vector2d(40,75)},new BeamModel(variant==0?points[1]:points[2],variant==0?points[2]:points[1],property,angleDeg:variant==0?23:-23){BeamId="right",Offset=new Vector2d(variant==0?40:-40,75)}};
        void Add(object value,string name){var goo=(IGH_Goo)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_"+name));goo.GetType().GetProperty("Value").SetValue(goo,value);((Param_GenericObject)build.Params.Input[0]).PersistentData.Append(goo);}
        foreach(var n in nodes)Add(n,"Node");foreach(var b in beams)Add(b,"Beam");
        ((Param_String)build.Params.Input[1]).PersistentData.Clear(); ((Param_Integer)build.Params.Input[2]).PersistentData.Clear();
        ((Param_String)build.Params.Input[1]).SetPersistentData("F4 bridge "+variant);((Param_Integer)build.Params.Input[2]).SetPersistentData(2);
        GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
        Check(build.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,"GH build");
        var input=(ModelModel)build.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        Console.WriteLine("INPUT "+input.Nodes.Count+" nodes, "+input.Beams.Count+" beams; units="+input.Units);
        Mark("ghBuildSeconds");
        var legacy=StrausAnalysis.Run(input,Path.Combine(folder,"baseline-"+variant+".st7"),0,new[]{"P"},1e-6,false,out var oldWarnings);
        Mark("legacySolveSeconds");
        var model=GpcPilotAdapter.Import(input,GpcPilotAdapter.Identity(new Guid("7666411e-1815-40d8-bb83-89b91240340d"),"branch:"+variant));
        Mark("gpcImportSeconds");
        string fingerprint=model.AnalysisFingerprint();var branch=ModelValues.CopyValue(model);branch.NodesElements.Values.First().Position.Z+=10;
        Check(model.AnalysisFingerprint()==fingerprint&&branch.AnalysisFingerprint()!=fingerprint,"branch isolation");
            var unsupported=ModelValues.CopyValue(model); unsupported.NodesElements.Values.First().Assignments.Active=false;
            bool rejected=false; try { GpcPilotAdapter.Export(unsupported,out _); } catch(NotSupportedException) { rejected=true; }
            Check(rejected,"unsupported assignment rejected");
        foreach(Action<Model> addUnsupported in new Action<Model>[] {
            m=>m.MovingLoads=new GPC.Model.Actions.Moving.MovingLoadDefinition(),
            m=>m.BeamElements.Values.First().Assignments.Physical=new GPC.Model.Structure.Assignments.BeamPhysicalData {
                Taper=new GPC.Model.Structure.Assignments.BeamSectionTaper {ScaleJ1=1.5} } })
        {
            var extended=ModelValues.CopyValue(model);addUnsupported(extended);bool refused=false;
            try {GpcPilotAdapter.Export(extended,out _);}catch(NotSupportedException){refused=true;}
            Check(refused,"F5 data rejected by the limited F4 adapter");
        }
        var exported=GpcPilotAdapter.Export(model,out var map);
        Mark("gpcCopyAndExportSeconds");
        var solved=StrausAnalysis.Run(exported,Path.Combine(folder,"gpc-"+variant+".st7"),0,new[]{"P"},1e-6,false,out var newWarnings);
        Mark("gpcSolveSeconds");
        Compare(legacy,solved);Check(model.AnalysisFingerprint()==fingerprint,"solver leaves GPC input unchanged");
        // Global equilibrium independently of the adapter (both original and migrated solver paths).
        Near(solved.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Sum(r=>r.Force.Z),1000,"vertical equilibrium [N]");
        GpcPilotAdapter.Attach(model,solved,map); string resultDigest=Numeric(model);
        Mark("compareAndAttachSeconds");
        var archive=new ExternalModelArchive();var reference=archive.Prepare(model,map);var panel=new GH_Panel();panel.CreateAttributes();doc.AddObject(panel,false);
        string document=Path.Combine(folder,"bridge-"+variant+".ghx");
        archive.Save(ExternalModelArchive.PathFor(document),new[]{reference},refs=>
        {
            using(var s=new MemoryStream()){refs[0].Write(s);panel.UserText=Encoding.UTF8.GetString(s.ToArray());}
            var gh=new GH_Archive();Check(gh.AppendObject(doc,"Definition"),"append GH document");
            BlockArchive.WriteFile(document,s=>{byte[] bytes=Encoding.UTF8.GetBytes(gh.Serialize_Xml());s.Write(bytes,0,bytes.Length);});
        });
        Mark("prepareAndSaveSeconds");
            File.WriteAllText(document+".numeric.sha256",resultDigest);
            File.WriteAllText(document+".input.sha256",fingerprint);
            File.WriteAllText(document+".map.sha256",ModelValues.Fingerprint(new object[]{map}));
        var read=new GH_Archive();Check(read.ReadFromFile(document),"read GHX");using var reopened=new GH_Document();Check(read.ExtractObject(reopened,"Definition"),"reopen GH document");
        ExternalModelReference saved;using(var s=new MemoryStream(Encoding.UTF8.GetBytes(reopened.Objects.OfType<GH_Panel>().Single().UserText)))saved=ExternalModelReference.Read(s);
        var run=ExternalModelArchive.Open(document,saved);Check(run.Model.AnalysisFingerprint()==fingerprint,"reopened physical inputs");
        Check(Numeric(run.Model)==resultDigest,"reopened exact result bytes");
        Check(run.Model.AllElements.Sum(e=>e.Results.Count)==model.AllElements.Sum(e=>e.Results.Count),"reopened result count");
        Mark("reopenAndVerifySeconds");
        var again=GpcPilotAdapter.Export(run.Model,out _);var repeated=StrausAnalysis.Run(again,Path.Combine(folder,"reopened-"+variant+".st7"),0,new[]{"P"},1e-6,false,out _);Compare(legacy,repeated);
        File.WriteAllLines(Path.Combine(folder,"metrics-"+variant+".csv"),new[]{"metric,seconds"}.Concat(timings));
        File.WriteAllText(Path.Combine(folder,"case-"+variant+".txt"),"PASS\ninput="+fingerprint+"\nlegacyWarnings="+string.Join(";",oldWarnings)+"\ngpcWarnings="+string.Join(";",newWarnings));
        return solved;
    }
    static void Compare(ModelModel expected,ModelModel actual,bool compareBeams=true)
    {
        Check(expected.Nodes.Count==actual.Nodes.Count&&expected.Beams.Count==actual.Beams.Count,"topology counts");
        for(int i=0;i<expected.Nodes.Count;i++)
        {
            var a=actual.Nodes[i];var e=expected.Nodes[i];Check(a.Position==e.Position,"node identity "+i);
            var ad=a.Results.OfType<NodeDisplResultModel>().Single();var ed=e.Results.OfType<NodeDisplResultModel>().Single();
            for(int j=0;j<3;j++){Near(ad.Displacement[j],ed.Displacement[j],"U "+i+":"+j);Near(ad.Rotation[j],ed.Rotation[j],"R "+i+":"+j);}
            var ar=a.Results.OfType<NodeReactResultModel>().Single();var er=e.Results.OfType<NodeReactResultModel>().Single();
            for(int j=0;j<3;j++){Near(ar.Force[j],er.Force[j],"reaction "+i+":"+j);Near(ar.Moment[j],er.Moment[j],"moment reaction "+i+":"+j);}
        }
        if(!compareBeams)return;
        for(int i=0;i<expected.Beams.Count;i++)
        {
            var a=actual.Beams[i].Results.OfType<BeamForceResultModel>().OrderBy(f=>f.Station).ToArray();var e=expected.Beams[i].Results.OfType<BeamForceResultModel>().OrderBy(f=>f.Station).ToArray();Check(a.Length==e.Length,"station count");
            for(int j=0;j<a.Length;j++){Near(a[j].Station,e[j].Station,"station");Near(a[j].AxialForce,e[j].AxialForce,"N");Near(a[j].Vx,e[j].Vx,"V1");Near(a[j].Vy,e[j].Vy,"V2");Near(a[j].Torque,e[j].Torque,"T");Near(a[j].Mx,e[j].Mx,"BM1");Near(a[j].My,e[j].My,"BM2");}
        }
    }
}
#endif
