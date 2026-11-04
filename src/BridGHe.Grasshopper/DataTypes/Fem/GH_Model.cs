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

        // F6 phase (c), C2: the GPC model of this FEM model, converted once (GpcModels.FromFemm, the legacy reader) and kept with it, or the
        // analysed GPC model an analysis component attached (results as GPC datasets). It follows Value: a new FEM model gives a new
        // conversion. Consumers never change it in place (an analysis copies it first); copies of this datum share it.
        private readonly object gpcLock = new object();
        private GPC.Model.Models.Model _gpc;
        private ModelModel _gpcOf;
        private bool _gpcAttached;
        private string _gpcError;

        /// <summary>The GPC model (N-mm) of <see cref="Value"/>; null, with the reason in <see cref="GpcError"/>, when it cannot be converted.</summary>
        public GPC.Model.Models.Model Gpc
        {
            get
            {
                lock (gpcLock)
                {
                    if (Value == null) return null;
                    if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                    _gpcAttached = false; _gpcOf = Value;
                    try { _gpc = Common.Gpc.GpcModels.FromFemm(Value, Settings.Instance.Tolerance, out _); _gpcError = null; }
                    catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException || ex is System.Runtime.Serialization.SerializationException)
                    { _gpc = null; _gpcError = ex.Message; }
                    return _gpc;
                }
            }
        }

        /// <summary>Why <see cref="Gpc"/> is null.</summary>
        public string GpcError { get { lock (gpcLock) return _gpcError; } }

        /// <summary>True when <see cref="Gpc"/> is the analysed model attached by an analysis, not a conversion of <see cref="Value"/>.</summary>
        public bool HasAnalysedGpc { get { lock (gpcLock) return _gpcAttached && _gpc != null && ReferenceEquals(_gpcOf, Value); } }

        /// <summary>An analysis component: the analysed GPC model of <see cref="Value"/> (with its results).</summary>
        public void AttachGpc(GPC.Model.Models.Model analysed)
        {
            lock (gpcLock) { _gpc = analysed ?? throw new ArgumentNullException(nameof(analysed)); _gpcOf = Value; _gpcAttached = true; _gpcError = null; }
        }

        private void ShareGpc(GH_Model copy)
        {
            lock (gpcLock)
                if (_gpcAttached && _gpc != null && ReferenceEquals(_gpcOf, Value)) copy.AttachGpc(_gpc);
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
                writer.SetByteArray("Data", Common.Helpers.ModelArchive.Write(Value));
                writer.SetString("ElementGuids", JsonSerializer.Serialize(Value?.Elements.Select(GetGuid).ToArray() ?? Array.Empty<Guid>()));
                return true;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                Value = Common.Helpers.ModelArchive.Read<ModelModel>(reader.GetByteArray("Data"));
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
