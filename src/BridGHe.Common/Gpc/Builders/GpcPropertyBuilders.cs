using System;
using System.Collections.Generic;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.Materials;
using GPC.Model.Structure.Assignments;

namespace BridGHe.Common.Gpc.Builders
{
    /// <summary>
    /// R6.2 part C (P2N): the GPC builder of the Beam Property component: the property the reader reads from the former value the component
    /// makes (<see cref="LegacyModelImport.BeamProperty(LegacyValue, BridgeUnits)"/>, definition units), built from the GPC data of its inputs
    /// (the section recipe and the GPC material), with the steps of the reader in its order and its messages.
    /// <para>The section of the property is the one the former component made: a copy of the input section without its name, recomputed
    /// (standard shapes: the same principal angle; a generic section: its explicit values; a native Straus7 section: the explicit values the
    /// former section held, computed from the native section as <c>StrausSection.Calculate</c> did, without the native definition).</para>
    /// <para>Signature (§1bis, until R6.5): the digest of the former content, that of the conversion of the FEM output, given by the
    /// component (like the identity of the element drafts); R6.3 moves every draft to the GPC fingerprint.</para>
    /// </summary>
    public static class GpcPropertyBuilders
    {
        // The former BeamPropertyType, by its value (the Type input of the component).
        private static readonly string[] Types = { "SpringDamper", "Cable", "Truss", "CutoffBar", "PointContact", "Beam", "UserDefined", "Pipe", "Connection" };

        /// <summary>The Beam Property component (type index of the former enum; material: the GPC material by kind of the input).</summary>
        public static LegacyBeamProperty Beam(string name, int type, LegacySectionRecipe section, Material material, string signature)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (material == null) throw new ArgumentNullException(nameof(material));
            string typeName = type >= 0 && type < Types.Length ? Types[type] : type == -1 ? "Undefined" : type.ToString();
            var formulation = typeName == "Beam" ? BeamFormulation.StraightTwoNode : typeName == "Truss" ? BeamFormulation.Truss : typeName == "Cable" ? BeamFormulation.Cable
                : typeName == "CutoffBar" ? BeamFormulation.CutoffBar : typeName == "PointContact" ? BeamFormulation.PointContact : typeName == "Pipe" ? BeamFormulation.Pipe
                : typeName == "Connection" ? BeamFormulation.Connection : throw new NotSupportedException("LegacyBeamPropertyType: " + typeName);
            if (formulation == BeamFormulation.CutoffBar) throw new NotSupportedException("LegacyCutoffBarNotMappedYet");
            var analysis = GpcMaterials.Analysis(material);
            if (analysis.AnalysisData?.StressStrainPairs != null && typeName != "Beam" && typeName != "Truss") analysis.AnalysisData.StressStrainPairs = null; // former exporter: tables only for beams and trusses
            var recipe = PropertySection(section);
            var units = GpcBeamDrafts.DraftUnits;
            var (property, angle, native) = LegacyModelImport.SectionProperty(recipe, name ?? "Property", analysis, units, new Dictionary<string, (double, double)>(StringComparer.Ordinal), new LegacyImportReport());
            return new LegacyBeamProperty(property, new LegacyBeamPropertyData(formulation, angle, native, signature) { Section = recipe });
        }

