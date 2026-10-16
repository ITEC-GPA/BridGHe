using System;
using System.Drawing;
using System.Runtime.Serialization;
using Rhino.Display;
using Rhino.Geometry;
using BridGHe.Common.Helpers;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class PlateNSMassModel : LoadModel<PlateNSMassModel.LoadData>
    {
        [System.Serializable]
        public class LoadData
        {
            public double Mass { get; set; }
            public double DynFactor { get; set; }
            public Vector3d Offset { get; set; }
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

        public override string Description => "Plate Non Structural mass";

        public PlateNSMassModel()
        {
        }

        public PlateNSMassModel(LoadCaseModel loadCase, LoadData value)
            : base(loadCase, value)
        {
        }

        public PlateNSMassModel(PlateNSMassModel nsMass)
        {
            _loadCase = nsMass._loadCase;
            _value = new LoadData()
            {
                Mass = nsMass.Value.Mass,
                DynFactor = nsMass.Value.DynFactor,
                Offset = nsMass.Value.Offset
            };
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "PlateNSMassModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected PlateNSMassModel(SerializationInfo info, StreamingContext context)
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
                Mass = info.GetDouble("Mass"),
                DynFactor = info.GetDouble("DynFactor"),
                Offset = (Vector3d)info.GetValue("Offset", typeof(Vector3d))
            };
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("Mass", _value.Mass);
            info.AddValue("DynFactor", _value.DynFactor);
            info.AddValue("Offset", _value.Offset, typeof(Vector3d));
        }

        #endregion

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {
            PlateModel plate = (PlateModel)element;
            if (element != null)
            {
                if (Plane.FitPlaneToPoints(plate.Points, out Plane plane) == PlaneFitResult.Success)
                {
                    Polyline pline = new Polyline(plate.Points);
                    pline.Add(plate.Points[0]);
                    Point3d pt = pline.CenterPoint();
                    plane = new Plane(pt, plane.Normal);
                    BoundingBox bbox = new BoundingBox(plate.Points);
                    display.DrawCircle(new Circle(plane, bbox.Diagonal.Length * 0.2 * Scale), Color);
                }
            }
        }

        public override string ToString()
        {
            string text = $"BridGHe NS mass: Load Case: {_loadCase},  V={_value.Mass},  DF={_value.DynFactor}, O={_value.Offset}";
            return text;
        }
    }
}
