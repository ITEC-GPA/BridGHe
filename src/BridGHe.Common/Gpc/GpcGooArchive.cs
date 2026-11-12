using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using GPC.Model.Persistence;
using GModel = GPC.Model.Models.Model;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// Bytes of the BridGHe Grasshopper data (internalized parameters, copies, undo) once the GH types carry GPC objects: GPC contracts
    /// only, as Arachne does. Header BGHGPC1, a version and a kind, then the deflated GPC archive (<see cref="ModelValueArchive"/> for a
    /// value, <see cref="ModelArchive"/> for a model). The root type is chosen by the reader, never by the data. No BinaryFormatter, no NRBF.
    /// The former payloads (BGHA1, DataContract binary XML of FeMM graphs) are not read here: <see cref="IsLegacy"/> lets the GH type
    /// read them as data with <see cref="Legacy.LegacyArchive"/> and convert them (inputs) or drop them with <see cref="LegacyCacheMessage"/>
    /// (caches). Version 1 is the XML archive; the compact format of lane L5 becomes version 2 when it lands.
    /// </summary>
    public static class GpcGooArchive
    {
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("BGHGPC1");
        private const byte Version = 1;
        public const int MaximumBytes = 256 * 1024 * 1024;

        /// <summary>The kind of an archive: a standalone value or a model.</summary>
        public enum Kind : byte { Value = 1, Model = 2, BridgeModel = 3 }

        public const string LegacyCacheMessage = "Dati di una versione precedente di BridGHe (risultati o cache di calcolo): non più letti. Ricalcolare il componente.";

        public static byte[] Value<T>(T value) where T : class
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return Pack(Kind.Value, stream => ModelValueArchive.Save(value, stream));
        }

        public static T ReadValue<T>(byte[] data) where T : class => Unpack(data, Kind.Value, ModelValueArchive.Load<T>);

        /// <summary>The GPC archive of a value without the BGHGPC1 header and not compressed (the bytes <see cref="Value{T}"/> deflates): the
        /// drafts of a model compress all their values in one stream (<see cref="GpcModelDrafts.Write"/>, BGHDRF2).</summary>
        internal static byte[] RawValue<T>(T value) where T : class
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            using (var output = new MemoryStream())
            {
                ModelValueArchive.Save(value, output);
                if (output.Length > MaximumBytes) throw new SerializationException("BridgheGpcArchiveTooLarge");
                return output.ToArray();
            }
        }

        /// <summary>A value from <see cref="RawValue{T}"/>; the root type is chosen by the reader.</summary>
        internal static T ReadRawValue<T>(byte[] raw) where T : class
        {
            if (raw == null) throw new ArgumentNullException(nameof(raw));
            if (raw.Length > MaximumBytes) throw new SerializationException("BridgheGpcArchiveTooLarge");
            using (var input = new MemoryStream(raw, false)) return ModelValueArchive.Load<T>(input);
        }

        public static byte[] Model(GModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            return Pack(Kind.Model, stream => ModelArchive.SaveDocument(model, stream));
        }

        public static GModel ReadModel(byte[] data) => Unpack(data, Kind.Model, ModelArchive.Load);

        /// <summary>F6 (L12): a GPC model with its bridge data, in the one stream of <see cref="GPC.Checkers.CompositeBridge.BridgeModel.Save(Stream)"/>
        /// (no new format: header GPCBRG, the bridge data, then the model archive).</summary>
        public static byte[] BridgeModel(GPC.Checkers.CompositeBridge.BridgeModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            return Pack(Kind.BridgeModel, model.Save);
        }

        /// <summary>F6 (L12): a GPC model with its bridge data; an archive of a model alone (<see cref="Kind.Model"/>) gives empty bridge data.
        /// Archives written on the integration branch before L12 with the former bridge attributes do not load (unknown types): stale caches.</summary>
        public static GPC.Checkers.CompositeBridge.BridgeModel ReadBridgeModel(byte[] data)
        {
            if (data != null && IsGpc(data) && data.Length > Header.Length + 1 && data[Header.Length + 1] == (byte)Kind.Model)
                return new GPC.Checkers.CompositeBridge.BridgeModel(ReadModel(data));
            return Unpack(data, Kind.BridgeModel, GPC.Checkers.CompositeBridge.BridgeModel.Load);
        }

        public static bool IsGpc(byte[] data) => data != null && data.Length > Header.Length + 2 && data.Take(Header.Length).SequenceEqual(Header);

        /// <summary>A payload of the former FeMM GH types (BGHA1).</summary>
        public static bool IsLegacy(byte[] data) => data != null && Legacy.LegacyArchive.IsLegacy(data);

        private static byte[] Pack(Kind kind, Action<Stream> write)
        {
            using (var output = new MemoryStream())
            {
                output.Write(Header, 0, Header.Length); output.WriteByte(Version); output.WriteByte((byte)kind);
                using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true)) write(deflate);
                if (output.Length > MaximumBytes) throw new SerializationException("BridgheGpcArchiveTooLarge");
                return output.ToArray();
            }
        }

        private static T Unpack<T>(byte[] data, Kind kind, Func<Stream, T> read)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (IsLegacy(data)) throw new SerializationException("BridgheLegacyPayload");
            if (!IsGpc(data)) throw new SerializationException("NotABridgheGpcArchive");
            if (data[Header.Length] > Version) throw new GpcNewerFormatException("archivio BGHGPC1 versione " + data[Header.Length]); // P4 (P2D, U1)
            if (data[Header.Length] != Version) throw new SerializationException("UnsupportedBridgheGpcArchiveVersion: " + data[Header.Length]);
            if (data[Header.Length + 1] != (byte)kind) throw new SerializationException("BridgheGpcArchiveKindMismatch");
            using (var input = new MemoryStream(data, Header.Length + 2, data.Length - Header.Length - 2, false))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var limited = new LimitedStream(deflate, MaximumBytes))
                return read(limited);
        }

        /// <summary>Refuses inflated data above the archive limit (decompression bombs).</summary>
        private sealed class LimitedStream : Stream
        {
            private readonly Stream inner; private readonly long limit; private long read;
            internal LimitedStream(Stream inner, long limit) { this.inner = inner; this.limit = limit; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int n = inner.Read(buffer, offset, count); read += n;
                if (read > limit) throw new SerializationException("BridgheGpcArchiveTooLarge");
                return n;
            }
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => read; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
