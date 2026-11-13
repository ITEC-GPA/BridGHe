using System;
using System.Globalization;
using System.IO;
using System.Linq;
using GPC.Converter.Straus7;

/// <summary>Diagnostic: distributed loads and result stations of one beam (--st7-beam model.st7 results.lsa beam case).</summary>
internal static class St7BeamProbe
{
    internal static int Run(string[] args)
    {
        var a = args.SkipWhile(x => x != "--st7-beam").Skip(1).ToArray();
        int beam = int.Parse(a[2], CultureInfo.InvariantCulture), c = int.Parse(a[3], CultureInfo.InvariantCulture);
        using var api = new Straus7NativeApi(); api.OpenModelReadOnly(Path.GetFullPath(a[0]), Path.GetTempPath());
        try
        {
            foreach (var (attribute, frame) in new[] { (Straus7Attributes.BeamDLL, Straus7LoadFrame.Principal), (Straus7Attributes.BeamDLG, Straus7LoadFrame.Global) })
                foreach (var at in api.ReadAttributes(Straus7Entity.Beam, beam, attribute))
                {
                    var d = api.ReadBeamDistributedLoad(beam, frame, at.Axis, at.Case, at.Id);
                    Console.WriteLine($"{frame} case {at.Case} axis {at.Axis} id {at.Id} type {d.Type} project {d.Project}: {string.Join(" ", d.Values.Select(v => v.ToString("G6", CultureInfo.InvariantCulture)))}");
                }
            foreach (var at in api.ReadAttributes(Straus7Entity.Beam, beam, Straus7Attributes.BeamOffset)) Console.WriteLine("offset " + string.Join(" ", api.ReadBeamOffset(beam)));
            api.OpenResults(Path.GetFullPath(a[1]));
            var t = api.ReadBeamForces(beam, c, 5);
            Console.WriteLine("stations " + string.Join(" ", t.Positions.Take(t.Rows).Select(v => v.ToString("G6", CultureInfo.InvariantCulture))));
        }
        finally { api.CloseModel(); }
        return 0;
    }
}
