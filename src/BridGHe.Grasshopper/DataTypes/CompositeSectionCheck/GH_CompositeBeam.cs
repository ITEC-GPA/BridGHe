using BridGHe.Grasshopper.DataTypes.Fem;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes
{
    /// <summary>
    /// R6.5 P1 lane 3a (coordinator, 8/11): the GPC datum of a composite beam: the draft of its beam (<see cref="Beam"/>, the input of Composite
    /// Beam as it was given: its identity, ends, BridGHe ID, bridge data), the composite sections of its start and of its end as the GPC
    /// contracts of the Checker (null: a composite property that is not a valid definition, the error of the checks that read it, as on the FEM
    /// path) and the full names of its groups (those of the beam, then those of the Groups input that it has not). Immutable by convention.
    /// </summary>
    public sealed record GpcCompositeBeam(Common.Gpc.Legacy.LegacyBeamDraft Beam, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Start,
        GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition End, IReadOnlyList<string> Groups)
    {
        /// <summary>The BridGHe ID of the beam (the name of the beam of the draft, the ID Build Model gave it), as the FEM value copies it.</summary>
        public string BeamId => Beam?.Beam?.Name;
    }

    public class GH_CompositeBeam : GH_GeometricGoo<object>, IGH_PreviewData
    {
        protected Guid _guid;
        BoundingBox _boundingBox;
        long _boundedRevision = -1;

        #region Properties

        public Guid Guid => _guid;

        // R6.5 P1 lane 3a: without a FEM value the composite beam is its GPC datum.
        public override bool IsValid => Gpc?.Beam?.Beam != null;

        public override string IsValidWhyNot
        {
            get
            {
                return Gpc?.Beam?.Beam != null ? string.Empty : "No internal BeamModel instance";
            }
        }

        public override string TypeName => "Beam";

        public override string TypeDescription => "Define a single beam";

        public override BoundingBox Boundingbox
        {
            get
            {
                return GpcDraftGeometry.Bounds(Gpc?.Beam);
            }
        }

        /// <summary>R6.5 P1 lane 3a: the BridGHe ID of the beam: that of the FEM value, or of the GPC datum without one.</summary>
        public string BeamId => Gpc?.BeamId;

        #endregion

        #region Constructors

        public GH_CompositeBeam()
        {
            _guid = Guid.NewGuid();
            _boundingBox = BoundingBox.Unset;
        }



        /// <summary>
        /// Copy constructor. Use to duplicate the object to add data through the components.
        /// NOTE: It keeps the Guid original value
        /// </summary>
        /// <param name="beam">The beam to duplicate</param>
        public GH_CompositeBeam(GH_CompositeBeam beam) : this()
        {
            _guid = beam._guid;
            //_beamProperty = beam._beamProperty;
            //_groups = beam._groups;
            // R6.5 P1 lane 3a: a composite beam without a FEM value is its datum: the copy keeps it.
            _gpc = beam.Gpc; _gpcOf = null;
            AnalysedGpc = beam.AnalysedGpc; AnalysedGpcModel = beam.AnalysedGpcModel;
            _startDefinition = beam._startDefinition; _endDefinition = beam._endDefinition;
        }

        /// <summary>R6.5 P1 lane 3a (coordinator, 8/11): a composite beam given by its GPC datum alone, with no FEM value: <see cref="Gpc"/> is
        /// the datum, the definitions are those of the datum, and validity, bounding box, preview, cast, copies and text read it. Not persisted
        /// (R6.1: a composite beam is a reference into an analysed bridge model; Composite Beam makes it again).</summary>
        public static GH_CompositeBeam FromGpc(GpcCompositeBeam datum, Guid? guid = null)
        {
            var beam = new GH_CompositeBeam { _gpc = datum ?? throw new ArgumentNullException(nameof(datum)), _gpcOf = null };
            beam._startDefinition = datum.Start; beam._endDefinition = datum.End;
            if (guid.HasValue) beam._guid = guid.Value;
            return beam;
        }

        /// <summary>
        /// R6.5 P1 lane 3a: the GPC datum of this composite beam: the one Composite Beam attached to the current FEM value (the dual rule of R6.2:
        /// the draft of its input beam, its two definitions, its groups) or the one it was made of (<see cref="FromGpc"/>); null for a composite
        /// beam made from its FEM value alone (a value read from a document, a composite beam of a FEM model).
        /// </summary>
        public GpcCompositeBeam Gpc => _gpc != null && ReferenceEquals(_gpcOf, Value) ? _gpc : null;

        /// <summary>Composite Beam: the GPC datum built for the current FEM value.</summary>
        public void AttachGpc(GpcCompositeBeam datum)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); _gpcOf = Value;
        }

        private GpcCompositeBeam _gpc;
        private object _gpcOf;

        /// <summary>F6 (a)/(b), chain C: the beam of the GPC bridge model this composite beam was made from and that model (set by Composite
        /// Beam from a beam of Super Element Filter or Decompose Model of a Build BridGHe Model output); null otherwise. Shared by copies.</summary>
        public GPC.Model.Elements.Element AnalysedGpc { get; set; }
        public GPC.Model.Models.Model AnalysedGpcModel { get; set; }

        /// <summary>
        /// Chain E (c): the composite sections of the start and of the end of the beam as the GPC contracts of the Checker
        /// (<see cref="GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition"/>), what Super Element Check, Composite Beam Linear Tension
        /// Analysis and the preview read (the FEM properties of <see cref="GH_Goo{T}.Value"/> go with the FeMM models). Composite Beam sets them
        /// to the definitions of its two <see cref="GH_CompositeBeamProperty"/> inputs (<see cref="GH_CompositeBeamProperty.Definition"/>);
        /// without them (a beam read from a document or made elsewhere) they are built once from the FEM properties with the same rule (steel H
        /// named by the property). Shared by copies (the definitions are values). R6.5 P1 lane 3a: without a FEM value, those of the datum.
        /// </summary>
        public GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition StartDefinition
        {
            get => _startDefinition; // P2CE phase 2b: no FEM properties to build it from (a beam without FEM value has the definitions of its datum)
            set => _startDefinition = value;
        }

        /// <inheritdoc cref="StartDefinition"/>
        public GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition EndDefinition
        {
            get
            {
                return _endDefinition; // P2CE phase 2b: no FEM properties to build it from
            }
            set => _endDefinition = value;
        }

        private GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition _startDefinition, _endDefinition;

        #endregion

        #region PreviewData methods

        // C3 (11/11): without a FEM value, the box of the beam with the solids of its two definitions.
        public BoundingBox ClippingBox => Display.ModelPreview.DraftBounds(Gpc);

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            if (!Display.ModelPreview.Automatic) return;
            // C3 (11/11): without a FEM value, the solids of the datum shaded (steel and slab), as the FEM composite beam.
            Display.ModelPreview.DraftMeshes(Gpc,args.Pipeline,Display.DisplayPreferences.Current);
        }

        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if (!Display.ModelPreview.Automatic) return;
            // R6.5 P1 lane 3a: without a FEM value the draft of the beam (its axis, label), as a beam given by its draft alone; C3 (11/11): with
            // the solids and the bar lines of its two definitions (the datum).
            Display.ModelPreview.DraftWires(Gpc,args.Pipeline,args.Viewport,args.Color,Display.DisplayPreferences.Current);
        }

        /// <summary>Chain E (c): the definitions the preview draws (none when they cannot be built: the preview then reads the FEM value).</summary>
        public Display.ModelPreview.CompositeSections PreviewSections
        {
            get
            {
                try { return new Display.ModelPreview.CompositeSections(StartDefinition, EndDefinition); }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { return default; }
            }
        }

        #endregion

        #region Casting methods

        public override bool CastTo<Q>(out Q target)
        {
            // R6.5 P1 lane 3a: the axis of the draft of the beam; the shapes come with the FEM value.
                var axis = GpcDraftGeometry.Axis(Gpc?.Beam);
                if (typeof(Q) == typeof(GH_Curve) && axis.IsValid) { target = (Q)(object)new GH_Curve(new LineCurve(axis)); return true; }
                target = default;
                return false;
        }

        #endregion

        public override IGH_GeometricGoo DuplicateGeometry()
        {
            GH_CompositeBeam copy = new GH_CompositeBeam
            {
                _guid = this._guid, AnalysedGpc = AnalysedGpc, AnalysedGpcModel = AnalysedGpcModel,
                Value = null, _startDefinition = _startDefinition, _endDefinition = _endDefinition
            };
            // R6.5 P1 lane 3a: a copy of the same content keeps the datum (attached to the copy of the value, or the datum alone).
            if (Gpc is { } datum) { copy._gpc = datum; copy._gpcOf = copy.Value; }
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
            // R6.1 (§1bis): a composite beam is a reference into an analysed bridge model, not persisted (Composite Beam makes it again): its
            // GUID only. P2D: never its FEM value (the transitional chunk FemmR6 is no longer written).
            try { writer.SetGuid("Guid", _guid); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                _guid = reader.GetGuid("Guid"); _startDefinition = _endDefinition = null; _gpc = null; _gpcOf = null; Value = null;
                // R6.1: a former payload (0.3.0, Data) is a cache of an analysed model: dropped (Common.Gpc.GpcGooArchive.LegacyCacheMessage). P2D:
                // so is the FEM chunk of a development file (a reference into an analysed model, no FEM value is read).
                System.Diagnostics.Debug.WriteLine(Common.Gpc.GpcGooArchive.LegacyCacheMessage); return false;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            return Gpc == null ? "Null Beam" : "BridGHe Composite Beam" + (string.IsNullOrEmpty(Gpc.BeamId) ? "" : " #" + Gpc.BeamId)
                    + "\nSections: " + (Gpc.Start?.Name ?? "-") + ", " + (Gpc.End?.Name ?? "-") + "\nGroups: " + string.Join(", ", Gpc.Groups ?? Array.Empty<string>())
                    + "\nGPC datum (no FEM value)";
        }

        public override IGH_GeometricGoo Transform(Transform xform)
        {
            throw new NotImplementedException();
        }

        public override IGH_GeometricGoo Morph(SpaceMorph xmorph)
        {
            throw new NotImplementedException();
        }
    }
}
