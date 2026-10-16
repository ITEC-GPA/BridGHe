using Rhino.Geometry;
using System;
using System.Runtime.Serialization;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class BeamForceResultModel : ResultModel
    {
        protected double _station;
        protected Vector2d _shearForce;
        protected Vector2d _bendingMoment;
        protected double _axialForce;
        protected double _torque;
        protected bool _normalizedLength;

        public double Station { get => _station; set => _station = value; }

        public double AxialForce { get => _axialForce; set => _axialForce = value; }

        public Vector2d ShearForce { get => _shearForce; set => _shearForce = value; }

        public double Vx { get => _shearForce.X; set => _shearForce.X = value; }

        public double Vy { get => _shearForce.Y; set => _shearForce.Y = value; }

        public double Torque { get => _torque; set => _torque = value; }

        public Vector2d BendingMoment { get => _bendingMoment; set => _bendingMoment = value; }

        public double Mx { get => _bendingMoment.X; set => _bendingMoment.X = value; }

        public double My { get => _bendingMoment.Y; set => _bendingMoment.Y = value; }

        public bool NormalizedLength { get => _normalizedLength; set => _normalizedLength = value; }

        public BeamForceResultModel(CaseModel caseModel, double station, double stepNum, string stepType, bool normalizedLength = false)
            : base(caseModel, stepNum, stepType)
        {
            _station = station;
            _normalizedLength = normalizedLength;
        }

        #region Serialization
        private string SERIALIZATION_RELEASE_NAME = "BeamForceResultModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected BeamForceResultModel(SerializationInfo info, StreamingContext context)
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
            _station = info.GetDouble("Station");
            _shearForce = (Vector2d)info.GetValue("ShearForce", typeof(Vector2d));
            _bendingMoment = (Vector2d)info.GetValue("BendingMoment", typeof(Vector2d));
            _axialForce = info.GetDouble("AxialForce");
            _torque = info.GetDouble("Torque");
            _normalizedLength = info.GetBoolean("NormalizedLength");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("Station", _station);
            info.AddValue("ShearForce", _shearForce, typeof(Vector2d));
            info.AddValue("BendingMoment", _bendingMoment, typeof(Vector2d));
            info.AddValue("AxialForce", _axialForce);
            info.AddValue("Torque", _torque);
            info.AddValue("NormalizedLength", _normalizedLength);
        }

        #endregion

        public override string ToString()
        {
            string text = "BridGHe Beam Results\n";
            text += $"Case Name={_case.Name}\n";
            text += $"N={_axialForce}\n";
            text += $"V2={_shearForce.X}\n";
            text += $"V3={_shearForce.Y}\n";
            text += $"T={_torque}\n";
            text += $"M2={_bendingMoment.X}\n";
            text += $"M3={_bendingMoment.Y}\n";
            text += $"Station={_station}\n";
            text += $"NormalizedLength={_normalizedLength}\n";
            return text;
        }
    }
}
