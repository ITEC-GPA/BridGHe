using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using BridGHe.Common.Gpc.Legacy;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// Element drafts carried beside a resolved GPC model. Resolution can merge restraints, labels, loads and nodes, so the original
    /// drafts cannot be reconstructed exactly from the resolved model alone. Build Model, the supported legacy reader, analysis
    /// inputs and persistence provide them. Consumers require matching element counts and stable element identities; absence is
    /// reported explicitly and never falls back to removed FEM objects.
    /// </summary>
    public sealed class GpcModelDrafts
    {
        public IReadOnlyList<LegacyNodeDraft> Nodes { get; }
        public IReadOnlyList<LegacyBeamDraft> Beams { get; }
        public IReadOnlyList<LegacyPlateDraft> Plates { get; }

        public GpcModelDrafts(IEnumerable<LegacyNodeDraft> nodes, IEnumerable<LegacyBeamDraft> beams, IEnumerable<LegacyPlateDraft> plates)
        {
            Nodes = (nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray();
            Beams = (beams ?? throw new ArgumentNullException(nameof(beams))).ToArray();
            Plates = (plates ?? throw new ArgumentNullException(nameof(plates))).ToArray();
            if (Nodes.Any(d => d == null) || Beams.Any(d => d == null) || Plates.Any(d => d == null)) throw new ArgumentException("GpcModelDraftsNullDraft");
        }

        /// <summary>True when these are the drafts of a model with these element counts (the FEM model of the GH datum until R6.5: a model whose
        /// FEM elements merged by ID, <c>ModelModel.AddElements</c>, has fewer elements in its lists than in its element list, and none fits).</summary>
        public bool Fits(int nodes, int beams, int plates) => Nodes.Count == nodes && Beams.Count == beams && Plates.Count == plates;

        /// <summary>The drafts of a former model read by value (BGHA1, <see cref="GpcLegacyPayloads.Model"/>): every element of its element list
        /// by the reader of one value (<see cref="LegacyModelImport.NodeDraft"/>, <see cref="LegacyModelImport.BeamDraft"/>,
        /// <see cref="LegacyModelImport.PlateDraft"/>) in the definition units; no former property signature (a section state is current, decision 4
        /// of 6/11, as for the elements read by value); identities from the owner of the model and the place of the element.</summary>
        internal static GpcModelDrafts FromLegacy(LegacyValue model, Guid owner)
        {
            var nodes = new List<LegacyNodeDraft>(); var beams = new List<LegacyBeamDraft>(); var plates = new List<LegacyPlateDraft>();
            foreach (var e in model.Field("Elements").Items)
            {
                if (e.Is("NodeModel")) nodes.Add(LegacyModelImport.NodeDraft(e, GpcBeamDrafts.DraftUnits, GpcIdentity.Derive(owner, "model-draft-node:" + nodes.Count)));
                else if (e.Is("BeamModel")) beams.Add(LegacyModelImport.BeamDraft(e, GpcBeamDrafts.DraftUnits, GpcIdentity.Derive(owner, "model-draft-beam:" + beams.Count)));
                else if (e.Is("PlateModel")) plates.Add(LegacyModelImport.PlateDraft(e, GpcBeamDrafts.DraftUnits, GpcIdentity.Derive(owner, "model-draft-plate:" + plates.Count)));
                else throw new NotSupportedException("LegacyElementTypeNotMappedYet: " + e.ShortType);
            }
            return new GpcModelDrafts(nodes, beams, plates);
        }

        // -------------------------------------------------------------------------------------------------------------------------------------
        // Persistence (BGHGPC1 of the GH_Model, extended)

        /// <summary>The chunk of the <c>GH_Model</c> that holds the drafts of its elements (beside the two chunks of its GPC input). The
        /// analysis components do not save it with the model they keep (option 1 of P2Q, coordinator 7/11): they attach the drafts of their
        /// input again at the first solution after opening (<c>Helpers.AnalysisDrafts</c> of the plug-in).</summary>
        public const string Chunk = "BridGHeDrafts";
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("BGHDRF2");
        private const byte Version = 1;
        /// <summary>BGHDRF1 (R6.4 (2), 41d4947 to 64cee6b of the integration branch, never released): read, no longer written.</summary>
        private static readonly byte[] Header1 = Encoding.ASCII.GetBytes("BGHDRF1");
        private const byte Version1 = 1;
        private const int MaximumBytes = GpcGooArchive.MaximumBytes;
        /// <summary>Brotli of the BCL (net8), quality 5 and window 24 (2^24 bytes): measured by P2Q on the drafts of the example 50 (q4–q6:
        /// −81…83% against BGHDRF1, 0.03–0.12 s a model, read 0.01 s; q11 4.6–6.8 s a model).</summary>
        private const int BrotliQuality = 5, BrotliWindow = 24;

        /// <summary>The drafts as one chunk (BGHDRF2, option 2 of P2Q, coordinator 7/11): header BGHDRF2, a version, the length of the stream
        /// (Int32, little endian), then the stream compressed with Brotli in one piece: the three counts and, for every draft in order (nodes,
        /// beams, plates), the GPC archive of the value of its element not compressed (<see cref="GpcGooArchive.RawValue{T}"/>, the bytes the
        /// final writer deflates) and its BridGHe records (<see cref="GpcDraftArchive.WriteRaw(LegacyNodeDraft)"/> and the others), each with
        /// its length. Compressing the values together removes what the drafts of one model repeat (properties, sections, materials,
        /// coordinate systems, restraints). No BinaryFormatter, no NRBF.</summary>
        public static byte[] Write(GpcModelDrafts drafts)
        {
            if (drafts == null) throw new ArgumentNullException(nameof(drafts));
            using var raw = new MemoryStream();
            using (var writer = new BinaryWriter(raw, Encoding.UTF8, true))
            {
                writer.Write(drafts.Nodes.Count); writer.Write(drafts.Beams.Count); writer.Write(drafts.Plates.Count);
                void Pair((byte[] Value, byte[] Records) chunks)
                {
                    writer.Write(chunks.Value.Length); writer.Write(chunks.Value); writer.Write(chunks.Records.Length); writer.Write(chunks.Records);
                    if (raw.Length > MaximumBytes) throw new SerializationException("BridgheModelDraftsTooLarge");
                }
                foreach (var d in drafts.Nodes) Pair(GpcDraftArchive.WriteRaw(d));
                foreach (var d in drafts.Beams) Pair(GpcDraftArchive.WriteRaw(d));
                foreach (var d in drafts.Plates) Pair(GpcDraftArchive.WriteRaw(d));
            }
            var source = new ReadOnlySpan<byte>(raw.GetBuffer(), 0, (int)raw.Length);
            int start = Header.Length + 1 + sizeof(int);
            var packed = new byte[start + BrotliEncoder.GetMaxCompressedLength(source.Length)];
            Header.CopyTo(packed, 0); packed[Header.Length] = Version;
            BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(Header.Length + 1), source.Length);
            if (!BrotliEncoder.TryCompress(source, packed.AsSpan(start), out int written, BrotliQuality, BrotliWindow))
                throw new SerializationException("BridgheModelDraftsNotCompressed");
            if (start + written > MaximumBytes) throw new SerializationException("BridgheModelDraftsTooLarge");
            Array.Resize(ref packed, start + written);
            return packed;
        }

        /// <summary>The drafts of a chunk BGHDRF2 (<see cref="Write"/>) or BGHDRF1 (the integration branch before BGHDRF2).</summary>
        public static GpcModelDrafts Read(byte[] data)
        {
            if (Starts(data, Header1))
            {
                if (data[Header1.Length] != Version1) throw new SerializationException("UnsupportedBridgheModelDraftsVersion: " + data[Header1.Length]);
                using var input = new MemoryStream(data, Header1.Length + 1, data.Length - Header1.Length - 1, false);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                return Read(deflate, GpcDraftArchive.ReadNode, GpcDraftArchive.ReadBeam, GpcDraftArchive.ReadPlate);
            }
            int start = Header.Length + 1 + sizeof(int);
            if (!Starts(data, Header) || data.Length < start) throw new SerializationException("NotBridgheModelDrafts");
            if (data[Header.Length] > Version) throw new GpcNewerFormatException("bozze BGHDRF2 versione " + data[Header.Length]); // P4 (P2D, U1)
            if (data[Header.Length] != Version) throw new SerializationException("UnsupportedBridgheModelDraftsVersion: " + data[Header.Length]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(Header.Length + 1, sizeof(int)));
            if (length < 3 * sizeof(int) || length > MaximumBytes) throw new SerializationException("BridgheModelDraftsInvalidLength");
            var raw = new byte[length];
            using (var decoder = new BrotliDecoder())
            {
                var status = decoder.Decompress(data.AsSpan(start), raw, out int consumed, out int inflated);
                if (status != OperationStatus.Done || inflated != length || consumed != data.Length - start) throw new SerializationException("BridgheModelDraftsTruncated");
            }
            using var stream = new MemoryStream(raw, false);
            return Read(stream, GpcDraftArchive.ReadNodeRaw, GpcDraftArchive.ReadBeamRaw, GpcDraftArchive.ReadPlateRaw);
        }

        private static bool Starts(byte[] data, byte[] header) => data != null && data.Length > header.Length && data.AsSpan(0, header.Length).SequenceEqual(header);

        private static GpcModelDrafts Read(Stream stream, Func<byte[], byte[], LegacyNodeDraft> node, Func<byte[], byte[], LegacyBeamDraft> beam, Func<byte[], byte[], LegacyPlateDraft> plate)
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, true);
            long total = 0;
            byte[] Bytes()
            {
                int length = reader.ReadInt32();
                if (length < 0 || (total += length) > MaximumBytes) throw new SerializationException("BridgheModelDraftsTooLarge");
                var bytes = reader.ReadBytes(length);
                if (bytes.Length != length) throw new SerializationException("BridgheModelDraftsTruncated");
                return bytes;
            }
            int Count() { int n = reader.ReadInt32(); if (n < 0 || n > 10_000_000) throw new SerializationException("BridgheModelDraftsInvalidCount"); return n; }
            int nodes = Count(), beams = Count(), plates = Count();
            var n = new List<LegacyNodeDraft>(nodes); var b = new List<LegacyBeamDraft>(beams); var p = new List<LegacyPlateDraft>(plates);
            for (int i = 0; i < nodes; i++) n.Add(node(Bytes(), Bytes()));
            for (int i = 0; i < beams; i++) b.Add(beam(Bytes(), Bytes()));
            for (int i = 0; i < plates; i++) p.Add(plate(Bytes(), Bytes()));
            // P30-04 (C1, 11/11): the drafts end the stream (counts smaller than the drafts written, or bytes after them, are damage).
            if (stream.ReadByte() != -1) throw new SerializationException("BridgheModelDraftsTrailingData");
            return new GpcModelDrafts(n, b, p);
        }
    }
}
