using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Rhino.Geometry;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class NodeReactResultModel : ResultModel
    {
        protected Vector3d _force;
        protected Vector3d _moment;

        public Vector3d Force
        {
            get => _force;
            set => _force = value;
        }

        public Vector3d Moment
        {
            get => _moment;
            set => _moment = value;
        }

        public NodeReactResultModel(CaseModel loadCase)
            : base(loadCase, 0, "")
        {
        }

        public NodeReactResultModel(CaseModel loadCase, double stepNum, string stepType)
            : base(loadCase, stepNum, stepType)
        {
        }

        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "NodeReactResultModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected NodeReactResultModel(SerializationInfo info, StreamingContext context)
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
            _force = (Vector3d)info.GetValue("Force", typeof(Vector3d));
            _moment = (Vector3d)info.GetValue("Moment", typeof(Vector3d));
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("Force", _force, typeof(Vector3d));
            info.AddValue("Moment", _moment, typeof(Vector3d));
        }

        #endregion
    }
}
