using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Forms
{
    /// <summary>
    /// R65-C2-05 (review of lane 2, 8/11): the rows of the Load Combinations form and the combinations made of them, without the form (the
    /// form reads and writes through it; the lane test calls it). P2K (P1): a combination is read from its GPC datum and on OK the
    /// combinations are made by their GPC datum alone on the case records (the FEM values and FEM cases removed).
    /// </summary>
    internal static class LoadCombinationRows
    {
        /// <summary>P2CG (13/11/2026, review of ChatGPT task 51, CE-01): the message of the form and of its parameter with an unknown case catalogue.</summary>
        internal const string UnknownCatalog = "The load cases of the combinations are not known yet: run Build Model to give them, then edit the combinations.";

        /// <summary>P2CG (CE-01): why the combinations cannot be edited on <paramref name="caseRecords"/>, or null. An unknown catalogue (null) is refused:
        /// the form would make the combinations without the factors of their cases. A known empty catalogue is accepted, as before.</summary>
        internal static string Refusal(IReadOnlyList<LegacyCaseData> caseRecords) => caseRecords == null ? UnknownCatalog : null;

        /// <summary>P2CH (14/11/2026, review of ChatGPT task 83, 83-02): the message of the form when the combinations are in several branches.</summary>
        internal const string SeveralBranches = "These combinations are in more than one branch, and the form edits one list only: flatten them into one list to edit them here.";

        /// <summary>P2CH (83-02): why the form cannot edit data of <paramref name="branches"/> branches, or null. The form shows and writes one list:
        /// before, its OK replaced a tree of several branches by the edited first branch, and the others were lost.</summary>
        internal static string TreeRefusal(int branches) => branches > 1 ? SeveralBranches : null;

        /// <summary>A row: the name of the combination and its factor by case name.</summary>
        internal sealed record Row(string Name, IReadOnlyDictionary<string, double> Factors);

        /// <summary>The rows of the combinations, in order (a later factor of a case name replaces an earlier one, as the cells of the form).</summary>
        internal static List<Row> Of(IEnumerable<GH_LoadCombination> values)
        {
            var rows = new List<Row>();
            foreach (var v in values ?? Enumerable.Empty<GH_LoadCombination>())
            {
                var factors = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var f in v.Gpc.Factors) factors[f.Case?.Name ?? ""] = f.Factor;
                rows.Add(new Row(v.Gpc.Name, factors));
            }
            return rows;
        }

        /// <summary>The names of the case columns: the case records.</summary>
        internal static string[] CaseColumns(IReadOnlyList<LegacyCaseData> caseRecords) =>
            (caseRecords ?? Array.Empty<LegacyCaseData>()).Select(c => c.Name).Distinct(StringComparer.Ordinal).ToArray();

        /// <summary>The combinations of the rows (name and the cells of the case columns, in the order of <see cref="CaseColumns"/>), by their GPC
        /// datum alone, with the checks and messages of the form; null and the message when a name is refused.</summary>
        internal static (List<GH_LoadCombination> Combinations, string Error) Make(IReadOnlyList<(string Name, IReadOnlyList<object> Cells)> rows, IReadOnlyList<string> columns,
            IReadOnlyList<LegacyCaseData> caseRecords)
        {
            // P2CG (CE-01): no combination is made on an unknown catalogue (the OK of the form keeps the data of the parameter).
            if (Refusal(caseRecords) is string refusal) return (null, refusal);
            var values = new List<GH_LoadCombination>(); var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (name, cells) in rows)
            {
                if (columns.Contains(name, StringComparer.Ordinal))
                    return (null, $"The combination names {name} is invalid. The name must be different to the load cases names");
                if (!names.Add(name)) return (null, $"The combination name {name} is used more than one time");
                var cases = columns.Select(c => caseRecords.First(r => r.Name == c)).ToList();
                var factors = columns.Select((c, j) => Convert.ToDouble(j < cells.Count ? cells[j] : null)).ToList();
                values.Add(GH_LoadCombination.FromGpc(Common.Gpc.Builders.GpcCaseBuilders.Combination(name, cases, factors)));
            }
            return (values, null);
        }

        // P2K (P1): TRANSITIONAL, the former signatures read by reflection by the lane test (GpcOnlyReplay, R65-C2-05); the FEM cases are
        // ignored (every combination is made by its GPC datum alone). Deleted by P2-taglio with the FEM model.
        internal static bool GpcOnly(IReadOnlyList<GH_LoadCombination> values, IReadOnlyList<LegacyCaseData> caseRecords) => true;
        internal static string[] Columns(bool gpcOnly, IReadOnlyList<LegacyCaseData> caseRecords) => CaseColumns(caseRecords);
        internal static (List<GH_LoadCombination> Combinations, string Error) Combinations(IReadOnlyList<(string Name, IReadOnlyList<object> Cells)> rows, IReadOnlyList<string> columns,
            IReadOnlyList<LegacyCaseData> caseRecords, bool gpcOnly) => Make(rows, columns, caseRecords);
    }
}
