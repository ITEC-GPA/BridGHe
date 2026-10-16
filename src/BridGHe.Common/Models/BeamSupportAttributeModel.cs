using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using BridGHe.Common.Helpers;
using Rhino.Display;
using Rhino.Geometry;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class BeamSupportAttributeModel : AttributeModel<BeamSupportAttributeModel.AttributeData>, IEquatable<BeamSupportAttributeModel>
    {
        public enum SpringType
        {
            [Description("Simple")]
            Simple = 1,
            [Description("Link property")]
            LinkProperty = 2,
        }

        public enum CoordinateSystem
        {
            [Description("Global")]
            Global = 1,
            [Description("Local")]
            Local = 2,
        }

        public enum SimpleSpringTypes
        {
            [Description("1 - Tension Compression")]
            TensionCompression = 1,
            [Description("2 - Compression only")]
            CompressionOnly = 2,
            [Description("3 - Tension only")]
            TensionOnly = 3,
        }

        public enum SpringTensionOrientation
        {
            [Description("1 - Beam local axis")]
            BeamLocalAxis = 1,
            [Description("2 - Beam object 1-2 plane")]
            BeamObject12Plane = 2,
            [Description("3 - Custom vector")]
            CustomVector = 3,
        }

        public enum LocalAxisDirection
        {
            [Description("-3 Axis")]
            negative3Axis = 1,
            [Description("-2 Axis")]
            negative2Axis = 2,
            [Description("-1 Axis")]
            negative1Axis = 3,
            [Description("3 Axis")]
            positive3Axis = 4,
            [Description("2 Axis")]
            positive2Axis = 5,
            [Description("1 Axis")]
            positive1Axis = 6,
        }


        public override string Description => "Beam Linear Support";

        public BeamSupportAttributeModel()
        {
        }

        public BeamSupportAttributeModel(AttributeData value)
            : base(value)
        {
        }

        public BeamSupportAttributeModel(BeamSupportAttributeModel attr)
        {
            _value = new AttributeData()
            {
                SpringType = attr.Value.SpringType,
                SpringTensionOrientation = attr.Value.SpringTensionOrientation,
                LocalAxisDirection = attr.Value.LocalAxisDirection,
                SimpleSpringType = attr.Value.SimpleSpringType,
                LinkProperty = attr.Value.LinkProperty,
                Stiffness = attr.Value.Stiffness,
                LocalAxis2Angle = attr.Value.LocalAxis2Angle,
                Plane12Angle = attr.Value.Plane12Angle,
                CoordinateSystem = attr.Value.CoordinateSystem,
                CustomDirection = attr.Value.CustomDirection,
            };

        }


        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "BeamSupportAttributeModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 2;

        protected BeamSupportAttributeModel(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            _value = new AttributeData()
            {
                SpringType = (BeamSupportAttributeModel.SpringType)info.GetValue("SpringType", typeof(BeamSupportAttributeModel.SpringType)),
                SpringTensionOrientation = (BeamSupportAttributeModel.SpringTensionOrientation)info.GetValue("SpringTensionOrientation", typeof(BeamSupportAttributeModel.SpringTensionOrientation)),
                LocalAxisDirection = (BeamSupportAttributeModel.LocalAxisDirection)info.GetValue("LocalAxisDirection", typeof(BeamSupportAttributeModel.LocalAxisDirection)),
                SimpleSpringType = (BeamSupportAttributeModel.SimpleSpringTypes)info.GetValue("SimpleSpringTypes", typeof(BeamSupportAttributeModel.SimpleSpringTypes)),
                LinkProperty = (object)info.GetValue("Property", typeof(object)),
                Stiffness = info.GetDouble("Stiffness"),
                LocalAxis2Angle = info.GetDouble("LocalAxis2Angle"),
                Plane12Angle = info.GetDouble("Plane12Angle"),
                CoordinateSystem = (BeamSupportAttributeModel.CoordinateSystem)info.GetValue("CoordinateSystem", typeof(BeamSupportAttributeModel.CoordinateSystem)),
                CustomDirection = (Vector3d)info.GetValue("Vector3d", typeof(Vector3d)),

            };
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);

            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);

            info.AddValue("SpringType", _value.SpringType);
            info.AddValue("SpringTensionOrientation", _value.SpringTensionOrientation);
            info.AddValue("LocalAxisDirection", _value.LocalAxisDirection);
            info.AddValue("SimpleSpringTypes", _value.SimpleSpringType);
            info.AddValue("Property", _value.LinkProperty);
            info.AddValue("Stiffness", _value.Stiffness);
            info.AddValue("LocalAxis2Angle", _value.LocalAxis2Angle);
            info.AddValue("Plane12Angle", _value.Plane12Angle);
            info.AddValue("CoordinateSystem", _value.CoordinateSystem);
            info.AddValue("Vector3d", _value.CustomDirection);

        }

        #endregion

        public static bool operator ==(BeamSupportAttributeModel bt1, BeamSupportAttributeModel bt2)
        {
            if (ReferenceEquals(bt1, bt2))
                return true;
            if (bt1 is null)
                return false;
            return bt1.Equals(bt2);
        }

        public static bool operator !=(BeamSupportAttributeModel bt1, BeamSupportAttributeModel bt2)
        {
            return !(bt1 == bt2);
        }

        public bool Equals(BeamSupportAttributeModel other)
        {
            return !(other is null) && Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is BeamSupportAttributeModel bt)
                return Equals(bt);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return -19 * Value.GetHashCode();
            }
        }

        public static AttributeData GetSupport(BeamModel beam)
        {
            if (beam.HasAttribute(typeof(BeamSupportAttributeModel)))
            {
                IEnumerable<AttributeModel> attr = beam.Attributes.
                    Where(n => n.GetType() == typeof(BeamSupportAttributeModel));
                if (attr.Count() > 0)
                    return attr.Cast<BeamSupportAttributeModel>().First().Value;
            }
            return null;
        }

        public override string ToString()
        {
            string description = String.Empty;
            if (_value.SpringType == SpringType.Simple)
            {
                description += $"Stiffness: {_value.Stiffness}; Type: {_value.SimpleSpringType.GetDescription()}; ";
            }
            else if (_value.SpringType == SpringType.LinkProperty)
            {
                description += $"Link property: {_value.LinkProperty.ToString()}; {_value.LocalAxis2Angle} deg; ";
            }
            else
            {
                throw new NotSupportedException();
            }


            if (_value.SpringTensionOrientation == SpringTensionOrientation.BeamLocalAxis)
            {
                description += $"Axis: {_value.LocalAxisDirection.GetDescription()} ";
            }
            else if (_value.SpringTensionOrientation == SpringTensionOrientation.BeamObject12Plane)
            {
                description += $"Angle +1Axis: {_value.Plane12Angle} deg ";
            }
            else if (_value.SpringTensionOrientation == SpringTensionOrientation.CustomVector)
            {
                description += $"Coordinate system: {_value.CoordinateSystem}; ";
                description += $"Vector: {_value.CustomDirection.ToString()} ";
            }
            else
            {
                throw new NotSupportedException();
            }

            return description;
        }

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {

            base.Draw(display, viewport, color, element);
        }


        #region Nested class
        [System.Serializable]
        public class AttributeData : IEquatable<AttributeData>
        {
            public BeamSupportAttributeModel.SpringType SpringType { get; set; }

            /// <summary>
            /// Stiffness of spring only for <see cref="BeamSupportAttributeModel.SpringType.Simple"/>
            /// </summary>
            public double Stiffness { get; set; }

            public BeamSupportAttributeModel.SimpleSpringTypes SimpleSpringType { get; set; }

            public object LinkProperty { get; set; }

            public BeamSupportAttributeModel.LocalAxisDirection LocalAxisDirection { get; set; }

            public BeamSupportAttributeModel.SpringTensionOrientation SpringTensionOrientation { get; set; }

            /// <summary>
            /// measured clockwise
            /// </summary>
            public double Plane12Angle { get; set; }

            public Vector3d CustomDirection { get; set; }

            /// <summary>
            /// This is the angle that the link local 2 - axis is rotated from its default orientation.This item applies only when <see cref="BeamSupportAttributeModel.SpringType.LinkProperty"/>. [deg]
            /// </summary>
            public double LocalAxis2Angle { get; set; }

            public BeamSupportAttributeModel.CoordinateSystem CoordinateSystem { get; set; }



            public AttributeData()
            {
                CustomDirection = Vector3d.Unset;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as AttributeData);
            }

            public bool Equals(AttributeData other)
            {

                if (ReferenceEquals(this, other))
                    return true;

                return !(other is null) && other.SpringType.Equals(SpringType)
                                        && other.Stiffness.Equals(Stiffness)
                                        && other.SimpleSpringType.Equals(SimpleSpringType)
                                        && other.LinkProperty.Equals(LinkProperty)
                                        && other.SpringTensionOrientation.Equals(SpringTensionOrientation)
                                        && other.CustomDirection.Equals(CustomDirection)
                                        && other.LocalAxis2Angle.Equals(LocalAxis2Angle)
                                        && other.LocalAxisDirection.Equals(LocalAxisDirection)
                                        && other.Plane12Angle.Equals(Plane12Angle)
                                        && other.CoordinateSystem.Equals(CoordinateSystem)
                                        && base.Equals(other);

            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return base.GetHashCode();
                }
            }
        }

        #endregion

    }
}
