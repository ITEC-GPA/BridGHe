using System;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// R6.2 lane A (R62A), the dual rule of §1bis: a producer component that still builds its FEM value attaches to it the GPC draft built on
    /// GPC from the drafts of its inputs (<c>AttachGpc</c>).
    /// <list type="bullet">
    /// <item>A value the reader refuses (a builder throws the <see cref="ArgumentException"/> or <see cref="NotSupportedException"/> of the reader,
    /// or the draft of an input is refused) gets no draft: its <c>Gpc</c> stays the conversion, which refuses it, and Build Model gives the error
    /// as before.</item>
    /// <item>A decision of a component on its inputs (a filter, an Element ID already given) is taken on the GPC drafts; while the FEM values
    /// exist the decision on the FEM value is the bridge for an input whose draft is refused (<see cref="Decide"/>).</item>
    /// </list>
    /// P2D (P3): the comparison with the conversion of the FEM value under a check host (DRAFT CHECK) is removed; the FEM values and this
    /// bridge go with the FEM types (P2-taglio).
    /// </summary>
    internal static class GpcBuilderAttach
    {
        internal static void Node(GH_Node target, Func<LegacyNodeDraft> build)
        {
            var draft = Build(build);
            if (draft != null) target.AttachGpc(draft);
        }

        internal static void Plate(GH_Plate target, Func<LegacyPlateDraft> build)
        {
            var draft = Build(build);
            if (draft != null) target.AttachGpc(draft);
        }

        internal static void Beam(GH_Beam target, Func<LegacyBeamDraft> build)
        {
            var draft = Build(build);
            if (draft != null) target.AttachGpc(draft);
        }

        /// <summary>R6.2 part C (P2N): the recipe of a section component, with the same refusal rule.</summary>
        internal static void Section(GH_BeamSection target, Func<LegacySectionRecipe> build)
        {
            var recipe = Build(build);
            if (recipe != null) target.AttachGpc(recipe);
        }

        /// <summary>R6.2 part C (P2N): the property of the Beam Property component, with the same refusal rule.</summary>
        internal static void BeamProperty(GH_BeamProperty target, Func<LegacyBeamProperty> build)
        {
            var property = Build(build);
            if (property != null) target.AttachGpc(property);
        }

        /// <summary>R6.2 part C (P2N): the property of the Plate Property component, with the same refusal rule.</summary>
        /// <param name="membraneBending">L2C: the bending thickness of a membrane (<see cref="GH_PlateProperty.GpcMembraneBending"/>).</param>
        internal static void PlateProperty(GH_PlateProperty target, Func<GPC.Model.ElementProperties.PlateProperty> build, double membraneBending = 0)
        {
            var property = Build(build);
            if (property != null) target.AttachGpc(property, membraneBending);
        }

        /// <summary>R6.2 part C (P2N): the case record of the Load Case component, with the same refusal rule.</summary>
        internal static void Case(GH_LoadCase target, Func<LegacyCaseData> build)
        {
            var record = Build(build);
            if (record != null) target.AttachGpc(record);
        }

        /// <summary>R6.2 part C (P2N): the composite section of Composite Beam Property, with the same refusal rule; the definition attached
        /// (null when refused), for the homogenised properties built from it.</summary>
        internal static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition Composite(DataTypes.GH_CompositeBeamProperty target,
            Func<GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition> build)
        {
            var definition = Build(build);
            if (definition != null) target.AttachGpc(definition);
            return definition;
        }

        /// <summary>R6.3 (P2N): the combination of Load Combination and of the Load Combinations parameter, with the same refusal rule.</summary>
        internal static void Combination(GH_LoadCombination target, Func<LegacyCombinationDraft> build)
        {
            var combination = Build(build);
            if (combination != null) target.AttachGpc(combination);
        }

        /// <summary>R6.3 (P2N): the stage of Staged Construction, with the same refusal rule.</summary>
        internal static void Stage(GH_StagedConstruction target, Func<LegacyStageDraft> build)
        {
            var stage = Build(build);
            if (stage != null) target.AttachGpc(stage);
        }

        /// <summary>R6.2 part C (P2N): the chain of names of the group of Group and of the Group parameter (from the root).</summary>
        internal static void Group(GH_Group target, Func<System.Collections.Generic.IReadOnlyList<string>> build)
        {
            var chain = Build(build);
            if (chain != null) target.AttachGpc(chain);
        }

        /// <summary>The datum of a builder, null when it refuses its data (the refusals of the reader).</summary>
        private static T Build<T>(Func<T> build) where T : class
        {
            try { return build(); }
            catch (Exception refused) when (Refusal(refused)) { return null; }
        }

        /// <summary>The names of a group from the root to the group (its parents first), as the reader joins them into the full name: the GPC
        /// datum of the GH group (R6.2 part C, <see cref="GH_Group.Gpc"/>); no group: an empty chain.</summary>
        internal static System.Collections.Generic.IReadOnlyList<string> GroupChain(GH_Group group) =>
            group?.Gpc ?? (System.Collections.Generic.IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>The refusals of the reader (and of the builders, which use its pieces and messages).</summary>
        internal static bool Refusal(Exception x) => x is ArgumentException || x is NotSupportedException;

        /// <summary>A decision taken on the GPC draft of an input (<paramref name="gpc"/>, null when the draft of the input is refused: then the
        /// decision on the FEM value, the bridge while FEM values exist). R6.5 P1 lane 1a: <paramref name="fem"/> null for an input without a FEM
        /// value (given by its GPC draft alone): the decision on the draft is the only one (a refusal of the draft is rethrown, there is no FEM
        /// value to fall back on). P2D: the comparison of the two decisions under a check host is removed.</summary>
        internal static bool Decide(Func<bool> gpc, Func<bool> fem, string what)
        {
            if (fem == null) return gpc();
            bool? onGpc;
            try { onGpc = gpc(); } catch (Exception x) when (Refusal(x)) { onGpc = null; }
            return onGpc ?? fem();
        }
    }
}