        /// <summary>The section a former Beam Property made of its input section (see the class).</summary>
        public static LegacySectionRecipe PropertySection(LegacySectionRecipe s)
        {
            if (s.NativeKey != null)
            {
                // Former StrausSection.Calculate: the values of the native section in the length unit it was read in (library and BXS) or as given.
                var resolved = GpcNativeSections.Resolve(s.NativeKey, ToArray(s.NativeDimensions), s.BxsPath, s.LibraryName, s.LibraryItem, 1);
                double f = GpcNativeSections.IsLibraryOrBxs(s.NativeKey) ? GpcNativeSections.MillimetresPer(s.NativeLengthUnit) : 1, f2 = f * f, f4 = f2 * f2;
                var data = resolved.Section.ReferenceProperties;
                return new LegacySectionRecipe
                {
                    Kind = BridgeSectionKind.Generic, Name = "", Angle = Z(data[10]), Area = Z(data[0] / f2), I11 = Z(data[1] / f4), I22 = Z(data[2] / f4), J = Z(data[3] / f4),
                    CentroidX = Z(data[8] / f), CentroidY = Z(data[9] / f), ShearL1 = Z(data[4] / f), ShearL2 = Z(data[5] / f), ShearA1 = Z(data[6] / f2), ShearA2 = Z(data[7] / f2),
                };
            }
            if (s.Kind == BridgeSectionKind.Generic)
                return new LegacySectionRecipe
                {
                    Kind = s.Kind, Name = "", Angle = s.Angle, Area = s.Area, I11 = s.I11, I22 = s.I22, J = s.J, CentroidX = s.CentroidX, CentroidY = s.CentroidY,
                    ShearL1 = s.ShearL1, ShearL2 = s.ShearL2, ShearA1 = s.ShearA1, ShearA2 = s.ShearA2,
                };
            if (s.Kind == BridgeSectionKind.Composite) return new LegacySectionRecipe { Kind = s.Kind, Name = "", Angle = s.Angle };
            return new LegacySectionRecipe { Kind = s.Kind, Name = "", Angle = s.Angle, D = s.D, B = s.B, B1 = s.B1, B2 = s.B2, T = s.T, T1 = s.T1, T2 = s.T2, T3 = s.T3 };
        }

        // The former PlatePropertyType, by its value (the Type input of Plate Property).
        private static readonly string[] PlateTypes = { "ShellThin", "ShellThick", "PlateThin", "PlateThick", "Membrane", "LoadPatch", "ShearPanel" };

        /// <summary>
        /// The Plate Property component: the plate property of the reader (<see cref="LegacyModelImport.PlatePropertyDraft"/>): a load patch
        /// without material; otherwise the elastic material of the reader for plates (the analysis material of the input with Use Poisson, no
        /// stress-strain table: a table is refused as the reader refuses it) and the thicknesses in the definition units.
        /// </summary>
        public static GPC.Model.ElementProperties.PlateProperty Plate(string name, int type, Material material, double membraneThickness, double bendingThickness)
        {
            string typeName = type >= 0 && type < PlateTypes.Length ? PlateTypes[type] : type == -1 ? "Undefined" : type.ToString();
            string propertyName = name ?? "Plate property";
            if (typeName == "LoadPatch") return new GPC.Model.ElementProperties.ElasticPlateProperty(propertyName, null, 0, 0, GPC.Model.ElementProperties.ElasticPlateFormulation.LoadPatch);
            if (material == null) throw new ArgumentNullException(nameof(material));
            var plateMaterial = GpcMaterials.Analysis(material);
            if (plateMaterial.AnalysisData?.StressStrainPairs != null) throw new NotSupportedException("LegacyPlateStressStrainTableNotMappedYet");
            // The reader makes the plate material with Use Poisson: no beam shear modulus, analysis data only for damping and thermal data.
            var a = plateMaterial.AnalysisData;
            if (a != null)
            {
                a.BeamShearModulus = null;
                if (a.ViscousDampingDensity == 0 && a.ModalDampingRatio == 0 && a.ThermalConductivity == 0 && a.SpecificHeat == 0) plateMaterial.AnalysisData = null;
            }
            var units = GpcBeamDrafts.DraftUnits;
            double membrane = units.ToMm(Z(membraneThickness)), bending = units.ToMm(Z(bendingThickness));
            switch (typeName)
            {
                case "ShellThin": case "ShellThick": case "PlateThin": case "PlateThick":
                    return new GPC.Model.ElementProperties.ElasticPlateProperty(propertyName, plateMaterial, bending, membrane, GPC.Model.ElementProperties.ElasticPlateFormulation.Shell);
                case "Membrane": return new GPC.Model.ElementProperties.ElasticPlateProperty(propertyName, plateMaterial, 0, membrane, GPC.Model.ElementProperties.ElasticPlateFormulation.Membrane);
                case "ShearPanel": return new GPC.Model.ElementProperties.ElasticPlateProperty(propertyName, plateMaterial, bending, membrane, GPC.Model.ElementProperties.ElasticPlateFormulation.ShearPanel);
                default: throw new NotSupportedException("LegacyPlatePropertyType: " + typeName);
            }
        }

        private static double Z(double v) => GpcBeamBuilders.Z(v);

        private static double[] ToArray(IReadOnlyList<double> v)
        {
            var a = new double[v?.Count ?? 0];
            for (int i = 0; i < a.Length; i++) a[i] = v[i];
            return a;
        }
    }
}
