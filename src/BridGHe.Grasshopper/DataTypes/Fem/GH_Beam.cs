using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.IO;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_Beam : GH_GeometricGoo<BeamModel>, IGH_BakeAwareData, IGH_PreviewData
    {
        protected Guid _guid;
        BoundingBox _boundingBox;
        BeamModel _boundedModel;
        long _boundedRevision = -1;

        #region Properties

        public Guid Guid => _guid;

        // R6.5 P0: without a FEM value the beam is its GPC draft.
        public override bool IsValid => Value == null ? Gpc?.Beam != null : Value.IsValid;

        public override string IsValidWhyNot
        {
            get
            {
                if (Value == null)
                    return Gpc?.Beam != null ? string.Empty : "No internal BeamModel instance";
                if (Value.IsValid)
                    return string.Empty;
                return "Invalid BeamModel instance";
            }
        }

        public override string TypeName => "Beam";

        public override string TypeDescription => "Define a single beam";

        public override BoundingBox Boundingbox
        {
            get
            {
                if (m_value == null) return GpcDraftGeometry.Bounds(Gpc);
                if (!ReferenceEquals(_boundedModel, m_value) || _boundedRevision != m_value.GeometryRevision)
                {
                    _boundingBox = BoundingBox.Empty;
                    if (m_value.PointFrom.IsValid) _boundingBox.Union(m_value.PointFrom);
                    if (m_value.PointTo.IsValid) _boundingBox.Union(m_value.PointTo);
                    foreach (Brep shape in m_value.Shapes) _boundingBox.Union(shape.GetBoundingBox(true));
                    _boundedModel = m_value;
                    _boundedRevision = m_value.GeometryRevision;
                }
                return _boundingBox;
            }
        }

        #endregion

        #region Constructors

        public GH_Beam()
        {
            _guid = Guid.NewGuid();
            _boundingBox = BoundingBox.Unset;
        }

        public GH_Beam(Point3d from, Point3d to, GH_BeamProperty bp, GH_Group[] groups = null)
            : this()
        {
            Value = new BeamModel(from, to, bp.Value);
            if (groups != null && groups.Length > 0)
            {
                foreach (GH_Group group in groups)
                {
                    Value.Groups.Add(group.Value);
                }
            }
        }

        public GH_Beam(BeamModel beam, Guid? guid = null)
            : this()
        {
            if (beam == null)
                beam = new BeamModel();
            Value = beam;
            if (guid.HasValue)
                _guid = guid.Value;
            //_beamProperty = new GH_BeamProperty(beam.BeamProperty);
            /*if (beam.Groups.Count > 0)
            {
                foreach (string group in beam.Groups)
                {
                    _groups.Add(new GH_Group(group));
                }
            }*/
        }

        /// <summary>
        /// Copy constructor. Use to duplicate the object to add data through the components.
        /// NOTE: It keeps the Guid original value
        /// </summary>
        /// <param name="beam">The beam to duplicate</param>
        public GH_Beam(GH_Beam beam) : this()
        {
            _guid = beam._guid;
            //_beamProperty = beam._beamProperty;
            //_groups = beam._groups;
            // R6.5 P0: a beam without a FEM value is its draft: the copy keeps it (a component that changes the copy attaches its own).
            if (beam.Value == null) { if (beam.Gpc is { } draft) { _gpc = draft; _gpcOf = null; HasAttachedGpc = true; } }
            else Value = new BeamModel(beam.Value);
            AnalysedGpc = beam.AnalysedGpc; AnalysedGpcModel = beam.AnalysedGpcModel;
        }

        /// <summary>R6.5 P0 (coordinator, 7/11): a beam given by its GPC draft alone, with no FEM value: <see cref="Gpc"/> is the draft and
        /// every member of the type (validity, bounding box, preview, bake, cast, copies, persistence, text) reads it.</summary>
        public static GH_Beam FromGpc(Common.Gpc.Legacy.LegacyBeamDraft draft, Guid? guid = null)
        {
            var beam = new GH_Beam { _gpc = draft ?? throw new ArgumentNullException(nameof(draft)), HasAttachedGpc = true };
            if (guid.HasValue) beam._guid = guid.Value;
            return beam;
        }

        /// <summary>R6.5 P1 lane 2a (rule of passo 75): the copy of this beam (its GUID and analysed element, as the copy constructor keeps
        /// them) given by <paramref name="draft"/> alone, without the FEM value: the output of a component whose beam has a FEM value but
        /// another input (a load case, a property, a group) is given by its GPC datum alone.</summary>
        internal GH_Beam WithDraftOnly(Common.Gpc.Legacy.LegacyBeamDraft draft)
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
        /// Chain E (d): the GPC draft of this beam (L12 §5.4: the <see cref="GPC.Model.Elements.BeamElement"/> and its
        /// <see cref="GPC.Checkers.CompositeBridge.BridgeBeamData"/>, definition units, no nodes): the one a GPC component attached to this FEM
        /// value (<see cref="AttachGpc"/>, e.g. Bridge State Override on GPC), otherwise the per-value conversion of the FEM value
        /// (<see cref="Common.Gpc.GpcBeamDrafts.FromFemm(BeamModel)"/>), once per value. A new FEM value (a modified copy) drops the attached
        /// draft. Immutable by convention: whoever changes it makes a copy (<see cref="GPC.Model.Core.ModelValues.CopyValue{T}(T)"/>).
        /// </summary>
        public Common.Gpc.Legacy.LegacyBeamDraft Gpc
        {
            get
            {
                // R6.1: the datum attached or read (BGHGPC1, or a former payload read by value: P2D), otherwise the conversion of the FEM value
                // (the FEM value of a component not yet on GPC: removed with the FEM types).
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                if (Value == null) return null;
                _gpc = Common.Gpc.GpcBeamDrafts.FromFemm(Value); _gpcOf = Value; HasAttachedGpc = false;
                return _gpc;
            }
        }

        /// <summary>True when <see cref="Gpc"/> is a draft attached by a GPC component or read from a file (not the conversion of a FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC draft built on GPC for the current FEM value.</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyBeamDraft draft)
        {
            _gpc = draft ?? throw new ArgumentNullException(nameof(draft)); _gpcOf = Value; HasAttachedGpc = true;
        }

        private Common.Gpc.Legacy.LegacyBeamDraft _gpc;
        private BeamModel _gpcOf;

        /// <summary>R6.1: a copy of the same content (<c>DuplicateGeometry</c>) keeps the datum attached or read; a conversion of the FEM value
        /// is recomputed. The copy constructor (a copy that a component changes) keeps nothing.</summary>
        private void CarryGpcTo(GH_Beam copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
        }


        #endregion

        #region PreviewData methods

        // C3 (11/11): without a FEM value, the box of the draft with its section solids (as the FEM box holds the shapes).
        public BoundingBox ClippingBox => Value == null ? Display.ModelPreview.DraftBounds(Gpc) : Boundingbox;

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            if (!Display.ModelPreview.Automatic) return;
            // C3 (11/11): a beam given by its draft alone draws its section solids shaded, as the FEM beam.
            if (Value == null) Display.ModelPreview.DraftMeshes(Gpc,args.Pipeline,Display.DisplayPreferences.Current);
            else Display.ModelPreview.Meshes(Value,args.Pipeline,Display.DisplayPreferences.Current);
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
            /*
            if (Value == null)
                return null;
            if (!Value.IsValid)
                return null;

            Brep brep = Value.Shape.DuplicateBrep();
            xmorph.Morph(brep);
            return new GH_Brep(brep);
            */
            throw new NotImplementedException();
        }

        public override IGH_GeometricGoo Transform(Transform xform)
        {
            /*
            // It's debatable whether you should maintain a BoatShell through transformations. 
            // It might not be easy/make sense to apply scaling/rotations, shears etc.
            // In this example, I'll convert the shell to a brep.
            // Perhaps you will want to check for translations/rotations only, operations that make sense.
            if (Value == null)
                return null;
            if (!Value.IsValid)
                return null;

            Brep brep = Value.Shape.DuplicateBrep();
            brep.Transform(xform);
            return new GH_Brep(brep);
            */
            throw new NotImplementedException();

        }

        #endregion

        #region BakeAwareData methods

        public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
        {
            if (Value == null)
            {
                var axis = GpcDraftGeometry.Axis(Gpc);
                obj_guid = axis.IsValid ? doc.Objects.AddLine(axis.From, axis.To, att) : Guid.Empty;
                return obj_guid != Guid.Empty;
            }
            obj_guid = doc.Objects.AddLine(Value.PointFrom, Value.PointTo, att);
            return obj_guid != Guid.Empty;
        }

        #endregion

        #region Casting methods

        public override bool CastTo<Q>(out Q target)
        {
            if (Value == null)
            {
                // R6.5 P0: the axis of the draft; shapes and results come with the FEM value until the components give them on GPC.
                var axis = GpcDraftGeometry.Axis(Gpc);
                if (typeof(Q) == typeof(GH_Curve) && axis.IsValid) { target = (Q)(object)new GH_Curve(new LineCurve(axis)); return true; }
                if (typeof(Q).IsAssignableFrom(typeof(GH_BeamResults)) && AnalysedGpc != null) { target = (Q)(object)new GH_BeamResults(this); return true; }
                target = default;
                return false;
            }
            if (typeof(Q) == typeof(GH_Curve))
            {
                LineCurve line = new LineCurve(Value.PointFrom, Value.PointTo);
                target = (Q)Convert.ChangeType(new GH_Curve(line), typeof(Q));
                return true;
            }
            else if (typeof(Q) == typeof(GH_Brep))
            {
                if (Value.Shapes.Length == 1)
                {
                    Brep brep = Value.Shapes[0].DuplicateBrep();
                    target = (Q)Convert.ChangeType(new GH_Brep(brep), typeof(Q));
                    return true;
                }
                /*else
                {
                    GH_Structure<GH_Brep> breps = new GH_Structure<GH_Brep>();
                    for (int i = 0; i < Value.Shapes.Length; i++)
                    {
                        Brep brep = Value.Shapes[i].DuplicateBrep();
                        breps.Append(new GH_Brep(brep));
                    }
                    target = (Q)Convert.ChangeType(breps, typeof(Q));
                }*/
                target = default;
                return false;
            }
            else if (typeof(Q).IsAssignableFrom(typeof(GH_BeamResults)))
            {
                if (Value != null && Value.IsValid)
                {
                    GH_BeamResults results = new GH_BeamResults(this);
                    target = (Q)Convert.ChangeType(results, typeof(Q));
                    return true;
                }
            }

            target = default;
            return false;
        }

        #endregion

        public override IGH_GeometricGoo DuplicateGeometry()
        {
            GH_Beam copy = new GH_Beam
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
            var boundingbox = Boundingbox;
            boundingbox.Transform(xform);
            return boundingbox;
        }

        public override bool Write(GH_IWriter writer)
        {
            try
            {
                writer.SetGuid("Guid", _guid);
                GpcGooPersistence.Write(writer, Common.Gpc.GpcDraftArchive.Write(Gpc)); // P2D: the datum alone, never the FEM value
                return true;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                _guid = reader.GetGuid("Guid");
                // P2D: the datum (BGHGPC1), or a former payload / the FEM chunk alone of a development file read by value; never a FEM value.
                var draft = GpcGooPersistence.Read(reader, out var data, out var records, out var femm) switch
                {
                    GpcGooPersistence.Kind.Gpc => Common.Gpc.GpcDraftArchive.ReadBeam(data, records),
                    GpcGooPersistence.Kind.Legacy => Common.Gpc.GpcLegacyPayloads.Beam(femm),
                    _ => null,
                };
                if (draft == null) return false;
                Value = null; _gpc = draft; _gpcOf = null; HasAttachedGpc = true; return true;
            }
            catch (Exception ex) { return GpcGooPersistence.Refused(reader, TypeName, ex); }
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
