using System;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_LoadCase : GH_Goo<LoadCaseModel>
    {
        // R6.5 P0: without a FEM value the case is its record.
        public override bool IsValid => Value == null ? !string.IsNullOrEmpty(Gpc?.Name) : Value.Name != string.Empty;

        public override string IsValidWhyNot => "Not propely defined";

        public override string TypeName => "LoadCase";

        public override string TypeDescription => "The load case definition";

        public GH_LoadCase()
        {
        }

        public GH_LoadCase(LoadCaseModel lc)
        {
            Value = lc;
        }

        public GH_LoadCase(GH_LoadCase lc)
        {
            Value = lc.Value == null ? null : new LoadCaseModel(lc.Value);
            // R6.5 P1 lane 2a (revisione ChatGPT R65-2): the copy keeps the record attached or given alone and the former payload, as
            // Duplicate does (a record with a list of stages as another instance, CarryGpcTo).
            lc.CarryGpcTo(this);
        }

        /// <summary>
        /// R6.1 (frozen API for R6.2, §1bis): the GPC datum of this GH_LoadCase, the record of the load case (name, temperature, masses, inertia, gravity, stages; the assembly builds the GPC case): the one a GPC builder attached
        /// (<see cref="AttachGpc"/>), the one read from a file (BGHGPC1), the former payload read by value
        /// (<see cref="Common.Gpc.GpcLegacyPayloads"/>), otherwise the conversion of the FEM value, once per value. Immutable by convention.
        /// </summary>
        public Common.Gpc.Legacy.LegacyCaseData Gpc
        {
            get
            {
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { _gpc = Common.Gpc.GpcLegacyPayloads.Case(_legacy); _gpcOf = Value; HasAttachedGpc = false; return _gpc; }
                if (Value == null) return null;
                _gpc = Common.Gpc.GpcModels.CaseDraft(Value); _gpcOf = Value; HasAttachedGpc = false;
                return _gpc;
            }
        }

        /// <summary>True when <see cref="Gpc"/> was attached by a GPC builder or read from a file (not converted from the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC datum built on GPC for the current FEM value (it must describe it: the gates compare the two, <see cref="Common.Gpc.GpcDraftCheck"/>).</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyCaseData datum)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); _gpcOf = Value; HasAttachedGpc = true;
            if (Value != null) { var value = Value; Common.Gpc.GpcDraftCheck.Attached("GH_LoadCase", datum, () => Common.Gpc.GpcModels.CaseDraft(value)); }
        }

        private Common.Gpc.Legacy.LegacyCaseData _gpc;
        private LoadCaseModel _gpcOf;
        private byte[] _legacy;
        private LoadCaseModel _legacyOf;

        /// <summary>R6.1: a copy (<c>Duplicate</c>) keeps the datum attached or read and the former payload, for its copy of the same value.</summary>
        private void CarryGpcTo(GH_LoadCase copy)
        {
            // R6.3: the copy of the value is another case (the former copy made a new list of stages, and a case with a list of stages is
            // compared by reference): the copy carries a copy of the record, the same values in another instance.
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc.HasStageList ? _gpc with { } : _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
            if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { copy._legacy = _legacy; copy._legacyOf = copy.Value; }
        }

        /// <summary>R6.5 P0 (coordinator, 7/11): a load case given by its GPC datum alone, with no FEM value: <see cref="Gpc"/> is the datum and
        /// validity, copies, persistence and text read it.</summary>
        public static GH_LoadCase FromGpc(Common.Gpc.Legacy.LegacyCaseData datum)
        {
            var goo = new GH_LoadCase();
            goo.Value = null;
            goo._gpc = datum ?? throw new System.ArgumentNullException(nameof(datum)); goo._gpcOf = null; goo.HasAttachedGpc = true;
            return goo;
        }

        public override IGH_Goo Duplicate()
        {
            var copy = DuplicateValue();
            CarryGpcTo(copy);
            return copy;
        }

        private GH_LoadCase DuplicateValue()
        {
            return new GH_LoadCase(this);
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                switch (GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case GpcGooPersistence.Kind.Gpc:
                        var datum = Common.Gpc.GpcDraftArchive.ReadCase(records);
                        Value = femm == null ? null : Common.Helpers.ModelArchive.Read<LoadCaseModel>(femm);
                        _gpc = datum; _gpcOf = Value; HasAttachedGpc = true; return true;
                    case GpcGooPersistence.Kind.Legacy:
                        Value = Common.Helpers.ModelArchive.Read<LoadCaseModel>(femm); _legacy = femm; _legacyOf = Value; return true;
                    case GpcGooPersistence.Kind.FemmOnly:
                        Value = Common.Helpers.ModelArchive.Read<LoadCaseModel>(femm); return true;
                    default: return false;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, () => ((byte[])null, Common.Gpc.GpcDraftArchive.Write(Gpc)), Value == null ? null : Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return Gpc == null ? "Null LoadCase property" : $"BridGHe LoadCase: {Gpc.Name}\nGPC datum (no FEM value)";
            else
                return Value.ToString();
        }
    }
}
