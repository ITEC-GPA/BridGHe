using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BridGHe.Common.Checks
{
    /// <summary>Study items 14 and 16 (Brecciarola, 19/11/2026): the combinations of a check that take another limit state, and the list of the
    /// sections a check could not calculate.</summary>
    public static class BridgeCheckSelection
    {
        /// <summary>The combinations named by the entries: an entry is an exact name (ordinal), or a prefix when it ends with <c>*</c> (a lone
        /// <c>*</c> takes every combination). Empty entries are ignored; an entry that names no combination is an error, so that a typo does not
        /// leave a combination at the wrong limit state.</summary>
        public static bool[] Match(IReadOnlyList<string> names, IEnumerable<string> entries)
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            var selected = new bool[names.Count];
            var unknown = new List<string>();
            foreach (var raw in entries ?? Enumerable.Empty<string>())
            {
                var entry = raw?.Trim();
                if (string.IsNullOrEmpty(entry)) continue;
                bool prefix = entry.EndsWith("*", StringComparison.Ordinal);
                var stem = prefix ? entry.Substring(0, entry.Length - 1) : entry;
                bool any = false;
                for (int c = 0; c < names.Count; c++)
                    if (prefix ? names[c].StartsWith(stem, StringComparison.Ordinal) : string.Equals(names[c], stem, StringComparison.Ordinal)) { selected[c] = true; any = true; }
                if (!any) unknown.Add(entry);
            }
            if (unknown.Count > 0) throw new ArgumentException("Combinazioni SLU sconosciute: " + string.Join(", ", unknown) + ". Usare i nomi della tabella delle combinazioni, oppure un prefisso seguito da *.");
            return selected;
        }

        /// <summary>One section a check could not calculate: the beam, its station, the combination and the message of the error.</summary>
        public readonly record struct Failure(int BeamIndex, string BeamId, double StationMm, int CombinationIndex, string Combination, string Message);

        /// <summary>The message of the failures of a check: how many of how many, at how many stations, then every failure in the order of the
        /// beams, stations and combinations (at most <paramref name="maximum"/> lines, then how many more).</summary>
        public static string Message(IEnumerable<Failure> failures, int total, int maximum = 50)
        {
            var ordered = (failures ?? Enumerable.Empty<Failure>()).OrderBy(f => f.BeamIndex).ThenBy(f => f.StationMm).ThenBy(f => f.CombinationIndex).ToArray();
            if (ordered.Length == 0) return null;
            int stations = ordered.Select(f => (f.BeamIndex, f.StationMm)).Distinct().Count();
            var lines = ordered.Take(Math.Max(0, maximum)).Select(f => "trave " + f.BeamId + ", x = " + f.StationMm.ToString("0.#", CultureInfo.InvariantCulture) + " mm, " + f.Combination + ": " + f.Message);
            var text = ordered.Length.ToString(CultureInfo.InvariantCulture) + " verifiche non calcolate su " + total.ToString(CultureInfo.InvariantCulture)
                + " (" + stations.ToString(CultureInfo.InvariantCulture) + (stations == 1 ? " stazione" : " stazioni")
                + "): al loro posto l'uscita ha un elemento vuoto, le altre sono calcolate.\n" + string.Join("\n", lines);
            if (ordered.Length > maximum) text += "\n… e altre " + (ordered.Length - maximum).ToString(CultureInfo.InvariantCulture) + ".";
            return text;
        }
    }
}
