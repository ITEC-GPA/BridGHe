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

        // R6.5 P0: without a FEM value the material is its GPC material.
        public override bool IsValid => Value == null ? Gpc != null : Value.IsValid;

        public override string TypeName => "Material";

        public override string TypeDescription => "Material definition";

        /// <summary>R6.5 P0 (coordinator, 7/11): a material given by its GPC datum alone, with no FEM value: <see cref="Gpc"/> is the datum and
        /// validity, copies, persistence and text read it.</summary>
        /// <param name="kind">PAR (A10): the kind the material component declares (the former <c>MaterialKind</c> by name: Undefined, Steel,
        /// Concrete, Aluminium, Rebar); null: the kind of the GPC type (<see cref="GpcKind"/>).</param>
        public static GH_Material FromGpc(GPC.Model.Materials.Material datum, string kind = null)
        {
            var goo = new GH_Material();
            goo.Value = null;
            goo._gpc = datum ?? throw new System.ArgumentNullException(nameof(datum)); goo._gpcOf = null; goo.HasAttachedGpc = true;
            goo._kind = kind; goo._kindOf = null;
            return goo;
        }

        /// <summary>
        /// PAR (A10, coordinator 8/11): the kind of the material as the user declared it, a BridGHe datum beside the GPC material (as lane 2c
        /// keeps the kind of a beam property in its record): the kind given by the material component (a Custom material of kind Steel whose
        /// strengths the GPC steel type refuses is the generic GPC material, and still Steel here, as the former value was), else the kind of
        /// the FEM value, else the kind of the GPC type (<see cref="Common.Gpc.GpcMaterials.KindOf"/>). Saved in the records of the datum only
        /// when it is not the kind of the GPC type (a material saved before reads the kind of its GPC type, as before).
        /// </summary>
        public string GpcKind
        {
            get
            {
                var gpc = Gpc;
                if (gpc == null) return null;
                if (_kind != null && ReferenceEquals(_kindOf, Value)) return _kind;
                if (Value != null) return Value.Kind.ToString();
                return Common.Gpc.GpcMaterials.KindOf(gpc);
            }
        }

        public override IGH_Goo Duplicate()
        {
            // The value is shared (materials are immutable by convention, §1bis), and with it its GPC material.
            return new GH_Material(Value) { _gpc = _gpc, _gpcOf = _gpcOf, HasAttachedGpc = HasAttachedGpc, _legacy = _legacy, _legacyOf = _legacyOf, _kind = _kind, _kindOf = _kindOf };
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
        public void AttachGpc(GPC.Model.Materials.Material material, string kind = null)
        {
            _gpc = material ?? throw new ArgumentNullException(nameof(material)); _gpcOf = Value; HasAttachedGpc = true;
            _kind = kind; _kindOf = Value;
            // R6.1 (frozen API for R6.2): the gates compare the attached material with the conversion of the FEM value.
            if (Value != null) { var value = Value; Common.Gpc.GpcDraftCheck.Attached("GH_Material", material, () => Common.Gpc.GpcMaterials.FromFemm(value)); }
        }

        private GPC.Model.Materials.Material _gpc;
        private MaterialModel _gpcOf;
        // R6.1: a former payload (BGHA1) read from a file, converted by value on first use while the FEM value read with it is current.
        private byte[] _legacy;
        private MaterialModel _legacyOf;
        // PAR (A10): the declared kind given with the GPC material (null: none) and the FEM value it was given for.
        private string _kind;
        private MaterialModel _kindOf;

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, () => Common.Gpc.GpcDraftArchive.Write(Gpc, GpcKind), Value == null ? null : Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                switch (GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case GpcGooPersistence.Kind.Gpc:
                        var material = Common.Gpc.GpcDraftArchive.ReadMaterial(data, records, out var kind);
                        Value = femm == null ? null : Common.Helpers.ModelArchive.Read<MaterialModel>(femm);
                        _gpc = material; _gpcOf = Value; HasAttachedGpc = true; _kind = kind; _kindOf = Value; return true;
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
                return Gpc == null ? "Null Material" : $"BridGHe Material {Gpc.Name}\nGPC datum (no FEM value)";
            else
                return Value.ToString();
        }
    }
}
