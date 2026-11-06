using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;
using System.IO;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_Material : GH_Goo<MaterialModel>
    {
        public GH_Material()
        {
            Value = new MaterialModel();
        }

        public GH_Material(MaterialModel material)
        {
            Value = material;
        }

        public override bool IsValid => Value == null ? false : Value.IsValid;

        public override string TypeName => "Material";

        public override string TypeDescription => "Material definition";

        public override IGH_Goo Duplicate()
        {
            // The value is shared (materials are immutable by convention, §1bis), and with it its GPC material.
            return new GH_Material(Value) { _gpc = _gpc, _gpcOf = _gpcOf, HasAttachedGpc = HasAttachedGpc, _legacy = _legacy, _legacyOf = _legacyOf };
        }

        /// <summary>
        /// M1 (coordinator, 6/11; §1bis, row <c>GH_Material</c>): the GPC material of this datum, by kind
        /// (<see cref="Common.Gpc.GpcMaterials.FromFemm"/>: steel and B450C <c>SteelMaterialEN1992</c>, concrete <c>ConcreteMaterialEN1992</c>,
        /// aluminium <c>AluminiumMaterialEN1999</c>, custom without a kind and table the generic material with its analysis data): the one a GPC
        /// builder attached to this FEM value (<see cref="AttachGpc"/>), otherwise the conversion of the FEM value, once per value. N-mm (MPa) in
        /// every unit system. Immutable by convention (shared by the copies): whoever wants another material makes a new one. The analysis material
        /// of a property is <see cref="Common.Gpc.GpcMaterials.Analysis"/> of it, the check materials <c>GpcMaterials.StructuralSteel</c>,
        /// <c>Reinforcement</c> and <c>Concrete</c>.
        /// </summary>
        public GPC.Model.Materials.Material Gpc
        {
            get
            {
                // R6.1: the material attached or read (BGHGPC1), the former payload read by value (BGHA1), otherwise the conversion of the FEM value.
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                if (_legacy != null && ReferenceEquals(_legacyOf, Value)) { _gpc = Common.Gpc.GpcLegacyPayloads.Material(_legacy); _gpcOf = Value; HasAttachedGpc = false; return _gpc; }
                if (Value == null) return null;
                _gpc = Common.Gpc.GpcMaterials.FromFemm(Value); _gpcOf = Value; HasAttachedGpc = false;
                return _gpc;
            }
        }

        /// <summary>True when <see cref="Gpc"/> is a material attached by a GPC builder (not the conversion of the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC material built on GPC for the current FEM value (it must describe it: regression <c>gpc-material-kinds</c>).</summary>
        public void AttachGpc(GPC.Model.Materials.Material material)
        {
            _gpc = material ?? throw new ArgumentNullException(nameof(material)); _gpcOf = Value; HasAttachedGpc = true;
            // R6.1 (frozen API for R6.2): the gates compare the attached material with the conversion of the FEM value.
            if (Value != null) { var value = Value; Common.Gpc.GpcDraftCheck.Attached("GH_Material", material, () => Common.Gpc.GpcMaterials.FromFemm(value)); }
        }

        private GPC.Model.Materials.Material _gpc;
        private MaterialModel _gpcOf;
        // R6.1: a former payload (BGHA1) read from a file, converted by value on first use while the FEM value read with it is current.
        private byte[] _legacy;
        private MaterialModel _legacyOf;

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, () => (Common.Gpc.GpcDraftArchive.Write(Gpc), (byte[])null), Value == null ? null : Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                switch (GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case GpcGooPersistence.Kind.Gpc:
                        var material = Common.Gpc.GpcDraftArchive.ReadMaterial(data);
                        Value = femm == null ? null : Common.Helpers.ModelArchive.Read<MaterialModel>(femm);
                        _gpc = material; _gpcOf = Value; HasAttachedGpc = true; return true;
                    case GpcGooPersistence.Kind.Legacy:
                        Value = Common.Helpers.ModelArchive.Read<MaterialModel>(femm); _legacy = femm; _legacyOf = Value; return true;
                    case GpcGooPersistence.Kind.FemmOnly:
                        Value = Common.Helpers.ModelArchive.Read<MaterialModel>(femm); return true;
                    default: return false;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null Material";
            else
                return Value.ToString();
        }
    }
}
