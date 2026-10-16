using System;
using System.Runtime.Serialization;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class SectionConcreteSlabModel : ISerializable
    {
        protected double _height;
        protected double _width;
        protected double _topCover;
        protected double _topRebarArea;

        public double Height { get => _height; set { _height = value; } }

        public double Width { get => _width; set { _width = value; } }

        public double TopCover { get => _topCover; set { _topCover = value; } }

        public double TopRebarArea { get => _topRebarArea; set { _topRebarArea = value; } }

        public bool IsValid => _height > 0 && _width > 0;

        public SectionConcreteSlabModel(double height, double width, double topRebarArea, double topCover)
        {
            _height = height;
            _width = width;
            _topCover = topCover;
            _topRebarArea = topRebarArea;
        }


        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "SectionConcreteSlabModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;

        protected SectionConcreteSlabModel(SerializationInfo info, StreamingContext context)
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
            _height = info.GetDouble("_height");
            _width = info.GetDouble("_width");
            _topCover = info.GetDouble("_topCover");
            _topRebarArea = info.GetDouble("_topRebarArea");
        }

        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("_height", _height);
            info.AddValue("_width", _width);
            info.AddValue("_topCover", _topCover);
            info.AddValue("_topRebarArea", _topRebarArea);
        }

        #endregion

    }
}
