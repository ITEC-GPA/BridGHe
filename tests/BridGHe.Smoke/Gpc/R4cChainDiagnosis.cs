#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.6 lane R4c-moving, task 2 (R1, open question 6): why the analyses M02_NINF and M03_N0 of the chain of the rehearsal of the 50
/// (<see cref="GpcOnlyReplay.Rehearsal"/>) have another analysis fingerprint than the analyses of the definition. Diagnosis only
/// (<c>BRIDGHE_R4CM_CHAIN=1</c>, native, never in the gate): the part of the chain between the analysis M01 and the analyses whose input is
/// built downstream of it (M02, M03: Decompose Model of M01, Bridge Segment Composite, Beam Moment Dispatch, Bridge State Override, the loads,
/// Build Model), rebuilt as R1's chain builds it (the copies wired as in the definition, every datum from outside by its GPC datum alone,
/// the analyses pressed with their Output Path in TEMP, one solution with the switches of P2 on), then for every such analysis: its GPC input
/// against the input of the definition (analysis fingerprint, element identities, the conversion text modulo GUIDs) and its analysed model
/// against the analysed model of the definition (R1's comparison, then the same comparison without the identities: name, datasets, catalog,
/// every result row by element number).
/// </summary>
internal static class R4cChainDiagnosis
{
    private static readonly MethodInfo GpcOnlyMethod = typeof(GpcOnlyReplay).GetMethod("GpcOnly", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("GpcOnlyReplay.GpcOnly");
    private static IGH_Goo GpcOnly(IGH_Goo goo, ISet<string> kinds) => (IGH_Goo)GpcOnlyMethod.Invoke(null, new object[] { goo, kinds });

    internal static void Run(GH_Document doc, string label)
    {
        if (Environment.GetEnvironmentVariable("BRIDGHE_R4CM_CHAIN") != "1") return;
        var kinds = new HashSet<string>(GpcOnlyReplay.ElementGoo.Concat(GpcOnlyReplay.PropertyGoo).Concat(GpcOnlyReplay.ModelGoo), StringComparer.Ordinal);
        var analyses = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent").ToList();
        static IEnumerable<IGH_Param> Outputs(IGH_DocumentObject o) => o is IGH_Component c ? c.Params.Output : o is IGH_Param p ? new[] { p } : Enumerable.Empty<IGH_Param>();
        static IEnumerable<IGH_Param> Inputs(IGH_DocumentObject o) => o is IGH_Component c ? c.Params.Input : o is IGH_Param p ? new[] { p } : Enumerable.Empty<IGH_Param>();
        HashSet<IGH_DocumentObject> Walk(IEnumerable<IGH_DocumentObject> from, bool down)
        {
            var seen = new HashSet<IGH_DocumentObject>(); var queue = new Queue<IGH_DocumentObject>(from);
            while (queue.Count > 0)
            {
                var o = queue.Dequeue();
                var next = down ? Outputs(o).SelectMany(p => p.Recipients) : Inputs(o).SelectMany(p => p.Sources);
                foreach (var p in next) { var top = p.Attributes?.GetTopLevel?.DocObject; if (top != null && seen.Add(top)) queue.Enqueue(top); }
            }
            return seen;
        }
        // The analyses whose input is built downstream of another analysis (in the chain of R1: by the copies, on the path of P2).
        var downstreamOf = analyses.ToDictionary(a => a, a => Walk(new[] { a }, true));
        var targets = analyses.Where(t => analyses.Any(a => a != t && downstreamOf[a].Contains(t)) && t.GetType().Name == "StrausR3AnalysisComponent").ToList();
        if (targets.Count == 0) { Console.WriteLine($"R4C CHAIN {label}: no analysis takes an input built downstream of another analysis"); return; }
        var sources = analyses.Where(a => targets.Any(t => t != a && downstreamOf[a].Contains(t))).ToList();
        var upstream = Walk(targets, false);
        var objects = doc.Objects.Where(o => sources.Contains(o) || targets.Contains(o) || sources.Any(s => downstreamOf[s].Contains(o)) && upstream.Contains(o)).ToList();
        Console.WriteLine($"R4C CHAIN {label}: analyses {string.Join(", ", targets.Select(t => t.NickName))} take an input built downstream of {string.Join(", ", sources.Select(s => s.NickName))}: "
            + $"{objects.Count} objects rebuilt as the chain of the rehearsal ({string.Join(", ", objects.Where(o => !(o is IGH_Param)).GroupBy(o => o.GetType().Name).Select(g => g.Key + " " + g.Count()))})");
        string folder = Path.Combine(Path.GetTempPath(), "bridghe-r4cm-chain-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        using var chain = new GH_Document();
        var copies = new Dictionary<IGH_DocumentObject, IGH_DocumentObject>();
        foreach (var o in objects)
        {
            var archive = new GH_Archive();
            if (!archive.AppendObject((GH_DocumentObject)o, "object")) throw new Exception("R4C CHAIN: " + o.Name + " not written");
            var copy = Grasshopper.Instances.ComponentServer.EmitObject(o.ComponentGuid); copy.CreateAttributes();
            if (!archive.ExtractObject((GH_IO.GH_ISerializable)copy, "object")) throw new Exception("R4C CHAIN: " + o.Name + " not read");
            chain.AddObject(copy, false); copies[o] = copy;
        }
        IGH_Param Mapped(IGH_Param source)
        {
            var top = source.Attributes.GetTopLevel.DocObject;
            if (!copies.TryGetValue(top, out var copy)) return null;
            return copy is IGH_Component c ? c.Params.Output[((IGH_Component)top).Params.Output.IndexOf(source)] : (IGH_Param)copy;
        }
        GH_Structure<IGH_Goo> Only(IGH_Structure data)
        {
            var tree = new GH_Structure<IGH_Goo>();
            for (int b = 0; b < data.PathCount; b++)
            {
                var p = data.get_Path(b); tree.EnsurePath(p);
                foreach (var item in data.get_Branch(b)) tree.Append(item == null ? null : GpcOnly((IGH_Goo)item, kinds), p);
            }
            return tree;
        }
        int external = 0;
        foreach (var o in objects)
        {
            var originals = Inputs(o).ToList(); var targetsOfCopy = Inputs(copies[o]).ToList();
            for (int i = 0; i < originals.Count; i++)
            {
                var input = originals[i]; var target = targetsOfCopy[i]; target.RemoveAllSources();
                if (input.Sources.Count == 0) { if (o is IGH_Param) continue; R4cMoving.Set(target, Only(input.VolatileData)); continue; }
                foreach (var source in input.Sources)
                {
                    var inside = Mapped(source);
                    if (inside != null) { target.AddSource(inside); continue; }
                    var relay = new Grasshopper.Kernel.Parameters.Param_GenericObject(); relay.CreateAttributes(); chain.AddObject(relay, false);
                    R4cMoving.Set(relay, Only(source.VolatileData)); target.AddSource(relay); external++;
                }
            }
        }
        // The analyses pressed, each in a folder of its own with the file name of the definition (the analysed model takes its name from it).
        foreach (var a in sources.Concat(targets))
        {
            var copy = (GH_Component)copies[a];
            var path = copy.Params.Input.First(p => p.Name.Equals("Output Path", StringComparison.OrdinalIgnoreCase));
            string given = a.Params.Input.First(p => p.Name.Equals("Output Path", StringComparison.OrdinalIgnoreCase)).VolatileData.AllData(true).Single().ScriptVariable() as string;
            string own = Path.Combine(folder, a.NickName.Split(' ')[0]); Directory.CreateDirectory(own);
            var tree = new GH_Structure<IGH_Goo>(); tree.Append(new GH_String(Path.Combine(own, Path.GetFileName(given))), new GH_Path(0)); R4cMoving.Set(path, tree);
            copy.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(copy, true);
        }
        using (R2aDesignFixtures.WithoutFem()) { GH_Document.EnableSolutions = true; chain.Enabled = true; chain.NewSolution(false, GH_SolutionMode.Silent); }
        Console.WriteLine($"R4C CHAIN {label}: {external} inputs from outside by their GPC data alone; solved");
        var inv = CultureInfo.InvariantCulture;
        foreach (var t in targets)
        {
            string where = $"R4C CHAIN {label} {t.NickName}";
            var copy = (GH_Component)copies[t];
            var definitionInput = R4cMoving.Input(t.Params.Input[0].VolatileData.AllData(true).Single());
            var chainInput = R4cMoving.Input(copy.Params.Input[0].VolatileData.AllData(true).SingleOrDefault());
            if (chainInput == null) { Console.WriteLine(where + ": no GPC input in the chain: " + string.Join(" / ", copy.RuntimeMessages(GH_RuntimeMessageLevel.Error))); continue; }
            // 1. The input.
            string fa = GPC.Model.Models.ModelRevisions.AnalysisFingerprint(definitionInput.Model), fb = GPC.Model.Models.ModelRevisions.AnalysisFingerprint(chainInput.Model);
            var ga = definitionInput.Model.AllElements.Select(e => e.Guid).ToList(); var gb = chainInput.Model.AllElements.Select(e => e.Guid).ToList();
            int sameGuids = ga.Zip(gb).Count(p => p.First == p.Second);
            string ta = GpcDraftCapture.WithoutGuids(GpcDraftCapture.ConversionText(definitionInput)), tb = GpcDraftCapture.WithoutGuids(GpcDraftCapture.ConversionText(chainInput));
            string firstText = FirstDifference(ta, tb);
            Console.WriteLine($"{where}: input: analysis fingerprint {(fa == fb ? "equal" : "DIFFERENT")} ({fa[..12]} / {fb[..12]}); model GUID {(definitionInput.Model.Guid == chainInput.Model.Guid ? "equal" : "different")}; "
                + $"{ga.Count} / {gb.Count} elements, {sameGuids} with the same GUID; the conversion text modulo GUIDs ({ta.Length} characters) {(firstText == null ? "EQUAL" : "DIFFERENT: " + firstText)}");
            // 2. The analysed model.
            var a = t.Params.Output[0].VolatileData.AllData(true).OfType<IGH_Goo>().SingleOrDefault();
            var b = copy.Params.Output[0].VolatileData.AllData(true).OfType<IGH_Goo>().SingleOrDefault();
            if (a == null || b == null) { Console.WriteLine(where + ": no output: " + string.Join(" / ", copy.RuntimeMessages(GH_RuntimeMessageLevel.Error))); continue; }
            string r1 = GpcOnlyReplay.ResultsDifference(a, b);
            var (rows, rowsDifference) = RowsDifference(R4cMoving.Results(a), R4cMoving.Results(b));
            Console.WriteLine($"{where}: analysed: R1's comparison {(r1 == null ? "equal" : "'" + r1 + "'")}; without the identities (name, dataset keys, catalog, every result row by element number): "
                + (rowsDifference == null ? "EQUAL, " + rows.ToString(inv) + " rows" : "DIFFERENT after " + rows.ToString(inv) + " rows: " + rowsDifference));
            // 3. The written files.
            string given = Path.GetFileName(t.Params.Input.First(p => p.Name.Equals("Output Path", StringComparison.OrdinalIgnoreCase)).VolatileData.AllData(true).Single().ScriptVariable() as string);
            string chainFile = Path.Combine(folder, t.NickName.Split(' ')[0], given), definitionFile = t.Params.Input.First(p => p.Name.Equals("Output Path", StringComparison.OrdinalIgnoreCase)).VolatileData.AllData(true).Single().ScriptVariable() as string;
            if (File.Exists(chainFile) && File.Exists(definitionFile))
            {
                byte[] x = File.ReadAllBytes(definitionFile), y = File.ReadAllBytes(chainFile);
                int at = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
                Console.WriteLine($"{where}: the written .st7: {x.Length} / {y.Length} bytes, " + (at < 0 && x.Length == y.Length ? "equal byte for byte" : "first different byte at " + (at < 0 ? Math.Min(x.Length, y.Length) : at)));
            }
        }
        Console.WriteLine($"R4C CHAIN {label}: done (temporary folder {folder})");
    }

    /// <summary>R4c-moving, task 2 (the OutOfMemoryException of the Bridge Report of the chain): with <c>BRIDGHE_R4CM_REPORT=1</c> (diagnosis only, never in
    /// the gate) the Bridge Report of the definition itself is generated once, before the rehearsal, as the chain presses it (File in TEMP, Overwrite,
    /// the template resolved against the definition), with the time, the messages, the size of the file and the memory of the process before and after:
    /// whether the report of the 50 can be generated at all in this process, without the second copy of the chain.</summary>
    internal static void Report(GH_Document doc, string label)
    {
        if (Environment.GetEnvironmentVariable("BRIDGHE_R4CM_REPORT") != "1") return;
        var report = doc.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == "BridgeReportComponent");
        string folder = Path.Combine(Path.GetTempPath(), "bridghe-r4cm-report-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "report.docx");
        GH_Structure<IGH_Goo> One(IGH_Goo value) { var t = new GH_Structure<IGH_Goo>(); t.Append(value, new GH_Path(0)); return t; }
        string template = report.Params.Input.Single(p => p.Name == "Template").VolatileData.AllData(true).FirstOrDefault()?.ScriptVariable() as string;
        if (template != null && !Path.IsPathRooted(template) && !string.IsNullOrEmpty(doc.FilePath)) template = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc.FilePath), template));
        R4cMoving.Set(report.Params.Input.Single(p => p.Name == "File"), One(new GH_String(file)));
        R4cMoving.Set(report.Params.Input.Single(p => p.Name == "Overwrite"), One(new GH_Boolean(true)));
        if (template != null) R4cMoving.Set(report.Params.Input.Single(p => p.Name == "Template"), One(new GH_String(template)));
        R4cMoving.Memory(label + " before the Bridge Report of the definition");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        report.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(report, true); report.ExpireSolution(false);
        BridgeSegmentExcelTests.Solve(doc, true);
        var messages = new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning }.SelectMany(l => report.RuntimeMessages(l).Select(m => l + ": " + m)).ToArray();
        Console.WriteLine($"R4C REPORT {label}: the Bridge Report of the definition generated in {clock.Elapsed.TotalSeconds:F1} s; file {(File.Exists(file) ? new FileInfo(file).Length + " bytes" : "not written")}; "
            + $"{report.Params.Output.Sum(p => p.VolatileDataCount)} output items; messages: {(messages.Length == 0 ? "none" : string.Join(" / ", messages.Select(m => Cut(m))))}");
        R4cMoving.Memory(label + " after the Bridge Report of the definition");
    }

    private static string FirstDifference(string a, string b)
    {
        if (a == b) return null;
        var x = a.Split('\n'); var y = b.Split('\n');
        int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
        return line < 0 ? $"{x.Length} / {y.Length} lines" : $"line {line + 1}: '{Cut(x[line])}' / '{Cut(y[line])}'";
    }

    private static string Cut(string s) => s.Length > 240 ? s[..240] + "…" : s;

    /// <summary>The analysed models compared as R1's comparison does without the analysis fingerprint (which holds the identities of the model and
    /// of its elements): name, dataset keys, catalog and every result row the components read, elements by number.</summary>
    private static (long Rows, string Difference) RowsDifference(R4cMoving.Analysed a, R4cMoving.Analysed b)
    {
        if (a == null || b == null) return (0, "an output without results catalog");
        if (a.Model.Name != b.Model.Name) return (0, "the name: " + a.Model.Name + " / " + b.Model.Name);
        if (!a.Model.Datasets.Keys.SequenceEqual(b.Model.Datasets.Keys)) return (0, "the dataset keys");
        static string E(GpcResultCatalog c) => c.Units.Units + " " + string.Join(";", c.Entries.Select(e => e.Label + "|" + e.Case + "|" + e.Step + "|" + e.Phase));
        if (E(a.Catalog) != E(b.Catalog)) return (0, "the catalog");
        using var x = Rows(a).GetEnumerator(); using var y = Rows(b).GetEnumerator(); long n = 0;
        while (true)
        {
            bool p = x.MoveNext(), q = y.MoveNext();
            if (p != q) return (n, "the number of rows");
            if (!p) return (n, null);
            n++;
            if (x.Current != y.Current) return (n, Cut(x.Current) + " / " + Cut(y.Current));
        }
    }

    private static IEnumerable<string> Rows(R4cMoving.Analysed a)
    {
        static string V(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string Vs(IEnumerable<double> v) => v == null ? "-" : string.Join(" ", v.Select(V));
        foreach (var n in a.Model.NodesElements.Values.OrderBy(e => e.Id)) foreach (var r in GpcResultRows.Node(a.Catalog, a.Model, n))
            yield return "N " + n.Id + " " + r.Entry + " " + Vs(r.Displacement) + " / " + Vs(r.Reaction);
        foreach (var b in a.Model.BeamElements.Values.OrderBy(e => e.Id)) foreach (var r in GpcResultRows.Beam(a.Catalog, a.Model, b))
            yield return "B " + b.Id + " " + r.Entry + " " + V(r.Station) + " " + Vs(new[] { r.Axial, r.Shear1, r.Shear2, r.Torque, r.Bending1, r.Bending2 });
        foreach (var s in a.Model.AreaElements.Values.OrderBy(e => e.Id)) foreach (var r in GpcResultRows.Plate(a.Catalog, a.Model, s))
            yield return "P " + s.Id + " " + r.Entry + " " + r.Node + " " + Vs(r.Values);
    }
}
#endif
