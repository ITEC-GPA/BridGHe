using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Common.Checks
{
    /// <summary>Stations of an element at which the Super Element Check verifies the section (user decision of 20/11/2026: an options
    /// component with the number of analysis stations per element, by default the 2 nodes). The FEM gives its output stations (5 by default,
    /// more where a load starts or ends); the check keeps <c>count</c> of them, evenly spread over the element: 2 = the two ends, 3 = the ends
    /// and the middle, 0 = all. In the BridGHe grillage the loads reach the girders at the nodes, so M is linear between two nodes and the ends
    /// hold the maxima; with 1 m elements this is one check per metre on both sides of every node.</summary>
    public static class BridgeCheckStations
    {
        /// <summary>The default: the two nodes of the element.</summary>
        public const int DefaultPerElement = 2;

        /// <summary>The stations to check, ascending, a subset of <paramref name="stations"/> (sorted ascending, element coordinate in mm).
        /// For each of <paramref name="count"/> targets evenly spread from 0 to <paramref name="length"/> the nearest available station (the
        /// first on a tie); duplicates collapse. 0, or a count not lower than the available stations, keeps all of them. 1 is refused.</summary>
        public static IReadOnlyList<double> Select(IReadOnlyList<double> stations, double length, int count)
        {
            if (stations == null) throw new ArgumentNullException(nameof(stations));
            if (count < 0 || count == 1) throw new ArgumentException("Stazioni di analisi per elemento: 0 (tutte) oppure almeno 2.", nameof(count));
            if (count == 0 || count >= stations.Count || stations.Count <= 2) return stations;
            if (!(length > 0) || double.IsNaN(length) || double.IsInfinity(length)) throw new ArgumentException("Lunghezza dell'elemento non valida.", nameof(length));
            var chosen = new SortedSet<int>();
            for (int k = 0; k < count; k++)
            {
                double target = length * k / (count - 1);
                int best = 0; double distance = double.PositiveInfinity;
                for (int s = 0; s < stations.Count; s++)
                {
                    double d = Math.Abs(stations[s] - target);
                    if (d < distance) { distance = d; best = s; }
                }
                chosen.Add(best);
            }
            return chosen.Select(i => stations[i]).ToArray();
        }
    }
}
