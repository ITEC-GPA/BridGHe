using System;
using System.Linq;
using System.Drawing;
using System.Runtime.Serialization;
using Rhino.Display;
using Rhino.Geometry;
using System.Collections.Generic;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class BeamStringGroupAttributeModel : AttributeModel<BeamStringGroupAttributeModel.AttributeData>,
        IEquatable<BeamStringGroupAttributeModel>
    {
        [System.Serializable]
        public class AttributeData : IEquatable<AttributeData>
        {
            public int ID { get; set; }

            public AttributeData()
            {

            }

            public override bool Equals(object obj)
            {
                return Equals(obj as AttributeData);
            }

            public bool Equals(AttributeData other)
            {
                if (other == null)
                    return false;

                if (ID.Equals(other.ID))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public override int GetHashCode()
            {
                return 1213502048 + ID.GetHashCode();
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

        public override string Description => "Beam String Group";

        public BeamStringGroupAttributeModel()
        {
        }

        public BeamStringGroupAttributeModel(AttributeData value)
            : base(value)
        {
        }

        public BeamStringGroupAttributeModel(BeamStringGroupAttributeModel attr)
        {
            _value = new AttributeData()
            {
                ID = attr._value.ID
            };
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "BeamStringGroupAttributeModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected BeamStringGroupAttributeModel(SerializationInfo info, StreamingContext context)
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
                ID = (int)info.GetValue("ID", typeof(int))
            };
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("ID", _value.ID, typeof(int));
        }

        #endregion

        public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
        {

        }

        public static bool operator ==(BeamStringGroupAttributeModel br1, BeamStringGroupAttributeModel br2)
        {
            if (ReferenceEquals(br1, br2))
                return true;
            if (br1 is null)
                return false;
            return br1.Equals(br2);
        }

        public static bool operator !=(BeamStringGroupAttributeModel br1, BeamStringGroupAttributeModel br2)
        {
            return !(br1 == br2);
        }

        public bool Equals(BeamStringGroupAttributeModel other)
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
            return -1939223833 + _value.GetHashCode();
        }

        public override string ToString()
        {
            string text = "BridGHe String Group attribute: ID = " + _value.ID;

            return text;
        }

        public static AttributeData GetStringGroup(BeamModel beam)
        {
            if (beam.HasAttribute(typeof(BeamStringGroupAttributeModel)))
            {
                IEnumerable<AttributeModel> attr = beam.Attributes.
                    Where(n => n.GetType() == typeof(BeamStringGroupAttributeModel));
                if (attr.Count() > 0)
                    return attr.Cast<BeamStringGroupAttributeModel>().First().Value;
            }
            return null;
        }
    }
}
