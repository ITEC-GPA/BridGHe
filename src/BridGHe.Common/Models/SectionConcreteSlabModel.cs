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
        protected double _bottomCover = 50;
        protected double _bottomRebarArea;

        public double Height { get => _height; set { _height = value; } }

        public double Width { get => _width; set { _width = value; } }

        public double TopCover { get => _topCover; set { _topCover = value; } }

        public double TopRebarArea { get => _topRebarArea; set { _topRebarArea = value; } }
        public double BottomCover { get => _bottomCover; set => _bottomCover = value; }
        public double BottomRebarArea { get => _bottomRebarArea; set => _bottomRebarArea = value; }
        public double TotalRebarArea => _topRebarArea + _bottomRebarArea;

        public bool IsValid => double.IsFinite(_height) && _height > 0 && double.IsFinite(_width) && _width > 0
            && double.IsFinite(_topRebarArea) && _topRebarArea >= 0 && double.IsFinite(_bottomRebarArea) && _bottomRebarArea >= 0
            && double.IsFinite(_topCover) && double.IsFinite(_bottomCover)
            && (_topRebarArea == 0 || (_topCover > 0 && _topCover < _height))
            && (_bottomRebarArea == 0 || (_bottomCover > 0 && _bottomCover < _height))
            && (_topRebarArea == 0 || _bottomRebarArea == 0 || _topCover + _bottomCover < _height);

        public SectionConcreteSlabModel(double height, double width, double topRebarArea, double topCover)
            : this(height, width, topRebarArea, topCover, 0, 50) { }

        public SectionConcreteSlabModel(double height, double width, double topRebarArea, double topCover,
            double bottomRebarArea, double bottomCover)
        {
            _height = height;
            _width = width;
            _topCover = topCover;
            _topRebarArea = topRebarArea;
            _bottomRebarArea = bottomRebarArea;
            _bottomCover = bottomCover;
        }


        #region Serialization

        private string SERIALIZATION_RELEASE_NAME = "SectionConcreteSlabModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 2;

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
            if (savedRelease >= 2)
            {
                _bottomRebarArea = info.GetDouble("_bottomRebarArea");
                _bottomCover = info.GetDouble("_bottomCover");
            }
        }

        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("_height", _height);
            info.AddValue("_width", _width);
            info.AddValue("_topCover", _topCover);
            info.AddValue("_topRebarArea", _topRebarArea);
            info.AddValue("_bottomRebarArea", _bottomRebarArea);
            info.AddValue("_bottomCover", _bottomCover);
        }

        #endregion

    }
}
