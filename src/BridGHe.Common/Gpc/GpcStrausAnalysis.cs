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

        public static Outcome LinearStatic(Model input, string outputPath, string scratchPath, string datasetId = "BridGHe linear static")
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var model = ModelValues.Copy(input);
            var preflight = GpcPreflight.Require(model);
            var plan = Straus7ModelWritePlan.Prepare(model);
            var run = plan.RunLinearStatic(model, System.IO.Path.GetFullPath(outputPath), System.IO.Path.GetFullPath(scratchPath));
            var report = run.ImportResults(model, datasetId);
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
        public static IEnumerable<T> Locations<T>(Element element, string caseName) where T : ResultLocation =>
            element.Results.SelectMany(r => r.Results).OfType<T>().Where(l => l.Case?.Name == caseName);

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
        public static double[] NodeDisplacement(NodeElement node, string caseName)
        {
            var d = Locations<NodeResultDisplacement>(node, caseName).SingleOrDefault()?.ResultDisplacement;
            return d == null ? null : new[] { d.D1, d.D2, d.D3, d.R1, d.R2, d.R3 };
        }

        /// <summary>Support reaction force and moment, global axes.</summary>
        public static double[] NodeReaction(NodeElement node, string caseName)
        {
            var f = Locations<NodeResultForces>(node, caseName).SingleOrDefault(l => l.Kind == NodalForceKind.SupportReaction)?.ResultBeamForces;
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

        /// <summary>Beam actions at the stations: (parametric station, N, SF1, SF2, torque, BM1, BM2) in the principal
        /// axes of the former export (beam frame rotated by its section's principal angle), ordered by station.</summary>
        public static IReadOnlyList<double[]> BeamForces(BeamElement beam, string caseName, GPC.Geometry.CoordinateSystem principal)
        {
            var result = new List<double[]>();
            foreach (var s in Locations<StationResultBeamForces>(beam, caseName).OrderBy(s => s.ParametricDistance))
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
