using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.MovingLoads;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// P2CF (13/11/2026): the checks that answer the findings of ChatGPT task 44 on the retirements by name (chatgpt-compiti/revisione-p2-ritiri.md).
/// <list type="bullet">
/// <item><see cref="NativeSections"/> (RT-01, <c>--c3 --native</c> and core-native): an oracle of its own for each of the 21 native Straus7 recipes,
/// the area and the extents of the section computed from the dimensions of the recipe by their own formula (the shapes of the Straus7 API manual,
/// pp. 1003-1009, and of the reference manual), against the section Straus7 computes for the recipe, its display mesh and the property the
/// analysis takes; the key and the dimensions through the recipe and its GH archive.</item>
/// <item><see cref="MovingPaths"/> (RT-03, gpc-model-transforms): Attach Moving Paths on GPC leaves its source as it was (its canonical text before
/// and after the call) and replaces the old path.</item>
/// <item><see cref="ArchivedAttributeUpdates"/> (RT-03, gpc-c3): a beam draft written to a GH archive and read back, its preview made (the cache
/// of the plug-in filled), a taper given after the round trip: the new draft draws the new solid, the cached ones do not move.</item>
/// <item><see cref="RegressionKeys"/> (item 4): the keys <c>regression|</c> and <c>regression-part|</c> of <see cref="P2rRetired.Checks"/> against
/// the regressions that run and those of the base of P2CUT.</item>
/// </list>
/// </summary>
internal static class P2cfChecks
{
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static bool Near(double a, double b, double relative) => Math.Abs(a - b) <= relative * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // RT-01: the 21 native recipes

    /// <summary>The dimensions of the oracle (mm): the thicknesses differ from one another, so that a dimension read in the slot of another
    /// changes the area; radii and angles 0 (the BGL shapes without fillets and slopes). The bulb flat needs a toe radius and a slope (with 0 it is
    /// degenerate): the dimensions of <c>CheckNativeSections</c>.</summary>
    private static double Dimension(string name) => name switch
    {
        "D" => 400, "B" or "B1" => 200, "B2" => 100, "L" => 20, "T" or "T1" or "tw" or "t" => 10, "T2" or "tf" or "tf1" => 12, "T3" => 8, "tf2" => 14,
        _ when name.StartsWith("R", StringComparison.Ordinal) || name.StartsWith("alpha", StringComparison.Ordinal) => 0,
        _ => throw new ArgumentException("P2CF: no dimension of the oracle for " + name),
    };

