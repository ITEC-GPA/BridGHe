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
        byte[] bytes=File.ReadAllBytes(source);
        string folder=Path.GetFullPath("artifacts/rev003-native");Directory.CreateDirectory(folder);
        using var doc=GrasshopperBridgeExample.Read(source);doc.FilePath=source;
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
            Press(c);BridgeSegmentExcelTests.Solve(doc,true);
            var result=Values<ModelModel>(Out(c,"Model")).Single();Require(result.Beams.Any(b=>b.Results.Count>0),Errors(doc));
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
        var actions=model.BridgeWorkflow.Phases.Single(p=>p.Order==30).Actions.Where(a=>ids.Contains(a.Key)).ToArray();
        Require(actions.Length==cases.Length&&actions.All(a=>a.Behavior==BridgeActionBehavior.ShortTerm&&cases.Contains(a.SourceCase)),"Moving positions lost their n0 source association.");
        int active=0;
        foreach(var c in model.LoadCombinations)
        {int count=c.Values.Count(v=>ids.Any(id=>v.Key.Name==id+" [FEM]")&&v.Value!=0);Require(count<=1,"Different traffic positions were summed.");active+=count;}
        Require(active==cases.Length,"Each moving position must enter exactly one cumulative combination.");
        ResultFingerprint.Mark("REV003 checks start");var checker=Find(doc,"SuperElementCheckComponent");Press(checker);BridgeSegmentExcelTests.Solve(doc,true);
        var rows=Values<BridgeStressResult>(Out(checker,"Tension Check"));Require(rows.Length>0,"No section checks: "+Errors(doc));
        var details=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgeDetailCheckComponent").ToArray();
        Require(details.Length==3&&details.All(c=>Out(c,"Section result").VolatileDataCount>0),"Missing detail checks: "+Errors(doc));
        var plots=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name.EndsWith("PlotComponent")).ToArray();Require(plots.Length==6&&plots.All(c=>Out(c,"SVG").VolatileDataCount>0),"Missing SVG plots.");
        Require(In(Find(doc,"BridgeReportComponent"),"Results").VolatileDataCount==details.Sum(c=>Out(c,"Section result").VolatileDataCount),"Report input coverage.");
        Require(Errors(doc).Length==0,Errors(doc));
        ResultFingerprint.Mark("REV003 checks, details and plots solved");ResultFingerprint.Write("example50-rev003-native",doc);
        // A path change invalidates the cached moving analysis rather than leaking old positions.
        var pathComponent=Find(doc,"BridgeStraightMovingPathComponent");var divisions=(Param_Integer)In(pathComponent,"Divisions");divisions.RemoveAllSources();divisions.SetPersistentData(path.Divisions+1);pathComponent.ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc,true);
        Require(Values<ModelModel>(Out(moving,"Model")).Length==0&&Values<string>(Out(moving,"Cases")).Length==0,"Stale moving results survived changed input.");
        Require(bytes.SequenceEqual(File.ReadAllBytes(source)),"Native replay modified the delivered GHX.");
        var report=new{Native=true,Definition=Path.GetFileName(source),DefinitionSHA256=Convert.ToHexString(SHA256.HashData(bytes)),Analyses=5,PhaseCount=4,Beams=n0.Beams.Count,Nodes=n0.Nodes.Count,MovingPositions=cases.Length,SlabForceAssignments=slabAssignments,Combinations=model.LoadCombinations.Count,SectionChecks=rows.Length,DetailBranches=details.Length,Plots=plots.Length,ReportInputs=true,CachedRunFalse=true,ChangedPathInvalidatesResults=true,ResultsSavedInGHX=false,Measurements=measurements};
        string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(Path.Combine(folder,"validation.json"),json);File.WriteAllText(Path.Combine(Path.GetDirectoryName(source),"REV003-native-validation.json"),json);
        Console.WriteLine($"REV003 NATIVE PASS: 5 analyses, {cases.Length} moving positions, {model.LoadCombinations.Count} combinations, {rows.Length} section checks, 3 details, 6 plots.");return 0;
    }
}
