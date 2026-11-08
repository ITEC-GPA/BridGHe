using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.IO;
using System.Linq;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    // https://api.ning.com/files/xorPYQRorQU*NhhPVfRpB6fFFUY*MNAdaUuNvl3280KJlbIveuzziw9C20corPfCIrwXFvI*F07PGI0*rGuZ0QJtB*T5m-Mp/Boat.cs

    public class GH_Node : GH_GeometricGoo<NodeModel>, IGH_BakeAwareData, IGH_PreviewData
    {
        protected Guid _guid;

        #region Properties

        public Guid Guid => _guid;

        // R6.5 P0: without a FEM value the node is its GPC draft.
        public override bool IsValid => Value == null ? Gpc?.Node != null : Value.IsValid;

        public override string IsValidWhyNot
        {
            get
            {
                if (Value == null)
                    return Gpc?.Node != null ? string.Empty : "No internal NodeModel instance";
                if (Value.IsValid)
                    return string.Empty;
                return "Invalid NodeModel instance";
            }
        }

        public override string TypeName => "Node";

        public override string TypeDescription => "Defines a single node";

        public override BoundingBox Boundingbox
        {
            get
            {
                if (Value == null)
                    return GpcDraftGeometry.Bounds(Gpc);
                if (Value.Position == Point3d.Unset)
                    return BoundingBox.Empty;
                return new BoundingBox(Value.Position, Value.Position);
            }
        }

        #endregion

        #region Constructors

        public GH_Node()
        {
            _guid = Guid.NewGuid();
        }

        public GH_Node(Point3d point)
            : this()
        {
            Value = new NodeModel(point);
        }

        public GH_Node(NodeModel node, Guid? guid = null)
            : this()
        {
            if (node == null)
                node = new NodeModel();
            Value = new NodeModel(node);
            if (guid.HasValue)
                _guid = guid.Value;
        }

        /// <summary>
        /// Copy constructor. Use to duplicate the object to add data through the components.
        /// NOTE: It keeps the Guid original value
        /// </summary>
        /// <param name="node">The node to duplicate</param>
        public GH_Node(GH_Node node) : this()
        {
            _guid = node.Guid;
            // R6.5 P0: a node without a FEM value is its draft: the copy keeps it (a component that changes the copy attaches its own).
            if (node.Value == null) { if (node.Gpc is { } draft) { _gpc = draft; _gpcOf = null; HasAttachedGpc = true; } }
            else Value = new NodeModel(node.Value);
            AnalysedGpc = node.AnalysedGpc; AnalysedGpcModel = node.AnalysedGpcModel;
        }

        /// <summary>R6.5 P0 (coordinator, 7/11): a node given by its GPC draft alone, with no FEM value: <see cref="Gpc"/> is the draft and
        /// every member of the type (validity, bounding box, preview, bake, cast, transforms, copies, persistence, text) reads it.</summary>
        public static GH_Node FromGpc(Common.Gpc.Legacy.LegacyNodeDraft draft, Guid? guid = null)
        {
            var node = new GH_Node { _gpc = draft ?? throw new System.ArgumentNullException(nameof(draft)), HasAttachedGpc = true };
            if (guid.HasValue) node._guid = guid.Value;
            return node;
        }

        /// <summary>R6.5 P1 lane 2a (rule of passo 75): the copy of this node (its GUID and analysed element) given by <paramref name="draft"/>
        /// alone, without the FEM value: the output of a component whose node has a FEM value but whose load case is given by its record alone.</summary>
        internal GH_Node WithDraftOnly(Common.Gpc.Legacy.LegacyNodeDraft draft)
        {
            var copy = FromGpc(draft, _guid);
            copy.AnalysedGpc = AnalysedGpc; copy.AnalysedGpcModel = AnalysedGpcModel;
            return copy;
        }

        /// <summary>F6 C4: the analysed GPC element of this element and its analysed model, set by Decompose Model when its model was
        /// analysed (Straus R3 / Moving Static Analysis); null otherwise. Immutable by convention, so copies share it.</summary>
        public GPC.Model.Elements.Element AnalysedGpc { get; set; }
        public GPC.Model.Models.Model AnalysedGpcModel { get; set; }

        /// <summary>
        /// Chain E (e1): the GPC draft of this node (definition units, see <see cref="Common.Gpc.Legacy.LegacyNodeDraft"/>): the one a GPC component
        /// attached to this FEM value (<see cref="AttachGpc"/>), otherwise the per-value conversion of the FEM value
        /// (<see cref="Common.Gpc.GpcBeamDrafts.FromFemm(NodeModel, System.Guid?)"/>), once per value. A new FEM value (a modified copy) drops the
        /// attached draft. Immutable by convention: whoever changes it makes a copy (<see cref="GPC.Model.Core.ModelValues.CopyValue{T}(T)"/>).
        /// </summary>
        public Common.Gpc.Legacy.LegacyNodeDraft Gpc
        {
            get
            {
                // R6.1: the datum attached or read (BGHGPC1), the former payload read by value (BGHA1), otherwise the conversion of the FEM value.
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { _gpc = Common.Gpc.GpcLegacyPayloads.Node(_legacy); _gpcOf = Value; HasAttachedGpc = false; return _gpc; }
                if (Value == null) return null;
                _gpc = Common.Gpc.GpcBeamDrafts.FromFemm(Value); _gpcOf = Value; HasAttachedGpc = false;
                return _gpc;
            }
        }

        /// <summary>True when <see cref="Gpc"/> is a draft attached by a GPC component (not the conversion of the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC draft built on GPC for the current FEM value (it must describe it: the gates compare the two).</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyNodeDraft draft)
        {
            _gpc = draft ?? throw new System.ArgumentNullException(nameof(draft)); _gpcOf = Value; HasAttachedGpc = true;
            // R6.1 (frozen API for R6.2): the gates compare the attached draft with the conversion of the FEM value, same identity.
            if (Value != null) { var value = Value; Common.Gpc.GpcDraftCheck.Attached("GH_Node", draft, () => Common.Gpc.GpcBeamDrafts.FromFemm(value, draft.Node.Guid)); }
        }

        private Common.Gpc.Legacy.LegacyNodeDraft _gpc;
        private NodeModel _gpcOf;
        // R6.1: a former payload (BGHA1) read from a file, converted by value on first use while the FEM value read with it is current.
        private byte[] _legacy;
        private NodeModel _legacyOf;

        /// <summary>R6.1: a copy of the same content (<c>DuplicateGeometry</c>) keeps the datum attached or read and the former payload;
        /// a conversion of the FEM value is recomputed. The copy constructor (a copy that a component changes) keeps nothing.</summary>
        private void CarryGpcTo(GH_Node copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
            if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { copy._legacy = _legacy; copy._legacyOf = copy.Value; }
        }


        #endregion

        #region PreviewData methods

        public BoundingBox ClippingBox => Boundingbox;

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            if (Display.ModelPreview.Automatic && Value != null) Display.ModelPreview.Meshes(Value,args.Pipeline,Display.DisplayPreferences.Current);
        }

        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if (!Display.ModelPreview.Automatic) return;
            if (Value == null) Display.ModelPreview.DraftWires(Gpc,args.Pipeline,args.Viewport,args.Color,Display.DisplayPreferences.Current);
            else Display.ModelPreview.Wires(Value,args.Pipeline,args.Viewport,args.Color,Display.DisplayPreferences.Current);
        }

        #endregion

        #region Transformation methods

        public override IGH_GeometricGoo Morph(SpaceMorph xmorph)
        {
            if (Value == null)
            {
                // R6.5 P0: the position of the draft, as a point (a node is rebuilt by the components).
                var at = GpcDraftGeometry.Position(Gpc);
                if (!at.IsValid) return null;
                var morphed = new Point(at); xmorph.Morph(morphed);
                return new GH_Point(morphed.Location);
            }
            if (Value.Position == Point3d.Unset)
                return null;

            Point point = new Point(Value.Position);
            xmorph.Morph(point);
            return new GH_Node(point.Location);
        }

        public override IGH_GeometricGoo Transform(Transform xform)
        {
            //It's debatable whether you should maintain a BoatShell through transformations. 
            //It might not be easy/make sense to apply scaling/rotations, shears etc.
            //In this example, I'll convert the shell to a brep.
            //Perhaps you will want to check for translations/rotations only, operations that make sense.
            if (Value == null)
            {
                var at = GpcDraftGeometry.Position(Gpc);
                if (!at.IsValid) return null;
                var moved = new GH_Point(at); moved.Transform(xform);
                return moved;
            }
            if (Value.Position == Point3d.Unset)
                return null;

            GH_Point point = new GH_Point(Value.Position);
            point.Transform(xform);
            return point;
        }

        #endregion

        #region BakeAwareData methods

        public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
        {
            if (Value == null)
            {
                var at = GpcDraftGeometry.Position(Gpc);
                obj_guid = at.IsValid ? doc.Objects.AddPoint(at, att) : Guid.Empty;
                return obj_guid != Guid.Empty;
            }
            obj_guid = doc.Objects.AddPoint(Value.Position, att);
            return obj_guid != Guid.Empty;
        }

        #endregion

        #region Casting methods

        public override bool CastTo<Q>(out Q target)
        {
            try
            {
                // R6.5 P0: the position of the draft; results come with the FEM value until the components give them on GPC.
                if (Value == null && typeof(Q).IsAssignableFrom(typeof(GH_Point)) && GpcDraftGeometry.Position(Gpc) is { IsValid: true } at)
                {
                    target = (Q)(object)new GH_Point(at);
                    return true;
                }
                if (Value == null && typeof(Q).IsAssignableFrom(typeof(GH_NodeResults)) && AnalysedGpc != null)
                {
                    target = (Q)(object)new GH_NodeResults(this);
                    return true;
                }
                if (Value != null && Value.IsValid)
                {
                    if (typeof(Q).IsAssignableFrom(typeof(GH_Point)))
                    {

                        GH_Point point = new GH_Point(Value.Position);
                        target = (Q)Convert.ChangeType(point, typeof(Q));
                        return true;
                    }
                    else if (typeof(Q).IsAssignableFrom(typeof(GH_NodeResults)))
                    {
                        GH_NodeResults results = new GH_NodeResults(this);
                        target = (Q)Convert.ChangeType(results, typeof(Q));
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e.Message);
            }

            target = default;
            return false;
        }

        public override bool CastFrom(object source)
        {
            if (source == null) { return false; }

            if (typeof(NodeModel).IsAssignableFrom(source.GetType()))
            {
                Value = (NodeModel)source;
                return true;
            }

            return false;
        }

        #endregion

        public override IGH_GeometricGoo DuplicateGeometry()
        {
            GH_Node copy = new GH_Node
            {
                _guid = _guid,
                Value = Value == null ? null : Common.Helpers.ModelArchive.Clone(Value),
                AnalysedGpc = AnalysedGpc, AnalysedGpcModel = AnalysedGpcModel,
            };
            CarryGpcTo(copy);
            return copy;
        }

        public override BoundingBox GetBoundingBox(Transform xform)
        {
            if (Value == null)
            {
                var box = Boundingbox;
                if (box.IsValid) box.Transform(xform);
                return box;
            }
            if (Value.Position == Point3d.Unset)
                return BoundingBox.Empty;
            BoundingBox bb = Boundingbox;
            bb.Transform(xform);
            return bb;
        }

        public override bool Write(GH_IWriter writer)
        {
            try
            {
                writer.SetGuid("Guid", _guid);
                GpcGooPersistence.Write(writer, () => Common.Gpc.GpcDraftArchive.Write(Gpc), Value == null ? null : Common.Helpers.ModelArchive.Write(Value));
                return true;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                _guid = reader.GetGuid("Guid");
                switch (GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case GpcGooPersistence.Kind.Gpc:
                        var draft = Common.Gpc.GpcDraftArchive.ReadNode(data, records);
                        Value = femm == null ? null : Common.Helpers.ModelArchive.Read<NodeModel>(femm);
                        _gpc = draft; _gpcOf = Value; HasAttachedGpc = true; return true;
                    case GpcGooPersistence.Kind.Legacy:
                        Value = Common.Helpers.ModelArchive.Read<NodeModel>(femm); _legacy = femm; _legacyOf = Value; return true;
                    case GpcGooPersistence.Kind.FemmOnly:
                        Value = Common.Helpers.ModelArchive.Read<NodeModel>(femm); return true;
                    default: return false;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return GpcDraftGeometry.Text(Gpc);
            else
                return Value.ToString();
        }
    }
}