    /// <summary>The section of a recipe by its own formula: area, width (x) and depth (y), and whether its outline is curved (the display mesh and
    /// the section of Straus7 are then polygons of the curve). Shapes (Straus7 API manual pp. 1003-1009, reference manual «Beam Standard Section
    /// Library»): circle and tube D (T); rectangle B × D; hollow rectangle T1 top and bottom, T2 sides; lipped channel and top hat: web T2, flanges
    /// B × T1, lips L × T3 (inside the channel, outside the top hat: its depth D + 2L); I: bottom flange B1 × T1, top B2 × T2, web T3; T: flange
    /// B × T1, web T2, lips L × T3 under the flange; angle: leg D × T2, leg B × T1; Z: flanges B1 × T1 (bottom) and B2 × T2 (top), each with the
    /// web T3, so the width B1 + B2 − T3; trapezoid B1 (bottom) and B2 (top); hollow trapezoid and triangle: the horizontal walls T1, the sloped
    /// walls T2 normal to them; triangle B (top) × D; cruciform B × T1 and D × T2. BGL with radii and slopes 0: rectangle tw sides, tf top and
    /// bottom; I B1 × tf1 (bottom), B2 × tf2 (top), web tw; channel the same with the flanges from the back of the web; T B × tf, web tw; angle
    /// leg D × tw, leg B × tf. The bulb flat has no closed formula (the bulb is drawn by its toe radius and slope): its extents only (NaN area).</summary>
    internal static (double Area, double Width, double Depth, bool Curved) Expected(string key, Func<string, double> v)
    {
        double D = v("D");
        switch (key)
        {
            case "CircularSolid": return (Math.PI * D * D / 4, D, D, true);
            case "CircularHollow": { double d = D - 2 * v("T"); return (Math.PI * (D * D - d * d) / 4, D, D, true); }
            case "RectangleSolid": return (v("B") * D, v("B"), D, false);
            case "RectangleHollow": return (v("B") * D - (v("B") - 2 * v("T2")) * (D - 2 * v("T1")), v("B"), D, false);
            case "LipChannel": return (D * v("T2") + 2 * (v("B") - v("T2")) * v("T1") + 2 * v("L") * v("T3"), v("B"), D, false);
            case "TopHat": return (D * v("T2") + 2 * (v("B") - v("T2")) * v("T1") + 2 * v("L") * v("T3"), v("B"), D + 2 * v("L"), false);
            case "I": return (v("B1") * v("T1") + v("B2") * v("T2") + (D - v("T1") - v("T2")) * v("T3"), Math.Max(v("B1"), v("B2")), D, false);
            case "T": return (v("B") * v("T1") + (D - v("T1")) * v("T2") + 2 * v("L") * v("T3"), v("B"), D, false);
            case "Angle": return (D * v("T2") + (v("B") - v("T2")) * v("T1"), v("B"), D, false);
            case "Z": return (v("B1") * v("T1") + v("B2") * v("T2") + (D - v("T1") - v("T2")) * v("T3"), v("B1") + v("B2") - v("T3"), D, false);
            case "TrapezoidSolid": return ((v("B1") + v("B2")) / 2 * D, Math.Max(v("B1"), v("B2")), D, false);
            case "TrapezoidHollow":
            {
                // The inner trapezoid: between the walls T1, its sloped sides moved in by T2 normal to them (horizontally by T2·s).
                double s = Math.Sqrt(1 + Math.Pow((v("B2") - v("B1")) / (2 * D), 2));
                return ((v("B1") + v("B2")) / 2 * D - (D - 2 * v("T1")) * ((v("B1") + v("B2")) / 2 - 2 * v("T2") * s), Math.Max(v("B1"), v("B2")), D, false);
            }
            case "TriangleSolid": return (v("B") * D / 2, v("B"), D, false);
            case "TriangleHollow":
            {
                // Apex at the bottom, half-width k·y; the inner triangle: its sides moved in by T2 normal to them, its top T1 under the top.
                double k = v("B") / (2 * D), s = Math.Sqrt(1 + k * k), h = D - v("T1") - v("T2") * s / k;
                return (k * (D * D - h * h), v("B"), D, false);
            }
            case "Cruciform": return (v("B") * v("T1") + D * v("T2") - v("T1") * v("T2"), v("B"), D, false);
            case "BglRectangle": return (v("B") * D - (v("B") - 2 * v("tw")) * (D - 2 * v("tf")), v("B"), D, false);
            case "BglI": return (v("B1") * v("tf1") + v("B2") * v("tf2") + (D - v("tf1") - v("tf2")) * v("tw"), Math.Max(v("B1"), v("B2")), D, false);
            case "BglChannel": return (D * v("tw") + (v("B1") - v("tw")) * v("tf1") + (v("B2") - v("tw")) * v("tf2"), Math.Max(v("B1"), v("B2")), D, false);
            case "BglT": return (v("B") * v("tf") + (D - v("tf")) * v("tw"), v("B"), D, false);
            case "BglAngle": return (D * v("tw") + (v("B") - v("tw")) * v("tf"), v("B"), D, false);
            case "BglBulb": return (double.NaN, v("B"), D, true);
            default: throw new ArgumentException("P2CF: no formula for the native section " + key + " (a new shape of the catalogue needs its own)");
        }
    }

