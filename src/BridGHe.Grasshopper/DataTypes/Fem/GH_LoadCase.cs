using System;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;


namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_LoadCase : GH_Goo<object>
    {
        // R6.5 P0: without a FEM value the case is its record.
        public override bool IsValid => !string.IsNullOrEmpty(Gpc?.Name);

        public override string IsValidWhyNot => "Not propely defined";

        public override string TypeName => "LoadCase";

        public override string TypeDescription => "The load case definition";

        public GH_LoadCase()
        {
        }


        public GH_LoadCase(GH_LoadCase lc)
        {
            Value = null;
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
                // P2D: a former payload is read by value when the file is read (no FEM value).
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                return null;
            }
        }

        /// <summary>True when <see cref="Gpc"/> was attached by a GPC builder or read from a file (not converted from the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC datum built on GPC for the current FEM value.</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyCaseData datum)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); _gpcOf = Value; HasAttachedGpc = true;
        }

        private Common.Gpc.Legacy.LegacyCaseData _gpc;
        private object _gpcOf;

        /// <summary>R6.1: a copy (<c>Duplicate</c>) keeps the datum attached or read, for its copy of the same value.</summary>
        private void CarryGpcTo(GH_LoadCase copy)
        {
            // R6.3: the copy of the value is another case (the former copy made a new list of stages, and a case with a list of stages is
            // compared by reference): the copy carries a copy of the record, the same values in another instance.
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc.HasStageList ? _gpc with { } : _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
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
                // P2D: the record, or a former payload / the FEM chunk alone of a development file read by value; never a FEM value.
                var datum = GpcGooPersistence.Read(reader, out _, out var records, out var femm) switch
                {
                    GpcGooPersistence.Kind.Gpc => Common.Gpc.GpcDraftArchive.ReadCase(records),
                    GpcGooPersistence.Kind.Legacy => Common.Gpc.GpcLegacyPayloads.Case(femm),
                    _ => null,
                };
                if (datum == null) return false;
                Value = null; _gpc = datum; _gpcOf = null; HasAttachedGpc = true; return true;
            }
            catch (Exception ex) { return GpcGooPersistence.Refused(reader, TypeName, ex); }
        }

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, ((byte[])null, Common.Gpc.GpcDraftArchive.Write(Gpc))); return true; } // P2D: the record alone
            catch (Exception ex) { return GpcGooPersistence.NotWritten(writer, TypeName, ex, () => Gpc != null); } // P2CI (MU-01): said to the writer
        }

        public override string ToString()
        {
            return Gpc == null ? "Null LoadCase property" : $"BridGHe LoadCase: {Gpc.Name}\nGPC datum (no FEM value)";
        }
    }
}
