using System;
using System.Linq;
using System.Security.Cryptography;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// Chain E (d): the GPC draft of a beam upstream of Build Model (§1bis, L12 §5.4: the <c>GH_Beam</c> carries the <see cref="GPC.Model.Elements.BeamElement"/>
    /// and its <see cref="GPC.Checkers.CompositeBridge.BridgeBeamData"/>), while the beams are still FEM values: the per-value conversion of the
    /// legacy reader (<see cref="LegacyModelImport.BeamDraft"/>, the rules of the conversion of a whole model), numbers in the definition units
    /// (Build Model normalises). The results of the former value are not converted. Identity: from the content of the former value (the same
    /// value gives the same GUID), or the one given (a GPC transformer keeps the GUID of its input beam; the comparisons convert its FEM output
    /// with it).
    /// </summary>
    public static class GpcBeamDrafts
    {
        /// <summary>Draft units: the numbers of the definition (identity factors).</summary>
        public static readonly BridgeUnits DraftUnits = new BridgeUnits(DefinitionUnits.Nmm);

        public static LegacyBeamDraft FromFemm(BeamModel beam) => FromFemm(beam, null);

        /// <summary>R6.1 (frozen API for R6.2): the identity the conversion of a FEM value gives its draft (<see cref="FromFemm(BeamModel, Guid?)"/>
        /// without an identity), for the draft a GPC builder attaches to that value while the FEM value is still built beside it (until R6.5:
        /// then the identity comes from the GPC draft itself, one change listed in the notes).</summary>
        public static Guid Identity(BeamModel beam) => GpcIdentity.Derive(GpcIdentity.NameSpace, "femm-beam:" + Convert.ToHexString(SHA256.HashData(Archive(beam))));
        public static Guid Identity(NodeModel node) => GpcIdentity.Derive(GpcIdentity.NameSpace, "femm-node:" + Convert.ToHexString(SHA256.HashData(Archive(node))));
        public static Guid Identity(PlateModel plate) => GpcIdentity.Derive(GpcIdentity.NameSpace, "femm-plate:" + Convert.ToHexString(SHA256.HashData(Archive(plate))));

        /// <summary>The archive of a FEM value without its results (the conversion never reads them).</summary>
        private static byte[] Archive<T>(T value) where T : ElementModel
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Results.Count != 0) { value = ModelArchive.Clone(value); value.Results.Clear(); }
            return ModelArchive.Write(value);
        }

        public static LegacyBeamDraft FromFemm(BeamModel beam, Guid? identity)
        {
            if (beam == null) throw new ArgumentNullException(nameof(beam));
            byte[] archive = Archive(beam);
            var guid = identity ?? GpcIdentity.Derive(GpcIdentity.NameSpace, "femm-beam:" + Convert.ToHexString(SHA256.HashData(archive)));
            string signature = beam.BeamProperty == null ? null : BridgeSectionAssignment.Signature(beam.BeamProperty);
            return LegacyModelImport.BeamDraft(LegacyArchive.Read(archive), DraftUnits, guid, signature);
        }

        /// <summary>Chain E (e1): the draft of a FEM node value (the rules of the conversion of a whole model, definition units, rigid links as
        /// GPC rigid connections); identity from the content of the value, or the one given. Results are never converted.</summary>
        public static LegacyNodeDraft FromFemm(NodeModel node, Guid? identity = null)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            byte[] archive = Archive(node);
            var guid = identity ?? GpcIdentity.Derive(GpcIdentity.NameSpace, "femm-node:" + Convert.ToHexString(SHA256.HashData(archive)));
            return LegacyModelImport.NodeDraft(LegacyArchive.Read(archive), DraftUnits, guid);
        }

        /// <summary>Chain E (e1): the draft of a FEM plate value (the rules of the conversion of a whole model, definition units, no nodes: its
        /// corners); identity from the content of the value, or the one given. Results are never converted.</summary>
        public static LegacyPlateDraft FromFemm(PlateModel plate, Guid? identity = null)
        {
            if (plate == null) throw new ArgumentNullException(nameof(plate));
            byte[] archive = Archive(plate);
            var guid = identity ?? GpcIdentity.Derive(GpcIdentity.NameSpace, "femm-plate:" + Convert.ToHexString(SHA256.HashData(archive)));
            return LegacyModelImport.PlateDraft(LegacyArchive.Read(archive), DraftUnits, guid);
        }

        /// <summary>A copy of a draft with another beam and bridge data (the report and the property data of the draft are kept).</summary>
        /// <remarks>R6.1 (found by GpcDraftCheck on the native 50): the bridge data of the report follow the bridge data given (the report of the
        /// conversion of a beam value holds the data of that beam), so the copy describes its beam as the conversion would; the rest of the report
        /// is kept. Nothing reads the bridge data of a draft report (the assembly takes the bridge data of the draft): no number changes.</remarks>
        public static LegacyBeamDraft With(LegacyBeamDraft draft, GPC.Model.Elements.BeamElement beam, GPC.Checkers.CompositeBridge.BridgeBeamData bridge, LegacyBeamPropertyData property = null) =>
            new LegacyBeamDraft(beam, bridge, draft.AngleDeg, property ?? draft.Property, GpcDrafts.WithBridge(draft.Report, bridge)) { Records = draft.Records };
    }
}
