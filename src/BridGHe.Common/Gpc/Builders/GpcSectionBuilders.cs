using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;

namespace BridGHe.Common.Gpc.Builders
{
    /// <summary>
    /// R6.2 part C (P2N): the GPC builders of the section components. A section is its recipe (<see cref="LegacySectionRecipe"/>, the
    /// numbers as written, in the definition units): the recipe the reader reads from the former value the component makes
    /// (<see cref="LegacyModelImport.SectionDraft"/>), built from the inputs of the component. Every number passes as the reader reads it
    /// from a former archive (<see cref="GpcBeamBuilders.Z"/>: −0 → +0). The principal angle of a shape section is the one the former value
    /// computed (<see cref="GpcSectionGeometry"/>, a copy of its computation); circles and composite sections keep 0, as the former value.
    /// A mirrored section is refused as the reader refuses it.
    /// </summary>
    public static class GpcSectionBuilders
    {
        private static double Z(double v) => GpcBeamBuilders.Z(v);

        /// <summary>A standard section (solid and hollow circle and rectangle, I, T, C, angle, omega, Z) of the section components.</summary>
        public static LegacySectionRecipe Standard(BridgeSectionKind kind, double d, double b = 0, double b1 = 0, double b2 = 0, double t = 0, double t1 = 0, double t2 = 0, double t3 = 0, bool mirrored = false)
        {
            if (mirrored) throw new NotSupportedException("LegacyMirroredSectionNotMappedYet");
            if (kind == BridgeSectionKind.Generic || kind == BridgeSectionKind.GenericShapes) throw new ArgumentException("GpcSectionBuildersStandardExpected: " + kind);
            double angle = kind == BridgeSectionKind.SolidCircle || kind == BridgeSectionKind.HollowCircle || kind == BridgeSectionKind.Composite ? 0
                : GpcSectionGeometry.Of(GpcSectionGeometry.StandardShapes(kind, d, b, b1, b2, t1, t2, t3, 0), false).Angle;
            return new LegacySectionRecipe { Kind = kind, Name = "", Angle = Z(angle), D = Z(d), B = Z(b), B1 = Z(b1), B2 = Z(b2), T = Z(t), T1 = Z(t1), T2 = Z(t2), T3 = Z(t3) };
        }

        /// <summary>A generic section (explicit values of the Generic section component).</summary>
        public static LegacySectionRecipe Generic(double area, double i11, double i22, double j, double shearL1, double shearL2, double shearA1, double shearA2, double x, double y, double angle) =>
            new LegacySectionRecipe
            {
                Kind = BridgeSectionKind.Generic, Name = "", Angle = Z(angle), Area = Z(area), I11 = Z(i11), I22 = Z(i22), J = Z(j), CentroidX = Z(x), CentroidY = Z(y),
                ShearL1 = Z(shearL1), ShearL2 = Z(shearL2), ShearA1 = Z(shearA1), ShearA2 = Z(shearA2),
            };

        /// <summary>A section of generic shapes (Brep section component): no dimensions and angle 0 (the former component did not compute
        /// the section; the shapes are not part of the recipe).</summary>
        public static LegacySectionRecipe Shapes() => new LegacySectionRecipe { Kind = BridgeSectionKind.GenericShapes, Name = "" };

        /// <summary>
        /// R6.2 part C: the units menu of the Beam Section parameter (the former <c>GH_BeamSection.ConvertUnits</c>): the lengths of the
        /// recipe converted with the length factor of GPCUtilities (dimensions and centroid ^1, area and shear areas ^2, inertias and torsion
        /// ^4), the angle kept; a native section unchanged (its native data were not converted); a mirrored section is refused by the reader
        /// before (no recipe).
        /// </summary>
        public static LegacySectionRecipe ConvertUnits(LegacySectionRecipe r, GPC.Utilities.Units.UnitsConvert.LengthUnits from, GPC.Utilities.Units.UnitsConvert.LengthUnits to)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            if (r.NativeKey != null) return r;
            double C(double v, int e) => GPC.Utilities.Units.UnitsConvert.Convert(v, from, to, e);
            return new LegacySectionRecipe
            {
                Kind = r.Kind, Name = r.Name, Angle = r.Angle, D = C(r.D, 1), B = C(r.B, 1), B1 = C(r.B1, 1), B2 = C(r.B2, 1), T = C(r.T, 1), T1 = C(r.T1, 1), T2 = C(r.T2, 1), T3 = C(r.T3, 1),
                Area = C(r.Area, 2), I11 = C(r.I11, 4), I22 = C(r.I22, 4), J = C(r.J, 4), CentroidX = C(r.CentroidX, 1), CentroidY = C(r.CentroidY, 1),
                ShearL1 = C(r.ShearL1, 1), ShearL2 = C(r.ShearL2, 1), ShearA1 = C(r.ShearA1, 2), ShearA2 = C(r.ShearA2, 2), CompositePreview = r.CompositePreview,
            };
        }

        /// <summary>R6.2 part C: a mirrored section (the mirror menu of the Beam Section parameter): refused as the reader refuses it.</summary>
        public static LegacySectionRecipe Mirrored() => throw new NotSupportedException("LegacyMirroredSectionNotMappedYet");

        /// <summary>A native Straus7 section (standard or BGL dimensions, BXS file, library item): the key and the data of the former
        /// native section; the length unit only for library and BXS sections (the reader reads it only for those).</summary>
        public static LegacySectionRecipe Native(string key, IReadOnlyList<double> dimensions, string bxsPath, string libraryName, string libraryItem, int lengthUnit) =>
            new LegacySectionRecipe
            {
                NativeKey = key, NativeLengthUnit = GpcNativeSections.IsLibraryOrBxs(key) ? lengthUnit : 0, NativeDimensions = (dimensions ?? Array.Empty<double>()).Select(Z).ToArray(),
                BxsPath = bxsPath, LibraryName = libraryName, LibraryItem = libraryItem,
            };
    }
}
