using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

/// <summary>Read-only audit of the user's saved bridge workflows. Native analyses, exports and report writes are disabled.</summary>
internal static class SavedBridgeWorkflowAudit
{
    internal static int Run()
    {
        BridgeMergeTests.Initialize();
        foreach(string name in new[]{"MathComponents","CurveComponents","SurfaceComponents","VectorComponents"})
        {
            var assembly=Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\"+name+".gha");
            typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(Assembly),typeof(string)},null)
                .Invoke(Grasshopper.Instances.ComponentServer,new object[]{assembly,assembly.Location});
        }
        var paths=new[]{"REV002","REV003","REV004"}.Select(r=>Path.GetFullPath(Path.Combine("example/99-Ponte completo GPC",r,"flusso-acciaio-misto_"+r+".ghx"))).ToArray();
        var rows=new List<object>();int failures=0;
        foreach(string path in paths)
        {
            string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            try
            {
                using var document=GrasshopperBridgeExample.Read(path);document.FilePath=path;
                var components=document.Objects.OfType<GH_Component>().ToArray();
                foreach(var c in components)
                {
                    // Only component-local inputs change, in this disposable document. Preserve all material/geometry toggles.
                    foreach(var p in c.Params.Input.OfType<Param_Boolean>().Where(p=>p.Name=="Keep Running"))
                    {p.RemoveAllSources();p.PersistentData.Clear();p.SetPersistentData(false);}
                    if(c.GetType().Name=="BridgeCombinationExcelComponent")
                    {var mode=(Param_Integer)c.Params.Input.Single(p=>p.Name=="Mode");mode.RemoveAllSources();mode.PersistentData.Clear();mode.SetPersistentData(1);}
                }
                void Solve()
                {
                    GH_Document.EnableSolutions=true;document.Enabled=true;document.NewSolution(false,GH_SolutionMode.Silent);
                    var errors=document.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>c.NickName+": "+e)).ToArray();
                    if(errors.Length>0)throw new Exception(string.Join(" | ",errors));
                }
                void RunButtons(string type)
                {
                    foreach(var c in components.Where(c=>c.GetType().Name==type))
                    {c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);}
                    Solve();
                }
                Solve();
                foreach(var excel in components.Where(c=>c.GetType().Name=="BridgeCombinationExcelComponent"))
                {
                    var catalog=new BridgeCombinationCatalog(excel.Params.Input[0].VolatileData.AllData(true).Select(g=>(ModelModel)g.ScriptVariable()).ToArray(),
                        excel.Params.Input[1].VolatileData.AllData(true).Select(g=>(BridgePhaseDefinition)g.ScriptVariable()));
                    File.WriteAllText("artifacts/"+Path.GetFileNameWithoutExtension(path)+"-catalog-"+excel.InstanceGuid+".json",JsonSerializer.Serialize(catalog.ReferenceRows));
                }
                RunButtons("BridgeCombinationExcelComponent");RunButtons("BuildBridgeModel");RunButtons("SuperElementCheckComponent");
                var checks=components.Where(c=>c.GetType().Name=="SuperElementCheckComponent").ToArray();
                var results=checks.SelectMany(c=>c.Params.Output[0].VolatileData.AllData(true)).Select(g=>g.ScriptVariable()).OfType<BridgeStressResult>().ToArray();
                if(checks.Length==0 || results.Length==0 || checks.Any(c=>c.Params.Output[0].VolatileDataCount==0))throw new Exception("Missing section results in a saved check branch.");
                var warnings=document.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Select(w=>c.NickName+": "+w)).Distinct().ToArray();
                rows.Add(new{file=path,status="passed",sectionResults=results.Length,checkBranches=checks.Length,warnings});
                Console.WriteLine($"SAVED BRIDGE AUDIT PASS: {Path.GetFileName(path)}, {results.Length} section results, {checks.Length} check branches; no native solver or report generation.");
            }
            catch(Exception error)
            {failures++;rows.Add(new{file=path,status="failed",error=error.Message});Console.WriteLine("SAVED BRIDGE AUDIT FAILED: "+Path.GetFileName(path)+": "+error.Message);}
            if(hash!=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))throw new Exception("Audit modified the user's GHX: "+path);
        }
        File.WriteAllText("artifacts/saved-bridge-workflow-audit.json",JsonSerializer.Serialize(new{nativeSolverInvoked=false,sourceDocumentsUnchanged=true,failures,documents=rows},new JsonSerializerOptions{WriteIndented=true}));
        return failures==0?0:1;
    }
}
