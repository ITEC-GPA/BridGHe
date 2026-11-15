using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GPC.Model.Persistence;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// F6 phase (c), C4 / persistence (step 12 of the notes): the analysed GPC model of an analysis component (its results as GPC datasets)
    /// kept next to its native output, <c>&lt;output&gt;.bghr</c> beside the .st7, as master's results file (d7e4092) but with a GPC payload.
    /// Format: "BGHR2", a version byte (1), then the compact GPC model archive (ModelArchive version 6, deflated by the archive itself).
    /// Written through a temporary file, so a partial file never replaces the previous results; the document keeps the path and the
    /// SHA-256 of the file. A results file of master (BGHR1, FeMM payload) is recognised and refused with a clear message, never read.
    /// </summary>
    public static class GpcResultsFile
    {
        public const string Extension = ".bghr";
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("BGHR2");
        private static readonly byte[] PreviousHeader = Encoding.ASCII.GetBytes("BGHR1");
        private const byte Version = 1;
        public const string PreviousVersionMessage = "Risultati di una versione precedente di BridGHe: rilanciare Run.";

        /// <summary>The results file of the analysis written to <paramref name="outputPath"/>.</summary>
        public static string PathFor(string outputPath) => Path.ChangeExtension(Path.GetFullPath(outputPath), Extension);

        /// <summary>True when the model carries results of an analysis.</summary>
        public static bool HasResults(GPC.Model.Models.Model model) => model != null && model.Datasets.Count != 0;

        /// <summary>Writes <paramref name="model"/> and returns the SHA-256 of the file (upper-case hexadecimal).</summary>
        public static string Write(string path, GPC.Model.Models.Model model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                file.Write(Header, 0, Header.Length); file.WriteByte(Version);
                ModelArchive.Save(model, file, ModelArchiveFormat.Compact);
            }
            File.Move(temporary, path, true);
            return Hash(path);
        }

        public static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        /// <summary>True when the file is a results file of master (BGHR1, FeMM payload).</summary>
        public static bool IsPreviousVersion(string path)
        {
            using var stream = File.OpenRead(path);
            var head = new byte[PreviousHeader.Length];
            return stream.Read(head, 0, head.Length) == head.Length && head.SequenceEqual(PreviousHeader);
        }

        /// <summary>R6.4 (coordinator, 7/11): the bytes of a results file of <paramref name="model"/> (the same format), for a model internalised
        /// with its results in a Grasshopper document (<c>GH_Model</c>, chunk <see cref="Chunk"/>).</summary>
        public static byte[] ToBytes(GPC.Model.Models.Model model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            using var stream = new MemoryStream();
            stream.Write(Header, 0, Header.Length); stream.WriteByte(Version);
            ModelArchive.Save(model, stream, ModelArchiveFormat.Compact);
            return stream.ToArray();
        }

        /// <summary>The model of <see cref="ToBytes"/>.</summary>
        public static GPC.Model.Models.Model FromBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length <= Header.Length + 1 || !bytes.AsSpan(0, Header.Length).SequenceEqual(Header)) throw new InvalidDataException("Formato dei risultati non riconosciuto.");
            if (bytes[Header.Length] != Version) throw new InvalidDataException("Versione dei risultati non supportata.");
            using var stream = new MemoryStream(bytes, Header.Length + 1, bytes.Length - Header.Length - 1, false);
            return ModelArchive.Load(stream);
        }

        /// <summary>The item of an internalised <c>GH_Model</c> with the analysed GPC model and its results (<see cref="ToBytes"/>).</summary>
        public const string Chunk = "BridGHeResults";

        /// <summary>Reads the model only when the SHA-256 of the file is still <paramref name="hash"/>.</summary>
        public static GPC.Model.Models.Model Read(string path, string hash)
        {
            if (IsPreviousVersion(path)) throw new InvalidDataException(PreviousVersionMessage + " (" + path + ")");
            byte[] bytes = File.ReadAllBytes(path);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Il file dei risultati è cambiato dopo il salvataggio del documento: " + path);
            if (bytes.Length <= Header.Length + 1 || !bytes.AsSpan(0, Header.Length).SequenceEqual(Header))
                throw new InvalidDataException("Formato del file dei risultati non riconosciuto: " + path);
            if (bytes[Header.Length] != Version) throw new InvalidDataException("Versione del file dei risultati non supportata: " + path);
            using var stream = new MemoryStream(bytes, Header.Length + 1, bytes.Length - Header.Length - 1, false);
            return ModelArchive.Load(stream);
        }
    }
}
