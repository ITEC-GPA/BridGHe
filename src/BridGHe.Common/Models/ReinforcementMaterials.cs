using System;
using GPC.Model.Materials;

namespace BridGHe.Common.Models;

/// <summary>Characteristic B450C data, matching the GPC ModelData catalogue. N-mm-MPa, t/mm³.</summary>
public static class ReinforcementMaterials
{
    public static MaterialModel B450C(string name = "B450C")
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Provide a reinforcement material name.");
        var steel = new SteelMaterialEN1992(name.Trim(), 200000, 450, 540, .075,
            SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
        return new MaterialModel
        {
            Name = steel.Name, Kind = MaterialModel.MaterialKind.Rebar,
            Modulus = steel.ElasticModulusTension, PoissonRatio = .3, UsePoisson = true,
            ShearModulus = steel.ElasticModulusTension / 2.6,
            Density = steel.Density, ThermalExpansion = steel.AlfaThermalExpansion,
            MinimumYieldStress = 450, MinimumTensileStress = 540,
            ExpectedYieldStress = 450, ExpectedTensileStress = 540,
            ReinforcementUltimateStrain = steel.StrainUTension
        };
    }
}
