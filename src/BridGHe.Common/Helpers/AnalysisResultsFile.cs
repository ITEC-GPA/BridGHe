using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BridGHe.Common.Models;

namespace BridGHe.Common.Helpers
{
    /// <summary>
    /// Results of an analysis kept next to its native files (the output .st7) instead of inside the Grasshopper document
    /// (4/11/2026). The document keeps only the path and the SHA-256 of the file, so saving it, and every Grasshopper
    /// autosave, no longer rewrites hundreds of MB of results.
    /// Format: "BGHR1", then a deflate stream with the number of element GUIDs, the GUIDs, the length of the
    /// <see cref="ModelArchive"/> of the model with its results and the archive itself.
    /// </summary>
    public static class AnalysisResultsFile
    {
        public const string Extension = ".bghr";
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("BGHR1");

        /// <summary>The results file of the analysis written to <paramref name="outputPath"/>.</summary>
        public static string PathFor(string outputPath) => Path.ChangeExtension(Path.GetFullPath(outputPath), Extension);

        /// <summary>True when at least one element of <paramref name="model"/> carries analysis results.</summary>
        public static bool HasResults(ModelModel model) => model != null && model.Elements.Any(e => e.Results.Count > 0);

        /// <summary>Writes <paramref name="model"/> with the GUIDs of its elements and returns the SHA-256 of the file.</summary>
        public static string Write(string path, ModelModel model, Guid[] elementGuids)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            return WriteArchive(path, ModelArchive.Write(model), elementGuids);
        }

        /// <summary>As <see cref="Write"/>, for a model already in its <see cref="ModelArchive"/> form (a saved document).</summary>
        public static string WriteArchive(string path, byte[] archive, Guid[] elementGuids)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            elementGuids ??= Array.Empty<Guid>();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            // A partial file never replaces the previous results.
            string temporary = path + ".tmp";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(Header);
                using var deflate = new DeflateStream(file, CompressionLevel.Fastest, true);
                using var writer = new BinaryWriter(deflate);
                writer.Write(elementGuids.Length);
                foreach (var guid in elementGuids) writer.Write(guid.ToByteArray());
                writer.Write(archive.Length);
                writer.Write(archive);
            }
            File.Move(temporary, path, true);
            return Hash(path);
        }

        /// <summary>SHA-256 of the file, in upper-case hexadecimal.</summary>
        public static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        /// <summary>Reads the model only when the SHA-256 of the file is still <paramref name="hash"/>.</summary>
        public static ModelModel Read(string path, string hash, out Guid[] elementGuids) =>
            ModelArchive.Read<ModelModel>(ReadArchive(path, hash, out elementGuids));

        /// <summary>As <see cref="Read"/>, returning the <see cref="ModelArchive"/> bytes of the model.</summary>
        public static byte[] ReadArchive(string path, string hash, out Guid[] elementGuids)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Il file dei risultati è cambiato dopo il salvataggio del documento: " + path);
            if (bytes.Length < Header.Length || !bytes.AsSpan(0, Header.Length).SequenceEqual(Header))
                throw new InvalidDataException("Formato del file dei risultati non riconosciuto: " + path);
            using var deflate = new DeflateStream(new MemoryStream(bytes, Header.Length, bytes.Length - Header.Length), CompressionMode.Decompress);
            using var reader = new BinaryReader(deflate);
            int count = reader.ReadInt32();
            if (count < 0) throw new InvalidDataException("File dei risultati non valido: " + path);
            elementGuids = new Guid[count];
            for (int i = 0; i < count; i++) elementGuids[i] = new Guid(reader.ReadBytes(16));
            int length = reader.ReadInt32();
            if (length < 0) throw new InvalidDataException("File dei risultati non valido: " + path);
            byte[] archive = reader.ReadBytes(length);
            if (archive.Length != length) throw new InvalidDataException("File dei risultati troncato: " + path);
            return archive;
        }
    }
}
