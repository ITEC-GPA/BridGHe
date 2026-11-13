using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel.Types;

/// <summary>
/// P2CG (13/11/2026, review of ChatGPT task 63, 2B-01): the text of the Node, Beam and Plate Results data on their GPC data. Since phase 2b a valid
/// datum (its analysed GPC element, as Decompose Model links it) said «Null … results»; it now names its element. Run by <c>p2ce-reader</c>.
/// For each of the three: empty (not valid, the null text), analysed (valid, the text of its element), copied (the same element and text).
/// </summary>
internal static class P2cgChecks
{
    internal static int ResultsTexts(List<string> failures)
    {
        int n = 0;
        void Check(bool ok, string what) { n++; if (!ok) { Console.WriteLine("P2CG RESULTS TEXT check " + n + " FAILED: " + what); failures.Add("P2CG 2B-01: " + what); } }
        var origin = new GPC.Geometry.Point3d(0, 0, 0);
        var elements = new (string Kind, GPC.Model.Elements.Element Element)[]
        {
            ("Node", new GPC.Model.Elements.NodeElement(origin, null, "", 7)),
            ("Beam", new GPC.Model.Elements.BeamElement(origin, new GPC.Geometry.Point3d(1000, 0, 0), null, null, "", 8)),
            ("Plate", new GPC.Model.Elements.AreaElement(null, null, null, "", 9)),
        };
        foreach (var (kind, element) in elements)
        {
            var type = R2aDesignFixtures.Plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_" + kind + "Results", true);
            var empty = (IGH_Goo)Activator.CreateInstance(type);
            Check(!empty.IsValid && empty.ToString() == "Null " + kind + " results", kind + " Results, empty: " + empty.IsValid + " «" + empty + "»");
            var analysed = (IGH_Goo)Activator.CreateInstance(type);
            type.GetProperty("AnalysedGpc").SetValue(analysed, element);
            string expected = kind + " results of element " + element.Id;
            Check(analysed.IsValid && analysed.ToString() == expected && !analysed.ToString().StartsWith("Null", StringComparison.Ordinal),
                kind + " Results, analysed: " + analysed.IsValid + " «" + analysed + "» (expected «" + expected + "»)");
            var copy = analysed.Duplicate();
            Check(copy.IsValid && copy.ToString() == expected && ReferenceEquals(type.GetProperty("AnalysedGpc").GetValue(copy), element),
                kind + " Results, copied: " + copy.IsValid + " «" + copy + "»");
        }
        return n;
    }
}
