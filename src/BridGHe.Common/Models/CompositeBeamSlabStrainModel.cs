using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.Serialization;
using Rhino.Display;
using Rhino.Geometry;
using BridGHe.Common.Helpers;

namespace BridGHe.Common.Models
{
    [Serializable]
    public class CompositeBeamSlabStrainModel : LoadModel<CompositeBeamSlabStrainModel.LoadData>, IEquatable<CompositeBeamSlabStrainModel>
    {
        [System.Serializable]
        public class LoadData : IEquatable<LoadData>
        {
            public double Strain { get; set; }

            public LoadData()
            {

            }

            public override bool Equals(object obj)
            {
                return Equals(obj as LoadData);
            }

            public bool Equals(LoadData other)
            {
                return other != null &&
                    Strain == other.Strain;
            }

            public override int GetHashCode()
            {
                var hashCode = 1147610325;
                hashCode = hashCode * -1521134295 + Strain.GetHashCode();
                return hashCode;
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

        public override string Description => "Composite Beam Slab Strain Load";


        public CompositeBeamSlabStrainModel()
        {
        }

        public CompositeBeamSlabStrainModel(LoadCaseModel loadCase, LoadData value)
            : base(loadCase, value)
        {

        }

        public CompositeBeamSlabStrainModel(CompositeBeamSlabStrainModel load)
        {
            _loadCase = load._loadCase;
            _value = new LoadData()
            {
                Strain = load._value.Strain,
            };
        }

        #region Serialization 

        private string SERIALIZATION_RELEASE_NAME = "CompositeBeamSlabStrainModel_SERIALIZATION_RELEASE"; //do not modify the name!
        private int SERIALIZATION_RELEASE = 1;
        protected CompositeBeamSlabStrainModel(SerializationInfo info, StreamingContext context)
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
            _value = new LoadData
            {
                Strain = info.GetDouble("Strain"),
            };
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue(SERIALIZATION_RELEASE_NAME, SERIALIZATION_RELEASE);
            info.AddValue("Strain", _value.Strain);
        }

        #endregion

        public static bool operator ==(CompositeBeamSlabStrainModel dl1, CompositeBeamSlabStrainModel dl2)
        {
            if (ReferenceEquals(dl1, dl2))
                return true;
            if (dl1 is null)
                return false;
            return dl1.Equals(dl2);
        }

        public static bool operator !=(CompositeBeamSlabStrainModel dl1, CompositeBeamSlabStrainModel dl2)
        {
            return !(dl1 == dl2);
        }

        public bool Equals(CompositeBeamSlabStrainModel other)
        {
            return !(other is null) && LoadCase == other.LoadCase &&
                Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            if (obj is CompositeBeamSlabStrainModel dl)
                return Equals(dl);
            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            var hashCode = 71987660;
            hashCode = hashCode * 190471339 + LoadCase.GetHashCode();
            hashCode = hashCode * 190471339 + Value.GetHashCode();

            return hashCode;
        }

        public override string ToString()
        {
            string text = "BridGHe Slab Stain load: ";
            text += $"Load Case: {_loadCase}, ";
            text += $"Strain={_value.Strain}";
            return text;
        }
    }
}
