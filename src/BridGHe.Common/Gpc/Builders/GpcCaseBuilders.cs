using System;
using BridGHe.Common.Gpc.Legacy;

namespace BridGHe.Common.Gpc.Builders
{
    /// <summary>
    /// R6.2 part C (P2N): the GPC builder of the Load Case component: the case record (<see cref="LegacyCaseData"/>) the reader reads
    /// from the former value the component makes (<see cref="LegacyModelImport.CaseDraft"/>).
    /// </summary>
    public static class GpcCaseBuilders
    {
        /// <summary>The former default case name.</summary>
        public const string Dead = "Dead";

        /// <summary>The gravity of the former cases (mm/s², on −Z).</summary>
        public const double Gravity = -9806.65;

        /// <summary>
        /// A case of the Load Case component. «Dead»: the copy of the former default case (gravity on Z with the structural mass, no list of
        /// stages); a non-structural-mass Dead is refused («Invalid nsm», as the component). Another name: a case with an empty list of
        /// stages, without inertia (the defaults of the former value: no inertia, direction X, 0), or with the non-structural mass: gravity on Z
        /// on the non-structural mass only.
        /// </summary>
        public static LegacyCaseData Case(string name, bool nonStructuralMass)
        {
            if (name == Dead)
            {
                if (nonStructuralMass) throw new ArgumentException("Invalid nsm");
                return new LegacyCaseData(Dead, 0, true, false, "Gravity", "Z", Gravity, 0, false);
            }
            return nonStructuralMass ? new LegacyCaseData(name, 0, false, true, "Gravity", "Z", Gravity, 0, true)
                : new LegacyCaseData(name, 0, false, false, "None", "X", 0, 0, true);
        }

        /// <summary>R6.2 part C: the group of the Group component (and of a text cast by the Group parameter): the chain of names of its
        /// parent (from the root; none: a root group) followed by its name, as given (the former group kept the name as given).</summary>
        public static System.Collections.Generic.IReadOnlyList<string> Group(string name, System.Collections.Generic.IReadOnlyList<string> parent)
        {
            var chain = new System.Collections.Generic.List<string>();
            if (parent != null) chain.AddRange(parent);
            chain.Add(name);
            return chain.ToArray();
        }
    }
}
