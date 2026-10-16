using System;
using System.Linq;
using GPC.Checkers.CompositeBridge;

namespace BridGHe.Common.Checks
{
    /// <summary>Retains the historical output slots while exposing the complete current gPC result.</summary>
    public sealed class BridgeStressResult
    {
        public HBridgeAnalysisResult Analysis { get; }
        public BridgeStage Stage { get; }
        public BridgeStressResult MeCheckResults => this;
        public BridgeStressResult(HBridgeAnalysisResult analysis, int stage) { Analysis = analysis; Stage = analysis.Stages[stage]; }
        private double Sigma(string material, double y) => Stage.Contributions.Sum(c => c.Stress(material, y));
        private double Steel(double y) => Sigma("Acciaio", y);
        private double Rebar(int index)
        {
            var rows = Analysis.Geometry.Bars.Select(b => b.Y).Distinct().OrderByDescending(y => y).ToArray();
            return index < rows.Length ? Sigma("Armatura", rows[index]) : double.NaN;
        }
        public double Point1_ConcreteSigma => Sigma("CLS", Analysis.Geometry.SlabHeight);
        public double Point1_ConcreteEpsilon => Stage.Contributions.Where(c => c.HasConcrete)
            .Sum(c => c.SteelStress(Analysis.Geometry.SlabHeight) / Analysis.Materials.Ea);
        public double Point2_RebarSigma => Rebar(0);
        public double Point3_RebarSigma => Rebar(1);
        public double Point4_ConcreteSigma => Sigma("CLS", 0);
        public double Point5_SteelSigma => Steel(0);
        public double Point6_SteelSigma => Steel(-Analysis.Geometry.TopThickness);
        public double Point7_SteelSigma => Steel(-Analysis.Geometry.TopThickness - Analysis.Geometry.WebHeight);
        // Same gross-section Jourawski calculation as current gPC HBridgeSection.Shear.
        // Use public gPC geometry/properties, evaluating at the actual point (not the envelope).
        public double ShearAt(double y)
        {
            var g = Analysis.Geometry; var m = Analysis.Materials;
            var parts = HBridgeSection.SteelPartProperties(g); double total = 0;
            for (int i = 0; i < Stage.Contributions.Count; i++)
            {
                var c = Stage.Contributions[i]; if (c.IsShrinkage) continue;
                var phase = Analysis.Input.Phases[i];
                var p = HBridgeSection.GrossPhaseProperties(Analysis.Input, phase);
                double firstMoment = parts.Sum(part => { double low = Math.Max(y, part.Bottom), height = Math.Max(0, part.Top - low); return part.Width * height * ((part.Top + low) / 2 - p.Y); });
                if (c.HasConcrete) firstMoment += g.Width * g.SlabHeight / c.HomogenizationN * (g.SlabHeight / 2 - p.Y);
                if (phase.Kind != BridgePhaseKind.SteelOnly) firstMoment += g.Bars.Sum(b => b.Area * (m.Es / m.Ea - (c.HasConcrete ? 1 / c.HomogenizationN : 0)) * (b.Y - p.Y));
                total += c.V * 1000 * firstMoment / (p.Ix * g.WebCount * g.PlateThickness);
            }
            return total;
        }
        public double Point6_SteelTau => ShearAt(-Analysis.Geometry.TopThickness);
        public double Point7_SteelTau => ShearAt(-Analysis.Geometry.TopThickness - Analysis.Geometry.WebHeight);
        public double Point6_SteelSigmaId => Math.Sqrt(Point6_SteelSigma * Point6_SteelSigma + 3 * Point6_SteelTau * Point6_SteelTau);
        public double Point7_SteelSigmaId => Math.Sqrt(Point7_SteelSigma * Point7_SteelSigma + 3 * Point7_SteelTau * Point7_SteelTau);
        public double Point8_SteelSigma => Steel(-Analysis.Geometry.Height);
        public double Point8_SteelEpsilon => Point8_SteelSigma / Analysis.Materials.Ea;
        public override string ToString() => $"gPC {Stage.Name}: N–Mx–Vy, tensioni MPa";
    }
}
