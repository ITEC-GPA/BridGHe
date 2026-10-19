using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

using System.Text;
using System.Threading.Tasks;

namespace BridGHe.Common.Models
{
    [System.Serializable]
    public class BeamOutputStationsModel : AttributeModel<BeamOutputStationsModel.AttributeData>, IEquatable<BeamOutputStationsModel>
    {
        [System.Serializable]
        public class AttributeData : IEquatable<AttributeData>
        {
            public int MinimumNumberOfStations { get; set; }

            public AttributeData(int minimumNumberOfStations)
            {
                MinimumNumberOfStations = minimumNumberOfStations;
            }

            public AttributeData()
            {
                MinimumNumberOfStations = 3;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as AttributeData);
            }

            public override int GetHashCode()
            {
                int hashCode = -23;
                hashCode = hashCode * -23 + MinimumNumberOfStations.GetHashCode();
                return hashCode;
            }

            public bool Equals(AttributeData other)
            {
                if (other == null)
                    return false;
                if (MinimumNumberOfStations != other.MinimumNumberOfStations)
                    return false;

                return true;
            }
        }

        public BeamOutputStationsModel(AttributeData attributeData)
            : base(attributeData)
        {
        }

        public BeamOutputStationsModel(BeamOutputStationsModel beamModifiersModel)
        {
            _value = new AttributeData()
            {
                MinimumNumberOfStations = beamModifiersModel.Value.MinimumNumberOfStations,
            };
        }

        private readonly string SERIALIZATION_RELEASE_NAME = "SERIALIZATION_RELEASE_BeamOutputStationsModel"; //do not modify the name!
        private readonly int SERIALIZATION_RELEASE = 2;

        protected BeamOutputStationsModel(SerializationInfo info, StreamingContext context)
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
                MinimumNumberOfStations = info.GetInt32("MinimumNumberOfStations"),
            };
        }
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("MinimumNumberOfStations", _value.MinimumNumberOfStations);
        }

        public bool Equals(BeamOutputStationsModel other)
        {
            return !(other is null) && Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is BeamOutputStationsModel br)
                return Equals(br);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = 23;
                hashCode = hashCode * 23 + Value.GetHashCode();

                return hashCode;
            }
        }

        public override string Description => "Beam Modifiers";

        public static AttributeData GetModifiers(BeamModel beam)
        {
            if (beam.HasAttribute(typeof(BeamOutputStationsModel)))
            {
                IEnumerable<AttributeModel> attr = beam.Attributes.
                    Where(n => n.GetType() == typeof(BeamOutputStationsModel));
                if (attr.Count() > 0)
                    return attr.Cast<BeamOutputStationsModel>().First().Value;
            }
            return null;
        }
    }
}
