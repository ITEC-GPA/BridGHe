#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Helpers;
using GPC.Model.Sections.Concrete;
using Grasshopper.Kernel;

/// <summary>
/// Coordinator decision 1 (5/11), the bar layout of the composite girder sections: the homogenised properties of the analysis (one bar per
/// row with the whole area, at the pitch of the slab width: the one-bar analysis section of <see cref="GpcCompositeSections.AnalysisProperties"/>,
/// the values of the former <c>CompositeBeamPropertyModel.CreateHomogenizedProperty</c> bit for bit) against the same properties from the
/// section of the checks (<c>BridgeCompositeSectionDefinition</c>: three bars of a third of the area at a third of the width), for every
/// composite section of a solved definition (the outputs of its components), at n0, n∞ and n,sh, with and without the concrete (Bridge
/// State Override cracked / uncracked; the rebars as the section says). A, I11, I22, centroid; J (steel only) and the shear areas (steel and
/// slab, without the bars) are the same functions in both layouts. Only with <c>BRIDGHE_BAR_LAYOUT</c> = output folder: a measurement, not a gate.
/// <para>R6.6 R2a: both layouts built on the GPC definition of each composite section (<c>GH_CompositeBeamProperty.Definition</c>, the datum
/// of the FEM-backed and of the GPC-only values alike), no FEM property: the sections are told apart by the SHA-256 of their GPC archive.</para>
/// </summary>
internal static class BarLayoutMeasurement
{
    internal static void Report(GH_Document doc, string label)
    {
        string folder = Environment.GetEnvironmentVariable("BRIDGHE_BAR_LAYOUT");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var definitions = new Dictionary<string, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition>(StringComparer.Ordinal);
        foreach (var component in doc.Objects.OfType<GH_Component>())
            foreach (var output in component.Params.Output)
                foreach (var goo in output.VolatileData.AllData(true))
                {
                    if (goo == null || goo.GetType().Name != "GH_CompositeBeamProperty") continue;
                    GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition d;
                    try { d = goo.GetType().GetProperty("Definition")?.GetValue(goo) as GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition; }
                    catch (System.Reflection.TargetInvocationException) { continue; } // not a valid definition (no slab, a vertical offset): no layout to measure
                    if (d?.Section == null) continue;
                    string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(GpcDraftArchive.Write(d)));
                    definitions.TryAdd(key, d);
                }
        string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        double Rel(double a, double b) => a == b ? 0 : Math.Abs(b - a) / Math.Max(Math.Abs(a), double.Epsilon);
        var lines = new List<string> { "property\tn\tconcrete\trebars\tA_one\tA_three\trelA\tI11_one\tI11_three\trelI11\tI22_one\tI22_three\trelI22\tcy_one\tcy_three\trelcy\tJ\tShearA1\tShearA2" };
        double maxA = 0, maxI11 = 0, maxI22 = 0, maxCy = 0; string whereI22 = "";
        foreach (var d in definitions.Values.OrderBy(d => d.Name, StringComparer.Ordinal))
        {
            var ns = new[] { ("n0", d.NShortTerm), ("ninf", d.NLongTerm), ("nsh", d.NShrinkage > 0 ? d.NShrinkage : d.NLongTerm) }.Where(n => double.IsFinite(n.Item2) && n.Item2 > 0).ToArray();
            foreach (var (nName, n) in ns)
                foreach (bool concrete in new[] { true, false })
                {
                    if (!concrete && !d.ConsiderRebar) continue; // steel only: no bar
                    var one = GpcCompositeSections.AnalysisProperties(d, n, concrete, d.ConsiderRebar, true);
                    var section = d.Section;
                    double phi = ReinforcedConcreteSection.CalculateHomogenizedFactorPhi(n, section.SteelSections.First().Section.SteelMaterial, section.ConcreteMaterial);
                    var three = CompositePropertyAdapter.Calculate(section, phi, concrete, d.ConsiderRebar, true);
                    double a1 = one.areaH / n, a3 = three.areaH / n, i11a = one.J11H / n, i11b = three.J11H / n, i22a = one.J22H / n, i22b = three.J22H / n;
                    double rA = Rel(a1, a3), r11 = Rel(i11a, i11b), r22 = Rel(i22a, i22b), rc = Rel(one.centroidH.Y, three.centroidH.Y);
                    // The shear areas of the analysis property: the steel and the slab as the section considers the concrete (the former HomogenisedShearAreas(n)).
                    var shear = GpcCompositeSections.HomogenisedShearAreas(d, n, d.ConsiderConcrete);
                    lines.Add(string.Join("\t", d.Name, nName + "=" + R(n), concrete, d.ConsiderRebar, R(a1), R(a3), R(rA), R(i11a), R(i11b), R(r11), R(i22a), R(i22b), R(r22),
                        R(one.centroidH.Y), R(three.centroidH.Y), R(rc), R(GpcCompositeSections.SteelTorsionConstant(d)), R(shear.Area1), R(shear.Area2)));
                    maxA = Math.Max(maxA, rA); maxI11 = Math.Max(maxI11, r11); maxCy = Math.Max(maxCy, rc);
                    if (r22 > maxI22) { maxI22 = r22; whereI22 = $"{d.Name} {nName}={R(n)} concrete={concrete}"; }
                }
        }
        string file = Path.Combine(folder, label + "-bar-layout.tsv");
        File.WriteAllLines(file, lines);
        Console.WriteLine($"BAR LAYOUT ({label}): {definitions.Count} composite properties, {lines.Count - 1} homogenised variants; maximum relative change one bar -> three bars: " +
            $"A {maxA:G3}, I11 {maxI11:G3}, I22 {maxI22:G3} ({whereI22}), centroid {maxCy:G3}; J and shear areas the same functions (0); table {file}");
    }
}
#endif
