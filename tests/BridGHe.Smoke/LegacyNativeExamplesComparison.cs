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
