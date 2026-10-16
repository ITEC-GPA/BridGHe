using System;
using System.Runtime.Serialization;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class BeamGradientLoadModel : LoadModel<BeamGradientLoadModel.LoadData>, IEquatable<BeamGradientLoadModel>
    {
        [System.Serializable]
        public class LoadData : IEquatable<LoadData>
        {
            public double Axis1 { get; set; }
            public double Axis2 { get; set; }

            public override bool Equals(object obj)
            {
                return Equals(obj as LoadData);
            }

            public bool Equals(LoadData other)
            {
                return other != null &&
                       Axis1 == other.Axis1 &&
                       Axis2 == other.Axis2;
            }

            public override int GetHashCode()
            {
                var hashCode = -1036728599;
                hashCode = hashCode * -1521134295 + Axis1.GetHashCode();
                hashCode = hashCode * -1521134295 + Axis2.GetHashCode();
                return hashCode;
            }
        }

        public override string Description => "Beam Gradient Load";

        public BeamGradientLoadModel()
        {
        }

        public BeamGradientLoadModel(LoadCaseModel loadCase, LoadData value)
            : base(loadCase, value)
        {
        }

        public BeamGradientLoadModel(BeamGradientLoadModel load)
        {
            _loadCase = load._loadCase;
            _value = new LoadData()
            {
                Axis1 = load._value.Axis1,
                Axis2 = load._value.Axis2
            };
        }

        #region Serialization 

        private string SERIALIZATION_RELEASE_NAME = "BeamGradientLoadModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected BeamGradientLoadModel(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            int savedRelease = info.GetInt32(SERIALIZATION_RELEASE_NAME);
            _value = new LoadData
            {
                Axis1 = info.GetDouble("Axis1"),
                Axis2 = info.GetDouble("Axis2")
            };
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("Axis1", _value.Axis1);
            info.AddValue("Axis2", _value.Axis2);
        }

        #endregion

        public static bool operator ==(BeamGradientLoadModel gl1, BeamGradientLoadModel gl2)
        {
            if (ReferenceEquals(gl1, gl2))
                return true;
            if (gl1 is null)
                return false;
            return gl1.Equals(gl2);
        }

        public static bool operator !=(BeamGradientLoadModel gl1, BeamGradientLoadModel gl2)
        {
            return !(gl1 == gl2);
        }

        public bool Equals(BeamGradientLoadModel other)
        {
            return !(other is null) && LoadCase == other.LoadCase && Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is BeamGradientLoadModel gl)
                return Equals(gl);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            var hashCode = 25635277;
            hashCode = hashCode * 177752599 + LoadCase.GetHashCode();
            hashCode = hashCode * 177752599 + Value.GetHashCode();

            return hashCode;
        }

        public override string ToString()
        {
            string text = "BridGHe Gradient load: ";
            text += $"\n - Load Case: {_loadCase}, ";
            text += $"\n - Axis 1: {_value.Axis1}, ";
            text += $"\n - Axis 2: {_value.Axis2} ";
            return text;
        }
    }
}
