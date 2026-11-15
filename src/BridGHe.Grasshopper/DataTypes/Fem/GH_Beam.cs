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
    public class GH_Beam : GH_GeometricGoo<object>, IGH_BakeAwareData, IGH_PreviewData
    {
        protected Guid _guid;
        BoundingBox _boundingBox;
        long _boundedRevision = -1;

        #region Properties

        public Guid Guid => _guid;

        // R6.5 P0: without a FEM value the beam is its GPC draft.
        public override bool IsValid => Gpc?.Beam != null;

        public override string IsValidWhyNot
        {
            get
            {
                return Gpc?.Beam != null ? string.Empty : "No internal BeamModel instance";
            }
        }

        public override string TypeName => "Beam";

        public override string TypeDescription => "Define a single beam";

        public override BoundingBox Boundingbox
        {
            get
            {
                return GpcDraftGeometry.Bounds(Gpc);
            }
        }

        #endregion

        #region Constructors

        public GH_Beam()
        {
            _guid = Guid.NewGuid();
            _boundingBox = BoundingBox.Unset;
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
            if (beam.Gpc is { } draft) { _gpc = draft; _gpcOf = null; HasAttachedGpc = true; }
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

        public Common.Gpc.Legacy.LegacyBeamDraft Gpc
        {
            get
            {
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                return null;
            }
        }

        /// <summary>True when <see cref="Gpc"/> is a draft attached by a GPC component or read from a file (including legacy payloads read by value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC draft to this wrapper.</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyBeamDraft draft)
        {
            _gpc = draft ?? throw new ArgumentNullException(nameof(draft)); _gpcOf = Value; HasAttachedGpc = true;
        }

        private Common.Gpc.Legacy.LegacyBeamDraft _gpc;
        private object _gpcOf;

        /// <summary>R6.1: a copy of the same content (<c>DuplicateGeometry</c>) keeps the datum attached or read; a conversion of the FEM value
        /// is recomputed. The copy constructor (a copy that a component changes) keeps nothing.</summary>
        private void CarryGpcTo(GH_Beam copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
        }


        #endregion

        #region PreviewData methods

        // C3 (11/11): without a FEM value, the box of the draft with its section solids (as the FEM box holds the shapes).
        public BoundingBox ClippingBox => Display.ModelPreview.DraftBounds(Gpc);

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            if (!Display.ModelPreview.Automatic) return;
            // C3 (11/11): a beam given by its draft alone draws its section solids shaded, as the FEM beam.
            Display.ModelPreview.DraftMeshes(Gpc,args.Pipeline,Display.DisplayPreferences.Current);
        }

        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if (!Display.ModelPreview.Automatic) return;
            Display.ModelPreview.DraftWires(Gpc,args.Pipeline,args.Viewport,args.Color,Display.DisplayPreferences.Current);
        }

        #endregion

        #region Transformation methods

        public override IGH_GeometricGoo Morph(SpaceMorph xmorph)
        {

            throw new NotImplementedException();
        }

        public override IGH_GeometricGoo Transform(Transform xform)
        {

            throw new NotImplementedException();

        }

        #endregion

        #region BakeAwareData methods

        public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
        {
            var axis = GpcDraftGeometry.Axis(Gpc);
                obj_guid = axis.IsValid ? doc.Objects.AddLine(axis.From, axis.To, att) : Guid.Empty;
                return obj_guid != Guid.Empty;
        }

        #endregion

        #region Casting methods

        /// <summary>P2D: the solids of a draft (copies) as a <see cref="GH_Brep"/> (one solid, as the FEM cast) or a <see cref="Brep"/>, or as a
        /// list of <see cref="GH_Brep"/> or <see cref="Brep"/> (every solid); false when there is nothing to give.</summary>
        internal static bool CastShapes<Q>(Brep[] shapes, out Q target)
        {
            target = default;
            var solids = (shapes ?? Array.Empty<Brep>()).Where(s => s != null).ToArray();
            if (solids.Length == 0) return false;
            if (solids.Length == 1 && typeof(Q) == typeof(GH_Brep)) { target = (Q)(object)new GH_Brep(solids[0].DuplicateBrep()); return true; }
            if (solids.Length == 1 && typeof(Q) == typeof(Brep)) { target = (Q)(object)solids[0].DuplicateBrep(); return true; }
            if (typeof(Q) == typeof(System.Collections.Generic.List<GH_Brep>)) { target = (Q)(object)solids.Select(s => new GH_Brep(s.DuplicateBrep())).ToList(); return true; }
            if (typeof(Q) == typeof(System.Collections.Generic.List<Brep>)) { target = (Q)(object)solids.Select(s => s.DuplicateBrep()).ToList(); return true; }
            return false;
        }

        public override bool CastTo<Q>(out Q target)
        {
            // R6.5 P0: the axis of the draft; results with the analysed element.
                var axis = GpcDraftGeometry.Axis(Gpc);
                if (typeof(Q) == typeof(GH_Curve) && axis.IsValid) { target = (Q)(object)new GH_Curve(new LineCurve(axis)); return true; }
                // P2D (P2K, example 50 REV003): the solid of the beam from its GPC section along the axis (GpcDraftShapes.Beam), as the FEM value
                // gave it (Value.Shapes: a GH_Brep when the beam has one solid); every solid as a list of Breps.
                if (CastShapes(Display.ModelPreview.DraftShapes(Gpc), out target)) return true;
                if (typeof(Q).IsAssignableFrom(typeof(GH_BeamResults)) && AnalysedGpc != null) { target = (Q)(object)new GH_BeamResults(this); return true; }
                target = default;
                return false;
        }

        #endregion

        public override IGH_GeometricGoo DuplicateGeometry()
        {
            GH_Beam copy = new GH_Beam
            {
                _guid = _guid,
                Value = null,
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
            catch (Exception ex) { return GpcGooPersistence.NotWritten(writer, TypeName, ex, () => Gpc != null); } // P2CI (MU-01): said to the writer
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
            return GpcDraftGeometry.Text(Gpc);
        }
    }
}
