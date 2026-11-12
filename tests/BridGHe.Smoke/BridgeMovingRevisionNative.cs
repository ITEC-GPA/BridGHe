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
using BridGHe.Common.MovingLoads;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using static BridgeMovingRevisionExample;

internal static class BridgeMovingRevisionNative
{
    internal static int Run(string source)
    {
#if GPC_PILOT
        return RunGpc(source);
#else
        byte[] bytes=File.ReadAllBytes(source);
        // P2S: the capture input writes into artifacts/rev003-native as before; another REV003 (--input) next to its copy, never in the worktree.
        bool capture=string.Equals(Path.GetFullPath(source),CaptureInput,StringComparison.OrdinalIgnoreCase);
        string folder=capture?Path.GetFullPath("artifacts/rev003-native"):Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source)),"rev003-native");Directory.CreateDirectory(folder);
        using var doc=GrasshopperBridgeExample.Read(source);doc.FilePath=source;
        // Diagnostics (BRIDGHE_SERIAL=1): the task-capable components solve their iterations serially.
        if(Environment.GetEnvironmentVariable("BRIDGHE_SERIAL")=="1")foreach(var t in doc.Objects.OfType<IGH_TaskCapableComponent>())t.UseTasks=false;
        var analyses=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="StrausR3AnalysisComponent").OrderBy(c=>c.NickName).ToArray();
        void OutputPath(GH_Component c,string port,string file){var p=(Param_String)In(c,port);p.RemoveAllSources();p.SetPersistentData(Path.Combine(folder,file));}
        foreach(var c in analyses){Clear(c);OutputPath(c,"Output Path",c.NickName.Split(' ')[0]+".st7");}
        var moving=Find(doc,"MovingStaticAnalysisComponent");Clear(moving);OutputPath(moving,"Output path","M03_N0_MOBILE.st7");
        BridgeSegmentExcelTests.Solve(doc,true);
        foreach(var read in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeSectionReadComponent")){BridgeSegmentExcelTests.Press(read);read.ExpireSolution(false);}
        BridgeSegmentExcelTests.Solve(doc,true);ResultFingerprint.Mark("REV003 inputs solved");
        var measurements=new List<object>();
        void Near(string label,double actual,double expected,double tolerance)
        {Require(double.IsFinite(actual)&&Math.Abs(actual-expected)<=tolerance,$"{label}: {actual:G17} vs {expected:G17}, tol={tolerance}");measurements.Add(new{Label=label,Actual=actual,Expected=expected,Tolerance=tolerance});}
        foreach(var c in analyses)
        {
            var input=Values<ModelModel>(In(c,"Model")).Single();Console.WriteLine("REV003 NATIVE: "+c.NickName);
            MigrationCapture.Write(c.NickName.Split(' ')[0]+"-input", input);
            Press(c);BridgeSegmentExcelTests.Solve(doc,true);
            var result=Values<ModelModel>(Out(c,"Model")).Single();Require(result.Beams.Any(b=>b.Results.Count>0),Errors(doc));
            MigrationCapture.Write(c.NickName.Split(' ')[0]+"-result", result);
            foreach(string name in result.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Select(r=>r.LoadCase.Name).Distinct())
            {
                double expected=NativeModularBridgeTests.VerticalLoad(input,name);
                Near(name+" Rz [N]",result.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Where(r=>r.LoadCase.Name==name).Sum(r=>r.Force.Z),expected,Math.Max(.01,Math.Abs(expected)*1e-7));
            }
            File.WriteAllBytes(Path.Combine(folder,c.NickName.Split(' ')[0]+".bgmodel"),ModelArchive.Write(result));
        }
        var n0=Values<ModelModel>(In(analyses[2],"Model")).Single();var trafficInput=Values<ModelModel>(In(moving,"Model")).Single();
        Require(trafficInput.Beams.Count==n0.Beams.Count&&trafficInput.Nodes.Count==n0.Nodes.Count,"Traffic topology differs from n0.");
        foreach(var b in trafficInput.Beams)
        {var reference=n0.Beams.Single(r=>r.BeamId==b.BeamId);Require(b.PointFrom==reference.PointFrom&&b.PointTo==reference.PointTo&&BridgeSectionAssignment.Signature(b.BeamProperty)==BridgeSectionAssignment.Signature(reference.BeamProperty),"Traffic changed n0 geometry/rigidity.");}
        Console.WriteLine("REV003 NATIVE: moving positions on unchanged n0 properties.");
        Press(moving);BridgeSegmentExcelTests.Solve(doc,true);
        var traffic=Values<ModelModel>(Out(moving,"Model")).SingleOrDefault();Require(traffic!=null,"Moving analysis failed: "+Errors(doc));
        File.WriteAllBytes(Path.Combine(folder,"moving.bgmodel"),ModelArchive.Write(traffic));
        int slabAssignments=BridgeMovingSlabTargets.Run(folder);
        var path=trafficInput.MovingPaths.Single();var xyz=path.Coordinates;var start=new Point3d(xyz[0],xyz[1],xyz[2]);var end=new Point3d(xyz[3],xyz[4],xyz[5]);
        var axis=end-start;double length=axis.Length;axis.Unitize();var lateral=new Vector3d(xyz[6]-xyz[0],xyz[7]-xyz[1],xyz[8]-xyz[2]);lateral.Unitize();
        var wheels=path.Template.Vehicles.Single().Loads.Cast<MovingPointForce>().ToArray();double step=length/path.Divisions,maxX=wheels.Max(p=>p.X),minX=wheels.Min(p=>p.X);
        string[] cases=traffic.GeneratedMovingCases.OrderBy(c=>c.Key).Select(c=>c.Value).ToArray();int expectedCount=(int)Math.Ceiling((length+maxX-minX)/step-.5);
        Require(cases.Length==expectedCount,"Unexpected position count.");
        Require(traffic.Beams.All(b=>b.Results.OfType<BeamForceResultModel>().Select(r=>r.LoadCase.Name).Distinct().Count()==cases.Length),"Missing moving beam results.");
        for(int i=0;i<cases.Length;i++)
        {
            Vector3d force=Vector3d.Zero,moment=Vector3d.Zero;double front=(i+.5)*step;
            foreach(var w in wheels)
            {
                double s=front+w.X-maxX;if(s<0||s>length)continue;
                var point=start+s*axis+w.Y*lateral;var f=new Vector3d(w.Force[0],w.Force[1],w.Force[2]);force-=f;moment-=Vector3d.CrossProduct((Vector3d)point,f);
            }
            Vector3d actualForce=Vector3d.Zero,actualMoment=Vector3d.Zero;
            foreach(var node in traffic.Nodes)
            {var r=node.Results.OfType<NodeReactResultModel>().Single(r=>r.LoadCase.Name==cases[i]);actualForce+=r.Force;actualMoment+=Vector3d.CrossProduct((Vector3d)node.Position,r.Force)+r.Moment;}
            for(int d=0;d<3;d++)
            {Near(cases[i]+" F"+d+" [N]",actualForce[d],force[d],Math.Max(.02,Math.Abs(force.Z)*1e-7));Near(cases[i]+" M"+d+" [Nmm]",actualMoment[d],moment[d],Math.Max(100,Math.Abs(force.Z)*length*1e-7));}
        }
        // False does not discard results, nor rerun the native solver when the input fingerprint is unchanged.
        string resultFile=Path.Combine(folder,"M03_N0_MOBILE.lsa");var written=File.GetLastWriteTimeUtc(resultFile);
        moving.ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc,true);
        Require(Values<ModelModel>(Out(moving,"Model")).SingleOrDefault()?.GeneratedMovingCases.Count==cases.Length&&File.GetLastWriteTimeUtc(resultFile)==written,"Moving result cache lost or solver reran while Run=False.");
        var combo=Find(doc,"BridgeMovingCombinationsComponent");var ids=Values<string>(Out(combo,"Action IDs"));
        ResultFingerprint.Mark("REV003 merge start");var merge=Find(doc,"BuildBridgeModel");Press(merge);BridgeSegmentExcelTests.Solve(doc,true);
        var model=Values<ModelModel>(Out(merge,"Model")).SingleOrDefault();Require(model?.BridgeWorkflow?.Phases.Length==4,"Four-phase merge failed: "+Errors(doc));
        MergedCapture.Write("example50-rev003",model);
        var actions=model.BridgeWorkflow.Phases.Single(p=>p.Order==30).Actions.Where(a=>ids.Contains(a.Key)).ToArray();
        Require(actions.Length==cases.Length&&actions.All(a=>a.Behavior==BridgeActionBehavior.ShortTerm&&cases.Contains(a.SourceCase)),"Moving positions lost their n0 source association.");
        int active=0;
        foreach(var c in model.LoadCombinations)
        {int count=c.Values.Count(v=>ids.Any(id=>v.Key.Name==id+" [FEM]")&&v.Value!=0);Require(count<=1,"Different traffic positions were summed.");active+=count;}
        Require(active==cases.Length,"Each moving position must enter exactly one cumulative combination.");
        ResultFingerprint.Mark("REV003 checks start");var checker=Find(doc,"SuperElementCheckComponent");Press(checker);BridgeSegmentExcelTests.Solve(doc,true);
        var rows=Values<BridgeStressResult>(Out(checker,"Tension Check"));Require(rows.Length>0,"No section checks: "+Errors(doc));
