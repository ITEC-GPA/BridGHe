using System;
using System.Drawing;
using Rhino.Display;
using Rhino.Geometry;
using BridGHe.Common.Helpers;
using System.Runtime.Serialization;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class NodeTemperatureLoadModel : LoadModel<double>, IEquatable<NodeTemperatureLoadModel>
    {
        public enum LoadType
        {
            Reference,
            Fixed,
            Initial
        }

        protected static bool _isVisible = true;
        protected static double _scale = 1;
        protected static Color _color = Color.IndianRed;
        protected LoadType _loadType;

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

        public override string Description => "Node Temperature Load";

        public LoadType TemperatureLoadType
        {
            get => _loadType;
            set => _loadType = value;
        }

        public NodeTemperatureLoadModel()
        {
        }

        public NodeTemperatureLoadModel(LoadCaseModel loadCase, double value, LoadType type)
            : base(loadCase, value)
        {
            _loadType = type;
        }

        public NodeTemperatureLoadModel(NodeTemperatureLoadModel load)
        {
            _loadCase = load._loadCase;
            _loadType = load._loadType;
            _value = load.Value;
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "NodeTemperatureLoadModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected NodeTemperatureLoadModel(SerializationInfo info, StreamingContext context)
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

            _loadType = (LoadType)info.GetValue("LoadType", typeof(LoadType));
            _value = info.GetDouble("Value");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("LoadType", _loadType, typeof(LoadType));
            info.AddValue("Value", _value);
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

                Vector3d v = new Vector3d(scale / 2, scale / 2, scale / 2);
                BoundingBox box = new BoundingBox(node.Position - v, node.Position + v);
                display.DrawBox(box, Color);
            }
        }

        public static bool operator ==(NodeTemperatureLoadModel m1, NodeTemperatureLoadModel m2)
        {
            if (ReferenceEquals(m1, m2))
                return true;
            return !(m1 is null) && m1.Equals(m2);
        }

        public static bool operator !=(NodeTemperatureLoadModel m1, NodeTemperatureLoadModel m2)
        {
            return !(m1 == m2);
        }

        public bool Equals(NodeTemperatureLoadModel other)
        {
            return !(other is null) && LoadCase == other.LoadCase &&
                TemperatureLoadType == other.TemperatureLoadType && Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is NodeTemperatureLoadModel model)
                return Equals(model);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            int hashCode = 1051660382;
            hashCode = hashCode * 105744319 + LoadCase.GetHashCode();
            hashCode = hashCode * 105744319 + TemperatureLoadType.GetHashCode();
            hashCode = hashCode * 105744319 + Value.GetHashCode();
            return hashCode;
        }
    }
}
