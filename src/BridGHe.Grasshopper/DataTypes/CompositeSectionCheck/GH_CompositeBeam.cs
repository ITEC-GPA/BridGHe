using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.IO;
using System.Linq;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes
{
    public class GH_CompositeBeam : GH_GeometricGoo<CompositeBeamModel>, IGH_PreviewData
    {
        protected Guid _guid;
        BoundingBox _boundingBox;
        CompositeBeamModel _boundedModel;
        long _boundedRevision = -1;

        #region Properties

        public Guid Guid => _guid;

        public override bool IsValid => Value == null ? false : Value.IsValid;

        public override string IsValidWhyNot
        {
            get
            {
                if (Value == null)
                    return "No internal BeamModel instance";
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
                if (m_value == null) return BoundingBox.Empty;
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
            Value = new CompositeBeamModel(beam.Value);
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
            if (Display.ModelPreview.Automatic) Display.ModelPreview.Wires(Value,args.Pipeline,args.Viewport,args.Color,Display.DisplayPreferences.Current);
        }

        #endregion

        #region Casting methods

        public override bool CastTo<Q>(out Q target)
        {
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
                _guid = this._guid,
                Value = Common.Helpers.ModelArchive.Clone(Value)
            };
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
            try { writer.SetGuid("Guid", _guid); writer.SetByteArray("Data", Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try { _guid = reader.GetGuid("Guid"); Value = Common.Helpers.ModelArchive.Read<CompositeBeamModel>(reader.GetByteArray("Data")); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null Beam";
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
