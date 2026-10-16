using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class PlateForceResultModel : ResultModel
    {
        protected Vector3d _force;
        protected double _forceMax;
        protected double _forceMin;
        protected double _forceAngle;
        protected double _forceVonMises;
        protected Vector3d _moment;
        protected double _momentMax;
        protected double _momentMin;
        protected double _momentAngle;
        protected Vector2d _transverseShearForce;
        protected double _transverseShearForceMax;
        protected double _transverseShearForceAng;
        protected string _nodeId;

        /// <summary>
        /// The internal force per length (X = F11 direct force, Y = F22 direct force, Z = F12 shear force)
        /// </summary>
        public Vector3d Force
        {
            get => _force;
            set => _force = value;
        }

        /// <summary>
        /// The maximum principal force per length
        /// </summary>
        public double ForceMax
        {
            get => _forceMax;
            set => _forceMax = value;
        }

        /// <summary>
        /// The minimum principal force per length
        /// </summary>
        public double ForceMin
        {
            get => _forceMin;
            set => _forceMin = value;
        }

        /// <summary>
        /// The angle from the area local 1 axis to the direction of the maximum principal force
        /// </summary>
        public double ForceAngle
        {
            get => _forceAngle;
            set => _forceAngle = value;
        }

        public double ForceVonMises
        {
            get => _forceVonMises;
            set => _forceVonMises = value;
        }

        /// <summary>
        /// The internal moment per length (X = M11 bending moment, Y = M22 bending moment, Z = M12 twisting moment)
        /// </summary>
        public Vector3d Moment
        {
            get => _moment;
            set => _moment = value;
        }

        /// <summary>
        /// The maximum principal moment per length
        /// </summary>
        public double MomentMax
        {
            get => _momentMax;
            set => _momentMax = value;
        }

        /// <summary>
        /// The minimum principal moment per length
        /// </summary>
        public double MomentMin
        {
            get => _momentMin;
            set => _momentMin = value;
        }

        /// <summary>
        /// The angle from the area local 1 axis to the direction of the maximum principal moment
        /// </summary>
        public double MomentAngle
        {
            get => _momentAngle;
            set => _momentAngle = value;
        }

        /// <summary>
        /// The transverse shear force per length (X = V13, Y = V23)
        /// </summary>
        public Vector2d TransverseShearForce
        {
            get => _transverseShearForce;
            set => _transverseShearForce = value;
        }

        public double TransverseShearForceMax
        {
            get => _transverseShearForceMax;
            set => _transverseShearForceMax = value;
        }

        public double TransverseShearForceAng
        {
            get => _transverseShearForceAng;
            set => _transverseShearForceAng = value;
        }

        public string NodeId
        {
            get => _nodeId;
            set => _nodeId = value;
        }

        public PlateForceResultModel(CaseModel caseModel, string nodeId, double stepNum, string stepType)
                    : base(caseModel, stepNum, stepType)
        {
            _nodeId = nodeId;
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "PlateForceResultModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected PlateForceResultModel(SerializationInfo info, StreamingContext context)
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
            _force = (Vector3d)info.GetValue("_force", typeof(Vector3d));
            _forceMax = info.GetDouble("_forceMax");
            _forceMin = info.GetDouble("_forceMin");
            _forceAngle = info.GetDouble("_forceAngle");
            _forceVonMises = info.GetDouble("_forceVonMises");
            _moment = (Vector3d)info.GetValue("_moment", typeof(Vector3d));
            _momentMax = info.GetDouble("_momentMax");
            _momentMin = info.GetDouble("_momentMin");
            _momentAngle = info.GetDouble("_momentAngle");
            _transverseShearForce = (Vector2d)info.GetValue("_transverseShearForce", typeof(Vector2d));
            _transverseShearForceMax = info.GetDouble("_transverseShearForceMax");
            _transverseShearForceAng = info.GetDouble("_transverseShearForceAng");
            _nodeId = info.GetString("_nodeId");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("_force", _force, typeof(Vector3d));
            info.AddValue("_forceMax", _forceMax);
            info.AddValue("_forceMin", _forceMin);
            info.AddValue("_forceAngle", _forceAngle);
            info.AddValue("_forceVonMises", _forceVonMises);
            info.AddValue("_moment", _moment, typeof(Vector3d));
            info.AddValue("_momentMax", _momentMax);
            info.AddValue("_momentMin", _momentMin);
            info.AddValue("_momentAngle", _momentAngle);
            info.AddValue("_transverseShearForce", _transverseShearForce, typeof(Vector2d));
            info.AddValue("_transverseShearForceMax", _transverseShearForceMax);
            info.AddValue("_transverseShearForceAng", _transverseShearForceAng);
            info.AddValue("_nodeId", _nodeId);
        }

        #endregion

        public override string ToString()
        {
            string text = "BridGHe Plate Results";
            return text;
        }
    }
}
