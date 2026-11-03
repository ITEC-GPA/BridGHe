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
    }
}
