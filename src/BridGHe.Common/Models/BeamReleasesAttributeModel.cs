using Rhino.Display;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class BeamReleasesAttributeModel : AttributeModel<BeamReleasesAttributeModel.AttributeData>, IEquatable<BeamReleasesAttributeModel>
    {
        [System.Serializable]
        public class AttributeData : IEquatable<AttributeData>
        {
            public bool[] TransNode1 { get; set; }
            public bool[] TransNode2 { get; set; }
            public bool[] RotNode1 { get; set; }
            public bool[] RotNode2 { get; set; }

            public Vector3d TransN1Partial { get; set; }
            public Vector3d TransN2Partial { get; set; }
            public Vector3d RotN1Partial { get; set; }
            public Vector3d RotN2Partial { get; set; }

            public AttributeData()
            {
                TransNode1 = new bool[3];
                TransNode2 = new bool[3];
                RotNode1 = new bool[3];
                RotNode2 = new bool[3];
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as AttributeData);
            }

            public bool Equals(AttributeData other)
            {
                if (other == null)
                    return false;
                for (int i = 0; i < 3; i++)
                {
                    if (TransNode1[i] != other.TransNode1[i])
                        return false;
                    if (TransNode2[i] != other.TransNode2[i])
                        return false;
                    if (RotNode1[i] != other.RotNode1[i])
                        return false;
                    if (RotNode2[i] != other.RotNode2[i])
                        return false;
                }
                if (!TransN1Partial.Equals(other.TransN1Partial))
                    return false;
                if (!TransN2Partial.Equals(other.TransN2Partial))
                    return false;
                if (!RotN1Partial.Equals(other.RotN1Partial))
                    return false;
                if (!RotN2Partial.Equals(other.RotN2Partial))
                    return false;
                return true;
            }

            public override int GetHashCode()
            {
                var hashCode = 559903368;
                hashCode = hashCode * -1521134295 + EqualityComparer<bool[]>.Default.GetHashCode(TransNode1);
                hashCode = hashCode * -1521134295 + EqualityComparer<bool[]>.Default.GetHashCode(TransNode2);
                hashCode = hashCode * -1521134295 + EqualityComparer<bool[]>.Default.GetHashCode(RotNode1);
                hashCode = hashCode * -1521134295 + EqualityComparer<bool[]>.Default.GetHashCode(RotNode2);
                hashCode = hashCode * -1521134295 + EqualityComparer<Vector3d>.Default.GetHashCode(TransN1Partial);
                hashCode = hashCode * -1521134295 + EqualityComparer<Vector3d>.Default.GetHashCode(TransN2Partial);
                hashCode = hashCode * -1521134295 + EqualityComparer<Vector3d>.Default.GetHashCode(RotN1Partial);
                hashCode = hashCode * -1521134295 + EqualityComparer<Vector3d>.Default.GetHashCode(RotN2Partial);
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

        public override string Description => "Beam Releases";

        public BeamReleasesAttributeModel()
        {
        }

        public BeamReleasesAttributeModel(AttributeData value)
            : base(value)
        {
        }

        public BeamReleasesAttributeModel(BeamReleasesAttributeModel attr)
        {
            _value = new AttributeData()
            {
                TransN1Partial = new Vector3d(attr._value.TransN1Partial),
                TransN2Partial = new Vector3d(attr._value.TransN2Partial),
                RotN1Partial = new Vector3d(attr._value.RotN1Partial),
                RotN2Partial = new Vector3d(attr._value.RotN2Partial)
            };
            for (int i = 0; i < _value.TransNode1.Length; i++)
                _value.TransNode1[i] = attr._value.TransNode1[i];
            for (int i = 0; i < _value.TransNode2.Length; i++)
                _value.TransNode2[i] = attr._value.TransNode2[i];
            for (int i = 0; i < _value.RotNode1.Length; i++)
                _value.RotNode1[i] = attr._value.RotNode1[i];
            for (int i = 0; i < _value.RotNode2.Length; i++)
                _value.RotNode2[i] = attr._value.RotNode2[i];
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "BeamReleasesAttributeModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected BeamReleasesAttributeModel(SerializationInfo info, StreamingContext context)
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
                TransN1Partial = (Vector3d)info.GetValue("TransN1Partial", typeof(Vector3d)),
                TransN2Partial = (Vector3d)info.GetValue("TransN2Partial", typeof(Vector3d)),
                RotN1Partial = (Vector3d)info.GetValue("RotN1Partial", typeof(Vector3d)),
                RotN2Partial = (Vector3d)info.GetValue("RotN2Partial", typeof(Vector3d))
            };
            for (int i = 0; i < _value.TransNode1.Length; i++)
                _value.TransNode1[i] = info.GetBoolean($"TransNode1{i}");
            for (int i = 0; i < _value.TransNode2.Length; i++)
                _value.TransNode2[i] = info.GetBoolean($"TransNode2{i}");
            for (int i = 0; i < _value.RotNode1.Length; i++)
                _value.RotNode1[i] = info.GetBoolean($"RotNode1{i}");
            for (int i = 0; i < _value.RotNode2.Length; i++)
                _value.RotNode2[i] = info.GetBoolean($"RotNode2{i}");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("TransN1Partial", _value.TransN1Partial, typeof(Vector3d));
            info.AddValue("TransN2Partial", _value.TransN2Partial, typeof(Vector3d));
            info.AddValue("RotN1Partial", _value.RotN1Partial, typeof(Vector3d));
            info.AddValue("RotN2Partial", _value.RotN2Partial, typeof(Vector3d));
            for (int i = 0; i < _value.TransNode1.Length; i++)
                info.AddValue($"TransNode1{i}", _value.TransNode1[i]);
            for (int i = 0; i < _value.TransNode2.Length; i++)
                info.AddValue($"TransNode2{i}", _value.TransNode2[i]);
            for (int i = 0; i < _value.RotNode1.Length; i++)
                info.AddValue($"RotNode1{i}", _value.RotNode1[i]);
            for (int i = 0; i < _value.RotNode2.Length; i++)
                info.AddValue($"RotNode2{i}", _value.RotNode2[i]);
        }

        #endregion

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {
            if (!IsVisible)
                return;

            BeamModel beam = (BeamModel)element;
            if (beam != null)
            {
                Vector3d vec = beam.PointTo - beam.PointFrom;
                Vector3d uvec_h = new Vector3d(vec);
                double len = uvec_h.Length;
                uvec_h.Unitize();

                if (_value.TransNode1.Count(b => b == true) > 0 || _value.RotNode1.Count(b => b == true) > 0)
                {
                    Plane p = new Plane(beam.PointFrom + uvec_h * (len * 0.1 * Scale), uvec_h);
                    Circle c = new Circle(p, len * 0.1 * Scale);
                    display.DrawCircle(c, Color);
                    c.Translate(uvec_h * len * 0.01 * Scale);
                    display.DrawCircle(c, Color);
                }

                if (_value.TransNode2.Count(b => b == true) > 0 || _value.RotNode2.Count(b => b == true) > 0)
                {
                    Plane p = new Plane(beam.PointTo - uvec_h * (len * 0.1 * Scale), uvec_h);
                    Circle c = new Circle(p, len * 0.1 * Scale);
                    display.DrawCircle(c, Color);
                    c.Translate(-uvec_h * len * 0.01 * Scale);
                    display.DrawCircle(c, Color);
                }
            }
        }

        public static bool operator ==(BeamReleasesAttributeModel br1, BeamReleasesAttributeModel br2)
        {
            if (ReferenceEquals(br1, br2))
                return true;
            if (br1 is null)
                return false;
            return br1.Equals(br2);
        }

        public static bool operator !=(BeamReleasesAttributeModel br1, BeamReleasesAttributeModel br2)
        {
            return !(br1 == br2);
        }

        public bool Equals(BeamReleasesAttributeModel other)
        {
            return !(other is null) && Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is BeamReleasesAttributeModel br)
                return Equals(br);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            var hashCode = 6512171;
            hashCode = hashCode * 115409737 + Value.GetHashCode();

            return hashCode;
        }

        public override string ToString()
        {
            string text = "BridGHe End Release attribute: N1= ";
            List<string> relNode1 = new List<string>();
            if (_value.TransNode1[0])
                relNode1.Add("V1"); //TX
            if (_value.TransNode1[1])
                relNode1.Add("V2"); //TY
            if (_value.TransNode1[2])
                relNode1.Add("P1"); //TZ
            if (_value.RotNode1[0])
                relNode1.Add("M1"); //RX
            if (_value.RotNode1[1])
                relNode1.Add("M2"); //RY
            if (_value.RotNode1[2])
                relNode1.Add("T1"); //RZ

            text += string.Join(", ", relNode1.ToArray()) + " ; N2= ";

            List<string> relNode2 = new List<string>();
            if (_value.TransNode2[0])
                relNode2.Add("V1"); //TX
            if (_value.TransNode2[1])
                relNode2.Add("V2"); //TY
            if (_value.TransNode2[2])
                relNode2.Add("P2"); //TZ
            if (_value.RotNode2[0])
                relNode2.Add("M1"); //RX
            if (_value.RotNode2[1])
                relNode2.Add("M2"); //RY
            if (_value.RotNode2[2])
                relNode2.Add("T2"); //RZ

            text += string.Join(", ", relNode2.ToArray());

            return text;
        }

        public static AttributeData GetRelease(BeamModel beam)
        {
            if (beam.HasAttribute(typeof(BeamReleasesAttributeModel)))
            {
                IEnumerable<AttributeModel> attr = beam.Attributes.
                    Where(n => n.GetType() == typeof(BeamReleasesAttributeModel));
                if (attr.Count() > 0)
                    return attr.Cast<BeamReleasesAttributeModel>().First().Value;
            }
            return null;
        }
    }
}
