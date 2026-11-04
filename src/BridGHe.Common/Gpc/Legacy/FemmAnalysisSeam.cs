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
        public static void Export(ModelModel model, string outputPath, double tolerance, List<string> warnings)
        {
            var report = new LegacyImportReport();
            var gpc = Import(model, outputPath, tolerance, report, warnings);
            var preflight = GpcPreflight.Require(gpc); warnings.AddRange(preflight.Warnings);
            var plan = GPC.Converter.Straus7.Straus7ModelWritePlan.Prepare(gpc, null, null, report.NativeSections);
            InPrivateFolder(outputPath, (output, scratch) =>
            {
                using (var writer = new GPC.Converter.Straus7.Straus7NativeWriter(output, scratch)) { plan.Write(writer, gpc); writer.Save(); }
                return true;
            });
        }

        private static GPC.Model.Models.Model Import(ModelModel model, string outputPath, double tolerance, LegacyImportReport report, List<string> warnings)
        {
            var owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "analysis:" + Path.GetFullPath(outputPath).ToUpperInvariant());
            var gpc = LegacyModelImport.Import(LegacyArchive.Read(ModelArchive.Write(model)), owner, tolerance, report);
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
            double tolerance, bool movingOnly, List<string> warnings)
        {
            var report = new LegacyImportReport();
            var gpc = Import(model, outputPath, tolerance, report, warnings);
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
            var solved = outcome.Model;
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
                    AttachNonlinear(model, solved, entry.Case, NonlinearCase, phase, step);
                }
                return model;
            }
            if (movingOnly)
            {
                model.GeneratedMovingCases = outcome.Run.GeneratedCases.ToDictionary(p => p.Key, p => p.Value);
                foreach (var generated in model.GeneratedMovingCases.OrderBy(p => p.Key))
                {
                    var c = new LoadCaseModel(generated.Key.ToString(), generated.Value); model.LoadCases.Add(c);
                    Attach(model, solved, c, MovingStep(generated.Key, generated.Value));
                }
                return model;
            }
            foreach (var entry in selection) Attach(model, solved, entry.Case, null);
            return model;
        }

        private static string MovingStep(int number, string name) => number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + name;

        /// <summary>Former result objects of one case (load case, combination by superposition, or generated moving step).</summary>
        private static void Attach(ModelModel model, GPC.Model.Models.Model solved, CaseModel caseModel, string movingStep)
        {
            // A generated moving step carries the results of its parent moving case, with the native case name as step.
            string name = movingStep == null ? caseModel.Name
                : solved.LoadCases.Values.Single(l => solved.MovingLoads.Paths.Select(p => p.CaseGuid).Distinct().Single() == l.Guid).Name;
            bool combination = movingStep == null && solved.Combinations.ContainsKey(name);
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                var n = solved.NodesElements[i + 1];
                var d = combination ? GpcResults.Combined(solved, name, c => GpcResults.NodeDisplacement(n, c)) : GpcResults.NodeDisplacement(n, name, movingStep);
                var r = combination ? GpcResults.Combined(solved, name, c => GpcResults.NodeReaction(n, c)) : GpcResults.NodeReaction(n, name, movingStep);
                if (d != null) model.Nodes[i].Results.Add(new NodeDisplResultModel(caseModel) { Displacement = new R.Vector3d(d[0], d[1], d[2]), Rotation = new R.Vector3d(d[3], d[4], d[5]) });
                if (r != null) model.Nodes[i].Results.Add(new NodeReactResultModel(caseModel) { Force = new R.Vector3d(r[0], r[1], r[2]), Moment = new R.Vector3d(r[3], r[4], r[5]) });
            }
            for (int k = 0; k < model.Beams.Count; k++)
            {
                var beam = solved.BeamElements[k + 1]; var legacy = model.Beams[k];
                var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, legacy.AngleDeg);
                var rows = combination ? CombinedBeam(solved, beam, name, principal) : GpcResults.BeamForces(beam, name, principal, movingStep);
                double length = beam.Length;
                foreach (var row in rows)
                    legacy.Results.Add(new BeamForceResultModel(caseModel, row[0] * length, 0, "")
                    { AxialForce = row[1], ShearForce = new R.Vector2d(row[2], row[3]), Torque = row[4], BendingMoment = new R.Vector2d(row[5], row[6]) });
            }
            for (int k = 0; k < model.Plates.Count; k++)
            {
                var plate = solved.AreaElements[k + 1];
                var rows = combination ? CombinedPlate(solved, plate, name) : GpcResults.PlateNodeForces(plate, name, movingStep);
                foreach (var row in rows) model.Plates[k].Results.Add(PlateResult(caseModel, row.Node, row.Values));
            }
        }

        /// <summary>Former plate result at a plate node: local forces, transverse shear and moments as read; principal values and angle
        /// (degrees from local x) and the von Mises force of the in-plane tensor derived from them, as Straus7 combined them.</summary>
        private static PlateForceResultModel PlateResult(CaseModel caseModel, int node, double[] v)
        {
            var (fMax, fMin, fAngle) = Principal(v[0], v[1], v[2]); var (mMax, mMin, mAngle) = Principal(v[5], v[6], v[7]);
            return new PlateForceResultModel(caseModel, node.ToString(System.Globalization.CultureInfo.InvariantCulture), 0, "")
            {
                Force = new R.Vector3d(v[0], v[1], v[2]), ForceMax = fMax, ForceMin = fMin, ForceAngle = fAngle,
                ForceVonMises = Math.Sqrt(v[0] * v[0] - v[0] * v[1] + v[1] * v[1] + 3 * v[2] * v[2]),
                Moment = new R.Vector3d(v[5], v[6], v[7]), MomentMax = mMax, MomentMin = mMin, MomentAngle = mAngle,
                TransverseShearForce = new R.Vector2d(v[3], v[4]), TransverseShearForceMax = double.NaN, TransverseShearForceAng = double.NaN
            };
        }
        private static (double Max, double Min, double AngleDeg) Principal(double xx, double yy, double xy)
        {
            double c = (xx + yy) / 2, r = Math.Sqrt((xx - yy) * (xx - yy) / 4 + xy * xy);
            return (c + r, c - r, 0.5 * Math.Atan2(2 * xy, xx - yy) * 180 / Math.PI);
        }
        private static IReadOnlyList<(int Node, double[] Values)> CombinedPlate(GPC.Model.Models.Model solved, AreaElement plate, string name)
        {
            var pairs = solved.Combinations[name].GetLoadCaseCoefficientsPair();
            var perCase = pairs.Select(p => (p.Value, Rows: GpcResults.PlateNodeForces(plate, p.Key.Name).ToDictionary(r => r.Node, r => r.Values))).ToArray();
            if (perCase.Length == 0) return Array.Empty<(int, double[])>();
            return plate.Nodes.Where(n => perCase.All(c => c.Rows.ContainsKey(n.Id))).Select(n =>
            {
                var sum = new double[8];
                foreach (var c in perCase) for (int k = 0; k < 8; k++) sum[k] += c.Value * c.Rows[n.Id][k];
                return (n.Id, sum);
            }).ToArray();
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

        private static void AttachNonlinear(ModelModel model, GPC.Model.Models.Model solved, CaseModel caseModel, string outputCase, string phase, string step)
        {
            if (!solved.NodesElements.Values.Any(n => GpcResults.NodeDisplacement(n, outputCase, step, phase) != null))
            {
                var states = solved.NodesElements.Values.SelectMany(n => n.Results).SelectMany(r => r.Results).Select(l => l.Case?.Name + " | " + l.State?.Phase + " | " + l.State?.Step).Distinct();
                throw new InvalidOperationException("Risultati non lineari assenti per " + caseModel.Name + " (" + phase + " | " + step + "); stati: " + string.Join("; ", states));
            }
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                var n = solved.NodesElements[i + 1];
                var d = GpcResults.NodeDisplacement(n, outputCase, step, phase); var r = GpcResults.NodeReaction(n, outputCase, step, phase);
                if (d != null) model.Nodes[i].Results.Add(new NodeDisplResultModel(caseModel) { Displacement = new R.Vector3d(d[0], d[1], d[2]), Rotation = new R.Vector3d(d[3], d[4], d[5]) });
                if (r != null) model.Nodes[i].Results.Add(new NodeReactResultModel(caseModel) { Force = new R.Vector3d(r[0], r[1], r[2]), Moment = new R.Vector3d(r[3], r[4], r[5]) });
            }
            for (int k = 0; k < model.Beams.Count; k++)
            {
                var beam = solved.BeamElements[k + 1]; var legacy = model.Beams[k];
                var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, legacy.AngleDeg); double length = beam.Length;
                foreach (var row in GpcResults.BeamForces(beam, outputCase, principal, step, phase))
                    legacy.Results.Add(new BeamForceResultModel(caseModel, row[0] * length, 0, "")
                    { AxialForce = row[1], ShearForce = new R.Vector2d(row[2], row[3]), Torque = row[4], BendingMoment = new R.Vector2d(row[5], row[6]) });
            }
            for (int k = 0; k < model.Plates.Count; k++)
                foreach (var row in GpcResults.PlateNodeForces(solved.AreaElements[k + 1], outputCase, step, phase))
                    model.Plates[k].Results.Add(PlateResult(caseModel, row.Node, row.Values));
        }

        /// <summary>Combination at the stations common to its cases (Straus7 adds result-dependent stations per case).</summary>
        private static IReadOnlyList<double[]> CombinedBeam(GPC.Model.Models.Model solved, BeamElement beam, string name, GPC.Geometry.CoordinateSystem principal)
        {
            var combination = solved.Combinations[name]; var pairs = combination.GetLoadCaseCoefficientsPair();
            var perCase = pairs.Select(p => (p.Value, Rows: GpcResults.BeamForces(beam, p.Key.Name, principal))).ToArray();
            if (perCase.Length == 0) return Array.Empty<double[]>();
            var stations = perCase[0].Rows.Select(r => r[0]).Where(s => perCase.All(c => c.Rows.Any(r => Math.Abs(r[0] - s) < 1e-9))).ToArray();
            return stations.Select(s =>
            {
                var sum = new double[7]; sum[0] = s;
                foreach (var c in perCase) { var row = c.Rows.First(r => Math.Abs(r[0] - s) < 1e-9); for (int k = 1; k < 7; k++) sum[k] += c.Value * row[k]; }
                return sum;
            }).ToArray();
        }
    }
}
