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

    /// <summary>--legacy-beam-results &lt;bgmodel&gt; &lt;beam id&gt; [case]: the beam force results of a former model archive, read as data
    /// (R6.6: without the FeMM classes; the size limit of the former reader lifted, as for the merged models of the native captures, 448 MB
    /// for example 50): the beams of that BridGHe ID in the order of the elements, their length (the distance of the two points, as RhinoCommon
    /// computes it), their force results of the case (all when none is given) by station.</summary>
    internal static int BeamResults(string path, string beamId, string caseName)
    {
        var model = LegacyArchive.Read(File.ReadAllBytes(path), int.MaxValue);
        string G(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        // Rhino points and vectors: their components X, Y (and Z), or the fields m_x, m_y (m_z) of a [Serializable] struct.
        double C(LegacyValue v, string axis) => v.Any(axis, "m_" + axis.ToLowerInvariant()).AsDouble();
        Rhino.Geometry.Point3d P(LegacyValue v) => new Rhino.Geometry.Point3d(C(v, "X"), C(v, "Y"), C(v, "Z"));
        foreach (var b in model.Field("Elements").Items.Where(e => e.Is("BeamModel") && e.String("BeamId") == beamId))
        {
            Console.WriteLine($"BEAM {beamId} length {G(P(b.Field("PointFrom")).DistanceTo(P(b.Field("PointTo"))))}");
            var results = b["Results"].IsNull ? Enumerable.Empty<LegacyValue>() : b["Results"].Items;
            foreach (var r in results.Where(r => r.Is("BeamForceResultModel") && (caseName == null || r["Case"].String("Name") == caseName)).OrderBy(r => r.Double("Station")))
            {
                var shear = r.Field("ShearForce"); var moment = r.Field("BendingMoment");
                Console.WriteLine($"  {r["Case"].String("Name")} station {G(r.Double("Station"))} N {G(r.Double("AxialForce"))} SF {G(C(shear, "X"))} {G(C(shear, "Y"))} BM {G(C(moment, "X"))} {G(C(moment, "Y"))}");
            }
        }
        return 0;
    }
}
