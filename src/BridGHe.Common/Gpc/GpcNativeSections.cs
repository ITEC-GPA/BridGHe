using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using GPC.Converter.Straus7;

namespace BridGHe.Common.Gpc
{
    /// <summary>Native Straus7 sections (standard and BGL shapes, BXS files, BSL/BGL library items) resolved through the GPC Converter in
    /// mm, with their display mesh. Resolutions are cached per process by definition (a BXS file by its content), so a section computed
    /// by a component is not resolved again by the analysis. The former St7API binding of BridGHe is not used.</summary>
    public static class GpcNativeSections
    {
        public sealed class Resolved
        {
            /// <summary>Resolved definition in mm (reference properties A, I11, I22, J, SL1, SL2, SA1, SA2, Xbar, Ybar, angle).</summary>
            public Straus7SectionDefinition Section { get; internal set; }
            /// <summary>Display mesh in mm: {x, y, 0} vertices and zero-based corner indices.</summary>
            public double[][] Vertices { get; internal set; }
            public int[][] Faces { get; internal set; }
        }

        private static readonly ConcurrentDictionary<string, Resolved> cache = new ConcurrentDictionary<string, Resolved>(StringComparer.Ordinal);

        /// <summary>Millimetres per Straus7 length unit (0 m, 1 cm, 2 mm, 3 ft, 4 in).</summary>
        public static double MillimetresPer(int straus7LengthUnit)
        {
            switch (straus7LengthUnit)
            {
                case 0: return 1000; case 1: return 10; case 2: return 1; case 3: return 304.8; case 4: return 25.4;
                default: throw new ArgumentOutOfRangeException(nameof(straus7LengthUnit));
            }
        }

        /// <summary>Straus7 length unit of a model length factor (mm per unit), as the former exporter set it (m, mm, in).</summary>
        public static int Straus7LengthUnit(double millimetresPerUnit)
        {
            foreach (int u in new[] { 0, 1, 2, 3, 4 }) if (MillimetresPer(u) == millimetresPerUnit) return u;
            throw new ArgumentOutOfRangeException(nameof(millimetresPerUnit));
        }

        public static bool IsLibraryOrBxs(string key) => key == "BSL" || key == "BGL" || key == "BXS";

        /// <param name="scale">Millimetres per unit of the standard/BGL dimensions (angles are not scaled). BXS files and library
        /// items carry their own units and are resolved in mm.</param>
        public static Resolved Resolve(string key, double[] dimensions, string bxsPath, string library, string item, double scale)
        {
            if (!(scale > 0) || double.IsInfinity(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
            string id; Func<string, Straus7SectionMesh> run;
            if (key == "BSL" || key == "BGL")
            {
                id = key + "|" + library + "|" + item;
                run = scratch => Straus7SectionLibrary.MeshLibrary(key == "BGL", library, item, scratch);
            }
            else if (key == "BXS")
            {
                if (!File.Exists(bxsPath)) throw new FileNotFoundException("BXS non trovato.", bxsPath);
                var bytes = File.ReadAllBytes(bxsPath);
                using (var sha = SHA256.Create()) id = "BXS|" + Path.GetFileName(bxsPath) + "|" + BitConverter.ToString(sha.ComputeHash(bytes));
                var captured = Straus7SectionDefinition.CaptureBxs(bxsPath);
                run = scratch => Straus7SectionLibrary.Mesh(captured, scratch);
            }
            else
            {
                var shape = Straus7SectionCatalog.Get(key); var names = shape.Dimensions;
                if (dimensions == null || dimensions.Length != names.Length) throw new ArgumentException(key + ": attese " + names.Length + " dimensioni.");
                var values = dimensions.Select((d, i) => names[i].StartsWith("alpha", StringComparison.Ordinal) ? d : d * scale).ToArray();
                id = key + "|" + string.Join(",", values.Select(v => BitConverter.DoubleToInt64Bits(v).ToString("X16")));
                // Display discretisation of the circular shapes as the former preview (properties are read before it).
                run = scratch => Straus7SectionLibrary.Mesh(shape.Create(values), scratch, 6);
            }
            return cache.GetOrAdd(id, _ => Run(run));
        }

        private static Resolved Run(Func<string, Straus7SectionMesh> run)
        {
            string scratch = Path.Combine(Path.GetTempPath(), "BridGHe", "section-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                var mesh = run(scratch);
                return new Resolved { Section = mesh.Section, Vertices = mesh.Vertices, Faces = mesh.Faces };
            }
            finally { try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
