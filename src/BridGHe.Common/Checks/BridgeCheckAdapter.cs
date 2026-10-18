using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Checkers.CompositeBridge;
using GPC.Geometry;
using GPC.Model.Materials;
using GPC.Model.Results;
using GPC.Model.Sections;
using GPC.Model.Sections.Concrete;

namespace BridGHe.Common.Checks
{
    /// <summary>Adapter from Straus section forces to the gPC bridge engine.
    /// Geometry is in mm; actions are N and Nmm in the supplied reference system.</summary>
    public sealed class BridgeCheckAdapter
    {
        private readonly ReinforcedConcreteSection section;
        private readonly CoordinateSystem reference;
        private readonly BridgeAnalysisOptions options;

        public BridgeCheckAdapter(ReinforcedConcreteSection section, CoordinateSystem reference,
            BridgeAnalysisOptions options = null)
        {
            this.section = section ?? throw new ArgumentNullException(nameof(section));
            this.reference = reference ?? throw new ArgumentNullException(nameof(reference));
            this.options = options ?? new BridgeAnalysisOptions
            { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare };
        }

        private HBridgeInput Input(bool concrete, bool rebars, bool buckling)
        {
            if (section.SteelSections.Count != 1 || !(section.SteelSections[0].Section.SectionShape is SectionH h))
                throw new NotSupportedException("L'adattatore BridgeChecks supporta una singola sezione ad H con soletta rettangolare.");
            var positioned = section.SteelSections[0];
            if (Math.Abs(positioned.Rotation) > 1e-10 || Math.Abs(section.Jxy) > 1e-10 * (section.Jxx + section.Jyy))
                throw new NotSupportedException("Sezione ruotata o non simmetrica: adattamento del verificatore da completare.");
            var rows = section.Rebars.GroupBy(b => Math.Round(b.Position.Y, 8)).OrderByDescending(g => g.Key).ToArray();
            if (rows.Length > 2) throw new NotSupportedException("Sono supportate al massimo due file di armatura.");
            BridgeRebarRow Row(int index)
            {
                if (!rebars || index >= rows.Length) return new BridgeRebarRow();
                var bars = rows[index].ToArray();
                if (bars.Any(b => Math.Abs(b.RebarSection.Diameter - bars[0].RebarSection.Diameter) > 1e-8))
                    throw new NotSupportedException("Diametri diversi nella stessa fila non supportati dall'adattatore.");
                return new BridgeRebarRow
                {
                    Enabled = true,
                    Diameter = bars[0].RebarSection.Diameter,
                    Pitch = section.Width / bars.Length,
                    AxisDistance = index == 0 ? section.Height - bars[0].Position.Y : bars[0].Position.Y
                };
            }
            var steel = positioned.Section.SteelMaterial;
            var rebar = section.Rebars.FirstOrDefault()?.RebarSection.RebarMaterial ?? steel;
            return new HBridgeInput
            {
                Materials = new BridgeMaterialSet((ConcreteMaterialEN1992)section.ConcreteMaterial, steel, rebar),
                Geometry = new HSectionDimensions
                {
                    SlabWidth = section.Width,
                    SlabHeight = section.Height,
                    WebHeight = h.HeightWeb,
                    WebThickness = h.ThicknessWeb,
                    TopWidth = h.LenghtTopFlange,
                    TopThickness = h.ThicknessTopFlange,
                    BottomWidth = h.LenghtBottomFlange,
                    BottomThickness = h.ThicknessBottomFlange
                },
                TopRebars = Row(0),
                BottomRebars = Row(1),
                Options = options with
                {
                    Class4 = buckling && options.Class4,
                    CommonLoadY = reference.Origin.Y
                }
            };
        }

        private BridgePhase Phase(ResultBeamForces forces, BridgePhaseKind kind, double phi, string name)
        {
            if (forces == null) throw new ArgumentNullException(nameof(forces));
            // Respect the supplied local axes. Native symmetric grillages retain
            // round-off torsion (~1e-6 N mm); use dimensional tolerances, not the
            // same near-machine-zero number for both forces and moments.
            var global = forces.ToGlobalCoordinateSystem();
            if (new[] { global.N, global.V1, global.V2, global.T, global.M1, global.M2 }.Any(value => !double.IsFinite(value)))
                throw new ArgumentException("Azioni di sezione non finite.", nameof(forces));
            const double forceNoiseN = 1e-6, momentNoiseNmm = 1e-3;
            if (Math.Abs(global.M2) > momentNoiseNmm || Math.Abs(global.T) > momentNoiseNmm || Math.Abs(global.V1) > forceNoiseN)
                throw new NotSupportedException($"Il motore gPC adottato copre N–Mx–Vy. Flessione deviata, taglio trasversale e torsione richiedono un verificatore dedicato. " +
                    $"M2={global.M2:G6}, T={global.T:G6} N mm (tolleranza numerica {momentNoiseNmm:G}); V1={global.V1:G6} N (tolleranza {forceNoiseN:G}).");
            if (phi < 0 || !double.IsFinite(phi)) throw new ArgumentOutOfRangeException(nameof(phi), "Il rapporto modulare deve dare phi >= 0.");
            return new BridgePhase
            {
                Name = name,
                Kind = kind,
                ForceKN = global.N / 1000,
                MomentKNm = global.M1 / 1e6,
                ShearKN = global.V2 / 1000,
                Phi = phi,
                // Straus end forces are about the gross centroid of each phase section.
                // The bridge engine transports them to the effective centroid if required.
                Reference = BridgeLoadReference.GrossCentroid
            };
        }

