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

        /// <summary>P2D: a former material by value with the kind it declared (the former <c>MaterialKind</c> by name, "Undefined" when none):
        /// the kind the GH type keeps beside the GPC material (<c>GH_Material.GpcKind</c>), as the FEM value gave it.</summary>
        public static (GPC.Model.Materials.Material Material, string Kind) MaterialWithKind(byte[] payload)
        {
            var recipe = LegacyModelImport.MaterialRecipe(Root(payload));
            return (LegacyModelImport.Material(recipe), string.IsNullOrEmpty(recipe.Kind) ? "Undefined" : recipe.Kind);
        }

        public static LegacyCaseData Case(byte[] payload) => LegacyModelImport.CaseDraft(Root(payload));
        /// <summary>P2CE (13/11/2026, phase 2a): the cases of a saved list of FEM cases (the «LoadCases» item of Staged Construction, written
        /// by 0.3.0 and by the development files) by value, in order, each as <see cref="Case"/> reads one; no FEM CLR type.</summary>
        public static LegacyCaseData[] Cases(byte[] payload)
        {
            var root = Root(payload);
            // The list of the former archive: the collection contract ArrayOfLoadCaseModel (its items carry no type of their own: the element
            // type of the list; a derived item would carry its type).
            if (root.IsNull || !root.IsCollection || root.Type == null || !(root.Is("ArrayOfLoadCaseModel") || root.Type.Contains("BridGHe.Common.Models.LoadCaseModel,", StringComparison.Ordinal)))
                throw new System.Runtime.Serialization.SerializationException("LegacyLoadCaseListExpected: " + (root.Type ?? "null"));
            return root.Items.Select(i => !i.IsNull && i.Type == null ? LegacyCaseData.From(i) : !i.IsNull && i.Is("LoadCaseModel") ? LegacyModelImport.CaseDraft(i)
                : throw new System.Runtime.Serialization.SerializationException("LegacyLoadCaseExpected: " + (i.IsNull ? "null" : i.Type))).ToArray();
        }

        public static LegacyCombinationDraft Combination(byte[] payload) => LegacyModelImport.CombinationDraft(Root(payload));

        public static LegacyStageDraft Stage(byte[] payload) => LegacyModelImport.StageDraft(Root(payload));

        public static LegacySectionRecipe Section(byte[] payload) => LegacyModelImport.SectionDraft(Root(payload));

        public static LegacyBeamProperty BeamProperty(byte[] payload) => LegacyModelImport.BeamProperty(Root(payload), GpcBeamDrafts.DraftUnits);

        public static GPC.Model.ElementProperties.PlateProperty PlateProperty(byte[] payload) => LegacyModelImport.PlatePropertyDraft(Root(payload), GpcBeamDrafts.DraftUnits);

        /// <summary>L2C: the bending thickness of a former membrane plate property (<see cref="LegacyModelImport.PlatePropertyMembraneBending"/>).</summary>
        public static double PlatePropertyMembraneBending(byte[] payload) => LegacyModelImport.PlatePropertyMembraneBending(Root(payload), GpcBeamDrafts.DraftUnits);

        public static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Composite(byte[] payload) => GpcCompositeSections.Definition(Root(payload));

        /// <summary>R6.5 P1 lane 3c: the payload of a former model carried results (the notice of a model read from it, without its FEM value).</summary>
        public static bool ModelHadResults(byte[] payload) => HadResults(Root(payload));

        private static bool HadResults(LegacyValue root) => !root.IsNull && root.Is("ModelModel") && root.Has("Elements")
            && root.Field("Elements").Items.Any(e => !e.IsNull && e.Has("Results") && e.Field("Results").Items.Count != 0);

        /// <summary>A former model (<c>GH_Model</c>, internalised or the cache of an analysis component) by value, at the tolerance given
        /// (definition units): the model without its results, the owner of its identities from the stored bytes and the tolerance; null when
        /// 0.3.0 reads no model from it (null value or no element list). <paramref name="hadResults"/>: the payload carried results.
        /// <paramref name="owner"/>: another owner of the identities (the proofs give the owner of the former FEM path, to compare the data
        /// apart from the renaming of the GUIDs that the owner of the stored bytes gives).</summary>
        public static GpcModels.Conversion Model(byte[] payload, double toleranceDefinitionUnits, out bool hadResults, Guid? owner = null) =>
            Model(Root(payload), payload, toleranceDefinitionUnits, out hadResults, owner);

        /// <summary>P2D: <see cref="Model(byte[], double, out bool, Guid?)"/> with the kinds of the former elements in their order
        /// (<see cref="Kinds"/>), for the GH GUIDs saved with them (<see cref="InDraftOrder"/>).</summary>
        public static GpcModels.Conversion Model(byte[] payload, double toleranceDefinitionUnits, out bool hadResults, out string elementKinds)
        {
            var root = Root(payload);
            elementKinds = Kinds(root);
            return Model(root, payload, toleranceDefinitionUnits, out hadResults, null);
        }

        /// <summary>P2D: the kinds of the elements of a former model in their order: N node, B beam, P plate, ? any other (a composite beam,
        /// a null entry); empty without an element list.</summary>
        private static string Kinds(LegacyValue root) => root.IsNull || !root.Is("ModelModel") || !root.Has("Elements") ? ""
            : new string(root.Field("Elements").Items.Select(e => e.IsNull ? '?' : e.Is("NodeModel") ? 'N' : e.Is("BeamModel") ? 'B' : e.Is("PlateModel") ? 'P' : '?').ToArray());

        /// <summary>P2D: the GH GUIDs saved with the elements of a former model (in their order, <c>ElementGuids</c>) in the order of the
        /// drafts of the model (its nodes, then its beams, then its plates, each in the former order); null when they do not match (another
        /// count, or an element that is not a node, a beam or a plate: the model has no drafts).</summary>
        public static Guid[] InDraftOrder(Guid[] guids, string kinds)
        {
            if (guids == null || kinds == null || guids.Length != kinds.Length || kinds.Contains('?')) return null;
            return "NBP".SelectMany(k => Enumerable.Range(0, kinds.Length).Where(i => kinds[i] == k).Select(i => guids[i])).ToArray();
        }

        /// <summary>P2D: the FEM chunk of a development file (<c>FemmR6</c>, the BGHA1 archive of the FEM model a component kept beside the GPC
        /// input until P2) read by value: the drafts of its elements with the identities of <paramref name="owner"/> (when
        /// <paramref name="drafts"/>; null when the reader refuses one of its elements, as for a former payload) and the GH GUIDs saved with
        /// its FEM elements in the order of the drafts (<see cref="InDraftOrder"/>, null when none or not matching).</summary>
        public static (GpcModelDrafts Drafts, Guid[] Guids) Development(byte[] femm, Guid owner, Guid[] femGuids, bool drafts)
        {
            var root = Root(femm);
            GpcModelDrafts read = null;
            if (drafts && !root.IsNull && root.Is("ModelModel") && root.Has("Elements"))
                try { read = GpcModelDrafts.FromLegacy(root, owner); }
                catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException || x is System.Collections.Generic.KeyNotFoundException) { read = null; }
            return (read, InDraftOrder(femGuids, Kinds(root)));
        }

        private static GpcModels.Conversion Model(LegacyValue root, byte[] payload, double toleranceDefinitionUnits, out bool hadResults, Guid? owner)
        {
            hadResults = false;
            if (root.IsNull || !root.Is("ModelModel") || !root.Has("Elements")) return null;
            hadResults = HadResults(root);
            var identities = owner ?? ModelOwner(payload, toleranceDefinitionUnits);
            var report = new LegacyImportReport();
            var model = LegacyModelImport.Import(root, identities, toleranceDefinitionUnits, report);
            // R6.4 (2), extension (α): the drafts of its elements, each read by value as the element payloads are, on first use; a payload whose
            // elements the reader of one value refuses gives none (its elements keep the conversion of their FEM values until R6.5).
            var drafts = new Lazy<GpcModelDrafts>(() =>
            {
                try { return GpcModelDrafts.FromLegacy(root, identities); }
                catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException || x is System.Collections.Generic.KeyNotFoundException) { return null; }
            });
            return new GpcModels.Conversion(model, report, toleranceDefinitionUnits, drafts);
        }
    }
}
