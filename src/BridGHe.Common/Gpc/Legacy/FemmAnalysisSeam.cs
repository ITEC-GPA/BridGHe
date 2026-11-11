using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Model.Elements;
using R = Rhino.Geometry;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>TRANSITIONAL SEAM (F6A, removed when the GH data types carry GPC objects): the former FEM model of the components is
    /// converted to GPC with the legacy reader (same semantics as the former exporter, verified on examples 50/99, 01-10 and 77
    /// captured models), analysed by the GPC Converter, and the GPC results are written back as the former result objects with
    /// the former conventions (GpcResults). The former Straus7 exporter and result reader are no longer used by the plug-in.</summary>
    public static class FemmAnalysisSeam
    {
        /// <summary>Former export component: the FEM model written as a Straus7 model (no solve) by the GPC Converter writer.</summary>
        public static void Export(ModelModel model, string outputPath, double tolerance, List<string> warnings) => Export(model, null, outputPath, tolerance, warnings);

        /// <param name="conversion">C3: the conversion the GH model keeps (same FEM model, same tolerance), copied; null converts the FEM model.</param>
        public static void Export(ModelModel model, GpcModels.Conversion conversion, string outputPath, double tolerance, List<string> warnings)
        {
            var (gpc, report) = Input(model, conversion, outputPath, tolerance, warnings);
            var preflight = GpcPreflight.Require(gpc); warnings.AddRange(preflight.Warnings);
            // C2 (P26-01/02/03): the section labels, plate material names and plate properties of the 0.3.0 exporter.
            var plan = GPC.Converter.Straus7.Straus7ModelWritePlan.Prepare(gpc, null, null, report.NativeSections, GpcStrausAnalysis.LegacyWriteOptions(gpc, report));
            InPrivateFolder(outputPath, (output, scratch) =>
            {
                using (var writer = new GPC.Converter.Straus7.Straus7NativeWriter(output, scratch)) { plan.Write(writer, gpc); writer.Save(); }
                return true;
            });
        }

        /// <summary>C3: a private copy of the conversion of the GH model when given (no second conversion), otherwise the conversion of the FEM model.</summary>
        private static (GPC.Model.Models.Model, LegacyImportReport) Input(ModelModel model, GpcModels.Conversion conversion, string outputPath, double tolerance, List<string> warnings)
        {
            // R6.5 P1 lane 3e: a model without FEM value is its GPC input, at the tolerance of its producer (no conversion to make).
            if (model == null && conversion == null) throw new ArgumentException("GPC-only input: the model has no GPC input.");
            if (conversion == null || (model != null && conversion.Tolerance != tolerance)) { var report = new LegacyImportReport(); return (Import(model, outputPath, tolerance, report, warnings), report); }
            warnings.AddRange(conversion.Report.Warnings);
            return (GPC.Model.Core.ModelValues.Copy(conversion.Model), conversion.Report);
        }

        private static GPC.Model.Models.Model Import(ModelModel model, string outputPath, double tolerance, LegacyImportReport report, List<string> warnings)
        {
            var owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "analysis:" + Path.GetFullPath(outputPath).ToUpperInvariant());
            var gpc = LegacyModelImport.Import(LegacyArchive.Read(ModelArchive.Write(model)), owner, tolerance, report, GpcModels.SectionStateSignatures(model));
            warnings.AddRange(report.Warnings);
            return gpc;
        }

        /// <summary>The former components overwrote their output (St7NewFile and the solver result files); the Converter never overwrites.
        /// The run happens in a private temporary folder and its files (output stem, any extension) then replace those at the output path;
        /// other files of the output folder are left as they are.</summary>
        private static T InPrivateFolder<T>(string outputPath, Func<string, string, T> run)
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

        public static ModelModel Run(ModelModel model, string outputPath, int solver, IReadOnlyCollection<Straus.StrausResultSelection.Entry> selection,
            double tolerance, bool movingOnly, List<string> warnings) => Run(model, null, outputPath, solver, selection, tolerance, movingOnly, warnings, out _);

        /// <param name="conversion">C3: the conversion the GH model keeps for this FEM model (same tolerance), copied; null converts the FEM model.</param>
        /// <param name="analysed">The analysed GPC model (its results as GPC datasets), kept by the GH model of the output (C3).</param>
        public static ModelModel Run(ModelModel model, GpcModels.Conversion conversion, string outputPath, int solver, IReadOnlyCollection<Straus.StrausResultSelection.Entry> selection,
            double tolerance, bool movingOnly, List<string> warnings, out GPC.Model.Models.Model analysed)
        {
            var (gpc, report) = Input(model, conversion, outputPath, tolerance, warnings);
            // R6.4 (1): the results asked of the solver come from the GPC catalogue of the input (GpcResultSelection), before the input is
            // prepared for the solver; until R6.5 the former catalogue of the FEM model gives the former result objects, and the two must agree.
            var gpcSelection = movingOnly ? Array.Empty<GpcResultSelection.Entry>() : GpcSelection(gpc, model, solver, selection);
            var solved = Solve(gpc, report, outputPath, solver, gpcSelection, movingOnly, warnings, out var entries, out var generated); analysed = solved;
            // The former result objects: the cases of the requested results in their order, the generated cases of a moving-only analysis.
            var caseModels = new List<CaseModel>();
            if (movingOnly)
            {
                model.GeneratedMovingCases = generated;
                foreach (var g in generated.OrderBy(p => p.Key)) { var c = new LoadCaseModel(g.Key.ToString(), g.Value); model.LoadCases.Add(c); caseModels.Add(c); }
            }
            else caseModels.AddRange(selection.Select(e => e.Case));
            var units = BridgeUnits.Parse(model.Units.ToString());
            var catalog = GpcResultCatalog.Attach(solved, units, entries, Enumerable.Range(0, model.Beams.Count).ToDictionary(k => k + 1, k => model.Beams[k].AngleDeg));
            WriteBack(model, solved, catalog, caseModels);
            return model;
        }

        /// <summary>R6.5 P1 lane 3e: the analysis of a model given by its GPC input alone: a copy of the input is written, solved and read by the
        /// Converter as on the FEM path, and the catalog of the requested results is attached to the analysed model with the units and the beam
        /// angles of the input's report. No FEM result object is written: the analysed model is the output.</summary>
        public static GPC.Model.Models.Model Run(GpcModels.Conversion input, string outputPath, int solver, IReadOnlyList<GpcResultSelection.Entry> selection, bool movingOnly, List<string> warnings)
        {
            var (gpc, report) = Input(null, input ?? throw new ArgumentNullException(nameof(input)), outputPath, input.Tolerance, warnings);
            var solved = Solve(gpc, report, outputPath, solver, selection, movingOnly, warnings, out var entries, out _);
            GpcResultCatalog.Attach(solved, report.Units.Units, entries, report.BeamAngles);
            return solved;
        }

        /// <summary>The solution of a private copy of the input and the entries of the results catalog, common to the two paths (R6.4 and C4):
        /// the requested results in their order, or the generated cases of a moving-only analysis (<paramref name="generated"/>).</summary>
        private static GPC.Model.Models.Model Solve(GPC.Model.Models.Model gpc, LegacyImportReport report, string outputPath, int solver, IReadOnlyList<GpcResultSelection.Entry> selection, bool movingOnly,
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

        /// <summary>R6.4 (1): the selection of the GPC catalogue of the input (<see cref="GpcResultSelection"/>) for the former selection on the FEM
        /// model; until R6.5 the two whole catalogues must be equal, entry by entry (increment and name), on every analysis.</summary>
        private static GpcResultSelection.Entry[] GpcSelection(GPC.Model.Models.Model gpc, ModelModel model, int solver, IReadOnlyCollection<Straus.StrausResultSelection.Entry> selection)
        {
            var fem = Straus.StrausResultSelection.Catalog(model, solver);
            var catalog = GpcResultSelection.Catalog(gpc, solver);
            for (int i = 0; i < Math.Max(fem.Count, catalog.Count); i++)
                if (i >= fem.Count || i >= catalog.Count || fem[i].Increment != catalog[i].Increment || fem[i].Case.Name != catalog[i].Name)
                    throw new InvalidOperationException("GpcResultSelectionMismatch: result " + (i + 1) + ", FEM '" + (i < fem.Count ? fem[i].Increment + " " + fem[i].Case.Name : "-")
                        + "', GPC '" + (i < catalog.Count ? catalog[i].Increment + " " + catalog[i].Name : "-") + "'");
            var byName = catalog.ToDictionary(e => e.Name, StringComparer.Ordinal);
            return selection.Select(e => byName[e.Case.Name]).ToArray();
        }

        private static string MovingStep(int number, string name) => number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + name;

        /// <summary>Former result objects of every element, from the rows of the Results components (same numbers bit for bit).</summary>
        private static void WriteBack(ModelModel model, GPC.Model.Models.Model solved, GpcResultCatalog catalog, IReadOnlyList<CaseModel> cases)
        {
            for (int i = 0; i < model.Nodes.Count; i++)
                foreach (var row in GpcResultRows.Node(catalog, solved, solved.NodesElements[i + 1]))
                {
                    var c = cases[row.Entry]; var d = row.Displacement; var r = row.Reaction;
                    if (d != null) model.Nodes[i].Results.Add(new NodeDisplResultModel(c) { Displacement = new R.Vector3d(d[0], d[1], d[2]), Rotation = new R.Vector3d(d[3], d[4], d[5]) });
                    if (r != null) model.Nodes[i].Results.Add(new NodeReactResultModel(c) { Force = new R.Vector3d(r[0], r[1], r[2]), Moment = new R.Vector3d(r[3], r[4], r[5]) });
                }
            for (int k = 0; k < model.Beams.Count; k++)
                foreach (var row in GpcResultRows.Beam(catalog, solved, solved.BeamElements[k + 1]))
                    model.Beams[k].Results.Add(new BeamForceResultModel(cases[row.Entry], row.Station, 0, "")
                    { AxialForce = row.Axial, ShearForce = new R.Vector2d(row.Shear1, row.Shear2), Torque = row.Torque, BendingMoment = new R.Vector2d(row.Bending1, row.Bending2) });
            for (int k = 0; k < model.Plates.Count; k++)
                foreach (var row in GpcResultRows.Plate(catalog, solved, solved.AreaElements[k + 1]))
                {
                    var v = row.Values;
                    model.Plates[k].Results.Add(new PlateForceResultModel(cases[row.Entry], row.Node, 0, "")
                    {
                        Force = new R.Vector3d(v[0], v[1], v[2]), ForceMax = row.ForceMax, ForceMin = row.ForceMin, ForceAngle = row.ForceAngle, ForceVonMises = row.ForceVonMises,
                        Moment = new R.Vector3d(v[5], v[6], v[7]), MomentMax = row.MomentMax, MomentMin = row.MomentMin, MomentAngle = row.MomentAngle,
                        TransverseShearForce = new R.Vector2d(v[3], v[4]), TransverseShearForceMax = double.NaN, TransverseShearForceAng = double.NaN
                    });
                }
        }

        private static void RequireNonlinear(GPC.Model.Models.Model solved, string caseName, string outputCase, string phase, string step)
        {
            if (solved.NodesElements.Values.Any(n => GpcResults.NodeDisplacement(n, outputCase, step, phase) != null)) return;
            var states = solved.NodesElements.Values.SelectMany(n => n.Results).SelectMany(r => r.Results).Select(l => l.Case?.Name + " | " + l.State?.Phase + " | " + l.State?.Step).Distinct();
            throw new InvalidOperationException("Risultati non lineari assenti per " + caseName + " (" + phase + " | " + step + "); stati: " + string.Join("; ", states));
        }
    }
}
