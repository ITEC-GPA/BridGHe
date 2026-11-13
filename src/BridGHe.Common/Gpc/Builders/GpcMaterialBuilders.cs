using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.Materials;
using BridGHe.Common.Models;

namespace BridGHe.Common.Gpc.Builders
{
    /// <summary>
    /// R6.2 part C (P2N): the GPC builders of the six material components. Each component fills a <see cref="LegacyMaterialRecipe"/> (the
    /// numbers of the component, once) and gets the GPC material by kind with the one rule of the reader (<see cref="Material"/> =
    /// <see cref="LegacyModelImport.Material(LegacyMaterialRecipe)"/>). Until R6.5 the FEM value the consumers not yet on GPC read is
    /// made from the same recipe (<see cref="Femm"/>, transitional) and the GPC material is attached to it (the gates compare the two).
    /// <para>Aluminium (user decision of 6/11): fu = 262 MPa and the shear ultimate strength 165.5 MPa in its own declared field; the FEM
    /// value keeps the 0.3.0 field (165.5, written over by its shear property), a declared difference of the checks.</para>
    /// </summary>
    public static class GpcMaterialBuilders
    {
        // P2CE (13/11/2026, phase 2b): the former FEM enumerations of the recipes (names and integers as MaterialModel.MaterialKind and
        // MaterialModel.AlumiumTypes, gone with the FEM types).
        private enum MaterialKind { Undefined = -1, Steel, Concrete, Aluminium, Rebar }
        private enum AlumiumTypes { Wrought, CastMold, CastSand }

        /// <summary>The GPC material by kind of a recipe (N-mm-MPa).</summary>
        public static Material Material(LegacyMaterialRecipe recipe) => LegacyModelImport.Material(recipe ?? throw new ArgumentNullException(nameof(recipe)));

        private static readonly string[] SteelGrades = { "S235", "S275", "S355", "S450" };
        private static readonly double[,] SteelStrengths = { { 235, 360, 282, 396 }, { 275, 430, 316.3, 473 }, { 355, 510, 390.5, 561 }, { 440, 550, 484, 605 } };

        /// <summary>Steel: S235, S275, S355, S450 (grade 0–3): E 210000, ν 0.3, α 1.17E-5, G 80770, density 7.849E-9; minimum and expected
        /// yield and tensile stresses of the grade. Another grade: the material without strengths (as the former component).</summary>
        public static LegacyMaterialRecipe Steel(string name, int grade)
        {
            var r = new LegacyMaterialRecipe { Kind = "Steel", Name = name };
            if (grade < 0 || grade >= SteelGrades.Length) return r;
            r.Modulus = 2.1E+5; r.PoissonRatio = 0.3; r.ThermalExpansion = 1.170E-05; r.ShearModulus = 8.077E+4; r.UsePoisson = true; r.Density = 7.849E-9;
            r.MinimumYieldStress = SteelStrengths[grade, 0]; r.MinimumTensileStress = SteelStrengths[grade, 1];
            r.ExpectedYieldStress = SteelStrengths[grade, 2]; r.ExpectedTensileStress = SteelStrengths[grade, 3];
            return r;
        }

        private static readonly double[] ConcreteFck = { 8, 12, 16, 20, 25, 28, 32, 35, 40, 45, 50, 55, 60, 70, 80, 90 };

        /// <summary>Concrete C8/10 … C90/105 (grade 0–15): fck and expected compressive strength of the grade, ν 0.2, α 1E-5, density
        /// 2.549E-9, E = Ecm of GPC from fck (E10) and G = E/2(1+ν) (E11). Another grade: fck 0 (as the former component).</summary>
        public static LegacyMaterialRecipe Concrete(string name, int grade)
        {
            var r = new LegacyMaterialRecipe { Kind = "Concrete", Name = name };
            if (grade >= 0 && grade < ConcreteFck.Length) { r.SpecificCompressiveStrength = ConcreteFck[grade]; r.ExpectedCompressiveStrength = ConcreteFck[grade]; }
            r.PoissonRatio = 0.2; r.ThermalExpansion = 1.0E-05; r.UsePoisson = true; r.Density = 2.549E-09;
            r.Modulus = new ConcreteMaterialEN1992(name, r.SpecificCompressiveStrength, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle).ElasticModulusCompression;
            r.ShearModulus = r.Modulus / (2 * (1 + r.PoissonRatio));
            return r;
        }

