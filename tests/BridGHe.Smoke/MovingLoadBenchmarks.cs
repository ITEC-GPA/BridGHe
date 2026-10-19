using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using BridGHe.Common.Straus;

/// <summary>Independent simply-supported beam references; no road-code load model is implied.</summary>
internal static class MovingLoadBenchmarks
{
    private const double Length = 6000, E = 210000, I = 450000000;
    internal sealed record Scenario(string Id, int Positions, double Force, double[] Offsets, bool Reverse = false);
    private static readonly Scenario[] Scenarios = {
        new("01-single-13",13,1000,new[]{0.0}),
        new("02-single-25",25,1000,new[]{0.0}),
        new("03-reverse",13,1000,new[]{0.0},true),
        new("04-tandem",13,1000,new[]{-750.0,750}),
        new("05-double-force",13,2000,new[]{0.0})
    };
    private static double[] Axles(Scenario s,int step)
    {
        double margin=s.Offsets.Max(Math.Abs), t=(step+.5)/s.Positions;
        double center=margin+(Length-2*margin)*(s.Reverse?1-t:t);
        return s.Offsets.Select(a=>center+(s.Reverse?-a:a)).ToArray();
    }
    private static double Moment(Scenario s,double[] axles,double x)
        => s.Force*axles.Sum(a=>1-a/Length)*x-s.Force*axles.Sum(a=>Math.Max(0,x-a));
    // Euler-Bernoulli Green function for a downward point force; negative global Uz.
    private static double Displacement(Scenario s,double[] axles,double x)
        => -s.Force*axles.Sum(a=>x<=a ? (Length-a)*x*(Length*Length-(Length-a)*(Length-a)-x*x)
            : a*(Length-x)*(Length*Length-a*a-(Length-x)*(Length-x)))/(6*Length*E*I);
    internal static int Run(bool native,string selection="all")
    {
        string root=Path.Combine(ExamplePaths.Calculations("21-benchmark-carichi-mobili"),DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(root);
        var selected=Scenarios.Where(s=>selection=="all"||s.Id.StartsWith(selection,StringComparison.Ordinal)).ToArray();
        if(selected.Length==0)throw new ArgumentException("Unknown moving-load scenario: "+selection);
        var checks=new NativeExamples.Recorder();
        string status="running";string error=null;
        try
        {
            foreach(var scenario in selected)
            {
                string folder=Path.Combine(root,scenario.Id);Directory.CreateDirectory(folder);
                var model=ModelArchive.Clone(NativeExamples.Create().Single(e=>e.Id=="04-moving-load").Model);
                model.Name=scenario.Id;var path=model.MovingPaths.Single();path.LoadCaseName=scenario.Id;
                path.Divisions=scenario.Positions;double margin=scenario.Offsets.Max(Math.Abs);
                double start=scenario.Reverse?Length-margin:margin,end=scenario.Reverse?margin:Length-margin;
                path.Coordinates=new[]{start,0,0,end,0,0,start,1000,0};
                path.Template.Vehicles[0].Loads=scenario.Offsets.Select(a=>(MovingObject)new MovingPointForce{X=a,Force=new[]{0.0,0,-scenario.Force}}).ToArray();
                path.Validate();
                File.WriteAllBytes(Path.Combine(folder,"input.bgmodel"),ModelArchive.Write(model));
                var roundtrip=ModelArchive.Read<ModelModel>(File.ReadAllBytes(Path.Combine(folder,"input.bgmodel")));
                checks.Near(scenario.Id+" archive positions",roundtrip.MovingPaths.Single().Divisions,scenario.Positions,0,0);
                checks.Near(scenario.Id+" archive axles",roundtrip.MovingPaths.Single().Template.Vehicles[0].Loads.Length,scenario.Offsets.Length,0,0);
                var csv=new StringBuilder("step,axles_mm,Rleft_N,Rright_N,Mmax_Nmm,Uz_mid_mm\n");
                for(int step=0;step<scenario.Positions;step++)
                {
                    var axles=Axles(scenario,step);double right=scenario.Force*axles.Sum()/Length;
                    csv.AppendLine(FormattableString.Invariant($"{step+1},{string.Join(";",axles.Select(a=>a.ToString("G17",CultureInfo.InvariantCulture)))},{scenario.Force*axles.Length-right:G17},{right:G17},{axles.Max(a=>Moment(scenario,axles,a)):G17},{Displacement(scenario,axles,Length/2):G17}"));
                }
                File.WriteAllText(Path.Combine(folder,"analytical.csv"),csv.ToString());
                if(!native)continue;
                var result=StrausAnalysis.Run(model,Path.Combine(folder,"model.st7"),0,Array.Empty<string>(),1e-6,true,out var warnings);
                File.WriteAllBytes(Path.Combine(folder,"results.bgmodel"),ModelArchive.Write(result));
                File.WriteAllText(Path.Combine(folder,"warnings.json"),JsonSerializer.Serialize(warnings));
                var cases=result.GeneratedMovingCases.OrderBy(c=>c.Key).Select(c=>c.Value).ToArray();
                checks.Near(scenario.Id+" generated positions",cases.Length,scenario.Positions,0,0);
                if(cases.Length!=scenario.Positions)throw new InvalidOperationException("Wrong generated case count.");
                for(int step=0;step<cases.Length;step++)
                {
                    string name=cases[step],label=scenario.Id+" / "+name;var axles=Axles(scenario,step);
                    double left=BridgeGrillageExample.Reaction(result.Nodes.First(),name).Force.Z;
                    double right=BridgeGrillageExample.Reaction(result.Nodes.Last(),name).Force.Z;
                    checks.Near(label+" equilibrium [N]",left+right,scenario.Force*axles.Length,1e-6,1e-5);
                    checks.Near(label+" right reaction [N]",right,scenario.Force*axles.Sum()/Length,1e-6,1e-5);
                    checks.Near(label+" midspan Uz [mm]",BridgeGrillageExample.Displ(result.Nodes[6],name).Displacement.Z,Displacement(scenario,axles,Length/2),1e-5,1e-8);
                    var forces=result.Beams.SelectMany(b=>b.Results.OfType<BeamForceResultModel>().Where(f=>f.LoadCase.Name==name).Select(f=>(Beam:b,Force:f))).ToArray();
                    if(forces.Length==0)throw new InvalidOperationException("Missing moving beam results.");
                    // Compare at every returned station: the API need not sample the exact axle position.
                    foreach(var item in forces)
                        checks.Near(label+" M(x) [Nmm]",Math.Abs(item.Force.My),Math.Abs(Moment(scenario,axles,item.Beam.PointFrom.X+item.Force.Station)),1e-5,1e-3);
                }
            }
            var invalid=new MovingPointForce{Mobility=2};bool rejected=false;try{invalid.Validate();}catch(ArgumentException){rejected=true;}
            checks.Near("invalid mobility rejected",rejected?1:0,1,0,0);
            if(checks.Measurements.Any(m=>!m.Passed))throw new InvalidOperationException("Moving load benchmark failed; see report.json.");
            status=native?"native-passed":"prepared-offline-passed";
        }
        catch(Exception ex){status="failed";error=ex.ToString();throw;}
        finally
        {
            File.WriteAllText(Path.Combine(root,"report.json"),JsonSerializer.Serialize(new{Status=status,NativeExecuted=native,Error=error,Scenarios=selected,Measurements=checks.Measurements},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("MOVING REPORT "+Path.Combine(root,"report.json"));
        }
        Console.WriteLine(native?"MOVING NATIVE PASS":"MOVING PREPARED: native solver not invoked");return 0;
    }
}
