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
    public class GH_BeamProperty : GH_Goo<object>
    {
        private readonly GH_Material _material;

        public GH_BeamProperty()
        {
        }



        #region Properties

        // R6.5 P0: without a FEM value the property is its GPC datum.
        public override bool IsValid => Gpc?.Property != null;

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
                // P2D: a former payload is read by value when the file is read (no FEM value).
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                return null;
            }
        }

        /// <summary>True when <see cref="Gpc"/> was attached by a GPC builder or read from a file (not converted from the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC datum built on GPC for the current FEM value.</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacyBeamProperty datum)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); _gpcOf = Value; HasAttachedGpc = true;
        }

        private Common.Gpc.Legacy.LegacyBeamProperty _gpc;
        private object _gpcOf;

        /// <summary>R6.1: a copy (<c>Duplicate</c>) keeps the datum attached or read, for its copy of the same value.</summary>
        private void CarryGpcTo(GH_BeamProperty copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
        }

        /// <summary>R6.5 P0 (coordinator, 7/11): a beam property given by its GPC datum alone, with no FEM value: <see cref="Gpc"/> is the datum and
        /// validity, copies, persistence and text read it.</summary>
        public static GH_BeamProperty FromGpc(Common.Gpc.Legacy.LegacyBeamProperty datum)
        {
            var goo = new GH_BeamProperty();
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

        private GH_BeamProperty DuplicateValue()
        {
            return new GH_BeamProperty();
        }

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, Common.Gpc.GpcDraftArchive.Write(Gpc)); return true; } // P2D: the datum alone
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                // P2D: the datum (BGHGPC1), or a former payload / the FEM chunk alone of a development file read by value; never a FEM value.
                var datum = GpcGooPersistence.Read(reader, out var data, out var records, out var femm) switch
                {
                    GpcGooPersistence.Kind.Gpc => Common.Gpc.GpcDraftArchive.ReadBeamProperty(data, records),
                    GpcGooPersistence.Kind.Legacy => Common.Gpc.GpcLegacyPayloads.BeamProperty(femm),
                    _ => null,
                };
                if (datum == null) return false;
                Value = null; _gpc = datum; _gpcOf = null; HasAttachedGpc = true; return true;
            }
            catch (Exception ex) { return GpcGooPersistence.Refused(reader, TypeName, ex); }
        }

        public override string ToString()
        {
            return Gpc?.Property == null ? "Null Beam property" : $"BridGHe beam Property {Gpc.Property.Name}\nGPC datum (no FEM value)";
        }
    }
}
