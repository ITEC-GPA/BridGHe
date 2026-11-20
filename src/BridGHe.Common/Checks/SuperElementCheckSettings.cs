using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Checkers.CompositeBridge;

namespace BridGHe.Common.Checks
{
    /// <summary>The options of the Super Element Check (user decision of 20/11/2026): what were its optional inputs (the three local
    /// instability toggles, Bridge Analysis Options, ULS Combinations) plus the number of analysis stations per element.
    /// Without this object the check uses <see cref="Default"/>: the three instabilities on, Eurocode 4 rare SLS, no ULS combinations,
    /// the 2 nodes of every element.</summary>
    public sealed record SuperElementCheckSettings
    {
        /// <summary>Local instability of the web, used only without <see cref="AnalysisOptions"/>.</summary>
        public bool WebBuckling { get; init; } = true;
        /// <summary>Local instability of the top flange, used only without <see cref="AnalysisOptions"/>.</summary>
        public bool TopFlangeBuckling { get; init; } = true;
        /// <summary>Local instability of the bottom flange, used only without <see cref="AnalysisOptions"/>.</summary>
        public bool BottomFlangeBuckling { get; init; } = true;
        /// <summary>Bridge Analysis Options: when given they replace the defaults (Eurocode 4, rare SLS, Class 4, partial factors) and the
        /// three toggles above.</summary>
        public BridgeAnalysisOptions AnalysisOptions { get; init; }
        /// <summary>Combinations checked at the ultimate limit state: exact names, or a prefix ending with * (e.g. SLU_*).</summary>
        public string[] UlsCombinations { get; init; } = Array.Empty<string>();
        /// <summary>Analysis stations per element: 2 = the two nodes (default), 3 = nodes and middle, 0 = all the FEM output stations.</summary>
        public int StationsPerElement { get; init; } = BridgeCheckStations.DefaultPerElement;

        public static SuperElementCheckSettings Default { get; } = new SuperElementCheckSettings();

        /// <summary>The options of the checks: those supplied, else Eurocode 4 rare SLS with the three toggles.</summary>
        public BridgeAnalysisOptions Options() => AnalysisOptions ?? new BridgeAnalysisOptions
        {
            Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare,
            WebBuckling = WebBuckling, TopFlangeBuckling = TopFlangeBuckling, BottomFlangeBuckling = BottomFlangeBuckling
        };

        public override string ToString()
        {
            string stations = StationsPerElement == 0 ? "tutte le stazioni" : StationsPerElement == 2 ? "2 nodi per elemento" : $"{StationsPerElement} stazioni per elemento";
            string options = AnalysisOptions != null ? "Bridge Analysis Options collegate" : $"EC4 SLE rara, instabilità anima {On(WebBuckling)}, piattabanda sup. {On(TopFlangeBuckling)}, inf. {On(BottomFlangeBuckling)}";
            string uls = (UlsCombinations?.Length ?? 0) == 0 ? "nessuna combinazione SLU" : "SLU: " + string.Join(", ", UlsCombinations);
            return $"Opzioni Super Element Check: {stations}; {options}; {uls}";
        }

        private static string On(bool value) => value ? "sì" : "no";
    }
}