#if GPC_PILOT
        C5WorkflowComparison.RequireGpcChecks(Out(merge,"Model").VolatileData.AllData(true).Single(),model,new[]{checker},false,"50");
        GpcMergeCheck.Require(merge,"50");
        GpcBridgeComparison.RequireComponent(merge,"50");
        GpcBridgeComparison.RequireGpcLinks(doc,"50");
        GpcInputComparison.RequireComponents(doc,"50"); BuildModelScan.Print(doc,"50");
        GpcSecForcesCheck.Require(checker,"50"); GpcCompositeSectionChecks.RequireSuperElementCheck(checker,"50");
        GpcSecForcesCheck.RequireGirders(doc,"50");
        GpcSecForcesCheck.RequireMomentDispatch(doc,"50"); GpcStateOverrideChecks.RequireComponents(doc,"50"); GpcStateOverrideChecks.MeasureModelAreas(doc,"50"); BarLayoutMeasurement.Report(doc,"50"); CompositeAnalysisPropertyChecks.Require(doc,"50"); NativeParityCapture.Close();
#endif
        var details=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeDetailCheckComponent").ToArray();
        Require(details.Length==3&&details.All(c=>Out(c,"Section result").VolatileDataCount>0),"Missing detail checks: "+Errors(doc));
        var plots=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name.EndsWith("PlotComponent")).ToArray();Require(plots.Length==6&&plots.All(c=>Out(c,"SVG").VolatileDataCount>0),"Missing SVG plots.");
        Require(In(Find(doc,"BridgeReportComponent"),"Results").VolatileDataCount==details.Sum(c=>Out(c,"Section result").VolatileDataCount),"Report input coverage.");
        Require(Errors(doc).Length==0,Errors(doc));
        ResultFingerprint.Mark("REV003 checks, details and plots solved");ResultFingerprint.Write("example50-rev003-native",doc);
        // F6A measurement (BRIDGHE_SAVE_TIMING=<folder>): size and time of saving the solved definition as a binary .gh (what Grasshopper
        // writes on save and autosave), and of reading it back. Measurement only; the delivered definition is not touched.
        if(Environment.GetEnvironmentVariable("BRIDGHE_SAVE_TIMING") is { Length: > 0 } timing)
        {
            Directory.CreateDirectory(timing);string saved=Path.Combine(Path.GetFullPath(timing),"example50-rev003-solved.gh");
            var clock=System.Diagnostics.Stopwatch.StartNew();var archive=new GH_IO.Serialization.GH_Archive();
            Require(archive.AppendObject(doc,"Definition"),"Append the solved definition.");var serialized=clock.Elapsed;
            Require(archive.WriteToFile(saved,true,false),"Write the solved definition.");var writeTime=clock.Elapsed;
            clock.Restart();var reread=new GH_IO.Serialization.GH_Archive();Require(reread.ReadFromFile(saved),"Read the solved definition.");
            TimeSpan read;string drafts="";
            using(var reopened=new GH_Document())
            {
                Require(reread.ExtractObject(reopened,"Definition"),"Reopen the solved definition.");read=clock.Elapsed;
#if GPC_PILOT
                drafts=", "+AnalysisDraftsAfterReopen(doc,reopened);
#endif
            }
            string line=$"SAVE TIMING example50-rev003 solved: {new FileInfo(saved).Length} bytes, serialize {serialized.TotalSeconds:F1} s, write {writeTime.TotalSeconds:F1} s, read+reopen {read.TotalSeconds:F1} s{drafts}";
            File.WriteAllText(Path.Combine(timing,"save-timing.txt"),line+Environment.NewLine);Console.WriteLine(line);
        }
