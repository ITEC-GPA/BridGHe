using BridGHe.Common.Models;
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

    public class GH_CompositeBeam : GH_GeometricGoo<CompositeBeamModel>, IGH_PreviewData
    {
        protected Guid _guid;
        BoundingBox _boundingBox;
        CompositeBeamModel _boundedModel;
        long _boundedRevision = -1;

        #region Properties

        public Guid Guid => _guid;

        // R6.5 P1 lane 3a: without a FEM value the composite beam is its GPC datum.
        public override bool IsValid => Value == null ? Gpc?.Beam?.Beam != null : Value.IsValid;

        public override string IsValidWhyNot
        {
            get
            {
                if (Value == null)
                    return Gpc?.Beam?.Beam != null ? string.Empty : "No internal BeamModel instance";
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
                if (m_value == null) return GpcDraftGeometry.Bounds(Gpc?.Beam);
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

        /// <summary>R6.5 P1 lane 3a: the BridGHe ID of the beam: that of the FEM value, or of the GPC datum without one.</summary>
        public string BeamId => Value != null ? Value.BeamId : Gpc?.BeamId;

        #endregion

        #region Constructors

        public GH_CompositeBeam()
        {
            _guid = Guid.NewGuid();
            _boundingBox = BoundingBox.Unset;
        }

        public GH_CompositeBeam(Point3d from, Point3d to, GH_CompositeBeamProperty startbp, GH_Group[] groups = null, GH_CompositeBeamProperty endbp = null)
            : this()
        {
            Value = new CompositeBeamModel(from, to, startbp.Value, groups == null ? null : groups.Select(i => i.Value).ToArray(), endbp == null ? null : endbp.Value);
            if (groups != null && groups.Length > 0)
            {
                foreach (GH_Group group in groups)
                {
                    Value.Groups.Add(group.Value);
                }
            }
        }

        public GH_CompositeBeam(CompositeBeamModel beam, Guid? guid = null)
            : this()
        {
            if (beam == null)
                beam = new CompositeBeamModel();
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
        public GH_CompositeBeam(GH_CompositeBeam beam) : this()
        {
            _guid = beam._guid;
            //_beamProperty = beam._beamProperty;
            //_groups = beam._groups;
            // R6.5 P1 lane 3a: a composite beam without a FEM value is its datum: the copy keeps it.
            if (beam.Value == null) { _gpc = beam.Gpc; _gpcOf = null; }
            else Value = new CompositeBeamModel(beam.Value);
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
        private CompositeBeamModel _gpcOf;

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
            get => _startDefinition ??= Value?.StartBeamProperty == null ? null : Common.Gpc.GpcCompositeSections.Definition(Value.StartBeamProperty, Value.StartBeamProperty.Name);
            set => _startDefinition = value;
        }

        /// <inheritdoc cref="StartDefinition"/>
        public GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition EndDefinition
        {
            get
            {
                if (_endDefinition != null) return _endDefinition;
                if (Value?.EndBeamProperty == null) return null;
                // The same property at both ends (Composite Beam without Section End): the same definition.
                return _endDefinition = ReferenceEquals(Value.EndBeamProperty, Value.StartBeamProperty) ? StartDefinition
                    : Common.Gpc.GpcCompositeSections.Definition(Value.EndBeamProperty, Value.EndBeamProperty.Name);
            }
            set => _endDefinition = value;
        }

        private GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition _startDefinition, _endDefinition;

        #endregion

        #region PreviewData methods

        // C3 (11/11): without a FEM value, the box of the beam with the solids of its two definitions.
        public BoundingBox ClippingBox => Value == null ? Display.ModelPreview.DraftBounds(Gpc) : Boundingbox;

        public void DrawViewportMeshes(GH_PreviewMeshArgs args)
        {
            if (!Display.ModelPreview.Automatic) return;
            // C3 (11/11): without a FEM value, the solids of the datum shaded (steel and slab), as the FEM composite beam.
            if (Value == null) Display.ModelPreview.DraftMeshes(Gpc,args.Pipeline,Display.DisplayPreferences.Current);
            else Display.ModelPreview.Meshes(Value,args.Pipeline,Display.DisplayPreferences.Current);
        }

        public void DrawViewportWires(GH_PreviewWireArgs args)
        {
            if (!Display.ModelPreview.Automatic) return;
            // R6.5 P1 lane 3a: without a FEM value the draft of the beam (its axis, label), as a beam given by its draft alone; C3 (11/11): with
            // the solids and the bar lines of its two definitions (the datum).
            if (Value == null) Display.ModelPreview.DraftWires(Gpc,args.Pipeline,args.Viewport,args.Color,Display.DisplayPreferences.Current);
            else Display.ModelPreview.SectionWires(Value,args.Pipeline,args.Viewport,args.Color,Display.DisplayPreferences.Current,PreviewSections);
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
            if (Value == null)
            {
                // R6.5 P1 lane 3a: the axis of the draft of the beam; the shapes come with the FEM value.
                var axis = GpcDraftGeometry.Axis(Gpc?.Beam);
                if (typeof(Q) == typeof(GH_Curve) && axis.IsValid) { target = (Q)(object)new GH_Curve(new LineCurve(axis)); return true; }
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
                target = default(Q);
                return false;
            }
            //else if (typeof(Q).IsAssignableFrom(typeof(GH_BeamResults)))
            //{
            //    if (Value != null && Value.IsValid)
            //    {
            //        GH_BeamResults results = new GH_BeamResults(this);
            //        target = (Q)Convert.ChangeType(results, typeof(Q));
            //        return true;
            //    }
            //}

            target = default(Q);
            return false;
        }

        #endregion

        public override IGH_GeometricGoo DuplicateGeometry()
        {
            GH_CompositeBeam copy = new GH_CompositeBeam
            {
                _guid = this._guid, AnalysedGpc = AnalysedGpc, AnalysedGpcModel = AnalysedGpcModel,
                Value = Value == null ? null : Common.Helpers.ModelArchive.Clone(Value), _startDefinition = _startDefinition, _endDefinition = _endDefinition
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
            // R6.1 (§1bis): a composite beam is a reference into an analysed bridge model, not persisted; until R6.5 its FEM value goes in the
            // transitional chunk only (copies and undo), never in Data. R6.5 P1 lane 3a: without a FEM value only its GUID (not persisted).
            try { writer.SetGuid("Guid", _guid); if (Value != null) writer.SetByteArray(GpcGooPersistence.Femm, Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                _guid = reader.GetGuid("Guid"); _startDefinition = _endDefinition = null; _gpc = null; _gpcOf = null;
                // R6.1: a former payload (0.3.0, Data) is a cache of an analysed model: dropped (Common.Gpc.GpcGooArchive.LegacyCacheMessage).
                if (!reader.ItemExists(GpcGooPersistence.Femm)) { System.Diagnostics.Debug.WriteLine(Common.Gpc.GpcGooArchive.LegacyCacheMessage); return false; }
                Value = Common.Helpers.ModelArchive.Read<CompositeBeamModel>(reader.GetByteArray(GpcGooPersistence.Femm)); return true;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return Gpc == null ? "Null Beam" : "BridGHe Composite Beam" + (string.IsNullOrEmpty(Gpc.BeamId) ? "" : " #" + Gpc.BeamId)
                    + "\nSections: " + (Gpc.Start?.Name ?? "-") + ", " + (Gpc.End?.Name ?? "-") + "\nGroups: " + string.Join(", ", Gpc.Groups ?? Array.Empty<string>())
                    + "\nGPC datum (no FEM value)";
            else
                return Value.ToString();
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
