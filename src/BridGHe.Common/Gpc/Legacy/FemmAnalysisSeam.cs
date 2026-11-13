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
    /// <summary>P2CE (13/11/2026, phase 1): the GPC-only paths (Export and the analysis of a model given by its GPC input) moved to
    /// <see cref="GpcAnalysisSeam"/>; what is left here is the FEM path, deleted with the FEM models in phase 2.
    /// TRANSITIONAL SEAM (F6A, removed when the GH data types carry GPC objects): the former FEM model of the components is
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
            GpcAnalysisSeam.InPrivateFolder(outputPath, (output, scratch) =>
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
            var solved = GpcAnalysisSeam.Solve(gpc, report, outputPath, solver, gpcSelection, movingOnly, warnings, out var entries, out var generated); analysed = solved;
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

    }
}
