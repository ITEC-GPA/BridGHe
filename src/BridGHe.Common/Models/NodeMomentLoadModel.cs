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
    public class NodeMomentLoadModel : LoadModel<Vector3d>, IEquatable<NodeMomentLoadModel>
    {
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

        public override string Description => "Node Moment Load";

        public NodeMomentLoadModel()
        {
        }

        public NodeMomentLoadModel(LoadCaseModel loadCase, Vector3d value)
            : base(loadCase, value)
        {
        }

        public NodeMomentLoadModel(NodeMomentLoadModel ml)
        {
            _loadCase = ml._loadCase;
            _value = ml._value;
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "NodeMomentLoadModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected NodeMomentLoadModel(SerializationInfo info, StreamingContext context)
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
            double x = info.GetDouble("X");
            double y = info.GetDouble("Y");
            double z = info.GetDouble("Z");
            _value = new Vector3d(x, y, z);
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("X", _value.X);
            info.AddValue("Y", _value.Y);
            info.AddValue("Z", _value.Z);
        }

        #endregion

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {
            if (!IsVisible)
                return;

            NodeModel node = (NodeModel)element;
            if (node != null)
            {
                double scale = DrawingHelper.GetScaleFactor(display.Viewport, node.Position) * Scale;

                if (_value.X != 0)
                    display.DrawCircle(new Circle(new Plane(node.Position, Vector3d.ZAxis), _value.X * scale), Color);
                if (_value.Y != 0)
                    display.DrawCircle(new Circle(new Plane(node.Position, Vector3d.XAxis), _value.Y * scale), Color);
                if (_value.Z != 0)
                    display.DrawCircle(new Circle(new Plane(node.Position, Vector3d.YAxis), _value.Z * scale), Color);
            }
        }

        public static bool operator ==(NodeMomentLoadModel m1, NodeMomentLoadModel m2)
        {
            if (ReferenceEquals(m1, m2))
                return true;
            return !(m1 is null) && m1.Equals(m2);
        }

        public static bool operator !=(NodeMomentLoadModel m1, NodeMomentLoadModel m2)
        {
            return !(m1 == m2);
        }

        public bool Equals(NodeMomentLoadModel other)
        {
            return !(other is null) && LoadCase == other.LoadCase && Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is NodeMomentLoadModel model)
                return Equals(model);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            var hashCode = 63277979;
            hashCode = hashCode * 64857271 + LoadCase.GetHashCode();
            hashCode = hashCode * 64857271 + EqualityComparer<Vector3d>.Default.GetHashCode(Value);

            return hashCode;
        }

        public override string ToString()
        {
            string text = $"BridGHe Node Moment load: ";
            text += $"Load Case: {_loadCase}, ";
            text += $"V={_value}";
            return text;
        }
    }
}
