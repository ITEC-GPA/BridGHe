using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;
using System.IO;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_Material : GH_Goo<object>
    {
        public GH_Material()
        {
            Value = null; // P2CE phase 2b: no FEM material (a material is its GPC datum)
        }


        // R6.5 P0: without a FEM value the material is its GPC material.
        public override bool IsValid => Gpc != null;

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
                
                return Common.Gpc.GpcMaterials.KindOf(gpc);
            }
        }

        public override IGH_Goo Duplicate()
        {
            // The value is shared (materials are immutable by convention, §1bis), and with it its GPC material.
            return new GH_Material { _gpc = _gpc, _gpcOf = _gpcOf, HasAttachedGpc = HasAttachedGpc, _kind = _kind, _kindOf = _kindOf };
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
                // R6.1: the material attached or read (BGHGPC1, or a former payload read by value: P2D), otherwise the conversion of the FEM
                // value (the FEM value of a component not yet on GPC: removed with the FEM types).
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                return null;
            }
        }

        /// <summary>True when <see cref="Gpc"/> is a material attached by a GPC builder (not the conversion of the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC material built on GPC for the current FEM value (it must describe it: regression <c>gpc-material-kinds</c>).</summary>
        public void AttachGpc(GPC.Model.Materials.Material material, string kind = null)
        {
            _gpc = material ?? throw new ArgumentNullException(nameof(material)); _gpcOf = Value; HasAttachedGpc = true;
            _kind = kind; _kindOf = Value;
        }

        private GPC.Model.Materials.Material _gpc;
        private object _gpcOf;
        // PAR (A10): the declared kind given with the GPC material (null: none) and the FEM value it was given for.
        private string _kind;
        private object _kindOf;

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, Common.Gpc.GpcDraftArchive.Write(Gpc, GpcKind)); return true; } // P2D: the datum alone
            catch (Exception ex) { return GpcGooPersistence.NotWritten(writer, TypeName, ex, () => Gpc != null); } // P2CI (MU-01): said to the writer
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                // P2D: the material (BGHGPC1) with its declared kind, or a former payload / the FEM chunk alone of a development file read by
                // value with the kind it declared (the former MaterialKind); never a FEM value.
                GPC.Model.Materials.Material material; string kind;
                switch (GpcGooPersistence.Read(reader, out var data, out var records, out var femm))
                {
                    case GpcGooPersistence.Kind.Gpc: material = Common.Gpc.GpcDraftArchive.ReadMaterial(data, records, out kind); break;
                    case GpcGooPersistence.Kind.Legacy: (material, kind) = Common.Gpc.GpcLegacyPayloads.MaterialWithKind(femm); break;
                    default: return false;
                }
                Value = null; _gpc = material; _gpcOf = null; HasAttachedGpc = true; _kind = kind; _kindOf = null; return true;
            }
            catch (Exception ex) { return GpcGooPersistence.Refused(reader, TypeName, ex); }
        }

        public override string ToString()
        {
            return Gpc == null ? "Null Material" : $"BridGHe Material {Gpc.Name}\nGPC datum (no FEM value)";
        }
    }
}
