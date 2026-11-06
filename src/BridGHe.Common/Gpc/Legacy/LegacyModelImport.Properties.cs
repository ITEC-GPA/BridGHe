using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Geometry;
using GPC.Model.Attributes;
using GPC.Model.Combinations;
using GPC.Model.Constraints;
using GPC.Model.Core.Identity;
using GPC.Model.ElementProperties;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Materials;
using GPC.Model.Models;
using GPC.Model.Restraints;
using GPC.Model.Sections;
using GPC.Model.Structure.Assignments;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>R6.1 (lanes of R6.2): the readers of the former beam and plate properties, sections and materials.</summary>
    /// <summary>
    /// R6.2 part C (P2N): the data of a material as the former material value held them (N-mm-MPa, t/mm³, 1/°C), the input of the one rule
    /// that makes the GPC material by kind (<see cref="LegacyModelImport.Material(LegacyMaterialRecipe)"/>) and the analysis material
    /// (<see cref="LegacyModelImport.ElasticMaterial(LegacyMaterialRecipe)"/>): read from a former payload (<see cref="LegacyModelImport.MaterialRecipe"/>)
    /// or filled by the GPC builders of the material components. Kind: "Undefined", "Steel", "Rebar", "Concrete", "Aluminium" (the former
    /// names); aluminium type: "Wrought", "CastMold", "CastSand". Defaults: those of the former material value (zeros, Use Poisson).
    /// </summary>
    public sealed class LegacyMaterialRecipe
    {
        public string Kind { get; set; } = "Undefined";
        public string Name { get; set; }
        public double Modulus { get; set; }
        public double PoissonRatio { get; set; }
        public double ShearModulus { get; set; }
        public bool UsePoisson { get; set; } = true;
        public double Density { get; set; }
        public double ThermalExpansion { get; set; }
        public double ViscousDamping { get; set; }
        public double DampingRatio { get; set; }
        public double Conductivity { get; set; }
        public double SpecificHeat { get; set; }
        public (double stress, double strain)[] StressStrainTable { get; set; }
        public double MinimumYieldStress { get; set; }
        public double MinimumTensileStress { get; set; }
        public double SpecificCompressiveStrength { get; set; }
        public double? ReinforcementUltimateStrain { get; set; }
        public double ExpectedYieldStress { get; set; }
        public double ExpectedTensileStress { get; set; }
        public double ExpectedCompressiveStrength { get; set; }
        public double CompressiveYieldStrength { get; set; }
        public double TensileYieldStrength { get; set; }
        public double TensileUltimateStrength { get; set; }
        public double ShearUltimateStrength { get; set; }
        public string Alloy { get; set; }
        public string AluminiumType { get; set; }
    }

    public static partial class LegacyModelImport
    {
        private static string PlatePropertyName(LegacyValue pp) => pp.Any("Name", "_name").Text ?? pp.Any("Id", "_id").Text ?? "Plate property";

        private static string PropertyName(LegacyValue p) => p.String("Name") ?? p.String("Id") ?? "Property";
        /// <summary>Content of a legacy property as text (to detect different properties with the same name).</summary>
        private static string Signature(LegacyValue v)
        {
            if (v.IsNull) return "null";
            if (v.Text != null) return v.Text;
            if (v.IsCollection) return "[" + string.Join(",", v.Items.Select(Signature)) + "]";
            return "{" + string.Join(";", v.Fields.Where(f => f.Key != "CompositePreview").OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + "=" + Signature(f.Value))) + "}";
        }

        /// <summary>F6 chain D: a former beam property converted on its own, with the rules of the conversion of a whole model (the model
        /// transformers that replace the property of beams, e.g. Bridge Phase Model); dimensions in the definition <paramref name="units"/>.</summary>
        public static LegacyBeamProperty BeamProperty(LegacyValue property, BridgeUnits units)
        {
            if (property == null || property.IsNull || !property.Is("BeamPropertyModel")) throw new ArgumentException("LegacyBeamPropertyModelExpected");
            return BeamProperty(property, PropertyName(property), units, new Dictionary<string, (double, double)>(StringComparer.Ordinal), new LegacyImportReport());
        }

        /// <summary>
        /// R6.3 (bridge 3 removed, decision 4 of the pre-removal point): the signature of a beam property in the drafts, the reports and the
        /// registry of Build Model: the GPC fingerprint of the property ("GPC:" prefix), one rule for the reader, the GPC builders and the model
        /// transformers (before: the digest of the former content for a former value, the GPC fingerprint for a property built in GPC, so that
        /// Build Model took the property data of the conversion of the FEM value of the drafts of Bridge State Override).
        /// </summary>
        public static string PropertySignature(BeamProperty property) =>
            // On a copy: the fingerprint reads the lazy members of a section (its shape), which would change the property it signs.
            "GPC:" + GPC.Model.Core.ModelValues.Fingerprint(new object[] { GPC.Model.Core.ModelValues.CopyValue(property ?? throw new ArgumentNullException(nameof(property))) });

        /// <summary>The GPC material of a beam property of the former exporter (<see cref="GpcMaterials.Elastic"/>: E, ν, G with Use Poisson,
        /// density, thermal expansion, damping, conductivity, specific heat, stress-strain table), the one rule of the reader for every beam
        /// property; chain E: the steel of the analysis properties of a composite section (<c>GpcModels.Material</c>).</summary>
        public static Material ElasticMaterial(LegacyValue m) => ElasticMaterial(ElasticRecipe(m));

        /// <summary>R6.2 part C: the analysis material of a material recipe (the elastic fields; the rule of <see cref="ElasticMaterial(LegacyValue)"/>).</summary>
        public static Material ElasticMaterial(LegacyMaterialRecipe r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            return GpcMaterials.Elastic(r.Name, r.Modulus, r.PoissonRatio, r.ShearModulus, r.UsePoisson, r.Density, r.ThermalExpansion, r.ViscousDamping, r.DampingRatio,
                r.Conductivity, r.SpecificHeat, r.StressStrainTable);
        }

        /// <summary>The elastic fields of a former material, read in the order of the former reader.</summary>
        private static LegacyMaterialRecipe ElasticRecipe(LegacyValue m)
        {
            if (m == null || m.IsNull) throw new ArgumentException("LegacyMaterialModelExpected");
            return new LegacyMaterialRecipe
            {
                Name = m.String("Name"), Modulus = m.Double("Modulus"), PoissonRatio = m.Double("PoissonRatio"), ShearModulus = m.Double("ShearModulus"), UsePoisson = m.Bool("UsePoisson"),
                Density = m.Double("Density"), ThermalExpansion = m.Double("ThermalExpansion"), ViscousDamping = m.Double("ViscousDamping"), DampingRatio = m.Double("DampingRatio"),
                Conductivity = m.Double("Conductivity"), SpecificHeat = m.Double("SpecificHeat"), StressStrainTable = StressStrain(m["StressStrainTable"]),
            };
        }

        /// <summary>R6.2 part C: every field of a former material the reader reads (<see cref="Material(LegacyValue)"/>). The former
        /// <c>ShearUltimateStrength</c> property read the tensile ultimate field: a payload gives that value as the shear ultimate strength
        /// (the value at the ports of 0.3.0), as it is stored.</summary>
        public static LegacyMaterialRecipe MaterialRecipe(LegacyValue m)
        {
            var r = ElasticRecipe(m);
            r.Kind = m.Enum("MaterialKind");
            r.MinimumYieldStress = m.Double("MinimumYieldStress"); r.MinimumTensileStress = m.Double("MinimumTensileStress");
            r.SpecificCompressiveStrength = m.Double("SpecificCompressiveStrength");
            r.ReinforcementUltimateStrain = m.Has("ReinforcementUltimateStrain") ? m.Double("ReinforcementUltimateStrain") : (double?)null;
            r.ExpectedYieldStress = m.Double("ExpectedYieldStress"); r.ExpectedTensileStress = m.Double("ExpectedTensileStress");
            r.ExpectedCompressiveStrength = m.Double("ExpectedCompressiveStrength");
            r.CompressiveYieldStrength = m.Double("CompressiveYieldStrength"); r.TensileYieldStrength = m.Double("TensileYieldStrength");
            r.TensileUltimateStrength = m.Double("TensileUltimateStrength"); r.ShearUltimateStrength = r.TensileUltimateStrength;
            r.Alloy = m.String("Alloy");
            r.AluminiumType = r.Kind == "Aluminium" ? m.Enum("AlumiumType") : null;
            return r;
        }

        /// <summary>
        /// M1 (coordinator, 6/11; §1bis, row <c>GH_Material</c>): the GPC material of a former material by kind, the datum of a Material
        /// parameter (<see cref="GpcMaterials.FromFemm"/> reads the archive of a FEM value with this function; a BGHA1 payload is read with it
        /// as data). Steel and rebar (B450C): <see cref="SteelMaterialEN1992"/> with E, fy = minimum yield, fu = minimum tensile, εu = the
        /// reinforcement ultimate strain or 0.1, elastic perfectly plastic, structural or rebar; concrete: <see cref="ConcreteMaterialEN1992"/>
        /// from fck (parabola-rectangle); aluminium: <see cref="AluminiumMaterialEN1999"/> (fo = tensile yield, fu = tensile ultimate, as
        /// stored); undefined (custom without a kind, table, the materials of the example generators): the generic material of <see cref="ElasticMaterial"/>, with the characteristic strengths the former check builders read in its stresses. Every typed material also
        /// carries the analysis values of the former material (ν, density, thermal expansion, and in <see cref="Material.AnalysisData"/> the
        /// G without Use Poisson, damping, conductivity, specific heat and table, as <see cref="ElasticMaterial"/>) and its declared values
        /// (<see cref="MaterialDeclarations"/>: every declared value of the former material: expected strengths, aluminium strengths, alloy and form), so that
        /// <see cref="GpcMaterials.Analysis"/> gives back the analysis material bit for bit; the check materials are built from it by
        /// <see cref="GpcMaterials.StructuralSteel"/>, <see cref="GpcMaterials.Reinforcement"/> and <see cref="GpcMaterials.Concrete"/> with the
        /// rules of the former builders (their ν, density and thermal expansion are the defaults of the GPC types, as before). A concrete whose
        /// E is not the Ecm of its fck (a custom concrete, a value saved before E10) keeps its E for the analysis (the checks use fck, as before).
        /// A kind whose strength data the GPC type refuses is the generic material too (the analysis is unchanged; the checks read the same values).
        /// Not carried (unused, decision M4): identifier, temperature-on-time, mode and hardening of the former material, the dead shear
        /// ultimate field (its property reads the tensile ultimate strength), strengths of another kind.
        /// </summary>
        public static Material Material(LegacyValue m) => Material(MaterialRecipe(m));

        /// <summary>R6.2 part C: the GPC material by kind of a material recipe (the rule of <see cref="Material(LegacyValue)"/>, shared by the
        /// reader of the former payloads and the GPC builders of the material components).</summary>
        public static Material Material(LegacyMaterialRecipe r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            var elastic = ElasticMaterial(r);
            string kind = r.Kind, name = r.Name;
            double E = r.Modulus, nu = r.PoissonRatio, rho = r.Density, alpha = r.ThermalExpansion;
            double fy = r.MinimumYieldStress, fu = r.MinimumTensileStress, fck = r.SpecificCompressiveStrength;
            double? strainU = r.ReinforcementUltimateStrain;
            Material typed = null;
            try
            {
                switch (kind)
                {
                    case "Steel":
                    case "Rebar":
                        typed = new SteelMaterialEN1992(name, E, fy, fu, strainU ?? 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic,
                            kind == "Rebar" ? SteelMaterial.SteelTypes.Rebar : SteelMaterial.SteelTypes.Structural, nu, rho, alpha);
                        break;
                    case "Concrete":
                        var concrete = new ConcreteMaterialEN1992(name, fck, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle, nu, rho, alpha);
                        if (concrete.ElasticModulusCompression != E || concrete.ElasticModulusTension != E) { concrete.ElasticModulusCompression = E; concrete.ElasticModulusTension = E; }
                        typed = concrete;
                        break;
                    case "Aluminium":
                        typed = new AluminiumMaterialEN1999(name, E, r.TensileYieldStrength, r.TensileUltimateStrength, poisson: nu, density: rho, alfaThermalExpansion: alpha);
                        break;
                }
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException) { typed = null; }
            if (typed == null)
            {
                // A former material without a kind (or a kind whose strengths the GPC type refuses): the generic material, with the
                // characteristic strengths the former builders of the checks read in its stresses (yield and ultimate in tension, compressive
                // as a negative stress) and the reinforcement ultimate strain: the example generators internalise such steels and concretes.
                typed = elastic;
                // The name as given, as the typed materials and the former builders (the analysis material names a blank one "Material"); a
                // former material without a name keeps the name of the analysis.
                if (name != null && typed.Name != name) typed.Name = name;
                if (fy != 0) typed.StressYTension = fy;
                if (fu != 0) typed.StressUTension = fu;
                if (fck != 0) typed.StressYCompression = -fck;
                if (strainU.HasValue) typed.StrainUTension = strainU.Value;
            }
            else typed.AnalysisData = elastic.AnalysisData;
            static double? Declared(double v) => v == 0 ? (double?)null : v;
            string form = kind == "Aluminium" ? r.AluminiumType : null;
            var declared = new MaterialDeclarations
            {
                ExpectedYieldStress = Declared(r.ExpectedYieldStress), ExpectedTensileStrength = Declared(r.ExpectedTensileStress),
                ExpectedCompressiveStrength = Declared(r.ExpectedCompressiveStrength),
                CompressiveYieldStrength = Declared(r.CompressiveYieldStrength), TensileYieldStrength = Declared(r.TensileYieldStrength),
                // A former payload: the ShearUltimateStrength property read (and wrote) the tensile ultimate field (MaterialRecipe gives that value).
                TensileUltimateStrength = Declared(r.TensileUltimateStrength), ShearUltimateStrength = Declared(r.ShearUltimateStrength),
                Alloy = r.Alloy,
                AluminiumForm = form == null ? AluminiumManufacturingForm.Unspecified : form == "CastMold" ? AluminiumManufacturingForm.CastMold
                    : form == "CastSand" ? AluminiumManufacturingForm.CastSand : AluminiumManufacturingForm.Wrought,
            };
            bool anyDeclared = declared.ExpectedYieldStress.HasValue || declared.ExpectedTensileStrength.HasValue || declared.ExpectedCompressiveStrength.HasValue
                || declared.CompressiveYieldStrength.HasValue || declared.TensileYieldStrength.HasValue || declared.TensileUltimateStrength.HasValue
                || declared.ShearUltimateStrength.HasValue || declared.Alloy != null || form != null;
            typed.Declarations = anyDeclared ? declared : null;
            return typed;
        }

        /// <param name="checkLengthUnit">False for a draft (definition units, identity factors): the length unit of a library or BXS section
        /// is checked against the units of the model by the assembly (<see cref="LegacyAssembly.NormaliseBeamProperty"/>).</param>
        private static LegacyBeamProperty BeamProperty(LegacyValue p, string propertyName, BridgeUnits units, Dictionary<string, (double, double)> shearAreas, LegacyImportReport report,
            bool checkLengthUnit = true)
        {
            var (property, formulation, angle, native, recipe) = Property(p, propertyName, units, shearAreas, report, checkLengthUnit);
            return new LegacyBeamProperty(property, new LegacyBeamPropertyData(formulation, angle, native, PropertySignature(property)) { Section = recipe });
        }

        private static (BeamProperty, BeamFormulation, double, GPC.Converter.Straus7.Straus7SectionDefinition, LegacySectionRecipe) Property(LegacyValue p, string propertyName, BridgeUnits units,
            Dictionary<string, (double, double)> shearAreas, LegacyImportReport report, bool checkLengthUnit)
        {
            string type = p.Enum("BeamPropertyType");
            var formulation = type == "Beam" ? BeamFormulation.StraightTwoNode : type == "Truss" ? BeamFormulation.Truss : type == "Cable" ? BeamFormulation.Cable
                : type == "CutoffBar" ? BeamFormulation.CutoffBar : type == "PointContact" ? BeamFormulation.PointContact : type == "Pipe" ? BeamFormulation.Pipe
                : type == "Connection" ? BeamFormulation.Connection : throw new NotSupportedException("LegacyBeamPropertyType: " + type);
            if (formulation == BeamFormulation.CutoffBar) throw new NotSupportedException("LegacyCutoffBarNotMappedYet");
            var m = p.Field("Material"); var s = p.Field("SectionModel");
            if (s.Enum("MirrorType") != "None") throw new NotSupportedException("LegacyMirroredSectionNotMappedYet");
            var material = ElasticMaterial(m);
            if (!m["StressStrainTable"].IsNull && type != "Beam" && type != "Truss") material.AnalysisData.StressStrainPairs = null; // former exporter: tables only for beams and trusses
            var recipe = SectionRecipe(s);
            var (property, angle, native) = SectionProperty(recipe, propertyName, material, units, shearAreas, report, checkLengthUnit);
            return (property, formulation, angle, native, recipe);
        }

        /// <summary>Chain E (e3 point 5): the section of a former SectionModel in the definition units (the numbers as written, nothing derived).</summary>
        private static LegacySectionRecipe SectionRecipe(LegacyValue s)
        {
            if (!s["NativeSection"].IsNull)
            {
                var n = s["NativeSection"]; string key = n.String("Key");
                return new LegacySectionRecipe
                {
                    NativeKey = key, NativeLengthUnit = GpcNativeSections.IsLibraryOrBxs(key) ? n.Int("LengthUnit") : 0,
                    NativeDimensions = n["Dimensions"].IsNull ? Array.Empty<double>() : n["Dimensions"].Items.Select(v => v.AsDouble()).ToArray(),
                    BxsPath = n.String("BxsPath"), LibraryName = n.String("LibraryName"), LibraryItem = n.String("LibraryItem"),
                };
            }
            var kind = (BridgeSectionKind)Enum.Parse(typeof(BridgeSectionKind), s.Enum("SectionType"));
            string name = s.String("Name") ?? ""; double angle = s.Double("AngleX1");
            if (kind == BridgeSectionKind.Composite) return new LegacySectionRecipe { Kind = kind, Name = name, Angle = angle };
            if (kind == BridgeSectionKind.Generic)
            {
                var c = s["Centroid"];
                return new LegacySectionRecipe
                {
                    Kind = kind, Name = name, Angle = angle, Area = s.Double("Area"), I11 = s.Double("I11"), I22 = s.Double("I22"), J = s.Double("J"), CentroidX = c.Double("X"), CentroidY = c.Double("Y"),
                    ShearL1 = s.Double("ShearL1"), ShearL2 = s.Double("ShearL2"), ShearA1 = s.Double("ShearA1"), ShearA2 = s.Double("ShearA2"), CompositePreview = !s["CompositePreview"].IsNull,
                };
            }
            return new LegacySectionRecipe
            {
                Kind = kind, Name = name, Angle = angle, D = s.Double("D"), B = s.Double("B"), B1 = s.Double("B1"), B2 = s.Double("B2"), T = s.Double("T"), T1 = s.Double("T1"), T2 = s.Double("T2"), T3 = s.Double("T3"),
            };
        }

        /// <summary>The message of a library or BXS section read in a length unit other than that of the model (former exporter).</summary>
        internal const string LibraryUnitsMessage = "Le unità del modello differiscono da quelle usate per leggere la sezione di libreria/BXS. Ricalcolare la sezione nelle unità del modello.";

        /// <summary>
        /// Chain E (e3 point 5, coordinator decision 1 of 5/11): the beam property of a section in the definition <paramref name="units"/>, with
        /// the operations of the former exporter. The reader converts a former property with it, Build Model normalises the property of a draft
        /// with it (<see cref="LegacyAssembly.NormaliseBeamProperty"/>): the same numbers in, the same operations, the same section.
        /// </summary>
        internal static (BeamProperty Property, double Angle, GPC.Converter.Straus7.Straus7SectionDefinition Native) SectionProperty(LegacySectionRecipe r, string propertyName, Material material,
            BridgeUnits units, Dictionary<string, (double, double)> shearAreas, LegacyImportReport report, bool checkLengthUnit = true)
        {
            if (r.NativeKey != null)
            {
                // Former exporter: the native section was assigned again in the model units (standard and BGL dimensions as numbers in
                // the model length unit; BXS and library sections only when read in the model length unit); Straus7 computed the properties.
                if (checkLengthUnit && GpcNativeSections.IsLibraryOrBxs(r.NativeKey) && r.NativeLengthUnit != GpcNativeSections.Straus7LengthUnit(units.Length))
                    throw new ArgumentException(LibraryUnitsMessage);
                var resolved = GpcNativeSections.Resolve(r.NativeKey, r.NativeDimensions.ToArray(), r.BxsPath, r.LibraryName, r.LibraryItem, units.Length).Section;
                var nativeProperty = resolved.ToBeamProperty(propertyName, material);
                report.Carry("Native section preview contours (display only)");
                return (nativeProperty, resolved.ReferenceProperties[10], resolved);
            }
            var kind = r.Kind;
            string sectionName = r.Name; double angle = r.Angle;
            double L(double v) => units.ToMm(v);
            ISectionShape section; double shear1, shear2;
            if (kind == BridgeSectionKind.Generic || kind == BridgeSectionKind.Composite)
            {
                if (kind == BridgeSectionKind.Composite) throw new NotSupportedException("LegacyCompositeSectionTypeNotMappedYet");
                section = GpcSections.Explicit(sectionName, units.ToMm2(r.Area), units.ToMm4(r.I11), units.ToMm4(r.I22), units.ToMm4(r.J),
                    units.ToMm(r.CentroidX), units.ToMm(r.CentroidY), units.ToMm(r.ShearL1), units.ToMm(r.ShearL2), angle);
                shear1 = units.ToMm2(r.ShearA1); shear2 = units.ToMm2(r.ShearA2);
                if (r.CompositePreview) report.Carry("Composite preview geometry (preview and checks input, not exported)");
            }
            else
            {
                if (angle != 0) throw new NotSupportedException("LegacyStandardSectionWithPrincipalAngleNotMappedYet");
                section = GpcSections.Standard(kind, sectionName, L(r.D), L(r.B), L(r.B1), L(r.B2), L(r.T), L(r.T1), L(r.T2), L(r.T3));
                // Shear areas from the GPC section geometry (user decision C2; Straus7 no longer derives them): De Saint Venant at nu = 0
                // (user decision 4/11, public GPCModel API of L6), so they depend on the geometry only.
                string key = kind + "/" + string.Join(",", new[] { r.D, r.B, r.B1, r.B2, r.T, r.T1, r.T2, r.T3 }.Select(v => v.ToString("R")));
                if (!shearAreas.TryGetValue(key, out var areas))
                {
                    var computed = GPC.Model.Sections.SectionShearAreas.Compute(section);
                    shearAreas.Add(key, areas = (computed.Area1, computed.Area2));
                }
                (shear1, shear2) = areas;
            }
            var property = new ElasticBeamProperty(propertyName, material, section, shear1, shear2);
            return (property, angle, null);
        }

        /// <summary>BGHGPC1 (ii): a former section by value (the datum of a Beam Section parameter): its recipe, the numbers as written.</summary>
        public static LegacySectionRecipe SectionDraft(LegacyValue s)
        {
            if (s == null || s.IsNull || !s.Is("SectionModel")) throw new ArgumentException("LegacySectionModelExpected");
            if (s.Enum("MirrorType") != "None") throw new NotSupportedException("LegacyMirroredSectionNotMappedYet");
            return SectionRecipe(s);
        }

        /// <summary>BGHGPC1 (ii): a former plate property by value (the datum of a Plate Property parameter), with the rule of the plate drafts.</summary>
        public static PlateProperty PlatePropertyDraft(LegacyValue p, BridgeUnits units)
        {
            if (p == null || p.IsNull || !p.Is("PlatePropertyModel")) throw new ArgumentException("LegacyPlatePropertyModelExpected");
            return PlateProperty(p, PlatePropertyName(p), units);
        }

        private static PlateProperty PlateProperty(LegacyValue p, string name, BridgeUnits units)
        {
            string type = p.Any("PropertyType", "_platePropertyType").Text?.Trim();
            if (type == "LoadPatch") return new ElasticPlateProperty(name, null, 0, 0, ElasticPlateFormulation.LoadPatch);
            var m = p.Any("Material", "_material");
            var material = GpcMaterials.Elastic(m.String("Name"), m.Double("Modulus"), m.Double("PoissonRatio"), m.Double("ShearModulus"), true,
                m.Double("Density"), m.Double("ThermalExpansion"), m.Double("ViscousDamping"), m.Double("DampingRatio"), m.Double("Conductivity"), m.Double("SpecificHeat"), null);
            if (!m["StressStrainTable"].IsNull) throw new NotSupportedException("LegacyPlateStressStrainTableNotMappedYet");
            double membrane = units.ToMm(p.Any("MembraneThickness", "_membraneThickness").AsDouble()), bending = units.ToMm(p.Any("BendingThickness", "_bendingThickness").AsDouble());
            switch (type)
            {
                case "ShellThin": case "ShellThick": case "PlateThin": case "PlateThick":
                    // The former exporter wrote all four as the native ptPlateShell with the two thicknesses.
                    return new ElasticPlateProperty(name, material, bending, membrane, ElasticPlateFormulation.Shell);
                case "Membrane": return new ElasticPlateProperty(name, material, 0, membrane, ElasticPlateFormulation.Membrane);
                case "ShearPanel": return new ElasticPlateProperty(name, material, bending, membrane, ElasticPlateFormulation.ShearPanel);
                default: throw new NotSupportedException("LegacyPlatePropertyType: " + type);
            }
        }

        private static (double stress, double strain)[] StressStrain(LegacyValue table)
        {
            if (table.IsNull) return null;
            return table.Items.Select(t => (t.Double("Item1"), t.Double("Item2"))).ToArray();
        }
    }
}
