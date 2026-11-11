using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Converter;
using GPC.Converter.Straus7;
using GPC.Model.Analysis;
using GPC.Model.Core;
using GPC.Model.Elements;
using GPC.Model.Models;
using GPC.Model.Results.Locations;
using GPC.Model.Results.State;
using GPC.Model.Results.Storage;

namespace BridGHe.Common.Gpc
{
    /// <summary>Straus7 analysis of a GPC model through the GPC Converter (writer, solver run, verified result import). The input is
    /// never modified: the analysis works on a contractual copy, which carries the results. Domain preflight first.</summary>
    public static class GpcStrausAnalysis
    {
        public sealed class Outcome
        {
            public Model Model { get; internal set; }
            public Straus7ModelRun Run { get; internal set; }
            public ResultImportReport Import { get; internal set; }
            public IReadOnlyList<string> Warnings { get; internal set; }
        }

        /// <param name="nativeSections">Resolved native Straus7 sections by beam GUID (<see cref="Legacy.LegacyImportReport.NativeSections"/>), or null.</param>
        /// <param name="writeOptions">C2 (P26-01/02/03): the metadata of the 0.3.0 exporter (<see cref="Straus7WriteOptions"/>); null: the file of the Converter.</param>
        /// <param name="runOptions">C2 (P26-04/06/07): the cases solved, the combinations read from Straus7, the solver settings of the file
        /// (<see cref="Straus7LinearRunOptions"/>); null: every case solved, the combinations left to the superposition of the rows.</param>
        public static Outcome LinearStatic(Model input, string outputPath, string scratchPath, string datasetId = "BridGHe linear static",
            IReadOnlyDictionary<Guid, Straus7SectionDefinition> nativeSections = null, Straus7WriteOptions writeOptions = null, Straus7LinearRunOptions runOptions = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var model = ModelValues.Copy(input);
            var preflight = GpcPreflight.Require(model);
            var plan = Straus7ModelWritePlan.Prepare(model, null, null, nativeSections, writeOptions);
            var run = plan.RunLinearStatic(model, System.IO.Path.GetFullPath(outputPath), System.IO.Path.GetFullPath(scratchPath), Resolve(runOptions, input, model));
            // Plate results at the plate nodes (no averaging), as the former reader returned them.
            var report = run.ImportResults(model, datasetId, false, model.AreaElements.Count != 0);
            if (report.Status != ImportStatus.Completed)
                throw new InvalidOperationException("StrausResultImport" + report.Status + ": " + string.Join("; ", report.Diagnostics.Select(d => d.Code + " " + d.Message)));
            return new Outcome { Model = model, Run = run, Import = report, Warnings = preflight.Warnings.Concat(report.Diagnostics.Select(d => d.Code + " " + d.Message)).ToArray() };
        }

        /// <summary>
        /// C2 (P26-01/02/03, user decision of 11/11: as 0.3.0): the metadata the 0.3.0 exporter wrote (StrausR3Helper.cs:216-279, :378-428): the
        /// section label of the eight standard kinds it labelled (SolidRound, HollowRound, SolidRect, HollowRect, IBeam, TBeam, Lipped Channel, Angle;
        /// not a native section of the Native Section component, a Brep, Generic or Composite section), the material name of every plate property,
        /// one native plate property per Plate Property, named as it. The kind comes from the recipe of the beam property in the report; a property
        /// built in GPC without a recipe takes the label of its GPC shape (the five shapes the writer writes with St7SetBeamSectionGeometry).
        /// C4 (user decision of 11/11: as 0.3.0): the supports, temperature gradients and tapers of a beam with a principal angle (an angle, a
        /// double angle) on the native axes 1 and 2 as 0.3.0 wrote them (StrausR3Helper.cs:956-1020, :1225-1230, BeamSupportExport), and the
        /// nonlinear increments of a model without stages in the order of its combinations (StrausR3Helper.cs:666-709).
        /// </summary>
        public static Straus7WriteOptions LegacyWriteOptions(Model gpc, Legacy.LegacyImportReport report)
        {
            if (gpc == null) throw new ArgumentNullException(nameof(gpc));
            var labels = new Dictionary<Guid, string>();
            foreach (var beam in gpc.BeamElements.Values)
            {
                Legacy.LegacyBeamPropertyData data = null;
                if (beam.BeamProperty?.Name != null) report?.BeamProperties.TryGetValue(beam.BeamProperty.Name, out data);
                string label = data?.Section != null ? (data.Section.NativeKey != null ? null : SectionLabel(data.Section.Kind)) : ShapeLabel(beam.BeamProperty);
                if (label != null) labels.Add(beam.Guid, label);
            }
            return new Straus7WriteOptions { BeamSectionLabels = labels, PlateMaterialNames = true, PlatePropertiesByProperty = true,
                SectionAxisDataOnNativeAxes = true, NonlinearIncrementsInModelOrder = true };
        }

