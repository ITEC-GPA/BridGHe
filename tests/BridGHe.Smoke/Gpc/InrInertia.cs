#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Numbers = GpcCompositeDefinitions.Numbers;

/// <summary>
/// Lane INR (12/11/2026): the inertias of the generic homogenised analysis property of a composite girder on the section axes (user decision
/// of 12/11, option B: axis 1 horizontal and axis 2 vertical on every beam, <c>I11</c> the inertia about the horizontal axis, angle 0).
/// Before, the property wrote the sorted principal values (<c>J11H ≥ J22H</c>) as I11/I22 with angle 0: when the slab makes Iyy &gt; Ixx the
/// lateral inertia became the vertical bending stiffness (found by lane R4c-grafi, present in 0.3.0 too).
/// <list type="bullet">
/// <item><see cref="HorizontalFirst"/>: the expected rule, written here independently of the product (the principal value whose axis is nearest
/// the horizontal X axis as I11; exactly Ixx/Iyy when X and Y are principal; the others as they were);</item>
/// <item><see cref="Scan"/> (<c>--inr-scan [file ...]</c>, not native): the measurement of every homogenised composite property the example
/// definitions produce (Composite Beam Property, the variants of Bridge State Override, Composite Shrinkage Actions), with only the upstream
/// of those components solved: no analysis runs, nothing saved;</item>
/// <item><see cref="Regression"/> (<c>--regression inr-inertia</c>, not native): the in-memory checks of the fix on the FEM property and on its
/// GPC twin.</item>
/// </list>
/// </summary>
internal static class InrInertia
{
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string G(double v) => v.ToString("G7", CultureInfo.InvariantCulture);
    private static long Bits(double v) => BitConverter.DoubleToInt64Bits(v);
    private static string Messages(IGH_DocumentObject o) => o is GH_ActiveObject a
        ? string.Join("; ", a.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => "error: " + m).Concat(a.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Select(m => "warning: " + m))) : "";

    /// <summary>The expected inertias about the section axes from the homogenised values (any scale): the axis of <paramref name="j11"/> (the
    /// maximum, at <paramref name="angle"/> from X) within 45° of X keeps the values as they were; beyond it the two principal values are
    /// exchanged, and exactly <paramref name="jxx"/>/<paramref name="jyy"/> when the angle is ±90° (X and Y principal).</summary>
    internal static (double I11, double I22, bool Changed) HorizontalFirst(double j11, double j22, double jxx, double jyy, double angle)
    {
        if (!(Math.Abs(angle) > Math.PI / 4)) return (j11, j22, false);
        return Math.Abs(angle) == Math.PI / 2 ? (jxx, jyy, true) : (j22, j11, true);
    }

    private sealed class Row
    {
        internal string File, Source, Definition, Output, Fingerprint; internal double N; internal bool Concrete, Rebars;
        internal double Ixx, Iyy, Ixy, I1, I2, Angle, WrittenI11, WrittenI22, NewI11, NewI22; internal int Count = 1; internal string Note = "";
        /// <summary>The 0.3.0 rule (I1 = J11H/n, I2 = J22H/n, what CreateHomogenizedProperty wrote) against the rule of option B.</summary>
        internal bool Changes => Bits(I1) != Bits(NewI11) || Bits(I2) != Bits(NewI22);
        /// <summary>What the product of this build writes: "0.3.0", "B" (both when nothing changes) or "other".</summary>
        internal string Product => Bits(WrittenI11) == Bits(NewI11) && Bits(WrittenI22) == Bits(NewI22) ? (Changes ? "B" : "0.3.0 = B")
            : Bits(WrittenI11) == Bits(I1) && Bits(WrittenI22) == Bits(I2) ? "0.3.0" : "other";
        internal string Key => string.Join("|", File, Source, Fingerprint, Output, R(N), Concrete);
    }

    /// <summary><c>--inr-scan [file ...] [--out file.tsv]</c>: every definition of <c>example/</c> (archives included, <c>calcoli</c> excluded) or
    /// the given ones.</summary>
    internal static int Scan(string[] args)
    {
        BridgeExampleMigration.Initialize();
        var rest = args.SkipWhile(a => a != "--inr-scan").Skip(1).ToList();
        int o = rest.IndexOf("--out"); string outFile = o >= 0 ? rest[o + 1] : Path.GetFullPath(Path.Combine("..", "results", "inr-measure.tsv"));
        var files = rest.Where((a, i) => !a.StartsWith("--") && (o < 0 || i != o + 1)).Select(a => Path.GetFullPath(Path.Combine(ExamplePaths.Root, a))).ToList();
        if (files.Count == 0)
            files = Directory.GetFiles(ExamplePaths.Root, "*.gh*", SearchOption.AllDirectories)
                .Where(f => (f.EndsWith(".gh") || f.EndsWith(".ghx")) && !f.Contains(Path.DirectorySeparatorChar + "calcoli" + Path.DirectorySeparatorChar))
                .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var rows = new Dictionary<string, Row>(StringComparer.Ordinal); var order = new List<Row>();
        var shrinkage = new List<string>(); var informative = new List<Row>(); int failed = 0, relevant = 0;
        void Add(Row r) { if (rows.TryGetValue(r.Key, out var seen)) { seen.Count++; return; } rows.Add(r.Key, r); order.Add(r); }
        Row Values(string label, string source, BridgeCompositeSectionDefinition d, string output, double n, bool concrete, double writtenI11, double writtenI22, string note)
        {
            var p = GpcCompositeSections.AnalysisProperties(d, n, concrete, d.ConsiderRebar, true);
            var e = HorizontalFirst(p.J11H, p.J22H, p.JxxH, p.JyyH, p.angleX);
            return new Row
            {
                File = label, Source = source, Definition = d.Name, Output = output, Fingerprint = d.Fingerprint(), N = n, Concrete = concrete, Rebars = d.ConsiderRebar,
                Ixx = p.JxxH / n, Iyy = p.JyyH / n, Ixy = p.JxyH / n, I1 = p.J11H / n, I2 = p.J22H / n, Angle = p.angleX, WrittenI11 = writtenI11, WrittenI22 = writtenI22,
                NewI11 = e.I11 / n, NewI22 = e.I22 / n, Note = note,
            };
        }
        foreach (var file in files)
        {
            string label = Path.GetRelativePath(ExamplePaths.Root, file);
            try
            {
                using var doc = GrasshopperBridgeExample.Read(file); doc.FilePath = file;
                var components = doc.Objects.OfType<IGH_Component>().ToArray();
                var composites = components.Where(c => c.GetType().Name == "BeamPropertyCompositeComponent").ToArray();
                var states = components.Where(c => c.GetType().Name == "BridgeStateOverrideComponent").ToArray();
                var shrinks = components.Where(c => c.GetType().Name == "CompositeShrinkageActionsComponent").ToArray();
                var internalised = doc.Objects.OfType<IGH_Param>().Concat(components.SelectMany(c => c.Params.Input))
                    .Where(p => (p.GetType().GetProperty("PersistentData")?.GetValue(p) as Grasshopper.Kernel.Data.IGH_Structure)?.AllData(true).Any(g => g?.GetType().Name == "GH_CompositeBeamProperty") == true).ToArray();
                if (composites.Length + states.Length + shrinks.Length + internalised.Length == 0) { Console.WriteLine($"INR SCAN {label}: no composite property"); continue; }
                relevant++;
                // Only the upstream of the composite properties is solved: the Composite Beam Property components, the composite input of State
                // Override and Shrinkage Actions; analyses are never part of it (locked, Keep Running and Run false in memory, nothing saved).
                static bool Analysis(IGH_DocumentObject x) => x.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent";
                var upstream = new HashSet<IGH_DocumentObject>(); var skipped = new HashSet<string>(StringComparer.Ordinal);
                void Up(IGH_DocumentObject x)
                {
                    if (Analysis(x)) { skipped.Add(x.NickName); return; }
                    if (!upstream.Add(x)) return;
                    var parameters = x is IGH_Component c ? c.Params.Input : x is IGH_Param single ? new List<IGH_Param> { single } : new List<IGH_Param>();
                    foreach (var p in parameters) foreach (var s in p.Sources) Up(s.Attributes.GetTopLevel.DocObject);
                }
                void UpParam(IGH_Param p) { foreach (var s in p.Sources) Up(s.Attributes.GetTopLevel.DocObject); }
                foreach (var c in composites) Up(c);
                foreach (var c in states) UpParam(c.Params.Input[2]);
                foreach (var c in shrinks) Up(c); // the component itself too: the guard of the product gives its message
                foreach (var x in doc.Objects.OfType<IGH_ActiveObject>()) if (!upstream.Contains(x) || Analysis(x)) x.Locked = true;
                foreach (var c in components.Where(Analysis))
                    foreach (var p in c.Params.Input.OfType<Grasshopper.Kernel.Parameters.Param_Boolean>().Where(p => p.Name is "Keep Running" or "Run"))
                    { foreach (var s in p.Sources.ToArray()) p.RemoveSource(s); p.PersistentData.Clear(); p.SetPersistentData(false); }
                BridgeSegmentExcelTests.Solve(doc, true);
                int before = order.Count;
                var definitions = new Dictionary<string, BridgeCompositeSectionDefinition>(StringComparer.Ordinal);
                foreach (var c in composites)
                {
                    var outputs = Enumerable.Range(0, 5).Select(i => c.Params.Output[i].VolatileData.AllData(true).ToList()).ToArray();
                    string source = "Composite Beam Property '" + c.NickName + "' " + c.InstanceGuid.ToString("N").Substring(0, 8);
                    if (outputs[0].Count == 0) { Console.WriteLine($"INR SCAN {label}: {source}: no output ({Messages(c)}); inputs: {string.Join("; ", c.Params.Input.Select(p => p.NickName + " " + p.VolatileDataCount + " from " + string.Join(",", p.Sources.Select(s => s.Attributes.GetTopLevel.DocObject.NickName + (((IGH_ActiveObject)s.Attributes.GetTopLevel.DocObject).Locked ? " (locked)" : "") + " " + s.VolatileDataCount + " " + Messages(s.Attributes.GetTopLevel.DocObject)))))}"); continue; }
                    if (outputs.Skip(2).Any(x => x.Count != outputs[0].Count)) throw new Exception(source + ": outputs not aligned");
                    for (int i = 0; i < outputs[0].Count; i++)
                    {
                        var d = (BridgeCompositeSectionDefinition)outputs[0][i].GetType().GetProperty("Gpc").GetValue(outputs[0][i]);
                        definitions[d.Fingerprint()] = d;
                        bool steelOnly = !d.ConsiderConcrete && !d.ConsiderRebar;
                        foreach (var (port, n, name) in new[] { (2, d.NShortTerm, "n0"), (3, d.NLongTerm, "n∞"), (4, d.NShrinkage, "n,sh") })
                        {
                            if (steelOnly) continue; // the Only Steel property: not homogenised
                            var goo = outputs[port][i];
                            var gpc = goo.GetType().GetProperty("Gpc")?.GetValue(goo) as BridGHe.Common.Gpc.Legacy.LegacyBeamProperty;
                            var fem = goo.GetType().GetProperty("Value")?.GetValue(goo) as BeamPropertyModel;
                            var section = (gpc?.Property as GPC.Model.ElementProperties.ElasticBeamProperty)?.Section;
                            double w11 = section?.J11 ?? fem.I11, w22 = section?.J22 ?? fem.I22;
                            string note = fem != null && section != null && (Bits(fem.I11) != Bits(section.J11) || Bits(fem.I22) != Bits(section.J22)) ? "FEM/GPC differ" : "";
                            Add(Values(label, source, d, name, n, d.ConsiderConcrete, w11, w22, note));
                        }
                    }
                }
                foreach (var p in doc.Objects.OfType<IGH_Param>().Concat(components.SelectMany(c => c.Params.Input.Concat(c.Params.Output))))
                    foreach (var goo in p.VolatileData.AllData(true).Concat((p.GetType().GetProperty("PersistentData")?.GetValue(p) as Grasshopper.Kernel.Data.IGH_Structure)?.AllData(true) ?? Enumerable.Empty<Grasshopper.Kernel.Types.IGH_Goo>()))
                        if (goo?.GetType().Name == "GH_CompositeBeamProperty" && goo.GetType().GetProperty("Gpc").GetValue(goo) is BridgeCompositeSectionDefinition d && !definitions.ContainsKey(d.Fingerprint()))
                        {
                            definitions[d.Fingerprint()] = d; Console.WriteLine($"INR SCAN {label}: composite '{d.Name}' in '{p.NickName}' not from a Composite Beam Property of the file");
                            // Informative: the homogenised properties a Composite Beam Property would give on this definition (the file produces none).
                            foreach (var (n, name) in new[] { (d.NShortTerm, "n0"), (d.NLongTerm, "n∞"), (d.NShrinkage, "n,sh") })
                            {
                                if (!d.ConsiderConcrete && !d.ConsiderRebar) continue;
                                var h = GpcCompositeSections.Homogenised(d, n, d.ConsiderConcrete);
                                informative.Add(Values(label, "internalised in '" + p.NickName + "' (no homogenised property produced by the file)", d, name, n, d.ConsiderConcrete, h.I11, h.I22, "informative"));
                            }
                        }
                foreach (var c in states)
                {
                    string source = "Bridge State Override '" + c.NickName + "' " + c.InstanceGuid.ToString("N").Substring(0, 8);
                    var given = c.Params.Input[2].Sources.SelectMany(s => s.VolatileData.AllData(true)).Select(g => g?.GetType().GetProperty("Gpc")?.GetValue(g) as BridgeCompositeSectionDefinition).Where(d => d != null).ToList();
                    string note = given.Count > 0 ? "its composite input" : "composite input not solved here (upstream of an analysis): every definition of the file";
                    var used = (given.Count > 0 ? given : definitions.Values.ToList()).GroupBy(d => d.Fingerprint()).Select(g => g.First()).ToList();
                    var response = c.Params.Input[4];
                    int? behaviour = response.Sources.Count == 0 ? (response.GetType().GetProperty("PersistentData").GetValue(response) as Grasshopper.Kernel.Data.IGH_Structure)?.AllData(true)
                        .Select(g => g is Grasshopper.Kernel.Types.GH_Integer x ? x.Value : (int?)null).FirstOrDefault() : null;
                    foreach (var d in used)
                        foreach (var (b, n, name) in new[] { (1, d.NLongTerm, "LongTerm n∞"), (2, d.NShortTerm, "ShortTerm n0") })
                        {
                            if (behaviour != null && behaviour != b) continue;
                            foreach (bool cracked in new[] { false, true })
                            {
                                var h = GpcCompositeSections.Homogenised(d, n, !cracked);
                                Add(Values(label, source, d, name + (cracked ? " C" : " U"), n, !cracked, h.I11, h.I22, note + (behaviour == null ? "; response wired: both" : "")));
                            }
                        }
                }
                foreach (var c in shrinks)
                {
                    string source = "Composite Shrinkage Actions '" + c.NickName + "' " + c.InstanceGuid.ToString("N").Substring(0, 8);
                    foreach (var goo in c.Params.Input[0].Sources.SelectMany(s => s.VolatileData.AllData(true)))
                    {
                        if (goo?.GetType().GetProperty("Gpc")?.GetValue(goo) is not BridgeCompositeSectionDefinition d) continue;
                        var p = GpcCompositeSections.AnalysisProperties(d, d.NShrinkage, true, d.ConsiderRebar, true);
                        var e = HorizontalFirst(p.J11H, p.J22H, p.JxxH, p.JyyH, p.angleX);
                        bool product = Math.Abs(p.JxyH) > 1e-10 * (p.JxxH + p.JyyH);
                        bool refusedBefore = product || Math.Abs(p.J11H - p.JxxH) > 1e-8 * p.JxxH, refusedAfter = product || Math.Abs(e.I11 - p.JxxH) > 1e-8 * p.JxxH;
                        string text = $"{label}\t{source}\t{d.Name}\tn,sh {R(d.NShrinkage)}\tIxx/n {G(p.JxxH / d.NShrinkage)}\tIyy/n {G(p.JyyH / d.NShrinkage)}\tguard before: {(refusedBefore ? "refused" : "accepted")}\tafter: {(refusedAfter ? "refused" : "accepted")}\tcomponent: {c.Params.Output[1].VolatileDataCount} outputs {Messages(c)}";
                        shrinkage.Add(text); Console.WriteLine("INR SHRINKAGE " + text);
                    }
                }
                Console.WriteLine($"INR SCAN {label}: {composites.Length} Composite Beam Property, {states.Length} State Override, {shrinks.Length} Shrinkage Actions; {order.Count - before} distinct properties, {order.Skip(before).Count(r => r.Changes)} change" +
                    (skipped.Count > 0 ? "; analyses not solved: " + string.Join(", ", skipped) : ""));
            }
            catch (Exception e) { failed++; Console.WriteLine($"INR SCAN {label}: FAILED {e.GetType().Name}: {e.Message}"); }
        }
        var lines = new List<string> { string.Join("\t", "file", "source", "definition", "output", "n", "concrete", "rebars", "count", "Ixx/n", "Iyy/n", "Ixy/n", "i1/n", "i2/n", "angle deg",
            "0.3.0 I11 (J11H/n)", "0.3.0 I22 (J22H/n)", "new I11", "new I22", "changes", "I11 new/0.3.0-1", "I22 new/0.3.0-1", "this build writes", "note", "fingerprint") };
        var informativeRows = informative.GroupBy(r => r.Key).Select(g => { var r = g.First(); r.Count = g.Count(); return r; }).ToList();
        foreach (var r in order.Concat(informativeRows))
            lines.Add(string.Join("\t", r.File, r.Source, r.Definition, r.Output, R(r.N), r.Concrete, r.Rebars, r.Count, G(r.Ixx), G(r.Iyy), G(r.Ixy), G(r.I1), G(r.I2), G(r.Angle * 180 / Math.PI),
                R(r.I1), R(r.I2), R(r.NewI11), R(r.NewI22), r.Changes ? "YES" : "no", r.Changes ? (r.NewI11 / r.I1 - 1).ToString("P2", CultureInfo.InvariantCulture) : "",
                r.Changes ? (r.NewI22 / r.I2 - 1).ToString("P2", CultureInfo.InvariantCulture) : "", r.Product, r.Note, r.Fingerprint.Substring(0, Math.Min(16, r.Fingerprint.Length))));
        lines.Add(""); lines.AddRange(shrinkage.Select(s => "SHRINKAGE\t" + s));
        File.WriteAllLines(outFile, lines);
        int changes = order.Count(r => r.Changes);
        Console.WriteLine($"INR SCAN TOTAL {files.Count} files, {relevant} with composite properties, {failed} failed: {order.Count} distinct homogenised properties, {changes} would change" +
            (changes > 0 ? $" (I11 relative change {order.Where(r => r.Changes).Min(r => r.NewI11 / r.I1 - 1):P2} to {order.Where(r => r.Changes).Max(r => r.NewI11 / r.I1 - 1):P2})" : "") +
            $"; this build writes: {string.Join(", ", order.GroupBy(r => r.Product).Select(g => g.Count() + " " + g.Key))}; {shrinkage.Count} shrinkage actions; " +
            $"informative (internalised definitions, no homogenised property produced): {informativeRows.Count} variants, {informativeRows.Count(r => r.Changes)} would differ from 0.3.0; table {outFile}");
        return failed == 0 ? 0 : 1;
    }

    /// <summary><c>--regression inr-inertia</c>: the homogenised property of Composite Beam Property (the FEM property and its GPC twin, Bridge
    /// State Override uncracked and cracked) on the section axes, against the homogenised values of the library (not the code changed):
    /// a wide slab (Iyy &gt; Ixx) gives I11 = Ixx/n and I22 = Iyy/n bit for bit; a narrow one keeps the former doubles J11H/n, J22H/n; the
    /// shear areas and the angle as before.</summary>
    internal static void Regression()
    {
        int wide = 0, wideCracked = 0, narrow = 0, checks = 0;
        void Require(bool ok, string what) { if (!ok) throw new Exception("INR INERTIA FAIL: " + what); checks++; }
        // The side girder of the spine examples 16–18 (H 1600 × 600 × 40, web 16, slab 4000 × 250: wide when uncracked) and a narrow slab
        // (1200 × 250) on the same H, with bars in both rows; a flat H (300 high, flanges 600 × 40) on the narrow slab: wider than high also
        // cracked (steel and bars: the difference of two homogenisations of CompositePropertyAdapter, the bars of a row at the centre).
        Numbers Section(string name, double height, double slab) => new Numbers
        {
            Name = name, Height = height, Web = 16, TopWidth = 600, TopThickness = 40, BottomWidth = 600, BottomThickness = 40, SlabWidth = slab, SlabHeight = 250,
            TopArea = slab * 250 * 0.006, TopCover = 50, BottomArea = slab * 250 * 0.006, BottomCover = 50, SteelThermalExpansion = 1.2e-5, SteelDensity = 7.85e-9,
            NShortTerm = 6.2, NLongTerm = 18.3, NShrinkage = 16.1, ConsiderConcrete = true, ConsiderRebar = true,
        };
        foreach (var x in new[] { Section("INR wide", 1600, 4000), Section("INR narrow", 1600, 1200), Section("INR flat", 300, 1200) })
        {
            var definition = GpcCompositeDefinitions.Create(x);
            var fem = Fem(x);
            foreach (double n in new[] { x.NShortTerm, x.NLongTerm, x.NShrinkage })
                foreach (bool concrete in new[] { true, false })
                {
                    string label = $"{x.Name} n={R(n)} concrete={concrete}";
                    var p = GpcCompositeSections.AnalysisProperties(definition, n, concrete, true, true);
                    Require(Math.Abs(p.JxyH) <= 1e-10 * (p.JxxH + p.JyyH), label + ": the sections of the check are symmetric");
                    var gpc = GpcCompositeSections.Homogenised(definition, n, concrete);
                    var femCopy = ModelArchive.Clone(fem); femCopy.ConsiderConcrete = concrete;
                    var s = femCopy.CreateHomogenizedProperty(n, "").Section;
                    double expected11, expected22;
                    if (p.JyyH > p.JxxH)
                    {
                        // Axis 1 horizontal: the inertia about X (vertical bending) as I11, exactly.
                        expected11 = p.JxxH / n; expected22 = p.JyyH / n; if (concrete) wide++; else wideCracked++;
                        Require(Bits(p.J11H) != Bits(p.JxxH), label + ": a wide section has its maximum about Y");
                    }
                    else { expected11 = p.J11H / n; expected22 = p.J22H / n; narrow++; } // as before: the same doubles
                    Require(Bits(gpc.I11) == Bits(expected11) && Bits(gpc.I22) == Bits(expected22), $"{label}: GPC I11 {R(gpc.I11)} I22 {R(gpc.I22)}, expected {R(expected11)} {R(expected22)}");
                    Require(Bits(s.I11) == Bits(expected11) && Bits(s.I22) == Bits(expected22), $"{label}: FEM I11 {R(s.I11)} I22 {R(s.I22)}, expected {R(expected11)} {R(expected22)}");
                    Require(s.AngleX1Rad == 0, label + ": angle 0");
                    // The shear areas stay those of axis 1 horizontal (GPCModel ComputeHomogenised at angle 0), unchanged by the fix.
                    var shear = GpcCompositeSections.HomogenisedShearAreas(definition, n, concrete);
                    Require(Bits(gpc.ShearArea1) == Bits(shear.Area1) && Bits(gpc.ShearArea2) == Bits(shear.Area2) && Bits(s.ShearA1) == Bits(shear.Area1) && Bits(s.ShearA2) == Bits(shear.Area2), label + ": shear areas");
                    Require(Bits(gpc.Area) == Bits(p.areaH / n) && Bits(s.SectionArea) == Bits(p.areaH / n), label + ": area");
                }
        }
        Require(wide >= 3 && wideCracked >= 3 && narrow >= 6, $"the sections of the check give {wide} wide uncracked, {wideCracked} wide cracked and {narrow} narrow variants");
        Console.WriteLine($"INR INERTIA PASS: {wide} wide uncracked and {wideCracked} wide cracked variants (Iyy > Ixx) with I11 = Ixx/n and I22 = Iyy/n bit for bit, {narrow} narrow ones with the former J11H/n, J22H/n bit for bit, " +
            $"on the FEM property and on its GPC twin (uncracked and cracked); angle 0, shear areas of axis 1 horizontal and area unchanged; {checks} checks.");
    }

    /// <summary>The FEM composite property of the numbers (as <c>GpcCompositeSectionChecks.FemProperty</c>).</summary>
    private static CompositeBeamPropertyModel Fem(Numbers x)
    {
        var steel = new SectionModel { Name = x.Name + " I", SectionType = SectionModel.SectionTypes.I, D = x.Height, B1 = x.BottomWidth, T1 = x.BottomThickness, B2 = x.TopWidth, T2 = x.TopThickness, T3 = x.Web };
        return new CompositeBeamPropertyModel(x.Name)
        {
            Name = x.Name, SteelSection = steel, ConcreteSection = new SectionConcreteSlabModel(x.SlabHeight, x.SlabWidth, x.TopArea, x.TopCover, x.BottomArea, x.BottomCover),
            SteelMaterial = new MaterialModel(x.Steel) { Name = x.Steel, Modulus = x.SteelE, MinimumYieldStress = x.SteelFy, MinimumTensileStress = x.SteelFu, PoissonRatio = x.SteelPoisson,
                ShearModulus = x.SteelShearModulus, UsePoisson = x.SteelUsePoisson, Density = x.SteelDensity, ThermalExpansion = x.SteelThermalExpansion },
            ConcreteMaterial = new MaterialModel(x.Concrete) { Name = x.Concrete, Modulus = x.ConcreteE, SpecificCompressiveStrength = x.Fck },
            RebarMaterial = new MaterialModel(x.Rebar) { Name = x.Rebar, Modulus = x.RebarE, MinimumYieldStress = x.RebarFy, MinimumTensileStress = x.RebarFu, ReinforcementUltimateStrain = x.RebarUltimateStrain },
            NInfinite = x.NLongTerm, NInstant = x.NShortTerm, NShrinkage = x.NShrinkage, VerticalOffset = x.VerticalOffset, ConsiderConcrete = x.ConsiderConcrete, ConsiderRebar = x.ConsiderRebar,
        };
    }
}
#endif
