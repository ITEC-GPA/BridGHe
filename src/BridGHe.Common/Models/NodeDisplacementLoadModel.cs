using System;
using System.Drawing;
using System.Runtime.Serialization;
using Rhino.Display;
using Rhino.Geometry;
using System.Collections.Generic;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class NodeDisplacementLoadModel : LoadModel<NodeDisplacementLoadModel.LoadData>, IEquatable<NodeDisplacementLoadModel>
    {
        [System.Serializable]
        public class LoadData : IEquatable<LoadData>
        {
            public Vector3d Translation { get; set; }
            public Vector3d Rotation { get; set; }

            public LoadData()
            {
                Translation = Vector3d.Zero;
                Rotation = Vector3d.Zero;
            }

            public LoadData(Vector3d translation, Vector3d rotation)
            {
                Translation = translation;
                Rotation = rotation;
            }
            public LoadData(LoadData other)
            {
                Translation = new Vector3d(other.Translation);
                Rotation = new Vector3d(other.Rotation);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as LoadData);
            }

            public bool Equals(LoadData other)
            {
                return other != null && Translation.Equals(other.Translation) && Rotation.Equals(other.Rotation);
            }

            public override int GetHashCode()
            {
                var hashCode = -1051130837;
                hashCode = hashCode * -1521134295 + EqualityComparer<Vector3d>.Default.GetHashCode(Translation);
                hashCode = hashCode * -1521134295 + EqualityComparer<Vector3d>.Default.GetHashCode(Rotation);
                return hashCode;
            }
        }

        protected static bool _isVisible = true;
        protected static Color _color = Color.IndianRed;

        public static bool IsVisible
        {
            get => _isVisible;
            set => _isVisible = value;
        }

        public static Color Color
        {
            get => _color;
            set => _color = value;
        }

        public override string Description => "Node Displacement Load";

        public NodeDisplacementLoadModel()
        {
        }

        public NodeDisplacementLoadModel(LoadCaseModel loadCase, LoadData value)
            : base(loadCase, value)
        {
        }

        public NodeDisplacementLoadModel(NodeDisplacementLoadModel dl)
        {
            _loadCase = dl._loadCase;
            _value = new LoadData(dl.Value);
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "NodeDisplacementLoadModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected NodeDisplacementLoadModel(SerializationInfo info, StreamingContext context)
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
            double dx = info.GetDouble("dX");
            double dy = info.GetDouble("dY");
            double dz = info.GetDouble("dZ");
            double rx = info.GetDouble("rX");
            double ry = info.GetDouble("rY");
            double rz = info.GetDouble("rZ");
            _value = new LoadData(new Vector3d(dx, dy, dz), new Vector3d(rx, ry, rz));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("dX", _value.Translation.X);
            info.AddValue("dY", _value.Translation.Y);
            info.AddValue("dZ", _value.Translation.Z);
            info.AddValue("rX", _value.Rotation.X);
            info.AddValue("rY", _value.Rotation.Y);
            info.AddValue("rZ", _value.Rotation.Z);
        }

        #endregion

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {
            if (!IsVisible)
                return;

            NodeModel node = (NodeModel)element;
            if (node != null)
            {
                if (_value.Translation != Vector3d.Unset && _value.Translation.SquareLength > 0.000001)
                {
                    display.DrawArrow(new Line(node.Position, node.Position + _value.Translation), Color);
                }
                if (_value.Rotation != Vector3d.Unset && _value.Rotation.SquareLength > 0.000001)
                {
                    display.DrawArrow(new Line(node.Position, node.Position + _value.Rotation), Color);
                }
            }
        }

        public static bool operator ==(NodeDisplacementLoadModel dl1, NodeDisplacementLoadModel dl2)
        {
            if (ReferenceEquals(dl1, dl2))
                return true;
            if (dl1 is null)
                return false;
            return dl1.Equals(dl2);
        }

        public static bool operator !=(NodeDisplacementLoadModel dl1, NodeDisplacementLoadModel dl2)
        {
            return !(dl1 == dl2);
        }

        public bool Equals(NodeDisplacementLoadModel other)
        {
            return !(other is null) && LoadCase == other.LoadCase && Value.Equals(other.Value); ;
        }

        public override bool Equals(object obj)
        {
            if (obj is NodeDisplacementLoadModel dl)
                return Equals(dl);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            int hashCode = 132748229;
            hashCode = hashCode * 96323 + LoadCase.GetHashCode();
            hashCode = hashCode * 96323 + Value.GetHashCode();
            return hashCode;
        }

        public override string ToString()
        {
            string text = $"BridGHe Node displacement load: ";
            text += $"Load Case: {_loadCase}, ";
            text += $"D={_value.Translation} R={_value.Rotation}";
            return text;
        }
    }
}
