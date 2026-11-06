using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// R6.3: a combination of the Load Combination component: its cases in input order with their factors (missing factors 0); a case
        /// given twice adds its factors (the former dictionary keyed by the case value: the same case is the same record instance, or for a
        /// case without a list of stages, «Dead», the same definition).
        /// </summary>
        public static LegacyCombinationDraft Combination(string name, IReadOnlyList<LegacyCaseData> cases, IReadOnlyList<double> values)
        {
            var factors = new List<(LegacyCaseData Case, double Factor)>();
            for (int i = 0; i < cases.Count; i++)
            {
                double value = i < values.Count ? values[i] : 0;
                int k = factors.FindIndex(f => SameKey(f.Case, cases[i]));
                if (k >= 0) factors[k] = (factors[k].Case, factors[k].Factor + value);
                else factors.Add((cases[i], value));
            }
            return new LegacyCombinationDraft(name, factors.ToArray());
        }

        /// <summary>
        /// R6.3: a combination of the Load Combinations parameter after Build Model or Staged Construction updated its cases (the former
        /// <c>UpdateLoadCases</c>, which changed the dictionary of the combination in place): the factors of cases no longer present (by name)
        /// removed, the cases not yet present (by name) added with factor 1, in the order of the former dictionary (an added entry takes the
        /// place of the entry removed last, then the end). Null when nothing changes.
        /// </summary>
        public static LegacyCombinationDraft UpdatedCombination(LegacyCombinationDraft combination, IReadOnlyList<LegacyCaseData> cases)
        {
            if (combination == null) throw new ArgumentNullException(nameof(combination));
            var slots = combination.Factors.Select(f => ((LegacyCaseData Case, double Factor)?)f).ToList();
            var free = new Stack<int>();
            bool changed = false;
            for (int i = 0; i < slots.Count; i++)
                if (!cases.Any(c => c.Name == slots[i].Value.Case.Name)) { slots[i] = null; free.Push(i); changed = true; }
            foreach (var c in cases)
            {
                if (slots.Any(s => s != null && s.Value.Case.Name == c.Name)) continue;
                if (free.Count > 0) slots[free.Pop()] = (c, 1); else slots.Add((c, 1));
                changed = true;
            }
            return changed ? new LegacyCombinationDraft(combination.Name, slots.Where(s => s != null).Select(s => s.Value).ToArray()) : null;
        }

        private static bool SameKey(LegacyCaseData a, LegacyCaseData b) => ReferenceEquals(a, b) || !a.HasStageList && !b.HasStageList && a == b;

        /// <summary>
        /// R6.3: the stage of Staged Construction: its name, the full names of its groups (chains of names from the root), Morph, Rotate and one
        /// increment per combination, in order.
        /// </summary>
        public static LegacyStageDraft Stage(string name, IReadOnlyList<IReadOnlyList<string>> groups, IReadOnlyList<LegacyCombinationDraft> combinations, bool morph, bool rotate) =>
            new LegacyStageDraft(name, groups.Select(GpcBeamBuilders.FullName).ToArray(), morph, rotate, combinations.ToArray());

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
