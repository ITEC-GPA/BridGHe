using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_PlateProperty : GH_Goo<object>
    {
        GH_Material _material;

        // Grasshopper instantiates persistent generic data before calling Read.
        public GH_PlateProperty() { }



        #region Properties

        // R6.5 P0: without a FEM value the property is its GPC property.
        public override bool IsValid => Gpc != null;

        public override string TypeName => "PlateProperty";

        public override string TypeDescription => "Define a plate property";

        #endregion

        /// <summary>
        /// R6.1 (frozen API for R6.2, §1bis): the GPC datum of this GH_PlateProperty, the GPC plate property (elastic or membrane, definition units): the one a GPC builder attached
        /// (<see cref="AttachGpc"/>), the one read from a file (BGHGPC1), the former payload read by value
        /// (<see cref="Common.Gpc.GpcLegacyPayloads"/>), otherwise the conversion of the FEM value, once per value. Immutable by convention.
        /// </summary>
        public GPC.Model.ElementProperties.PlateProperty Gpc
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

        /// <summary>L2C (lane 2c): the bending thickness of a membrane property (N-mm, as the thicknesses of <see cref="Gpc"/>), a BridGHe
        /// datum beside the GPC membrane, which has none (the former property held it; Bridge Deck Mesh checks it against the slab, as before):
        /// the one attached or read with <see cref="Gpc"/>, otherwise that of the former payload or of the FEM value; 0 for another type. Set
        /// with a datum alone (<see cref="FromGpc"/>; the lane test gives it so).</summary>
        public double GpcMembraneBending
        {
            get
            {
                if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) return _bending;
                return _bending;
            }
            set => _bending = value;
        }

        /// <summary>Attaches the GPC datum built on GPC for the current FEM value.</summary>
        public void AttachGpc(GPC.Model.ElementProperties.PlateProperty datum) => AttachGpc(datum, 0);

        /// <summary>L2C: <see cref="AttachGpc(GPC.Model.ElementProperties.PlateProperty)"/> with the bending thickness of a membrane
        /// (<see cref="GpcMembraneBending"/>).</summary>
        public void AttachGpc(GPC.Model.ElementProperties.PlateProperty datum, double membraneBending)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); _gpcOf = Value; HasAttachedGpc = true; _bending = membraneBending;
        }

        private double _bending;
        private GPC.Model.ElementProperties.PlateProperty _gpc;
        private object _gpcOf;

        /// <summary>R6.1: a copy (<c>Duplicate</c>) keeps the datum attached or read, for its copy of the same value.</summary>
        private void CarryGpcTo(GH_PlateProperty copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; copy._bending = _bending; }
        }

        /// <summary>R6.5 P0 (coordinator, 7/11): a plate property given by its GPC datum alone, with no FEM value: <see cref="Gpc"/> is the datum and
        /// validity, copies, persistence and text read it.</summary>
        public static GH_PlateProperty FromGpc(GPC.Model.ElementProperties.PlateProperty datum)
        {
            var goo = new GH_PlateProperty();
            goo.Value = null;
            goo._gpc = datum ?? throw new System.ArgumentNullException(nameof(datum)); goo._gpcOf = null; goo.HasAttachedGpc = true;
            return goo;
        }

        /// <summary>L2C: this plate property given by its datum alone with the bending thickness of a membrane (<see cref="GpcMembraneBending"/>).</summary>
        public GH_PlateProperty WithMembraneBending(double membraneBending)
        {
            _bending = membraneBending;
            return this;
        }

        public override IGH_Goo Duplicate()
        {
            var copy = DuplicateValue();
            CarryGpcTo(copy);
            return copy;
        }

        private GH_PlateProperty DuplicateValue()
        {
            return new GH_PlateProperty();
        }

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, Common.Gpc.GpcDraftArchive.Write(Gpc, GpcMembraneBending)); return true; } // P2D: the datum alone
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                // P2D: the datum with the bending thickness of a membrane, or a former payload / the FEM chunk alone of a development file read
                // by value with its bending thickness; never a FEM value.
                GPC.Model.ElementProperties.PlateProperty datum; double bending;
                switch (GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case GpcGooPersistence.Kind.Gpc: datum = Common.Gpc.GpcDraftArchive.ReadPlateProperty(data, records, out bending); break;
                    case GpcGooPersistence.Kind.Legacy:
                        datum = Common.Gpc.GpcLegacyPayloads.PlateProperty(femm); bending = Common.Gpc.GpcLegacyPayloads.PlatePropertyMembraneBending(femm); break;
                    default: return false;
                }
                if (datum == null) return false;
                Value = null; _gpc = datum; _gpcOf = null; HasAttachedGpc = true; _bending = bending; return true;
            }
            catch (Exception ex) { return GpcGooPersistence.Refused(reader, TypeName, ex); }
        }

        public override string ToString()
        {
            return Gpc == null ? "Null Plate property" : $"BridGHe plate Property {Gpc.Name}\nGPC datum (no FEM value)";
        }
    }
}
