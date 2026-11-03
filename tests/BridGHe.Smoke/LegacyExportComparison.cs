#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Converter.Straus7;

/// <summary>F6A gate: the frozen F1 inputs of examples 50 and 99 (former FEM models, BGHA1) are converted to GPC without the FeMM
/// classes, written by the GPC Converter and compared with the .st7 files of the former exporter (F1).
/// Usage: --legacy-export &lt;output folder&gt; [--f1 &lt;artifacts/f1 folder&gt;] [--only name]</summary>
internal static class LegacyExportComparison
{
    internal static readonly (string Name, string Input, string Native)[] Cases =
    {
        ("50-M01_ACCIAIO", "native-50-traced/models/M01_ACCIAIO-input.bgmodel", "native-50-traced/native/M01_ACCIAIO.st7"),
        ("50-M02_NINF", "native-50-traced/models/M02_NINF-input.bgmodel", "native-50-traced/native/M02_NINF.st7"),
        ("50-M03_N0", "native-50-traced/models/M03_N0-input.bgmodel", "native-50-traced/native/M03_N0.st7"),
        ("50-M04_NSH", "native-50-traced/models/M04_NSH-input.bgmodel", "native-50-traced/native/M04_NSH.st7"),
        ("99-phase-01", "native-99-traced/models/phase-01-input.bgmodel", "native-99-traced/native/01-Straus.st7"),
        ("99-phase-02", "native-99-traced/models/phase-02-input.bgmodel", "native-99-traced/native/02-Straus.st7"),
        ("99-phase-03", "native-99-traced/models/phase-03-input.bgmodel", "native-99-traced/native/03-Straus.st7"),
        ("99-phase-04", "native-99-traced/models/phase-04-input.bgmodel", "native-99-traced/native/04-Straus.st7"),
    };
    internal const string DefaultF1 = @"C:\Users\g.pacini\Documents\Repository\gpc-unification-20261102\artifacts\f1";

    internal static int Run(string[] args)
    {
        string output = Path.GetFullPath(After(args, "--legacy-export") ?? throw new ArgumentException("--legacy-export <output folder>"));
        string f1 = After(args, "--f1") ?? DefaultF1; string only = After(args, "--only");
        Directory.CreateDirectory(output);
        var summary = new StringBuilder(); int failures = 0;
        foreach (var c in Cases.Where(c => only == null || c.Name == only))
        {
            var watch = Stopwatch.StartNew(); string folder = Path.Combine(output, c.Name);
            if (Directory.Exists(folder)) throw new IOException("Output exists (choose a new folder): " + folder);
            Directory.CreateDirectory(folder);
            var report = new LegacyImportReport();
            var tree = LegacyArchive.Read(File.ReadAllBytes(Path.Combine(f1, c.Input)));
            var model = LegacyModelImport.Import(tree, GpcIdentity.Derive(GpcIdentity.NameSpace, "f1:" + c.Name), 0.001, report);
            double import = watch.Elapsed.TotalSeconds;
            // Domain preflight before the export (coordinator note 4/11): errors stop, warnings are reported.
            var preflight = GpcPreflight.Require(model);
            foreach (var w in preflight.Warnings) report.Warnings.Add("PREFLIGHT " + w);
            string st7 = Path.Combine(folder, c.Name + ".st7");
            var plan = Straus7ModelWritePlan.Prepare(model);
            bool solve = args.Contains("--solve"); string lsa = null;
            using (var writer = new Straus7NativeWriter(st7, folder)) { plan.Write(writer, model); if (solve) lsa = writer.Solve(); else writer.Save(); }
            double write = watch.Elapsed.TotalSeconds - import;
            var legacy = St7Dump.Read(Path.Combine(f1, c.Native)); var current = St7Dump.Read(st7);
            var differences = St7Dump.Compare(legacy, current);
            File.WriteAllLines(Path.Combine(folder, "differences.tsv"), differences.Select(d => d.Key + "\t" + d.A + "\t" + d.B));
            File.WriteAllText(Path.Combine(folder, "import-report.txt"), string.Join(Environment.NewLine,
                report.Warnings.Select(w => "WARNING " + w).Concat(report.NotExported.Select(p => $"CARRIED {p.Value} × {p.Key}"))));
            string text = $"== {c.Name}: nodes {legacy.Nodes}/{current.Nodes}, beams {legacy.Beams}/{current.Beams}, links {legacy.Links}/{current.Links}, " +
                $"beam properties {legacy.Properties}/{current.Properties}; {differences.Count} differences; import {import:F1} s, write {write:F1} s, total {watch.Elapsed.TotalSeconds:F1} s"
                + Environment.NewLine + St7Dump.Summary(differences);
            if (solve)
            {
                string legacyLsa = Path.ChangeExtension(Path.Combine(f1, c.Native), ".LSA");
                text += "  results (legacy F1 .LSA vs GPC writer, global axes, max |difference| / max |value| per quantity and case):" + Environment.NewLine
                    + St7Results.Compare(Path.Combine(f1, c.Native), legacyLsa, st7, lsa, 3, Path.Combine(folder, "results.tsv"));
            }
            File.WriteAllText(Path.Combine(folder, "summary.txt"), text);
            Console.WriteLine(text); summary.AppendLine(text);
            if (legacy.Nodes != current.Nodes || legacy.Beams != current.Beams) failures++;
        }
        File.WriteAllText(Path.Combine(output, "summary.txt"), summary.ToString());
        return failures == 0 ? 0 : 1;
    }

    private static string After(string[] args, string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault();
}
#endif
