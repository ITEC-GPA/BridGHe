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

        /// <summary>A standard section (solid and hollow circle and rectangle, I, T, C, angle, omega, Z) of the section components; L2C: the
        /// fillet radius of the I section component (<paramref name="r"/>, the former <c>R</c>).</summary>
        /// <param name="mirror">L2C: the mirror of the double angle and double C (input Double Sections: the former <c>MirrorTypes.Left</c>,
        /// <see cref="LegacySectionRecipe.Mirror"/>) with its gap; the principal angle is the one the former component computed on the
        /// mirrored shapes.</param>
        public static LegacySectionRecipe Standard(BridgeSectionKind kind, double d, double b = 0, double b1 = 0, double b2 = 0, double t = 0, double t1 = 0, double t2 = 0, double t3 = 0, double r = 0,
            int mirror = LegacySectionRecipe.NoMirror, double gapA = 0)
        {
            if (kind == BridgeSectionKind.Generic || kind == BridgeSectionKind.GenericShapes) throw new ArgumentException("GpcSectionBuildersStandardExpected: " + kind);
            double angle = kind == BridgeSectionKind.SolidCircle || kind == BridgeSectionKind.HollowCircle || kind == BridgeSectionKind.Composite ? 0
                : GpcSectionGeometry.Of(GpcSectionGeometry.Mirrored(GpcSectionGeometry.StandardShapes(kind, d, b, b1, b2, t1, t2, t3, 0), GpcSectionGeometry.ValuesMirror(kind, mirror), gapA), false).Angle;
            return new LegacySectionRecipe { Kind = kind, Name = "", Angle = Z(angle), D = Z(d), B = Z(b), B1 = Z(b1), B2 = Z(b2), T = Z(t), T1 = Z(t1), T2 = Z(t2), T3 = Z(t3), R = Z(r),
                Mirror = mirror, MirrorGapA = Z(gapA) };
        }

        /// <summary>L2C: a copy of a recipe with every datum (the menus of the Beam Section parameter change one on the copy).</summary>
        private static LegacySectionRecipe Copy(LegacySectionRecipe r, int? mirror = null, double? gapA = null) => new LegacySectionRecipe
        {
            Kind = r.Kind, Name = r.Name, Angle = r.Angle, D = r.D, B = r.B, B1 = r.B1, B2 = r.B2, T = r.T, T1 = r.T1, T2 = r.T2, T3 = r.T3, Area = r.Area, I11 = r.I11, I22 = r.I22, J = r.J,
            CentroidX = r.CentroidX, CentroidY = r.CentroidY, ShearL1 = r.ShearL1, ShearL2 = r.ShearL2, ShearA1 = r.ShearA1, ShearA2 = r.ShearA2, CompositePreview = r.CompositePreview, CompositeOutline = r.CompositeOutline,
            NativeKey = r.NativeKey, NativeDimensions = r.NativeDimensions, BxsPath = r.BxsPath, LibraryName = r.LibraryName, LibraryItem = r.LibraryItem, NativeLengthUnit = r.NativeLengthUnit,
            R = r.R, W11 = r.W11, W22 = r.W22, Shapes = r.Shapes, Mirror = mirror ?? r.Mirror, MirrorGapA = gapA ?? r.MirrorGapA, MirrorGapB = r.MirrorGapB,
        };

        /// <summary>A generic section (explicit values of the Generic section component); L2C: with its section moduli (inputs S11/S22).</summary>
        public static LegacySectionRecipe Generic(double area, double i11, double i22, double j, double shearL1, double shearL2, double shearA1, double shearA2, double x, double y, double angle,
            double w11 = 0, double w22 = 0) =>
            new LegacySectionRecipe
            {
                Kind = BridgeSectionKind.Generic, Name = "", Angle = Z(angle), Area = Z(area), I11 = Z(i11), I22 = Z(i22), J = Z(j), CentroidX = Z(x), CentroidY = Z(y),
                ShearL1 = Z(shearL1), ShearL2 = Z(shearL2), ShearA1 = Z(shearA1), ShearA2 = Z(shearA2), W11 = Z(w11), W22 = Z(w22),
            };

        /// <summary>A section of generic shapes (Brep section component): no dimensions and angle 0 (the former component did not compute
        /// the section); L2C: its shapes as the former shapes stored them (<see cref="Shape"/>).</summary>
        public static LegacySectionRecipe Shapes(IEnumerable<GPC.Geometry.Shape2d> shapes) =>
            new LegacySectionRecipe { Kind = BridgeSectionKind.GenericShapes, Name = "", Shapes = (shapes ?? throw new ArgumentNullException(nameof(shapes))).Select(Shape).ToArray() };

        /// <summary>L2C: a former shape as the recipe holds it: the vertices of its fill and of its holes as stored (the GPC shape orients the
        /// holes against the fill), as the reader reads them from the former value.</summary>
        public static LegacySectionShape Shape(GPC.Geometry.Shape2d shape)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (shape.Childs2d != null && shape.Childs2d.Length != 0) throw new NotSupportedException("LegacySectionShapeWithChildren");
            static double[] Polygon(GPC.Geometry.Polygon3d p) => p.SelectMany(v => new[] { Z(v.X), Z(v.Y) }).ToArray();
            return new LegacySectionShape
            {
                Fill = Polygon(shape.Fill),
                Holes = (shape.Holes ?? Array.Empty<GPC.Geometry.Polygon3d>()).Select(h => (IReadOnlyList<double>)Polygon(h)).ToArray(),
            };
        }

        /// <summary>
        /// R6.2 part C: the units menu of the Beam Section parameter (the former <c>GH_BeamSection.ConvertUnits</c>): the lengths of the
        /// recipe converted with the length factor of GPCUtilities (dimensions and centroid ^1, area and shear areas ^2, inertias and torsion
        /// ^4), the angle kept; a native section unchanged (its native data were not converted). L2C: also the section moduli (^3) and the
        /// mirror gaps (^1), the mirror kept; the fillet radius and the shapes of a Brep section kept, as the former conversion left them
        /// (it did not convert R, and it moved copies of the shape vertices, not the shapes).
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
                ShearL1 = C(r.ShearL1, 1), ShearL2 = C(r.ShearL2, 1), ShearA1 = C(r.ShearA1, 2), ShearA2 = C(r.ShearA2, 2), CompositePreview = r.CompositePreview, CompositeOutline = r.CompositeOutline?.Convert(C),
                R = r.R, W11 = C(r.W11, 3), W22 = C(r.W22, 3), Shapes = r.Shapes, Mirror = r.Mirror, MirrorGapA = C(r.MirrorGapA, 1), MirrorGapB = C(r.MirrorGapB, 1),
            };
        }

        /// <summary>L2C (lane 2c, as in 0.3.0): the mirror menu of the Beam Section parameter on a C or angle section: the former toggle
        /// (none → Left, any mirror → none), the gaps kept; the other values as they were (the former menu did not recompute the section).</summary>
        public static LegacySectionRecipe Mirrored(LegacySectionRecipe r) =>
            Copy(r ?? throw new ArgumentNullException(nameof(r)), mirror: r.Mirror == LegacySectionRecipe.NoMirror ? 2 : LegacySectionRecipe.NoMirror);

        /// <summary>L2C (lane 2c, as in 0.3.0): the gap menu of the Beam Section parameter: the gap A of the mirror, the rest as it was.</summary>
        public static LegacySectionRecipe Gap(LegacySectionRecipe r, double gap) => Copy(r ?? throw new ArgumentNullException(nameof(r)), gapA: Z(gap));

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
