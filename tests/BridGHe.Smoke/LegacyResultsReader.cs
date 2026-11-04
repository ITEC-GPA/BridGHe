#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Converter;
using GPC.Geometry;
using GPC.Model.Analysis;
using GPC.Model.Core.Units;
using GPC.Model.Elements;
using GPC.Model.Results.Locations;
using GPC.Model.Results.State;

/// <summary>
/// TESTS ONLY (legacy engines live only in tests): the results stored in former model archives (BGHA1: node displacements and reactions,
/// beam actions at the stations, from the former Straus7 reader) as a GPC result dataset of the model converted by <see cref="LegacyModelImport"/>
/// from the same archive, through the Converter result mapper. The frozen F1 fixtures thus become GPC models with results, the reference
/// against which the GPC consumers (D3 BridgeWorkflowExecutor) are compared; the fixtures themselves are never rebuilt or replaced.
/// <para>Conventions: the inverse of <see cref="GpcResults"/> (verified on the F1 results of examples 50 and 99): beam actions of the former
/// principal frame (SF1 = −V1, SF2 = −V2, BM1 = M2, BM2 = −M1) back to the section axes, at the offset axis of the beam, positive section face;
/// nodal displacements and support reactions in global axes, as read. Only primary load cases: results of combinations are superpositions
/// (GpcResults.Combined) and are not stored. Units from the former model (N-mm for examples 50 and 99).</para>
/// </summary>
internal static class LegacyResultsReader
{
    internal const string Program = "BridGHe F1 results";

    internal sealed class Outcome
    {
        public ResultImportReport Report { get; set; }
        public int SkippedCombinationSamples { get; set; }
    }

