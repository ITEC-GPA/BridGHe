using System;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.MovingLoads;

internal static class BridgeMovingWorkflowTests
{
    internal static void Run()
    {
        int count=0;
        void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        void Reject(Action action){try{action();}catch(ArgumentException){count++;return;}throw new Exception("Invalid moving workflow accepted.");}
        var table=new[]{new[]{"Combination","G","R"},new[]{"BASE","1.35","0.8"},new[]{"R_ONLY","0","1"}};
        var cases=new[]{"native position 20","native position 3","native position 11"};
        var result=BridgeMovingWorkflow.Combinations(table,cases,"BASE",1.5,true,"Q");
        Check(result.Rows.Length==9&&result.ActionIds.Length==3,"Combination dimensions.");
        Check(result.Cases.SequenceEqual(cases)&&result.ActionIds.SequenceEqual(new[]{"Q_0001","Q_0002","Q_0003"}),"Native order preserved without lexicographic resorting.");
        Check(result.Rows[1].Take(3).SequenceEqual(table[1])&&result.Rows[2].Take(3).SequenceEqual(table[2]),"Original coefficients unchanged.");
        foreach(var row in result.Rows.Skip(3))Check(row.Skip(3).Count(v=>v!="0")==1&&row.Skip(3).Contains("1.5"),"Positions must remain mutually exclusive.");
        Check(result.Rows[3][1]=="1.35"&&result.Rows[3][2]=="0.8"&&result.Rows[4][1]=="0","Base and isolated increments.");
        Reject(()=>BridgeMovingWorkflow.Combinations(table,new[]{"x","x"},"BASE",1,false,"Q"));
        Reject(()=>BridgeMovingWorkflow.Combinations(table,cases,"UNKNOWN",1,false,"Q"));
        Reject(()=>BridgeMovingWorkflow.Combinations(table,cases,"BASE",double.NaN,false,"Q"));
        Reject(()=>BridgeMovingWorkflow.Combinations(new[]{new[]{"","Q_0001"},new[]{"BASE","1"}},cases,"BASE",1,false,"Q"));
        var template=new MovingTemplate{Vehicles=new[]{new MovingVehicle{Loads=new MovingObject[]{new MovingPointForce{Y=-1000,Axis=1,Force=new[]{0d,0d,-150000}},new MovingPointForce{Y=1000,Axis=1,Force=new[]{0d,0d,-150000}}}}}};
        var input=SimpleBridgeTests.Input() with{DeckBeamLevel=-200};
        var geometry=BridgeDeckGrillage.Build(input,SimpleBridgeBuilder.Build(input)).Geometry;
        var path=BridgeMovingWorkflow.StraightPath(geometry,template,"TRAFFIC",1500,50);
        Check(path.Coordinates.SequenceEqual(new[]{0d,1500d,-200d,100000d,1500d,-200d,0d,2500d,-200d}),"Actual slab level and lateral offset.");
        Check(path.Target==2&&path.ConvertToStaticCases&&!ReferenceEquals(path.Template,template),"Native target, conversion and immutable input.");
        Reject(()=>BridgeMovingWorkflow.StraightPath(geometry,template,"TRAFFIC",5500,50));
        Reject(()=>BridgeMovingWorkflow.StraightPath(geometry,template,"TRAFFIC",0,0));
        var curve=SimpleBridgeTests.Input(curved:true);var curved=BridgeDeckGrillage.Build(curve,SimpleBridgeBuilder.Build(curve)).Geometry;
        Reject(()=>BridgeMovingWorkflow.StraightPath(curved,template,"TRAFFIC",0,50));
        var slope=input with{CrossfallPercent=new[]{2d}};var tilted=BridgeDeckGrillage.Build(slope,SimpleBridgeBuilder.Build(slope)).Geometry;
        var tiltedPath=BridgeMovingWorkflow.StraightPath(tilted,template,"TRAFFIC",1500,50);double angle=Math.Atan(.02);
        Check(Math.Abs(tiltedPath.Coordinates[2]-(1500*Math.Sin(angle)-200*Math.Cos(angle)))<1e-8,"Constant crossfall follows slab axes.");
        Console.WriteLine($"MOVING WORKFLOW PASS: {count} checks, alternative combinations, independent load-path geometry, invalid inputs.");
    }
}