#if GPC_PILOT
        GpcOnlyReplay.Rehearsal(doc,"50"); // L3e: only with BRIDGHE_GPC_ONLY_REHEARSAL=1
#endif
        // A path change invalidates the cached moving analysis rather than leaking old positions.
        var pathComponent=Find(doc,"BridgeStraightMovingPathComponent");var divisions=(Param_Integer)In(pathComponent,"Divisions");divisions.RemoveAllSources();divisions.SetPersistentData(path.Divisions+1);pathComponent.ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc,true);
        Require(Values<ModelModel>(Out(moving,"Model")).Length==0&&Values<string>(Out(moving,"Cases")).Length==0,"Stale moving results survived changed input.");
        Require(bytes.SequenceEqual(File.ReadAllBytes(source)),"Native replay modified the delivered GHX.");
        var report=new{Native=true,Definition=Path.GetFileName(source),DefinitionSHA256=Convert.ToHexString(SHA256.HashData(bytes)),Analyses=5,PhaseCount=4,Beams=n0.Beams.Count,Nodes=n0.Nodes.Count,MovingPositions=cases.Length,SlabForceAssignments=slabAssignments,Combinations=model.LoadCombinations.Count,SectionChecks=rows.Length,DetailBranches=details.Length,Plots=plots.Length,ReportInputs=true,CachedRunFalse=true,ChangedPathInvalidatesResults=true,ResultsSavedInGHX=false,Measurements=measurements};
        string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(Path.Combine(folder,"validation.json"),json);if(!capture)File.WriteAllText(Path.Combine(Path.GetDirectoryName(source),"REV003-native-validation.json"),json);
        Console.WriteLine($"REV003 NATIVE PASS: 5 analyses, {cases.Length} moving positions, {model.LoadCombinations.Count} combinations, {rows.Length} section checks, 3 details, 6 plots.");return 0;