        /// <summary>The label 0.3.0 wrote for a section kind (null: none).</summary>
        public static string SectionLabel(BridgeSectionKind kind) => kind switch
        {
            BridgeSectionKind.SolidCircle => "SolidRound", BridgeSectionKind.HollowCircle => "HollowRound", BridgeSectionKind.SolidRectangle => "SolidRect",
            BridgeSectionKind.HollowRectangle => "HollowRect", BridgeSectionKind.I => "IBeam", BridgeSectionKind.T => "TBeam", BridgeSectionKind.C => "Lipped Channel",
            BridgeSectionKind.Angle => "Angle", _ => null,
        };

        private static string ShapeLabel(GPC.Model.ElementProperties.BeamProperty property)
        {
            var section = (property as GPC.Model.ElementProperties.ElasticBeamProperty)?.Section ?? (property as GPC.Model.Sections.Steel.SteelSection)?.SectionShape;
            return section switch
            {
                GPC.Model.Sections.SectionCircular => "SolidRound", GPC.Model.Sections.SectionCHS => "HollowRound", GPC.Model.Sections.SectionRectangular => "SolidRect",
                GPC.Model.Sections.SectionRHS => "HollowRect", GPC.Model.Sections.SectionH => "IBeam", _ => null,
            };
        }

        /// <summary>C2: the run options for the analysed copy: the cases and combinations of <paramref name="input"/> by name in <paramref name="copy"/>.</summary>
        private static Straus7LinearRunOptions Resolve(Straus7LinearRunOptions options, Model input, Model copy)
        {
            if (options == null) return null;
            string CaseName(Guid g) => input.LoadCases.Values.Single(c => c.Guid == g).Name;
            string CombinationName(Guid g) => input.Combinations.Values.Single(c => c.Guid == g).Name;
            return new Straus7LinearRunOptions
            {
                LoadCases = options.LoadCases?.Select(g => copy.LoadCases[CaseName(g)].Guid).ToArray(),
                SolveGeneratedMovingCases = options.SolveGeneratedMovingCases,
                Combinations = options.Combinations?.Select(g => copy.Combinations[CombinationName(g)].Guid).ToArray(),
                FileSolverDefaults = options.FileSolverDefaults,
            };
        }

        /// <summary>Staged nonlinear static analysis (small displacements); the totals of every stage increment are attached to
        /// the declared output case, with the stage as Phase and the increment name as Step.</summary>
        /// <param name="nonlinear">C2 (P26-06): the solver settings (<see cref="Straus7NonlinearRunOptions"/>); null: those of the Converter
        /// (small displacements, linear material, tolerances 1e-8, 100 iterations).</param>
        public static Outcome Staged(Model input, string outputCaseName, string outputPath, string scratchPath, string datasetId = "BridGHe staged nonlinear",
            IReadOnlyDictionary<Guid, Straus7SectionDefinition> nativeSections = null, Straus7WriteOptions writeOptions = null, Straus7NonlinearRunOptions nonlinear = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var model = ModelValues.Copy(input);
            var output = model.LoadCases.Values.Single(c => c.Name == outputCaseName);
            var preflight = GpcPreflight.Require(model);
            var plan = Straus7ModelWritePlan.Prepare(model, null, null, nativeSections, writeOptions);
            var run = plan.RunStagedNonlinear(model, output.Guid, System.IO.Path.GetFullPath(outputPath), System.IO.Path.GetFullPath(scratchPath), nonlinear ?? Straus7NonlinearRunOptions.Explicit());
            // Plate results at the plate nodes (no averaging), as the former reader returned them.
            var report = run.ImportResults(model, datasetId, false, model.AreaElements.Count != 0);
            if (report.Status != ImportStatus.Completed)
                throw new InvalidOperationException("StrausResultImport" + report.Status + ": " + string.Join("; ", report.Diagnostics.Select(d => d.Code + " " + d.Message)));
            return new Outcome { Model = model, Run = run, Import = report, Warnings = preflight.Warnings.Concat(report.Diagnostics.Select(d => d.Code + " " + d.Message)).ToArray() };
        }

