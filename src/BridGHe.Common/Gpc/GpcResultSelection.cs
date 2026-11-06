using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GPC.Model.Models;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// F6 R6.4 (1): the result names of an analysis and their unfiltered increment numbers from the GPC input of the analysis (the former
    /// <c>StrausResultSelection</c> on the FEM model): what the Analysis Results value list of Straus R3 Analysis shows, what Bridge Analysis
    /// Cases validates and what the analysis asks the solver for.
    /// <list type="bullet">
    /// <item>solver 0 (linear): every load case of the input, «Dead» first and then by name (the comparison of the current culture, as the
    /// former <c>LoadCaseModel</c> sorted them), then the combinations in their order; increment 0;</item>
    /// <item>solver 1 (nonlinear): the combinations in their order, increments 1, 2, …;</item>
    /// <item>solver 2 (staged): «combination [stage]» for every load increment of every stage, stages in their order, increments numbered
    /// across the stages (1, 2, …), as Straus7 numbers them.</item>
    /// </list>
    /// Empty or repeated names are refused with the former message. The GPC input has the case set of the former catalogue (the reader keeps
    /// the registered cases, those of the element loads, combinations, stage increments and moving paths, and «Dead»; the generated cases of
    /// an earlier moving analysis are left out), and the same order before sorting: the two catalogues are compared on every analysis until
    /// R6.5 (<c>FemmAnalysisSeam</c>) and on the corpus by the regression <c>gpc-result-selection</c>.
    /// </summary>
    public static class GpcResultSelection
    {
        /// <summary>One result: its name, its increment (0 for a linear result), and for a staged result the stage and the combination of
        /// the load increment.</summary>
        public sealed record Entry(int Increment, string Name, string Stage = null, string Combination = null);

        public const string DeadName = "Dead";
        public const string AmbiguousMessage = "Nomi dei risultati vuoti o ambigui: assegnare nomi univoci a casi, combinazioni e fasi.";
        public const string EmptySelectionMessage = "Selezionare almeno un risultato in Analysis Results.";
        public const string MissingPrefix = "Risultati assenti per il solutore selezionato: ";

        public static IReadOnlyList<Entry> Catalog(Model input, int solver)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var entries = new List<Entry>();
            switch (solver)
            {
                case 0:
                    var names = input.LoadCases.Values.Select(c => c.Name).ToList();
                    names.Sort(CompareCases);
                    entries.AddRange(names.Select(n => new Entry(0, n)));
                    entries.AddRange(input.Combinations.Values.Select(c => new Entry(0, c.Name)));
                    break;
                case 1:
                    entries.AddRange(input.Combinations.Values.Select((c, i) => new Entry(i + 1, c.Name)));
                    break;
                case 2:
                    foreach (var stage in input.Stages.Values.OrderBy(s => s.Id))
                        foreach (var increment in stage.AnalysisState?.Increments ?? Array.Empty<GPC.Model.Stages.StageLoadIncrement>())
                            entries.Add(new Entry(entries.Count + 1, increment.Name + " [" + stage.Name + "]", stage.Name, increment.Name));
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(solver));
            }
            if (entries.Any(e => string.IsNullOrWhiteSpace(e.Name)) || entries.GroupBy(e => e.Name, StringComparer.Ordinal).Any(g => g.Count() > 1))
                throw new ArgumentException(AmbiguousMessage);
            return entries;
        }

        public static IReadOnlyList<Entry> Select(Model input, int solver, IEnumerable<string> requested)
        {
            var names = new HashSet<string>(requested ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (names.Count == 0) throw new ArgumentException(EmptySelectionMessage);
            var catalog = Catalog(input, solver);
            var available = new HashSet<string>(catalog.Select(e => e.Name), StringComparer.Ordinal);
            var missing = names.Where(n => !available.Contains(n)).ToArray();
            if (missing.Length > 0) throw new ArgumentException(MissingPrefix + string.Join(", ", missing));
            return catalog.Where(e => names.Contains(e.Name)).ToArray();
        }

        /// <summary>The order of the former load cases (<c>LoadCaseModel.CompareTo</c>): equal names first, then «Dead», then the comparison
        /// of the current culture.</summary>
        public static int CompareCases(string a, string b) =>
            a.Equals(b) ? 0 : a == DeadName ? -1 : b == DeadName ? 1 : a.CompareTo(b);

        /// <summary>
        /// F6 R6.4 (1): the fingerprint of the input of an analysis (Moving Static Analysis keeps it with its results: they belong to the
        /// current input while it is unchanged): «GPC:» and the SHA-256 of the GPC analysis fingerprint of the input model
        /// (<see cref="ModelRevisions.AnalysisFingerprint"/>: elements, properties, loads, cases, combinations, stages, moving loads, and the
        /// model GUID, which comes from the content of the inputs) and of the native Straus7 sections the write plan uses, by beam GUID.
        /// The former fingerprint (SHA-256 of the archive of the FEM model) is not a «GPC:» value.
        /// </summary>
        public static string InputHash(GpcModels.Conversion input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var text = new StringBuilder(input.Model.AnalysisFingerprint());
            foreach (var native in input.Report.NativeSections.OrderBy(p => p.Key))
                text.Append('|').Append(native.Key.ToString("D")).Append(':')
                    .Append(native.Value == null ? "-" : Convert.ToHexString(SHA256.HashData(native.Value.Save())));
            text.Append("|tolerance:").Append(input.Tolerance.ToString("R", CultureInfo.InvariantCulture));
            return "GPC:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }
    }
}
