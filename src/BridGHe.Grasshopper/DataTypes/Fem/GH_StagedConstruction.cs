using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;
using System.IO;
using System.Linq;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_StagedConstruction : GH_Goo<object>
    {
        // R6.5 P0: without a FEM value the stage is its record.
        public override bool IsValid => Value != null || Gpc != null;

        public override string TypeName => "Staged Construction";

        public override string TypeDescription => "The Staged construction definition";

        public GH_StagedConstruction()
        {
        }


        public GH_StagedConstruction(GH_StagedConstruction stagedConstruction)
        {
            Value = null;
            // R6.5 P1 lane 2a (revisione ChatGPT R65-2): the copy keeps the datum attached or given alone and the former payload, as Duplicate does.
            stagedConstruction.CarryGpcTo(this);
        }

        /// <summary>
        /// R6.1 (frozen API for R6.2, §1bis): the GPC datum of this GH_StagedConstruction, the stage by value (name, group full names, morph, rotate, increments): the one a GPC builder attached
        /// (<see cref="AttachGpc"/>), the one read from a file (BGHGPC1), the former payload read by value
        /// (<see cref="Common.Gpc.GpcLegacyPayloads"/>), with no runtime conversion from a removed model type. Immutable by convention.
        /// </summary>
        public Common.Gpc.Legacy.LegacyStageDraft Gpc
        {
            get
            {
                // P2D: a former payload is read by value when the file is read (no FEM value).
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                return null;
            }
        }

        /// <summary>True when <see cref="Gpc"/> was attached by a GPC builder or read from a file (including legacy payloads read by value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC datum to this wrapper.</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyStageDraft datum)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); _gpcOf = Value; HasAttachedGpc = true;
        }

        private Common.Gpc.Legacy.LegacyStageDraft _gpc;
        private object _gpcOf;

        /// <summary>R6.1: a copy (<c>Duplicate</c>) keeps the datum attached or read, for its copy of the same value.</summary>
        private void CarryGpcTo(GH_StagedConstruction copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
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
            copy.Value = null;
            return copy;
        }

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, ((byte[])null, Common.Gpc.GpcDraftArchive.Write(Gpc))); return true; } // P2D: the record alone
            catch (Exception ex) { return GpcGooPersistence.NotWritten(writer, TypeName, ex, () => Gpc != null); } // P2CI (MU-01): said to the writer
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                // P2D: the record, or a former payload / the FEM chunk alone of a development file read by value; never a FEM value.
                var datum = GpcGooPersistence.Read(reader, out _, out var records, out var femm) switch
                {
                    GpcGooPersistence.Kind.Gpc => Common.Gpc.GpcDraftArchive.ReadStage(records),
                    GpcGooPersistence.Kind.Legacy => Common.Gpc.GpcLegacyPayloads.Stage(femm),
                    _ => null,
                };
                if (datum == null) return false;
                Value = null; _gpc = datum; _gpcOf = null; HasAttachedGpc = true; return true;
            }
            catch (Exception ex) { return GpcGooPersistence.Refused(reader, TypeName, ex); }
        }

        public override string ToString()
        {
            return Gpc == null ? "Null Staged Construcion" : $"BridGHe Staged Construction {Gpc.Name}\nGroups: {string.Join(", ", Gpc.Groups)}\nGPC datum (no FEM value)";
        }
    }
}
