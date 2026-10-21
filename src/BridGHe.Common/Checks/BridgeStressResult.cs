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
        public BridgeResultSource Source { get; }
        private readonly int stageIndex;
        private readonly Lazy<Func<double,double>> shear;
        private readonly Lazy<double[]> rebarRows;
        public BridgeStressResult(HBridgeAnalysisResult analysis, int stage) : this(analysis, stage, null) { }

        public BridgeStressResult(HBridgeAnalysisResult analysis, int stage, BridgeResultSource source)
        {
            Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis)); Stage = analysis.Stages[stage];
            stageIndex = stage; Source = source;
            shear = new Lazy<Func<double,double>>(CreateShearEvaluator);
            rebarRows = new Lazy<double[]>(()=>Analysis.Geometry.Bars.Select(b=>b.Y).Distinct().OrderByDescending(y=>y).ToArray());
        }
        /// <summary>Attach provenance without recomputing or modifying the numerical analysis.</summary>
        public BridgeStressResult WithSource(BridgeResultSource source) => new BridgeStressResult(Analysis, stageIndex, source);
        private double Sigma(string material, double y) => Stage.Contributions.Sum(c => c.Stress(material, y));
        private double Steel(double y) => Sigma("Acciaio", y);
        private double Rebar(int index)
        {
            var rows = rebarRows.Value;
            return index < rows.Length ? Sigma("Armatura", rows[index]) : double.NaN;
        }
        public double Point1_ConcreteSigma => Sigma("CLS", Analysis.Geometry.SlabHeight);
        public double Point1_ConcreteEpsilon => Stage.GetHistory() is { } history ? history.State.TotalPlane.At(Analysis.Geometry.SlabHeight) : Stage.Contributions.Where(c => c.HasConcrete)
            .Sum(c => c.SteelStress(Analysis.Geometry.SlabHeight) / Analysis.Materials.Ea);
        public double Point2_RebarSigma => Rebar(0);
        public double Point3_RebarSigma => Rebar(1);
        public double Point4_ConcreteSigma => Sigma("CLS", 0);
        public double Point5_SteelSigma => Steel(0);
        public double Point6_SteelSigma => Steel(-Analysis.Geometry.TopThickness);
        public double Point7_SteelSigma => Steel(-Analysis.Geometry.TopThickness - Analysis.Geometry.WebHeight);
        // Same gross-section Jourawski calculation as current gPC HBridgeSection.Shear.
        // Use public gPC geometry/properties, evaluating at the actual point (not the envelope).
        public double ShearAt(double y) => Stage.GetHistory() != null ? double.NaN : shear.Value(y);

        // A result represents an analysis snapshot. Build section properties once,
        // preserving the original summation order at each evaluation coordinate.
        private Func<double,double> CreateShearEvaluator()
        {
            var g = Analysis.Geometry; var m = Analysis.Materials;
            if(g.SectionType!=BridgeSteelSectionType.H)
                throw new NotSupportedException("Pointwise legacy tau is defined only for a vertical-web H. Use the native Bridge Shear Results for inclined webs and boxes, including torsion.");
            var parts = HBridgeSection.SteelPartProperties(g);
            var phases = Analysis.Input.Phases.Where(p=>p.Active).ToArray();
            var properties = Stage.Contributions.Select((c,i)=>c.IsShrinkage?null:HBridgeSection.GrossPhaseProperties(Analysis.Input,phases[i])).ToArray();
            return y =>
            {
                double total = 0;
                for (int i = 0; i < Stage.Contributions.Count; i++)
                {
                    var c = Stage.Contributions[i]; if (c.IsShrinkage) continue;
                    var phase = phases[i]; var p = properties[i];
                    double firstMoment = parts.Sum(part => { double low = Math.Max(y, part.Bottom), height = Math.Max(0, part.Top - low); return part.Width * height * ((part.Top + low) / 2 - p.Y); });
                    if (c.HasConcrete) firstMoment += g.Width * g.SlabHeight / c.HomogenizationN * (g.SlabHeight / 2 - p.Y);
                    if (phase.Kind != BridgePhaseKind.SteelOnly) firstMoment += g.Bars.Sum(b => b.Area * (m.Es / m.Ea - (c.HasConcrete ? 1 / c.HomogenizationN : 0)) * (b.Y - p.Y));
                    total += c.V * 1000 * firstMoment / (p.Ix * g.WebCount * g.PlateThickness);
                }
                return total;
            };
        }
        public double Point6_SteelTau => ShearAt(-Analysis.Geometry.TopThickness);
        public double Point7_SteelTau => ShearAt(-Analysis.Geometry.TopThickness - Analysis.Geometry.WebHeight);
        public double Point6_SteelSigmaId => Math.Sqrt(Point6_SteelSigma * Point6_SteelSigma + 3 * Point6_SteelTau * Point6_SteelTau);
        public double Point7_SteelSigmaId => Math.Sqrt(Point7_SteelSigma * Point7_SteelSigma + 3 * Point7_SteelTau * Point7_SteelTau);
        public double Point8_SteelSigma => Steel(-Analysis.Geometry.Height);
        public double Point8_SteelEpsilon => Point8_SteelSigma / Analysis.Materials.Ea;
        public override string ToString() => $"gPC {Stage.Name}: {Analysis.Geometry.SectionType}, tensioni MPa";
    }
}
