#if GPC_PILOT
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;

/// <summary>F6A diagnostic: the beam properties of a former model archive (BGHA1), read as data. Usage: --legacy-sections &lt;bgmodel&gt;</summary>
internal static class LegacySectionDump
{
    internal static int Run(string path)
    {
        var model = LegacyArchive.Read(File.ReadAllBytes(path));
        string G(double v) => v.ToString("G9", CultureInfo.InvariantCulture);
        Console.WriteLine($"MODEL {Path.GetFileName(path)} units {model.Enum("Units")}");
        var beams = model.Field("Elements").Items.Where(e => e.Is("BeamModel")).ToList();
        foreach (var g in beams.GroupBy(b => b["BeamProperty"].String("Name") ?? "?").OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var p = g.First()["BeamProperty"]; var s = p.Field("SectionModel"); var m = p.Field("Material");
            Console.WriteLine($"PROPERTY {g.Key} | beams {g.Count()} | {p.Enum("BeamPropertyType")} | section {s.Enum("SectionType")} | A {G(s.Double("Area"))} | I11 {G(s.Double("I11"))} | I22 {G(s.Double("I22"))} | J {G(s.Double("J"))}"
                + $" | SA1 {G(s.Double("ShearA1"))} | SA2 {G(s.Double("ShearA2"))} | composite preview {!s["CompositePreview"].IsNull} | material {m.String("Name")} E {G(m.Double("Modulus"))} nu {G(m.Double("PoissonRatio"))} usePoisson {m.Bool("UsePoisson")}");
        }
        return 0;
    }
}
#endif
