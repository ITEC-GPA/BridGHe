using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using GPC.Geometry;
using GPC.Model.Results;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// P2S (7/11), decision (i) = A, examples 11–19: the former path of Super Element Check («fasi BridGHe»: BridgeCheckAdapter on the FEM results
/// of the composite beams and the merged FEM model, the component of 3f8b8cd without its D3 branch), kept in the tests only as the reference of
/// the differences of the checks that move to the GPC workflow executor (D3) when Build BridGHe Model now builds the GPC bridge model from a
/// Build Model output whose beams come from an analysis. Retires with the FeMM models (R6.5).
/// <para>Compared per section result (same tree, same order): the stresses of points 5 and 8, the utilization of the stage and every check
/// (name, ratio, status), with the tolerances of the C5 comparison (1e-3 MPa, 1e-6); no status may change.</para>
/// </summary>
internal static class FemPhasesReference
{
    internal sealed record Comparison(int Results, int Identical, double MaxStress, double MaxUtilization, double MaxRatio, int Statuses, string Worst);

    /// <summary>The section results of the former path for the inputs of a Super Element Check (its model is the output of Build BridGHe Model).</summary>
    internal static BridgeStressResult[][][] Calculate(GH_Component check)
    {
        var modelGoo = check.Params.Input[0].VolatileData.AllData(true).Single();
        var merged = (ModelModel)modelGoo.ScriptVariable();
        var beams = check.Params.Input[1].VolatileData.AllData(true).ToArray();
        bool Flag(int i) => check.Params.Input[i].VolatileData.AllData(true).OfType<GH_Boolean>().Single().Value;
        bool web = Flag(2), top = Flag(3), bottom = Flag(4);
        var values = beams.Select(g => (CompositeBeamModel)g.ScriptVariable()).ToArray();
        var starts = beams.Select(g => (BridgeCompositeSectionDefinition)g.GetType().GetProperty("StartDefinition").GetValue(g)).ToArray();
        var ends = beams.Select(g => (BridgeCompositeSectionDefinition)g.GetType().GetProperty("EndDefinition").GetValue(g)).ToArray();
        var options = new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare, WebBuckling = web, TopFlangeBuckling = top, BottomFlangeBuckling = bottom };
        int n = values.Length;
        var checkers = new BridgeCheckAdapter[n][]; var psis = new (double Inf, double Inst, double Shr)[n][];
        var indexes = new BridgePhaseAssembler.ResultIndex[n]; var stations = new double[n][];
        var forces = new (ResultBeamForces[] Steel, ResultBeamForces[] Inf, ResultBeamForces[] Inst, ResultBeamForces[] Shr)[n][];
        var strains = new (double[] Inf, double[] Inst)[n][];
        double Phi(double ratio, GPC.Model.Materials.SteelMaterial s, GPC.Model.Materials.ConcreteMaterial c) => BridGHe.Common.Checks.BridgeCreepCoefficient.FromModularRatio(ratio, s.ElasticModulusTension, c.ElasticModulusCompression);
        for (int i = 0; i < n; i++)
        {
            var beam = values[i];
            var normalized = beam.Results.OfType<BeamForceResultModel>().Select(r => Math.Clamp(r.NormalizedLength ? r.Station : r.Station / beam.Length, 0, 1)).ToHashSet().OrderBy(v => v).ToList();
            checkers[i] = new BridgeCheckAdapter[normalized.Count]; psis[i] = new (double, double, double)[normalized.Count];
            bool constant = BridGHe.Common.Gpc.GpcCompositeSections.IsConstant(starts[i], ends[i]);
            for (int k = 0; k < normalized.Count; k++)
            {
                var definition = constant ? starts[i] : BridGHe.Common.Gpc.GpcCompositeSections.Definition(starts[i], ends[i], normalized[k]);
                var section = definition.Section; var steel = section.SteelSections[0].Section.SteelMaterial; var concrete = section.ConcreteMaterial;
                psis[i][k] = (Phi(definition.NLongTerm, steel, concrete), Phi(definition.NShortTerm, steel, concrete), Phi(definition.NShrinkage, steel, concrete));
                checkers[i][k] = new BridgeCheckAdapter(section, new CoordinateSystem(section.Centroid, new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0)), options);
            }
            var cs = new CoordinateSystem(Point3d.Origin, new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0));
            var station = beam.Results.OfType<BeamForceResultModel>().Select(r => Math.Round(r.NormalizedLength ? r.Station * beam.Length : r.Station, 15)).ToHashSet().OrderBy(v => v).ToList();
            var index = BridgePhaseAssembler.CreateIndex(beam, beam.Length, GeometryBase.Tolerance);
            indexes[i] = index; stations[i] = station.ToArray();
            forces[i] = new (ResultBeamForces[], ResultBeamForces[], ResultBeamForces[], ResultBeamForces[])[station.Count]; strains[i] = new (double[], double[])[station.Count];
            int m = merged.LoadCombinations.Count;
            for (int k = 0; k < station.Count; k++)
            {
                var f = (new ResultBeamForces[m], new ResultBeamForces[m], new ResultBeamForces[m], new ResultBeamForces[m]); var e = (new double[m], new double[m]);
                for (int c = 0; c < m; c++)
                {
                    var a = index.Assemble(station[k], merged.LoadCombinations[c], cs);
                    f.Item1[c] = a.Steel; f.Item2[c] = a.Infinite; f.Item3[c] = a.Instant; f.Item4[c] = a.Shrinkage; e.Item1[c] = a.StrainInfinite; e.Item2[c] = a.StrainInstant;
                }
                forces[i][k] = f; strains[i][k] = e;
            }
        }
        bool workflowPath = merged.BridgeWorkflow?.Method == BridGHe.Common.Checks.BridgeWorkflowMethod.LinearHistory || merged.BridgeWorkflow?.Phases.Any(p => p.Actions.Any(a => a.HasLocalSectionStates)) == true;
        var results = new BridgeStressResult[n][][];
        for (int i = 0; i < n; i++) results[i] = new BridgeStressResult[forces[i].Length][];
        var tasks = Enumerable.Range(0, n).SelectMany(i => Enumerable.Range(0, forces[i].Length).Select(j => (i, j))).ToArray();
        Parallel.For(0, tasks.Length, t =>
        {
            var (i, j) = tasks[t]; var start = starts[i]; var p = psis[i][j]; var f = forces[i][j]; var e = strains[i][j];
            if (workflowPath) { results[i][j] = checkers[i][j].AnalyzeWorkflow(merged.BridgeWorkflow, indexes[i], stations[i][j], merged.LoadCombinations, p.Inf, p.Inst, p.Shr, start.ConsiderRebar); return; }
            results[i][j] = checkers[i][j].GetLinearAnalysisResultByStages(out BridgeStressResult[] r, out _, p.Inf, p.Inst, f.Steel, f.Inf, f.Inst, start.ConsiderConcrete, e.Inf, e.Inst, start.ConsiderRebar, p.Shr, f.Shr) ? r : null;
        });
        return results;
    }

    /// <summary>The section results of a Super Element Check (D3) against the former path on the same inputs.</summary>
    internal static Comparison Compare(GH_Component check, string label)
    {
        void Fail(string what) => throw new Exception("FEM PHASES COMPARE FAIL (" + label + "): " + what);
        var reference = Calculate(check);
        var tree = check.Params.Output[0].VolatileData;
        double maxStress = 0, maxUtilization = 0, maxRatio = 0; int statuses = 0, count = 0, identical = 0; string worst = "";
        static double Diff(double x, double y) => double.IsNaN(x) && double.IsNaN(y) ? 0 : Math.Abs(x - y);
        int paths = 0;
        for (int i = 0; i < reference.Length; i++)
            for (int j = 0; j < reference[i].Length; j++)
            {
                int p = tree.Paths.IndexOf(new GH_Path(i, j));
                if (p < 0) Fail($"no branch {{{i};{j}}}");
                paths++;
                var x = tree.get_Branch(p).Cast<IGH_Goo>().Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray(); var y = reference[i][j];
                if (y == null || x.Length != y.Length) Fail($"branch {{{i};{j}}}: {x.Length} D3 results, {y?.Length ?? 0} former");
                for (int k = 0; k < x.Length; k++)
                {
                    count++;
                    double ds = Math.Max(Diff(x[k].Point5_SteelSigma, y[k].Point5_SteelSigma), Diff(x[k].Point8_SteelSigma, y[k].Point8_SteelSigma));
                    double du = Diff(x[k].Stage.MaxUtilization, y[k].Stage.MaxUtilization);
                    var cx = BridgeGpc.Checks(x[k].Stage).ToArray(); var cy = BridgeGpc.Checks(y[k].Stage).ToArray();
                    if (cx.Length != cy.Length) Fail($"{{{i};{j}}}[{k}]: {cx.Length} D3 checks, {cy.Length} former");
                    double dr = 0;
                    for (int c = 0; c < cx.Length; c++)
                    {
                        if (cx[c].Name != cy[c].Name) Fail($"{{{i};{j}}}[{k}]: check {cx[c].Name} / {cy[c].Name}");
                        if (cx[c].Status != cy[c].Status) statuses++;
                        dr = Math.Max(dr, Diff(cx[c].Ratio ?? double.NaN, cy[c].Ratio ?? double.NaN));
                    }
                    if (ds == 0 && du == 0 && dr == 0) identical++;
                    if (ds > maxStress) { maxStress = ds; worst = $"{x[k].Source?.Label} ({{{i};{j}}}[{k}])"; }
                    maxUtilization = Math.Max(maxUtilization, du); maxRatio = Math.Max(maxRatio, dr);
                }
            }
        if (paths != tree.PathCount) Fail($"{tree.PathCount} D3 branches, {paths} former");
        var result = new Comparison(count, identical, maxStress, maxUtilization, maxRatio, statuses, worst);
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "FEM PHASES COMPARE ({0}): {1} section results, {2} identical in every compared number; max |Δσ| points 5/8 {3:G3} MPa (worst {4}), " +
            "max |Δ utilization| {5:G3}, max |Δ ratio| {6:G3}, {7} status changes", label, count, identical, maxStress, worst, maxUtilization, maxRatio, statuses));
        if (count == 0) Fail("no section results");
        if (statuses != 0) Fail(statuses + " check statuses changed");
        if (maxStress > 1e-3 || maxUtilization > 1e-6 || maxRatio > 1e-6) Fail("differences beyond the tolerances of the C5 comparison (1e-3 MPa, 1e-6)");
        return result;
    }
}
