using System;

namespace BridGHe.Common.Checks
{
    public static class BridgeCreepCoefficient
    {
        /// <summary>n = Es/Ec * (1+phi). Moduli must use the same units.</summary>
        public static double FromModularRatio(double n, double steelModulus, double concreteModulus)
        {
            if (!double.IsFinite(n) || n <= 0 || !double.IsFinite(steelModulus) || steelModulus <= 0 ||
                !double.IsFinite(concreteModulus) || concreteModulus <= 0)
                throw new ArgumentOutOfRangeException(nameof(n), "Rapporto modulare e moduli elastici devono essere positivi e finiti.");
            double phi = concreteModulus / steelModulus * n - 1;
            // An exact instantaneous ratio can round to -1.11e-16. Clamp only
            // this dimensionless numerical neighbourhood, never a physical negative phi.
            if (phi >= -1e-12 && phi < 0) return 0;
            if (!double.IsFinite(phi) || phi < 0)
                throw new ArgumentOutOfRangeException(nameof(n), "Il rapporto modulare deve essere almeno Es/Ec (phi >= 0).");
            return phi;
        }
    }
}