#endif
    }
#if GPC_PILOT
    /// <summary>
    /// R6.6 lane R4c-moving: the native replay of the 50 REV003 read by the GPC data of the outputs alone (no FEM value of a model is read by an
    /// assertion of this runner):
    /// <list type="bullet">
    /// <item>every analysis: its GPC input (<c>GpcInput</c>), its analysed GPC model with the results catalog, the vertical reactions of every result
    /// (the rows of Node Results) against the independent integration of the vertical loads of the GPC input (<see cref="R4cMoving.VerticalLoad"/>),
    /// and the results file the component keeps next to its output (<c>&lt;output&gt;.bghr</c>, in place of the former <c>.bgmodel</c> archive);</item>
    /// <item>the traffic model against n0 (same nodes and beams, every beam by its BridGHe ID at the same ends with the same GPC property);</item>
    /// <item>the moving results read on the true GPC-only output (R6.6 plan: the consumers take <c>FromGpc(input)</c>): the moving analysis pressed again on
    /// its input given by its GPC input alone (<see cref="R4cMoving.GpcOnlyAnalysis"/>), equal to the analysis of the definition (R1's comparison:
    /// fingerprint, catalog, every result row); on it the count of the positions from the geometry of the path, the beam results of every position, the
    /// equilibrium of every position against the wheels of the vehicle and the native load attributes of its file (<see cref="BridgeMovingSlabTargets"/>);</item>
    /// <item>Run False keeps the moving results (the catalog of the output, the result file not rewritten); a changed path drops them;</item>
    /// <item>the merge read on the GPC bridge model of Build BridGHe Model (<c>GpcBridge</c>: its records, the former <c>BridgeWorkflow</c>, and its
    /// combination table, the former <c>LoadCombinations</c>): four phases, the moving positions short-term actions of n0 cases in phase 30, each in one
    /// combination alone.</item>
    /// </list>
    /// TRANSITIONAL (other lanes, P2 decides): the parity checks of C5, P2T, L3 and F7d on the FEM path (they feed the native capture) still take the FEM value
    /// of the merged model. The FEM captures (BRIDGHE_MIGRATION_CAPTURE writes the GPC data instead, <see cref="R4cMoving.Capture"/>; BRIDGHE_MERGED_CAPTURE,
    /// retired on 6/11) are not written.
    /// </summary>
    private static int RunGpc(string source)
    {
        byte[] bytes=File.ReadAllBytes(source);
        bool capture=string.Equals(Path.GetFullPath(source),CaptureInput,StringComparison.OrdinalIgnoreCase);
        string folder=capture?Path.GetFullPath("artifacts/rev003-native"):Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source)),"rev003-native");Directory.CreateDirectory(folder);
        using var doc=GrasshopperBridgeExample.Read(source);doc.FilePath=source;
        if(Environment.GetEnvironmentVariable("BRIDGHE_SERIAL")=="1")foreach(var t in doc.Objects.OfType<IGH_TaskCapableComponent>())t.UseTasks=false;
        var analyses=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="StrausR3AnalysisComponent").OrderBy(c=>c.NickName).ToArray();
        void OutputPath(GH_Component c,string port,string file){var p=(Param_String)In(c,port);p.RemoveAllSources();p.SetPersistentData(Path.Combine(folder,file));}
        foreach(var c in analyses){Clear(c);OutputPath(c,"Output Path",c.NickName.Split(' ')[0]+".st7");}
        var moving=Find(doc,"MovingStaticAnalysisComponent");Clear(moving);OutputPath(moving,"Output path","M03_N0_MOBILE.st7");
        BridgeSegmentExcelTests.Solve(doc,true);
        foreach(var read in doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeSectionReadComponent")){BridgeSegmentExcelTests.Press(read);read.ExpireSolution(false);}
        BridgeSegmentExcelTests.Solve(doc,true);ResultFingerprint.Mark("REV003 inputs solved");
        // R4c-moving (diagnosis, BRIDGHE_R4CM_DRY=1, not native): no analysis pressed, the independent integration of the requested results printed by part.
        if(Environment.GetEnvironmentVariable("BRIDGHE_R4CM_DRY")=="1"){R4cMoving.DryLoads(analyses.Select(a=>(a.NickName,R4cMoving.Input(In(a,"Model").VolatileData.AllData(true).SingleOrDefault()),Values<string>(In(a,"Analysis Results")))).ToArray());return 0;}
        var measurements=new List<object>();
        void Near(string label,double actual,double expected,double tolerance)
        {Require(double.IsFinite(actual)&&Math.Abs(actual-expected)<=tolerance,$"{label}: {actual:G17} vs {expected:G17}, tol={tolerance}");measurements.Add(new{Label=label,Actual=actual,Expected=expected,Tolerance=tolerance});}
        static object One(IGH_Param p)=>p.VolatileData.AllData(true).SingleOrDefault();
        foreach(var c in analyses)
        {
            string name=c.NickName.Split(' ')[0];
            var input=R4cMoving.Input(One(In(c,"Model")));Require(input!=null,"No GPC input: "+c.NickName+"\n"+Errors(doc));Console.WriteLine("REV003 NATIVE: "+c.NickName);
            R4cMoving.Capture(name+"-input",input:input);
            Press(c);BridgeSegmentExcelTests.Solve(doc,true);
            var result=R4cMoving.Results(One(Out(c,"Model")));Require(result!=null&&result.HasBeamRows(),Errors(doc));
            R4cMoving.Capture(name+"-result",analysed:result.Model);
            foreach(string label in result.ReactionLabels())
            {
                double expected=R4cMoving.VerticalLoad(input.Model,label,result.Catalog.Units);
                Near(label+" Rz [N]",result.ReactionZ(label),expected,Math.Max(.01,Math.Abs(expected)*1e-7));
            }
            Require(File.Exists(Path.Combine(folder,name+".bghr")),"The analysis did not keep its results next to its output: "+name);
        }
        var n0=R4cMoving.Input(One(In(analyses[2],"Model")));var trafficInput=R4cMoving.Input(One(In(moving,"Model")));
        Require(n0!=null&&trafficInput!=null,"No GPC input of n0 or of the traffic model.");
        Require(trafficInput.Model.BeamElements.Count==n0.Model.BeamElements.Count&&trafficInput.Model.NodesElements.Count==n0.Model.NodesElements.Count,"Traffic topology differs from n0.");
        static bool Same(GPC.Geometry.Point3d a,GPC.Geometry.Point3d b)=>a.X==b.X&&a.Y==b.Y&&a.Z==b.Z;
        var fingerprints=new Dictionary<object,string>(ReferenceEqualityComparer.Instance);
        string Property(GPC.Model.ElementProperties.BeamProperty p)=>fingerprints.TryGetValue(p,out var f)?f:fingerprints[p]=GPC.Model.Core.ModelValues.Fingerprint(new object[]{p});
        var n0Beams=n0.Model.BeamElements.Values.ToDictionary(b=>b.Name,StringComparer.Ordinal);
        foreach(var b in trafficInput.Model.BeamElements.Values)
        {var reference=n0Beams[b.Name];Require(Same(b.StartPoint,reference.StartPoint)&&Same(b.EndPoint,reference.EndPoint)&&Property(b.BeamProperty)==Property(reference.BeamProperty),"Traffic changed n0 geometry/rigidity.");}
        fingerprints.Clear();n0Beams=null;
        Console.WriteLine("REV003 NATIVE: moving positions on unchanged n0 properties.");
        Press(moving);BridgeSegmentExcelTests.Solve(doc,true);
        var definitionTraffic=One(Out(moving,"Model"));Require(R4cMoving.Results(definitionTraffic)!=null,"Moving analysis failed: "+Errors(doc));
        // P2R (T3, coordinator decision of 12/11): the GPC-only copy of the moving analysis (its input given by its GPC input alone, solved again) is
        // retired: since P2K the analysis of the definition is itself on GPC data alone. Its checks read the analysis of the definition.
        var traffic=R4cMoving.Results(definitionTraffic);Require(traffic!=null,"Moving analysis without GPC results.");
        int slabAssignments=BridgeMovingSlabTargets.Run(folder);
        var path=trafficInput.Model.MovingLoads.Paths.Single();
        var start=new Point3d(path.Start.X,path.Start.Y,path.Start.Z);var end=new Point3d(path.End.X,path.End.Y,path.End.Z);
        var axis=end-start;double length=axis.Length;axis.Unitize();var lateral=new Vector3d(path.Reference.X-path.Start.X,path.Reference.Y-path.Start.Y,path.Reference.Z-path.Start.Z);lateral.Unitize();
        var wheels=path.Template.Vehicles.Single().PointForces;double step=length/path.Divisions,maxX=wheels.Max(p=>p.X),minX=wheels.Min(p=>p.X);
        string[] cases=traffic.Catalog.GeneratedMovingCases().Select(g=>g.Name).ToArray();int expectedCount=(int)Math.Ceiling((length+maxX-minX)/step-.5);
        Require(cases.Length==expectedCount,"Unexpected position count.");
        foreach(var b in traffic.Model.BeamElements.Values)
            Require(BridGHe.Common.Gpc.GpcResultRows.Beam(traffic.Catalog,traffic.Model,b).Select(r=>r.Entry).Distinct().Count()==cases.Length,"Missing moving beam results.");
        var nodes=traffic.Nodes;var nodeRows=traffic.NodeRows;
        for(int i=0;i<cases.Length;i++)
        {
            Vector3d force=Vector3d.Zero,moment=Vector3d.Zero;double front=(i+.5)*step;
            foreach(var w in wheels)
            {
                double s=front+w.X-maxX;if(s<0||s>length)continue;
                var point=start+s*axis+w.Y*lateral;var f=new Vector3d(w.Force.X,w.Force.Y,w.Force.Z);force-=f;moment-=Vector3d.CrossProduct((Vector3d)point,f);
            }
            Vector3d actualForce=Vector3d.Zero,actualMoment=Vector3d.Zero;
            for(int k=0;k<nodes.Count;k++)
            {
                var r=nodeRows[k].Single(x=>x.Reaction!=null&&traffic.Label(x.Entry)==cases[i]).Reaction;var position=nodes[k].Position;
                var rf=new Vector3d(r[0],r[1],r[2]);actualForce+=rf;actualMoment+=Vector3d.CrossProduct(new Vector3d(position.X,position.Y,position.Z),rf)+new Vector3d(r[3],r[4],r[5]);
            }
            for(int d=0;d<3;d++)
            {Near(cases[i]+" F"+d+" [N]",actualForce[d],force[d],Math.Max(.02,Math.Abs(force.Z)*1e-7));Near(cases[i]+" M"+d+" [Nmm]",actualMoment[d],moment[d],Math.Max(100,Math.Abs(force.Z)*length*1e-7));}
        }
        traffic=null;nodes=null;nodeRows=null;
        // False does not discard results, nor rerun the native solver when the input fingerprint is unchanged.
        string resultFile=Path.Combine(folder,"M03_N0_MOBILE.lsa");var written=File.GetLastWriteTimeUtc(resultFile);
        moving.ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc,true);
        Require(R4cMoving.Results(One(Out(moving,"Model")))?.Catalog.GeneratedMovingCases().Count==cases.Length&&File.GetLastWriteTimeUtc(resultFile)==written,"Moving result cache lost or solver reran while Run=False.");
        var combo=Find(doc,"BridgeMovingCombinationsComponent");var ids=Values<string>(Out(combo,"Action IDs"));
        ResultFingerprint.Mark("REV003 merge start");var merge=Find(doc,"BuildBridgeModel");Press(merge);BridgeSegmentExcelTests.Solve(doc,true);
        var mergedGoo=One(Out(merge,"Model"));var bridge=R4cMoving.Bridge(mergedGoo);Require(bridge?.Records?.Phases.Length==4,"Four-phase merge failed: "+Errors(doc));
        var actions=bridge.Records.Phases.Single(p=>p.Order==30).Actions.Where(a=>ids.Contains(a.Key)).ToArray();
        Require(actions.Length==cases.Length&&actions.All(a=>a.Behavior==BridgeActionBehavior.ShortTerm&&cases.Contains(a.SourceCase)),"Moving positions lost their n0 source association.");
        int active=0;
        foreach(var c in bridge.Combinations)
        {int count=c.Factors.Count(v=>ids.Any(id=>v.Case==id+" [FEM]")&&v.Factor!=0);Require(count<=1,"Different traffic positions were summed.");active+=count;}
        Require(active==cases.Length,"Each moving position must enter exactly one cumulative combination.");
        int combinations=bridge.Combinations.Count;
        ResultFingerprint.Mark("REV003 checks start");var checker=Find(doc,"SuperElementCheckComponent");Press(checker);BridgeSegmentExcelTests.Solve(doc,true);
        var rows=Values<BridgeStressResult>(Out(checker,"Tension Check"));Require(rows.Length>0,"No section checks: "+Errors(doc));int sectionChecks=rows.Length;rows=null;
        // P2R (T2/R5, 12/11): the sources of the native capture on GPC data alone, against the frozen capture (its «fem-constant» keys retired by
        // name, P2rRetired). Retired with the FEM merged model (their coverage: the capture, the result fingerprint, r2b-merge-forces,
        // gpc-bridge-model, gpc-state-override): GpcMergeCheck.Require, GpcBridgeComparison.RequireComponent, GpcInputComparison.RequireComponents,
        // GpcStateOverrideChecks.RequireComponents and MeasureModelAreas (FEM-vs-GPC parities).
        C5WorkflowComparison.RequireGpcChecks(mergedGoo,new[]{checker},false,"50");
        GpcBridgeComparison.RequireGpcLinks(doc,"50"); BuildModelScan.Print(doc,"50");
        GpcSecForcesCheck.Require(checker,"50"); GpcCompositeSectionChecks.RequireSuperElementCheck(checker,"50");
        GpcSecForcesCheck.RequireGirders(doc,"50");
        GpcSecForcesCheck.RequireMomentDispatch(doc,"50"); BarLayoutMeasurement.Report(doc,"50"); CompositeAnalysisPropertyChecks.Require(doc,"50");
        bridge=null;mergedGoo=null;
        var details=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeDetailCheckComponent").ToArray();
        Require(details.Length==3&&details.All(c=>Out(c,"Section result").VolatileDataCount>0),"Missing detail checks: "+Errors(doc));
        var plots=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name.EndsWith("PlotComponent")).ToArray();Require(plots.Length==6&&plots.All(c=>Out(c,"SVG").VolatileDataCount>0),"Missing SVG plots.");
        Require(In(Find(doc,"BridgeReportComponent"),"Results").VolatileDataCount==details.Sum(c=>Out(c,"Section result").VolatileDataCount),"Report input coverage.");
        Require(Errors(doc).Length==0,Errors(doc));
        ResultFingerprint.Mark("REV003 checks, details and plots solved");ResultFingerprint.Write("example50-rev003-native",doc);
        NativeParityCapture.Close(); // P2R: after the result fingerprint, so that a capture difference still leaves the fingerprint of the run
        if(Environment.GetEnvironmentVariable("BRIDGHE_SAVE_TIMING") is { Length: > 0 } timing)
        {
            Directory.CreateDirectory(timing);string saved=Path.Combine(Path.GetFullPath(timing),"example50-rev003-solved.gh");
            var clock=System.Diagnostics.Stopwatch.StartNew();var archive=new GH_IO.Serialization.GH_Archive();
            Require(archive.AppendObject(doc,"Definition"),"Append the solved definition.");var serialized=clock.Elapsed;
            Require(archive.WriteToFile(saved,true,false),"Write the solved definition.");var writeTime=clock.Elapsed;
            clock.Restart();var reread=new GH_IO.Serialization.GH_Archive();Require(reread.ReadFromFile(saved),"Read the solved definition.");
            TimeSpan read;string drafts="";
            using(var reopened=new GH_Document())
            {Require(reread.ExtractObject(reopened,"Definition"),"Reopen the solved definition.");read=clock.Elapsed;drafts=", "+AnalysisDraftsAfterReopen(doc,reopened);}
            string line=$"SAVE TIMING example50-rev003 solved: {new FileInfo(saved).Length} bytes, serialize {serialized.TotalSeconds:F1} s, write {writeTime.TotalSeconds:F1} s, read+reopen {read.TotalSeconds:F1} s{drafts}";
            File.WriteAllText(Path.Combine(timing,"save-timing.txt"),line+Environment.NewLine);Console.WriteLine(line);
        }
        // R4c-moving (task 2): the diagnosis of the chain of the rehearsal (BRIDGHE_R4CM_CHAIN=1) and of its report (BRIDGHE_R4CM_REPORT=1); then the rehearsal (BRIDGHE_GPC_ONLY_REHEARSAL=1)
        // after a full collection, with the memory of the process before and after (the Bridge Report of its chain: OutOfMemoryException).
        R4cChainDiagnosis.Run(doc,"50");R4cChainDiagnosis.Report(doc,"50");
        bool rehearsal=Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_REHEARSAL")=="1";
        if(rehearsal)R4cMoving.Release("50 before the rehearsal");
        try{GpcOnlyReplay.Rehearsal(doc,"50");}
        finally{if(rehearsal)R4cMoving.Memory("50 after the rehearsal");}
        // A path change invalidates the cached moving analysis rather than leaking old positions.
        var pathComponent=Find(doc,"BridgeStraightMovingPathComponent");var divisions=(Param_Integer)In(pathComponent,"Divisions");divisions.RemoveAllSources();divisions.SetPersistentData(path.Divisions+1);pathComponent.ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc,true);
        Require(!Out(moving,"Model").VolatileData.AllData(true).Any(g=>R4cMoving.Results(g)!=null)&&Values<string>(Out(moving,"Cases")).Length==0,"Stale moving results survived changed input.");
        Require(bytes.SequenceEqual(File.ReadAllBytes(source)),"Native replay modified the delivered GHX.");
        var report=new{Native=true,Definition=Path.GetFileName(source),DefinitionSHA256=Convert.ToHexString(SHA256.HashData(bytes)),Analyses=5,PhaseCount=4,Beams=n0.Model.BeamElements.Count,Nodes=n0.Model.NodesElements.Count,MovingPositions=cases.Length,SlabForceAssignments=slabAssignments,Combinations=combinations,SectionChecks=sectionChecks,DetailBranches=details.Length,Plots=plots.Length,ReportInputs=true,CachedRunFalse=true,ChangedPathInvalidatesResults=true,ResultsSavedInGHX=false,Measurements=measurements};
        string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(Path.Combine(folder,"validation.json"),json);if(!capture)File.WriteAllText(Path.Combine(Path.GetDirectoryName(source),"REV003-native-validation.json"),json);
        Console.WriteLine($"REV003 NATIVE PASS: 5 analyses, {cases.Length} moving positions, {combinations} combinations, {sectionChecks} section checks, 3 details, 6 plots (GPC data alone).");return 0;
    }