    private static IGH_Goo Goo(Assembly plugin, string type, object datum)
    {
        var method = plugin.GetTypes().Single(x => x.Name == type).GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "FromGpc");
        return (IGH_Goo)method.Invoke(null, new[] { datum }.Concat(Enumerable.Repeat((object)null, method.GetParameters().Length - 1)).ToArray());
    }

    /// <summary>The binary GH archive round trip of a goo; null when it is not written or not read.</summary>
    private static IGH_Goo RoundTrip(IGH_Goo goo)
    {
        var chunk = new GH_LooseChunk("P2CF");
        if (!goo.Write(chunk)) return null;
        var loaded = new GH_LooseChunk("P2CF"); loaded.Deserialize_Binary(chunk.Serialize_Binary());
        var copy = (IGH_Goo)Activator.CreateInstance(goo.GetType());
        return copy.Read(loaded) ? copy : null;
    }

    private static object Datum(object goo) => goo?.GetType().GetProperty("Gpc")?.GetValue(goo);

    /// <summary>
    /// RT-01 (native: Straus7 resolves the sections). For each shape of <see cref="BridGHe.Common.Straus.StrausSectionCatalog"/> (21; a shape
    /// without a formula in <see cref="Expected"/> fails): the names of its dimensions are those the resolver reads (the GPC catalogue, in the same
    /// order); the recipe of the Straus7 Section component holds the key and the dimensions, and so does its GH archive read back (the same text);
    /// the section Straus7 computes for the recipe (area), its display mesh (area of the faces, extents) and the area of the property the analysis
    /// takes (<see cref="GpcPropertyBuilders.PropertySection"/>) equal the formula: 1e-9 for the polygons; for the circles and the bulb flat, whose
    /// outline Straus7 and its mesh make polygonal, 2 % on the areas and 1 % on the extents. The bulb flat: its extents only.
    /// </summary>
    internal static void NativeSections(Action<bool, string> check)
    {
        var plugin = R2aDesignFixtures.Plugin;
        int shapes = 0, formulas = 0;
        check(BridGHe.Common.Straus.StrausSectionCatalog.All.Length == 21, "RT-01: the catalogue holds the 21 native shapes the oracle covers");
        foreach (var shape in BridGHe.Common.Straus.StrausSectionCatalog.All)
        {
            string key = shape.Key, label = "RT-01 " + key;
            var dimensions = key == "BglBulb" ? new double[] { 200, 50, 10, 10, 0, 5, 0, 0, 30 } : shape.Dimensions.Select(Dimension).ToArray();
            double V(string name) { int i = Array.IndexOf(shape.Dimensions, name); if (i < 0) throw new ArgumentException(key + ": no dimension " + name); return dimensions[i]; }
            var expected = Expected(key, V);
            // The names the resolver reads, in the same order.
            var gpcNames = GPC.Converter.Straus7.Straus7SectionCatalog.Get(key).Dimensions;
            check(gpcNames.SequenceEqual(shape.Dimensions), label + ": the dimensions of the component are those of the resolver, in the same order (" + string.Join(",", gpcNames) + ")");
            // The recipe of the component (key and dimensions) and its GH archive.
            var recipe = GpcSectionBuilders.Native(key, dimensions, null, null, null, 2);
            check(recipe.NativeKey == key && recipe.NativeDimensions.SequenceEqual(dimensions), label + ": the recipe holds the key and the dimensions");
            var back = Datum(RoundTrip(Goo(plugin, "GH_BeamSection", recipe))) as LegacySectionRecipe;
            check(back != null && back.NativeKey == key && back.NativeDimensions.SequenceEqual(dimensions) && GpcDraftCapture.Text(back) == GpcDraftCapture.Text(recipe),
                label + ": the GH archive of the recipe reads back with its key and dimensions (the same text)");
            // The section Straus7 computes for the recipe, its display mesh, the property of the analysis.
            var resolved = GpcNativeSections.Resolve(back?.NativeKey ?? key, (back?.NativeDimensions ?? dimensions).ToArray(), null, null, null, 1);
            double area = resolved.Section.ReferenceProperties[0];
            double mesh = resolved.Faces.Sum(f => Math.Abs(Enumerable.Range(0, f.Length).Sum(k => { var p = resolved.Vertices[f[k]]; var q = resolved.Vertices[f[(k + 1) % f.Length]]; return p[0] * q[1] - q[0] * p[1]; })) / 2);
            double width = resolved.Vertices.Max(p => p[0]) - resolved.Vertices.Min(p => p[0]), depth = resolved.Vertices.Max(p => p[1]) - resolved.Vertices.Min(p => p[1]);
            double onArea = expected.Curved ? 0.02 : 1e-9, onExtent = expected.Curved ? 0.01 : 1e-9;
            check(Near(width, expected.Width, onExtent) && Near(depth, expected.Depth, onExtent),
                label + $": the extents of the display mesh {R(width)} × {R(depth)}, by the formula {R(expected.Width)} × {R(expected.Depth)}");
            if (!double.IsNaN(expected.Area))
            {
                double property = GpcPropertyBuilders.PropertySection(back ?? recipe).Area;
                check(Near(area, expected.Area, onArea), label + $": the area Straus7 computes {R(area)}, by the formula {R(expected.Area)}");
                check(Near(mesh, expected.Area, onArea), label + $": the area of the display mesh {R(mesh)}, by the formula {R(expected.Area)}");
                check(Near(property, expected.Area, onArea), label + $": the area of the property of the analysis {R(property)}, by the formula {R(expected.Area)}");
                formulas++;
            }
            shapes++;
            Console.WriteLine($"P2CF RT-01 {key}: A {R(area)} (formula {R(expected.Area)}), mesh {R(mesh)}, extents {R(width)} x {R(depth)} (formula {R(expected.Width)} x {R(expected.Depth)})");
        }
        Console.WriteLine($"P2CF RT-01: {shapes} native recipes, {formulas} with the area by their own formula (the bulb flat by its extents only)");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // RT-03

    /// <summary>
    /// RT-03, Attach Moving Paths on GPC (former <c>core|replacing moving paths leaves original model intact</c> and <c>…removes obsolete generated
    /// cases</c>): a source with a path of its own (Attach Moving Paths on the first GPC example with beams), its canonical text (the text of the
    /// R6.0 capture) taken before the call; the paths replaced by a new one: the source has the same text after the call and keeps its path, the
    /// output is another model with the new path only, its case registered with the path, the case of the old path no longer a path case. The static
    /// cases a former moving analysis generated are not an input of the GPC path (the reader leaves them out, <c>LegacyModelImport.Cases</c>): no
    /// frozen payload holds one, so that part of the former check has no GPC case (P2CF report). Returns the checks.
    /// </summary>
    internal static int MovingPaths(Func<string, double, MovingPath> path)
    {
        int checks = 0;
        void Expect(bool ok, string what) { if (!ok) throw new Exception("P2CF RT-03 FAIL: " + what); checks++; }
        var example = GpcNativeExamples.Create().First(e => e.Input.Model.BeamElements.Count > 0);
        var source = GpcModelTransforms.ReplacePaths(example.Input, new[] { path("Old moving case", 0) }, GpcIdentity.Derive(GpcIdentity.NameSpace, "p2cf-rt03:source"));
        string before = GpcDraftCapture.Text(source);
        var origins = source.Report.CaseOrigins.ToDictionary(p => p.Key, p => p.Value);
        var output = GpcModelTransforms.ReplacePaths(source, new[] { path("Traffic", 200) }, GpcIdentity.Derive(GpcIdentity.NameSpace, "p2cf-rt03:output"));
        Expect(GpcDraftCapture.Text(source) == before, example.Id + ": the source of Attach Moving Paths has the same canonical text after the call");
        Expect(source.Report.CaseOrigins.Count == origins.Count && origins.All(p => source.Report.CaseOrigins.TryGetValue(p.Key, out var o) && o == p.Value), example.Id + ": the case origins of the source as before");
        Expect(source.Model.MovingLoads?.Paths.Count == 1 && source.Model.MovingLoads.Paths[0].Template.Name == "Axle Old moving case", example.Id + ": the source keeps its own path");
        Expect(!ReferenceEquals(output.Model, source.Model) && output.Model.MovingLoads?.Paths.Count == 1 && output.Model.MovingLoads.Paths[0].Template.Name == "Axle Traffic"
            && output.Model.LoadCases.TryGetValue("Traffic", out var traffic) && output.Model.MovingLoads.Paths[0].CaseGuid == traffic.Guid, example.Id + ": the output is another model with the new path only, on its case");
        Expect(output.Report.CaseOrigins.TryGetValue("Traffic", out var t) && t.HasFlag(LegacyCaseOrigins.Moving) && t.HasFlag(LegacyCaseOrigins.Registered)
            && output.Report.CaseOrigins.TryGetValue("Old moving case", out var old) && !old.HasFlag(LegacyCaseOrigins.Moving), example.Id + ": the case of the new path is a path case, the case of the old one no longer");
        Console.WriteLine($"P2CF RT-03 moving paths ({example.Id}): {checks} checks (the source intact, the old path replaced)");
        return checks;
    }

    /// <summary>
    /// RT-03, the former regression <c>archived-attribute-updates</c> on GPC: a vertical beam on a solid rectangle B 20 × D 40, 1000 long (its B along
    /// global Y, as the former check measured it); its GH archive read back (the same text); the preview of the copy made (the cache of the plug-in
    /// filled); then a taper 2 on x at both ends given to the copy (Beam Taper after the round trip): the new draft draws Y 40, X 40, Z 1000; the
    /// copy and the original still draw Y 20 (the cache does not move, the source is not altered). Four checks.
    /// </summary>
    internal static void ArchivedAttributeUpdates(Action<bool, string> check)
    {
        var plugin = R2aDesignFixtures.Plugin;
        var preview = plugin.GetType("BridGHe.Grasshopper.Display.ModelPreview", true).GetMethod("DraftShapes", BindingFlags.Public | BindingFlags.Static);
        BoundingBox Box(LegacyBeamDraft d) { var box = BoundingBox.Empty; foreach (var s in (Brep[])preview.Invoke(null, new object[] { d })) box.Union(s.GetBoundingBox(true)); return box; }
        static bool Is(BoundingBox b, double x, double y, double z) => b.IsValid && Math.Abs(b.Diagonal.X - x) < 1e-6 && Math.Abs(b.Diagonal.Y - y) < 1e-6 && Math.Abs(b.Diagonal.Z - z) < 1e-6;
        static string Of(BoundingBox b) => b.IsValid ? $"{R(b.Diagonal.X)} x {R(b.Diagonal.Y)} x {R(b.Diagonal.Z)}" : "nothing";
        var property = GpcPropertyBuilders.Beam("P2CF archive", 5, GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, 40, b: 20), R2aDesignFixtures.Steel(), "Steel");
        var original = GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(0, 0, 1000), property, null, 0, 0, 0, R2aDesignFixtures.Id("p2cf:archived-attribute-updates"), "A1");
        var copy = Datum(RoundTrip(R2aDesignFixtures.Beam(original))) as LegacyBeamDraft;
        check(copy != null && !ReferenceEquals(copy, original) && GpcDraftCapture.Text(copy) == GpcDraftCapture.Text(original), "RT-03 archived attribute updates: the beam draft reads back from its GH archive (the same text)");
        if (copy == null) return;
        var cached = Box(copy);
        check(Is(cached, 40, 20, 1000), "RT-03 archived attribute updates: the preview of the copy, B 20 along Y: " + Of(cached));
        var tapered = GpcBeamBuilders.WithTaper(copy, 2, 1, 2, 1, "Center", "Center", R2aDesignFixtures.Id("p2cf:archived-attribute-updates:taper"));
        check(Is(Box(tapered), 40, 40, 1000), "RT-03 archived attribute updates: a taper given after the round trip draws the new solid (Y 40): " + Of(Box(tapered)));
        check(Is(Box(copy), 40, 20, 1000) && Is(Box(original), 40, 20, 1000), "RT-03 archived attribute updates: the copy (its cached preview) and the original still draw Y 20: " + Of(Box(copy)) + ", " + Of(Box(original)));
        // (The former check then cleared the attributes of the FEM beam: a draft is not changed in place, the draft without the taper is the copy
        // above; a second Beam Taper does not replace the first, the reader keeps the first and refuses the others.)
        Console.WriteLine($"P2CF RT-03 archived attribute updates: copy {Of(cached)}, tapered {Of(Box(tapered))}, copy and original after {Of(Box(copy))}, {Of(Box(original))}");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Item 4: the keys of the regressions in P2rRetired.Checks

    /// <summary>The regressions of <c>RegressionTests</c> at <c>b73e5e9^</c>, the base of P2CUT (120 names, from git): every one runs now or is
    /// retired by name.</summary>
    internal const string FormerRegressions = "archive-before-cleanup archived-attribute-updates beam-property-override beam-property-validity bridge-alignment-inputs "
        + "bridge-assignment bridge-bearings bridge-bracing bridge-check-map bridge-check-summary bridge-combination-excel bridge-combination-excel-component "
        + "bridge-connections bridge-construction bridge-deck bridge-deck-grillage bridge-deflection bridge-design-audit bridge-design-components bridge-detail-archives "
        + "bridge-detail-layout bridge-detail-plots bridge-detail-stations bridge-details-example bridge-example-parameters bridge-fatigue-history bridge-history-workflow "
        + "bridge-instant-modulus bridge-linear-small-loads bridge-merge-origin bridge-moment-state bridge-moving-workflow bridge-numerical-baseline bridge-planar-noise "
        + "bridge-plots bridge-report bridge-report-component bridge-result-labels bridge-segment-composite bridge-segment-excel bridge-segment-override "
        + "bridge-segment-sections bridge-span-advisor bridge-stations bridge-utilities bridge-utility-components bridge-workflow build-model-parameters "
        + "component-layout composite-archive composite-component composite-preview composite-property-phases concrete-shrinkage deep-copies display-settings "
        + "get-beam-property gh-model-gpc gh-node-merge gpc-analysis-drafts gpc-bridge-model gpc-build-model gpc-build-model-legacy gpc-builders-a gpc-builders-b "
        + "gpc-builders-c gpc-c3 gpc-c5 gpc-composite-sections gpc-draft-archive gpc-draft-merge gpc-element-drafts gpc-legacy-import gpc-material-kinds gpc-materials "
        + "gpc-model-transforms gpc-null-values gpc-only-elements gpc-only-generators gpc-only-models gpc-only-properties gpc-release-set gpc-result-selection "
        + "gpc-results-file gpc-review-fixes gpc-state-override group-null-identity incomplete-beam-preview indexed-phases inr-inertia load-case-identity merge-mesh "
        + "merge-node-data merge-plate-connectivity model-guids model-preview modular-bridge native-beam-validity native-bridge-components nodal-actions node-index p2d "
        + "p2k-components p2k-natives parametric-bridge plate-point-updates plot-preview plugin-build-check preview-bounds r2b-merge-forces r4b-inputs r4c-inputs "
        + "reinforcement-material shared-properties simple-bridge simple-bridge-components slab-reinforcement staged-load-cases staged-selection wired-load-schema";

    /// <summary>
    /// Item 4 (P2CF): what of <see cref="P2rRetired.Checks"/> can be checked against the code. A key <c>regression|x</c>: x was a regression of the
    /// base (<see cref="FormerRegressions"/>) and is not run now; a key <c>regression-part|x: …</c>: x is a regression that runs; every regression
    /// of the base runs or is retired by name (none dropped silently); a key is declared once (the dictionary refuses a second one when it is built).
    /// The other scopes (core, core-native, import-dump, legacy-payload-scan) name assertions of runs that no longer exist: they stay declarations.
    /// Throws on the first run of a regression (any selection) when one does not hold.
    /// </summary>
    internal static void RegressionKeys(ICollection<string> current, bool print)
    {
        var former = new HashSet<string>(FormerRegressions.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        var bad = new List<string>(); int retired = 0, parts = 0;
        if (former.Count != 120) bad.Add("the list of the base holds " + former.Count + " names, not 120");
        foreach (var key in P2rRetired.Checks.Keys)
        {
            if (key.StartsWith("regression|", StringComparison.Ordinal))
            {
                retired++; string name = key.Substring("regression|".Length);
                if (current.Contains(name)) bad.Add("retired by name but run: " + name);
                if (!former.Contains(name)) bad.Add("retired by name but not a regression of the base: " + name);
            }
            else if (key.StartsWith("regression-part|", StringComparison.Ordinal))
            {
                parts++; int colon = key.IndexOf(':');
                string name = colon < 0 ? null : key.Substring("regression-part|".Length, colon - "regression-part|".Length);
                if (name == null || !current.Contains(name)) bad.Add("a part of a regression that does not run: " + key);
            }
        }
        foreach (var name in former.Where(n => !current.Contains(n) && !P2rRetired.Checks.ContainsKey("regression|" + n))) bad.Add("a regression of the base neither run nor retired by name: " + name);
        if (bad.Count > 0) throw new InvalidOperationException("P2CF RETIRED CHECKS: " + string.Join(" | ", bad));
        if (print) Console.WriteLine($"P2CF RETIRED CHECKS: {retired} regressions retired by name (regressions of b73e5e9^ not run now), {parts} parts of regressions that run; every regression of b73e5e9^ runs or is retired by name");
    }
}
