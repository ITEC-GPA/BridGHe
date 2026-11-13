using BridGHe.Grasshopper.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Rhino.Geometry;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_Model : GH_Goo<object>, IGH_BakeAwareData, IGH_PreviewData
    {
        private readonly ConditionalWeakTable<Common.MovingLoads.MovingPath, MovingGoo> movingPreviews = new();

        private readonly ConditionalWeakTable<object, IGH_PreviewData> draftPreviews = new();

        /// <summary>R6.5 P0: the element types of the drafts of a model without FEM value (none without drafts).</summary>
        private IEnumerable<IGH_GeometricGoo> DraftElements()
        {
            var drafts = GpcDrafts;
            if (drafts == null) yield break;
            foreach (var n in drafts.Nodes) yield return (IGH_GeometricGoo)draftPreviews.GetValue(n, d => GH_Node.FromGpc((Common.Gpc.Legacy.LegacyNodeDraft)d));
            foreach (var b in drafts.Beams) yield return (IGH_GeometricGoo)draftPreviews.GetValue(b, d => GH_Beam.FromGpc((Common.Gpc.Legacy.LegacyBeamDraft)d));
            foreach (var p in drafts.Plates) yield return (IGH_GeometricGoo)draftPreviews.GetValue(p, d => GH_Plate.FromGpc((Common.Gpc.Legacy.LegacyPlateDraft)d));
        }

        // P2CE (13/11/2026, phase 2b): the elements of the drafts (a model has no FEM value since P2K/P2D: the same elements as before).
        private IEnumerable<IGH_PreviewData> PreviewElements()
        {
            foreach (var e in DraftElements()) yield return (IGH_PreviewData)e;
        }

        public BoundingBox ClippingBox
        {
            get
            {
                var bounds = BoundingBox.Empty;
                foreach (var preview in PreviewElements())
                    if (preview.ClippingBox.IsValid) bounds.Union(preview.ClippingBox);
                return bounds;
            }
        }

        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            foreach (var preview in PreviewElements())
                if (preview.ClippingBox.IsValid) preview.DrawViewportWires(args);
        }

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            foreach (var preview in PreviewElements())
                if (preview.ClippingBox.IsValid) preview.DrawViewportMeshes(args);
        }


        // F6 phase (c), C2: the GPC model of this FEM model, converted once (GpcModels.Convert, the legacy reader) and kept with it, or the
        // analysed GPC model an analysis component attached (results as GPC datasets). It follows Value: a new FEM model gives a new
        // conversion. Consumers never change it in place (an analysis copies it first); copies of this datum share the analysed model.
        // C3: the analysis and export components take a copy of the conversion (GpcInput) instead of converting the FEM model again.
        private readonly object gpcLock = new object();

        // R6.5 P0 (coordinator, 7/11): the owner of the GPC state (input, conversion, analysed model, drafts, linked results, former payload):
        // the FEM value while the model has one (as before), otherwise the GPC input it was given (FromGpc, a file without the FEM chunk). A
        // new FEM value or a new GPC input of a model without FEM value gives new state.
        private object StateKey => (object)Value ?? _gpcOnly;
        private Common.Gpc.GpcModels.Conversion _gpcOnly;
        private Common.Gpc.GpcModels.Conversion _converted;
        private object _convertedOf;
        private GPC.Model.Models.Model _analysed;
        private object _analysedOf;
        private Common.Gpc.GpcBridge _bridge;
        private string _gpcError;

        /// <summary>The GPC model (N-mm) of <see cref="Value"/>: the analysed model when an analysis attached one, otherwise the conversion;
        /// null, with the reason in <see cref="GpcError"/>, when it cannot be converted.</summary>
        public GPC.Model.Models.Model Gpc
        {
            get
            {
                lock (gpcLock)
                {
                    if (StateKey == null) return null;
                    if (_analysed != null && ReferenceEquals(_analysedOf, StateKey)) return _analysed;
                    try { return Conversion().Model; }
                    catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException || ex is System.Runtime.Serialization.SerializationException)
                    { _gpcError = ex.Message; return null; }
                }
            }
        }

        /// <summary>C3: the conversion of <see cref="Value"/> (never the analysed model), with its import report (warnings, resolved native
        /// sections), at the current model tolerance: the input the analysis and export components copy. Throws when it cannot be converted.</summary>
        public Common.Gpc.GpcModels.Conversion GpcInput { get { lock (gpcLock) return StateKey == null ? null : Conversion(); } }

        /// <summary>F6 chain D: the GPC input a model transformer (Attach Moving Paths, ...) built from the GPC input of its source, used in
        /// place of a conversion of <see cref="Value"/> while Value and the tolerance are those it was built for (the same model as the
        /// conversion of Value, checked bit for bit by the tests). Copies of this datum keep it.</summary>
        public void AttachInput(Common.Gpc.GpcModels.Conversion input)
        {
            lock (gpcLock) { _input = input ?? throw new ArgumentNullException(nameof(input)); _gpcOnly = input; _inputOf = StateKey; }
        }

        /// <summary>True when <see cref="GpcInput"/> is the input attached by a model transformer, not a conversion of <see cref="Value"/>.</summary>
        public bool HasAttachedInput { get { lock (gpcLock) return _input != null && ReferenceEquals(_inputOf, StateKey) && (Value == null || _input.Tolerance == Settings.Instance.Tolerance); } }

        private Common.Gpc.GpcModels.Conversion _input;
        private object _inputOf;

        private Common.Gpc.GpcModels.Conversion Conversion()
        {
            // R6.5 P0: a model without FEM value is its GPC input (made at its own tolerance; a new tolerance is a new input from its producer).
            // P2CE phase 2b: no FEM value to convert (removed with the FEM types; a model has no FEM value since P2K/P2D).
            return _gpcOnly ?? throw new InvalidOperationException("GpcModelWithoutInput");
        }

        // R6.1 (decision 1 of 6/11), P2D: the state of a model read from a former payload whose model carried results (not read: LegacyNotice).
        private object _legacyResultsOf;
        // P2D: never set by the product (a former payload is read by value when the file is read); kept only because a test of P2-prove sets
        // them by reflection (GpcOnlyReplay, the notice of a former payload): to delete with that test (P2-taglio).
        private byte[] _legacy;
        private object _legacyOf;

        // P2D: the FEM chunk of a development file (FemmR6 beside the datum, written until P2), read by value on first use: the drafts of its
        // elements (when the file has no drafts chunk) and the GH GUIDs saved in the order of its FEM elements, for the state it was read with.
        private Lazy<(Common.Gpc.GpcModelDrafts Drafts, Guid[] Guids)> _femm;
        private object _femmOf;

        /// <summary>P2D: the drafts and GUIDs of the FEM chunk of a development file (<see cref="_femm"/>), once, where none is attached.</summary>
        private void ResolveFemm()
        {
            if (_femm == null || !ReferenceEquals(_femmOf, StateKey)) return;
            var femm = _femm; _femm = null;
            try
            {
                var (drafts, guids) = femm.Value;
                if (drafts != null && (_drafts == null || !ReferenceEquals(_draftsOf, StateKey))) { _drafts = drafts; _draftsOf = StateKey; }
                var carried = _drafts != null && ReferenceEquals(_draftsOf, StateKey) ? _drafts : null;
                if (guids != null && carried != null && guids.Length == carried.Nodes.Count + carried.Beams.Count + carried.Plates.Count
                    && (_draftGuids == null || !ReferenceEquals(_draftGuidsOf, StateKey))) { _draftGuids = guids; _draftGuidsOf = StateKey; }
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException || ex is System.Runtime.Serialization.SerializationException)
            { _gpcError = ex.Message; }
        }

        // R6.4 (2), extension (α): the drafts of the elements attached apart from the GPC input (an analysis: those of its input; a file: the
        // drafts chunk), while the FEM value they were attached for is current.
        private Common.Gpc.GpcModelDrafts _drafts;
        private object _draftsOf;

        /// <summary>
        /// R6.4 (2), extension (α) of §1bis (coordinator, 7/11): the drafts of the elements of this model, in the order of the elements of
        /// <see cref="Value"/> (<see cref="Common.Gpc.GpcModelDrafts"/>): those of the GPC input a producer attached (Build Model, Attach Moving
        /// Paths), those attached apart (<see cref="AttachDrafts"/>: an analysis, a file), those of a former payload read by value; null when
        /// none is carried (the conversion of the FEM value: its elements keep the conversion of their FEM values until R6.5). Never converts
        /// the FEM value.
        /// </summary>
        public Common.Gpc.GpcModelDrafts GpcDrafts
        {
            get
            {
                lock (gpcLock)
                {
                    if (StateKey == null) return null;
                    // A former payload: the drafts of its input read by value (lazy, null when the reader refuses one of its elements).
                    if (_input != null && ReferenceEquals(_inputOf, StateKey) && (Value == null || _input.Tolerance == Settings.Instance.Tolerance) && _input.Drafts != null) return _input.Drafts;
                    ResolveFemm(); // P2D: a development file without its drafts chunk: the drafts of its FEM chunk by value
                    if (_drafts != null && ReferenceEquals(_draftsOf, StateKey)) return _drafts;
                    return null;
                }
            }
        }

        /// <summary>R6.4 (2): the drafts of the elements of the current <see cref="Value"/> (an analysis: the drafts of its input, whose elements
        /// its output keeps; a file: the drafts it saved). Copies of this datum keep them. P2D: they supersede the FEM chunk of a development file.</summary>
        public void AttachDrafts(Common.Gpc.GpcModelDrafts drafts)
        {
            lock (gpcLock) { _drafts = drafts ?? throw new ArgumentNullException(nameof(drafts)); _draftsOf = StateKey; _femm = null; }
        }

        /// <summary>R6.1 (decision 1 of 6/11): the warning of a model read from a former payload that carried results (they are not read:
        /// <see cref="Common.Gpc.GpcLegacyPayloads.ResultsDroppedMessage"/>), or null. The components that read this model show it.</summary>
        public string LegacyNotice
        {
            get
            {
                lock (gpcLock)
                {
                    // P30-01 (C1, 11/11): the results saved with the model that could not be read (the readers of this notice show it).
                    if (_unreadableResultsOf != null && ReferenceEquals(_unreadableResultsOf, StateKey)) return UnreadableResultsMessage;
                    // P2D: whether the former payload carried results is read with it (no FEM value).
                    if (_legacyResultsOf != null && ReferenceEquals(_legacyResultsOf, StateKey)) return Common.Gpc.GpcLegacyPayloads.ResultsDroppedMessage;
                    return _legacy != null && ReferenceEquals(_legacyOf, StateKey) && Common.Gpc.GpcLegacyPayloads.ModelHadResults(_legacy) ? Common.Gpc.GpcLegacyPayloads.ResultsDroppedMessage : null;
                }
            }
        }

        /// <summary>P30-01 (C1, 11/11): the state of a model read with a results chunk that could not be read (dropped, not attached).</summary>
        private object _unreadableResultsOf;

        /// <summary>P30-01 (C1, 11/11): the warning of a model whose saved results could not be read (they were dropped; Run is needed),
        /// or null. <see cref="LegacyNotice"/> gives it too, so the analysis components and the readers of the results show it.</summary>
        public string ResultsNotice { get { lock (gpcLock) return _unreadableResultsOf != null && ReferenceEquals(_unreadableResultsOf, StateKey) ? UnreadableResultsMessage : null; } }

        /// <summary>P30-01 (C1, 11/11): the text of <see cref="ResultsNotice"/>.</summary>
        public const string UnreadableResultsMessage = "I risultati salvati con il modello non si possono leggere (dati danneggiati) e non sono stati caricati: premere Run per ricalcolare l'analisi.";

        /// <summary>F6 (L12): the bridge data of <see cref="Gpc"/> (members, super elements, local section states by beam GUID): those of the
        /// GPC input of <see cref="Value"/>; for an attached analysed (or bridge) model, the same data on its beams by number (the analysed
        /// model of an analysis has the numbering of the input it copied, but the GUIDs of the input of the analysis, not those of the
        /// conversion of this FEM output: found by the native 50, s60). Null when it cannot be converted.</summary>
        public GPC.Checkers.CompositeBridge.BridgeModelData GpcBridgeData
        {
            get
            {
                lock (gpcLock)
                {
                    if (StateKey == null) return null;
                    Common.Gpc.GpcModels.Conversion conversion;
                    try { conversion = Conversion(); }
                    catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException || ex is System.Runtime.Serialization.SerializationException)
                    { _gpcError = ex.Message; return null; }
                    if (_analysed == null || !ReferenceEquals(_analysedOf, StateKey) || ReferenceEquals(_analysed, conversion.Model)) return conversion.Bridge;
                    if (_analysedBridge != null && ReferenceEquals(_analysedBridgeOf, _analysed) && ReferenceEquals(_analysedBridgeFrom, conversion)) return _analysedBridge;
                    // The map by number must be one to one (coordinator, 5/11): a partial map would lose bridge data silently (no section state).
                    var from = conversion.Model.BeamElements; var to = _analysed.BeamElements;
                    int missing = from.Keys.Where(k => !to.ContainsKey(k)).Concat(to.Keys.Where(k => !from.ContainsKey(k))).DefaultIfEmpty(0).Min();
                    if (from.Count != to.Count || missing != 0)
                        throw new InvalidOperationException($"GpcBridgeDataNumberingMismatch: the analysed GPC model has {to.Count} beams, the GPC input of the FEM model {from.Count}"
                            + (missing != 0 ? $"; first beam number on one side only: {missing}" : "") + ". Recalculate the analysis.");
                    var data = new GPC.Checkers.CompositeBridge.BridgeModelData { Workflow = conversion.Bridge.Workflow };
                    foreach (var beam in from.Values)
                    {
                        var d = conversion.Bridge.Beam(beam.Guid);
                        if (d != null) data.Set(new GPC.Checkers.CompositeBridge.BridgeBeamData(to[beam.Id].Guid, d.Member, d.SuperElement, d.SectionState, d.CompositeSection));
                    }
                    _analysedBridge = data; _analysedBridgeOf = _analysed; _analysedBridgeFrom = conversion;
                    return data;
                }
            }
        }
        private GPC.Checkers.CompositeBridge.BridgeModelData _analysedBridge;
        private GPC.Model.Models.Model _analysedBridgeOf;
        private Common.Gpc.GpcModels.Conversion _analysedBridgeFrom;

        /// <summary>Why <see cref="Gpc"/> is null.</summary>
        public string GpcError { get { lock (gpcLock) return _gpcError; } }

        /// <summary>True when <see cref="Gpc"/> is the analysed model attached by an analysis, not a conversion of <see cref="Value"/>.</summary>
        public bool HasAnalysedGpc { get { lock (gpcLock) return _analysed != null && ReferenceEquals(_analysedOf, StateKey); } }

        /// <summary>F6 (a)/(b): the analysed GPC model with the results catalog of a BridGHe analysis (what the result consumers read), or
        /// null: not analysed, analysed before F6 C3, or the bridge model of Build Bridge Model (its merged actions have no catalog).</summary>
        public GPC.Model.Models.Model GpcResults
        {
            get
            {
                lock (gpcLock)
                    return _analysed != null && ReferenceEquals(_analysedOf, StateKey) && Common.Gpc.GpcResultCatalog.Of(_analysed) != null ? _analysed : null;
            }
        }

        /// <summary>An analysis component: the analysed GPC model of <see cref="Value"/> (with its results).</summary>
        public void AttachGpc(GPC.Model.Models.Model analysed)
        {
            lock (gpcLock) { _analysed = analysed ?? throw new ArgumentNullException(nameof(analysed)); _analysedOf = StateKey; _bridge = null; _gpcError = null; }
        }

        /// <summary>C5, Build Bridge Model: the GPC bridge model of <see cref="Value"/> (merged actions as a GPC dataset) with its GPC recipe.</summary>
        public void AttachBridge(Common.Gpc.GpcBridge bridge)
        {
            lock (gpcLock) { _analysed = (bridge ?? throw new ArgumentNullException(nameof(bridge))).Model; _analysedOf = StateKey; _bridge = bridge; _gpcError = null; }
        }

        /// <summary>
        /// R6.4 (5), decision (i) = A, examples 11–19 (coordinator, 7/11): the analysed GPC model (with the results catalog of a BridGHe analysis)
        /// that every beam of this model comes from, when Build Model built it from beams linked to one analysed model (Super Element Filter or
        /// Decompose Model on the output of an analysis) and nothing else; null otherwise. Its numbering is that of the analysis, not of
        /// <see cref="Value"/>: Build BridGHe Model reads the actions of such a model from it by BridGHe ID (the same beams, the same results).
        /// Never <see cref="GpcResults"/> (whose numbering is that of this model). Not persisted; copies of this datum keep it.
        /// </summary>
        public GPC.Model.Models.Model LinkedGpcResults { get { lock (gpcLock) return _linked != null && ReferenceEquals(_linkedOf, StateKey) ? _linked : null; } }

        /// <summary>Build Model: the analysed GPC model every beam of <see cref="Value"/> comes from (<see cref="LinkedGpcResults"/>).</summary>
        public void AttachLinkedResults(GPC.Model.Models.Model analysed)
        {
            if (Common.Gpc.GpcResultCatalog.Of(analysed ?? throw new ArgumentNullException(nameof(analysed))) == null) throw new ArgumentException("GpcLinkedModelWithoutResults: " + analysed.Name);
            lock (gpcLock) { _linked = analysed; _linkedOf = StateKey; }
        }
        private GPC.Model.Models.Model _linked;
        private object _linkedOf;

        /// <summary>The analysed GPC model the actions of this model are read from: <see cref="GpcResults"/>, else <see cref="LinkedGpcResults"/>.</summary>
        public GPC.Model.Models.Model GpcActionResults => GpcResults ?? LinkedGpcResults;

        /// <summary>The GPC bridge model attached by Build Bridge Model for <see cref="Value"/>, or null.</summary>
        public Common.Gpc.GpcBridge GpcBridge { get { lock (gpcLock) return _bridge != null && ReferenceEquals(_analysedOf, StateKey) && ReferenceEquals(_analysed, _bridge.Model) ? _bridge : null; } }

        // R6.5 P1 lane 3c: the GH GUIDs of the elements of a model without FEM value (the FEM-backed path has _guids), in the order of its drafts.
        private Guid[] _draftGuids;
        private object _draftGuidsOf;

        /// <summary>R6.5 P1 lane 3c: the GH GUIDs of the elements of a model without FEM value, in the order of its drafts (nodes, beams, plates):
        /// those Build Model was given. Copies of this datum keep them; saved with the model (item <c>ElementGuids</c>).</summary>
        public void AttachElementGuids(IEnumerable<Guid> guids)
        {
            lock (gpcLock) { _draftGuids = (guids ?? throw new ArgumentNullException(nameof(guids))).ToArray(); _draftGuidsOf = StateKey; }
        }

        /// <summary>R6.5 P1 lane 3c: the GH GUID of every element of a model without FEM value, in the order of <paramref name="drafts"/>: the GUIDs
        /// attached (<see cref="AttachElementGuids"/>), otherwise (none attached, or for other drafts) the GUIDs of the drafts.</summary>
        public Guid[] ElementGuidsOf(Common.Gpc.GpcModelDrafts drafts)
        {
            int count = drafts.Nodes.Count + drafts.Beams.Count + drafts.Plates.Count;
            lock (gpcLock)
            {
                ResolveFemm(); // P2D: the GUIDs saved with the FEM chunk of a development file
                if (_draftGuids != null && ReferenceEquals(_draftGuidsOf, StateKey) && _draftGuids.Length == count) return _draftGuids.ToArray();
            }
            return drafts.Nodes.Select(d => d.Node.Guid).Concat(drafts.Beams.Select(d => d.Beam.Guid)).Concat(drafts.Plates.Select(d => d.Plate.Guid)).ToArray();
        }

        private void ShareGpc(GH_Model copy)
        {
            lock (gpcLock)
            {
                // R6.5 P0: a copy without FEM value owns the same GPC input first.
                if (Value == null && copy.Value == null && _gpcOnly != null) lock (copy.gpcLock) copy._gpcOnly = _gpcOnly;
                if (_analysed != null && ReferenceEquals(_analysedOf, StateKey)) { if (_bridge != null && ReferenceEquals(_analysed, _bridge.Model)) copy.AttachBridge(_bridge); else copy.AttachGpc(_analysed); }
                if (_input != null && ReferenceEquals(_inputOf, StateKey)) copy.AttachInput(_input);
                if (_legacyResultsOf != null && ReferenceEquals(_legacyResultsOf, StateKey)) lock (copy.gpcLock) copy._legacyResultsOf = copy.StateKey;
                if (_drafts != null && ReferenceEquals(_draftsOf, StateKey)) copy.AttachDrafts(_drafts);
                if (_femm != null && ReferenceEquals(_femmOf, StateKey)) lock (copy.gpcLock) { copy._femm = _femm; copy._femmOf = copy.StateKey; } // P2D
                if (_linked != null && ReferenceEquals(_linkedOf, StateKey)) lock (copy.gpcLock) { copy._linked = _linked; copy._linkedOf = copy.StateKey; }
                if (_draftGuids != null && ReferenceEquals(_draftGuidsOf, StateKey)) copy.AttachElementGuids(_draftGuids);
                if (_unreadableResultsOf != null && ReferenceEquals(_unreadableResultsOf, StateKey)) lock (copy.gpcLock) copy._unreadableResultsOf = copy.StateKey; // P30-01
            }
        }

        /// <summary>R6.5 P0 (coordinator, 7/11): a model given by its GPC input alone (no FEM value): <see cref="GpcInput"/> is the input, the
        /// state (analysed model, drafts, linked results) belongs to it, and validity, preview, bake, copies, persistence and text read it.</summary>
        public static GH_Model FromGpc(Common.Gpc.GpcModels.Conversion input)
        {
            var model = new GH_Model();
            model.AttachInput(input ?? throw new ArgumentNullException(nameof(input)));
            return model;
        }





        public override bool IsValid => (_gpcOnly?.Model.AllElements.Count() ?? 0) > 0;

        public override string TypeName => "FEM Model";

        public override string TypeDescription => "The FEM model definition";

        public GH_Model()
        {
        }



        public override IGH_Goo Duplicate()
        {
            var copy = new GH_Model();
            ShareGpc(copy);
            return copy;
        }


        public override bool Write(GH_IWriter writer) => Write(writer, true, true);

        /// <summary>The datum with or without the drafts of its elements: the analysis components leave them out of the model they keep
        /// (option 1 of P2Q, coordinator 7/11: <see cref="Helpers.AnalysisDrafts"/> attaches those of their input again after opening), and
        /// without its results (they keep them in the results file next to the .st7).</summary>
        public bool Write(GH_IWriter writer, bool drafts) => Write(writer, drafts, false);

        /// <summary>R6.4 (coordinator, 7/11): <paramref name="results"/> writes the analysed GPC model with its results (<see cref="GpcResults"/>)
        /// in the item <see cref="Common.Gpc.GpcResultsFile.Chunk"/>: a model internalised in a parameter (a model analysed, or with synthetic
        /// results, in 0.4.0 form) keeps them; read back, they are attached again. Never the bridge model of Build BridGHe Model (no catalog).</summary>
        public bool Write(GH_IWriter writer, bool drafts, bool results)
        {
            try
            {
                // R6.1: the GPC input with its report (BGHGPC1, GpcDraftArchive.Write(Conversion)); the analysed results stay in the sidecar of
                // the analysis components. P2D: never the FEM model (the transitional chunk FemmR6 is no longer written): read back, the model is
                // its GPC input alone.
                DataTypes.GpcGooPersistence.Write(writer, Common.Gpc.GpcDraftArchive.Write(GpcInput));
                // R6.4 (2), extension (α): the drafts of the elements, in a chunk of their own beside the GPC input (none when the model carries
                // none). A FEM value whose drafts cannot be written leaves them out (the conversion of a component not yet on GPC).
                byte[] draftBytes = null; Common.Gpc.GpcModelDrafts carried = null;
                if (drafts)
                    try { carried = GpcDrafts; if (carried != null) draftBytes = Common.Gpc.GpcModelDrafts.Write(carried); }
                catch (Exception ex) when (Value != null && (ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException || ex is System.Runtime.Serialization.SerializationException)) { }
                // R6.5 P1 lane 3c: the GH GUIDs of the elements in the order of the drafts written, read back below. P30-01 (C1, 11/11): only with
                // the drafts (GUIDs without drafts read as damage): the analysis components, which leave the drafts out, attach both again from
                // their live input after opening (AnalysisDrafts). P2D: a FEM value writes the GUIDs of its nodes, beams and plates in the order
                // of the drafts (they are read back by the drafts, no FEM value is read), none when they do not match the drafts.
                var elementGuids = Array.Empty<Guid>();
                if (draftBytes != null && Value == null && _draftGuids != null && ReferenceEquals(_draftGuidsOf, StateKey)) elementGuids = _draftGuids;
                writer.SetString("ElementGuids", JsonSerializer.Serialize(elementGuids));
                if (draftBytes != null) writer.SetByteArray(Common.Gpc.GpcModelDrafts.Chunk, draftBytes);
                if (results && GpcResults is GPC.Model.Models.Model analysed) writer.SetByteArray(Common.Gpc.GpcResultsFile.Chunk, Common.Gpc.GpcResultsFile.ToBytes(analysed));
                return true;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                // P2D: the model is its GPC input, never a FEM value: the datum (BGHGPC1), or a former payload / the FEM chunk alone of a
                // development file read by value (the model without its results, at the tolerance of the document when the file is read).
                Value = null; byte[] development = null; string legacyKinds = null;
                switch (DataTypes.GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case DataTypes.GpcGooPersistence.Kind.Gpc:
                        AttachInput(Common.Gpc.GpcDraftArchive.ReadConversion(data, records));
                        development = femm; // the FEM chunk of a development file beside the datum: the drafts and GUIDs of its elements
                        break;
                    case DataTypes.GpcGooPersistence.Kind.Legacy:
                    {
                        var input = Common.Gpc.GpcLegacyPayloads.Model(femm, Settings.Instance.Tolerance, out bool hadResults, out legacyKinds);
                        if (input == null) return true; // 0.3.0 reads no model from it (null value or no element list): an empty model, as before
                        AttachInput(input);
                        if (hadResults) lock (gpcLock) _legacyResultsOf = StateKey;
                        break;
                    }
                    default: return false;
                }
                Guid[] savedDraftGuids = null, femGuids = null;
                if (reader.ItemExists("ElementGuids"))
                {
                    var guids = JsonSerializer.Deserialize<Guid[]>(reader.GetString("ElementGuids"));
                    if (guids == null) return false;
                    // P2D: GUIDs saved with a FEM value are in the order of its elements: by the kinds of the former elements in the order of
                    // the drafts (nodes, beams, plates); not kept when they do not match.
                    if (legacyKinds != null) { if (Common.Gpc.GpcLegacyPayloads.InDraftOrder(guids, legacyKinds) is { } ordered) AttachElementGuids(ordered); }
                    else if (development != null) femGuids = guids;
                    else if (guids.Length != 0) savedDraftGuids = guids; // R6.5 P1 lane 3c: attached once the drafts are read
                }
                // R6.4 (coordinator, 7/11): an internalised model with its results (0.4.0 form). P30-01 (C1, 11/11): results that cannot be read
                // are dropped visibly: the model opens without them and says so (ResultsNotice, shown by the components that read the model).
                if (reader.ItemExists(Common.Gpc.GpcResultsFile.Chunk) && StateKey != null)
                    try { AttachGpc(Common.Gpc.GpcResultsFile.FromBytes(reader.GetByteArray(Common.Gpc.GpcResultsFile.Chunk))); }
                    catch (Exception ex) when (DataTypes.GpcGooPersistence.IsRefusal(ex) || ex is System.IO.IOException) // P2D: a defect is not a damaged datum
                    { System.Diagnostics.Debug.WriteLine(ex); lock (gpcLock) _unreadableResultsOf = StateKey; }
                // R6.4 (2): the drafts of the elements saved with the model (BGHGPC1 of the GH_Model, extended; absent in former files, allowed).
                // P30-01 (C1, 11/11): drafts saved but unreadable are a damaged datum: the model does not open as if complete (before, they were
                // left out and the model opened without them).
                if (reader.ItemExists(Common.Gpc.GpcModelDrafts.Chunk) && StateKey != null)
                    try { AttachDrafts(Common.Gpc.GpcModelDrafts.Read(reader.GetByteArray(Common.Gpc.GpcModelDrafts.Chunk))); }
                    catch (Exception ex) when (DataTypes.GpcGooPersistence.IsRefusal(ex)) { return DataTypes.GpcGooPersistence.Refused(reader, TypeName, ex); }
                // P2D: the FEM chunk of a development file, read by value on first use (the drafts of its elements when no drafts chunk was
                // saved, with the identities of the owner of the GPC input; the GUIDs saved with its FEM elements in the order of the drafts).
                if (development != null && StateKey != null)
                {
                    var input = GpcInput; bool chunk = reader.ItemExists(Common.Gpc.GpcModelDrafts.Chunk);
                    var owner = input.Report?.Owner is Guid o && o != Guid.Empty ? o : Common.Gpc.GpcLegacyPayloads.ModelOwner(development, input.Tolerance);
                    var guids = femGuids;
                    lock (gpcLock)
                    {
                        _femm = new Lazy<(Common.Gpc.GpcModelDrafts, Guid[])>(() => Common.Gpc.GpcLegacyPayloads.Development(development, owner, guids, !chunk));
                        _femmOf = StateKey;
                    }
                }
                // R6.5 P1 lane 3c: the GUIDs of a model without FEM value belong to its drafts: another count is a damaged datum (as for the FEM elements).
                // P30-01 (C1, 11/11): so are GUIDs saved without drafts (before, the check was skipped when the drafts were missing).
                if (savedDraftGuids != null)
                {
                    if (!(GpcDrafts is Common.Gpc.GpcModelDrafts read) || savedDraftGuids.Length != read.Nodes.Count + read.Beams.Count + read.Plates.Count) return false;
                    AttachElementGuids(savedDraftGuids);
                }
                return true;
            }
            catch (Exception ex) { return DataTypes.GpcGooPersistence.Refused(reader, TypeName, ex); }
        }

        public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
        {
            obj_guid = Guid.Empty;
            foreach (var e in DraftElements()) ((IGH_BakeAwareData)e).BakeGeometry(doc, att, out obj_guid);
            return obj_guid != Guid.Empty;
        }

        // R6.5 P0: the text of a model without FEM value (the layout of the FEM text, with what the GPC input has).
        private static string GpcText(Common.Gpc.GpcModels.Conversion input)
        {
            var m = input.Model;
            if (!m.AllElements.Any()) return "Empty Model";
            string text = $"BridGHe Model {m.Name}";
            text += $"\nNodes: {m.NodesElements.Count}, beams: {m.BeamElements.Count}, plates: {m.AreaElements.Count}";
            text += $"\nLoad cases: {m.LoadCases.Count}, combinations: {m.Combinations.Count}";
            text += "\nGPC datum (no FEM value)";
            return text;
        }

        public override string ToString()
        {
            return _gpcOnly == null ? "Null Model" : GpcText(_gpcOnly);
        }
    }
}
