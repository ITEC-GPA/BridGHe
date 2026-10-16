using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Models;
using Rhino.Geometry;
using St7API;

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
        public StrausSection Clone() => new StrausSection
        {
            Key = Key,
            Dimensions = (double[])Dimensions.Clone(),
            BxsPath = BxsPath,
            LibraryName = LibraryName,
            LibraryItem = LibraryItem,
            LengthUnit = LengthUnit
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

        public void Assign(int modelId, int propertyId)
        {
            Validate();
            if (Key == "BXS" || Key == "BSL" || Key == "BGL")
            {
                var units = new int[St7.kLastUnit];
                StrausRuntime.Check(St7.St7GetUnits(modelId, units), "St7GetUnits");
                if (units[St7.ipLENGTHU] != LengthUnit)
                    throw new ArgumentException("Le unità del modello differiscono da quelle usate per leggere la sezione di libreria/BXS. Ricalcolare la sezione nelle unità del modello.");
            }
            if (Key == "BSL" || Key == "BGL")
            {
                int library = 0, item = 0, type = Key == "BSL" ? St7.lbBeamSection : St7.lbSectionGeometry;
                StrausRuntime.Check(St7.St7GetLibraryID(type, LibraryName, ref library), "St7GetLibraryID");
                StrausRuntime.Check(St7.St7GetLibraryItemID(type, library, LibraryItem, ref item), "St7GetLibraryItemID");
                var flags = new[] { St7.btFalse, St7.btTrue, St7.btFalse, St7.btFalse };
                StrausRuntime.Check(Key == "BSL" ? St7.St7AssignLibraryBeamSection(modelId, propertyId, library, item, flags)
                    : St7.St7AssignLibraryBeamSectionBGL(modelId, propertyId, library, item, flags), "Assign library section");
                return;
            }
            if (Key == "BXS")
            {
                StrausRuntime.Check(St7.St7AssignBXS(modelId, propertyId, Path.GetFullPath(BxsPath)), "St7AssignBXS");
                return;
            }
            var shape = StrausSectionCatalog.Get(Key);
            var native = new double[shape.Bgl ? St7.kMaxBGLDimensions : 6];
            for (int i = 0; i < Dimensions.Length; i++) native[shape.Slots[i]] = Dimensions[i];
            if (shape.Bgl)
                StrausRuntime.Check(St7.St7SetBeamSectionGeometryBGL(modelId, propertyId, shape.NativeId, native), "St7SetBeamSectionGeometryBGL");
            else
                StrausRuntime.Check(St7.St7SetBeamSectionGeometry(modelId, propertyId, shape.NativeId, native), "St7SetBeamSectionGeometry");
            StrausRuntime.Check(St7.St7CalculateBeamSectionProperties(modelId, propertyId, St7.btTrue), "St7CalculateBeamSectionProperties");
        }

        /// <summary>Dimensions use the eventual model length unit. No silent unit conversion.</summary>
        public SectionModel Calculate()
        {
            Validate();
            lock (StrausRuntime.Gate)
            {
                using var model = StrausRuntime.NewTemporaryModel();
                var units = new int[St7.kLastUnit];
                StrausRuntime.Check(St7.St7GetUnits(model.Id, units), "St7GetUnits");
                units[St7.ipLENGTHU] = LengthUnit;
                StrausRuntime.Check(St7.St7SetUnits(model.Id, units), "St7SetUnits");
                StrausRuntime.Check(St7.St7NewBeamProperty(model.Id, 1, St7.btBeam, Key), "St7NewBeamProperty");
                Assign(model.Id, 1);
                var integers = new int[1];
                var data = new double[11];
                StrausRuntime.Check(St7.St7GetBeamSectionPropertyData(model.Id, 1, integers, data), "St7GetBeamSectionPropertyData");
                double angle = data[St7.ipANGLE], c = Math.Cos(angle), s = Math.Sin(angle);
                double i11 = data[St7.ipI11], i22 = data[St7.ipI22];
                return new SectionModel
                {
                    Name = Key,
                    SectionType = SectionModel.SectionTypes.Generic,
                    NativeSection = Clone(),
                    SectionArea = data[St7.ipAREA],
                    I11 = i11,
                    I22 = i22,
                    Ixx = i11 * c * c + i22 * s * s,
                    Iyy = i11 * s * s + i22 * c * c,
                    Ixy = (i22 - i11) * s * c,
                    J = data[St7.ipJ],
                    ShearL1 = data[St7.ipSL1],
                    ShearL2 = data[St7.ipSL2],
                    ShearA1 = data[St7.ipSA1],
                    ShearA2 = data[St7.ipSA2],
                    Centroid = new Point2d(data[St7.ipXBAR], data[St7.ipYBAR]),
                    AngleX1Rad = angle,
                    IntegrationPoints = integers[0],
                    Rho11 = Math.Sqrt(i11 / data[St7.ipAREA]),
                    Rho22 = Math.Sqrt(i22 / data[St7.ipAREA]),
                    // Native section data does not contain elastic section moduli.
                    W11 = double.NaN,
                    W22 = double.NaN,
                    Wxx = double.NaN,
                    Wyy = double.NaN
                };
            }
        }
    }

    public sealed record StrausSectionShape(string Key, string Name, int NativeId, bool Bgl, string[] Dimensions, int[] Slots);
}