        public bool GetLinearAnalysisResultByStages(out BridgeStressResult steelResult, out BridgeStressResult infiniteResult,
            out BridgeStressResult result, out double tauB, double phiInfinite, double phiInstant,
            ResultBeamForces steel, ResultBeamForces infinite, ResultBeamForces instant, bool concrete)
        {
            var analysis = Analyze(phiInfinite, phiInstant, steel, infinite, instant, concrete, true, 0, 0);
            steelResult = new BridgeStressResult(analysis, 0);
            infiniteResult = new BridgeStressResult(analysis, 1);
            result = new BridgeStressResult(analysis, analysis.Stages.Count - 1);
            tauB = double.NaN;
            return true;
        }

        public bool GetLinearAnalysisResultByStages(out BridgeStressResult[] results, out double[] tauB,
            double phiInfinite, double phiInstant, ResultBeamForces[] steel, ResultBeamForces[] infinite,
            ResultBeamForces[] instant, bool concrete, double[] strainInfinite, double[] strainInstant, bool rebars)
        {
            int count = steel.Length;
            if (infinite.Length != count || instant.Length != count || strainInfinite.Length != count || strainInstant.Length != count)
                throw new ArgumentException("Le serie di fasi e deformazioni devono avere la stessa lunghezza.");
            results = new BridgeStressResult[count]; tauB = new double[count];
            for (int i = 0; i < count; i++)
            {
                var analysis = Analyze(phiInfinite, phiInstant, steel[i], infinite[i], instant[i], concrete, rebars, strainInfinite[i], strainInstant[i]);
                results[i] = new BridgeStressResult(analysis, analysis.Stages.Count - 1);
                tauB[i] = double.NaN;
            }
            return true;
        }

        private HBridgeAnalysisResult Analyze(double phiInfinite, double phiInstant, ResultBeamForces steel,
            ResultBeamForces infinite, ResultBeamForces instant, bool concrete, bool rebars, double strainInfinite, double strainInstant)
        {
            var data = Input(concrete, rebars, true);
            var kind = concrete ? BridgePhaseKind.Composite : BridgePhaseKind.ConcreteExcluded;
            var phases = new List<BridgePhase> { Phase(steel, BridgePhaseKind.SteelOnly, 0, "Only Steel"),
                Phase(infinite, kind, phiInfinite, "Infinite Time"), Phase(instant, kind, phiInstant, "Instant Time") };
            if (!concrete && (strainInfinite != 0 || strainInstant != 0)) throw new NotSupportedException("Ritiro con soletta esclusa: definire prima la sequenza fisica delle fasi.");
            if (strainInfinite != 0) phases.Add(new BridgePhase { Name = "Strain Infinite", Kind = BridgePhaseKind.Shrinkage, Phi = phiInfinite, ShrinkageMicrostrain = strainInfinite * 1e6 });
            if (strainInstant != 0) phases.Add(new BridgePhase { Name = "Strain Instant", Kind = BridgePhaseKind.Shrinkage, Phi = phiInstant, ShrinkageMicrostrain = strainInstant * 1e6 });
            return HBridgeSection.Calculate(data with { Phases = phases.ToArray() });
        }

        public BridgeStressResult GetSelfTension(double strain, double phi)
        {
            var data = Input(true, true, false) with
            {
                Phases = new[] { new BridgePhase {
                Name = "Self tension", Kind = BridgePhaseKind.Shrinkage, Phi = phi, ShrinkageMicrostrain = strain * 1e6 } }
            };
            return new BridgeStressResult(HBridgeSection.Calculate(data), 0);
        }

        public static ResultBeamForces Scale(ResultBeamForces f, double factor) => new ResultBeamForces(
            f.N * factor, f.V1 * factor, f.V2 * factor, f.T * factor, f.M1 * factor, f.M2 * factor, f.CoordinateSystem, f.Id, f.Name);
    }
}
