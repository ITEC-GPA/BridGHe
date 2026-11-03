#if GPC_PILOT
using System;
using System.IO;
using System.Linq;
using System.Text;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Helpers;
using BridGHe.Common.Straus;
using GPC.Converter.Straus7;

/// <summary>F6A gate on the analytic examples 01-10 (nodal loads, distributed loads, plate patch, moving load with static
/// conversion, nonlinear increments, stages). Two processes, because the former binding keeps St7api.dll loaded:
/// --legacy-examples-write &lt;folder&gt; writes the input archive and the .st7 of the former exporter;
/// --legacy-examples-compare &lt;folder&gt; converts the archive to GPC, writes it with the Converter and compares the files.</summary>
internal static class LegacyNativeExamplesComparison
{
    internal static int Write(string folder)
    {
        StrausRuntime.EnsureInitialized(); Directory.CreateDirectory(folder); int failures = 0;
        foreach (var example in NativeExamples.Create())
        {
            string dir = Path.Combine(folder, example.Id); Directory.CreateDirectory(dir); example.Model.Name = example.Title;
            File.WriteAllBytes(Path.Combine(dir, "input.bgmodel"), ModelArchive.Write(example.Model));
            bool ok = StrausR3Helper.CreateModelR3(ModelArchive.Clone(example.Model), Path.Combine(dir, "legacy.st7"), 1e-6, out _, out var warnings, out var errors);
            File.WriteAllLines(Path.Combine(dir, "legacy-export.txt"), warnings.Select(w => "WARNING " + w).Concat(errors.Select(e => "ERROR " + e)));
            Console.WriteLine($"LEGACY {example.Id}: {(ok ? "written" : "FAILED " + string.Join(" | ", errors))}"); if (!ok) failures++;
        }
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Folder of pairs captured from the former exporter (BRIDGHE_LEGACY_EXPORT_CAPTURE): stem.bgmodel, stem.st7, stem.txt
    /// (output path, tolerance). Each pair is converted, written and compared like the examples.</summary>
    internal static int CompareCapture(string folder)
    {
        var summary = new StringBuilder(); int failures = 0, passed = 0; var categories = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var input in Directory.GetFiles(folder, "*.bgmodel").OrderBy(f => f, StringComparer.Ordinal))
        {
            string stem = Path.ChangeExtension(input, null), id = Path.GetFileName(stem); var info = File.ReadAllLines(stem + ".txt");
            string text;
            try
            {
                var report = new LegacyImportReport();
                double tolerance = double.Parse(info[1], System.Globalization.CultureInfo.InvariantCulture);
                var model = LegacyModelImport.Import(LegacyArchive.Read(File.ReadAllBytes(input)), GpcIdentity.Derive(GpcIdentity.NameSpace, "capture:" + id), tolerance, report);
                GpcPreflight.Require(model);
                string st7 = stem + ".gpc.st7"; if (File.Exists(st7)) File.Delete(st7);
                var plan = Straus7ModelWritePlan.Prepare(model);
                using (var writer = new Straus7NativeWriter(st7, Path.GetDirectoryName(stem))) { plan.Write(writer, model); writer.Save(); }
                var differences = St7Dump.Compare(St7Dump.Read(stem + ".st7"), St7Dump.Read(st7));
                File.WriteAllLines(stem + ".differences.tsv", differences.Select(d => d.Key + "\t" + d.A + "\t" + d.B));
                var kinds = differences.Select(d => St7Dump.CategoryOf(d.Key)).Distinct().ToArray();
                foreach (var k in kinds) categories[k] = categories.TryGetValue(k, out int n) ? n + 1 : 1;
                text = $"== {id} ({Path.GetFileName(info[0])}): {differences.Count} differences" + Environment.NewLine + St7Dump.Summary(differences); passed++;
            }
            catch (Exception error) { failures++; text = $"== {id} ({Path.GetFileName(info[0])}): NOT CONVERTED {error.GetType().Name}: {error.Message}" + Environment.NewLine; }
            Console.Write(text); summary.Append(text);
        }
        string totals = $"CAPTURE {passed} compared, {failures} not converted; models with difference categories:" + Environment.NewLine
            + string.Join(Environment.NewLine, categories.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => $"{c.Value,5}  {c.Key}"));
        Console.WriteLine(totals); summary.AppendLine(totals);
        File.WriteAllText(Path.Combine(folder, "summary.txt"), summary.ToString());
        return 0;
    }

    internal static int Compare(string folder)
    {
        var summary = new StringBuilder(); int failures = 0;
        foreach (var dir in Directory.GetDirectories(folder).OrderBy(d => d, StringComparer.Ordinal))
        {
            string id = Path.GetFileName(dir); string text;
            try
            {
                var report = new LegacyImportReport();
                var model = LegacyModelImport.Import(LegacyArchive.Read(File.ReadAllBytes(Path.Combine(dir, "input.bgmodel"))), GpcIdentity.Derive(GpcIdentity.NameSpace, "example:" + id), 1e-6, report);
                var preflight = GpcPreflight.Require(model);
                string st7 = Path.Combine(dir, "gpc.st7"); if (File.Exists(st7)) throw new IOException("Output exists: " + st7);
                var plan = Straus7ModelWritePlan.Prepare(model);
                using (var writer = new Straus7NativeWriter(st7, dir)) { plan.Write(writer, model); writer.Save(); }
                var differences = St7Dump.Compare(St7Dump.Read(Path.Combine(dir, "legacy.st7")), St7Dump.Read(st7));
                File.WriteAllLines(Path.Combine(dir, "differences.tsv"), differences.Select(d => d.Key + "\t" + d.A + "\t" + d.B));
                File.WriteAllText(Path.Combine(dir, "import-report.txt"), string.Join(Environment.NewLine,
                    report.Warnings.Concat(preflight.Warnings).Select(w => "WARNING " + w).Concat(report.NotExported.Select(p => $"CARRIED {p.Value} × {p.Key}"))));
                text = $"== {id}: {differences.Count} differences" + Environment.NewLine + St7Dump.Summary(differences);
            }
            catch (Exception error) { failures++; text = $"== {id}: FAILED {error.GetType().Name}: {error.Message}" + Environment.NewLine; }
            Console.Write(text); summary.Append(text);
        }
        File.WriteAllText(Path.Combine(folder, "summary.txt"), summary.ToString());
        return failures == 0 ? 0 : 1;
    }
}
#endif
