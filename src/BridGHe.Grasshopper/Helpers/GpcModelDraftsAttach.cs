using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// R6.4 (2), extension (α) of §1bis (coordinator, 7/11): the components that give the elements of a model back (Decompose Model, Super
    /// Element Filter) attach to every element the draft the model carries for it (<see cref="GH_Model.GpcDrafts"/>), with the identity of the
    /// conversion of its FEM value until R6.5 (§1bis, point 4: Build Model and the captures stay equal in the GUIDs too); the DRAFT CHECK
    /// compares it with that conversion on every run of the tests. A model that carries no drafts, or whose drafts do not describe its FEM
    /// element lists (FEM elements merged by ID), leaves the elements with the conversion of their FEM values, as before, until R6.5.
    /// </summary>
    internal static class GpcModelDraftsAttach
    {

        /// <summary>R6.5 P1 lane 3c: the drafts of a model given by its GPC input alone: they keep their own identity (no conversion of a FEM value to
        /// follow). A model without them has no elements to give: the error of the component and no output.</summary>
        internal static Common.Gpc.GpcModelDrafts OfGpcOnly(GH_Model model) =>
            model.GpcDrafts ?? throw GpcOnlyModels.Refused("the model carries no drafts of its elements: connect the output of Build Model or of the component that made the model.");



    }
}
