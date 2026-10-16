using System;
using System.Drawing;
using System.Runtime.Serialization;
using Rhino.Display;
using Rhino.Geometry;
using BridGHe.Common.Helpers;
using System.Collections.Generic;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class PlatePressureModel : LoadModel<PlatePressureModel.LoadData>, IEquatable<PlatePressureModel>
    {
        public enum LoadFace
        {
            Top,
            Bottom
        }

        [System.Serializable]
        public class LoadData : IEquatable<LoadData>
        {
            public LoadFace Face { get; set; }
            /// <summary>
            /// NULL for local system
            /// </summary>
            public CoordinateSystemModel CoordinateSystem { get; set; }
            public Vector3d P { get; set; }
            public bool Projected { get; set; }

            public override bool Equals(object obj)
            {
                return Equals(obj as LoadData);
            }

            public bool Equals(LoadData other)
            {
                return other != null && Face == other.Face &&
                    EqualityComparer<CoordinateSystemModel>.Default.Equals(CoordinateSystem, other.CoordinateSystem) &&
                    P.Equals(other.P) && Projected == other.Projected;
            }

            public override int GetHashCode()
            {
                var hashCode = 1384602228;
                hashCode = hashCode * -1521134295 + Face.GetHashCode();
                hashCode = hashCode * -1521134295 + EqualityComparer<CoordinateSystemModel>.Default.GetHashCode(CoordinateSystem);
                hashCode = hashCode * -1521134295 + EqualityComparer<Vector3d>.Default.GetHashCode(P);
                hashCode = hashCode * -1521134295 + Projected.GetHashCode();
                return hashCode;
            }
        }


        protected static bool _isVisible = true;
        protected static double _scale = 1;
        protected static Color _color = Color.IndianRed;

        public static bool IsVisible
        {
            get => _isVisible;
            set => _isVisible = value;
        }

        public static double Scale
        {
            get => _scale;
            set => _scale = value;
        }

        public static Color Color
        {
            get => _color;
            set => _color = value;
        }

        public override string Description => "Plate Pressure";

        public PlatePressureModel()
        {
        }

        public PlatePressureModel(LoadCaseModel loadCase, LoadData value)
            : base(loadCase, value)
        {
        }

        public PlatePressureModel(PlatePressureModel load)
        {
            _loadCase = load._loadCase;
            _value = new LoadData()
            {
                Face = load.Value.Face,
                CoordinateSystem = load.Value.CoordinateSystem != null ? new CoordinateSystemModel(load.Value.CoordinateSystem) : null,
                P = new Vector3d(load.Value.P),
                Projected = load.Value.Projected
            };
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "PlatePressureModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected PlatePressureModel(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            int savedRelease;
            try
            {
                savedRelease = info.GetInt32(SERIALIZATION_RELEASE_NAME);
            }
            catch
            {
                savedRelease = 0;
            }
            _value = new LoadData()
            {
                Face = (LoadFace)info.GetValue("Face", typeof(LoadFace)),
                CoordinateSystem = (CoordinateSystemModel)info.GetValue("CoordinateSystem", typeof(CoordinateSystemModel)),
                P = (Vector3d)info.GetValue("Pressure", typeof(Vector3d)),
                Projected = info.GetBoolean("Projected")
            };
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("Face", _value.Face, typeof(LoadFace));
            info.AddValue("CoordinateSystem", _value.CoordinateSystem, typeof(CoordinateSystemModel));
            info.AddValue("Pressure", _value.P, typeof(Vector3d));
            info.AddValue("Projected", _value.Projected);
        }

        #endregion

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {
            if (!IsVisible)
                return;

            PlateModel plate = (PlateModel)element;
            if (element != null)
            {
                Polyline pline = new Polyline(plate.Points) { plate.Points[0] };
                Point3d pt = pline.CenterPoint();
                Vector3d v3;
                if (_value.CoordinateSystem == null) // Local system
                {
                    //Plane.FitPlaneToPoints(plate.Points, out Plane pl);

                    //v3 = pl.Normal;
                    //v3.Unitize();
                    plate.GetLocalAxes(out _, out _, out v3);
                }
                else if (_value.CoordinateSystem.Name == CoordinateSystemModel.Global.Name)
                {
                    v3 = Vector3d.ZAxis;
                }
                else //  Custom system
                {
                    // TODO: gestire ucs
                    throw new NotImplementedException("custom ucs not implemented");
                }
                double scale = DrawingHelper.GetScaleFactor(display.Viewport, pt) * Scale;

                display.DrawArrow(new Line(pt + v3 * -_value.P.Z * scale, pt), Color, 0, DrawingHelper.ArrowSize);
            }
        }

        public static bool operator ==(PlatePressureModel m1, PlatePressureModel m2)
        {
            if (ReferenceEquals(m1, m2))
                return true;
            return !(m1 is null) && m1.Equals(m2);
        }

        public static bool operator !=(PlatePressureModel m1, PlatePressureModel m2)
        {
            return !(m1 == m2);
        }

        public bool Equals(PlatePressureModel other)
        {
            return !(other is null) && Value.Equals(other.Value) && other.LoadCase.Equals(_loadCase);
        }

        public override bool Equals(object obj)
        {
            if (obj is PlatePressureModel model)
                return Equals(model);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            var hashCode = 75377;
            hashCode = hashCode * 71947 + LoadCase.GetHashCode();
            hashCode = hashCode * 71947 + Value.GetHashCode();

            return hashCode;
        }

        public override string ToString()
        {
            string text = "BridGHe Pressure load: ";
            text += $"Load Case: {_loadCase}, ";
            text += $"V={_value.P}";
            if (_value.CoordinateSystem == null)
                text += " Local";
            else
                text += " " + _value.CoordinateSystem.Name;
            return text;
        }
    }
}
