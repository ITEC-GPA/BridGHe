using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static class GrasshopperExamples
{
    internal static int Run(string selected)
    {
        var assembly=Assembly.LoadFrom(Path.GetFullPath("src/BridGHe.Grasshopper/bin/Release/net8.0-windows/BridGHe.Grasshopper.gha"));
        // Isolated test process only. Avoid starting every third-party plugin installed
        // in the user's Rhino profile. No profile settings or installed files are changed.
        var field=typeof(Grasshopper.Instances).GetField("m_comServer",BindingFlags.NonPublic|BindingFlags.Static)
            ??throw new NotSupportedException("Rhino 8 component-server field changed.");
        if(field.GetValue(null)!=null)throw new InvalidOperationException("GH example generation requires a fresh process.");
        var server=new GH_ComponentServer();field.SetValue(null,server);
        var parse=typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance,null,new[]{typeof(Assembly),typeof(string)},null)
            ??throw new NotSupportedException("Rhino 8 assembly loader changed.");
        foreach(var a in new[]{typeof(GH_Document).Assembly,assembly})
        {Console.WriteLine("GH REGISTER "+a.GetName().Name);parse.Invoke(server,new object[]{a,a.Location});}
        var modelType=assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model",true);
        Directory.CreateDirectory("examples/grasshopper");
        foreach(var e in NativeExamples.Create().Where(x=>selected=="all"||x.Id.StartsWith(selected,StringComparison.Ordinal)))
        {
            Console.WriteLine("GH BUILD "+e.Id);
            using var doc=new GH_Document();
            void Add(IGH_DocumentObject item,float x,float y){item.CreateAttributes();item.Attributes.Pivot=new PointF(x,y);doc.AddObject(item,false);}
            var note=new GH_Panel{UserText=e.Title+"\nUnita N-mm. Materiale elastico E=210000 MPa, nu=0.3.\nModello incorporato: menu del parametro per gestire i dati.\nImpostare il percorso .st7 prima di eseguire. Vedere examples/README.md."};
            Add(note,20,10);note.Attributes.Bounds=new RectangleF(20,10,700,110);
            var input=new Param_GenericObject{Name="Modello di ingresso",NickName="Modello"};
            input.PersistentData.Append((IGH_Goo)modelType.GetMethod("FromModel").Invoke(null,new object[]{e.Model,null}));Add(input,100,200);
            string component=e.Moving?"MovingStaticAnalysisComponent":"StrausR3AnalysisComponent";
            var analysis=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.Analysis."+component,true));Add(analysis,550,220);analysis.Params.Input[0].AddSource(input);
            var path=new GH_Panel{UserText=Path.Combine(Path.GetTempPath(),"BridGHe-"+e.Id+".st7")};Add(path,20,390);path.Attributes.Bounds=new RectangleF(20,390,450,60);
            analysis.Params.Input[e.Moving?1:3].AddSource(path);
            if(e.Moving){var toggle=new GH_BooleanToggle{Value=false};Add(toggle,320,290);analysis.Params.Input[2].AddSource(toggle);}
            else{
                var solver=(Param_Integer)analysis.Params.Input[1];solver.PersistentData.Clear();solver.SetPersistentData(e.Solver);
                var names=new GH_Panel{UserText=string.Join(Environment.NewLine,e.Cases)};names.Properties.Multiline=false;Add(names,230,160);names.Attributes.Bounds=new RectangleF(230,160,250,120);analysis.Params.Input[2].AddSource(names);
            }
            var parts=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.Models.DecomposeModelComponent",true));Add(parts,800,220);parts.Params.Input[0].AddSource(analysis.Params.Output[0]);
            var nodeResults=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.Results.NodeResultsComponent",true));Add(nodeResults,1000,170);nodeResults.Params.Input[0].AddSource(parts.Params.Output[0]);
            var displacements=new GH_Panel{NickName="Spostamenti XYZ [mm]"};Add(displacements,1200,100);displacements.Attributes.Bounds=new RectangleF(1200,100,400,170);displacements.AddSource(nodeResults.Params.Output[2]);
            bool plate=e.Model.Plates.Count>0;
            var forceResults=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.Results."+(plate?"PlateResultsComponent":"BeamResultsComponent"),true));Add(forceResults,1000,400);forceResults.Params.Input[0].AddSource(parts.Params.Output[plate?2:1]);
            var forces=new GH_Panel{NickName=plate?"Nxx, Nyy, Nxy [N/mm]":"Momenti BM1, BM2 [N mm]"};Add(forces,1200,340);forces.Attributes.Bounds=new RectangleF(1200,340,400,220);forces.AddSource(forceResults.Params.Output[plate?3:4]);
            var archive=new GH_Archive();Console.WriteLine("GH SERIALIZE "+e.Id);
            if(!archive.AppendObject(doc,"Definition")||!archive.WriteToFile(Path.GetFullPath("examples/grasshopper/"+e.Id+".ghx"),true,false))throw new Exception("GH archive write failed");
            Console.WriteLine("GH WRITTEN "+e.Id);
            var restoredArchive=new GH_Archive();using var restored=new GH_Document();
            if(!restoredArchive.ReadFromFile(Path.GetFullPath("examples/grasshopper/"+e.Id+".ghx"))||!restoredArchive.ExtractObject(restored,"Definition")||restored.Objects.Count!=doc.Objects.Count)
                throw new Exception("GH roundtrip failed: "+e.Id);
            var restoredAnalysis=restored.Objects.OfType<GH_Component>().Single(c=>c.GetType()==analysis.GetType());
            var restoredPath=(GH_Panel)restoredAnalysis.Params.Input[e.Moving?1:3].Sources.Single();
            string output=Path.GetFullPath(Path.Combine("artifacts","gh-examples",e.Id));Directory.CreateDirectory(output);restoredPath.UserText=Path.Combine(output,"model.st7");
            if(e.Moving)((GH_BooleanToggle)restoredAnalysis.Params.Input[2].Sources.Single()).Value=true;
            else {var run=(Param_Boolean)restoredAnalysis.Params.Input[4];run.PersistentData.Clear();run.SetPersistentData(true);}
            GH_Document.EnableSolutions=true;restored.Enabled=true;
            Console.WriteLine("GH SOLVE "+e.Id);restored.NewSolution(false,GH_SolutionMode.Silent);
            var errors=restored.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error)).ToArray();
            if(errors.Length>0)throw new Exception(string.Join(Environment.NewLine,errors));
            var resultGoo=restoredAnalysis.Params.Output[0].VolatileData.AllData(true).Single();
            var result=(ModelModel)modelType.GetProperty("Value").GetValue(resultGoo);
            var restoredNodes=restored.Objects.OfType<GH_Component>().Single(c=>c.GetType()==nodeResults.GetType());
            if(restoredNodes.Params.Output[2].VolatileDataCount==0)throw new Exception("Node result component has no displacement output.");
            var restoredForces=restored.Objects.OfType<GH_Component>().Single(c=>c.GetType()==forceResults.GetType());
            if(restoredForces.Params.Output[plate?3:4].VolatileDataCount==0)throw new Exception("Element result component has no force output.");
            var recorder=new NativeExamples.Recorder();e.Verify(result,recorder);
            if(recorder.Measurements.Any(m=>!m.Passed))throw new Exception("GH numerical comparison failed: "+e.Id);
            File.WriteAllText(Path.Combine(output,"report.json"),System.Text.Json.JsonSerializer.Serialize(recorder.Measurements,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("GH VERIFIED "+e.Id);
        }
        return 0;
    }
}
