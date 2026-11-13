using System;
using System.Globalization;
using System.Collections.Generic;
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
    internal sealed record Scenario(string Id, int Divisions, double Force, double[] Offsets, bool Reverse = false);
    private static readonly Scenario[] Scenarios = {
        new("01-single-13",13,1000,new[]{0.0}),
        new("02-single-25",25,1000,new[]{0.0}),
        new("03-reverse",13,1000,new[]{0.0},true),
        new("04-tandem",13,1000,new[]{-750.0,750}),
        new("05-double-force",13,2000,new[]{0.0})
    };
    private static double[] Axles(Scenario s,int step)
    {
        // The leading axle starts at the first patch centre. Continue until the last
        // axle leaves the path; entry/exit cases contain only the axles on the bridge.
        // This reference is computed from input geometry, never inferred from reactions.
        double front=(step+.5)*Length/s.Divisions, leading=s.Offsets.Max();
        return s.Offsets.Select(a=>front+a-leading).Where(x=>x>=0 && x<=Length)
            .Select(x=>s.Reverse?Length-x:x).ToArray();
    }
    private static int Steps(Scenario s) => (int)Math.Ceiling(
        (Length+s.Offsets.Max()-s.Offsets.Min())/(Length/s.Divisions)-.5);
    private static double Moment(Scenario s,double[] axles,double x)
        => s.Force*axles.Sum(a=>1-a/Length)*x-s.Force*axles.Sum(a=>Math.Max(0,x-a));
    // Euler-Bernoulli Green function for a downward point force; negative global Uz.
    private static double Displacement(Scenario s,double[] axles,double x)
        => -s.Force*axles.Sum(a=>x<=a ? (Length-a)*x*(Length*Length-(Length-a)*(Length-a)-x*x)
            : a*(Length-x)*(Length*Length-a*a-(Length-x)*(Length-x)))/(6*Length*E*I);
    // TRANSITIONAL (R6.6 lane R2b): --centrifugal-probe is a native report (no pass/fail) on the FEM example 04; it moves to GPC with a native stage.
    /// <summary>
    /// --centrifugal-probe (native, L10 decision 6 of 4/11): what Straus7 R31 does with a nonzero centrifugal offset together with lane
    /// factors and adjacency factors. One point force Fz = -1000 N with centrifugal, K0 = 100 N, K1 = 0 (Fc = K0 + K1 Fz per force), on the
    /// simply supported benchmark beam; per configuration the sums of the support reactions (X, Y, Z) of the first, middle and last
    /// generated positions, and the GPC offset per vehicle written by the Converter. The force is in path axes (Axis 0): R31 applies no
    /// centrifugal force to a point force in global axes (the Converter refuses it). A report, not a pass/fail check.
    /// </summary>
    internal static int CentrifugalProbe(string only = null)
    {
        string root=Path.Combine(ExamplePaths.Calculations("21-benchmark-carichi-mobili"),"centrifugal-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(root);
        // Straight path: (0,0,0) -> (Length,0,0), plane point (0,1000,0). Circular path (Shape 1, constant radius): the same chord with
        // the third point (Length/2, 10, 0), a sagitta of 10 mm (radius about 450 m if the point is on the arc).
        var configurations=new (string Id,double[] Lanes,int[] VehicleLanes,bool Adjacent,int MultiLane,int Shape)[]
        {
            ("1-lane-1.0",new[]{1.0},new[]{1},false,0,0),
            ("1-lane-0.8",new[]{0.8},new[]{1},false,0,0),
            ("2-lanes-lane1",new[]{1.0,0.5},new[]{1},false,0,0),
            ("2-lanes-lane2",new[]{1.0,0.5},new[]{2},false,0,0),
            ("2-lanes-lane1-adjacent",new[]{1.0,0.5},new[]{1},true,0,0),
            ("2-lanes-lane2-adjacent",new[]{1.0,0.5},new[]{2},true,0,0),
            ("2-lanes-lane2-adjacent-rule1",new[]{1.0,0.5},new[]{2},true,1,0),
            ("arc-1-lane-1.0",new[]{1.0},new[]{1},false,0,1),
            ("arc-2-lanes-lane2-0.5",new[]{1.0,0.5},new[]{2},false,0,1),
        };
        if(only!=null)configurations=configurations.Where(c=>c.Id.StartsWith(only,StringComparison.Ordinal)).ToArray();
        var report=new List<object>();
        foreach(var c in configurations)
        {
            var model=ModelArchive.Clone(NativeExamples.Create().Single(e=>e.Id=="04-moving-load").Model);
            model.Name=c.Id;var path=model.MovingPaths.Single();path.LoadCaseName=c.Id;path.Divisions=6;
            path.Shape=c.Shape;path.Coordinates=c.Shape==0?new[]{0,0,0,Length,0,0,0,1000,0}:new[]{0,0,0,Length,0,0,Length/2,10,0};
            path.Template.LaneFactors=c.Lanes;path.Template.MultiLaneType=c.MultiLane;
            path.Template.Centrifugal=new MovingCentrifugal{K0="100",K1="0",LengthUnit=2,ForceUnit=0};
            path.Template.Vehicles[0].Lanes=c.VehicleLanes;
            path.Template.Vehicles[0].Loads=new MovingObject[]{new MovingPointForce{X=0,Axis=0,Centrifugal=true,Adjacent=c.Adjacent,Force=new[]{0.0,0,-1000}}};
            path.Validate();
            string folder=Path.Combine(root,c.Id);Directory.CreateDirectory(folder);
            try
            {
                var result=StrausAnalysis.Run(model,Path.Combine(folder,"model.st7"),0,Array.Empty<string>(),1e-6,true,out var warnings,out var analysed);
                var law=analysed?.MovingLoads?.Paths?.FirstOrDefault()?.Template?.Centrifugal;
                var cases=result.GeneratedMovingCases.OrderBy(k=>k.Key).Select(k=>k.Value).ToArray();
                foreach(var name in new[]{cases.First(),cases[cases.Length/2],cases.Last()}.Distinct())
                {
                    double Sum(Func<NodeReactResultModel,double> f)=>result.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Where(r=>r.LoadCase.Name==name).Sum(f);
                    var row=new{Configuration=c.Id,c.Lanes,c.VehicleLanes,c.Adjacent,MultiLaneType=c.MultiLane,Case=name,
                        Rx=Sum(r=>r.Force.X),Ry=Sum(r=>r.Force.Y),Rz=Sum(r=>r.Force.Z),GpcOffsetN=law?.OffsetForce,GpcFactorKind=law?.FactorKind.ToString(),Warnings=warnings};
                    report.Add(row);
                    Console.WriteLine(FormattableString.Invariant($"CENTRIFUGAL {c.Id} {name}: Rx {row.Rx:G6} Ry {row.Ry:G6} Rz {row.Rz:G6} N; GPC offset {row.GpcOffsetN:G6} N/vehicle ({row.GpcFactorKind})"));
                }
            }
            catch(Exception ex){report.Add(new{Configuration=c.Id,Error=ex.Message});Console.WriteLine("CENTRIFUGAL "+c.Id+": "+ex.GetType().Name+": "+ex.Message);}
        }
        File.WriteAllText(Path.Combine(root,"report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("CENTRIFUGAL PROBE DONE "+Path.Combine(root,"report.json"));return 0;
    }

    internal static int Run(bool native,string selection="all")
        => RunGpc(native,selection);

    /// <summary>R6.6 R2b: the moving-load benchmarks on GPC data alone: the GPC input of example 04 (<see cref="GpcNativeExamples"/>: the simply
    /// supported beam of 6000 mm in 12 beams) with the path of each scenario (Attach Moving Paths on GPC, <c>GpcModelTransforms.ReplacePaths</c>);
    /// the BGHGPC1 archive of the input keeps the divisions and the axles; with <paramref name="native"/> the GPC-only analysis
    /// (<c>StrausAnalysis.Run(Conversion, …)</c>) and the same analytic references on the rows of the GPC readers: generated positions, equilibrium,
    /// right reaction, midspan displacement and |M(x)| at every returned station. No FEM model.</summary>
    private static int RunGpc(bool native,string selection)
    {
        string root=Path.Combine(ExamplePaths.Calculations("21-benchmark-carichi-mobili"),DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(root);
        var selected=Scenarios.Where(s=>selection=="all"||s.Id.StartsWith(selection,StringComparison.Ordinal)).ToArray();
        if(selected.Length==0)throw new ArgumentException("Unknown moving-load scenario: "+selection);
        var checks=new NativeExamples.Recorder();
        string status="running";string error=null;
        try
        {
            var example=GpcNativeExamples.Create().Single(e=>e.Id=="04-moving-load");
            foreach(var scenario in selected)
            {
                string folder=Path.Combine(root,scenario.Id);Directory.CreateDirectory(folder);
                var path=GpcNativeExamples.Path04(Length);
                path.LoadCaseName=scenario.Id;path.Divisions=scenario.Divisions;
                double start=scenario.Reverse?Length:0,end=scenario.Reverse?0:Length;
                path.Coordinates=new[]{start,0,0,end,0,0,start,1000,0};
                // Gravity remains global -Z when the path reverses: its local normal reverses too (Straus R3 lpAxisGlobal = 1).
                path.Template.Vehicles[0].Loads=scenario.Offsets.Select(a=>(MovingObject)new MovingPointForce{X=a,Axis=1,Force=new[]{0.0,0,-scenario.Force}}).ToArray();
                path.Validate();
                var input=BridGHe.Common.Gpc.GpcModelTransforms.ReplacePaths(example.Input,new[]{path},BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace,"r66-r2b-moving:"+scenario.Id));
                var (data,records)=BridGHe.Common.Gpc.GpcDraftArchive.Write(input);
                File.WriteAllBytes(Path.Combine(folder,"input.gpc"),data);File.WriteAllBytes(Path.Combine(folder,"input.records"),records);
                var roundtrip=BridGHe.Common.Gpc.GpcDraftArchive.ReadConversion(File.ReadAllBytes(Path.Combine(folder,"input.gpc")),File.ReadAllBytes(Path.Combine(folder,"input.records")));
                checks.Near(scenario.Id+" archive divisions",roundtrip.Model.MovingLoads.Paths.Single().Divisions,scenario.Divisions,0,0);
                checks.Near(scenario.Id+" archive axles",roundtrip.Model.MovingLoads.Paths.Single().Template.Vehicles[0].PointForces.Count(),scenario.Offsets.Length,0,0);
                var csv=new StringBuilder("step,axles_mm,Rleft_N,Rright_N,Mmax_Nmm,Uz_mid_mm\n");
                for(int step=0;step<Steps(scenario);step++)
                {
                    var axles=Axles(scenario,step);double right=scenario.Force*axles.Sum()/Length;
                    csv.AppendLine(FormattableString.Invariant($"{step+1},{string.Join(";",axles.Select(a=>a.ToString("G17",CultureInfo.InvariantCulture)))},{scenario.Force*axles.Length-right:G17},{right:G17},{axles.Max(a=>Moment(scenario,axles,a)):G17},{Displacement(scenario,axles,Length/2):G17}"));
                }
                File.WriteAllText(Path.Combine(folder,"analytical.csv"),csv.ToString());
                if(!native)continue;
                var analysed=StrausAnalysis.Run(input,Path.Combine(folder,"model.st7"),0,Array.Empty<string>(),true,out var warnings);
                BridGHe.Common.Gpc.GpcResultsFile.Write(Path.Combine(folder,"results.bghr"),analysed);
                File.WriteAllText(Path.Combine(folder,"warnings.json"),JsonSerializer.Serialize(warnings));
                var results=new GpcNativeExamples.Results(analysed);
                var cases=results.GeneratedMovingCases.OrderBy(c=>c.Number).Select(c=>c.Name).ToArray();
                checks.Near(scenario.Id+" generated positions",cases.Length,Steps(scenario),0,0);
                if(cases.Length!=Steps(scenario))throw new InvalidOperationException("Wrong generated case count.");
                var catalog=results.Catalog;
                var nodes=analysed.NodesElements.Values.OrderBy(n=>n.Id).ToArray();
                double[] Row(GPC.Model.Elements.NodeElement n,string name,bool reaction){var r=BridGHe.Common.Gpc.GpcResultRows.Node(catalog,analysed,n).Single(x=>catalog.Entries[x.Entry].Label==name&&(reaction?x.Reaction:x.Displacement)!=null);return reaction?r.Reaction:r.Displacement;}
                for(int step=0;step<cases.Length;step++)
                {
                    string name=cases[step],label=scenario.Id+" / "+name;var axles=Axles(scenario,step);
                    double left=Row(nodes.First(),name,true)[2],right=Row(nodes.Last(),name,true)[2];
                    checks.Near(label+" equilibrium [N]",left+right,scenario.Force*axles.Length,1e-6,1e-5);
                    checks.Near(label+" right reaction [N]",right,scenario.Force*axles.Sum()/Length,1e-6,1e-5);
                    checks.Near(label+" midspan Uz [mm]",Row(nodes[6],name,false)[2],Displacement(scenario,axles,Length/2),1e-5,1e-8);
                    var forces=analysed.BeamElements.Values.SelectMany(b=>BridGHe.Common.Gpc.GpcResultRows.Beam(catalog,analysed,b).Where(f=>catalog.Entries[f.Entry].Label==name).Select(f=>(Beam:b,Force:f))).ToArray();
                    if(forces.Length==0)throw new InvalidOperationException("Missing moving beam results.");
                    // Compare at every returned station: the API need not sample the exact axle position.
                    foreach(var item in forces)
                        checks.Near(label+" M(x) [Nmm]",Math.Abs(item.Force.Bending2),Math.Abs(Moment(scenario,axles,item.Beam.StartPoint.X+item.Force.Station)),1e-5,1e-3);
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
        Console.WriteLine(native?"MOVING NATIVE PASS (GPC data alone)":"MOVING PREPARED: native solver not invoked (GPC data alone)");return 0;
    }
}
