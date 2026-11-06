using System;
using System.Linq;
using System.Security.Cryptography;
using BridGHe.Common.Gpc.Legacy;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// R6.1 (coordinator decisions of 6/11 on the pre-removal checkpoint): the GPC datum of a payload of the former GH types (BGHA1, a
    /// DataContract graph of FeMM values), read by value without the FeMM classes (<see cref="LegacyArchive"/> + the readers of
    /// <see cref="LegacyModelImport"/>, the rules of the conversion of a whole model, proven by <c>--legacy-payload-scan</c>). Numbers in the
    /// definition units (<see cref="GpcBeamDrafts.DraftUnits"/>), as the drafts of the GH types; materials in N-mm (MPa).
    /// <list type="bullet">
    /// <item>Identity (decision 2): from the stored bytes, <c>SHA-256</c> of the payload with the prefix of the former value conversion
    /// (<c>femm-node:</c>, <c>femm-beam:</c>, <c>femm-plate:</c>); equal to the identity of the former value but where the stored bytes are
    /// not those of a rewrite of the value (1 beam of example 37: only its GUIDs change).</item>
    /// <item>Section states (decision 4): no former property signature: a state read by value is current, and its property fingerprint is
    /// the GPC one (no payload of the examples has a state).</item>
    /// <item>Models (decision 1, one rule for all, analysed or not): the model is converted without its results; the caller reports that
    /// results were dropped (<see cref="ResultsDroppedMessage"/>). A payload from which 0.3.0 reads no model gives null.</item>
    /// </list>
    /// A payload the reader refuses throws its message (as the former conversion of the value did).
    /// </summary>
    public static class GpcLegacyPayloads
    {
        /// <summary>The owner of the identities of a former model read by value: from the stored bytes and the tolerance (decision 2).</summary>
        public static Guid ModelOwner(byte[] payload, double toleranceDefinitionUnits) => GpcIdentity.Derive(GpcIdentity.NameSpace, "femm-model:"
            + Convert.ToHexString(SHA256.HashData(payload ?? throw new ArgumentNullException(nameof(payload)))) + ":" + toleranceDefinitionUnits.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

        /// <summary>The warning of a model read from a former payload that carried results (decision 1).</summary>
        public const string ResultsDroppedMessage = "Risultati di una versione precedente di BridGHe: non più letti, ricalcolare l'analisi.";

        public static Guid Identity(string prefix, byte[] payload) =>
            GpcIdentity.Derive(GpcIdentity.NameSpace, prefix + Convert.ToHexString(SHA256.HashData(payload ?? throw new ArgumentNullException(nameof(payload)))));

        private static LegacyValue Root(byte[] payload) => LegacyArchive.Read(payload ?? throw new ArgumentNullException(nameof(payload)));

        public static LegacyNodeDraft Node(byte[] payload) => LegacyModelImport.NodeDraft(Root(payload), GpcBeamDrafts.DraftUnits, Identity("femm-node:", payload));

        public static LegacyBeamDraft Beam(byte[] payload) => LegacyModelImport.BeamDraft(Root(payload), GpcBeamDrafts.DraftUnits, Identity("femm-beam:", payload));

        public static LegacyPlateDraft Plate(byte[] payload) => LegacyModelImport.PlateDraft(Root(payload), GpcBeamDrafts.DraftUnits, Identity("femm-plate:", payload));

        public static GPC.Model.Materials.Material Material(byte[] payload) => LegacyModelImport.Material(Root(payload));

        public static LegacyCaseData Case(byte[] payload) => LegacyModelImport.CaseDraft(Root(payload));

        public static LegacyCombinationDraft Combination(byte[] payload) => LegacyModelImport.CombinationDraft(Root(payload));

        public static LegacyStageDraft Stage(byte[] payload) => LegacyModelImport.StageDraft(Root(payload));

        public static LegacySectionRecipe Section(byte[] payload) => LegacyModelImport.SectionDraft(Root(payload));

        public static LegacyBeamProperty BeamProperty(byte[] payload) => LegacyModelImport.BeamProperty(Root(payload), GpcBeamDrafts.DraftUnits);

        public static GPC.Model.ElementProperties.PlateProperty PlateProperty(byte[] payload) => LegacyModelImport.PlatePropertyDraft(Root(payload), GpcBeamDrafts.DraftUnits);

        public static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Composite(byte[] payload) => GpcCompositeSections.Definition(Root(payload));

        /// <summary>A former model (<c>GH_Model</c>, internalised or the cache of an analysis component) by value, at the tolerance given
        /// (definition units): the model without its results, the owner of its identities from the stored bytes and the tolerance; null when
        /// 0.3.0 reads no model from it (null value or no element list). <paramref name="hadResults"/>: the payload carried results.
        /// <paramref name="owner"/>: another owner of the identities (the proofs give the owner of the former FEM path, to compare the data
        /// apart from the renaming of the GUIDs that the owner of the stored bytes gives).</summary>
        public static GpcModels.Conversion Model(byte[] payload, double toleranceDefinitionUnits, out bool hadResults, Guid? owner = null)
        {
            var root = Root(payload);
            hadResults = false;
            if (root.IsNull || !root.Is("ModelModel") || !root.Has("Elements")) return null;
            hadResults = root.Field("Elements").Items.Any(e => !e.IsNull && e.Has("Results") && e.Field("Results").Items.Count != 0);
            var identities = owner ?? ModelOwner(payload, toleranceDefinitionUnits);
            var report = new LegacyImportReport();
            var model = LegacyModelImport.Import(root, identities, toleranceDefinitionUnits, report);
            return new GpcModels.Conversion(model, report, toleranceDefinitionUnits);
        }
    }
}
