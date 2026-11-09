using System;
using System.Collections;
using System.Linq;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// R6.5 P1, lane 3 (coordinator decision 5, 8/11): the rule of the components that read models, composite beams and workflow objects.
    /// <list type="bullet">
    /// <item><b>GPC-only input gives GPC-only output; a refusal gives an error and no output.</b> An input given by its GPC datum alone (a
    /// <see cref="GH_Model"/> made by <see cref="GH_Model.FromGpc"/>, a <see cref="GH_CompositeBeam"/> made by
    /// <see cref="GH_CompositeBeam.FromGpc"/>, an element, a property or a datum of lanes 1 and 2 without FEM value) is read by its GPC data
    /// only: no FEM value is made from a null <c>Value</c>, and a reading that exists only on the FEM data (the FEM results of a composite
    /// beam, the FEM fallbacks of the checks) is an error on the component with no output (decision 2 of passo 73).</item>
    /// <item>With the inputs of today (FEM value present) nothing changes: the FEM path keeps its readings (the dual rule of R6.2, until P2).</item>
    /// </list>
    /// Lane 3 only (lanes 1 and 2 have <see cref="GpcOnlyElements"/> and <see cref="GpcOnlyProperties"/>). Removed in P2 with the FEM path.
    /// </summary>
    internal static class GpcOnlyModels
    {
        /// <summary>A model given by its GPC input alone (no FEM value): its GPC input, state and results are those of the input.</summary>
        internal static bool WithoutFem(GH_Model model) => model != null && model.Value == null && model.HasAttachedInput;

        /// <summary>A composite beam given by its GPC datum alone (no FEM value).</summary>
        internal static bool WithoutFem(GH_CompositeBeam beam) => beam != null && beam.Value == null && beam.Gpc != null;

        /// <summary>True when an input of a lane-3 component is given by its GPC datum alone: a model, a composite beam, a workflow object
        /// whose actions have no FEM source, or an input of the kinds of lanes 1 and 2 without FEM value (a goo or a list of them).</summary>
        internal static bool Any(params object[] inputs) => inputs.Any(One);

        private static bool One(object input) => input switch
        {
            null => false,
            GH_Model m => WithoutFem(m),
            GH_CompositeBeam b => WithoutFem(b),
            GH_Node n => n.Value == null && n.Gpc != null,
            GH_Beam b => b.Value == null && b.Gpc != null,
            GH_Plate p => p.Value == null && p.Gpc != null,
            string _ => false,
            IEnumerable list => list.Cast<object>().Any(One),
            _ => GpcOnlyProperties.WithoutFem(input),
        };

        /// <summary>L3d: the GPC input of a model for a model transformer on the GPC-only path: with the drafts the model carries, also when they are
        /// attached apart (Build BridGHe Model): the drafts of the output and A09 read them.</summary>
        internal static Common.Gpc.GpcModels.Conversion InputOf(GH_Model model)
        {
            var input = model.GpcInput ?? throw Refused("the model has no GPC input.");
            var drafts = model.GpcDrafts;
            return input.Drafts != null || drafts == null ? input : input.WithDrafts(drafts);
        }

        /// <summary>L3d: the output of a model transformer that keeps the elements of its source one to one (Attach Moving Paths, Bridge Phase Model) on
        /// the GPC-only path: the GPC model alone, with the GH GUIDs of the elements of the source (the former path: <c>GH_Model.FromModel(value, source)</c>).</summary>
        internal static GH_Model SameElements(Common.Gpc.GpcModels.Conversion output, GH_Model source, Common.Gpc.GpcModels.Conversion input)
        {
            var model = GH_Model.FromGpc(output);
            if (input.Drafts != null && output.Drafts != null) model.AttachElementGuids(source.ElementGuidsOf(input.Drafts));
            return model;
        }

        /// <summary>The error of a reading that exists only on the FEM data, on the GPC-only path (no output).</summary>
        internal static Exception Refused(string reading) => new ArgumentException(RefusedPrefix + reading);

        internal const string RefusedPrefix = "GPC-only input: ";

        /// <summary>The plots, Bridge Girder Results: the stations and combinations of composite beams given by their GPC data alone come from
        /// the GPC bridge model of Build BridGHe Model (the FEM results of a composite beam have no GPC equivalent).</summary>
        internal const string RequiresBridgeModel = "the stations and combinations come from the GPC bridge model of Build BridGHe Model: connect its Model output (and the composite beams made from its beams).";

        /// <summary>Bridge Detail Stations: the reactions of the phases on the GPC-only path come from the GPC bridge model and the GPC sources of
        /// the phases (the FEM phase sources have no GPC equivalent).</summary>
        internal const string RequiresBridgeReactions = "the support reactions come from the GPC bridge model of Build BridGHe Model and the analysed GPC sources of the phases.";
    }
}
