using System;
using System.Runtime.Serialization;

using Rhino.Geometry;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class NodeDisplResultModel : ResultModel
    {
        protected Vector3d _displacement;
        protected Vector3d _rotation;

        public Vector3d Displacement
        {
            get => _displacement;
            set => _displacement = value;
        }

        public Vector3d Rotation
        {
            get => _rotation;
            set => _rotation = value;
        }

        public NodeDisplResultModel(CaseModel loadCase)
            : base(loadCase, 0, "")
        {
        }

        public NodeDisplResultModel(CaseModel loadCase, double stepNum, string stepType)
            : base(loadCase, stepNum, stepType)
        {
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "NodeDisplResultModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected NodeDisplResultModel(SerializationInfo info, StreamingContext context)
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
            _displacement = (Vector3d)info.GetValue("Displacement", typeof(Vector3d));
            _rotation = (Vector3d)info.GetValue("Rotation", typeof(Vector3d));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("Displacement", _displacement, typeof(Vector3d));
            info.AddValue("Rotation", _rotation, typeof(Vector3d));
        }

        #endregion
    }
}
