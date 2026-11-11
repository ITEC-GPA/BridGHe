using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_Plate : GH_GeometricGoo<PlateModel>, IGH_BakeAwareData, IGH_PreviewData
    {
        protected Guid _guid;
        protected GH_PlateProperty _plateProperty;
        protected List<GH_Group> _groups;

        #region Properties

        public Guid Guid => _guid;

        public GH_PlateProperty PlateProperty => _plateProperty;

        public List<GH_Group> Groups => _groups;

        // R6.5 P0: without a FEM value the plate is its GPC draft.
        public override bool IsValid => Value == null ? Gpc?.Plate != null : Value.IsValid;

        public override string IsValidWhyNot
        {
            get
            {
                if (Value == null)
                    return Gpc?.Plate != null ? string.Empty : "No internal PlateModel instance";
                if (Value.IsValid)
                    return string.Empty;
                return "Invalid PlateModel instance";
            }
        }

        public override string TypeName => "Plate";

        public override string TypeDescription => "Define a single plate";

        public override BoundingBox Boundingbox
        {
            get
            {
                if (Value == null)
                    return GpcDraftGeometry.Bounds(Gpc);
                if (!Value.IsValid)
                    return BoundingBox.Empty;
                return Value.Shape.GetBoundingBox(true);
            }
        }

        #endregion

        #region Constructors

        public GH_Plate()
        {
            _guid = Guid.NewGuid();
            _groups = new List<GH_Group>();
        }

        public GH_Plate(Point3d[] points, GH_PlateProperty pp, GH_Group[] groups = null)
            : this()
        {
            Value = new PlateModel(points, pp.Value);
            if (groups != null)
            {
                foreach (GH_Group group in groups)
                {
                    Value.Groups.Add(group.Value);
                    _groups.Add(group);
                }
            }
        }

        public GH_Plate(PlateModel plate, Guid? guid = null)
            : this()
        {
            if (plate == null)
                plate = new PlateModel();
            Value = plate;
            if (guid.HasValue)
                _guid = guid.Value;
            /*_plateProperty = new GH_PlateProperty(plate.PlateProperty);
            if (plate.Groups.Count > 0)
            {
                foreach (string group in plate.Groups)
                {
                    _groups.Add(new GH_Group(group));
                }
            }*/
        }

        /// <summary>
        /// Copy constructor. Use to duplicate the object to add data through the components.
        /// NOTE: It keeps the Guid original value
        /// </summary>
        /// <param name="plate">The plate to duplicate</param>
        public GH_Plate(GH_Plate plate) : this()
        {
            _guid = plate._guid;
            _plateProperty = plate._plateProperty == null ? null : (GH_PlateProperty)plate._plateProperty.Duplicate();
            _groups = plate._groups.ConvertAll(g => (GH_Group)g.Duplicate());
            // R6.5 P0: a plate without a FEM value is its draft: the copy keeps it (a component that changes the copy attaches its own).
            if (plate.Value == null) { if (plate.Gpc is { } draft) { _gpc = draft; _gpcOf = null; HasAttachedGpc = true; } }
            else Value = new PlateModel(plate.Value);
            AnalysedGpc = plate.AnalysedGpc; AnalysedGpcModel = plate.AnalysedGpcModel;
        }

        /// <summary>R6.5 P0 (coordinator, 7/11): a plate given by its GPC draft alone, with no FEM value: <see cref="Gpc"/> is the draft and
        /// every member of the type (validity, bounding box, preview, bake, cast, copies, persistence, text) reads it.</summary>
        public static GH_Plate FromGpc(Common.Gpc.Legacy.LegacyPlateDraft draft, Guid? guid = null)
        {
            var plate = new GH_Plate { _gpc = draft ?? throw new System.ArgumentNullException(nameof(draft)), HasAttachedGpc = true };
            if (guid.HasValue) plate._guid = guid.Value;
            return plate;
        }

        /// <summary>R6.5 P1 lane 2a (rule of passo 75): the copy of this plate (its GUID, analysed element and GH data, as the copy constructor
        /// keeps them) given by <paramref name="draft"/> alone, without the FEM value: the output of a component whose plate has a FEM value
        /// but whose load case is given by its record alone.</summary>
        internal GH_Plate WithDraftOnly(Common.Gpc.Legacy.LegacyPlateDraft draft)
        {
            var copy = FromGpc(draft, _guid);
            copy._plateProperty = _plateProperty == null ? null : (GH_PlateProperty)_plateProperty.Duplicate();
            copy._groups = _groups.ConvertAll(g => (GH_Group)g.Duplicate());
            copy.AnalysedGpc = AnalysedGpc; copy.AnalysedGpcModel = AnalysedGpcModel;
            return copy;
        }

        /// <summary>F6 C4: the analysed GPC element of this element and its analysed model, set by Decompose Model when its model was
        /// analysed (Straus R3 / Moving Static Analysis); null otherwise. Immutable by convention, so copies share it.</summary>
        public GPC.Model.Elements.Element AnalysedGpc { get; set; }
        public GPC.Model.Models.Model AnalysedGpcModel { get; set; }

        /// <summary>
        /// Chain E (e1): the GPC draft of this plate (definition units, see <see cref="Common.Gpc.Legacy.LegacyPlateDraft"/>): the one a GPC component
        /// attached to this FEM value (<see cref="AttachGpc"/>), otherwise the per-value conversion of the FEM value
        /// (<see cref="Common.Gpc.GpcBeamDrafts.FromFemm(PlateModel, System.Guid?)"/>), once per value. A new FEM value (a modified copy) drops the
        /// attached draft. Immutable by convention: whoever changes it makes a copy (<see cref="GPC.Model.Core.ModelValues.CopyValue{T}(T)"/>).
        /// </summary>
        public Common.Gpc.Legacy.LegacyPlateDraft Gpc
        {
            get
            {
                // R6.1: the datum attached or read (BGHGPC1), the former payload read by value (BGHA1), otherwise the conversion of the FEM value.
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { _gpc = Common.Gpc.GpcLegacyPayloads.Plate(_legacy); _gpcOf = Value; HasAttachedGpc = false; return _gpc; }
                if (Value == null) return null;
                _gpc = Common.Gpc.GpcBeamDrafts.FromFemm(Value); _gpcOf = Value; HasAttachedGpc = false;
                return _gpc;
            }
        }

        /// <summary>True when <see cref="Gpc"/> is a draft attached by a GPC component (not the conversion of the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC draft built on GPC for the current FEM value (it must describe it: the gates compare the two).</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyPlateDraft draft)
        {
            _gpc = draft ?? throw new System.ArgumentNullException(nameof(draft)); _gpcOf = Value; HasAttachedGpc = true;
            // R6.1 (frozen API for R6.2): the gates compare the attached draft with the conversion of the FEM value, same identity.
            if (Value != null) { var value = Value; Common.Gpc.GpcDraftCheck.Attached("GH_Plate", draft, () => Common.Gpc.GpcBeamDrafts.FromFemm(value, draft.Plate.Guid)); }
        }

        private Common.Gpc.Legacy.LegacyPlateDraft _gpc;
        private PlateModel _gpcOf;
        // R6.1: a former payload (BGHA1) read from a file, converted by value on first use while the FEM value read with it is current.
        private byte[] _legacy;
        private PlateModel _legacyOf;

        /// <summary>R6.1: a copy of the same content (<c>DuplicateGeometry</c>) keeps the datum attached or read and the former payload;
        /// a conversion of the FEM value is recomputed. The copy constructor (a copy that a component changes) keeps nothing.</summary>
        private void CarryGpcTo(GH_Plate copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
            if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { copy._legacy = _legacy; copy._legacyOf = copy.Value; }
        }


        #endregion

        #region PreviewData methods

        // C3 (11/11): without a FEM value, the box of the draft with its solid.
        public BoundingBox ClippingBox => Value == null ? Display.ModelPreview.DraftBounds(Gpc) : Boundingbox;

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            if (!Display.ModelPreview.Automatic) return;
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
            // R6.5 P1 lane 2a (revisione ChatGPT R65-1): a plate given by its draft alone morphs the solid of its draft, as the FEM plate its shape.
            if (Value == null)
            {
                var shape = GpcDraftGeometry.Shape(Gpc);
                if (shape == null) return null;
                xmorph.Morph(shape);
                return new GH_Brep(shape);
            }
            if (!Value.IsValid)
                return null;

            Brep brep = Value.Shape.DuplicateBrep();
            xmorph.Morph(brep);
            return new GH_Brep(brep);
        }

        public override IGH_GeometricGoo Transform(Transform xform)
        {
            //It's debatable whether you should maintain a BoatShell through transformations. 
            //It might not be easy/make sense to apply scaling/rotations, shears etc.
            //In this example, I'll convert the shell to a brep.
            //Perhaps you will want to check for translations/rotations only, operations that make sense.
            // R6.5 P1 lane 2a (revisione ChatGPT R65-1): a plate given by its draft alone transforms the solid of its draft.
            if (Value == null)
            {
                var shape = GpcDraftGeometry.Shape(Gpc);
                if (shape == null) return null;
                shape.Transform(xform);
                return new GH_Brep(shape);
            }
            if (!Value.IsValid)
                return null;

            Brep brep = Value.Shape.DuplicateBrep();
            brep.Transform(xform);
            return new GH_Brep(brep);
        }

        #endregion

        #region BakeAwareData methods

        public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
        {
            if (Value == null)
            {
                var flat = GpcDraftGeometry.FlatMesh(Gpc);
                obj_guid = flat == null ? Guid.Empty : doc.Objects.AddMesh(flat, att);
                return obj_guid != Guid.Empty;
            }
            var mesh = new Mesh();
            mesh.Vertices.AddVertices(Value.Points);
            for (int i = 1; i < Value.Points.Count - 1; i++) mesh.Faces.AddFace(0, i, i + 1);
            obj_guid = doc.Objects.AddMesh(mesh, att);
            return obj_guid != Guid.Empty;
        }

        #endregion

        #region Casting methods

        public override bool CastTo<Q>(out Q target)
        {
            if (Value == null)
            {
                // R6.5 P0: the outline and the flat mesh of the draft; the shape and results come with the FEM value until P1.
                if (typeof(Q) == typeof(GH_Curve) && GpcDraftGeometry.Outline(Gpc) is { } outline) { target = (Q)(object)new GH_Curve(outline); return true; }
                if (typeof(Q) == typeof(GH_Mesh) && GpcDraftGeometry.FlatMesh(Gpc) is { } flat) { target = (Q)(object)new GH_Mesh(flat); return true; }
                if (typeof(Q).IsAssignableFrom(typeof(GH_PlateResults)) && AnalysedGpc != null) { target = (Q)(object)new GH_PlateResults(this); return true; }
                target = default;
                return false;
            }
            if (typeof(Q) == typeof(GH_Curve))
            {
                PolylineCurve pline = new PolylineCurve(Value.Points);
                pline.MakeClosed(0);
                target = (Q)Convert.ChangeType(new GH_Curve(pline), typeof(Q));
                return true;
            }
            else if (typeof(Q) == typeof(GH_Brep))
            {
                target = (Q)Convert.ChangeType(new GH_Brep(Value.Shape), typeof(Q));
                return true;
            }
            else if (typeof(Q).IsAssignableFrom(typeof(GH_PlateResults)))
            {
                if (Value != null && Value.IsValid)
                {
                    GH_PlateResults results = new GH_PlateResults(this);
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
            var copy = new GH_Plate(this);
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
            if (!Value.IsValid)
                return BoundingBox.Empty;
            return Value.Shape.GetBoundingBox(xform);
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
