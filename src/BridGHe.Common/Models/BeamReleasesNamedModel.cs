using System;
using System.Drawing;
using System.Runtime.Serialization;
using Rhino.Display;
using Rhino.Geometry;
using BridGHe.Common.Helpers;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class BeamReleasesNamedModel : NamedSetsModel
    {
        protected bool[] _transNode1;
        protected bool[] _transNode2;
        protected bool[] _rotNode1;
        protected bool[] _rotNode2;
        protected Vector3d _transN1Partial;
        protected Vector3d _transN2Partial;
        protected Vector3d _rotN1Partial;
        protected Vector3d _rotN2Partial;

        public bool[] TransNode1
        {
            get => _transNode1;
            set => _transNode1 = value;
        }

        public bool[] TransNode2
        {
            get => _transNode2;
            set => _transNode2 = value;
        }

        public bool[] RotNode1
        {
            get => _rotNode1;
            set => _rotNode1 = value;
        }

        public bool[] RotNode2
        {
            get => _rotNode2;
            set => _rotNode2 = value;
        }

        public Vector3d TransN1Partial
        {
            get => _transN1Partial;
            set => _transN1Partial = value;
        }

        public Vector3d TransN2Partial
        {
            get => _transN2Partial;
            set => _transN2Partial = value;
        }

        public Vector3d RotN1Partial
        {
            get => _rotN1Partial;
            set => _rotN1Partial = value;
        }

        public Vector3d RotN2Partial
        {
            get => _rotN2Partial;
            set => _rotN2Partial = value;
        }

        public BeamReleasesNamedModel(string name,
                                      bool[] transNode1,
                                      bool[] transNode2,
                                      bool[] rotNode1,
                                      bool[] rotNode2,
                                      Vector3d transN1Partial,
                                      Vector3d transN2Partial,
                                      Vector3d rotN1Partial,
                                      Vector3d rotN2Partial) : base(name)
        {
            _transNode1 = new bool[3];
            _transNode2 = new bool[3];
            _rotNode1 = new bool[3];
            _rotNode2 = new bool[3];
            for (int i = 0; i < 3; i++)
            {
                _transNode1[i] = transNode1[i];
                _transNode2[i] = transNode2[i];
                _rotNode1[i] = rotNode1[i];
                _rotNode2[i] = rotNode2[i];
            }
            _transN1Partial = new Vector3d(transN1Partial);
            _transN2Partial = new Vector3d(transN2Partial);
            _rotN1Partial = new Vector3d(rotN1Partial);
            _rotN2Partial = new Vector3d(rotN2Partial);
        }

        public BeamReleasesNamedModel(BeamReleasesNamedModel other) : base(other.Name)
        {
            _transNode1 = new bool[3];
            _transNode2 = new bool[3];
            _rotNode1 = new bool[3];
            _rotNode2 = new bool[3];
            for (int i = 0; i < 3; i++)
            {
                _transNode1[i] = other._transNode1[i];
                _transNode2[i] = other._transNode2[i];
                _rotNode1[i] = other._rotNode1[i];
                _rotNode2[i] = other._rotNode2[i];
            }

            _transN1Partial = new Vector3d(other._transN1Partial);
            _transN2Partial = new Vector3d(other._transN2Partial);
            _rotN1Partial = new Vector3d(other._rotN1Partial);
            _rotN2Partial = new Vector3d(other._rotN2Partial);
        }

        public override NamedSetsModel Clone()
        {
            return new BeamReleasesNamedModel(this);
        }


        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "BeamReleasesNamedModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected BeamReleasesNamedModel(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            int savedRelease;
            savedRelease = info.GetInt32(SERIALIZATION_RELEASE_NAME);

            TransN1Partial = (Vector3d)info.GetValue("TransN1Partial", typeof(Vector3d));
            TransN2Partial = (Vector3d)info.GetValue("TransN2Partial", typeof(Vector3d));
            RotN1Partial = (Vector3d)info.GetValue("RotN1Partial", typeof(Vector3d));
            RotN2Partial = (Vector3d)info.GetValue("RotN2Partial", typeof(Vector3d));
            for (int i = 0; i < TransNode1.Length; i++)
                TransNode1[i] = info.GetBoolean($"TransNode1{i}");
            for (int i = 0; i < TransNode2.Length; i++)
                TransNode2[i] = info.GetBoolean($"TransNode2{i}");
            for (int i = 0; i < RotNode1.Length; i++)
                RotNode1[i] = info.GetBoolean($"RotNode1{i}");
            for (int i = 0; i < RotNode2.Length; i++)
                RotNode2[i] = info.GetBoolean($"RotNode2{i}");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("TransN1Partial", TransN1Partial, typeof(Vector3d));
            info.AddValue("TransN2Partial", TransN2Partial, typeof(Vector3d));
            info.AddValue("RotN1Partial", RotN1Partial, typeof(Vector3d));
            info.AddValue("RotN2Partial", RotN2Partial, typeof(Vector3d));
            for (int i = 0; i < TransNode1.Length; i++)
                info.AddValue($"TransNode1{i}", TransNode1[i]);
            for (int i = 0; i < TransNode2.Length; i++)
                info.AddValue($"TransNode2{i}", TransNode2[i]);
            for (int i = 0; i < RotNode1.Length; i++)
                info.AddValue($"RotNode1{i}", RotNode1[i]);
            for (int i = 0; i < RotNode2.Length; i++)
                info.AddValue($"RotNode2{i}", RotNode2[i]);
        }

        #endregion

    }
}