    /// <param name="gpc">The model converted from <paramref name="legacy"/> by <see cref="LegacyModelImport.Import"/> with <paramref name="owner"/>.</param>
    internal static Outcome Import(GPC.Model.Models.Model gpc, LegacyValue legacy, Guid owner, string datasetId)
    {
        var units = new BridgeUnits(BridgeUnits.Parse(legacy.Enum("Units")));
        // The results refer to the elements by their source identity: the program and revision of the legacy reader.
        string revision = owner.ToString("D");
        var source = new AnalysisSource { Program = LegacyModelImport.Program, SolverVersion = "Straus7 R3 (F1 archive)", ModelRevision = revision, AnalysisId = datasetId };
        gpc.AnalysisSource = source;
        var batch = new ResultImportBatch
        {
            Source = source, DatasetId = datasetId, SourceHash = "legacy-archive/" + datasetId, ExpectedInputFingerprint = GPC.Model.Models.ModelRevisions.AnalysisFingerprint(gpc),
            Units = new ResultUnits(units.Force, units.Length, units.Force * units.Length), ShellDenominatorLengthToMm = units.Length,
            ResolvedConvention = "Former BridGHe result objects (F1): beam actions in the former principal frame, SF1 = -V1, SF2 = -V2, BM1 = M2, BM2 = -M1; nodal values global.",
            ReaderVersion = "BridGHe.Smoke.LegacyResultsReader/1", Semantics = AnalysisSemantics.LinearStatic, IsSynthetic = false,
        };
        var cases = new HashSet<string>(gpc.LoadCases.Values.Where(c => !(c is GPC.Model.Stages.StageResultCase)).Select(c => c.Name), StringComparer.Ordinal);
        var combinations = new HashSet<string>(gpc.Combinations.Keys, StringComparer.Ordinal);
        var outcome = new Outcome();
        var elements = legacy.Field("Elements").Items;
        var nodes = elements.Where(e => e.Is("NodeModel")).ToList(); var beams = elements.Where(e => e.Is("BeamModel")).ToList();
        var states = new Dictionary<string, ResultState>(StringComparer.Ordinal);
        ResultState State(string caseName, int count) => new ResultState { Semantics = AnalysisSemantics.LinearStatic, ConcomitantStateId = "F1 case " + caseName,
            IsCombined = false, IsCumulative = true, Components = Enumerable.Repeat(ComponentAvailability.Available, count).ToArray(), Coverage = "Former result objects of the F1 archive." };
        bool Primary(string caseName)
        {
            if (cases.Contains(caseName)) return true;
            if (combinations.Contains(caseName)) { outcome.SkippedCombinationSamples++; return false; }
            throw new ArgumentException("LegacyResultCaseNotInModel: " + caseName);
        }
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = gpc.NodesElements[i + 1]; string id = node.Source.OriginalId;
            var axes = new CoordinateSystem(new Point3d(node.Position.X / units.Length, node.Position.Y / units.Length, node.Position.Z / units.Length), new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1));
            foreach (var r in nodes[i].Field("Results").Items)
            {
                string c = r.Field("Case").String("Name"); if (!Primary(c)) continue;
                if (r.Is("NodeDisplResultModel"))
                    batch.NodeDisplacements.Add(new NodeDisplacementRecord { ElementId = id, Case = c, State = State(c, 6), Axes = axes, Record = "F1 node " + id + " displacement " + c,
                        Values = V(r["Displacement"]).Concat(V(r["Rotation"])).Select(v => (double?)v).ToArray() });
                else if (r.Is("NodeReactResultModel"))
                    batch.NodeForces.Add(new NodeForceRecord { ElementId = id, Case = c, State = State(c, 6), Axes = axes, Record = "F1 node " + id + " reaction " + c,
                        Kind = NodalForceKind.SupportReaction, Body = ActionBody.OnNode, Values = V(r["Force"]).Concat(V(r["Moment"])).Select(v => (double?)v).ToArray() });
            }
        }
        for (int k = 0; k < beams.Count; k++)
        {
            var beam = gpc.BeamElements[k + 1]; string id = beam.Source.OriginalId; var axes = beam.Assignments.SectionAxes;
            var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, beams[k].Double("Angle"));
            double legacyLength = beam.Length / units.Length;
            foreach (var r in beams[k].Field("Results").Items.Where(r => r.Is("BeamForceResultModel")))
            {
                string c = r.Field("Case").String("Name"); if (!Primary(c)) continue;
                double station = r.Bool("NormalizedLength") ? r.Double("Station") : r.Double("Station") / legacyLength;
                var sf = r["ShearForce"]; var bm = r["BendingMoment"];
                // Inverse of GpcResults.BeamForces.
                var shear = principal.V1 * -sf.Double("X") + principal.V2 * -sf.Double("Y");
                var bending = principal.V2 * bm.Double("X") + principal.V1 * -bm.Double("Y");
                double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
                var point = beam.StartPoint + (beam.EndPoint - beam.StartPoint) * station;
                if (beam.Assignments.OffsetI != null) point += (beam.Assignments.OffsetAxes ?? axes).ToGlobal(beam.Assignments.OffsetI);
                var at = new CoordinateSystem(new Point3d(point.X / units.Length, point.Y / units.Length, point.Z / units.Length),
                    new Vector3d(axes.V1.X, axes.V1.Y, axes.V1.Z), new Vector3d(axes.V2.X, axes.V2.Y, axes.V2.Z), new Vector3d(axes.V3.X, axes.V3.Y, axes.V3.Z));
                batch.Beams.Add(new BeamForceRecord { ElementId = id, Case = c, State = State(c, 6), Axes = at, Station = station, PhysicalDistance = legacyLength * station,
                    Body = ActionBody.PositiveSectionFace, StationDomain = "NodeToNode", Record = "F1 beam " + id + " station " + station.ToString("R") + " " + c,
                    Values = new double?[] { r.Double("AxialForce"), Dot(shear, axes.V1), Dot(shear, axes.V2), r.Double("Torque"), Dot(bending, axes.V1), Dot(bending, axes.V2) } });
            }
        }
        outcome.Report = ResultMapper.Import(gpc, batch);
        return outcome;
    }

    private static double[] V(LegacyValue v) => new[] { v.Double("X"), v.Double("Y"), v.Double("Z") };

    /// <summary>--legacy-results-reader-check [--only name]: the F1 result archives of examples 50 and 99 read into GPC datasets; GpcResults on
    /// them must give back the stored values (the reader is the exact inverse of the conventions).</summary>
    internal static int Check(string[] args)
    {
        string only = args.SkipWhile(a => a != "--only").Skip(1).FirstOrDefault(); string f1 = LegacyExportComparison.DefaultF1; int failures = 0;
        foreach (var c in LegacyExportComparison.Cases.Where(c => only == null || c.Name == only))
        {
            var owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "f1:" + c.Name);
            var gpc = LegacyModelImport.Import(LegacyArchive.Read(System.IO.File.ReadAllBytes(System.IO.Path.Combine(f1, c.Input))), owner, 0.001, new LegacyImportReport());
            var legacy = LegacyArchive.Read(System.IO.File.ReadAllBytes(System.IO.Path.Combine(f1, c.Input.Replace("-input.bgmodel", "-result.bgmodel"))));
            var outcome = Import(gpc, legacy, owner, "F1 " + c.Name);
            if (outcome.Report.Status != ImportStatus.Completed && outcome.SkippedCombinationSamples > 0 && outcome.Report.Diagnostics.Any(d => d.Message == "EmptyResultDataset"))
            { Console.WriteLine($"SKIP {c.Name}: only results of combinations ({outcome.SkippedCombinationSamples} samples), not stored"); continue; }
            if (outcome.Report.Status != ImportStatus.Completed) { Console.WriteLine($"FAIL {c.Name}: {outcome.Report.Status} {string.Join("; ", outcome.Report.Diagnostics.Select(d => d.Code + " " + d.Message))}"); failures++; continue; }
            double worst = 0; string where = ""; int compared = 0;
            void Compare(string label, double[] expected, double[] actual)
            {
                if (actual == null) throw new InvalidOperationException("LegacyResultMissing: " + label);
                for (int k = 0; k < expected.Length; k++)
                {
                    double d = Math.Abs(expected[k] - actual[k]) / Math.Max(1, Math.Abs(expected[k]));
                    if (d > worst) { worst = d; where = label + " component " + k + ": " + expected[k].ToString("R") + " / " + actual[k].ToString("R"); }
                }
                compared++;
            }
            var elements = legacy.Field("Elements").Items; var nodes = elements.Where(e => e.Is("NodeModel")).ToList(); var beams = elements.Where(e => e.Is("BeamModel")).ToList();
            var primary = new HashSet<string>(gpc.LoadCases.Keys, StringComparer.Ordinal);
            for (int i = 0; i < nodes.Count; i++)
                foreach (var r in nodes[i].Field("Results").Items)
                {
                    string name = r.Field("Case").String("Name"); if (!primary.Contains(name)) continue;
                    var node = gpc.NodesElements[i + 1];
                    if (r.Is("NodeDisplResultModel")) Compare("node " + (i + 1) + " displacement " + name, V(r["Displacement"]).Concat(V(r["Rotation"])).ToArray(), GpcResults.NodeDisplacement(node, name));
                    if (r.Is("NodeReactResultModel")) Compare("node " + (i + 1) + " reaction " + name, V(r["Force"]).Concat(V(r["Moment"])).ToArray(), GpcResults.NodeReaction(node, name));
                }
            for (int k = 0; k < beams.Count; k++)
            {
                var beam = gpc.BeamElements[k + 1]; var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, beams[k].Double("Angle"));
                foreach (var group in beams[k].Field("Results").Items.Where(r => r.Is("BeamForceResultModel")).GroupBy(r => r.Field("Case").String("Name")).Where(g => primary.Contains(g.Key)))
                {
                    var rows = GpcResults.BeamForces(beam, group.Key, principal); var expected = group.ToList();
                    if (rows.Count != expected.Count) throw new InvalidOperationException($"LegacyResultStationCount: beam {k + 1} {group.Key} {expected.Count} / {rows.Count}");
                    var ordered = expected.OrderBy(r => r.Double("Station")).ToList();
                    for (int j = 0; j < rows.Count; j++)
                    {
                        var r = ordered[j]; var sf = r["ShearForce"]; var bm = r["BendingMoment"];
                        Compare($"beam {k + 1} {group.Key} station {j}", new[] { r.Double("AxialForce"), sf.Double("X"), sf.Double("Y"), r.Double("Torque"), bm.Double("X"), bm.Double("Y") }, rows[j].Skip(1).ToArray());
                    }
                }
            }
            Console.WriteLine($"{(worst <= 1e-9 ? "PASS" : "FAIL")} {c.Name}: {outcome.Report.ImportedSamples} samples, {compared} compared, {outcome.SkippedCombinationSamples} combination samples not stored, worst relative {worst:E2} {where}");
            if (worst > 1e-9) failures++;
        }
        return failures == 0 ? 0 : 1;
    }
}
#endif
