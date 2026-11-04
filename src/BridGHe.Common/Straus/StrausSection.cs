using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Straus
{
    [Serializable]
    public sealed class StrausSection
    {
        public string Key { get; set; }
        public double[] Dimensions { get; set; } = Array.Empty<double>();
        public string BxsPath { get; set; }
        public string LibraryName { get; set; }
        public string LibraryItem { get; set; }
        public int LengthUnit { get; set; } = 2;
        public double[][] PreviewContours { get; set; } = Array.Empty<double[]>();
        public StrausSection Clone() => new StrausSection
        {
            Key = Key,
            Dimensions = (double[])Dimensions.Clone(),
            BxsPath = BxsPath,
            LibraryName = LibraryName,
            LibraryItem = LibraryItem,
            LengthUnit = LengthUnit,
            PreviewContours = PreviewContours?.Select(loop=>(double[])loop.Clone()).ToArray() ?? Array.Empty<double[]>()
        };

        public void Validate()
        {
            if (LengthUnit < 0 || LengthUnit > 4) throw new ArgumentOutOfRangeException(nameof(LengthUnit));
            if (Key == "BSL" || Key == "BGL")
            {
                if (string.IsNullOrWhiteSpace(LibraryName) || string.IsNullOrWhiteSpace(LibraryItem))
                    throw new ArgumentException("Specificare nome libreria Straus senza estensione e nome del profilo.");
                return;
            }
            if (Key == "BXS")
            {
                if (!File.Exists(BxsPath) || !string.Equals(Path.GetExtension(BxsPath), ".bxs", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Specificare un file .bxs esistente.");
                return;
            }
            var shape = StrausSectionCatalog.Get(Key);
            if (Dimensions == null || Dimensions.Length != shape.Dimensions.Length)
                throw new ArgumentException($"{Key}: attese {shape.Dimensions.Length} dimensioni.");
            for (int i = 0; i < Dimensions.Length; i++)
            {
                double d = Dimensions[i];
                string name = shape.Dimensions[i];
                if (!double.IsFinite(d) || (!name.StartsWith("alpha") && d < 0))
                    throw new ArgumentException($"{Key}: dimensione {name} non valida.");
                if ((name.StartsWith("D") || name.StartsWith("B") || name.StartsWith("t")) && d == 0)
                    throw new ArgumentException($"{Key}: {name} deve essere positiva.");
            }
            // Geometric admissibility (overlapping walls, radii, taper) is checked by Straus.
        }

        /// <summary>Dimensions use the eventual model length unit. No silent unit conversion. F6A: resolved through the GPC Converter
        /// (<see cref="Gpc.GpcNativeSections"/>); standard and BGL dimensions are taken as numbers (their properties scale with them),
        /// BXS files and library items are resolved in mm and expressed in <see cref="LengthUnit"/>, as the former model in that unit did.</summary>
        public SectionModel Calculate()
        {
            Validate();
            var resolved = Gpc.GpcNativeSections.Resolve(Key, Dimensions, BxsPath, LibraryName, LibraryItem, 1);
            double f = Gpc.GpcNativeSections.IsLibraryOrBxs(Key) ? Gpc.GpcNativeSections.MillimetresPer(LengthUnit) : 1, f2 = f * f, f4 = f2 * f2;
            // Native section-property slots: A, I11, I22, J, SL1, SL2, SA1, SA2, Xbar, Ybar, principal angle [rad].
            var data = resolved.Section.ReferenceProperties;
            double angle = data[10], c = Math.Cos(angle), s = Math.Sin(angle);
            double area = data[0] / f2, i11 = data[1] / f4, i22 = data[2] / f4;
            var native = Clone();
            native.PreviewContours = NativeSectionPreview.Contours(resolved.Vertices, resolved.Faces, 1 / f);
            return new SectionModel
            {
                Name = Key,
                SectionType = SectionModel.SectionTypes.Generic,
                NativeSection = native,
                SectionArea = area,
                I11 = i11,
                I22 = i22,
                Ixx = i11 * c * c + i22 * s * s,
                Iyy = i11 * s * s + i22 * c * c,
                Ixy = (i22 - i11) * s * c,
                J = data[3] / f4,
                ShearL1 = data[4] / f,
                ShearL2 = data[5] / f,
                ShearA1 = data[6] / f2,
                ShearA2 = data[7] / f2,
                Centroid = new Point2d(data[8] / f, data[9] / f),
                AngleX1Rad = angle,
                // The integration-point count of the former St7GetBeamSectionPropertyData is not part of the resolved definition
                // (informational, used by no analysis or export).
                IntegrationPoints = 0,
                Rho11 = Math.Sqrt(i11 / area),
                Rho22 = Math.Sqrt(i22 / area),
                // Native section data does not contain elastic section moduli.
                W11 = double.NaN,
                W22 = double.NaN,
                Wxx = double.NaN,
                Wyy = double.NaN
            };
        }
    }

    public sealed record StrausSectionShape(string Key, string Name, int NativeId, bool Bgl, string[] Dimensions, int[] Slots);
}
