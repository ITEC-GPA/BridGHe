using System;
using System.Diagnostics;
using System.IO;
using BridGHe.Common.Checks;
using GPC.Model.Results;

internal static class BridgePerformanceTests
{
    internal static void Run()
    {
        var checker=BridgeSuiteExample.Checker(BridgeSuiteExample.SectionParameters(BridgeSuiteExample.Defaults[0]));
        var reference=BridgeSuiteExample.Reference;
        var a=new ResultBeamForces(0,0,300000,0,1e9,0,reference);
        checker.GetLinearAnalysisResultByStages(out var _,out var _,out var result,out _,2,0,a,a,a,true);
        _=result.Point6_SteelTau;_=result.Point7_SteelTau;
        const int iterations=2000;double sum=0;long start=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
        for(int i=0;i<iterations;i++)sum+=result.Point6_SteelTau+result.Point7_SteelTau+result.Point6_SteelSigmaId+result.Point7_SteelSigmaId;
        timer.Stop();long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
        var data=new{iterations,values=4*iterations,ms=timer.Elapsed.TotalMilliseconds,allocatedBytes=allocated,checksum=sum};
        File.WriteAllText("artifacts/shear-performance.json",System.Text.Json.JsonSerializer.Serialize(data));
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(data));
        BridgeNumericalBaseline.Verify();
    }
}
