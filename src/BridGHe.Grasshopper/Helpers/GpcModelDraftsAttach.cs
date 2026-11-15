using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// Attaches the element drafts and their stable identities from a GPC model to its element outputs.
    /// A missing or inconsistent draft collection is refused; no removed FEM value is used as a fallback.
    /// </summary>
    internal static class GpcModelDraftsAttach
    {

        /// <summary>R6.5 P1 lane 3c: the drafts of a model given by its GPC input alone: they keep their own identity (no conversion of a FEM value to
        /// follow). A model without them has no elements to give: the error of the component and no output.</summary>
        internal static Common.Gpc.GpcModelDrafts OfGpcOnly(GH_Model model) =>
            model.GpcDrafts ?? throw GpcOnlyModels.Refused("the model carries no drafts of its elements: connect the output of Build Model or of the component that made the model.");



    }
}
