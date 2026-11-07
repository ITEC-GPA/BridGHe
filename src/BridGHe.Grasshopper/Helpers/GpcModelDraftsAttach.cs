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
        /// <summary>The drafts of <paramref name="model"/> when they describe the element lists of its FEM value; null otherwise.</summary>
        internal static Common.Gpc.GpcModelDrafts Of(GH_Model model)
        {
            var value = model?.Value;
            if (value == null) return null;
            var drafts = model.GpcDrafts;
            return drafts != null && drafts.Fits(value.Nodes.Count, value.Beams.Count, value.Plates.Count)
                && value.Elements.Count == value.Nodes.Count + value.Beams.Count + value.Plates.Count ? drafts : null;
        }

        internal static void Attach(GH_Node node, Common.Gpc.Legacy.LegacyNodeDraft draft) =>
            node.AttachGpc(Common.Gpc.GpcDrafts.Reidentify(draft, Common.Gpc.GpcBeamDrafts.Identity(node.Value)));

        internal static void Attach(GH_Beam beam, Common.Gpc.Legacy.LegacyBeamDraft draft) =>
            beam.AttachGpc(Common.Gpc.GpcDrafts.Reidentify(draft, Common.Gpc.GpcBeamDrafts.Identity(beam.Value)));

        internal static void Attach(GH_Plate plate, Common.Gpc.Legacy.LegacyPlateDraft draft) =>
            plate.AttachGpc(Common.Gpc.GpcDrafts.Reidentify(draft, Common.Gpc.GpcBeamDrafts.Identity(plate.Value)));
    }
}
