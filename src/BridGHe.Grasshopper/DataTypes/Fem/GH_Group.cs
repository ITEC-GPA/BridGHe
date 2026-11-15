using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;


namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_Group : GH_Goo<object>
    {
        // R6.5 P0: without a FEM value the group is its chain of names.
        public override bool IsValid => Gpc != null && Gpc.Count > 0 && Gpc[Gpc.Count - 1].Length != 0;

        public override string IsValidWhyNot => "Not propely defined";

        public override string TypeName => "Group";

        public override string TypeDescription => "The group definition";

        public GH_Group()
        {
        }


        public GH_Group(GH_Group grp)
        {
            Value = null;
            // R6.5 P1 lane 2a (revisione ChatGPT R65-2): the copy keeps the datum attached or given alone, as Duplicate does.
            if (grp._gpc != null && grp.HasAttachedGpc && ReferenceEquals(grp._gpcOf, grp.Value)) { _gpc = grp._gpc; _gpcOf = Value; HasAttachedGpc = true; }
        }

        /// <summary>
        /// R6.2 part C (P2N): the GPC datum of this GH_Group, the chain of the names of the group from its root (its parents first; the full
        /// name of the reader is their join with "\", <c>GpcBeamBuilders.FullName</c>), the form in which the builders of the elements take a
        /// group (§1bis, lanes A and B). It is the chain a GPC builder attached (Group, the Group parameter: <see cref="AttachGpc"/>),
        /// otherwise the chain of the FEM value, once per value. Immutable. A GH group is not persisted (as before).
        /// </summary>
        public IReadOnlyList<string> Gpc
        {
            get
            {
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                return null;
            }
        }

        /// <summary>True when <see cref="Gpc"/> was attached by a GPC builder (not converted from the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the chain built on GPC for the current FEM value.</summary>
        public void AttachGpc(IReadOnlyList<string> chain)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            _gpc = chain.ToArray(); _gpcOf = Value; HasAttachedGpc = true;
        }


        private IReadOnlyList<string> _gpc;
        private object _gpcOf;

        /// <summary>R6.5 P0 (coordinator, 7/11): a group given by its GPC datum alone, with no FEM value: <see cref="Gpc"/> is the datum and
        /// validity, copies, persistence and text read it.</summary>
        public static GH_Group FromGpc(IReadOnlyList<string> datum)
        {
            var goo = new GH_Group();
            goo.Value = null;
            goo._gpc = (datum ?? throw new System.ArgumentNullException(nameof(datum))).ToArray(); goo._gpcOf = null; goo.HasAttachedGpc = true;
            return goo;
        }

        public override IGH_Goo Duplicate()
        {
            var copy = new GH_Group(this);
            // R6.1 rule: a copy (Duplicate) keeps the datum attached, for its copy of the same group.
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
            return copy;
        }

        public override string ToString()
        {
            return Gpc == null ? "Null Group property" : $"BridGHe Group: {string.Join("\\", Gpc)}\nGPC datum (no FEM value)";
        }
    }
}