        private static readonly string[] AluminiumTypes = { "Wrought", "CastMold", "CastSand" };

        /// <summary>Aluminium (type 0–2: wrought, cast mold, cast sand): E 69637.05, ν 0.33, α 2.358E-5, G 26179.344, density 2.714E-9,
        /// compressive and tensile yield 241.3, tensile ultimate 262 and shear ultimate 165.5 (user decision of 6/11: 0.3.0 stored 165.5 as
        /// the tensile ultimate strength).</summary>
        public static LegacyMaterialRecipe Aluminium(string name, int type, string alloy) => new LegacyMaterialRecipe
        {
            Kind = "Aluminium", Name = name, AluminiumType = type >= 0 && type < AluminiumTypes.Length ? AluminiumTypes[type] : ((AlumiumTypes)type).ToString(), Alloy = alloy,
            Modulus = 69637.05, PoissonRatio = 0.33, ThermalExpansion = 2.358E-05, ShearModulus = 26179.344, UsePoisson = true, Density = 2.714E-09,
            CompressiveYieldStrength = 241.3, TensileYieldStrength = 241.3, TensileUltimateStrength = 262, ShearUltimateStrength = 165.5,
        };

        /// <summary>Reinforcing steel B450C (the steel of <see cref="ReinforcementMaterials"/>): the GPC rebar of the catalogue (E 200000,
        /// fy 450, fu 540, εu 0.075, its density and α), ν 0.3, G = E/2.6, expected strengths = characteristic. A blank name: an error.</summary>
        public static LegacyMaterialRecipe Rebar(string name = "B450C")
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Provide a reinforcement material name.");
            var steel = new SteelMaterialEN1992(name.Trim(), 200000, 450, 540, .075, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
            return new LegacyMaterialRecipe
            {
                Name = steel.Name, Kind = "Rebar", Modulus = steel.ElasticModulusTension, PoissonRatio = .3, UsePoisson = true, ShearModulus = steel.ElasticModulusTension / 2.6,
                Density = steel.Density, ThermalExpansion = steel.AlfaThermalExpansion, MinimumYieldStress = 450, MinimumTensileStress = 540,
                ExpectedYieldStress = 450, ExpectedTensileStress = 540, ReinforcementUltimateStrain = steel.StrainUTension,
            };
        }

        /// <summary>Custom material (kind −1 undefined, 0 steel, 1 concrete, 2 aluminium; values already in N-mm-MPa, t/mm³): steel takes
        /// fk/ftk as minimum yield/tensile and 1.1 times them as expected, concrete fk as fck, aluminium and undefined no strengths.
        /// Another kind: an error (as the former component).</summary>
        public static LegacyMaterialRecipe Custom(int kind, string name, double modulus, double poisson, double alpha, double shearModulus, bool usePoisson, double density, double fk, double ftk)
        {
            var k = (MaterialKind)kind;
            var r = new LegacyMaterialRecipe { Kind = k.ToString(), Name = name, Modulus = modulus, PoissonRatio = poisson, ThermalExpansion = alpha, ShearModulus = shearModulus, UsePoisson = usePoisson, Density = density };
            switch (k)
            {
                // The former value of an aluminium always had a manufacturing form (its default: wrought), which the reader declares.
                case MaterialKind.Aluminium: r.AluminiumType = AlumiumTypes.Wrought.ToString(); break;
                case MaterialKind.Concrete: r.SpecificCompressiveStrength = fk; break;
                case MaterialKind.Steel: r.MinimumYieldStress = fk; r.MinimumTensileStress = ftk; r.ExpectedYieldStress = 1.1 * fk; r.ExpectedTensileStress = 1.1 * ftk; break;
                case MaterialKind.Undefined: break;
                default: throw new NotSupportedException();
            }
            return r;
        }

        /// <summary>Table material: a material without a kind and without E (the former component), with ν, G, Use Poisson, α, density and
        /// the stress-strain table.</summary>
        public static LegacyMaterialRecipe Table(string name, bool usePoisson, double poisson, double shearModulus, double alpha, double density, IReadOnlyList<(double stress, double strain)> table) => new LegacyMaterialRecipe
        {
            Name = name, PoissonRatio = poisson, ThermalExpansion = alpha, ShearModulus = shearModulus, UsePoisson = usePoisson, Density = density, StressStrainTable = table?.ToArray(),
        };

    }
}
