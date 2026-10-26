using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

/// <summary>Execute the saved bridge graph with real Straus results; never regenerate it or save analysis caches into GHX.</summary>
internal static class NativeModularBridgeTests
{
    private static IGH_Param Input(GH_Component c,string name)=>c.Params.Input.Single(p=>p.Name==name);
    private static IGH_Param Output(GH_Component c,string name)=>c.Params.Output.Single(p=>p.Name==name);
    private static T[] Values<T>(IGH_Param p)=>p.VolatileData.AllData(true).Select(g=>g.ScriptVariable()).OfType<T>().ToArray();
    private static void SetText(GH_Component c,string name,string text)
    {var p=(Param_String)Input(c,name);p.RemoveAllSources();p.SetPersistentData(text);}
    private static void Press(GH_Component c)
    {c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);}
    private static void ClearAnalysis(GH_Component c)
    {
        var field=c.GetType().GetField("_model",BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(c,Activator.CreateInstance(field.FieldType));
        c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,false);
        var p=(Param_Boolean)Input(c,"Keep Running");p.RemoveAllSources();p.SetPersistentData(false);
    }
    internal static int Run(string selected=null)
    {
        BridgeExampleMigration.Initialize();
        string relative=selected??@"50-ponte-quattro-modelli\50-ponte-quattro-modelli_REV002.ghx";
        string source=Path.Combine(ExamplePaths.Root,relative),candidate=Path.GetFullPath(Path.Combine("artifacts/modular-bridge/candidates",relative));
        if(!File.Exists(candidate))throw new FileNotFoundException("Prepare the migrated candidate first.",candidate);
        string folder=Path.GetFullPath("artifacts/modular-bridge/native-"+Path.GetFileNameWithoutExtension(source));Directory.CreateDirectory(folder);
        byte[] original=File.ReadAllBytes(source),migrated=File.ReadAllBytes(candidate);
        using var doc=GrasshopperBridgeExample.Read(candidate);doc.FilePath=source;
        var analyses=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="StrausR3AnalysisComponent").OrderBy(c=>c.NickName).ToArray();
        if(analyses.Length!=4)throw new Exception("Expected four Straus phase models.");
        foreach(var a in analyses){ClearAnalysis(a);SetText(a,"Output Path",Path.Combine(folder,a.NickName.Split(' ')[0]+".st7"));}
        foreach(var report in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeReportComponent"))report.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(report,false);
        BridgeSegmentExcelTests.Solve(doc,true);
        foreach(var read in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeSectionReadComponent")){BridgeSegmentExcelTests.Press(read);read.ExpireSolution(false);}
        BridgeSegmentExcelTests.Solve(doc,true);
        var summary=new List<object>();
        for(int i=0;i<analyses.Length;i++)
        {
            var a=analyses[i];var input=Values<ModelModel>(Input(a,"Model")).SingleOrDefault();
            if(input==null||input.Beams.Count==0)throw new Exception("Phase model missing before analysis: "+a.NickName+"\n"+Errors(doc));
            Console.WriteLine("NATIVE MODULAR: "+a.NickName+"; "+input.Beams.Count+" beams, "+input.Nodes.Count+" nodes.");
            Press(a);BridgeSegmentExcelTests.Solve(doc,true);
            var result=Values<ModelModel>(Output(a,"Model")).SingleOrDefault();
            if(result==null||!result.Beams.Any(b=>b.Results.Count>0))throw new Exception("No native results: "+a.NickName+"\n"+Errors(doc));
            var equilibrium=new List<object>();
            var cases=result.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Select(r=>r.LoadCase.Name).Distinct().ToArray();
            if(cases.Length==0)throw new Exception("Native reaction results missing.");
            foreach(string lc in cases)
            {
                double actual=result.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Where(r=>r.LoadCase.Name==lc).Sum(r=>r.Force.Z);
                double expected=VerticalLoad(input,lc);
                double tolerance=Math.Max(.01,Math.Abs(expected)*1e-7);
                if(!double.IsFinite(actual)||Math.Abs(actual-expected)>tolerance)throw new Exception($"Vertical equilibrium {lc}: {actual:G17} vs {expected:G17} N; tolerance {tolerance}.");
                equilibrium.Add(new{Case=lc,ReactionZ_N=actual,Expected_N=expected,Tolerance_N=tolerance});
            }
            string path=Values<string>(Input(a,"Output Path")).Single();string log=Path.ChangeExtension(path,".LSL");
            if(File.Exists(log)){string text=File.ReadAllText(log);if(text.Contains("*WARNING[")||text.Contains("*ERROR["))throw new Exception("Straus solver warnings: "+log);}
            summary.Add(new{Phase=i,Beams=input.Beams.Count,Nodes=input.Nodes.Count,Equilibrium=equilibrium});
            File.WriteAllText(Path.Combine(folder,"progress.json"),JsonSerializer.Serialize(summary,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("NATIVE MODULAR PASS: "+a.NickName+"; reactions in equilibrium.");
        }
        var merge=doc.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BuildBridgeModel");Press(merge);BridgeSegmentExcelTests.Solve(doc,true);
        var model=Values<ModelModel>(Output(merge,"Model")).SingleOrDefault();if(model?.BridgeWorkflow?.Phases.Length!=4)throw new Exception("Four-phase merge failed.\n"+Errors(doc));
        var check=doc.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="SuperElementCheckComponent");Press(check);BridgeSegmentExcelTests.Solve(doc,true);
        var rows=Values<BridgeStressResult>(Output(check,"Tension Check"));if(rows.Length==0)throw new Exception("No section check results.\n"+Errors(doc));
        var details=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeDetailCheckComponent").ToArray();
        if(details.Any(c=>Output(c,"Section result").VolatileDataCount==0))throw new Exception("A detail check branch has no output.\n"+Errors(doc));
        var plots=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name.EndsWith("PlotComponent")).ToArray();
        if(plots.Any(c=>Output(c,"SVG").VolatileDataCount==0))throw new Exception("A plot has no SVG.\n"+Errors(doc));
        var reportComponent=doc.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BridgeReportComponent");
        if(Input(reportComponent,"Results").VolatileDataCount!=details.Sum(c=>Output(c,"Section result").VolatileDataCount))throw new Exception("Incomplete report result input.");
        string errors=Errors(doc);if(errors.Length>0)throw new Exception("Saved graph still has errors:\n"+errors);
        if(!original.SequenceEqual(File.ReadAllBytes(source))||!migrated.SequenceEqual(File.ReadAllBytes(candidate)))throw new Exception("Native test modified a GHX source.");
        File.WriteAllText(Path.Combine(folder,"validation.json"),JsonSerializer.Serialize(new{Definition=relative,SourceSHA256=Convert.ToHexString(SHA256.HashData(original)),MigratedSHA256=Convert.ToHexString(SHA256.HashData(migrated)),Native=true,Models=summary,Phases=model.BridgeWorkflow.Phases.Length,Combinations=model.LoadCombinations.Count,SectionChecks=rows.Length,DetailBranches=details.Length,Plots=plots.Length,ReportInputs=true,ResultsSavedInGHX=false},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"NATIVE MODULAR BRIDGE PASS: 4 models, {model.LoadCombinations.Count} combinations, {rows.Length} section results, {details.Length} details, {plots.Length} plots, report inputs. GHX files unchanged.");return 0;
    }
    private static string Errors(GH_Document doc)=>string.Join("\n",doc.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(s=>c.NickName+": "+s)));
    private static double VerticalLoad(ModelModel model,string name)
    {
        var combination=model.LoadCombinations.SingleOrDefault(c=>c.Name==name);
        if(combination!=null)return combination.Values.Sum(p=>p.Value*VerticalLoad(model,p.Key.Name));
        double sum=0;
        ModelHelper.GetLoadCases(model,out var cases);var lc=cases.SingleOrDefault(c=>c.Name==name)??throw new ArgumentException("Unknown native result case: "+name);
        if(lc.GlobalInertiaLoad==LoadCaseModel.GlobalInertiaLoads.Gravity&&lc.GravityDirection==LoadCaseModel.GravityDirections.Z)
            sum-=model.Beams.Sum(b=>b.BeamProperty.Material.Density*b.BeamProperty.SectionArea*b.Length)*lc.GravityValue;
        foreach(var b in model.Beams)foreach(var load in b.Loads.Where(l=>l.LoadCase.Name==name))
        {
            if(load is BeamDistributedLoadModel d)
            {
                var v=d.Value;if(d.DistributedLoadType!=BeamDistributedLoadModel.LoadType.Force||v.LoadSchema!=BeamDistributedLoadModel.LoadSchema.Uniform||v.CoordinateSystem==null||v.LoadDirection!=BeamDistributedLoadModel.LoadDirection.Z)
                    throw new NotSupportedException("Independent equilibrium fixture requires uniform global Z forces.");
                sum-=v.PA*b.Length*(1-v.A-v.B);
            }
            else if(load is not (BeamPreLoadModel or BeamGradientLoadModel or CompositeBeamSlabStrainModel))throw new NotSupportedException("Add independent integration for "+load.GetType().Name);
        }
        foreach(var node in model.Nodes)foreach(var load in node.Loads.Where(l=>l.LoadCase.Name==name))
            if(load is NodeForceLoadModel f)sum-=f.Value.Z;
        return sum;
    }
}
