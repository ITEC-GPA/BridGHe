using System;
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
    /// R6.4 (2), extension (α) of the contract of §1bis (approved by the coordinator on 7/11): the drafts of the elements of a model (the datum
    /// of a <c>GH_Model</c>), in the order of the elements of the model (nodes, beams, plates): for every element the draft the conversion of
    /// its FEM value would give (§1bis, <see cref="GpcBeamDrafts.FromFemm(Models.BeamModel, Guid?)"/> and the others), so that the components
    /// that give the elements of a model back (Decompose Model, Super Element Filter) carry their GPC data without the FEM values. The GPC model
    /// of a conversion is resolved (restraints and springs in one, labels beyond the first dropped, node loads summed, beams re-anchored and
    /// renumbered): the draft of an element cannot be derived from it exactly, so the model carries them.
    /// <list type="bullet">
    /// <item>Producers: Build Model (<see cref="LegacyBuildModel.Build"/>: the drafts after the IDs, the merges by GH GUID and by position, the
    /// nodes created at the beam ends and plate corners, the re-anchoring), the reader of a former model by value (<see cref="GpcLegacyPayloads.Model"/>),
    /// Attach Moving Paths (unchanged), the analysis components (those of their input), the persistence of the model (<see cref="Chunk"/>).
    /// A model without drafts (null): a conversion of its FEM value, or a producer not on (α) yet; its elements keep the conversion of their
    /// FEM values until R6.5.</item>
    /// <item>Identity: that of the producer (the drafts of Build Model keep the identity of their first input datum, a node it creates that of
    /// the created node); until R6.5 Decompose Model and Super Element Filter give every draft the identity of the conversion of its FEM value
    /// (<see cref="GpcDrafts.Reidentify(LegacyNodeDraft, Guid)"/>), as every producer of §1bis, and the DRAFT CHECK compares them with that
    /// conversion on every run.</item>
    /// <item>Units: the definition units (<see cref="GpcBeamDrafts.DraftUnits"/>), as every draft.</item>
    /// </list>
    /// Immutable by convention: whoever changes a draft copies it.
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

        /// <summary>The chunk of the <c>GH_Model</c> that holds the drafts of its elements (beside the two chunks of its GPC input).</summary>
        public const string Chunk = "BridGHeDrafts";
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("BGHDRF1");
        private const byte Version = 1;
        private const int MaximumBytes = GpcGooArchive.MaximumBytes;

        /// <summary>The drafts as one chunk: header BGHDRF1, a version, then deflated: the three counts and, for every draft in order (nodes,
        /// beams, plates), its two chunks of the final writer (<see cref="GpcDraftArchive.Write(LegacyNodeDraft)"/> and the others: the BGHGPC1
        /// value of its element and its BridGHe records), each with its length. No BinaryFormatter, no NRBF.</summary>
        public static byte[] Write(GpcModelDrafts drafts)
        {
            if (drafts == null) throw new ArgumentNullException(nameof(drafts));
            using var output = new MemoryStream();
            output.Write(Header, 0, Header.Length); output.WriteByte(Version);
            using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
            using (var writer = new BinaryWriter(deflate, Encoding.UTF8, true))
            {
                writer.Write(drafts.Nodes.Count); writer.Write(drafts.Beams.Count); writer.Write(drafts.Plates.Count);
                void Pair((byte[] Data, byte[] Records) chunks) { writer.Write(chunks.Data.Length); writer.Write(chunks.Data); writer.Write(chunks.Records.Length); writer.Write(chunks.Records); }
                foreach (var d in drafts.Nodes) Pair(GpcDraftArchive.Write(d));
                foreach (var d in drafts.Beams) Pair(GpcDraftArchive.Write(d));
                foreach (var d in drafts.Plates) Pair(GpcDraftArchive.Write(d));
            }
            if (output.Length > MaximumBytes) throw new SerializationException("BridgheModelDraftsTooLarge");
            return output.ToArray();
        }

        public static GpcModelDrafts Read(byte[] data)
        {
            if (data == null || data.Length < Header.Length + 1 || !data.Take(Header.Length).SequenceEqual(Header)) throw new SerializationException("NotBridgheModelDrafts");
            if (data[Header.Length] != Version) throw new SerializationException("UnsupportedBridgheModelDraftsVersion: " + data[Header.Length]);
            using var input = new MemoryStream(data, Header.Length + 1, data.Length - Header.Length - 1, false);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var reader = new BinaryReader(deflate, Encoding.UTF8);
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
            for (int i = 0; i < nodes; i++) n.Add(GpcDraftArchive.ReadNode(Bytes(), Bytes()));
            for (int i = 0; i < beams; i++) b.Add(GpcDraftArchive.ReadBeam(Bytes(), Bytes()));
            for (int i = 0; i < plates; i++) p.Add(GpcDraftArchive.ReadPlate(Bytes(), Bytes()));
            return new GpcModelDrafts(n, b, p);
        }
    }
}
