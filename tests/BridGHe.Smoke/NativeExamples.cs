using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Helpers;
using BridGHe.Common.MovingLoads;
using BridGHe.Common.Straus;
using Rhino.Geometry;

/// <summary>Numerical integration benchmarks, N-mm, linear elastic material, no gravity.
/// <para>R6.6 lane R0: <b>the FEM catalogue (<see cref="Create"/>, <see cref="Example"/> with its FEM model and checks) is transitional</b>: it is
/// kept for the callers outside R0 until lanes R1–R4 move them to the GPC catalogue (<see cref="GpcNativeExamples"/>, the same examples built
/// directly as GPC models), and P2 removes it. <c>--examples</c> runs the GPC catalogue on the GPC-only path (GPC_PILOT); <see cref="Recorder"/>
/// is shared by both.</para></summary>
internal static class NativeExamples
{
    internal sealed record Measurement(string Name,double Actual,double Expected,double AbsoluteTolerance,bool Passed);
    internal sealed class Recorder
    {
        public List<Measurement> Measurements { get; }=new();
        public void Near(string name,double actual,double expected,double relative=1e-5,double absolute=1e-8)
        {
            double tolerance=Math.Max(absolute,Math.Abs(expected)*relative);
            bool pass=double.IsFinite(actual)&&Math.Abs(actual-expected)<=tolerance;
            Measurements.Add(new(name,actual,expected,tolerance,pass));
            Console.WriteLine($"{(pass?"PASS":"FAIL")}: {name}: {actual:G12}, expected {expected:G12}, tolerance {tolerance:G3}");
        }
    }
    /// <summary>F6A: a moving template from an installed Straus7 library is expanded by the Converter (N-mm) when the former model is
    /// converted, as the former exporter assigned it; the export carries the expanded template and the moving analysis runs.</summary>
    internal static void CheckMovingLibrary()
    {
        string library,item;
        using(var api=new GPC.Converter.Straus7.Straus7NativeApi()){var entry=api.MovingLibraryCatalog().First(p=>p.Value.Length!=0);library=entry.Key;item=entry.Value[0];}
        const double length=6000;
        {
            // R6.6 R0: on GPC, without a FEM model: Build Model of the beam of example 04 and Attach Moving Paths with the library item
            // (GpcModelTransforms.ReplacePaths, the rule of the former exporter); the template expanded by the Converter (N-mm), and Export of
            // the GPC-only input writes it.
            var b=GpcNativeExamples.Beam("library",12,length,true);
            b.Paths.Add(GpcNativeExamples.Path04(length,new MovingTemplate{Name="Library",LibraryName=library,LibraryItem=item}));
            var input=b.Build("library").Input;
            var resolved=GPC.Converter.Straus7.Straus7MovingLibrary.Resolve(library,item,Path.GetTempPath());
            var expanded=input.Model.MovingLoads.Paths.Single().Template;
            if(expanded.Vehicles.Length==0||expanded.Vehicles.Length!=resolved.Template.Vehicles.Length||expanded.LaneFactors.Length!=resolved.Template.LaneFactors.Length)
                throw new Exception("FAIL: library moving template not expanded on GPC (Attach Moving Paths): "+library+"/"+item);
            Console.WriteLine($"PASS: library moving template {library}/{item} expanded by the Converter on GPC (Attach Moving Paths): {expanded.Vehicles.Length} vehicles, {expanded.LaneFactors.Length} lanes");
            string gpcFolder=Path.GetFullPath(Path.Combine("artifacts","native",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(gpcFolder);
            BridGHe.Common.Gpc.Legacy.GpcAnalysisSeam.Export(input,Path.Combine(gpcFolder,"library.st7"));
            if(!File.Exists(Path.Combine(gpcFolder,"library.st7")))throw new Exception("FAIL: library moving template export of the GPC-only input");
            Console.WriteLine("PASS: library moving template exported by the Converter writer");
        }
        // P2CUT (13/11): the transitional half (the reader of a payload written from a FEM model with the library item) is retired by name (P2rRetired.Checks).
    }

    /// <summary><c>--examples</c>. R6.6 R0: the GPC catalogue on the GPC-only path (<see cref="GpcNativeExamples.Run"/>: the analytic checks on the
    /// analysed GPC model through the GPC result readers, the Results components on the GPC-only model). P2CB (13/11): the run of the FEM catalogue
    /// on the FEM path (RunFem, without callers since T7) is removed; the FEM catalogue (<see cref="Create"/>) stays for the files that still read it.</summary>
    internal static int Run(string selected)
    {
        return GpcNativeExamples.Run(selected);
    }

}
