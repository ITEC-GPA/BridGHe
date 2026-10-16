using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_MixedSection : IGH_Goo
    {
        protected string _name = "";
        protected GH_BeamSection _concreteSection;
        protected double _reinforcementCover;
        protected double _reinforcementArea;
        protected GH_BeamSection _steelSection;
        protected double _verticalOffset;

        public string Name
        {
            get => _name;
            set => _name = value;
        }

        public GH_BeamSection ConcreteSection
        {
            get => _concreteSection;
            set => _concreteSection = value;
        }

        public double ReinforcementCover
        {
            get => _reinforcementCover;
            set => _reinforcementCover = value;
        }

        public double ReinforcementArea
        {
            get => _reinforcementArea;
            set => _reinforcementArea = value;
        }

        public double VerticalOffset
        {
            get => _verticalOffset;
            set => _verticalOffset = value;
        }

        public GH_BeamSection SteelSection
        {
            get => _steelSection;
            set => _steelSection = value;
        }

        public bool IsValid => Name != "" && _concreteSection != null && _concreteSection.IsValid &&
            _steelSection != null && _steelSection.IsValid && _reinforcementArea > 0 && _reinforcementCover > 0;

        public string IsValidWhyNot
        {
            get
            {
                if (_name == string.Empty)
                    return "Invalida name";
                if (_concreteSection == null || !_concreteSection.IsValid)
                    return "Concrete section not properly defined";
                if (_steelSection == null || !_steelSection.IsValid)
                    return "Concrete section not properly defined";
                if (_reinforcementArea <= 0)
                    return "Reinforcement area not properly defined";
                if (_reinforcementCover <= 0)
                    return "Reinforcement cover not properly defined";
                return "";
            }
        }

        public string TypeName => "BeamMexedSection";

        public string TypeDescription => "Defines a beam mixed section";

        public GH_MixedSection()
        {
            _concreteSection = null;
            _steelSection = null;
        }

        public GH_MixedSection(GH_MixedSection section)
        {
            _name = section._name;
            _concreteSection = new GH_BeamSection(section._concreteSection);
            _reinforcementCover = section._reinforcementCover;
            _reinforcementArea = section._reinforcementArea;
            _verticalOffset = section._verticalOffset;
            _steelSection = new GH_BeamSection(section._steelSection);
        }


        public bool CastFrom(object source)
        {
            return false;
        }

        public bool CastTo<T>(out T target)
        {
            target = default;
            return false;
        }

        public IGH_Goo Duplicate()
        {
            return new GH_MixedSection(this);
        }

        public IGH_GooProxy EmitProxy()
        {
            return null;
        }

        public bool Read(GH_IReader reader)
        {
            try
            {
                _name = reader.GetString("Name");
                _reinforcementCover = reader.GetDouble("ReinforcementCover");
                _reinforcementArea = reader.GetDouble("ReinforcementArea");
                _verticalOffset = reader.GetDouble("VerticalOffset");
                reader.FindChunk("ConcreteSection");
                _concreteSection = new GH_BeamSection();
                _concreteSection.Read(reader);
                reader.FindChunk("SteelSection");
                _steelSection = new GH_BeamSection();
                _steelSection.Read(reader);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public object ScriptVariable()
        {
            return null;
        }

        public bool Write(GH_IWriter writer)
        {
            try
            {
                writer.SetString("Name", _name);
                writer.SetDouble("ReinforcementCover", _reinforcementCover);
                writer.SetDouble("ReinforcementArea", _reinforcementArea);
                writer.SetDouble("VerticalOffset", _verticalOffset);
                writer.CreateChunk("ConcreteSection");
                _concreteSection.Write(writer);
                writer.CreateChunk("SteelSection");
                _steelSection.Write(writer);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
    }
}
