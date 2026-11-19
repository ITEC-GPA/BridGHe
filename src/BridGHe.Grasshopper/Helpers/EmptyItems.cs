using System.Globalization;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>Study item 19 (Brecciarola, 19/11/2026): the messages of the components that read check results when some items are empty.
    /// Super Element Check and Bridge Detail Check leave an empty item in place of every section they could not calculate (e.g. an effective
    /// section that does not converge), so that the tree keeps the order of the combinations; the readers skip those items and say how many.</summary>
    internal static class EmptyItems
    {
        /// <summary>The warning of a reader that skipped <paramref name="count"/> empty items.</summary>
        internal static string Skipped(int count) => count.ToString(CultureInfo.InvariantCulture)
            + (count == 1 ? " elemento vuoto ignorato: è una sezione" : " elementi vuoti ignorati: sono le sezioni")
            + " che la verifica a monte non ha calcolato (vedi i suoi errori).";

        /// <summary>The error of a reader whose results are all empty.</summary>
        internal const string AllEmpty = "Tutti i risultati sono vuoti: la verifica a monte non ha calcolato nessuna sezione (vedi i suoi errori).";
    }
}
