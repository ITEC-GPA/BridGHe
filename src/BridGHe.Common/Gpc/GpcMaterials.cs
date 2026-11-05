using System;
using System.Linq;
using GPC.Model.Materials;

namespace BridGHe.Common.Gpc
{
    /// <summary>GPC materials from the BridGHe material data, already in N-mm-t (MPa, t/mm³, 1/°C).</summary>
    public static class GpcMaterials
    {
        /// <param name="usePoisson">True: G = E/2(1+ν) (the native default mode of the former exporter); false: explicit G.</param>
        /// <param name="conductivity">Former value, in the J-based unit Straus7 received; GPC stores mJ-based values (×1000).</param>
        /// <param name="stressStrain">Legacy table as (stress, strain) pairs, or null.</param>
        public static Material Elastic(string name, double modulus, double poisson, double shearModulus, bool usePoisson, double density,
            double thermalExpansion, double viscousDamping, double dampingRatio, double conductivity, double specificHeat,
            (double stress, double strain)[] stressStrain)
        {
            var material = new Material(string.IsNullOrWhiteSpace(name) ? "Material" : name, modulus, poisson, density, thermalExpansion);
            bool analysis = !usePoisson || viscousDamping != 0 || dampingRatio != 0 || conductivity != 0 || specificHeat != 0 || stressStrain != null;
            if (analysis)
            {
                material.AnalysisData = new MaterialAnalysisData
                {
                    BeamShearModulus = usePoisson ? (double?)null : shearModulus,
                    ViscousDampingDensity = viscousDamping,
                    ModalDampingRatio = dampingRatio,
                    ThermalConductivity = conductivity * 1000,
                    SpecificHeat = specificHeat * 1000,
                    // The former tables were exported with the native default response (elastoplastic): review L2, question 3.
                    StressStrainPairs = stressStrain?.SelectMany(p => new[] { p.strain, p.stress }).ToArray(),
                    NonlinearResponse = MaterialNonlinearResponse.Elastoplastic,
                };
            }
            return material;
        }

        /// <summary>M1 (coordinator, 6/11): the GPC material by kind of a former material (§1bis): the archive of the FEM value read by
        /// <see cref="Legacy.LegacyModelImport.Material"/>, the reader of the BGHA1 payloads. One rule for every material of BridGHe.</summary>
        public static Material FromFemm(Models.MaterialModel material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            return Legacy.LegacyModelImport.Material(Legacy.LegacyArchive.Read(Helpers.ModelArchive.Write(material)));
        }

        /// <summary>M1: the analysis material of a beam or plate property from the GPC material of a Material parameter: the generic elastic
        /// material of <see cref="Elastic"/> (name, E, ν, density, thermal expansion, analysis data), bit for bit the one the reader builds from
        /// the former material (<see cref="Legacy.LegacyModelImport.ElasticMaterial"/>; regression <c>gpc-material-kinds</c>). The strength data
        /// and the declarations of a typed material do not enter the analysis.</summary>
        public static Material Analysis(Material material)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            var analysis = new Material(string.IsNullOrWhiteSpace(material.Name) ? "Material" : material.Name, material.ElasticModulusCompression, material.Ni,
                material.Density, material.AlfaThermalExpansion);
            if (material.AnalysisData != null) analysis.AnalysisData = GPC.Model.Core.ModelValues.CopyValue(material.AnalysisData);
            return analysis;
        }

        /// <summary>M1: the steel values the checks read (the former builders read the name, E, minimum yield and tensile stresses and the
        /// reinforcement ultimate strain of any former material): of a steel, its E, fy, fu and εu; of a generic material (a former material
        /// without a kind, e.g. the steels the example generators internalise), its E and the yield and ultimate stresses in tension and the
        /// ultimate strain that <see cref="Legacy.LegacyModelImport.Material"/> keeps there (εu 0.1 when none). Another kind: an error (the former
        /// builders read zeros: a concrete or an aluminium as a steel).</summary>
        public static (string Name, double E, double Fy, double Fu, double StrainU) SteelValues(Material material)
        {
            if (material is SteelMaterial s) return (s.Name, s.ElasticModulusTension, s.Fyk, s.Fu, s.StrainUTension);
            if (material != null && material.GetType() == typeof(Material) && (material.StressYTension != 0 || material.StressUTension != 0))
                return (material.Name, material.ElasticModulusTension, material.StressYTension, material.StressUTension, material.StrainUTension == 0 ? 0.1 : material.StrainUTension);
            // A generic material without the strengths of a steel (an analysis material): refused (the former builders gave fy = fu = 0).
            throw new ArgumentException("A steel material is required: " + material?.Name + " (" + material?.GetType().Name + ")");
        }

        /// <summary>M1: the concrete values the checks read (the former builders read the name and fck): of a concrete, its name and fck; of a
        /// generic material, the compressive stress that <see cref="Legacy.LegacyModelImport.Material"/> keeps for the fck of a former material
        /// without a kind. Another kind: an error.</summary>
        public static (string Name, double Fck) ConcreteValues(Material material)
        {
            // Fck of GPC is negative (compression); the constructors take -|fck|, so |fck| gives the same concrete.
            if (material is ConcreteMaterialEuropeanCommon c) return (c.Name, Math.Abs(c.Fck));
            if (material != null && material.GetType() == typeof(Material) && material.StressYCompression != 0) return (material.Name, Math.Abs(material.StressYCompression));
            throw new ArgumentException("A concrete material is required: " + material?.Name + " (" + material?.GetType().Name + ")");
        }

        /// <summary>M1: the structural steel of the checks (Bridge Section Check, composite sections): the rule of the former builders,
        /// <see cref="SteelMaterialEN1992"/> with the name, E, fy and fu of the material (<see cref="SteelValues"/>), εu 0.1, elastic perfectly
        /// plastic, and the defaults of the GPC type for ν, density and thermal expansion.</summary>
        public static SteelMaterialEN1992 StructuralSteel(Material material)
        {
            var v = SteelValues(material);
            return new SteelMaterialEN1992(v.Name, v.E, v.Fy, v.Fu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
        }

        /// <summary>M1: the bars of the checks: the rule of the former builders, <see cref="SteelMaterialEN1992"/> with the name, E, fy, fu
        /// and εu of the material (<see cref="SteelValues"/>: the reinforcement ultimate strain of B450C, 0.1 for a steel without it), elastic
        /// perfectly plastic, rebar, the defaults of the GPC type for ν, density and thermal expansion. (The definitions of the composite sections
        /// give their bars εu 0.1: listed difference of P2H.)</summary>
        public static SteelMaterialEN1992 Reinforcement(Material material)
        {
            var v = SteelValues(material);
            return new SteelMaterialEN1992(v.Name, v.E, v.Fy, v.Fu, v.StrainU, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
        }

        /// <summary>M1: the concrete of the checks: the rule of the former builders, <see cref="ConcreteMaterialEN1992"/> from the name and fck
        /// of the material (<see cref="ConcreteValues"/>, parabola-rectangle), E = Ecm.</summary>
        public static ConcreteMaterialEN1992 Concrete(Material material)
        {
            var v = ConcreteValues(material);
            return new ConcreteMaterialEN1992(v.Name, v.Fck, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
        }
    }
}
