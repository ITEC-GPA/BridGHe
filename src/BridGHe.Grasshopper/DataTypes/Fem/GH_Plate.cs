using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_Plate : GH_GeometricGoo<object>, IGH_BakeAwareData, IGH_PreviewData
    {
        protected Guid _guid;
        protected GH_PlateProperty _plateProperty;
        protected List<GH_Group> _groups;

        #region Properties

        public Guid Guid => _guid;

        public GH_PlateProperty PlateProperty => _plateProperty;

        public List<GH_Group> Groups => _groups;

        // R6.5 P0: without a FEM value the plate is its GPC draft.
        public override bool IsValid => Gpc?.Plate != null;

        public override string IsValidWhyNot
        {
            get
            {
                return Gpc?.Plate != null ? string.Empty : "No internal PlateModel instance";
            }
        }

        public override string TypeName => "Plate";

        public override string TypeDescription => "Define a single plate";

        public override BoundingBox Boundingbox
        {
            get
            {
                return GpcDraftGeometry.Bounds(Gpc);
            }
        }

        #endregion

        #region Constructors

        public GH_Plate()
        {
            _guid = Guid.NewGuid();
            _groups = new List<GH_Group>();
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
            if (plate.Gpc is { } draft) { _gpc = draft; _gpcOf = null; HasAttachedGpc = true; }
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

        public Common.Gpc.Legacy.LegacyPlateDraft Gpc
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
        public void AttachGpc(Common.Gpc.Legacy.LegacyPlateDraft draft)
        {
            _gpc = draft ?? throw new System.ArgumentNullException(nameof(draft)); _gpcOf = Value; HasAttachedGpc = true;
        }

        private Common.Gpc.Legacy.LegacyPlateDraft _gpc;
        private object _gpcOf;

        /// <summary>R6.1: a copy of the same content (<c>DuplicateGeometry</c>) keeps the datum attached or read; a conversion of the FEM value
        /// is recomputed. The copy constructor (a copy that a component changes) keeps nothing.</summary>
        private void CarryGpcTo(GH_Plate copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
        }


        #endregion

        #region PreviewData methods

        // C3 (11/11): without a FEM value, the box of the draft with its solid.
        public BoundingBox ClippingBox => Display.ModelPreview.DraftBounds(Gpc);

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            if (!Display.ModelPreview.Automatic) return;
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
            // R6.5 P1 lane 2a (revisione ChatGPT R65-1): a plate given by its draft alone morphs the solid of its draft, as the FEM plate its shape.
            var shape = GpcDraftGeometry.Shape(Gpc);
                if (shape == null) return null;
                xmorph.Morph(shape);
                return new GH_Brep(shape);
        }

        public override IGH_GeometricGoo Transform(Transform xform)
        {
            //It's debatable whether you should maintain a BoatShell through transformations. 
            //It might not be easy/make sense to apply scaling/rotations, shears etc.
            //In this example, I'll convert the shell to a brep.
            //Perhaps you will want to check for translations/rotations only, operations that make sense.
            // R6.5 P1 lane 2a (revisione ChatGPT R65-1): a plate given by its draft alone transforms the solid of its draft.
            var shape = GpcDraftGeometry.Shape(Gpc);
                if (shape == null) return null;
                shape.Transform(xform);
                return new GH_Brep(shape);
        }

        #endregion

        #region BakeAwareData methods

        public bool BakeGeometry(RhinoDoc doc, ObjectAttributes att, out Guid obj_guid)
        {
            var flat = GpcDraftGeometry.FlatMesh(Gpc);
                obj_guid = flat == null ? Guid.Empty : doc.Objects.AddMesh(flat, att);
                return obj_guid != Guid.Empty;
        }

        #endregion

        #region Casting methods

        public override bool CastTo<Q>(out Q target)
        {
            // R6.5 P0: the outline and the flat mesh of the draft; shape and results are read from GPC data.
                if (typeof(Q) == typeof(GH_Curve) && GpcDraftGeometry.Outline(Gpc) is { } outline) { target = (Q)(object)new GH_Curve(outline); return true; }
                if (typeof(Q) == typeof(GH_Mesh) && GpcDraftGeometry.FlatMesh(Gpc) is { } flat) { target = (Q)(object)new GH_Mesh(flat); return true; }
                if (typeof(Q).IsAssignableFrom(typeof(GH_PlateResults)) && AnalysedGpc != null) { target = (Q)(object)new GH_PlateResults(this); return true; }
                // P2D (P2K, example 50 REV003): the solid of the plate from its GPC draft (its section along the normal), as the FEM value gave it
                // (Value.Shape); a plate without a solid gives none.
                if (GH_Beam.CastShapes(Display.ModelPreview.DraftShapes(Gpc), out target)) return true;
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
            var box = Boundingbox;
                if (box.IsValid) box.Transform(xform);
                return box;
        }

        public override string ToString()
        {
            return GpcDraftGeometry.Text(Gpc);
        }
    }
}
