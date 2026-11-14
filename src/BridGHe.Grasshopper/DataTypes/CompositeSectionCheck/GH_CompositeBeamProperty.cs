using BridGHe.Grasshopper.DataTypes.Fem;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;

namespace BridGHe.Grasshopper.DataTypes
{
    public class GH_CompositeBeamProperty : GH_Goo<object>
    {
        public GH_CompositeBeamProperty() { }
        private readonly Fem.GH_Material _material;



        #region Properties

        // R6.5 P0: without a FEM value the composite property is its GPC definition.
        public override bool IsValid => Gpc != null;

        public override string TypeName => "BeamProperty";

        public override string TypeDescription => "Define a beam property";

        /// <summary>F6 (L11): the GPC contract of this composite property (<see cref="GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition"/>,
        /// the section of the checks with the rules of Super Element Check, <see cref="Common.Gpc.GpcCompositeSections"/>): the datum this type
        /// carries once the FEM value goes (chain E); meanwhile built from the FEM value, once per value.</summary>
        public GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Definition => Gpc;

        #endregion

        /// <summary>
        /// R6.1 (frozen API for R6.2, §1bis): the GPC datum of this GH_CompositeBeamProperty, the composite section definition of the Checker: the one a GPC builder attached
        /// (<see cref="AttachGpc"/>), the one read from a file (BGHGPC1), the former payload read by value
        /// (<see cref="Common.Gpc.GpcLegacyPayloads"/>), otherwise the conversion of the FEM value, once per value. Immutable by convention.
        /// </summary>
        public GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Gpc
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
        public void AttachGpc(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition datum)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); _gpcOf = Value; HasAttachedGpc = true;
        }

        private GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition _gpc;
        private object _gpcOf;

        /// <summary>R6.1: a copy (<c>Duplicate</c>) keeps the datum attached or read, for its copy of the same value.</summary>
        private void CarryGpcTo(GH_CompositeBeamProperty copy)
        {
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
        }

        /// <summary>R6.5 P0 (coordinator, 7/11): a composite property given by its GPC definition alone, with no FEM value: <see cref="Gpc"/>
        /// is the definition and validity, copies, persistence and text read it.</summary>
        public static GH_CompositeBeamProperty FromGpc(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition definition)
        {
            var goo = new GH_CompositeBeamProperty();
            goo.Value = null;
            goo._gpc = definition ?? throw new System.ArgumentNullException(nameof(definition)); goo._gpcOf = null; goo.HasAttachedGpc = true;
            return goo;
        }

        public override IGH_Goo Duplicate()
        {
            var copy = DuplicateValue();
            CarryGpcTo(copy);
            return copy;
        }

        private GH_CompositeBeamProperty DuplicateValue()
        {
            return new GH_CompositeBeamProperty();
        }

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, (Common.Gpc.GpcDraftArchive.Write(Gpc), (byte[])null)); return true; } // P2D: the definition alone
            catch (Exception ex) { return GpcGooPersistence.NotWritten(writer, TypeName, ex, () => Gpc != null); } // P2CI (MU-01): said to the writer
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                // P2D: the definition, or a former payload / the FEM chunk alone of a development file read by value; never a FEM value.
                var datum = GpcGooPersistence.Read(reader, out var data, out _, out var femm) switch
                {
                    GpcGooPersistence.Kind.Gpc => Common.Gpc.GpcDraftArchive.ReadComposite(data),
                    GpcGooPersistence.Kind.Legacy => Common.Gpc.GpcLegacyPayloads.Composite(femm),
                    _ => null,
                };
                if (datum == null) return false;
                Value = null; _gpc = datum; _gpcOf = null; HasAttachedGpc = true; return true;
            }
            catch (Exception ex) { return GpcGooPersistence.Refused(reader, TypeName, ex); }
        }

        public override string ToString()
        {
            return Gpc == null ? "Null Beam property" : $"BridGHe composite beam Property {Gpc.Name}\nGPC datum (no FEM value)";
        }
    }
}
