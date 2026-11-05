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
            var plan = GPC.Converter.Straus7.Straus7ModelWritePlan.Prepare(gpc, null, null, report.NativeSections);
            InPrivateFolder(outputPath, (output, scratch) =>
            {
                using (var writer = new GPC.Converter.Straus7.Straus7NativeWriter(output, scratch)) { plan.Write(writer, gpc); writer.Save(); }
                return true;
            });
        }

        /// <summary>C3: a private copy of the conversion of the GH model when given (no second conversion), otherwise the conversion of the FEM model.</summary>
        private static (GPC.Model.Models.Model, LegacyImportReport) Input(ModelModel model, GpcModels.Conversion conversion, string outputPath, double tolerance, List<string> warnings)
        {
            if (conversion == null || conversion.Tolerance != tolerance) { var report = new LegacyImportReport(); return (Import(model, outputPath, tolerance, report, warnings), report); }
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
            // Only the static cases generated from the paths are read by a moving-only analysis; otherwise the generated cases are not.
            if (gpc.MovingLoads != null)
                foreach (var path in gpc.MovingLoads.Paths) if (path.Options != null && !movingOnly) path.Options.ConvertToStaticCases = false;
            GpcStrausAnalysis.Outcome outcome;
            const string NonlinearCase = "BridGHe NLA results", UnstagedPhase = "BridGHe NLA";
            if (solver == 1) Unstaged(gpc, UnstagedPhase);
            if (solver != 0) gpc.LoadCases.Add(NonlinearCase, new GPC.Model.Stages.StageResultCase(NonlinearCase, "NLA"));
            outcome = InPrivateFolder(outputPath, (output, scratch) => solver == 0
                ? GpcStrausAnalysis.LinearStatic(gpc, output, scratch, nativeSections: report.NativeSections)
                : GpcStrausAnalysis.Staged(gpc, NonlinearCase, output, scratch, nativeSections: report.NativeSections));
            warnings.AddRange(outcome.Warnings);
            var solved = outcome.Model; analysed = solved;
            // C4: the requested results in their order, kept in the analysed model (GpcResultCatalog), and the former result objects written
            // from it with the same row functions as the Results components (GpcResultRows), in the definition units.
            var entries = new List<GpcResultCatalog.Entry>(); var caseModels = new List<CaseModel>();
            if (solver != 0)
            {
                // Former entries: solver 1 = the combinations as increments; solver 2 = "combination [stage]" per stage increment. Straus7 numbers
                // the increments across the stages ("<number>: <increment>"), as the former entries did.
                foreach (var entry in selection)
                {
                    string phase = null, step = null;
                    if (solver == 1) { phase = UnstagedPhase; step = entry.Case.Name; }
                    else
                    {
                        int count = 0;
                        foreach (var stage in model.StagedConstruction) foreach (var c in stage.LoadCombinations) if (++count == entry.Increment) { phase = stage.Name; step = c.Name; }
                        if (phase == null) throw new InvalidOperationException("Indice di incremento senza fase: " + entry.Increment);
                    }
                    step = entry.Increment.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + step;
                    RequireNonlinear(solved, entry.Case, NonlinearCase, phase, step);
                    entries.Add(new GpcResultCatalog.Entry { Label = entry.Case.Name, Case = NonlinearCase, Step = step, Phase = phase }); caseModels.Add(entry.Case);
                }
            }
            else if (movingOnly)
            {
                model.GeneratedMovingCases = outcome.Run.GeneratedCases.ToDictionary(p => p.Key, p => p.Value);
                // A generated moving step carries the results of its parent moving case, with the native case name as step.
                string parent = model.GeneratedMovingCases.Count == 0 ? null
                    : solved.LoadCases.Values.Single(l => solved.MovingLoads.Paths.Select(p => p.CaseGuid).Distinct().Single() == l.Guid).Name;
                foreach (var generated in model.GeneratedMovingCases.OrderBy(p => p.Key))
                {
                    var c = new LoadCaseModel(generated.Key.ToString(), generated.Value); model.LoadCases.Add(c);
                    entries.Add(new GpcResultCatalog.Entry { Label = c.Name, Case = parent, Step = MovingStep(generated.Key, generated.Value) }); caseModels.Add(c);
                }
            }
            else
                foreach (var entry in selection) { entries.Add(new GpcResultCatalog.Entry { Label = entry.Case.Name, Case = entry.Case.Name }); caseModels.Add(entry.Case); }
            var units = BridgeUnits.Parse(model.Units.ToString());
            var catalog = GpcResultCatalog.Attach(solved, units, entries, Enumerable.Range(0, model.Beams.Count).ToDictionary(k => k + 1, k => model.Beams[k].AngleDeg));
            WriteBack(model, solved, catalog, caseModels);
            return model;
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

        /// <summary>Solver 1 (nonlinear increments without stages) as one explicit stage with every element active and the combinations as
        /// increments, mechanically the same analysis; ungrouped beams and plates get a group of the stage (the native groups gain it).</summary>
        private static void Unstaged(GPC.Model.Models.Model gpc, string name)
        {
            if (gpc.Stages.Count != 0) throw new InvalidOperationException("Solver 1 con fasi definite: usare il solutore per fasi.");
            if (gpc.FreedomCases.Count == 0)
            { var fc = new GPC.Model.FreedomCases.FreedomCase(GpcIdentity.FreedomCase("Default restraints"), "Default restraints"); gpc.FreedomCases.Add(fc.Name, fc); }
            var ungrouped = gpc.BeamElements.Values.Cast<Element>().Concat(gpc.AreaElements.Values).Where(e => e.Groups.Count == 0).ToArray();
            if (ungrouped.Length != 0)
            {
                var group = GPC.Model.Attributes.Group.Create(GpcIdentity.Group(name), name); gpc.Groups.Add(group.Name, group);
                foreach (var e in ungrouped) e.AddGroup(group);
            }
            var stage = new GPC.Model.Stages.Stage(GpcIdentity.Stage(name), 1, name) { Definition = new GPC.Model.Stages.StageDefinition(1, 0) };
            stage.AnalysisState = new GPC.Model.Stages.StageAnalysisState
            {
                BirthGeometry = GPC.Model.Stages.StageBirthGeometry.OriginalCoordinates,
                ActiveGroupGuids = gpc.Groups.Values.Select(g => g.Guid).ToArray(), ActiveFreedomCaseGuids = gpc.FreedomCases.Values.Select(f => f.Guid).ToArray(),
                Increments = gpc.Combinations.Values.Select(c => new GPC.Model.Stages.StageLoadIncrement
                {
                    Name = c.Name,
                    LoadFactors = c.GetLoadCaseCoefficientsPair().Select(p => new GPC.Model.Stages.StageCaseFactor { CaseGuid = p.Key.Guid, Factor = p.Value }).ToArray(),
                }).ToArray(),
            };
            gpc.Stages.Add(stage);
            gpc.Combinations.Clear();
        }

        private static void RequireNonlinear(GPC.Model.Models.Model solved, CaseModel caseModel, string outputCase, string phase, string step)
        {
            if (solved.NodesElements.Values.Any(n => GpcResults.NodeDisplacement(n, outputCase, step, phase) != null)) return;
            var states = solved.NodesElements.Values.SelectMany(n => n.Results).SelectMany(r => r.Results).Select(l => l.Case?.Name + " | " + l.State?.Phase + " | " + l.State?.Step).Distinct();
            throw new InvalidOperationException("Risultati non lineari assenti per " + caseModel.Name + " (" + phase + " | " + step + "); stati: " + string.Join("; ", states));
        }
    }
}
