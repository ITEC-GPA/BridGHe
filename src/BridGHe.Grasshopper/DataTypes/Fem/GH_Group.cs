using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_Group : GH_Goo<GroupModel>
    {
        public override bool IsValid => Value == null ? false : Value.Name != string.Empty;

        public override string IsValidWhyNot => "Not propely defined";

        public override string TypeName => "Group";

        public override string TypeDescription => "The group definition";

        public GH_Group()
        {
        }

        public GH_Group(GroupModel group)
        {
            Value = group;
        }

        public GH_Group(GH_Group grp)
        {
            Value = new GroupModel(grp.Value);
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
                if (Value == null) return null;
                _gpc = Chain(Value); _gpcOf = Value; HasAttachedGpc = false;
                return _gpc;
            }
        }

        /// <summary>True when <see cref="Gpc"/> was attached by a GPC builder (not converted from the FEM value).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the chain built on GPC for the current FEM value (it must describe it: the gates compare the two).</summary>
        public void AttachGpc(IReadOnlyList<string> chain)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            _gpc = chain.ToArray(); _gpcOf = Value; HasAttachedGpc = true;
            if (Value != null) { var value = Value; Common.Gpc.GpcDraftCheck.Attached("GH_Group", new Common.Gpc.Legacy.LegacyGroupChain(_gpc), () => new Common.Gpc.Legacy.LegacyGroupChain(Chain(value))); }
        }

        /// <summary>The chain of the names of a former group from its root (the conversion of the FEM value).</summary>
        internal static IReadOnlyList<string> Chain(GroupModel group)
        {
            var names = new List<string>();
            for (var g = group; g != null; g = g.Parent) names.Insert(0, g.Name);
            return names.ToArray();
        }

        private IReadOnlyList<string> _gpc;
        private GroupModel _gpcOf;

        public override IGH_Goo Duplicate()
        {
            var copy = new GH_Group(this);
            // R6.1 rule: a copy (Duplicate) keeps the datum attached, for its copy of the same group.
            if (_gpc != null && HasAttachedGpc && ReferenceEquals(_gpcOf, Value)) { copy._gpc = _gpc; copy._gpcOf = copy.Value; copy.HasAttachedGpc = true; }
            return copy;
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null Group property";
            else
                return Value.ToString();
        }
    }
}
