using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>
    /// P2CE (13/11/2026, phase 1): the analysis and the export of a model given by its GPC input alone, moved out of the FEM seam
    /// (<c>FemmAnalysisSeam</c>) with the same code: a private copy of the input is written by the GPC Converter with the 0.3.0 options, solved
    /// and read (the results catalog attached), in a private folder whose files then replace those at the output path. No FEM type.
    /// </summary>
    public static class GpcAnalysisSeam
    {
        /// <summary>Straus R3 Export of a model given by its GPC input alone: a copy of the input (its warnings added), the preflight, the write
        /// plan with the section labels, plate material names and plate properties of the 0.3.0 exporter (C2, P26-01/02/03); no solve. The
        /// former FemmAnalysisSeam.Export with a null FEM model.</summary>
        public static void Export(GpcModels.Conversion input, string outputPath, List<string> warnings = null)
        {
            warnings ??= new List<string>();
            var (gpc, report) = Input(input, warnings);
            var preflight = GpcPreflight.Require(gpc); warnings.AddRange(preflight.Warnings);
            var plan = GPC.Converter.Straus7.Straus7ModelWritePlan.Prepare(gpc, null, null, report.NativeSections, GpcStrausAnalysis.LegacyWriteOptions(gpc, report));
            InPrivateFolder(outputPath, (output, scratch) =>
            {
                using (var writer = new GPC.Converter.Straus7.Straus7NativeWriter(output, scratch)) { plan.Write(writer, gpc); writer.Save(); }
                return true;
            });
        }

        /// <summary>A private copy of the GPC input and its report, its warnings added (R6.5 P1 lane 3e: the input at the tolerance of its producer).</summary>
        private static (GPC.Model.Models.Model, LegacyImportReport) Input(GpcModels.Conversion input, List<string> warnings)
        {
            if (input == null) throw new ArgumentException("GPC-only input: the model has no GPC input.");
            warnings.AddRange(input.Report.Warnings);
            return (GPC.Model.Core.ModelValues.Copy(input.Model), input.Report);
        }

        /// <summary>R6.5 P1 lane 3e: the analysis of a model given by its GPC input alone: a copy of the input is written, solved and read by the
        /// Converter as on the FEM path, and the catalog of the requested results is attached to the analysed model with the units and the beam
        /// angles of the input's report. No FEM result object is written: the analysed model is the output.</summary>
        public static GPC.Model.Models.Model Run(GpcModels.Conversion input, string outputPath, int solver, IReadOnlyList<GpcResultSelection.Entry> selection, bool movingOnly, List<string> warnings)
        {
            var (gpc, report) = Input(input ?? throw new ArgumentNullException(nameof(input)), warnings);
            var solved = Solve(gpc, report, outputPath, solver, selection, movingOnly, warnings, out var entries, out _);
            GpcResultCatalog.Attach(solved, report.Units.Units, entries, report.BeamAngles);
            return solved;
        }
        /// <summary>The solution of a private copy of the input and the entries of the results catalog, common to the two paths (R6.4 and C4):
        /// the requested results in their order, or the generated cases of a moving-only analysis (<paramref name="generated"/>).</summary>
        internal static GPC.Model.Models.Model Solve(GPC.Model.Models.Model gpc, LegacyImportReport report, string outputPath, int solver, IReadOnlyList<GpcResultSelection.Entry> selection, bool movingOnly,
            List<string> warnings, out List<GpcResultCatalog.Entry> entries, out Dictionary<int, string> generated)
        {
            // Only the static cases generated from the paths are read by a moving-only analysis. C4 (point 2, user decision of 11/11: as 0.3.0,
            // StrausR3Helper.cs:1536 and StrausAnalysis.cs:56-59 of 0.3.0): an ordinary run (linear, or nonlinear increments without stages) writes
            // them in the file too and does not solve them (LinearOptions: SolveGeneratedMovingCases false); a staged run writes the paths without
            // them (GPC refuses generated cases with stages: GeneratedMovingCasesNeedExplicitStageFactors).
            if (gpc.MovingLoads != null)
                foreach (var path in gpc.MovingLoads.Paths) if (path.Options != null && !movingOnly && solver == 2) path.Options.ConvertToStaticCases = false;
            GpcStrausAnalysis.Outcome outcome;
            const string NonlinearCase = "BridGHe NLA results", UnstagedPhase = "BridGHe NLA";
            // C2 (user decision of 11/11: Straus7 as 0.3.0): the metadata of the 0.3.0 exporter (P26-01/02/03); the linear run solves only the
            // requested cases and the cases of the requested combinations, and only the generated cases in a moving-only run (P26-04,
            // StrausAnalysis.cs:34-58 of 0.3.0), reads the requested combinations from Straus7 (P26-07); every run keeps the solver settings
            // of the new file (P26-06: 0.3.0 set none).
            // C4 (point 3, user decision of 11/11: as 0.3.0): solver 1 solves the combinations as the increments of stage 0 with the staged flag
            // off (before: one explicit stage with every element active, staged flag on), in the order of the combinations (the write options).
            var writeOptions = GpcStrausAnalysis.LegacyWriteOptions(gpc, report);
            var linear = solver == 0 ? LinearOptions(gpc, selection, movingOnly) : null;
            if (solver == 1 && gpc.Stages.Count != 0) throw new InvalidOperationException("Solver 1 con fasi definite: usare il solutore per fasi.");
            if (solver != 0) gpc.LoadCases.Add(NonlinearCase, new GPC.Model.Stages.StageResultCase(NonlinearCase, "NLA"));
            var fileDefaults = GPC.Converter.Straus7.Straus7NonlinearRunOptions.OfFile();
            outcome = InPrivateFolder(outputPath, (output, scratch) => solver == 0
                ? GpcStrausAnalysis.LinearStatic(gpc, output, scratch, nativeSections: report.NativeSections, writeOptions: writeOptions, runOptions: linear)
                : solver == 1
                ? GpcStrausAnalysis.Unstaged(gpc, NonlinearCase, UnstagedPhase, output, scratch, nativeSections: report.NativeSections, writeOptions: writeOptions, nonlinear: fileDefaults)
                : GpcStrausAnalysis.Staged(gpc, NonlinearCase, output, scratch, nativeSections: report.NativeSections, writeOptions: writeOptions, nonlinear: fileDefaults));
            warnings.AddRange(outcome.Warnings);
            var solved = outcome.Model;
            // C4: the requested results in their order, kept in the analysed model (GpcResultCatalog); the FEM path writes the former result
            // objects from it with the same row functions as the Results components (GpcResultRows), in the definition units.
            entries = new List<GpcResultCatalog.Entry>(); generated = null;
            if (solver != 0)
            {
                // Former entries: solver 1 = the combinations as increments; solver 2 = "combination [stage]" per stage increment. Straus7 numbers
                // the increments across the stages ("<number>: <increment>"), as the former entries did.
                foreach (var g in selection)
                {
                    // R6.4 (1): the stage and the combination of a staged increment from the GPC catalogue (the stage increments of the input).
                    string phase = solver == 1 ? UnstagedPhase : g.Stage, step = solver == 1 ? g.Name : g.Combination;
                    if (phase == null) throw new InvalidOperationException("Indice di incremento senza fase: " + g.Increment);
                    step = g.Increment.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + step;
                    RequireNonlinear(solved, g.Name, NonlinearCase, phase, step);
                    entries.Add(new GpcResultCatalog.Entry { Label = g.Name, Case = NonlinearCase, Step = step, Phase = phase });
                }
            }
            else if (movingOnly)
            {
                generated = outcome.Run.GeneratedCases.ToDictionary(p => p.Key, p => p.Value);
                // A generated moving step carries the results of its parent moving case, with the native case name as step.
                string parent = generated.Count == 0 ? null
                    : solved.LoadCases.Values.Single(l => solved.MovingLoads.Paths.Select(p => p.CaseGuid).Distinct().Single() == l.Guid).Name;
                foreach (var g in generated.OrderBy(p => p.Key)) entries.Add(new GpcResultCatalog.Entry { Label = g.Value, Case = parent, Step = MovingStep(g.Key, g.Value) });
            }
            else foreach (var g in selection) entries.Add(new GpcResultCatalog.Entry { Label = g.Name, Case = g.Name });
            return solved;
        }
        /// <summary>C2 (P26-04, P26-07, as 0.3.0 StrausAnalysis.cs:30-62): a moving-only run solves the generated cases alone; any other linear run
        /// solves the requested load cases and the cases with a non-zero factor in the requested combinations, and reads the requested combinations
        /// from the combination cases Straus7 computes. The solver switches of the new file are kept (P26-06).</summary>
        private static GPC.Converter.Straus7.Straus7LinearRunOptions LinearOptions(GPC.Model.Models.Model gpc, IReadOnlyList<GpcResultSelection.Entry> selection, bool movingOnly)
        {
            if (movingOnly) return new GPC.Converter.Straus7.Straus7LinearRunOptions { LoadCases = Array.Empty<Guid>(), SolveGeneratedMovingCases = true, FileSolverDefaults = true };
            var cases = new List<Guid>(); var combinations = new List<Guid>();
            foreach (var entry in selection)
            {
                if (gpc.LoadCases.TryGetValue(entry.Name, out var loadCase)) { cases.Add(loadCase.Guid); continue; }
                var combination = gpc.Combinations[entry.Name]; combinations.Add(combination.Guid);
                foreach (var pair in combination.GetLoadCaseCoefficientsPair()) if (pair.Value != 0) cases.Add(pair.Key.Guid);
            }
            return new GPC.Converter.Straus7.Straus7LinearRunOptions { LoadCases = cases.Distinct().ToArray(), SolveGeneratedMovingCases = false, Combinations = combinations, FileSolverDefaults = true };
        }
        private static string MovingStep(int number, string name) => number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + name;
        private static void RequireNonlinear(GPC.Model.Models.Model solved, string caseName, string outputCase, string phase, string step)
        {
            if (solved.NodesElements.Values.Any(n => GpcResults.NodeDisplacement(n, outputCase, step, phase) != null)) return;
            var states = solved.NodesElements.Values.SelectMany(n => n.Results).SelectMany(r => r.Results).Select(l => l.Case?.Name + " | " + l.State?.Phase + " | " + l.State?.Step).Distinct();
            throw new InvalidOperationException("Risultati non lineari assenti per " + caseName + " (" + phase + " | " + step + "); stati: " + string.Join("; ", states));
        }
        /// <summary>The former components overwrote their output (St7NewFile and the solver result files); the Converter never overwrites.
        /// The run happens in a private temporary folder and its files (output stem, any extension) then replace those at the output path;
        /// other files of the output folder are left as they are.</summary>
        internal static T InPrivateFolder<T>(string outputPath, Func<string, string, T> run)
        {
            outputPath = Path.GetFullPath(outputPath); string target = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(target)) throw new DirectoryNotFoundException("Directory does not exist: " + target);
            string work = Path.Combine(Path.GetTempPath(), "bridghe-gpc-" + Guid.NewGuid().ToString("N"));
            string folder = Path.Combine(work, "output"), scratch = Path.Combine(work, "scratch");
            Directory.CreateDirectory(folder); Directory.CreateDirectory(scratch);
            try
            {
                var result = run(Path.Combine(folder, Path.GetFileName(outputPath)), scratch);
                foreach (var file in Directory.GetFiles(folder)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                return result;
            }
            finally { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
