using BridGHe.Common.Models;
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
    public class GH_Model : GH_Goo<ModelModel>, IGH_BakeAwareData, IGH_PreviewData
    {
        protected Dictionary<ElementModel, Guid> _guids;
        private readonly ConditionalWeakTable<ElementModel, IGH_PreviewData> previews = new();
        private readonly ConditionalWeakTable<Common.MovingLoads.MovingPath, MovingGoo> movingPreviews = new();

        private IEnumerable<IGH_PreviewData> PreviewElements()
        {
            if (Value == null) yield break;
            foreach (var element in Value.Elements)
            {
                if (element is not NodeModel && element is not BeamModel && element is not PlateModel && element is not CompositeBeamModel) continue;
                yield return previews.GetValue(element, e => e switch
                {
                    NodeModel node => new GH_Node(node),
                    BeamModel beam => new GH_Beam(beam),
                    PlateModel plate => new GH_Plate(plate),
                    CompositeBeamModel beam => new GH_CompositeBeam(beam),
                    _ => throw new NotSupportedException()
                });
            }
            foreach(var path in Value.MovingPaths)
                if(path!=null)yield return movingPreviews.GetValue(path,p=>new MovingGoo(p));
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

        public Dictionary<ElementModel, Guid> Guids => _guids;

        public override ModelModel Value
        {
            get => base.Value;
            set { base.Value = value; SynchronizeGuids(); }
        }

        // F6 phase (c), C2: the GPC model of this FEM model, converted once (GpcModels.Convert, the legacy reader) and kept with it, or the
        // analysed GPC model an analysis component attached (results as GPC datasets). It follows Value: a new FEM model gives a new
        // conversion. Consumers never change it in place (an analysis copies it first); copies of this datum share the analysed model.
        // C3: the analysis and export components take a copy of the conversion (GpcInput) instead of converting the FEM model again.
        private readonly object gpcLock = new object();
        private Common.Gpc.GpcModels.Conversion _converted;
        private ModelModel _convertedOf;
        private GPC.Model.Models.Model _analysed;
        private ModelModel _analysedOf;
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
                    if (Value == null) return null;
                    if (_analysed != null && ReferenceEquals(_analysedOf, Value)) return _analysed;
                    try { return Conversion().Model; }
                    catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException || ex is System.Runtime.Serialization.SerializationException)
                    { _gpcError = ex.Message; return null; }
                }
            }
        }

        /// <summary>C3: the conversion of <see cref="Value"/> (never the analysed model), with its import report (warnings, resolved native
        /// sections), at the current model tolerance: the input the analysis and export components copy. Throws when it cannot be converted.</summary>
        public Common.Gpc.GpcModels.Conversion GpcInput { get { lock (gpcLock) return Value == null ? null : Conversion(); } }

        /// <summary>F6 chain D: the GPC input a model transformer (Attach Moving Paths, ...) built from the GPC input of its source, used in
        /// place of a conversion of <see cref="Value"/> while Value and the tolerance are those it was built for (the same model as the
        /// conversion of Value, checked bit for bit by the tests). Copies of this datum keep it.</summary>
        public void AttachInput(Common.Gpc.GpcModels.Conversion input)
        {
            lock (gpcLock) { _input = input ?? throw new ArgumentNullException(nameof(input)); _inputOf = Value; }
        }

        /// <summary>True when <see cref="GpcInput"/> is the input attached by a model transformer, not a conversion of <see cref="Value"/>.</summary>
        public bool HasAttachedInput { get { lock (gpcLock) return _input != null && ReferenceEquals(_inputOf, Value) && _input.Tolerance == Settings.Instance.Tolerance; } }

        private Common.Gpc.GpcModels.Conversion _input;
        private ModelModel _inputOf;

        private Common.Gpc.GpcModels.Conversion Conversion()
        {
            double tolerance = Settings.Instance.Tolerance;
            if (_input != null && ReferenceEquals(_inputOf, Value) && _input.Tolerance == tolerance) return _input;
            if (_converted != null && ReferenceEquals(_convertedOf, Value) && _converted.Tolerance == tolerance) return _converted;
            _converted = null; _convertedOf = Value; _gpcError = null;
            // R6.1 (decision 1 of 6/11): a model read from a former payload (internalised, or the cache of an analysis component) is converted
            // by value, without its results, whether it was analysed or not; the conversion of the FEM value otherwise (until R6.5).
            _converted = _legacy != null && ReferenceEquals(_legacyOf, Value)
                ? Common.Gpc.GpcLegacyPayloads.Model(_legacy, tolerance, out _) ?? throw new InvalidOperationException("GpcLegacyModelWithoutModel")
                : Common.Gpc.GpcModels.Convert(Value, tolerance);
            return _converted;
        }

        // R6.1: the former payload (BGHA1) this model was read from, while the FEM value read with it is current.
        private byte[] _legacy;
        private ModelModel _legacyOf;

        /// <summary>R6.1 (decision 1 of 6/11): the warning of a model read from a former payload that carried results (they are not read:
        /// <see cref="Common.Gpc.GpcLegacyPayloads.ResultsDroppedMessage"/>), or null. The components that read this model show it.</summary>
        public string LegacyNotice
        {
            get
            {
                lock (gpcLock)
                    return _legacy != null && Value != null && ReferenceEquals(_legacyOf, Value) && Value.Elements.Any(e => e.Results != null && e.Results.Count != 0)
                        ? Common.Gpc.GpcLegacyPayloads.ResultsDroppedMessage : null;
            }
        }

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
                    if (Value == null) return null;
                    Common.Gpc.GpcModels.Conversion conversion;
                    try { conversion = Conversion(); }
                    catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException || ex is System.Runtime.Serialization.SerializationException)
                    { _gpcError = ex.Message; return null; }
                    if (_analysed == null || !ReferenceEquals(_analysedOf, Value) || ReferenceEquals(_analysed, conversion.Model)) return conversion.Bridge;
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
        public bool HasAnalysedGpc { get { lock (gpcLock) return _analysed != null && ReferenceEquals(_analysedOf, Value); } }

        /// <summary>F6 (a)/(b): the analysed GPC model with the results catalog of a BridGHe analysis (what the result consumers read), or
        /// null: not analysed, analysed before F6 C3, or the bridge model of Build Bridge Model (its merged actions have no catalog).</summary>
        public GPC.Model.Models.Model GpcResults
        {
            get
            {
                lock (gpcLock)
                    return _analysed != null && ReferenceEquals(_analysedOf, Value) && Common.Gpc.GpcResultCatalog.Of(_analysed) != null ? _analysed : null;
            }
        }

        /// <summary>An analysis component: the analysed GPC model of <see cref="Value"/> (with its results).</summary>
        public void AttachGpc(GPC.Model.Models.Model analysed)
        {
            lock (gpcLock) { _analysed = analysed ?? throw new ArgumentNullException(nameof(analysed)); _analysedOf = Value; _bridge = null; _gpcError = null; }
        }

        /// <summary>C5, Build Bridge Model: the GPC bridge model of <see cref="Value"/> (merged actions as a GPC dataset) with its GPC recipe.</summary>
        public void AttachBridge(Common.Gpc.GpcBridge bridge)
        {
            lock (gpcLock) { _analysed = (bridge ?? throw new ArgumentNullException(nameof(bridge))).Model; _analysedOf = Value; _bridge = bridge; _gpcError = null; }
        }

        /// <summary>The GPC bridge model attached by Build Bridge Model for <see cref="Value"/>, or null.</summary>
        public Common.Gpc.GpcBridge GpcBridge { get { lock (gpcLock) return _bridge != null && ReferenceEquals(_analysedOf, Value) && ReferenceEquals(_analysed, _bridge.Model) ? _bridge : null; } }

        private void ShareGpc(GH_Model copy)
        {
            lock (gpcLock)
            {
                if (_analysed != null && ReferenceEquals(_analysedOf, Value)) { if (_bridge != null && ReferenceEquals(_analysed, _bridge.Model)) copy.AttachBridge(_bridge); else copy.AttachGpc(_analysed); }
                if (_input != null && ReferenceEquals(_inputOf, Value)) copy.AttachInput(_input);
                if (_legacy != null && ReferenceEquals(_legacyOf, Value)) lock (copy.gpcLock) { copy._legacy = _legacy; copy._legacyOf = copy.Value; }
            }
        }

        public static GH_Model FromModel(ModelModel value, GH_Model source = null)
        {
            var result = new GH_Model { Value = value };
            result.SynchronizeGuids(source);
            return result;
        }

        private static string Identity(ElementModel element) => element switch
        {
            NodeModel node => "N:" + node.NodeId,
            BeamModel beam => "B:" + beam.BeamId,
            PlateModel plate => "P:" + plate.PlateId,
            _ => null
        };

        public Guid GetGuid(ElementModel element)
        {
            if (!_guids.TryGetValue(element, out Guid guid)) _guids[element] = guid = Guid.NewGuid();
            return guid;
        }

        public void SynchronizeGuids(GH_Model source = null)
        {
            _guids ??= new Dictionary<ElementModel, Guid>();
            var previous = _guids;
            var identities = new Dictionary<string, Guid>(StringComparer.Ordinal);
            if (source?.Value != null)
                foreach (var element in source.Value.Elements)
                {
                    string key = Identity(element);
                    if (key != null && key.Length > 2) identities.TryAdd(key, source.GetGuid(element));
                }
            _guids = new Dictionary<ElementModel, Guid>();
            if (Value == null) return;
            for (int i = 0; i < Value.Elements.Count; i++)
            {
                var element = Value.Elements[i];
                string key = Identity(element);
                Guid guid;
                if (key != null && identities.TryGetValue(key, out guid)) { }
                else if (source?.Value != null && key?.Length == 2 && i < source.Value.Elements.Count && Identity(source.Value.Elements[i]) == key)
                    guid = source.GetGuid(source.Value.Elements[i]);
                else if (!previous.TryGetValue(element, out guid)) guid = Guid.NewGuid();
                _guids[element] = guid;
            }
        }

        public override bool IsValid => Value != null && Value.Elements != null && Value.Elements.Count() > 0;

        public override string TypeName => "FEM Model";

        public override string TypeDescription => "The FEM model definition";

        public GH_Model()
        {
            _guids = new Dictionary<ElementModel, Guid>();
        }

        public GH_Model(ModelModel.ModelUnits units, IEnumerable<IGH_GeometricGoo> elements, IEnumerable<GH_LoadCombination> combinations,
            IEnumerable<GH_StagedConstruction> stages)
            : this()
        {
            ModelHelper.ParseElements(elements, out List<GH_Node> nodes, out List<GH_Beam> beams, out List<GH_Plate> plates);
            ModelHelper.BuildModel(ref nodes, ref beams, ref plates);

            Value = new ModelModel(units);

            Dictionary<ElementModel, Guid> bufferGuidDict = new Dictionary<ElementModel, Guid>();
            List<ElementModel> elementsBag = new List<ElementModel>();

            for (int i = 0; i < nodes.Count; i++)
            {
                GH_Node node = nodes[i];
                if (!bufferGuidDict.ContainsKey(node.Value))
                    bufferGuidDict.Add(node.Value, node.Guid);
                elementsBag.Add(node.Value);
            }
            for (int i = 0; i < beams.Count; i++)
            {
                GH_Beam beam = beams[i];
                if (!bufferGuidDict.ContainsKey(beam.Value))
                    bufferGuidDict.Add(beam.Value, beam.Guid);
                elementsBag.Add(beam.Value);
            }
            for (int i = 0; i < plates.Count; i++)
            {
                GH_Plate plate = plates[i];
                if (!bufferGuidDict.ContainsKey(plate.Value))
                    bufferGuidDict.Add(plate.Value, plate.Guid);
                elementsBag.Add(plate.Value);
            }

            _guids = new Dictionary<ElementModel, Guid>(bufferGuidDict);

            Value.AddElements(elementsBag);

            Value.AddLoadCombinations(combinations.Select(c => c.Value));


            Value.AddStagedConstructions(stages.Select(s => s.Value));
        }

        public GH_Model(GH_Model model) : this()
        {
            Value = Common.Helpers.ModelArchive.Clone(model.Value);
            SynchronizeGuids(model);
            model.ShareGpc(this);
        }

        public override IGH_Goo Duplicate()
        {
            var copy = FromModel(Value == null ? null : new ModelModel(Value), this);
            ShareGpc(copy);
            return copy;
        }

        public void AddGuid(ElementModel elementModel, Guid guid)
        {
            _guids[elementModel] = guid;
        }

        public override bool Write(GH_IWriter writer)
        {
            try
            {
                // R6.1: the GPC input with its report (BGHGPC1, GpcDraftArchive.Write(Conversion)); the analysed results stay in the sidecar of
                // the analysis components. Until R6.5 the FEM model too (transitional chunk) with the GUIDs of its elements.
                DataTypes.GpcGooPersistence.Write(writer, () => Common.Gpc.GpcDraftArchive.Write(GpcInput), Value == null ? null : Common.Helpers.ModelArchive.Write(Value));
                writer.SetString("ElementGuids", JsonSerializer.Serialize(Value?.Elements.Select(GetGuid).ToArray() ?? Array.Empty<Guid>()));
                return true;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                switch (DataTypes.GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case DataTypes.GpcGooPersistence.Kind.Gpc:
                    {
                        var input = Common.Gpc.GpcDraftArchive.ReadConversion(data, records);
                        Value = femm == null ? null : Common.Helpers.ModelArchive.Read<ModelModel>(femm);
                        AttachInput(input); break;
                    }
                    case DataTypes.GpcGooPersistence.Kind.Legacy:
                        Value = Common.Helpers.ModelArchive.Read<ModelModel>(femm);
                        lock (gpcLock) { _legacy = femm; _legacyOf = Value; }
                        break;
                    case DataTypes.GpcGooPersistence.Kind.FemmOnly:
                        Value = Common.Helpers.ModelArchive.Read<ModelModel>(femm); break;
                    default: return false;
                }
                if (reader.ItemExists("ElementGuids"))
                {
                    var guids = JsonSerializer.Deserialize<Guid[]>(reader.GetString("ElementGuids"));
                    if (guids == null || guids.Length != (Value?.Elements.Count ?? 0)) return false;
                    for (int i = 0; i < guids.Length; i++) _guids[Value.Elements[i]] = guids[i];
                }
                return true;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
        {
            obj_guid = Guid.Empty;
            foreach (var node in Value.Nodes) new GH_Node(node).BakeGeometry(doc, att, out obj_guid);
            foreach (var beam in Value.Beams) new GH_Beam(beam).BakeGeometry(doc, att, out obj_guid);
            foreach (var plate in Value.Plates) new GH_Plate(plate).BakeGeometry(doc, att, out obj_guid);
            return obj_guid != Guid.Empty;
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null Model";
            else if (Value.Elements.Count() == 0)
                return "Empty Model";
            else
                return Value.ToString();
        }
    }
}