        /// <summary>C4 (point 3, user decision of 11/11: as 0.3.0): the nonlinear increments of a model without stages (solver 1) solved as 0.3.0
        /// solved them (StrausAnalysis.cs:64-77 of 0.3.0): every combination is an increment of stage 0, the NLA solver runs with the staged flag
        /// off. The totals of every increment are attached to the declared output case with Phase = <paramref name="phase"/> and Step = the native
        /// increment ("k: name"), as the one-stage run they replace. The increments follow the order of the write options
        /// (<see cref="Straus7WriteOptions.NonlinearIncrementsInModelOrder"/>).</summary>
        public static Outcome Unstaged(Model input, string outputCaseName, string phase, string outputPath, string scratchPath, string datasetId = "BridGHe staged nonlinear",
            IReadOnlyDictionary<Guid, Straus7SectionDefinition> nativeSections = null, Straus7WriteOptions writeOptions = null, Straus7NonlinearRunOptions nonlinear = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var model = ModelValues.Copy(input);
            var output = model.LoadCases.Values.Single(c => c.Name == outputCaseName);
            var preflight = GpcPreflight.Require(model);
            var plan = Straus7ModelWritePlan.Prepare(model, null, null, nativeSections, writeOptions);
            var run = plan.RunUnstagedNonlinear(model, output.Guid, System.IO.Path.GetFullPath(outputPath), System.IO.Path.GetFullPath(scratchPath), nonlinear ?? Straus7NonlinearRunOptions.Explicit(), phase);
            // Plate results at the plate nodes (no averaging), as the former reader returned them.
            var report = run.ImportResults(model, datasetId, false, model.AreaElements.Count != 0);
            if (report.Status != ImportStatus.Completed)
                throw new InvalidOperationException("StrausResultImport" + report.Status + ": " + string.Join("; ", report.Diagnostics.Select(d => d.Code + " " + d.Message)));
            return new Outcome { Model = model, Run = run, Import = report, Warnings = preflight.Warnings.Concat(report.Diagnostics.Select(d => d.Code + " " + d.Message)).ToArray() };
        }
    }

    /// <summary>GPC results in the component values BridGHe has always shown: node displacements, rotations (rad) and reactions in global
    /// axes; beam actions at the native stations in the beam principal axes with the former Straus7 labels (axial force, shear 1/2,
    /// torque, bending 1/2). Values are N-mm; the components convert them to the definition units.</summary>
    public static class GpcResults
    {
        /// <param name="step">Null for the case itself; the native case name ("N: name") for a static case generated by a moving load.</param>
        public static IEnumerable<T> Locations<T>(Element element, string caseName, string step = null, string phase = null) where T : ResultLocation =>
            element.Results.SelectMany(r => r.Results).OfType<T>().Where(l => l.Case?.Name == caseName && (l.State?.Step ?? null) == step && (phase == null || l.State?.Phase == phase));

        /// <summary>Linear combination of primary-case values (GPC combinations of linear cases are superpositions): null when a
        /// primary value is missing. <paramref name="name"/> may also be a primary case, returned unchanged.</summary>
        public static double[] Combined(Model model, string name, Func<string, double[]> primary)
        {
            if (!model.Combinations.TryGetValue(name, out var combination)) return primary(name);
            double[] sum = null;
            foreach (var pair in combination.GetLoadCaseCoefficientsPair())
            {
                var v = primary(pair.Key.Name); if (v == null) return null;
                sum ??= new double[v.Length];
                for (int k = 0; k < v.Length; k++) sum[k] += pair.Value * v[k];
            }
            return sum;
        }

