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
    public class PlatePreStressModel : LoadModel<PlatePreStressModel.LoadData>, IEquatable<PlatePreStressModel>
    {
        [System.Serializable]
        public class LoadData : IEquatable<LoadData>
        {
            public Vector3d LocalPressure { get; set; }

            public override bool Equals(object obj)
            {
                return Equals(obj as LoadData);
            }

            public bool Equals(LoadData other)
            {
                return other != null &&
                    LocalPressure.Equals(other.LocalPressure);
            }

            public override int GetHashCode()
            {
                return 540953319 + LocalPressure.GetHashCode();
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

        public override string Description => "Plate PreStress";

        public PlatePreStressModel()
        {
        }

        public PlatePreStressModel(LoadCaseModel loadCase, LoadData value)
            : base(loadCase, value)
        {
        }

        public PlatePreStressModel(PlatePreStressModel load)
        {
            _loadCase = load._loadCase;
            _value = new LoadData()
            {
                LocalPressure = new Vector3d(load.Value.LocalPressure)
            };
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "PlatePreStressModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected PlatePreStressModel(SerializationInfo info, StreamingContext context)
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
                LocalPressure = (Vector3d)info.GetValue("LocalPressure", typeof(Vector3d))
            };
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("LocalPressure", _value.LocalPressure, typeof(Vector3d));
        }
        #endregion

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {
            if (!IsVisible)
                return;

            PlateModel plate = (PlateModel)element;
            if (element != null)
            {
                Polyline pline = new Polyline(plate.Points);
                pline.Add(plate.Points[0]);
                Point3d pt = pline.CenterPoint();
                Vector3d v1, v2, v3;

                //Plane.FitPlaneToPoints(plate.Points, out Plane pl);

                //v3 = pl.Normal;
                //v3.Unitize();
                plate.GetLocalAxes(out v1, out v2, out v3);

                double scale = DrawingHelper.GetScaleFactor(display.Viewport, pt) * Scale;

                display.DrawArrow(new Line(pt + v3 * -_value.LocalPressure.Z * scale, pt), Color, 0, DrawingHelper.ArrowSize);
            }
        }

        public static bool operator ==(PlatePreStressModel m1, PlatePreStressModel m2)
        {
            if (ReferenceEquals(m1, m2))
                return true;
            return !(m1 is null) && m1.Equals(m2);
        }

        public static bool operator !=(PlatePreStressModel m1, PlatePreStressModel m2)
        {
            return !(m1 == m2);
        }

        public bool Equals(PlatePreStressModel other)
        {
            return !(other is null) && Value.Equals(other.Value) && other.LoadCase.Equals(_loadCase);
        }

        public override bool Equals(object obj)
        {
            if (obj is PlatePreStressModel model)
                return Equals(model);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            int hashCode = 383428191;
            hashCode = hashCode * -1521134295 + LoadCase.GetHashCode();
            hashCode = hashCode * -1521134295 + Value.GetHashCode();
            return hashCode;
        }

        public override string ToString()
        {
            string text = "BridGHe PreStress load: ";
            text += $"Load Case: {_loadCase}, ";
            text += $"PreStress={_value.LocalPressure}";
            text += " Local";
            return text;
        }
    }
}
