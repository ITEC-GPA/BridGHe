using System;
using System.IO;
using System.Linq;
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

    }

    public sealed record StrausSectionShape(string Key, string Name, int NativeId, bool Bgl, string[] Dimensions, int[] Slots);
}
