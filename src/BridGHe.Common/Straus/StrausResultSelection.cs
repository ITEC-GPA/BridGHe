using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Straus
{
    /// <summary>Result names and unfiltered increment numbers shared by the UI and analysis.</summary>
    public static class StrausResultSelection
    {
        public sealed record Entry(int Increment, CaseModel Case);

        public static IReadOnlyList<Entry> Catalog(ModelModel model, int solver)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var entries = new List<Entry>();
            switch (solver)
            {
                case 0:
                    ModelHelper.GetLoadCases(model, out var cases);
                    entries.AddRange(cases.Select(c => new Entry(0, c)));
                    entries.AddRange(model.LoadCombinations.Select(c => new Entry(0, c)));
                    break;
                case 1:
                    entries.AddRange(model.LoadCombinations.Select((c, i) => new Entry(i + 1, c)));
                    break;
                case 2:
                    foreach (var stage in model.StagedConstruction)
                        foreach (var combination in stage.LoadCombinations)
                            entries.Add(new Entry(entries.Count + 1, new LoadCombinationModel(combination)
                            { Name = combination.Name + " [" + stage.Name + "]" }));
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(solver));
            }
            if (entries.Any(e => string.IsNullOrWhiteSpace(e.Case.Name)) ||
                entries.GroupBy(e => e.Case.Name, StringComparer.Ordinal).Any(g => g.Count() > 1))
                throw new ArgumentException("Nomi dei risultati vuoti o ambigui: assegnare nomi univoci a casi, combinazioni e fasi.");
            return entries;
        }

        public static IReadOnlyList<Entry> Select(ModelModel model, int solver, IEnumerable<string> requested)
        {
            var names = new HashSet<string>(requested ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (names.Count == 0) throw new ArgumentException("Selezionare almeno un risultato in Analysis Results.");
            var catalog = Catalog(model, solver);
            var available = new HashSet<string>(catalog.Select(e => e.Case.Name), StringComparer.Ordinal);
            var missing = names.Where(n => !available.Contains(n)).ToArray();
            if (missing.Length > 0) throw new ArgumentException("Risultati assenti per il solutore selezionato: " + string.Join(", ", missing));
            return catalog.Where(e => names.Contains(e.Case.Name)).ToArray();
        }
    }
}