        /// <summary>Translations and rotations (rad), global axes.</summary>
        public static double[] NodeDisplacement(NodeElement node, string caseName, string step = null, string phase = null)
        {
            var d = Locations<NodeResultDisplacement>(node, caseName, step, phase).SingleOrDefault()?.ResultDisplacement;
            return d == null ? null : new[] { d.D1, d.D2, d.D3, d.R1, d.R2, d.R3 };
        }

        /// <summary>Support reaction force and moment, global axes.</summary>
        public static double[] NodeReaction(NodeElement node, string caseName, string step = null, string phase = null)
        {
            var f = Locations<NodeResultForces>(node, caseName, step, phase).SingleOrDefault(l => l.Kind == NodalForceKind.SupportReaction)?.ResultBeamForces;
            return f == null ? null : Global(f);
        }

        /// <summary>Global components {Fx, Fy, Fz, Mx, My, Mz} of a nodal action stored as ResultBeamForces in its coordinate system.</summary>
        internal static double[] Global(GPC.Model.Results.ResultBeamForces f)
        {
            var cs = f.CoordinateSystem ?? GPC.Geometry.CoordinateSystem.Global;
            // ResultBeamForces components: V1, V2 along axes 1, 2; N along axis 3; M1, M2 about 1, 2; T about 3.
            var force = cs.ToGlobal(new GPC.Geometry.Vector3d(f.V1, f.V2, f.N)); var moment = cs.ToGlobal(new GPC.Geometry.Vector3d(f.M1, f.M2, f.T));
            return new[] { force.X, force.Y, force.Z, moment.X, moment.Y, moment.Z };
        }

        /// <summary>Plate stress resultants at the plate nodes (no averaging), in the plate local axes, in the plate connectivity order:
        /// (node id, {Fxx, Fyy, Fxy, Fxz, Fyz, Mxx, Myy, Mxy}) per unit width, N-mm.</summary>
        public static IReadOnlyList<(int Node, double[] Values)> PlateNodeForces(AreaElement plate, string caseName, string step = null, string phase = null)
        {
            var rows = Locations<PointResultPlateForces>(plate, caseName, step, phase)
                .Where(l => l.PointKind == ShellResultPointKind.ElementNodeExtrapolated && l.SourceNodeId.HasValue).ToDictionary(l => l.SourceNodeId.Value);
            return plate.Nodes.Where(n => rows.ContainsKey(n.Id)).Select(n =>
            {
                var f = rows[n.Id].Forces;
                return (n.Id, new[] { f.Fxx, f.Fyy, f.Fxy, f.Fxz, f.Fyz, f.Mxx, f.Myy, f.Mxy });
            }).ToArray();
        }

        /// <summary>Beam actions at the stations: (parametric station, N, SF1, SF2, torque, BM1, BM2) in the principal
        /// axes of the former export (beam frame rotated by its section's principal angle), ordered by station.</summary>
        public static IReadOnlyList<double[]> BeamForces(BeamElement beam, string caseName, GPC.Geometry.CoordinateSystem principal, string step = null, string phase = null)
        {
            var result = new List<double[]>();
            foreach (var s in Locations<StationResultBeamForces>(beam, caseName, step, phase).OrderBy(s => s.ParametricDistance))
            {
                var f = s.ResultBeamForces; var cs = f.CoordinateSystem ?? beam.Assignments.SectionAxes;
                var shear = cs.ToGlobal(new GPC.Geometry.Vector3d(f.V1, f.V2, 0)); var bending = cs.ToGlobal(new GPC.Geometry.Vector3d(f.M1, f.M2, 0));
                double Dot(GPC.Geometry.Vector3d a, GPC.Geometry.Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
                // Former labels (St7GetBeamResultArray, stBeamPrincipal), verified on the F1 results of examples 50 and 99: the shear forces
                // are the opposite of the GPC V1/V2, BM1 = M2 and BM2 = -M1 (GPC M1/M2 about the principal axes 1/2); N and T unchanged.
                // (The F4 pilot mapping M1 = BM2, M2 = -BM1 had the opposite sign: it was never compared with a solver result.)
                result.Add(new[] { s.ParametricDistance, f.N, -Dot(shear, principal.V1), -Dot(shear, principal.V2), f.T, Dot(bending, principal.V2), -Dot(bending, principal.V1) });
            }
            return result;
        }
    }
}
