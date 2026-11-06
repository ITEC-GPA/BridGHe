using System;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// R6.2 lane A (R62A), the dual rule of §1bis until R6.5: a producer component still builds its FEM value and attaches to it the GPC draft
    /// built on GPC from the drafts of its inputs (<c>AttachGpc</c>, compared with the conversion of the FEM value by <see cref="GpcDraftCheck"/>).
    /// <list type="bullet">
    /// <item>A value the reader refuses (a builder throws the <see cref="ArgumentException"/> or <see cref="NotSupportedException"/> of the reader,
    /// or the draft of an input is refused) gets no draft: its <c>Gpc</c> stays the conversion, which refuses it, and Build Model gives the error
    /// as before. Under a check host (<see cref="GpcDraftCheck.Verify"/> set: the Smoke gates) the conversion of the FEM output must refuse it
    /// too, otherwise the component fails (a builder must never refuse what the reader converts).</item>
    /// <item>A decision of a component on its inputs (a filter, an Element ID already given) is taken on the GPC drafts; while the FEM values
    /// exist the decision on the FEM value is the bridge for an input whose draft is refused, and under a check host the two decisions must
    /// agree (<see cref="Decide"/>).</item>
    /// </list>
    /// </summary>
    internal static class GpcBuilderAttach
    {
        internal static void Node(GH_Node target, Func<LegacyNodeDraft> build)
        {
            var draft = Build(build, () => GpcBeamDrafts.FromFemm(target.Value));
            if (draft != null) target.AttachGpc(draft);
        }

        internal static void Plate(GH_Plate target, Func<LegacyPlateDraft> build)
        {
            var draft = Build(build, () => GpcBeamDrafts.FromFemm(target.Value));
            if (draft != null) target.AttachGpc(draft);
        }

        internal static void Beam(GH_Beam target, Func<LegacyBeamDraft> build)
        {
            var draft = Build(build, () => GpcBeamDrafts.FromFemm(target.Value));
            if (draft != null) target.AttachGpc(draft);
        }

        /// <summary>R6.2 part C (P2N): the recipe of a section component, with the same refusal rule.</summary>
        internal static void Section(GH_BeamSection target, Func<LegacySectionRecipe> build)
        {
            var recipe = Build(build, () => GpcModels.SectionDraft(target.Value));
            if (recipe != null) target.AttachGpc(recipe);
        }

        /// <summary>R6.2 part C (P2N): the property of the Beam Property component, with the same refusal rule.</summary>
        internal static void BeamProperty(GH_BeamProperty target, Func<LegacyBeamProperty> build)
        {
            var property = Build(build, () => GpcModels.BeamProperty(target.Value, GpcBeamDrafts.DraftUnits));
            if (property != null) target.AttachGpc(property);
        }

        /// <summary>R6.2 part C (P2N): the property of the Plate Property component, with the same refusal rule.</summary>
        internal static void PlateProperty(GH_PlateProperty target, Func<GPC.Model.ElementProperties.PlateProperty> build)
        {
            var property = Build(build, () => GpcModels.PlatePropertyDraft(target.Value));
            if (property != null) target.AttachGpc(property);
        }

        /// <summary>R6.2 part C (P2N): the case record of the Load Case component, with the same refusal rule.</summary>
        internal static void Case(GH_LoadCase target, Func<LegacyCaseData> build)
        {
            var record = Build(build, () => GpcModels.CaseDraft(target.Value));
            if (record != null) target.AttachGpc(record);
        }

        private static T Build<T>(Func<T> build, Func<object> conversion) where T : class
        {
            try { return build(); }
            catch (Exception refused) when (Refusal(refused))
            {
                if (GpcDraftCheck.Verify != null)
                {
                    bool converted;
                    try { conversion(); converted = true; } catch (Exception x) when (Refusal(x)) { converted = false; }
                    if (converted) throw new InvalidOperationException("GpcBuilderRefusalDifference: the GPC builder refused a value the reader converts (" + refused.Message + ").");
                }
                return null;
            }
        }

        /// <summary>The names of a group from the root to the group (its parents first), as the reader joins them into the full name (a group of
        /// a GH datum: GH_Group has no GPC datum until part C).</summary>
        internal static System.Collections.Generic.IReadOnlyList<string> GroupChain(GH_Group group)
        {
            var names = new System.Collections.Generic.List<string>();
            for (var g = group?.Value; g != null; g = g.Parent) names.Insert(0, g.Name);
            return names;
        }

        /// <summary>The refusals of the reader (and of the builders, which use its pieces and messages).</summary>
        internal static bool Refusal(Exception x) => x is ArgumentException || x is NotSupportedException;

        /// <summary>A decision taken on the GPC draft of an input (<paramref name="gpc"/>, null when the draft of the input is refused: then the
        /// decision on the FEM value, the bridge until R6.5). Under a check host the decision on the FEM value is computed too and must agree.</summary>
        internal static bool Decide(Func<bool> gpc, Func<bool> fem, string what)
        {
            bool? onGpc;
            try { onGpc = gpc(); } catch (Exception x) when (Refusal(x)) { onGpc = null; }
            if (onGpc == null) return fem();
            if (GpcDraftCheck.Verify != null && fem() != onGpc.Value)
                throw new InvalidOperationException("GpcDecisionDifference: " + what + " on the GPC draft differs from the decision on the FEM value.");
            return onGpc.Value;
        }
    }
}
