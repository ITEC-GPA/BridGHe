using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.Elements;

/// <summary>F6A gate for the result path: F1 inputs of examples 50 and 99 are analysed through GPC (Converter writer, solver, verified
/// result import) and the GPC results, expressed with the former component conventions (GpcResults), are compared with the results
/// that the former analysis stored in the F1 result models (node displacements, rotations, reactions, beam actions at the stations).
/// Usage: --legacy-results-compare &lt;output folder&gt; [--only name]</summary>
internal static class LegacyResultsComparison
{
    internal static int Run(string[] args)
    {
        string output = Path.GetFullPath(args.SkipWhile(a => a != "--legacy-results-compare").Skip(1).First());
        string only = args.SkipWhile(a => a != "--only").Skip(1).FirstOrDefault(); string f1 = LegacyExportComparison.DefaultF1;
        Directory.CreateDirectory(output); var summary = new StringBuilder();
        foreach (var c in LegacyExportComparison.Cases.Where(c => only == null || c.Name == only))
        {
            string folder = Path.Combine(output, c.Name); if (Directory.Exists(folder)) throw new IOException("Output exists: " + folder); Directory.CreateDirectory(folder);
            var model = LegacyModelImport.Import(LegacyArchive.Read(File.ReadAllBytes(Path.Combine(f1, c.Input))), GpcIdentity.Derive(GpcIdentity.NameSpace, "f1:" + c.Name), 0.001, new LegacyImportReport());
            var outcome = GpcStrausAnalysis.LinearStatic(model, Path.Combine(folder, c.Name + ".st7"), folder);
            var legacy = LegacyArchive.Read(File.ReadAllBytes(Path.Combine(f1, c.Input.Replace("-input.bgmodel", "-result.bgmodel"))));
            var elements = legacy.Field("Elements").Items;
            var nodes = elements.Where(e => e.Is("NodeModel")).ToList(); var beams = elements.Where(e => e.Is("BeamModel")).ToList();
            var stats = new SortedDictionary<string, (double Max, double Diff, int Count, int Missing, string Where)>(StringComparer.Ordinal);
            var missing = new List<string>();
            void Add(string quantity, string where, double[] expected, double[] actual)
            {
                stats.TryGetValue(quantity, out var s);
                if (actual == null) { missing.Add(quantity + " " + where); stats[quantity] = (s.Max, s.Diff, s.Count, s.Missing + 1, s.Where); return; }
                double max = Math.Max(s.Max, expected.Select(Math.Abs).Max()); double d = expected.Zip(actual, (a, b) => Math.Abs(a - b)).Max();
                stats[quantity] = (max, Math.Max(s.Diff, d), s.Count + 1, s.Missing, d > s.Diff ? where + ": " + Text(expected) + " | " + Text(actual) : s.Where);
            }
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = outcome.Model.NodesElements[i + 1];
                foreach (var r in nodes[i].Field("Results").Items)
                {
                    string caseName = r.Field("Case").String("Name"); string key = caseName + "/node " + (i + 1);
                    if (r.Is("NodeDisplResultModel"))
                    {
                        var d = GpcResults.Combined(outcome.Model, caseName, n => GpcResults.NodeDisplacement(node, n)); var t = V(r["Displacement"]); var q = V(r["Rotation"]);
                        Add("displacement", key, t, d?.Take(3).ToArray()); Add("rotation", key, q, d?.Skip(3).ToArray());
                    }
                    else if (r.Is("NodeReactResultModel"))
                    {
                        var f = GpcResults.Combined(outcome.Model, caseName, n => GpcResults.NodeReaction(node, n)); Add("reaction force", key, V(r["Force"]), f?.Take(3).ToArray()); Add("reaction moment", key, V(r["Moment"]), f?.Skip(3).ToArray());
                    }
                }
            }
            for (int k = 0; k < beams.Count; k++)
            {
                var beam = outcome.Model.BeamElements[k + 1];
                var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, beams[k].Double("Angle"));
                double length = beam.Length;
                foreach (var group in beams[k].Field("Results").Items.Where(r => r.Is("BeamForceResultModel")).GroupBy(r => r.Field("Case").String("Name")))
                {
                    bool combined = outcome.Model.Combinations.ContainsKey(group.Key);
                    var gpc = combined ? null : GpcResults.BeamForces(beam, group.Key, principal);
                    foreach (var r in group)
                    {
                        double station = r.Bool("NormalizedLength") ? r.Double("Station") : r.Double("Station") / length;
                        if (combined)
                        {
                            // Combinations at the regular stations only: Straus7 adds result-dependent stations per case.
                            double[] At(string n) { var g = GpcResults.BeamForces(beam, n, principal).FirstOrDefault(x => Math.Abs(x[0] - station) < 1e-9); return g; }
                            var cv = GpcResults.Combined(outcome.Model, group.Key, At);
                            if (cv == null && Math.Abs(station * 4 - Math.Round(station * 4)) > 1e-9) continue;
                            string ck = group.Key + "/beam " + (k + 1) + "@" + station.ToString("0.####", CultureInfo.InvariantCulture);
                            var sh = r["ShearForce"]; var be = r["BendingMoment"];
                            Add("beam N,T", ck, new[] { r.Double("AxialForce"), r.Double("Torque") }, cv == null ? null : new[] { cv[1], cv[4] });
                            Add("beam SF1,SF2", ck, new[] { sh.Double("X"), sh.Double("Y") }, cv == null ? null : new[] { cv[2], cv[3] });
                            Add("beam BM1,BM2", ck, new[] { be.Double("X"), be.Double("Y") }, cv == null ? null : new[] { cv[5], cv[6] });
                            continue;
                        }
                        var match = gpc.Where(g => Math.Abs(g[0] - station) < 1e-3).OrderBy(g => Math.Abs(g[0] - station)).FirstOrDefault(); /* Straus7 adds result-dependent stations (extrema): positions differ by ~1e-4 */ string key = group.Key + "/beam " + (k + 1) + "@" + station.ToString("0.####", CultureInfo.InvariantCulture);
                        var shear = r["ShearForce"]; var bending = r["BendingMoment"];
                        Add("beam N,T", key, new[] { r.Double("AxialForce"), r.Double("Torque") }, match == null ? null : new[] { match[1], match[4] });
                        Add("beam SF1,SF2", key, new[] { shear.Double("X"), shear.Double("Y") }, match == null ? null : new[] { match[2], match[3] });
                        Add("beam BM1,BM2", key, new[] { bending.Double("X"), bending.Double("Y") }, match == null ? null : new[] { match[5], match[6] });
                    }
                }
            }
            var legacyCases = elements.SelectMany(e => e.Field("Results").Items).Select(r => r.Field("Case").String("Name")).Distinct().OrderBy(n => n, StringComparer.Ordinal);
            var gpcCases = outcome.Model.AllElements.SelectMany(e => e.Results).SelectMany(r => r.Results).Select(l => l.Case?.Name).Distinct().OrderBy(n => n, StringComparer.Ordinal);
            var text = new StringBuilder($"== {c.Name}: GPC analysis {outcome.Run.ModelPath}; legacy cases {string.Join(",", legacyCases)}; GPC cases {string.Join(",", gpcCases)}" + Environment.NewLine);
            foreach (var s in stats)
                text.AppendLine($"  {s.Key,-16} values {s.Value.Count,6}  missing {s.Value.Missing,4}  max {s.Value.Max,12:G6}  max diff {s.Value.Diff,12:G6}  rel {(s.Value.Max > 0 ? s.Value.Diff / s.Value.Max : 0):E2}   worst {s.Value.Where}");
            File.WriteAllLines(Path.Combine(folder, "missing.txt"), missing);
            File.WriteAllText(Path.Combine(folder, "summary.txt"), text.ToString()); Console.Write(text); summary.Append(text);
        }
        File.WriteAllText(Path.Combine(output, "summary.txt"), summary.ToString());
        return 0;
    }
    private static double[] V(LegacyValue v) => new[] { v.Double("X"), v.Double("Y"), v.Double("Z") };
    private static string Text(double[] v) => string.Join(" ", v.Select(x => x.ToString("G6", CultureInfo.InvariantCulture)));
}
