using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Forms
{
    /// <summary>
    /// R65-C2-05 (review of lane 2, 8/11): the rows of the Load Combinations form and the combinations made of them, without the form (the
    /// form reads and writes through it; the lane test calls it). A combination given by its GPC datum alone is read from its datum (before,
    /// the form read its null FEM value); on OK the combinations are made by their GPC datum alone when the inputs are (a combination of the
    /// parameter given by its datum alone, or case records and no FEM cases), otherwise as before (FEM values).
    /// </summary>
    internal static class LoadCombinationRows
    {
        /// <summary>A row: the name of the combination and its factor by case name.</summary>
        internal sealed record Row(string Name, IReadOnlyDictionary<string, double> Factors);

        /// <summary>The rows of the combinations, in order (a later factor of a case name replaces an earlier one, as the cells of the form).</summary>
        internal static List<Row> Of(IEnumerable<GH_LoadCombination> values)
        {
            var rows = new List<Row>();
            foreach (var v in values ?? Enumerable.Empty<GH_LoadCombination>())
            {
                var factors = new Dictionary<string, double>(StringComparer.Ordinal);
                if (v.Value != null) { foreach (var item in v.Value.Values) factors[item.Key.Name] = item.Value; rows.Add(new Row(v.Value.Name, factors)); }
                else { foreach (var f in v.Gpc.Factors) factors[f.Case?.Name ?? ""] = f.Factor; rows.Add(new Row(v.Gpc.Name, factors)); }
            }
            return rows;
        }

        /// <summary>Whether the form makes the combinations by their GPC datum alone.</summary>
        internal static bool GpcOnly(IReadOnlyList<GH_LoadCombination> values, IReadOnlyList<LoadCaseModel> loadCases, IReadOnlyList<LegacyCaseData> caseRecords) =>
            values?.Any(v => v != null && v.Value == null) == true || (loadCases == null || loadCases.Count == 0) && caseRecords?.Count > 0;

        /// <summary>The names of the case columns: the FEM cases, or the case records for combinations made by their GPC datum alone.</summary>
        internal static string[] Columns(bool gpcOnly, IReadOnlyList<LoadCaseModel> loadCases, IReadOnlyList<LegacyCaseData> caseRecords) => gpcOnly
            ? (caseRecords ?? Array.Empty<LegacyCaseData>()).Select(c => c.Name).Distinct(StringComparer.Ordinal).ToArray()
            : (loadCases ?? Array.Empty<LoadCaseModel>()).Select(c => c.Name).ToArray();

        /// <summary>The combinations of the rows (name and the cells of the case columns, in the order of <see cref="Columns"/>), with the checks
        /// and messages of the form; null and the message when a name is refused.</summary>
        internal static (List<GH_LoadCombination> Combinations, string Error) Combinations(IReadOnlyList<(string Name, IReadOnlyList<object> Cells)> rows, IReadOnlyList<string> columns,
            IReadOnlyList<LoadCaseModel> loadCases, IReadOnlyList<LegacyCaseData> caseRecords, bool gpcOnly)
        {
            var values = new List<GH_LoadCombination>(); var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, cells) in rows)
            {
                if (columns.Contains(name, StringComparer.Ordinal) || !gpcOnly && loadCases.Any(lc => lc.Name == name))
                    return (null, $"The combination names {name} is invalid. The name must be different to the load cases names");
                if (!names.Add(name)) return (null, $"The combination name {name} is used more than one time");
                if (gpcOnly)
                {
                    var cases = columns.Select(c => caseRecords.First(r => r.Name == c)).ToList();
                    var factors = columns.Select((c, j) => Convert.ToDouble(j < cells.Count ? cells[j] : null)).ToList();
                    values.Add(GH_LoadCombination.FromGpc(Common.Gpc.Builders.GpcCaseBuilders.Combination(name, cases, factors)));
                    continue;
                }
                var combo = new GH_LoadCombination(name);
                for (int j = 0; j < columns.Count; j++)
                {
                    double incr = Convert.ToDouble(j < cells.Count ? cells[j] : null);
                    LoadCaseModel lcase = loadCases.Single(lc => lc.Name == columns[j]);
                    combo.Value.Values.Add(lcase, incr);
                }
                values.Add(combo);
            }
            return (values, null);
        }
    }
}
