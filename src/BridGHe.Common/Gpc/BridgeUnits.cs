using System;
using System.ComponentModel;

namespace BridGHe.Common.Gpc
{
    /// <summary>Units of a BridGHe definition: the "Units" input of Build Model. Same names, order and descriptions as before,
    /// so the integer input of existing definitions keeps its meaning.</summary>
    public enum DefinitionUnits
    {
        [Description("kNm (m, kN, T, kPa, s, kJ, C)")]
        Knm,
        [Description("SI (m, N, kg, Pa, s, J, C)")]
        SI,
        [Description("Nmm (mm, N, T, MPa, s, mJ, C)")]
        Nmm,
        [Description("PSI (in, lbf, lb, psi, s, btu, F)")]
        IPS
    }

    /// <summary>Factors from the definition units to the GPC domain (N, mm, rad, s, t, °C).
    /// Upstream of Build Model positions, section dimensions and actions are in the definition units; materials and
    /// gravity are already N-mm (MPa, t/mm³, mm/s²), as in the former model, and are never scaled.</summary>
    public readonly struct BridgeUnits
    {
        public DefinitionUnits Units { get; }
        /// <summary>mm per definition length unit.</summary>
        public double Length { get; }
        /// <summary>N per definition force unit.</summary>
        public double Force { get; }
        /// <summary>R6.5 P1 lane 3e (A05): tonnes per definition mass unit: the mass unit of the Straus7 files of BridGHe 0.3.0 for these units
        /// (t for kNm and Nmm, kg for SI, lb for PSI).</summary>
        public double Mass { get; }
        public bool Fahrenheit => Units == DefinitionUnits.IPS;

        public BridgeUnits(DefinitionUnits units)
        {
            Units = units;
            switch (units)
            {
                case DefinitionUnits.Knm: Length = 1000; Force = 1000; Mass = 1; break;
                case DefinitionUnits.SI: Length = 1000; Force = 1; Mass = 1e-3; break;
                case DefinitionUnits.Nmm: Length = 1; Force = 1; Mass = 1; break;
                case DefinitionUnits.IPS: Length = 25.4; Force = 4.4482216152605; Mass = 4.5359237e-4; break;
                default: throw new ArgumentOutOfRangeException(nameof(units));
            }
        }

        public bool IsIdentity => Length == 1 && Force == 1;
        public double ToMm(double length) => length * Length;
        public double ToN(double force) => force * Force;
        /// <summary>Force per length (distributed beam actions).</summary>
        public double ToNPerMm(double value) => IsIdentity ? value : value * Force / Length;
        /// <summary>Moment (force × length).</summary>
        public double ToNmm(double value) => IsIdentity ? value : value * Force * Length;
        /// <summary>Distributed moment (moment per length) has the dimension of a force.</summary>
        public double ToNmmPerMm(double value) => ToN(value);
        /// <summary>Pressure (force per area).</summary>
        public double ToMPa(double value) => IsIdentity ? value : value * Force / (Length * Length);
        public double ToMm2(double value) => IsIdentity ? value : value * Length * Length;
        public double ToMm4(double value) => IsIdentity ? value : value * Length * Length * Length * Length;
        /// <summary>R6.5 P1 lane 3e (A05): a mass per area (definition mass unit per definition area, as 0.3.0 wrote it) in t/mm².</summary>
        public double ToTonnePerMm2(double value) => IsIdentity ? value : value * Mass / (Length * Length);
        /// <summary>R6.5 P1 lane 3e (A06): the power of a moving heat source (energy per second in the energy unit of the Straus7 files of
        /// BridGHe 0.3.0: J for kNm, SI and Nmm, BTU for PSI) in N mm/s.</summary>
        public double ToNmmPerSecond(double power) => power * (Units == DefinitionUnits.IPS ? BtuInJoule : 1) * 1000;
        private const double BtuInJoule = 1055.05585262;
        /// <summary>Absolute temperature.</summary>
        public double ToCelsius(double value) => Fahrenheit ? (value - 32) * 5 / 9 : value;
        /// <summary>Temperature difference.</summary>
        public double ToCelsiusDelta(double value) => Fahrenheit ? value * 5 / 9 : value;
        /// <summary>Temperature gradient (difference per length).</summary>
        public double ToCelsiusPerMm(double value) => IsIdentity ? value : ToCelsiusDelta(value) / Length;

        // Back to the definition units (results shown by the components). Identity units return the value unchanged, bit for bit.
        public double FromMm(double length) => IsIdentity ? length : length / Length;
        public double FromN(double force) => IsIdentity ? force : force / Force;
        public double FromNmm(double moment) => IsIdentity ? moment : moment / (Force * Length);
        /// <summary>Force per length (plate direct and transverse shear forces per unit width).</summary>
        public double FromNPerMm(double value) => IsIdentity ? value : value * Length / Force;

        public static DefinitionUnits Parse(string name) =>
            Enum.TryParse<DefinitionUnits>(name, false, out var units) ? units : throw new ArgumentException("UnknownBridgheUnits: " + name);
    }
}
