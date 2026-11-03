#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GPC.Converter.Straus7;

/// <summary>Linear-static results of two Straus7 files compared by node coordinates and beam end nodes (numbering independent):
/// global node displacements/rotations, reactions, and beam forces/moments in global axes at the native stations.
/// Each difference is normalized by the largest value of the same quantity in the case.</summary>
internal static class St7Results
{
    private sealed class Data
    {
        internal readonly Dictionary<string, Dictionary<string, double[]>> Cases = new Dictionary<string, Dictionary<string, double[]>>(StringComparer.Ordinal);
    }
    private static string Key(double[] p) => "(" + string.Join(",", p.Select(v => Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture))) + ")";

    private static Data Read(string model, string results, int stations)
    {
        var data = new Data();
        using var api = new Straus7NativeApi();
        api.OpenModelReadOnly(Path.GetFullPath(model), Path.GetTempPath());
        try
        {
            var file = api.OpenResults(Path.GetFullPath(results));
            int nodes = api.Count(Straus7Entity.Node), beams = api.Count(Straus7Entity.Beam);
            var keys = Enumerable.Range(1, nodes).ToDictionary(n => n, n => Key(api.ReadNode(n).Coordinates));
            var beamKeys = Enumerable.Range(1, beams).ToDictionary(b => b, b => { var e = api.ReadElement(Straus7Entity.Beam, b); return keys[e.Nodes[0]] + "-" + keys[e.Nodes[1]]; });
            for (int c = 1; c <= file.PrimaryCases; c++)
            {
                var info = api.ReadResultCase(c); var values = new Dictionary<string, double[]>(StringComparer.Ordinal); data.Cases[info.Name] = values;
                foreach (var n in keys)
                {
                    var d = api.ReadNodeResult(n.Key, c, false).Values; values["displacement/" + n.Value] = d.Take(3).ToArray(); values["rotation/" + n.Value] = d.Skip(3).ToArray();
                    var r = api.ReadNodeResult(n.Key, c, true).Values; values["reaction force/" + n.Value] = r.Take(3).ToArray(); values["reaction moment/" + n.Value] = r.Skip(3).ToArray();
                }
                foreach (var b in beamKeys)
                {
                    var t = api.ReadBeamForces(b.Key, c, stations);
                    for (int row = 0; row < t.Rows; row++)
                    {
                        string at = t.Positions[row].ToString("0.######", CultureInfo.InvariantCulture);
                        var v = Enumerable.Range(0, t.Columns).Select(k => t.Values[row * t.Columns + k]).ToArray();
                        values["beam force/" + b.Value + "@" + at] = v.Take(3).ToArray(); values["beam moment/" + b.Value + "@" + at] = v.Skip(3).Take(3).ToArray();
                    }
                }
            }
        }
        finally { api.CloseModel(); }
        return data;
    }

    internal static string Compare(string legacyModel, string legacyResults, string model, string results, int stations, string tsv)
    {
        var a = Read(legacyModel, legacyResults, stations); var b = Read(model, results, stations);
        var text = new StringBuilder(); var lines = new List<string>();
        foreach (var c in a.Cases)
        {
            if (!b.Cases.TryGetValue(c.Key, out var other)) { text.AppendLine($"  case {c.Key}: missing in the GPC results"); continue; }
            foreach (var quantity in c.Value.Keys.Select(k => k.Split('/')[0]).Distinct())
            {
                var keys = c.Value.Keys.Where(k => k.StartsWith(quantity + "/", StringComparison.Ordinal)).ToArray();
                double scale = keys.SelectMany(k => c.Value[k]).Select(Math.Abs).DefaultIfEmpty(0).Max(); double worst = 0; string where = ""; int missing = 0;
                foreach (var k in keys)
                {
                    if (!other.TryGetValue(k, out var y)) { missing++; continue; }
                    var x = c.Value[k]; double d = x.Zip(y, (p, q) => Math.Abs(p - q)).Max();
                    if (d > worst) { worst = d; where = k + ": " + string.Join(" ", x.Select(v => v.ToString("G6", CultureInfo.InvariantCulture))) + " | " + string.Join(" ", y.Select(v => v.ToString("G6", CultureInfo.InvariantCulture))); }
                }
                double relative = scale > 0 ? worst / scale : 0;
                lines.Add($"{c.Key}\t{quantity}\t{scale:G6}\t{worst:G6}\t{relative:E2}\t{missing}\t{where}");
                text.AppendLine($"  {c.Key,-28} {quantity,-16} max {scale,12:G6}  max diff {worst,12:G6}  rel {relative:E2}{(missing != 0 ? "  missing " + missing : "")}");
            }
        }
        foreach (var c in b.Cases.Keys.Where(k => !a.Cases.ContainsKey(k))) text.AppendLine($"  case {c}: only in the GPC results");
        File.WriteAllLines(tsv, lines);
        return text.ToString();
    }
}
#endif
