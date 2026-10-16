using System;
using System.Linq;
using System.Drawing;
using System.Runtime.Serialization;
using Rhino.Display;
using Rhino.Geometry;
using BridGHe.Common.Helpers;
using System.Collections.Generic;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class NodeStiffnessAttributeModel : AttributeModel<NodeStiffnessAttributeModel.AttributeData>,
        IEquatable<NodeStiffnessAttributeModel>
    {
        protected static bool _isVisible = true;
        protected static double _scale = 1;
        protected static Color _color = Color.Beige;

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

        [System.Serializable]
        public class AttributeData : IEquatable<AttributeData>
        {
            public double Tx { get; set; }
            public double Ty { get; set; }
            public double Tz { get; set; }
            public double Rx { get; set; }
            public double Ry { get; set; }
            public double Rz { get; set; }

            public override bool Equals(object obj)
            {
                return Equals(obj as AttributeData);
            }

            public bool Equals(AttributeData other)
            {
                return other != null && Tx == other.Tx && Ty == other.Ty && Tz == other.Tz &&
                    Rx == other.Rx && Ry == other.Ry && Rz == other.Rz;
            }

            public override int GetHashCode()
            {
                var hashCode = 444219682;
                hashCode = hashCode * -1521134295 + Tx.GetHashCode();
                hashCode = hashCode * -1521134295 + Ty.GetHashCode();
                hashCode = hashCode * -1521134295 + Tz.GetHashCode();
                hashCode = hashCode * -1521134295 + Rx.GetHashCode();
                hashCode = hashCode * -1521134295 + Ry.GetHashCode();
                hashCode = hashCode * -1521134295 + Rz.GetHashCode();
                return hashCode;
            }
        }

        public override string Description => "Node stiffness";

        public NodeStiffnessAttributeModel()
        {
            _value = new AttributeData();
        }

        public NodeStiffnessAttributeModel(double tx, double ty, double tz, double rx, double ry, double rz)
        {
            _value = new AttributeData()
            {
                Tx = tx,
                Ty = ty,
                Tz = tz,
                Rx = rx,
                Ry = ry,
                Rz = rz
            };
        }

        public NodeStiffnessAttributeModel(AttributeData value)
            : base(value)
        {
        }

        public NodeStiffnessAttributeModel(NodeStiffnessAttributeModel attr)
        {
            _value = new AttributeData()
            {
                Tx = attr.Value.Tx,
                Ty = attr.Value.Ty,
                Tz = attr.Value.Tz,
                Rx = attr.Value.Rx,
                Ry = attr.Value.Ry,
                Rz = attr.Value.Rz,
            };
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "NodeStiffnessAttributeModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected NodeStiffnessAttributeModel(SerializationInfo info, StreamingContext context)
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
            _value = new AttributeData()
            {
                Tx = info.GetDouble("Tx"),
                Ty = info.GetDouble("Ty"),
                Tz = info.GetDouble("Tz"),
                Rx = info.GetDouble("Rx"),
                Ry = info.GetDouble("Ry"),
                Rz = info.GetDouble("Rz"),
            };
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("Tx", _value.Tx);
            info.AddValue("Ty", _value.Ty);
            info.AddValue("Tz", _value.Tz);
            info.AddValue("Rx", _value.Rx);
            info.AddValue("Ry", _value.Ry);
            info.AddValue("Rz", _value.Rz);
        }

        #endregion

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {
            const double TOLL = 0.0000000001;
            if (!IsVisible)
                return;

            NodeModel node = (NodeModel)element;
            if (node != null)
            {
                double lr = -DrawingHelper.GetScaleFactor(display.Viewport, node.Position) * Scale;
                if (Math.Abs(_value.Tx) > TOLL)
                    DrawZigZag(display, node.Position, Vector3d.XAxis, lr * Math.Sign(_value.Tx), Vector3d.YAxis, Color);
                if (Math.Abs(_value.Ty) > TOLL)
                    DrawZigZag(display, node.Position, Vector3d.YAxis, lr * Math.Sign(_value.Ty), Vector3d.XAxis, Color);
                if (Math.Abs(_value.Tz) > TOLL)
                    DrawZigZag(display, node.Position, Vector3d.ZAxis, lr * Math.Sign(_value.Tz), Vector3d.XAxis, Color);
                if (Math.Abs(_value.Rx) > TOLL)
                    DrawZigZag(display, node.Position, Vector3d.XAxis, lr * Math.Sign(_value.Rx), Vector3d.YAxis, Color);
                if (Math.Abs(_value.Ry) > TOLL)
                    DrawZigZag(display, node.Position, Vector3d.YAxis, lr * Math.Sign(_value.Ry), Vector3d.XAxis, Color);
                if (Math.Abs(_value.Rz) > TOLL)
                    DrawZigZag(display, node.Position, Vector3d.ZAxis, lr * Math.Sign(_value.Rz), Vector3d.XAxis, Color);
            }
        }

        private void DrawZigZag(DisplayPipeline display, Point3d origin, Vector3d dir, double length, Vector3d orto, Color color)
        {
            List<Point3d> points;
            Vector3d dir2 = Vector3d.CrossProduct(dir, orto);
            dir2.Unitize();
            points = new List<Point3d>();
            points.Add(origin);
            points.Add(origin + 0.5 * dir * length + 0.5 * dir2 * length);
            for (int i = 0; i < 5; i++)
            {
                double multip;
                if (i % 2 == 0)
                {
                    multip = -1;
                }
                else
                {
                    multip = 1;
                }
                points.Add(points[points.Count - 1] + 0.5 * dir * length + multip * dir2 * length);
            }
            for (int i = 0; i < points.Count - 1; i++)
            {
                display.DrawLine(points[i], points[i + 1], color);
            }

        }

        public static bool operator ==(NodeStiffnessAttributeModel ns1, NodeStiffnessAttributeModel ns2)
        {
            if (ReferenceEquals(ns1, ns2))
                return true;
            if (ns1 is null)
                return false;
            return ns1.Equals(ns2);
        }

        public static bool operator !=(NodeStiffnessAttributeModel ns1, NodeStiffnessAttributeModel ns2)
        {
            return !(ns1 == ns2);
        }

        public bool Equals(NodeStiffnessAttributeModel other)
        {
            return !(other is null) && Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is NodeStiffnessAttributeModel ns)
                return Equals(ns);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            int hashCode = 121657777;
            hashCode = hashCode * 19110407 + Value.GetHashCode();

            return hashCode;
        }

        public static AttributeData GetStiffness(NodeModel node)
        {
            if (node.HasAttribute(typeof(NodeStiffnessAttributeModel)))
            {
                return node.Attributes.Where(n => n.GetType() == typeof(NodeStiffnessAttributeModel)).
                    Cast<NodeStiffnessAttributeModel>().First().Value;
            }
            return null;
        }
    }
}
