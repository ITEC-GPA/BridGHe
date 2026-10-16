using System;
using System.Drawing;
using System.Runtime.Serialization;
using Rhino.Display;
using Rhino.Geometry;
using BridGHe.Common.Helpers;
using System.Linq;
using System.Collections.Generic;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class PlateSupportAttributeModel : AttributeModel<PlateSupportAttributeModel.AttributeData>, IEquatable<PlateSupportAttributeModel>
    {
        public enum SupportedFace
        {
            Top,
            Bottom
        }

        [System.Serializable]
        public class AttributeData : IEquatable<AttributeData>
        {
            public double Stiffness { get; set; }
            public bool CompressionOnly { get; set; }
            public SupportedFace Face { get; set; }

            public AttributeData()
            {
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as AttributeData);
            }

            public bool Equals(AttributeData other)
            {
                return other != null && Stiffness == other.Stiffness && CompressionOnly == other.CompressionOnly &&
                    Face == other.Face;
            }

            public override int GetHashCode()
            {
                var hashCode = 814130121;
                hashCode = hashCode * -1521134295 + Stiffness.GetHashCode();
                hashCode = hashCode * -1521134295 + CompressionOnly.GetHashCode();
                hashCode = hashCode * -1521134295 + Face.GetHashCode();
                return hashCode;
            }
        }

        protected static bool _isVisible = true;
        protected static double _scale = 1;
        protected static Color _color = Color.Brown;

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

        public override string Description => "Plate support";

        public PlateSupportAttributeModel()
        {
        }

        public PlateSupportAttributeModel(AttributeData value)
            : base(value)
        {
        }

        public PlateSupportAttributeModel(PlateSupportAttributeModel attr)
        {
            _value = new AttributeData()
            {
                Stiffness = attr.Value.Stiffness,
                CompressionOnly = attr.Value.CompressionOnly,
                Face = attr.Value.Face
            };
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "PlateSupportAttributeModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected PlateSupportAttributeModel(SerializationInfo info, StreamingContext context)
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
                Stiffness = info.GetDouble("Stiffness"),
                CompressionOnly = info.GetBoolean("CompressionOnly"),
                Face = (SupportedFace)info.GetValue("Face", typeof(SupportedFace))
            };
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("Stiffness", _value.Stiffness);
            info.AddValue("CompressionOnly", _value.CompressionOnly);
            info.AddValue("Face", _value.Face, typeof(SupportedFace));
        }

        #endregion

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {
            if (!IsVisible)
                return;

            PlateModel plate = (PlateModel)element;
            if (plate != null)
            {
                Vector3d normal;
                normal = Vector3d.CrossProduct((plate.Points[2] - plate.Points[0]), (plate.Points[1] - plate.Points[0]));
                if (normal.SquareLength > 0.0000001)
                {
                    normal.Unitize();
                    for (int i = 0; i < plate.Points.Count; i++)
                    {
                        Point3d p, nextP;

                        p = plate.Points[i];
                        if (i == plate.Points.Count - 1)
                        {
                            nextP = plate.Points[0];
                        }
                        else
                        {
                            nextP = plate.Points[i + 1];
                        }
                        double lr = DrawingHelper.GetScaleFactor(display.Viewport, p) * Scale;
                        display.DrawLine(p, p + lr * normal, Color);
                        display.DrawLine(p + lr * normal, nextP + lr * normal, Color);
                    }
                }
            }
        }

        public static bool operator ==(PlateSupportAttributeModel m1, PlateSupportAttributeModel m2)
        {
            if (ReferenceEquals(m1, m2))
                return true;
            return !(m1 is null) && m1.Equals(m2);
        }

        public static bool operator !=(PlateSupportAttributeModel m1, PlateSupportAttributeModel m2)
        {
            return !(m1 == m2);
        }

        public bool Equals(PlateSupportAttributeModel other)
        {
            return other != null && Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is PlateSupportAttributeModel model)
                return Equals(model);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            var hashCode = 34603;
            hashCode = hashCode * 24517 + Value.GetHashCode();
            return hashCode;
        }

        public static AttributeData GetSupport(PlateModel plate)
        {
            if (plate.HasAttribute(typeof(PlateSupportAttributeModel)))
            {
                return plate.Attributes.Where(n => n.GetType() == typeof(PlateSupportAttributeModel))
                    .Cast<PlateSupportAttributeModel>().First().Value;
            }
            return null;
        }
    }
}
