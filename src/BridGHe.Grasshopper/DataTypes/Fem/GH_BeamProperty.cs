using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_BeamProperty : GH_Goo<BeamPropertyModel>
    {
        private readonly GH_Material _material;

        public GH_BeamProperty()
        {
        }

        public GH_BeamProperty(BeamPropertyModel beamProperty)
        {
            Value = beamProperty;
            _material = new GH_Material(Value.Material);
        }

        public GH_BeamProperty(BeamPropertyModel beamProperty, GH_Material material)
        {
            Value = beamProperty;
            _material = material;
        }

        #region Properties

        public override bool IsValid => Value == null ? false : Value.IsValid;

        public override string TypeName => "BeamProperty";

        public override string TypeDescription => "Define a beam property";

        #endregion

        /// <summary>
        /// R6.1 (frozen API for R6.2, §1bis): the GPC datum of this GH_BeamProperty, the GPC beam property (definition units) with its data (formulation, angle, native section, signature, section recipe): the one a GPC builder attached
        /// (<see cref="AttachGpc"/>), the one read from a file (BGHGPC1), the former payload read by value
        /// (<see cref="Common.Gpc.GpcLegacyPayloads"/>), otherwise the conversion of the FEM value, once per value. Immutable by convention.
        /// </summary>
        public Common.Gpc.Legacy.LegacyBeamProperty Gpc
        {
            get
            {
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { _gpc = Common.Gpc.GpcLegacyPayloads.BeamProperty(_legacy); _gpcOf = Value; HasAttachedGpc = false; return _gpc; }
                if (Value == null) return null;
                _gpc = Common.Gpc.GpcModels.BeamProperty(Value, Common.Gpc.GpcBeamDrafts.DraftUnits); _gpcOf = Value; HasAttachedGpc = false;
                return _gpc;
            }
        }

        /// <summary>True when <see cref="Gpc"/> was attached by a GPC builder or read from a file (not converted from the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC datum built on GPC for the current FEM value (it must describe it: the gates compare the two, <see cref="Common.Gpc.GpcDraftCheck"/>).</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyBeamProperty datum)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); _gpcOf = Value; HasAttachedGpc = true;
            if (Value != null) { var value = Value; Common.Gpc.GpcDraftCheck.Attached("GH_BeamProperty", datum, () => Common.Gpc.GpcModels.BeamProperty(value, Common.Gpc.GpcBeamDrafts.DraftUnits)); }
        }

        private Common.Gpc.Legacy.LegacyBeamProperty _gpc;
        private BeamPropertyModel _gpcOf;
        private byte[] _legacy;
        private BeamPropertyModel _legacyOf;

        /// <summary>R6.1: a copy (<c>Duplicate</c>) keeps the datum attached or read and the former payload, for its copy of the same value.</summary>
        private void CarryGpcTo(GH_BeamProperty copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
            if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { copy._legacy = _legacy; copy._legacyOf = copy.Value; }
        }

        public override IGH_Goo Duplicate()
        {
            var copy = DuplicateValue();
            CarryGpcTo(copy);
            return copy;
        }

        private GH_BeamProperty DuplicateValue()
        {
            return new GH_BeamProperty(Common.Helpers.ModelArchive.Clone(Value));
        }

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, () => Common.Gpc.GpcDraftArchive.Write(Gpc), Value == null ? null : Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                switch (GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case GpcGooPersistence.Kind.Gpc:
                        var datum = Common.Gpc.GpcDraftArchive.ReadBeamProperty(data, records);
                        Value = femm == null ? null : Common.Helpers.ModelArchive.Read<BeamPropertyModel>(femm);
                        _gpc = datum; _gpcOf = Value; HasAttachedGpc = true; return true;
                    case GpcGooPersistence.Kind.Legacy:
                        Value = Common.Helpers.ModelArchive.Read<BeamPropertyModel>(femm); _legacy = femm; _legacyOf = Value; return true;
                    case GpcGooPersistence.Kind.FemmOnly:
                        Value = Common.Helpers.ModelArchive.Read<BeamPropertyModel>(femm); return true;
                    default: return false;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null Beam property";
            else
                return Value.ToString();
        }
    }
}