#endif
#if GPC_PILOT
    /// <summary>R6.4 (2), option 1 of P2Q (BRIDGHE_SAVE_TIMING): in the definition saved and reopened, every analysis component keeps its model
    /// without the drafts of its elements and with the fingerprint of its input; at the first solution the drafts of the input of the solved
    /// definition (the same input) are attached again, equal to those of the run.</summary>
    private static string AnalysisDraftsAfterReopen(GH_Document solved,GH_Document reopened)
    {
        const BindingFlags all=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;int components=0,attached=0;
        foreach(var c in reopened.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent"))
        {
            var t=c.GetType();var original=(GH_Component)solved.FindObject(c.InstanceGuid,true);
            var model=t.GetField("_model",all).GetValue(c);var helper=t.GetField("_drafts",all).GetValue(c);var property=model.GetType().GetProperty("GpcDrafts");
            var run=property.GetValue(t.GetField("_model",all).GetValue(original)) as BridGHe.Common.Gpc.GpcModelDrafts;
            Require(property.GetValue(model)==null,$"{c.NickName} ({c.InstanceGuid}): drafts saved with the analysed model.");
            bool pending=(bool)helper.GetType().GetProperty("Pending").GetValue(helper);
            Require(pending==(run!=null),$"{c.NickName} ({c.InstanceGuid}): fingerprint of the input saved: {pending}, drafts of the run: {run!=null}.");
            components++;if(!pending)continue;
            var input=original.Params.Input[0].VolatileData.AllData(true).Single();
            var warning=helper.GetType().GetMethod("Restore").Invoke(helper,new object[]{input,model,null});
            var back=property.GetValue(model) as BridGHe.Common.Gpc.GpcModelDrafts;
            Require(warning==null&&back!=null&&GpcDraftArchiveChecks.DraftsText(back)==GpcDraftArchiveChecks.DraftsText(run),$"{c.NickName} ({c.InstanceGuid}): drafts attached again after reopening: {warning ?? "different"}.");
            attached++;
        }
        Require(components==5,"Analysis components in the reopened definition: "+components);
        return $"{components} analysis components saved without drafts, {attached} attached again equal";
    }
#endif
}
