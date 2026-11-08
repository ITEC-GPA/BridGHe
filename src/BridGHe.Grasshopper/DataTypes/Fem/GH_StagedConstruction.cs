using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;
using System.IO;
using System.Linq;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_StagedConstruction : GH_Goo<StagedConstructionModel>
    {
        // R6.5 P0: without a FEM value the stage is its record.
        public override bool IsValid => Value != null || Gpc != null;

        public override string TypeName => "Staged Construction";

        public override string TypeDescription => "The Staged construction definition";

        public GH_StagedConstruction()
        {
        }

        public GH_StagedConstruction(StagedConstructionModel stagedConstruction)
        {
            Value = stagedConstruction;
        }

        public GH_StagedConstruction(GH_StagedConstruction stagedConstruction)
        {
            Value = stagedConstruction.Value == null ? null : new StagedConstructionModel(stagedConstruction.Value);
            // R6.5 P1 lane 2a (revisione ChatGPT R65-2): the copy keeps the datum attached or given alone and the former payload, as Duplicate does.
            stagedConstruction.CarryGpcTo(this);
        }

        /// <summary>
        /// R6.1 (frozen API for R6.2, §1bis): the GPC datum of this GH_StagedConstruction, the stage by value (name, group full names, morph, rotate, increments): the one a GPC builder attached
        /// (<see cref="AttachGpc"/>), the one read from a file (BGHGPC1), the former payload read by value
        /// (<see cref="Common.Gpc.GpcLegacyPayloads"/>), otherwise the conversion of the FEM value, once per value. Immutable by convention.
        /// </summary>
        public Common.Gpc.Legacy.LegacyStageDraft Gpc
        {
            get
            {
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { _gpc = Common.Gpc.GpcLegacyPayloads.Stage(_legacy); _gpcOf = Value; HasAttachedGpc = false; return _gpc; }
                if (Value == null) return null;
                _gpc = Common.Gpc.GpcModels.StageDraft(Value); _gpcOf = Value; HasAttachedGpc = false;
                return _gpc;
            }
        }

        /// <summary>True when <see cref="Gpc"/> was attached by a GPC builder or read from a file (not converted from the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC datum built on GPC for the current FEM value (it must describe it: the gates compare the two, <see cref="Common.Gpc.GpcDraftCheck"/>).</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyStageDraft datum)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); _gpcOf = Value; HasAttachedGpc = true;
            if (Value != null) { var value = Value; Common.Gpc.GpcDraftCheck.Attached("GH_StagedConstruction", datum, () => Common.Gpc.GpcModels.StageDraft(value)); }
        }

        private Common.Gpc.Legacy.LegacyStageDraft _gpc;
        private StagedConstructionModel _gpcOf;
        private byte[] _legacy;
        private StagedConstructionModel _legacyOf;

        /// <summary>R6.1: a copy (<c>Duplicate</c>) keeps the datum attached or read and the former payload, for its copy of the same value.</summary>
        private void CarryGpcTo(GH_StagedConstruction copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
            if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { copy._legacy = _legacy; copy._legacyOf = copy.Value; }
        }

        /// <summary>R6.5 P0 (coordinator, 7/11): a staged construction given by its GPC datum alone, with no FEM value: <see cref="Gpc"/> is the datum and
        /// validity, copies, persistence and text read it.</summary>
        public static GH_StagedConstruction FromGpc(Common.Gpc.Legacy.LegacyStageDraft datum)
        {
            var goo = new GH_StagedConstruction();
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

        private GH_StagedConstruction DuplicateValue()
        {
            GH_StagedConstruction copy = new GH_StagedConstruction();
            copy.Value = Value == null ? null : new StagedConstructionModel(Value);
            return copy;
        }

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, () => ((byte[])null, Common.Gpc.GpcDraftArchive.Write(Gpc)), Value == null ? null : Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                switch (GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case GpcGooPersistence.Kind.Gpc:
                        var datum = Common.Gpc.GpcDraftArchive.ReadStage(records);
                        Value = femm == null ? null : Common.Helpers.ModelArchive.Read<StagedConstructionModel>(femm);
                        _gpc = datum; _gpcOf = Value; HasAttachedGpc = true; return true;
                    case GpcGooPersistence.Kind.Legacy:
                        Value = Common.Helpers.ModelArchive.Read<StagedConstructionModel>(femm); _legacy = femm; _legacyOf = Value; return true;
                    case GpcGooPersistence.Kind.FemmOnly:
                        Value = Common.Helpers.ModelArchive.Read<StagedConstructionModel>(femm); return true;
                    default: return false;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return Gpc == null ? "Null Staged Construcion" : $"BridGHe Staged Construction {Gpc.Name}\nGroups: {string.Join(", ", Gpc.Groups)}\nGPC datum (no FEM value)";
            else if (Value.Groups.Count() == 0)
                return "Empty Staged Construction";
            else
                return Value.ToString();
        }
    }
}
