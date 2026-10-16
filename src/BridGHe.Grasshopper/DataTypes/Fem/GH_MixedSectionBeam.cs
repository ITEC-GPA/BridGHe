using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_MixedSectionBeam : IGH_Goo
    {
        protected GH_Beam _beam;
        protected GH_MixedSection _startSection;
        protected GH_MixedSection _endSection;

        public GH_Beam Beam
        {
            get => _beam;
            set => _beam = value;
        }

        public GH_MixedSection StartSection
        {
            get => _startSection;
            set => _startSection = value;
        }

        public GH_MixedSection EndSection
        {
            get => _endSection;
            set => _endSection = value;
        }

        public bool IsValid => _beam != null && _beam.IsValid && _startSection != null && _startSection.IsValid &&
            _endSection != null && _endSection.IsValid;

        public string IsValidWhyNot
        {
            get
            {
                if (_beam == null || !_beam.IsValid)
                    return "Beam not properly defined";
                if (_startSection == null || !_startSection.IsValid)
                    return "Start section not properly defined";
                if (_endSection == null || !_endSection.IsValid)
                    return "End section not properly defined";
                return "";
            }
        }

        public string TypeName => "MixedSectionBeam";

        public string TypeDescription => "Beam with Mixed Section";

        public GH_MixedSectionBeam()
        {
            _beam = null;
            _startSection = null;
            _endSection = null;
        }

        public GH_MixedSectionBeam(GH_MixedSectionBeam section)
        {
            _beam = new GH_Beam(section._beam);
            _startSection = new GH_MixedSection(section._startSection);
            _endSection = new GH_MixedSection(section._endSection);
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
            return new GH_MixedSectionBeam(this);
        }

        public IGH_GooProxy EmitProxy()
        {
            return null;
        }

        public bool Read(GH_IReader reader)
        {
            try
            {
                reader.FindChunk("Beam");
                _beam = new GH_Beam();
                _beam.Read(reader);
                reader.FindChunk("StartSection");
                _startSection = new GH_MixedSection();
                _startSection.Read(reader);
                reader.FindChunk("EndSection");
                _endSection = new GH_MixedSection();
                _endSection.Read(reader);
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
                writer.CreateChunk("Beam");
                _beam.Write(writer);
                writer.CreateChunk("StartSection");
                _startSection.Write(writer);
                writer.CreateChunk("EndSection");
                _endSection.Write(writer);
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }
    }
}
